using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace NaverSabermetrics.Web;

/// <summary>Persistent anonymous matches, separate from both the baseball DB and query quotas.</summary>
public sealed class DiamondGameService
{
    public string DatabasePath { get; }
    public DiamondData Data { get; }
    public DiamondEngine Engine { get; }
    private readonly Func<long> _clock;
    private readonly Func<double>? _random;
    private readonly DiamondRosterService? _roster;
    public DiamondGameService(string stateDirectory, string dataDirectory, Func<long>? clock = null, Func<double>? random = null,
        DiamondRosterService? roster = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _random = random; _roster = roster;
        Data = new DiamondData(dataDirectory); Engine = new DiamondEngine(Data, random);
        Directory.CreateDirectory(stateDirectory); DatabasePath = Path.Combine(Path.GetFullPath(stateDirectory), "diamond_game.db");
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Matches(Code TEXT PRIMARY KEY,Owner TEXT NOT NULL,State TEXT NOT NULL,
                Version INTEGER NOT NULL DEFAULT 0,CreatedAt INTEGER NOT NULL,ExpiresAt INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_DiamondMatches_Expires ON Matches(ExpiresAt);
            CREATE TABLE IF NOT EXISTS CreateLimits(Key TEXT PRIMARY KEY,Count INTEGER NOT NULL,ExpiresAt INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_DiamondCreateLimits_Expires ON CreateLimits(ExpiresAt);
            """;
        command.ExecuteNonQuery();
    }
    public long Now() => _clock();
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, DefaultTimeout = 3 }.ToString());
        connection.Open(); return connection;
    }
    public DiamondView Get(string? code, string actor, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var (game, version) = Load(code); Member(game, actor);
            if (!Tick(game, Now()) || Save(game, version)) return DiamondEngine.View(game, actor, Now());
        }
        throw new DiamondInputError("상태를 갱신 중입니다.", 409);
    }
    public DiamondView Post(JsonElement body, string actor, string ip, CancellationToken cancellationToken = default)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new DiamondInputError("잘못된 요청입니다.");
        var operation = Text(body, "op"); var now = Now();
        if (operation == "create") return Create(body, actor, ip, now, cancellationToken);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var (game, version) = Load(Text(body, "code"));
            var gameData = Data.ForRoster(game.Roster);
            var engine = game.Roster is null ? Engine : new DiamondEngine(gameData, _random);
            if (operation == "join")
            {
                if (game.Mode != "pvp") throw new DiamondInputError("PvP 대결 코드가 아닙니다.");
                if (game.Host == actor || game.Guest == actor) return DiamondEngine.View(game, actor, Now());
                if (game.Guest != null) throw new DiamondInputError("이미 두 명이 참가했습니다.", 409);
                game.Guest = actor;
            }
            else
            {
                Member(game, actor);
                if (game.Mode == "pvp" && game.Guest == null) throw new DiamondInputError("상대가 참가할 때까지 기다려 주세요.", 409);
                if (game.Round >= 6) return DiamondEngine.View(game, actor, Now());
                var role = DiamondEngine.Side(game, actor);
                if (Tick(game, now, engine)) { if (Save(game, version)) return DiamondEngine.View(game, actor, Now()); continue; }
                if (operation is "pitch" or "ready")
                {
                    if (operation == "ready" && !(game.Mode == "ai" && role == "batter") || operation == "pitch" && role != "pitcher")
                        throw new DiamondInputError("내 시점에서 가능한 조작을 사용해 주세요.", 403);
                    var previousPitch = Integer(body, "previousPitch", "투구 상태를 확인해 주세요.");
                    if (previousPitch < 0) throw new DiamondInputError("투구 상태를 확인해 주세요.");
                    if (previousPitch < game.PitchCount) return DiamondEngine.View(game, actor, Now());
                    if (previousPitch > game.PitchCount) throw new DiamondInputError("투구 상태가 바뀌었습니다.", 409);
                    if (game.Pitch is { Resolved: false }) return DiamondEngine.View(game, actor, Now());
                    if (game.Pitch is { } previous && now < Math.Max(previous.ReleaseAt + previous.FlightMs, previous.Reaction?.At ?? 0) + 900)
                        throw new DiamondInputError("다음 투구를 준비하고 있습니다.", 409);
                    if (operation == "ready")
                    {
                        game.Pitch = engine.CreateAiPitch(game, now);
                    }
                    else
                    {
                        var aim = Aim(body, "코스와 릴리스 입력을 확인해 주세요."); var quality = Number(body, "quality", "코스와 릴리스 입력을 확인해 주세요.");
                        if (quality < 0 || quality > 1) throw new DiamondInputError("코스와 릴리스 입력을 확인해 주세요.");
                        var type = Text(body, "type") ?? "";
                        game.Pitch = engine.CreatePitch(game, type, aim, quality, now);
                    }
                    game.PitchCount = game.Pitch.Id;
                    if (game.Mode == "ai" && game.HostRole == "pitcher")
                    { game.Pitch.AiBatterSwing = engine.AiSwing(game); game.Pitch.AiBatterSwingPrepared = true; }
                }
                else if (operation == "swing")
                {
                    if (role != "batter") throw new DiamondInputError("타자만 스윙할 수 있습니다.", 403);
                    if (game.Pitch == null) throw new DiamondInputError("진행 중인 투구가 없습니다.");
                    var pitchId = Integer(body, "pitchId", "진행 중인 투구가 없습니다.");
                    if (pitchId < game.Pitch.Id || game.Pitch.Resolved) return DiamondEngine.View(game, actor, Now());
                    var inputAt = body.TryGetProperty("inputAt", out _) ? Number(body, "inputAt", "스윙 입력을 확인해 주세요.")
                        : Number(body, "at", "스윙 입력을 확인해 주세요.") - DiamondEngine.SwingContactMs;
                    var aim = Aim(body, "스윙 입력을 확인해 주세요.");
                    if (pitchId != game.Pitch.Id) throw new DiamondInputError("스윙 입력을 확인해 주세요.");
                    if (inputAt < now - 1500 || inputAt > now + 150) throw new DiamondInputError("연결 지연이 큽니다. 다음 투구에서 다시 시도해 주세요.", 409);
                    DiamondEngine.FinishPitch(game, engine.EvaluatePitch(game, new(inputAt + DiamondEngine.SwingContactMs, aim), now));
                }
                else throw new DiamondInputError("지원하지 않는 조작입니다.");
            }
            if (Save(game, version)) return DiamondEngine.View(game, actor, Now());
        }
        throw new DiamondInputError("상대 입력을 반영 중입니다. 다시 시도해 주세요.", 409);
    }
    private DiamondView Create(JsonElement body, string actor, string ip, long now, CancellationToken cancellationToken)
    {
        var mode = Text(body, "mode"); var role = Text(body, "role"); var pace = Text(body, "pace");
        if (mode is not ("ai" or "pvp") || role is not ("batter" or "pitcher") || pace is not ("practice" or "real" or "full"))
            throw new DiamondInputError("모드와 시점을 선택해 주세요.");
        var batter = Text(body, "batter") ?? ""; var pitcher = Text(body, "pitcher") ?? "";
        // Resolve only IDs against the server's warehouse. Client-supplied statistics are never trusted.
        var roster = _roster?.Select(Integer(body, "season", "선수 기록 시즌을 선택해 주세요."), batter, pitcher, cancellationToken);
        DiamondPitchingProfile? pitching = null;
        if (_roster is not null && roster is not null && mode == "ai" && role == "batter")
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                pitching = _roster.SelectPitching(roster.Season, pitcher, cancellationToken);
                if (pitching.Revision == roster.Revision) break;
                roster = _roster.Select(roster.Season, batter, pitcher, cancellationToken);
            }
            if (pitching is null || pitching.Revision != roster.Revision) throw new DiamondInputError("DB 기록이 갱신되었습니다. 대결을 다시 시작해 주세요.", 409);
        }
        var selected = Data.ForRoster(roster);
        selected.Batter(batter); selected.Pitcher(pitcher); cancellationToken.ThrowIfCancellationRequested(); ConsumeCreationLimit(ip, now);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var code = "D" + string.Concat(RandomNumberGenerator.GetBytes(7).Select(v => alphabet[v % 32]));
            var game = new DiamondGame { Code = code, Mode = mode, Host = actor, HostRole = role, Batter = batter,
                Pitcher = pitcher, Roster = roster, PitchingProfile = pitching, Pace = pace, CreatedAt = now, ExpiresAt = now + 86400000 };
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Matches(Code,Owner,State,Version,CreatedAt,ExpiresAt) VALUES($code,$owner,$state,0,$now,$expires) ON CONFLICT(Code) DO NOTHING";
            command.Parameters.AddWithValue("$code", code); command.Parameters.AddWithValue("$owner", actor);
            command.Parameters.AddWithValue("$state", JsonSerializer.Serialize(game, DiamondJson.Options));
            command.Parameters.AddWithValue("$now", now); command.Parameters.AddWithValue("$expires", game.ExpiresAt);
            if (command.ExecuteNonQuery() == 0) continue;
            command.Parameters.Clear(); command.CommandText = "DELETE FROM Matches WHERE Code IN (SELECT Code FROM Matches WHERE ExpiresAt<$now LIMIT 100); DELETE FROM CreateLimits WHERE ExpiresAt<$now";
            command.Parameters.AddWithValue("$now", now); command.ExecuteNonQuery();
            return DiamondEngine.View(game, actor, Now());
        }
        throw new DiamondInputError("대결 생성이 겹쳤습니다. 다시 시도해 주세요.", 409);
    }
    private void ConsumeCreationLimit(string ip, long now)
    {
        var ipHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip))); var minute = now / 60000;
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var exceeded = false;
        foreach (var (key, maximum) in new[] { (ipHash + ":" + minute, 16), ("global:" + minute, 180) })
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO CreateLimits(Key,Count,ExpiresAt) VALUES($key,1,$expires) ON CONFLICT(Key) DO UPDATE SET Count=Count+1 RETURNING Count";
            command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$expires", (minute + 2) * 60000);
            exceeded |= Convert.ToInt64(command.ExecuteScalar()) > maximum;
        }
        transaction.Commit();
        if (exceeded) throw new DiamondInputError("대결 생성이 많습니다. 1분 후 다시 시도해 주세요.", 429);
    }
    private (DiamondGame Game, long Version) Load(string? code)
    {
        if (code == null || !Regex.IsMatch(code, "^D[A-Z2-9]{7}$", RegexOptions.CultureInvariant)) throw new DiamondInputError("8자리 대결 코드를 확인해 주세요.");
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT State,Version FROM Matches WHERE Code=$code AND ExpiresAt>$now";
        command.Parameters.AddWithValue("$code", code); command.Parameters.AddWithValue("$now", Now());
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new DiamondInputError("대결이 없거나 만료되었습니다.", 404);
        var game = JsonSerializer.Deserialize<DiamondGame>(reader.GetString(0), DiamondJson.Options) ?? throw new InvalidDataException("게임 상태가 비어 있습니다.");
        if (game.Format != "action-v2") throw new DiamondInputError("새 액션 게임에서 대결을 시작해 주세요.", 410);
        return (game, reader.GetInt64(1));
    }
    private bool Save(DiamondGame game, long version)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Matches SET State=$state,Version=Version+1 WHERE Code=$code AND Version=$version";
        command.Parameters.AddWithValue("$state", JsonSerializer.Serialize(game, DiamondJson.Options));
        command.Parameters.AddWithValue("$code", game.Code); command.Parameters.AddWithValue("$version", version);
        return command.ExecuteNonQuery() == 1;
    }
    private static void Member(DiamondGame game, string actor)
    { if (game.Host != actor && game.Guest != actor) throw new DiamondInputError("먼저 대결 코드로 참가해 주세요.", 403); }
    private bool Tick(DiamondGame game, long now, DiamondEngine? engine = null)
    {
        if (game.Pitch == null || game.Pitch.Resolved) return false;
        var arrival = game.Pitch.ReleaseAt + game.Pitch.FlightMs; var aiBatter = game.Mode == "ai" && game.HostRole == "pitcher";
        var deadline = aiBatter ? Math.Max(arrival + 260, (game.Pitch.AiBatterSwing?.At ?? 0) + 60) : arrival + 750;
        if (now <= deadline) return false;
        engine ??= game.Roster is null ? Engine : new DiamondEngine(Data.ForRoster(game.Roster), _random);
        var swing = aiBatter ? game.Pitch.AiBatterSwingPrepared ? game.Pitch.AiBatterSwing : engine.AiSwing(game) : null;
        DiamondEngine.FinishPitch(game, engine.EvaluatePitch(game, swing, now)); return true;
    }
    private static string? Text(JsonElement body, string key) => body.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static double Number(JsonElement body, string key, string error)
    {
        if (!body.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new DiamondInputError(error);
        return number;
    }
    private static int Integer(JsonElement body, string key, string error)
    {
        var number = Number(body, key, error);
        if (number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number)) throw new DiamondInputError(error);
        return (int)number;
    }
    private static DiamondVec Aim(JsonElement body, string error)
    {
        if (!body.TryGetProperty("aim", out var aim) || aim.ValueKind != JsonValueKind.Object) throw new DiamondInputError(error);
        var x = Number(aim, "x", error); var y = Number(aim, "y", error);
        if (Math.Abs(x) > 2 || Math.Abs(y) > 2) throw new DiamondInputError(error);
        return new(x, y);
    }
}
