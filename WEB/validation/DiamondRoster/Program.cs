using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using NaverSabermetrics.Web;

try
{
    if (args is ["--probe", var probePath])
    {
        var service = new DiamondRosterService(Path.GetFullPath(probePath));
        var timer = System.Diagnostics.Stopwatch.StartNew(); var cold = service.Get(null); timer.Stop(); var coldMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart(); var warm = service.Get(cold.Season); timer.Stop();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            cold.Season, cold.AsOf, cold.Revision, Seasons = cold.Seasons, Batters = cold.Batters.Count, Pitchers = cold.Pitchers.Count,
            MissingBatterHand = cold.Batters.Count(x => x.Profile?.Bats is null), MissingPitcherHand = cold.Pitchers.Count(x => x.Profile?.Throws is null),
            DefaultArsenals = cold.Pitchers.Count(x => x.ArsenalSource == "default"), ColdMs = coldMs, WarmMs = timer.Elapsed.TotalMilliseconds,
            CachedInstance = ReferenceEquals(cold, warm)
        }, DiamondJson.Options));
        return;
    }
    if (args.Length != 2) throw new ArgumentException("Usage: <absolute game-lib-directory> <absolute scratch-directory>");
    var dataDirectory = Path.GetFullPath(args[0]);
    var output = Path.Combine(Path.GetFullPath(args[1]), "roster-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(output);
    var dbPath = Path.Combine(output, "synthetic-baseball.db");
    RosterFixture.Create(dbPath);
    var checks = 0;
    void Check(bool value, string name) { checks++; if (!value) throw new InvalidDataException(name); }
    void Near(double expected, double? actual, string name) => Check(actual.HasValue && Math.Abs(expected - actual.Value) < 1e-8, $"{name}: expected {expected:R}, got {actual:R}");
    void Error(Action action, int status, string name)
    {
        try { action(); throw new InvalidDataException(name + " did not reject"); }
        catch (DiamondInputError e) { Check(e.Status == status, name + " status " + e.Status); }
    }
    void Cancelled(Action action, string name)
    {
        try { action(); throw new InvalidDataException(name + " did not cancel"); }
        catch (OperationCanceledException) { checks++; }
    }
    JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, DiamondJson.Options);
    string Snapshot(object? value) => JsonSerializer.Serialize(value, DiamondJson.Options);
    string HashDb() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dbPath)));
    var before = HashDb();
    var roster = new DiamondRosterService(dbPath);
    var latest = Json(roster.Get(null));
    Check(latest.GetProperty("season").GetInt32() == 2026, "latest completed regular season");
    Check(latest.GetProperty("asOf").GetString() == "2026-09-11", "completed regular date excludes future/postseason/all-star");
    var seasons = latest.GetProperty("seasons").EnumerateArray().Select(x => x.GetInt32()).ToArray();
    Check(seasons.SequenceEqual(new[] { 2026, 2025 }), "available season list excludes unstarted season");
    Check(latest.GetProperty("batters").GetArrayLength() == 3 && latest.GetProperty("pitchers").GetArrayLength() == 3, "only actual appearances are selectable");
    var selected = roster.Select(2026, "2026:101", "2026:201");
    var batter = selected.Batter; var pitcher = selected.Pitcher;
    Check(batter.PlayerId == "101" && batter.Id == "2026:101" && batter.Name == "같은이름", "stable batter identity");
    Check(batter.Team == "KT", "traded player uses selected-season last team");
    Near(9, batter.Pa, "traded PA sum"); Near(7, batter.Ab, "AB"); Near(3, batter.H, "hits"); Near(1, batter.Hr, "HR"); Near(1, batter.So, "batter SO");
    Near(3d / 7, batter.Avg, "AVG derived from aggregate AB"); Near(1, batter.Slg, "SLG from aggregate TB"); Near(1 + 5d / 9, batter.Ops, "OPS includes BB/HBP and correct denominator");
    Near(188, batter.Profile?.HeightCm, "recorded batter height");
    Check(batter.Profile?.Bats == "L" && pitcher.Profile?.Throws == "L", "recorded batting/throwing hand");
    Near(190, pitcher.Profile?.HeightCm, "recorded pitcher height");
    Near(18, pitcher.Tbf, "TBF"); Near(3, pitcher.Bb, "final BB preferred; PA fallback for absent final line"); Near(5, pitcher.So, "final SO preferred; PA fallback");
    Near(15, pitcher.Outs, "integer outs sum"); Near(1, pitcher.Er, "ER sum");
    Check(pitcher.Era is null && pitcher.Whip is null, "partial final pitching totals do not invent ERA/WHIP");
    Check(!string.IsNullOrWhiteSpace(pitcher.SampleNote), "partial pitching data note");
    foreach (var (discipline, name) in new[] { (batter.Discipline, "batter"), (pitcher.Discipline, "pitcher") })
    {
        Near(20d / 36, discipline?.ZonePitchRate, name + " zone rate");
        Near(13d / 20, discipline?.ZoneSwingRate, name + " zone swing");
        Near(6d / 16, discipline?.ChaseRate, name + " chase");
        Near(11d / 13, discipline?.ZoneContactRate, name + " zone contact");
        Near(.5, discipline?.OutZoneContactRate, name + " outside contact");
    }
    Check(pitcher.ArsenalSource != "default", "measured arsenal marked as measured");
    Check(pitcher.Arsenal.Count == 3, "unsupported pitch is not relabeled");
    foreach (var (type, velocity, usage) in new[] { ("fastball", 148d, 50d), ("slider", 132d, 25d), ("curve", 120d, 12.5d) })
    {
        var pitch = pitcher.Arsenal.Single(x => x.Type == type);
        Near(velocity, pitch.Velocity, type + " excludes invalid speeds from average");
        Near(usage, pitch.Usage, type + " counts unmeasured and unsupported pitches in denominator");
    }
    var unknown = roster.Select(2026, "2026:102", "2026:202");
    Check(unknown.Batter.Name == batter.Name && unknown.Batter.PlayerId != batter.PlayerId, "same display name remains separate identity");
    Check(unknown.Batter.Profile?.HeightCm is null && unknown.Batter.Profile?.Bats is null && unknown.Pitcher.Profile?.Throws is null, "missing profile remains unknown");
    Check(unknown.Batter.Discipline?.ChaseRate is null && unknown.Pitcher.Discipline?.ZoneContactRate is null, "zero-denominator discipline remains unknown");
    Check(unknown.Pitcher.ArsenalSource == "default" && !string.IsNullOrWhiteSpace(unknown.Pitcher.SampleNote), "fallback arsenal is visibly identified");
    Near(0, unknown.Pitcher.Era, "complete shutout ERA"); Near(0, unknown.Pitcher.Whip, "complete WHIP");
    var old = roster.Select(2025, "2025:101", "2025:201");
    Near(.25, old.Batter.Avg, "prior season separate rates"); Check(old.Batter.Team == "SS", "prior season team code");
    Check(roster.Get(2025).Teams.Single(x => x.Code == "SS").Name == "삼성", "team label uses actual team name");
    Check(old.Batter.Profile?.Bats == "R" && old.Pitcher.Profile?.Throws == "R", "selected season hand overrides global player profile");
    Near(181, old.Batter.Profile?.HeightCm, "selected season height excludes later-season measurement");
    Near(136, old.Pitcher.Arsenal.Single().Velocity, "prior season speed"); Near(9, old.Pitcher.Era, "complete ERA uses outs/3"); Near(5d / 3, old.Pitcher.Whip, "complete WHIP uses outs/3");
    var twoWay = roster.Select(2026, "2026:301", "2026:301");
    Check(twoWay.Batter.PlayerId == twoWay.Pitcher.PlayerId && twoWay.Batter.Pa == 2 && twoWay.Pitcher.Tbf == 4, "same ID supports independent roles");
    Error(() => roster.Get(2024), 400, "uncollected season");
    Error(() => roster.Select(2026, "2025:101", "2026:201"), 400, "wrong-season ID");
    Error(() => roster.Select(2026, "2026:101' OR 1=1--", "2026:201"), 400, "injection-like ID");
    Error(() => roster.Select(2026, "같은이름", "2026:201"), 400, "name cannot substitute stable ID");
    Error(() => roster.Select(2026, "2026:201", "2026:101"), 400, "invalid role ID");
    Error(() => roster.Select(2026, "2026:999", "2026:201"), 400, "no-appearance ID");
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        Cancelled(() => roster.Get(2026, cancelled.Token), "catalog cancellation");
        Cancelled(() => roster.Select(2026, "2026:101", "2026:201", cancelled.Token), "selection cancellation");
    }
    Check(HashDb() == before, "roster reading leaves source DB bytes unchanged");
    var missingPath = Path.Combine(output, "absent.db");
    Error(() => new DiamondRosterService(missingPath).Get(null), 503, "missing DB");
    Check(!File.Exists(missingPath), "missing DB never created implicitly");
    var optionalPath = Path.Combine(output, "without-optional-tables.db"); RosterFixture.Create(optionalPath);
    using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = optionalPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
    {
        db.Open(); using var command = db.CreateCommand();
        command.CommandText = "DROP TABLE Metadata; DROP TABLE Players; DROP TABLE GamePlayers; DROP TABLE BattingGameLines; DROP TABLE PitchingGameLines; DROP TABLE Pitches;";
        command.ExecuteNonQuery();
    }
    var optional = new DiamondRosterService(optionalPath).Select(2026, "2026:101", "2026:201");
    Check(optional.Batter.Name == "같은이름" && optional.Batter.Profile?.Bats is null && optional.Batter.Profile?.HeightCm is null, "minimal DB retains stat name without inventing profile");
    Near(3d / 7, optional.Batter.Avg, "minimal DB aggregation");
    Check(optional.Pitcher.ArsenalSource == "default" && optional.Revision.Length > 0, "minimal DB fallback provenance and revision");
    var detached = roster.Select(2026, "2026:101", "2026:201"); detached.Batter.H = 99999;
    Check(roster.Select(2026, "2026:101", "2026:201").Batter.H == 3, "selected match objects cannot mutate roster cache");
    Console.WriteLine($"PASS roster selection, aggregate statistics, measurements, missing values, input bounds, read-only DB ({checks} checks)");

    long now = 1700000000000;
    var statePath = Path.Combine(output, "matches");
    var games = new DiamondGameService(statePath, dataDirectory, () => now, () => .5, roster);
    DiamondView Create(DiamondGameService service, int season, string actor, string mode = "ai", string role = "pitcher") => service.Post(Json(new
    {
        op = "create", mode, role, pace = "full", season, batter = season + ":101", pitcher = season + ":201",
        roster = new { season = 1900, revision = "forged", batter = new { avg = 1, pa = 99999 }, pitcher = new { arsenal = new[] { new { type = "fastball", velocity = 999, usage = 100 } } } }
    }), actor, actor + "-ip");
    Error(() => games.Post(Json(new { op = "create", mode = "ai", role = "pitcher", pace = "full", batter = "2026:101", pitcher = "2026:201" }), "bad", "bad"), 400, "create requires selected season");
    Error(() => games.Post(Json(new { op = "create", mode = "ai", role = "pitcher", pace = "full", season = 2026, batter = "2025:101", pitcher = "2026:201" }), "bad", "bad"), 400, "create rejects mismatched season");
    var room26 = Create(games, 2026, "host26", "pvp"); var room25 = Create(games, 2025, "host25");
    Check(room26.Roster is not null && Snapshot(room26.Roster) == Snapshot(selected), "create selects server stats and ignores forged client roster");
    Check(room25.Roster?.Season == 2025 && room26.Roster?.Season == 2026, "concurrent seasons isolated");
    var baseline26 = Snapshot(room26.Roster); var baseline25 = Snapshot(room25.Roster);
    games.Post(Json(new { op = "join", code = room26.Code }), "guest26", "guest-ip");
    Check(Snapshot(games.Get(room26.Code, "guest26").Roster) == baseline26, "PvP guest shares host selection snapshot");
    RosterFixture.ChangeSeason2026(dbPath);
    var changed = roster.Select(2026, "2026:101", "2026:201");
    Near(4, changed.Batter.H, "new DB revision refreshes roster");
    Check(changed.Revision != selected.Revision, "source revision changes after DB update");
    Check(Snapshot(games.Get(room26.Code, "host26").Roster) == baseline26, "existing match survives DB stat update");
    var restarted = new DiamondGameService(statePath, dataDirectory, () => now, () => .5, new DiamondRosterService(dbPath));
    Check(Snapshot(restarted.Get(room26.Code, "host26").Roster) == baseline26, "persisted snapshot survives service restart");
    Check(Snapshot(restarted.Get(room25.Code, "host25").Roster) == baseline25, "other season persisted snapshot stays independent");
    var next = Create(restarted, 2026, "next"); Near(4, next.Roster?.Batter.H, "new match uses refreshed DB");
    var observed = new ConcurrentBag<(int Season, double Velocity)>();
    Parallel.ForEach(new[] { (room26.Code, "host26", 2026), (room25.Code, "host25", 2025) }, item =>
    {
        var view = restarted.Post(Json(new { op = "pitch", code = item.Code, previousPitch = 0, type = "fastball", quality = .5, aim = new { x = 0, y = 0 } }), item.Item2, item.Item2);
        observed.Add((item.Item3, view.Pitch!.Velocity));
    });
    Near(148, observed.Single(x => x.Season == 2026).Velocity, "pitch engine uses persisted original season speed");
    Near(136, observed.Single(x => x.Season == 2025).Velocity, "simultaneous other season engine isolated");
    var missingGames = new DiamondGameService(Path.Combine(output, "missing-matches"), dataDirectory, () => now, () => .5, new DiamondRosterService(missingPath));
    Error(() => Create(missingGames, 2026, "missing"), 503, "missing DB create does not fallback to bundled roster");
    using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = missingGames.DatabasePath, Mode = SqliteOpenMode.ReadOnly }.ToString()))
    { db.Open(); using var command = db.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM Matches"; Check(Convert.ToInt64(command.ExecuteScalar()) == 0, "failed roster create persists no match"); }
    var offlineRestart = new DiamondGameService(statePath, dataDirectory, () => now, () => .5, new DiamondRosterService(missingPath));
    Check(Snapshot(offlineRestart.Get(room26.Code, "host26").Roster) == baseline26, "existing match survives source DB unavailable after restart");
    Console.WriteLine($"PASS authoritative creation, PvP selection, persisted snapshots and concurrent seasons ({checks} checks)");

    // Real minimal API endpoints, using only the synthetic warehouse and match state.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Development" });
    builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
    builder.Services.AddSingleton(roster); builder.Services.AddSingleton(restarted);
    await using (var app = builder.Build())
    {
        app.MapDiamondGame(); await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var good = await client.GetAsync("/api/diamond/roster?season=2026");
        Check(good.StatusCode == HttpStatusCode.OK && good.Headers.CacheControl?.NoStore == true, "roster HTTP success is no-store");
        var wire = await good.Content.ReadFromJsonAsync<JsonElement>();
        Check(wire.GetProperty("batters")[0].TryGetProperty("playerId", out _), "camelCase roster fields");
        foreach (var query in new[] { "2026%20OR%201%3D1", "2024", "2026.5", "-1" })
        { using var response = await client.GetAsync("/api/diamond/roster?season=" + query); Check(response.StatusCode == HttpStatusCode.BadRequest, "invalid HTTP season " + query); }
        await app.StopAsync();
    }
    var unavailableBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Development" });
    unavailableBuilder.Logging.ClearProviders(); unavailableBuilder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
    unavailableBuilder.Services.AddSingleton(new DiamondRosterService(missingPath)); unavailableBuilder.Services.AddSingleton(missingGames);
    await using (var app = unavailableBuilder.Build())
    {
        app.MapDiamondGame(); await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var response = await client.GetAsync("/api/diamond/roster");
        Check(response.StatusCode == HttpStatusCode.ServiceUnavailable, "missing DB HTTP 503");
        Check(!(await response.Content.ReadAsStringAsync()).Contains(missingPath, StringComparison.Ordinal), "HTTP error does not disclose local DB path");
        await app.StopAsync();
    }
    Console.WriteLine($"PASS Diamond roster validation: {checks} checks. Synthetic artifacts: {output}");
}
catch (Exception error)
{
    Console.Error.WriteLine(error); Environment.ExitCode = 1;
}
