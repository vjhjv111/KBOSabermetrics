using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverSabermetrics.Web;

if (args.Length > 1) throw new ArgumentException("Optionally pass the read-only 2026 preview season.db path.");
await SyntheticCases();
if (args.Length == 1) await PreviewCase(Path.GetFullPath(args[0]));
Console.WriteLine("Analysis velocity validation passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}
static List<Dictionary<string, object?>> Bands(AnalysisResult result) => result.Tables.Single(t => t.Key == "velocityBands").Rows;
static Dictionary<string, object?> Coverage(AnalysisResult result) => result.Tables.Single(t => t.Key == "velocityCoverage").Rows.Single();
static long Number(Dictionary<string, object?> row, string key) => Convert.ToInt64(row[key]);
static long Sum(AnalysisResult result, string key) => Bands(result).Sum(row => Number(row, key));
static bool Near(object? actual, double expected) => actual is not null && Math.Abs(Convert.ToDouble(actual) - expected) < 1e-10;
static AnalysisWebService Service(string path) => new(new DatabaseCacheService(path, true), new SiteOptions { QuerySeconds = 30 });

static async Task SyntheticCases()
{
    var path = Path.Combine(AppContext.BaseDirectory, "velocity-fixture-" + Guid.NewGuid().ToString("N") + ".db");
    try
    {
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await db.OpenAsync();
        async Task Execute(string sql, params object?[] values)
        {
            await using var command = db.CreateCommand(); command.CommandText = sql;
            for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("$p" + i, values[i] ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }
        await Execute("""
            CREATE TABLE Games(GameId TEXT PRIMARY KEY,SeasonYear INTEGER,GameDate TEXT,RoundCode TEXT,HomeTeamCode TEXT,AwayTeamCode TEXT);
            CREATE TABLE PlateAppearances(PlateAppearanceId TEXT PRIMARY KEY,GameId TEXT,SequenceNumber INTEGER,
              IsOfficial INTEGER,Status INTEGER,CountsAsAtBat INTEGER,TotalBases INTEGER,IsStrikeout INTEGER,
              IsWalk INTEGER,IsIntentionalWalk INTEGER,IsHit INTEGER,WasRecognized INTEGER,ResultType INTEGER,
              BatterPcode TEXT,PitcherPcode TEXT,FinalPitcherPcode TEXT,BattingTeamCode TEXT,FieldingTeamCode TEXT);
            CREATE TABLE Pitches(PitchEventId TEXT PRIMARY KEY,GameId TEXT,PlateAppearanceId TEXT,ActualPitchIndex INTEGER,
              SourceOptionIndex INTEGER,PitcherPcode TEXT,BatterPcode TEXT,BattingSide INTEGER,PitchType TEXT,
              BatterStance TEXT,BallsBefore INTEGER,StrikesBefore INTEGER,SpeedKmh REAL,IsSwing INTEGER,IsWhiff INTEGER);
            CREATE INDEX IX_LastPitch ON Pitches(PlateAppearanceId,ActualPitchIndex DESC,SourceOptionIndex DESC,PitchEventId DESC);
            INSERT INTO Games VALUES('G1',2026,'2026-09-01','kbo_r','LG','OB');
            INSERT INTO Games VALUES('G2',2026,'2026-08-20','kbo_r','LG','OB');
            INSERT INTO Games VALUES('ALLSTAR',2026,'2026-09-01','kbo_r','EA','WE');
            INSERT INTO Games VALUES('EXHIBITION',2026,'2026-09-01','kbo_e','LG','OB');
            """);
        var sequence = 0;
        async Task Pa(string id, string batter, int result = 1, string pitcher = "P", string game = "G1", int known = 1, int official = 1, int status = 0)
        {
            var ab = result is >= 1 and <= 6 or >= 10 and <= 16 or >= 19 and <= 22 ? 1 : 0;
            var hit = result is >= 1 and <= 6 ? 1 : 0;
            var tb = result switch { 1 or 2 or 3 => 1, 4 => 2, 5 => 3, 6 => 4, _ => 0 };
            await Execute("INSERT INTO PlateAppearances VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13,$p14,$p14,'OB','LG')",
                id, game, ++sequence, official, status, ab, tb, result == 10 ? 1 : 0, result is 7 or 8 ? 1 : 0,
                result == 8 ? 1 : 0, hit, known, result, batter, pitcher);
        }
        var pitchSequence = 0;
        async Task Pitch(string? pa, string batter, double? speed, int actual = 1, string type = "F", string pitcher = "P",
            int swing = 0, int whiff = 0, string game = "G1", string stance = "R", int balls = 1, int strikes = 2,
            int option = 0, string? id = null)
        {
            await Execute("INSERT INTO Pitches VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,0,$p7,$p8,$p9,$p10,$p11,$p12,$p13)",
                id ?? "P" + (++pitchSequence).ToString("D4"), game, pa, actual, option, pitcher, batter, type, stance, balls, strikes, speed, swing, whiff);
        }
        foreach (var (speed, i) in new double?[] { 40, 119.999, 120, 134.999, 135, 149.999, 150, 180, 39.999, 180.001, null }.Select((s, i) => (s, i)))
        {
            await Pa("B" + i, "Boundary"); await Pitch("B" + i, "Boundary", speed, swing: 1, whiff: i == 0 ? 1 : 0);
        }
        await Pitch(null, "Orphan", 140, swing: 1, whiff: 1);
        await Pa("OPEN", "Open", status: 2); await Pitch("OPEN", "Open", 140);
        await Pa("UNOFFICIAL", "Unofficial", official: 0); await Pitch("UNOFFICIAL", "Unofficial", 140);
        await Pa("TERMINAL", "Filter", result: 6);
        await Pitch("TERMINAL", "Filter", 119.999, type: "F", stance: "R", balls: 0, strikes: 0, swing: 1, whiff: 1);
        await Pitch("TERMINAL", "Filter", 150, actual: 2, type: "C", stance: "L", balls: 2, strikes: 2, swing: 1);
        await Pa("INVALID_END", "InvalidEnd", result: 6);
        await Pitch("INVALID_END", "InvalidEnd", 140); await Pitch("INVALID_END", "InvalidEnd", null, actual: 2);
        await Pa("SWITCH", "NewB", result: 6, pitcher: "NewP");
        await Pitch("SWITCH", "OldB", 120, pitcher: "OldP"); await Pitch("SWITCH", "NewB", 150, actual: 2, pitcher: "NewP");
        await Pa("TIE", "Tie", result: 6);
        await Pitch("TIE", "Tie", 135, option: 0, id: "TIE_Z0");
        await Pitch("TIE", "Tie", 120, option: 1, id: "TIE_A1");
        await Pitch("TIE", "Tie", 150, type: "C", option: 1, id: "TIE_Z1");
        foreach (var result in new[] { 1, 8, 9, 17, 18 })
        {
            await Pa("R" + result, "Ratios", result); await Pitch("R" + result, "Ratios", 150, swing: result == 1 ? 1 : 0);
        }
        await Pa("UNKNOWN", "Ratios", result: 6, known: 0); await Pitch("UNKNOWN", "Ratios", 150);
        await Pa("NOPITCH", "Ratios", result: 8);
        await Pa("ONLY_WALK", "OnlyWalk", result: 8); await Pitch("ONLY_WALK", "OnlyWalk", 150);
        await Pa("OUT", "Zero", result: 11); await Pitch("OUT", "Zero", 120);
        foreach (var game in new[] { "G1", "G2", "ALLSTAR", "EXHIBITION" })
        {
            await Pa("S" + game, "Scope", game: game); await Pitch("S" + game, "Scope", 150, game: game, pitcher: "ScopeP");
        }
        var service = Service(path);
        var request = new AnalysisRequest(Section: "velocity", Code: "Boundary");
        async Task<AnalysisResult> Query(AnalysisRequest r) => await service.QueryAsync(r, CancellationToken.None);
        var boundary = await Query(request);
        Check(Bands(boundary).Count == 4 && Bands(boundary).Select(r => Number(r, "Band")).SequenceEqual(new long[] { 0, 1, 2, 3 }), "four bands in velocity order");
        Check(Bands(boundary).All(r => Number(r, "N") == 2 && Number(r, "PA") == 2), "40/119.999/120/134.999/135/149.999/150/180 boundaries");
        var coverage = Coverage(boundary);
        Check(Number(coverage, "Pitches") == 11 && Number(coverage, "SpeedPitches") == 8 && Number(coverage, "MissingSpeedPitches") == 1 && Number(coverage, "InvalidSpeedPitches") == 2,
            "missing and out-of-range pitches excluded and counted separately");
        Check(Number(coverage, "TerminalPA") == 11 && Number(coverage, "SpeedPA") == 8 && Number(coverage, "MissingSpeedPA") == 1 && Number(coverage, "InvalidSpeedPA") == 2,
            "missing and out-of-range terminal PA coverage reconciles");
        Check(Near(Bands(boundary)[0]["WhiffPct"], 50) && Near(Bands(boundary)[0]["SwingPct"], 100), "swing and whiff use pitch and swing denominators");
        foreach (var code in new[] { "Orphan", "Open", "Unofficial" })
        {
            var result = await Query(request with { Code = code });
            Check(Sum(result, "N") == 1 && Sum(result, "PA") == 0 && Sum(result, "AB") == 0, code + " pitch remains in pitch metrics without a completed official PA");
        }
        var unfiltered = await Query(request with { Code = "Filter" });
        Check(Number(Bands(unfiltered)[0], "N") == 1 && Number(Bands(unfiltered)[0], "PA") == 0 && Number(Bands(unfiltered)[3], "PA") == 1, "one PA belongs to actual last-pitch velocity only");
        foreach (var filtered in new[] { request with { Code = "Filter", PitchType = "F" }, request with { Code = "Filter", Stance = "R" }, request with { Code = "Filter", Count = "0-0" } })
        {
            var result = await Query(filtered);
            Check(Sum(result, "N") == 1 && Sum(result, "PA") == 0 && Sum(result, "TB") == 0, "filter does not shift terminal outcome: " + filtered.PitchType + filtered.Stance + filtered.Count);
        }
        var invalidEnd = await Query(request with { Code = "InvalidEnd" });
        Check(Sum(invalidEnd, "N") == 1 && Sum(invalidEnd, "PA") == 0 && Number(Coverage(invalidEnd), "MissingSpeedPA") == 1, "missing terminal speed never borrows an earlier speed");
        Check(Sum(await Query(request with { Code = "OldP", Role = "pitcher" }), "PA") == 0 && Sum(await Query(request with { Code = "OldB" }), "PA") == 0,
            "terminal lookup crosses player scope before attribution");
        var finalPitcher = await Query(request with { Code = "NewP", Role = "pitcher", Team = "LG" });
        Check(Sum(finalPitcher, "PA") == 1 && finalPitcher.Notes.Any(n => n.Contains("마지막 투구 기준 상대 타자")), "pitcher outcome belongs to final pitcher with explicit perspective note");
        var tied = await Query(request with { Code = "Tie" });
        Check(Sum(tied, "PA") == 1 && Number(Bands(tied)[3], "PA") == 1 && Sum(await Query(request with { Code = "Tie", PitchType = "F" }), "PA") == 0,
            "global final pitch uses source-option and event-id tie breaks");
        var ratios = await Query(request with { Code = "Ratios" }); var r = Bands(ratios)[3];
        Check(Number(r, "PA") == 6 && Number(r, "OutcomePA") == 5 && Number(r, "UnknownOutcomePA") == 1, "unknown result excluded from rates and counted explicitly");
        Check(Number(r, "AB") == 1 && Number(r, "BB") == 1 && Number(r, "HBP") == 1 && Number(r, "SF") == 1 && Number(r, "ObpN") == 4,
            "IBB counted once, SF in OBP denominator, SH outside AB and OBP denominator");
        Check(Near(r["AVG"], 1) && Near(r["OBP"], .75) && Near(r["SLG"], 1) && Near(r["OPS"], 1.75), "AVG/OBP/SLG/OPS use correct separate denominators");
        Check(Number(Coverage(ratios), "NoPitchPA") == 1 && Number(Coverage(await Query(request with { Code = "Ratios", PitchType = "Absent" })), "NoPitchPA") == 1,
            "no-pitch PA reported outside velocity and before unobservable pitch filters");
        var zero = Bands(await Query(request with { Code = "Zero" }))[1];
        Check(Near(zero["AVG"], 0) && Near(zero["OPS"], 0) && Near(zero["SwingPct"], 0) && zero["WhiffPct"] is null, "observed zero differs from missing swing denominator");
        var empty = await Query(request with { Code = "Missing" });
        Check(Bands(empty).Count == 4 && Bands(empty).All(b => Number(b, "N") == 0 && new[] { "AVG", "OBP", "SLG", "OPS", "SwingPct", "WhiffPct" }.All(k => b[k] is null)),
            "empty result retains all four bands and null rates");
        var bbOnly = Bands(await Query(request with { Code = "OnlyWalk" }))[3];
        Check(Near(bbOnly["OBP"], 1) && bbOnly["AVG"] is null && bbOnly["SLG"] is null && bbOnly["OPS"] is null,
            "walk-only bin retains OBP but leaves AB-based rates and OPS null");
        var scoped = await Query(request with { Code = "Scope", Team = "OB", Start = "2026-09-01", End = "2026-09-01", PitchType = "F", Stance = "R", Count = "1-2" });
        Check(Sum(scoped, "N") == 1 && Sum(scoped, "PA") == 1, "date/team/type/stance/count filters and regular-season exclusions combine");
        Check(Sum(await Query(request with { Code = "Scope", Team = "LG" }), "N") == 0 && Sum(await Query(request with { Code = "ScopeP", Role = "pitcher", Team = "LG", Start = "2026-09-01" }), "N") == 1,
            "team filter follows batter or pitcher perspective");
        Check((await Query(request with { Code = "" })).Tables.Length == 0, "player selection required");
        Check(new[] { boundary, ratios, empty }.All(result => result.Tables.SelectMany(t => t.Rows).SelectMany(row => row.Values).All(value => value is not double d || double.IsFinite(d))), "all reported numbers finite");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await service.QueryAsync(request, cancelled.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        Check(true, "cancellation honored");
    }
    finally
    {
        // Only this uniquely named fixture is removed; the optional preview database stays read-only.
        SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
    }
}

static async Task PreviewCase(string path)
{
    var service = Service(path);
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var result = await service.QueryAsync(new AnalysisRequest(Section: "velocity", Year: 2026, Code: "67893"), CancellationToken.None);
    Check(Bands(result).Select(r => Number(r, "N")).SequenceEqual(new long[] { 61, 597, 1414, 281 }), "박성한 actual velocity pitch counts");
    Check(Bands(result).Select(r => Number(r, "PA")).SequenceEqual(new long[] { 12, 159, 315, 68 }), "박성한 actual terminal PA by velocity");
    Check(Bands(result).Select(r => Number(r, "AB")).SequenceEqual(new long[] { 12, 143, 263, 55 }), "박성한 actual AB by velocity");
    Check(Sum(result, "PA") == 554 && Sum(result, "AB") == 473 && Sum(result, "H") == 157 && Sum(result, "TB") == 200 && Sum(result, "BB") == 76 && Sum(result, "HBP") == 2 && Sum(result, "SF") == 3,
        "박성한 actual batting numerators reconcile");
    Check(Number(Coverage(result), "NoPitchPA") == 2 && Number(Coverage(result), "MissingSpeedPitches") == 0 && Number(Coverage(result), "InvalidSpeedPitches") == 0,
        "박성한 two no-pitch walks stay outside velocity bins");
    await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
    await db.OpenAsync();
    await using var command = db.CreateCommand(); command.CommandText = """
        SELECT COUNT(*) FROM PlateAppearances pa JOIN Games g ON g.GameId=pa.GameId
        WHERE pa.BatterPcode='67893' AND pa.IsOfficial=1 AND pa.Status=0 AND g.SeasonYear=2026
          AND LOWER(TRIM(g.RoundCode))='kbo_r' AND UPPER(g.HomeTeamCode) NOT IN ('EA','WE') AND UPPER(g.AwayTeamCode) NOT IN ('EA','WE')
        """;
    Check(Convert.ToInt64(await command.ExecuteScalarAsync()) == Sum(result, "PA") + Number(Coverage(result), "NoPitchPA"), "박성한 556 official PA independently reconcile");
    Console.WriteLine($"Preview query and reconciliation: {watch.ElapsedMilliseconds} ms.");
}
