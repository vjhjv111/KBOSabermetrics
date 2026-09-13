using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace NaverSabermetrics.Web;

/// <summary>One durable league per anonymous owner, in a DB separate from warehouse records.</summary>
public sealed partial class DiamondSeasonService
{
    public string DatabasePath { get; }
    private readonly DiamondData _data;
    private readonly DiamondRosterService _roster;
    private readonly Func<long> _clock;
    private readonly Func<double> _random;
    private readonly Func<string, int, DiamondSeasonRosterOverride?>? _rosterOverride;
    private readonly Action<string, DiamondSeasonGame>? _gameCompleted;
    public DiamondSeasonService(string stateDirectory, string dataDirectory, DiamondRosterService roster,
        Func<long>? clock = null, Func<double>? random = null,
        Func<string, int, DiamondSeasonRosterOverride?>? rosterOverride = null, Action<string, DiamondSeasonGame>? gameCompleted = null)
    {
        _data = new DiamondData(dataDirectory); _roster = roster;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); _random = random ?? DiamondEngine.CryptoRandom;
        _rosterOverride = rosterOverride; _gameCompleted = gameCompleted;
        Directory.CreateDirectory(stateDirectory); DatabasePath = Path.Combine(Path.GetFullPath(stateDirectory), "diamond_career.db");
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS DiamondSeasons(Owner TEXT PRIMARY KEY,State TEXT NOT NULL,Version INTEGER NOT NULL,UpdatedAt INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS DiamondSeasonRequests(Owner TEXT NOT NULL,RequestId TEXT NOT NULL,Hash TEXT NOT NULL,CreatedAt INTEGER NOT NULL,PRIMARY KEY(Owner,RequestId));
            CREATE TABLE IF NOT EXISTS DiamondSeasonCreationLimits(Key TEXT PRIMARY KEY,Count INTEGER NOT NULL,ExpiresAt INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_DiamondSeasonCreationLimits_Expires ON DiamondSeasonCreationLimits(ExpiresAt);
            CREATE TABLE IF NOT EXISTS DiamondSeasonMatches(Code TEXT PRIMARY KEY,Owner TEXT NOT NULL,State TEXT NOT NULL,Version INTEGER NOT NULL,ExpiresAt INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_DiamondSeasonMatches_Expires ON DiamondSeasonMatches(ExpiresAt);
            CREATE TABLE IF NOT EXISTS DiamondSeasonMatchRequests(Owner TEXT NOT NULL,RequestId TEXT NOT NULL,Hash TEXT NOT NULL,Code TEXT NOT NULL,PRIMARY KEY(Owner,RequestId));
            CREATE TABLE IF NOT EXISTS DiamondSeasonMatchLimits(Key TEXT PRIMARY KEY,Count INTEGER NOT NULL,ExpiresAt INTEGER NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }
    public long Now() => _clock();
    private SqliteConnection Open()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, DefaultTimeout = 20 }.ToString());
        c.Open(); return c;
    }
    private static DiamondSeasonSave? Load(SqliteConnection c, SqliteTransaction tx, string actor)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT State,Version FROM DiamondSeasons WHERE Owner=$actor"; cmd.Parameters.AddWithValue("$actor", actor);
        using var r = cmd.ExecuteReader(); if (!r.Read()) return null;
        var save = JsonSerializer.Deserialize<DiamondSeasonSave>(r.GetString(0), DiamondJson.Options) ?? throw new InvalidDataException("리그 저장이 비어 있습니다.");
        save.Version = r.GetInt64(1); return save;
    }
    private void Store(SqliteConnection c, SqliteTransaction tx, string actor, DiamondSeasonSave save, bool increment = true)
    {
        if (increment) save.Version++;
        save.UpdatedAt = Now();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO DiamondSeasons(Owner,State,Version,UpdatedAt) VALUES($actor,$state,$version,$now) ON CONFLICT(Owner) DO UPDATE SET State=excluded.State,Version=excluded.Version,UpdatedAt=excluded.UpdatedAt";
        cmd.Parameters.AddWithValue("$actor", actor); cmd.Parameters.AddWithValue("$state", JsonSerializer.Serialize(save, DiamondJson.Options));
        cmd.Parameters.AddWithValue("$version", save.Version); cmd.Parameters.AddWithValue("$now", save.UpdatedAt); cmd.ExecuteNonQuery();
    }
    public DiamondSeasonResponse Get(string actor, CancellationToken token = default)
    {
        CheckActor(actor); token.ThrowIfCancellationRequested(); DrainRewards(actor);
        DiamondSeasonSave? save;
        using (var c = Open())
        using (var tx = c.BeginTransaction())
        {
            save = Load(c, tx, actor);
            if (save?.Game is { Complete: false } && Tick(save, actor, Now()))
            { CompleteDayIfNeeded(save, actor, token); Store(c, tx, actor, save); }
            tx.Commit();
        }
        DrainRewards(actor);
        return View(save, actor);
    }
    public DiamondSeasonResponse Post(JsonElement body, string actor, CancellationToken token = default, string? ip = null)
    {
        CheckActor(actor); token.ThrowIfCancellationRequested();
        if (body.ValueKind != JsonValueKind.Object) throw new DiamondInputError("잘못된 리그 요청입니다.");
        var op = Text(body, "op"); var request = Text(body, "requestId");
        if (request is null || !Regex.IsMatch(request, "^[A-Za-z0-9_-]{8,80}$", RegexOptions.CultureInvariant))
            throw new DiamondInputError("요청 식별자를 확인해 주세요.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.GetRawText())));
        DrainRewards(actor);
        // Retries remain usable even when the record warehouse becomes unavailable after creation.
        using (var read = Open())
        using (var rt = read.BeginTransaction(deferred: true))
        {
            using var prior = read.CreateCommand(); prior.Transaction = rt;
            prior.CommandText = "SELECT Hash FROM DiamondSeasonRequests WHERE Owner=$actor AND RequestId=$request";
            prior.Parameters.AddWithValue("$actor", actor); prior.Parameters.AddWithValue("$request", request);
            if (prior.ExecuteScalar() is string previous)
            {
                if (previous != hash) throw new DiamondInputError("같은 요청 식별자에 다른 입력을 보낼 수 없습니다.", 409);
                var existing = Load(read, rt, actor); rt.Commit(); return View(existing, actor);
            }
            rt.Commit();
        }
        // Read external career data before acquiring this database's write transaction.
        DiamondRoster? roster = op == "create" ? _roster.Get(Integer(body, "season"), token) : null;
        DiamondPitchingProfile? interactiveProfile = null;
        if (op == "ready")
        {
            using var read = Open(); using var rt = read.BeginTransaction(deferred: true);
            var preview = Load(read, rt, actor); rt.Commit();
            if (preview?.Game is { Complete: false } game && game.Duel.Pitch is not { Resolved: false })
            {
                AutoRelieve(preview, game, false);
                var id = DiamondSeasonRules.Pitcher(game);
                if (game.Duel.PitchingProfile is not { } prior || prior.PitcherId != id)
                {
                    try
                    {
                        if (!id.Contains(":career_", StringComparison.Ordinal))
                        {
                            var observed = _roster.SelectPitching(preview.Season, id, token);
                            if (observed.Revision == preview.Revision) interactiveProfile = observed;
                        }
                    }
                    catch (DiamondInputError e) when (e.Status is 400 or 503) { /* Continue from the saved player snapshot. */ }
                    interactiveProfile ??= new() { Season = preview.Season, PitcherId = id, Revision = preview.Revision,
                        Source = "default", SampleNote = "저장된 시즌 성적·구종·구속을 사용합니다. 같은 기준일의 투구 위치 분포가 없어 코스는 게임 모델로 생성합니다." };
                }
            }
        }
        DiamondSeasonRosterOverride? custom = null;
        if (_rosterOverride != null && op is "create" or "start-game" or "sim-game" or "sim-day" or "sim-half" or "sim-to-player")
        {
            int? year = roster?.Season;
            if (!year.HasValue) { using var read = Open(); using var rt = read.BeginTransaction(deferred: true); year = Load(read, rt, actor)?.Season; rt.Commit(); }
            if (year.HasValue) custom = _rosterOverride(actor, year.Value);
        }
        DiamondSeasonSave save;
        using (var c = Open())
        using (var tx = c.BeginTransaction())
        {
            using var prior = c.CreateCommand(); prior.Transaction = tx;
            prior.CommandText = "SELECT Hash FROM DiamondSeasonRequests WHERE Owner=$actor AND RequestId=$request";
            prior.Parameters.AddWithValue("$actor", actor); prior.Parameters.AddWithValue("$request", request);
            var previous = prior.ExecuteScalar() as string;
            var loaded = Load(c, tx, actor);
            if (previous != null)
            {
                if (previous != hash) throw new DiamondInputError("같은 요청 식별자에 다른 입력을 보낼 수 없습니다.", 409);
                tx.Commit(); return View(loaded, actor);
            }
            if (op == "create")
            {
                if (loaded != null) throw new DiamondInputError("저장된 리그가 있습니다. 이어서 진행해 주세요.", 409);
                save = Create(body, roster!, custom, actor);
                LimitSeasonCreation(c, tx, ip ?? "unknown");
            }
            else
            {
                save = loaded ?? throw new DiamondInputError("먼저 리그를 시작해 주세요.", 404);
                var version = Integer(body, "version");
                if (version != save.Version) throw new DiamondInputError("다른 입력으로 리그 상태가 바뀌었습니다. 최신 상태에서 다시 시도해 주세요.", 409);
                if (interactiveProfile != null && save.Game != null) save.Game.Duel.PitchingProfile = interactiveProfile;
                Apply(save, body, op, actor, custom, token);
            }
            token.ThrowIfCancellationRequested(); Store(c, tx, actor, save);
            using var remember = c.CreateCommand(); remember.Transaction = tx;
            remember.CommandText = "INSERT INTO DiamondSeasonRequests(Owner,RequestId,Hash,CreatedAt) VALUES($actor,$request,$hash,$now)";
            remember.Parameters.AddWithValue("$actor", actor); remember.Parameters.AddWithValue("$request", request);
            remember.Parameters.AddWithValue("$hash", hash); remember.Parameters.AddWithValue("$now", Now()); remember.ExecuteNonQuery();
            tx.Commit();
        }
        DrainRewards(actor); return View(save, actor);
    }
    private DiamondSeasonSave Create(JsonElement body, DiamondRoster roster, DiamondSeasonRosterOverride? custom, string actor, string? opponentTeam = null)
    {
        var team = Text(body, "team") ?? ""; var pace = Text(body, "pace") ?? "practice";
        if (pace is not ("practice" or "real" or "full")) throw new DiamondInputError("플레이 속도를 선택해 주세요.");
        var repeats = body.TryGetProperty("seriesPerPair", out _) ? Integer(body, "seriesPerPair") : 8;
        if (repeats is < 1 or > 8) throw new DiamondInputError("맞대결 반복 수는 1~8입니다.");
        var save = new DiamondSeasonSave { Id = "S" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)), Team = team, Season = roster.Season,
            AsOf = roster.AsOf, Revision = roster.Revision, Pace = pace, SeriesPerPair = repeats, TotalDays = repeats * 18, CreatedAt = Now(), UpdatedAt = Now() };
        foreach (var t in roster.Teams)
        {
            if (opponentTeam != null && t.Code != team && t.Code != opponentTeam) continue;
            var batters = roster.Batters.Where(x => x.Team == t.Code).OrderByDescending(x => x.Pa).ToList();
            var pitchers = roster.Pitchers.Where(x => x.Team == t.Code).OrderByDescending(x => x.Outs).ThenByDescending(x => x.Tbf).ToList();
            if (batters.Count < 9 || pitchers.Count < 1) continue;
            save.Teams.Add(new() { Code = t.Code, Name = t.Name, Batters = Clone(batters), Pitchers = Clone(pitchers), Lineup = batters.Take(9).Select(x => x.Id).ToList() });
        }
        if (opponentTeam == null && save.Teams.Count != 10) throw new DiamondInputError("이 시즌에는 9명 타순을 구성할 수 있는 10개 팀의 기록이 필요합니다.", 409);
        if (opponentTeam != null && (save.Teams.All(x => x.Code != team) || save.Teams.All(x => x.Code != opponentTeam)))
            throw new DiamondInputError("친선 경기의 두 팀 모두 타자 9명과 투수 1명 이상의 기록이 필요합니다.", 409);
        if (save.Teams.All(x => x.Code != team)) throw new DiamondInputError("리그 팀을 선택해 주세요.");
        ApplyCustom(save, custom);
        // A friendly fixture is assembled by its caller; historical seasons can have fewer than ten teams.
        if (opponentTeam == null) save.Schedule = DiamondSeasonRules.Schedule(save.Id, 1, save.Teams.Select(x => x.Code).ToArray(), repeats);
        return save;
    }
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, DiamondJson.Options), DiamondJson.Options)!;
    private void LimitSeasonCreation(SqliteConnection c, SqliteTransaction tx, string ip)
    {
        var now = Now(); var hour = now / 3600000; var day = now / 86400000;
        var ipHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip)));
        // Cookie rotation must not bypass admission limits for permanently stored leagues.
        foreach (var (key, limit, expires) in new[]
        {
            ($"ip-hour:{ipHash}:{hour}", 20, (hour + 1) * 3600000),
            ($"global-hour:{hour}", 200, (hour + 1) * 3600000),
            ($"ip-day:{ipHash}:{day}", 100, (day + 1) * 86400000),
            ($"global-day:{day}", 2000, (day + 1) * 86400000)
        })
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO DiamondSeasonCreationLimits(Key,Count,ExpiresAt) VALUES($key,1,$expires) ON CONFLICT(Key) DO UPDATE SET Count=Count+1 RETURNING Count";
            cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$expires", expires);
            if (Convert.ToInt32(cmd.ExecuteScalar()) > limit)
                throw new DiamondInputError("새 리그 생성 요청이 많습니다. 저장된 리그를 이어 하거나 잠시 후 다시 시도해 주세요.", 429);
        }
        using var cleanup = c.CreateCommand(); cleanup.Transaction = tx;
        cleanup.CommandText = "DELETE FROM DiamondSeasonCreationLimits WHERE Key IN (SELECT Key FROM DiamondSeasonCreationLimits WHERE ExpiresAt<=$now LIMIT 1000)";
        cleanup.Parameters.AddWithValue("$now", now); cleanup.ExecuteNonQuery();
    }
    private static void CheckActor(string actor) { if (string.IsNullOrWhiteSpace(actor) || actor.Length > 128) throw new DiamondInputError("리그 소유자를 확인해 주세요.", 403); }
    private static string? Text(JsonElement body, string name) => body.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static double Number(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetDouble(out var v) || !double.IsFinite(v)) throw new DiamondInputError($"{name} 입력을 확인해 주세요.");
        return v;
    }
    private static int Integer(JsonElement body, string name)
    { var n = Number(body, name); if (n < 0 || n > int.MaxValue || n != Math.Truncate(n)) throw new DiamondInputError($"{name} 입력을 확인해 주세요."); return (int)n; }
    private static DiamondVec Aim(JsonElement body)
    {
        if (!body.TryGetProperty("aim", out var aim) || aim.ValueKind != JsonValueKind.Object) throw new DiamondInputError("조준 위치를 확인해 주세요.");
        var x = Number(aim, "x"); var y = Number(aim, "y");
        if (Math.Abs(x) > 2 || Math.Abs(y) > 2) throw new DiamondInputError("조준 범위를 벗어났습니다."); return new(x, y);
    }
    private DiamondSeasonResponse View(DiamondSeasonSave? s, string actor)
    {
        if (s == null) return new(null, null, Now());
        DiamondView? action = null;
        if (s.Game is { } g)
        {
            var d = g.Duel; var role = DiamondSeasonRules.BattingTeam(g) == s.Team ? "batter" : "pitcher";
            // Controls follow the new half immediately. The resolved pitch still retains its own roster for animation.
            action = DiamondEngine.View(d, actor, Now()) with { Role = role, Done = g.Complete, Winner = null,
                Round = g.PlateAppearances, Score = g.HomeTeam == s.Team ? g.HomeRuns : g.AwayRuns, Waiting = false };
        }
        return new(new(s.Id, s.Version, s.Team, s.Season, s.SeasonNumber, s.AsOf, s.Revision, s.Pace, s.Day, s.TotalDays,
            s.SeriesPerPair, s.Complete, s.Teams, s.Schedule, DiamondSeasonRules.Standings(s), s.PlayerStats, s.Game,
            s.PreviousSeasons, s.CreatedAt, s.UpdatedAt, s.Appearances), action, Now());
    }
    private void DrainRewards(string actor)
    {
        if (_gameCompleted == null) return;
        List<DiamondSeasonGame> pending;
        using (var c = Open()) using (var tx = c.BeginTransaction(deferred: true))
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "SELECT json_extract(State,'$.pendingRewards') FROM DiamondSeasons WHERE Owner=$actor";
            cmd.Parameters.AddWithValue("$actor", actor);
            var json = cmd.ExecuteScalar() as string;
            // Polling usually has no rewards. Avoid materializing the full roster, schedule and game twice.
            pending = json is null or "[]" ? [] : JsonSerializer.Deserialize<List<DiamondSeasonGame>>(json, DiamondJson.Options) ?? [];
            tx.Commit();
        }
        if (pending.Count == 0) return;
        foreach (var game in pending) _gameCompleted(actor, game);
        var delivered = pending.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        using var write = Open(); using var transaction = write.BeginTransaction();
        var save = Load(write, transaction, actor);
        if (save != null && save.PendingRewards.RemoveAll(x => delivered.Contains(x.Id)) > 0) Store(write, transaction, actor, save, increment: false);
        transaction.Commit();
    }
}
