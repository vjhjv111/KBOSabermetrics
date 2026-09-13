using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace NaverSabermetrics.Web;

/// <summary>Custom player progression. Source KBO records are never modified.</summary>
public sealed class DiamondCareerService
{
    public string DatabasePath { get; }
    private readonly Func<long> _clock;
    private static readonly string[] Teams = ["HH", "HT", "KT", "LG", "LT", "NC", "OB", "SK", "SS", "WO"];
    public DiamondCareerService(string stateDirectory, Func<long>? clock = null)
    {
        Directory.CreateDirectory(stateDirectory);
        DatabasePath = Path.Combine(Path.GetFullPath(stateDirectory), "diamond_career.db");
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS DiamondCareerPlayers(Owner TEXT PRIMARY KEY,State TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS DiamondCareerRequests(Owner TEXT NOT NULL,RequestId TEXT NOT NULL,Hash TEXT NOT NULL,PRIMARY KEY(Owner,RequestId));
            CREATE TABLE IF NOT EXISTS DiamondCareerRewards(Owner TEXT NOT NULL,GameId TEXT NOT NULL,PRIMARY KEY(Owner,GameId));
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, DefaultTimeout = 20 }.ToString());
        c.Open(); return c;
    }
    private static void Actor(string actor)
    { if (!Regex.IsMatch(actor, "^[a-f0-9]{64}$")) throw new DiamondInputError("선수 저장 세션을 확인해 주세요.", 403); }
    private static DiamondCareerPlayer? Load(SqliteConnection c, SqliteTransaction tx, string actor)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT State FROM DiamondCareerPlayers WHERE Owner=$owner"; cmd.Parameters.AddWithValue("$owner", actor);
        var json = cmd.ExecuteScalar() as string;
        return json == null ? null : JsonSerializer.Deserialize<DiamondCareerPlayer>(json, DiamondJson.Options)!;
    }
    private void Store(SqliteConnection c, SqliteTransaction tx, string actor, DiamondCareerPlayer player)
    {
        player.Version++; player.UpdatedAt = _clock();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO DiamondCareerPlayers(Owner,State) VALUES($owner,$state) ON CONFLICT(Owner) DO UPDATE SET State=excluded.State";
        cmd.Parameters.AddWithValue("$owner", actor); cmd.Parameters.AddWithValue("$state", JsonSerializer.Serialize(player, DiamondJson.Options)); cmd.ExecuteNonQuery();
    }
    public DiamondCareerResponse Get(string actor)
    {
        Actor(actor); using var c = Open(); using var tx = c.BeginTransaction(deferred: true);
        var player = Load(c, tx, actor); tx.Commit(); return new(player);
    }
    public DiamondCareerResponse Post(JsonElement body, string actor)
    {
        Actor(actor);
        if (body.ValueKind != JsonValueKind.Object) throw new DiamondInputError("선수 요청 형식을 확인해 주세요.");
        var request = Text(body, "requestId");
        if (!Regex.IsMatch(request, "^[A-Za-z0-9_-]{8,80}$")) throw new DiamondInputError("요청 식별자를 확인해 주세요.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.GetRawText())));
        using var c = Open(); using var tx = c.BeginTransaction();
        var player = Load(c, tx, actor);
        using (var prior = c.CreateCommand())
        {
            prior.Transaction = tx; prior.CommandText = "SELECT Hash FROM DiamondCareerRequests WHERE Owner=$owner AND RequestId=$request";
            prior.Parameters.AddWithValue("$owner", actor); prior.Parameters.AddWithValue("$request", request);
            if (prior.ExecuteScalar() is string previous)
            {
                if (previous != hash) throw new DiamondInputError("같은 요청의 내용이 달라졌습니다.", 409);
                tx.Commit(); return new(player);
            }
        }
        var op = Text(body, "op");
        if (op == "create")
        {
            if (player != null) throw new DiamondInputError("이미 만든 선수가 있습니다. 계속 육성해 주세요.", 409);
            player = Create(body);
        }
        else
        {
            if (player == null) throw new DiamondInputError("먼저 선수를 만들어 주세요.", 404);
            if (!body.TryGetProperty("version", out var version) || !version.TryGetInt64(out var expected) || expected != player.Version)
                throw new DiamondInputError("선수 정보가 갱신되었습니다. 다시 확인해 주세요.", 409);
            if (op == "appearance")
            {
                player.Name = Name(Text(body, "name"));
                player.Stats.Name = player.Name;
                player.Appearance = Appearance(body.GetProperty("appearance"));
            }
            else if (op == "train")
            {
                var skill = Text(body, "skill");
                var property = typeof(DiamondGameRatings).GetProperties().SingleOrDefault(p => string.Equals(p.Name, skill, StringComparison.OrdinalIgnoreCase));
                if (property == null) throw new DiamondInputError("훈련할 능력치를 골라 주세요.");
                var pitchingSkill = property.Name is "Velocity" or "Control" or "Stamina";
                if ((player.Position == "P") != pitchingSkill)
                    throw new DiamondInputError(player.Position == "P" ? "투수는 구속·제구·체력만 훈련할 수 있습니다." : "타자는 컨택·파워·선구안·주루·수비만 훈련할 수 있습니다.");
                var value = (int)property.GetValue(player.Ratings)!;
                if (value >= 95) throw new DiamondInputError("이 능력치는 최대치입니다.");
                var cost = value >= 80 ? 2 : 1;
                if (player.TrainingPoints < cost) throw new DiamondInputError("훈련 포인트가 부족합니다. 경기를 완료해 포인트를 얻으세요.");
                player.TrainingPoints -= cost; property.SetValue(player.Ratings, Math.Min(95, value + 2));
                player.TrainingLog.Insert(0, $"{skill}: {value} → {Math.Min(95, value + 2)}");
                if (player.TrainingLog.Count > 20) player.TrainingLog.RemoveAt(20);
            }
            else throw new DiamondInputError("지원하지 않는 선수 요청입니다.");
        }
        Store(c, tx, actor, player);
        using var insert = c.CreateCommand(); insert.Transaction = tx;
        insert.CommandText = "INSERT INTO DiamondCareerRequests(Owner,RequestId,Hash) VALUES($owner,$request,$hash)";
        insert.Parameters.AddWithValue("$owner", actor); insert.Parameters.AddWithValue("$request", request); insert.Parameters.AddWithValue("$hash", hash); insert.ExecuteNonQuery();
        tx.Commit(); return new(player);
    }
    private DiamondCareerPlayer Create(JsonElement body)
    {
        var p = new DiamondCareerPlayer
        {
            Id = "career_" + Guid.NewGuid().ToString("N"), Name = Name(Text(body, "name")),
            Team = Choice(Text(body, "team"), Teams), Position = Choice(Text(body, "position"), ["P", "C", "1B", "2B", "3B", "SS", "LF", "CF", "RF", "DH"]),
            Bats = Choice(Text(body, "bats"), ["L", "R", "S"]), Throws = Choice(Text(body, "throws"), ["L", "R"]),
            Delivery = Choice(Text(body, "delivery"), ["overhand", "sidearm", "underhand"]),
            Archetype = Choice(Text(body, "archetype"), ["balanced", "slugger", "contact", "speed", "fireballer", "control"]),
            Appearance = Appearance(body.GetProperty("appearance")), CreatedAt = _clock()
        };
        switch (p.Archetype)
        {
            case "slugger": p.Ratings.Power = 74; p.Ratings.Contact = 52; p.Ratings.Speed = 48; break;
            case "contact": p.Ratings.Contact = 74; p.Ratings.Discipline = 66; p.Ratings.Power = 48; break;
            case "speed": p.Ratings.Speed = 78; p.Ratings.Fielding = 68; p.Ratings.Power = 46; break;
            case "fireballer": p.Ratings.Velocity = 76; p.Ratings.Control = 48; p.Ratings.Stamina = 66; break;
            case "control": p.Ratings.Control = 76; p.Ratings.Stamina = 66; p.Ratings.Velocity = 48; break;
        }
        p.Stats.PlayerId = p.Id; p.Stats.Name = p.Name; p.Stats.Team = p.Team;
        return p;
    }
    public DiamondSeasonRosterOverride? GetRosterOverride(string actor, int season)
    {
        var p = Get(actor).Player; if (p == null) return null;
        var r = p.Ratings; var id = $"{season}:{p.Id}";
        var profile = new DiamondProfile { Bats = p.Bats, Throws = p.Throws, Delivery = p.Delivery, HeightCm = p.Appearance.HeightCm, Position = p.Position, BodyType = p.Appearance.BodyType,
            BatsThrows = (p.Throws == "L" ? "좌투" : "우투") + (p.Bats == "S" ? "양타" : p.Bats == "L" ? "좌타" : "우타"), GameRatings = r };
        const string note = "커스텀 선수 · 훈련 능력치를 게임용 성적으로 환산합니다.";
        var discipline = new DiamondDiscipline { ZoneSwingRate = .64 + r.Contact * .001, ChaseRate = .48 - r.Discipline * .0038,
            ZoneContactRate = .61 + r.Contact * .0037, OutZoneContactRate = .34 + r.Contact * .004, ZonePitchRate = .38 + r.Control * .003 };
        if (p.Position == "P")
        {
            var velo = 116 + r.Velocity * .44; var era = 7.2 - r.Control * .036 - r.Velocity * .018;
            var pitcher = new DiamondPitcher { Id = id, PlayerId = p.Id, Name = p.Name, Team = p.Team, Profile = profile, Discipline = discipline,
                Tbf = 600, Bb = 600 * (100 - r.Control) / 400d, So = 600 * (.08 + r.Velocity * .0027), Outs = 450, Er = era * 150 / 9,
                Era = era, Whip = 1.9 - r.Control * .009, SampleNote = note, ArsenalSource = "default",
                Arsenal = [new("fastball", velo, 48), new("slider", velo - 12, 25), new("changeup", velo - 15, 17), new("curve", velo - 24, 10)] };
            return new(null, pitcher, p.Id, p.Appearance);
        }
        var avg = .14 + r.Contact * .0022;
        var batter = new DiamondBatter { Id = id, PlayerId = p.Id, Name = p.Name, Team = p.Team, Profile = profile, Discipline = discipline,
            Pa = 600, Ab = 530, H = Math.Round(avg * 530), Hr = Math.Round(Math.Max(2, (r.Power - 25) * .57)), So = 600 * (100 - r.Contact) / 170d,
            Avg = avg, Slg = avg + (r.Power - 28) / 200d, Ops = avg * 2 + (r.Power - 28) / 200d + .04 + r.Discipline * .001, SampleNote = note };
        return new(batter, null, p.Id, p.Appearance);
    }
    public void ApplyGame(string actor, DiamondSeasonGame game)
    {
        Actor(actor); if (!game.Complete) return;
        using var c = Open(); using var tx = c.BeginTransaction(); var player = Load(c, tx, actor);
        if (player == null) { tx.Commit(); return; }
        using var once = c.CreateCommand(); once.Transaction = tx;
        once.CommandText = "INSERT OR IGNORE INTO DiamondCareerRewards(Owner,GameId) VALUES($owner,$game)";
        once.Parameters.AddWithValue("$owner", actor); once.Parameters.AddWithValue("$game", game.Id);
        if (once.ExecuteNonQuery() == 0) { tx.Commit(); return; }
        var s = game.PlayerStats.Values.SingleOrDefault(x => x.PlayerId.EndsWith(":" + player.Id, StringComparison.Ordinal));
        // A defender or pinch runner can appear without completing a plate appearance.
        // Numeric fallback keeps previously saved games without Participants compatible.
        if (s == null || !game.Participants.Contains(s.PlayerId) && s.PA + s.PitchCount + s.R == 0) { tx.Commit(); return; }
        var xp = 25 + s.PA * 3 + s.H * 12 + s.HR * 20 + s.RBI * 5 + s.BB * 4 + s.OutsPitched * 2 + s.Strikeouts * 4;
        player.Xp += xp; player.Games++; player.TrainingPoints++; var levels = 0;
        while (player.Xp >= player.NextLevelXp && player.Level < 100)
        { player.Xp -= player.NextLevelXp; player.Level++; levels++; player.TrainingPoints += 4; }
        foreach (var property in typeof(DiamondSeasonPlayerStats).GetProperties().Where(p => p.PropertyType == typeof(int)))
            property.SetValue(player.Stats, (int)property.GetValue(player.Stats)! + (int)property.GetValue(s)!);
        var summary = player.Position == "P" ? $"{s.OutsPitched / 3}.{s.OutsPitched % 3}이닝 · {s.Strikeouts}K · {s.RunsAllowed}실점" : $"{s.AB}타수 {s.H}안타 · {s.HR}홈런 · {s.RBI}타점";
        player.Rewards.Insert(0, new(game.Id, xp, levels, summary, _clock()));
        if (player.Rewards.Count > 20) player.Rewards.RemoveAt(20);
        Store(c, tx, actor, player); tx.Commit();
    }
    private static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";
    private static string Choice(string value, string[] choices) => choices.Contains(value) ? value : throw new DiamondInputError("선택한 선수 설정을 확인해 주세요.");
    private static string Name(string name) => name.Length is >= 1 and <= 20 && !name.Any(char.IsControl) ? name : throw new DiamondInputError("선수 이름은 1~20자로 입력해 주세요.");
    private static DiamondCareerAppearance Appearance(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new DiamondInputError("선수 외형을 확인해 주세요.");
        DiamondCareerAppearance p;
        try { p = body.Deserialize<DiamondCareerAppearance>(DiamondJson.Options)!; } catch (JsonException) { throw new DiamondInputError("선수 외형 형식을 확인해 주세요."); }
        Choice(p.BodyType, ["lean", "athletic", "power"]); Choice(p.HairStyle, ["short", "buzz", "flow", "bald"]);
        if (p.HeightCm is < 155 or > 215 || !Regex.IsMatch(p.JerseyNumber ?? "", "^[0-9]{1,2}$")) throw new DiamondInputError("신장은 155~215cm, 등번호는 0~99로 설정해 주세요.");
        foreach (var color in new[] { p.SkinTone, p.HairColor, p.GloveColor, p.CleatColor, p.BatColor, p.EquipmentColor })
            if (color == null || !Regex.IsMatch(color, "^#[a-fA-F0-9]{6}$")) throw new DiamondInputError("색상을 확인해 주세요.");
        return p;
    }
}
