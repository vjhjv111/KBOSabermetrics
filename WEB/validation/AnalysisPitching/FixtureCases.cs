using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverSabermetrics.Web;

internal static class FixtureCases
{
    public static async Task RunAsync(SqliteConnection original)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "analysis-edge-" + Guid.NewGuid().ToString("N") + ".db");
        await using var fixture = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await fixture.OpenAsync();
        var schemas = new Dictionary<string, List<(string Name, string Type, bool Required)>>();
        foreach (var table in new[] { "Games", "PlateAppearances", "Pitches", "NormalizedEvents", "PitchingGameLines" })
        {
            await using var schema = original.CreateCommand();
            schema.CommandText = "SELECT sql FROM sqlite_master WHERE name=$name AND type='table'";
            schema.Parameters.AddWithValue("$name", table);
            await using var create = fixture.CreateCommand(); create.CommandText = Convert.ToString(await schema.ExecuteScalarAsync())!;
            await create.ExecuteNonQueryAsync();
            await using var info = fixture.CreateCommand(); info.CommandText = "PRAGMA table_info(" + table + ")";
            await using var reader = await info.ExecuteReaderAsync();
            var columns = new List<(string, string, bool)>();
            while (await reader.ReadAsync()) columns.Add((reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1 && reader.IsDBNull(4)));
            schemas[table] = columns;
        }
        async Task Insert(string table, params (string Key, object? Value)[] fields)
        {
            var values = fields.ToDictionary(x => x.Key, x => x.Value);
            foreach (var column in schemas[table])
                if (column.Required && !values.ContainsKey(column.Name)) values[column.Name] = column.Type.Contains("INT") ? 0 : "";
            await using var command = fixture.CreateCommand();
            command.CommandText = $"INSERT INTO {table} ({string.Join(',', values.Keys)}) VALUES ({string.Join(',', values.Keys.Select((_, i) => "$p" + i))})";
            var index = 0; foreach (var value in values.Values) command.Parameters.AddWithValue("$p" + index++, value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }
        await Insert("Games", ("GameId", "G1"), ("SeasonYear", 2026), ("GameDate", "2026-09-11"), ("RoundCode", "kbo_r"), ("HomeTeamCode", "LG"), ("AwayTeamCode", "OB"));
        async Task Pa(string id, int sequence, string batter, int official = 1, int ab = 1, int k = 0, int bb = 0, int tb = 0)
            => await Insert("PlateAppearances", ("PlateAppearanceId", id), ("GameId", "G1"), ("SequenceNumber", sequence), ("BatterPcode", batter),
                ("PitcherPcode", "P"), ("FinalPitcherPcode", "P"), ("BattingTeamCode", "OB"), ("FieldingTeamCode", "LG"),
                ("IsOfficial", official), ("Status", official == 1 ? 0 : 1), ("CountsAsAtBat", ab), ("IsStrikeout", k), ("IsWalk", bb), ("TotalBases", tb));
        var chronology = 0;
        async Task Pitch(string pa, int actual, string batter, string type, string pitcher = "P", bool eventPresent = true)
        {
            var id = "E" + ++chronology;
            if (eventPresent) await Insert("NormalizedEvents", ("EventId", id), ("GameId", "G1"), ("ChronologicalIndex", chronology));
            await Insert("Pitches", ("PitchEventId", id + "P"), ("SourceEventId", id), ("GameId", "G1"), ("PlateAppearanceId", pa),
                ("PitcherPcode", pitcher), ("BatterPcode", batter), ("ActualPitchIndex", actual), ("SourceOptionIndex", actual), ("PitchType", type),
                ("BattingSide", 0), ("CrossPlateX", 0.0), ("CalculatedCrossPlateZ", 2.0), ("BottomStrikeZone", 1.0), ("TopStrikeZone", 3.0), ("BatterStance", "R"),
                ("IsSwing", 1), ("IsWhiff", 1), ("SpeedKmh", 140.0));
        }
        await Pa("A1", 1, "B", k: 1);
        for (var i = 1; i <= 26; i++) await Pitch("A1", i, "B", i == 26 ? "C" : "F");
        await Pa("A2", 2, "B", ab: 0, bb: 1); // A zero-pitch walk still consumes an official PA and encounter.
        await Pa("A3", 3, "B", official: 0, ab: 0); await Pitch("A3", 1, "B", "F");
        await Pa("A4", 4, "B", tb: 2); await Pitch("A4", 1, "B", "F"); await Pitch("A4", 2, "B", "C");
        await Pa("A5", 5, "C"); await Pitch("A5", 1, "C", "F", eventPresent: false); // Its global event order is unavailable.
        await Pa("A6", 6, "D", official: 0); await Pitch("A6", 1, "D", "F"); await Pitch("A6", 3, "D", "C"); // A gap must not become adjacency.
        await Pa("A7", 7, "E"); await Pitch("A7", 1, "E", "F"); await Pitch("A7", 2, "E", "S", "Q"); await Pitch("A7", 3, "E", "C"); // Neither side of a pitcher change links.
        await Insert("PitchingGameLines", ("GameId", "G1"), ("Pcode", "P"), ("TeamCode", "LG"), ("AppearanceSequence", 1), ("PitchCount", 34));
        var service = new AnalysisWebService(new DatabaseCacheService(path, true), new SiteOptions { QuerySeconds = 30 });
        var request = new AnalysisRequest(Role: "pitcher", Code: "P");
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS fixture " + message); }
        static long Total(AnalysisResult r, string table, string column) => r.Tables.Single(t => t.Key == table).Rows.Sum(x => Convert.ToInt64(x[column]));
        var times = await service.QueryAsync(request with { Section = "times" }, CancellationToken.None);
        Check(Total(times, "pitchBands", "Pitches") == 33, "missing event excluded from global pitch order");
        Check(Total(times, "pitchBands", "PA") == 3, "pitch bands exclude pitchless and missing-order terminal PA");
        Check(Total(times, "encounters", "PA") == 5, "encounters retain all official PA including zero-pitch walk");
        var firstBand = times.Tables.Single(t => t.Key == "pitchBands").Rows.Single(x => Convert.ToString(x["Group"]) == "1–25");
        Check(Convert.ToInt64(firstBand["Pitches"]) == 25 && Convert.ToInt64(firstBand["PA"]) == 0, "PA-local index does not reset global pitch bands");
        var secondEncounter = times.Tables.Single(t => t.Key == "encounters").Rows.Single(x => Convert.ToString(x["Group"]) == "2");
        Check(Convert.ToInt64(secondEncounter["PA"]) == 1 && Convert.ToInt64(secondEncounter["BB"]) == 1 && Convert.ToInt64(secondEncounter["Pitches"]) == 0, "zero-pitch walk has its own encounter denominator");
        var zones = await service.QueryAsync(request with { Section = "zones", PitchType = "F" }, CancellationToken.None);
        Check(Total(zones, "zones", "PA") == 1, "filtered last pitch cannot inherit later terminal outcome");
        var sequences = await service.QueryAsync(request with { Section = "sequences", PitchType = "C" }, CancellationToken.None);
        Check(Total(sequences, "sequences", "Pairs") == 2, "no sequences across pitch gap or pitcher substitution");
        var trend = await service.QueryAsync(request with { Section = "trend", PitchType = "F" }, CancellationToken.None);
        Check(Total(trend, "trend", "PA") == 1, "conditional PA filters use actual terminal pitch");
        var workload = await service.QueryAsync(request with { Section = "workload" }, CancellationToken.None);
        Check(Convert.ToString(workload.Tables.Single(t => t.Key == "daily").Rows.Single()["Role"]) == "선발", "appearance sequence 1 labels starter");
    }
}
