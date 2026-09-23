using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    internal async Task<IReadOnlyList<PitcherExtendedRecordRow>> QueryPitcherExtendedAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var teamExpression = "COALESCE(pa.FieldingTeamCode, CASE WHEN p.BattingSide=0 THEN g.HomeTeamCode ELSE g.AwayTeamCode END)";
        var identity = GetPitcherRecordRoomIdentity(
            query.Grouping,
            "p.PitcherPcode",
            "p.PitcherName",
            teamExpression,
            "g");
        var bucket = PitchBucketSql("p.PitchType");
        var accumulators = new Dictionary<string, PitcherExtendedAccumulator>(StringComparer.Ordinal);

        await using (var connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                {filter.Cte}
                SELECT {identity.SelectColumns},
                       {bucket} AS PitchBucket,
                       COUNT(*) AS PitchCount
                FROM Pitches p
                INNER JOIN FilteredGames g ON g.GameId=p.GameId
                LEFT JOIN PlateAppearances pa ON pa.PlateAppearanceId=p.PlateAppearanceId
                {identity.JoinClause}
                WHERE COALESCE(p.PitcherPcode,'')<>''
                  AND ($resultTeam='' OR {teamExpression}=$resultTeam)
                GROUP BY {identity.GroupBy}, PitchBucket;
                """;
            AddParameters(command, filter.Parameters);
            command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var pcode = reader.GetString(0);
                var name = reader.GetString(1);
                var team = reader.GetString(2);
                var pitchBucket = reader.GetString(3);
                var count = ReadInt32(reader, 4);
                var key = PitcherRecordRoomKey(pcode, team);
                if (!accumulators.TryGetValue(key, out var accumulator))
                {
                    accumulator = new PitcherExtendedAccumulator(pcode, name, team);
                    accumulators[key] = accumulator;
                }
                accumulator.PitchCounts[pitchBucket] = count;
            }
        }

        var tto = await QueryPitcherTtoAsync(query, cancellationToken).ConfigureAwait(false);
        var line = await QueryPitcherLineSummaryAsync(query, cancellationToken).ConfigureAwait(false);
        var rows = new List<PitcherExtendedRecordRow>();
        foreach (var (key, accumulator) in accumulators)
        {
            line.TryGetValue(key, out var summary);
            tto.TryGetValue(key, out var ttoSummary);
            var total = accumulator.PitchCounts.Values.Sum();
            var active = accumulator.PitchCounts.Values.Count(value => value > 0);
            var entropy = total > 0
                ? -accumulator.PitchCounts.Values.Where(value => value > 0)
                    .Select(value => value / (double)total)
                    .Sum(probability => probability * Math.Log(probability))
                : (double?)null;
            double? normalizedEntropy = entropy.HasValue && active > 1
                ? entropy.Value / Math.Log(active)
                : entropy.HasValue ? 0.0 : null;
            rows.Add(new PitcherExtendedRecordRow
            {
                Pcode = accumulator.Pcode,
                Name = accumulator.Name,
                TeamCode = accumulator.TeamCode,
                Games = summary?.Games ?? 0,
                ThreeTrueOutcomeRate = ttoSummary is { PlateAppearances: > 0 }
                    ? ttoSummary.ThreeTrueOutcomes / (double)ttoSummary.PlateAppearances
                    : null,
                PitchTypeCount = active,
                PitchEntropy = entropy,
                NormalizedPitchEntropy = normalizedEntropy,
                TwoSeamUsage = RecordRoomDivide(accumulator.Get("투심"), total),
                FourSeamUsage = RecordRoomDivide(accumulator.Get("포심"), total),
                CutterUsage = RecordRoomDivide(accumulator.Get("커터"), total),
                CurveUsage = RecordRoomDivide(accumulator.Get("커브"), total),
                SliderUsage = RecordRoomDivide(accumulator.Get("슬라이더"), total),
                ChangeupUsage = RecordRoomDivide(accumulator.Get("체인지업"), total),
                SinkerUsage = RecordRoomDivide(accumulator.Get("싱커"), total),
                ForkballUsage = RecordRoomDivide(accumulator.Get("포크"), total),
                KnuckleballUsage = RecordRoomDivide(accumulator.Get("너클"), total),
                OtherUsage = RecordRoomDivide(accumulator.Get("기타"), total),
            });
        }

        rows = rows.OrderByDescending(row => row.NormalizedPitchEntropy ?? double.MinValue)
            .ThenByDescending(row => row.Games)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    internal async Task<IReadOnlyList<PitcherWinProbabilityRecordRow>> QueryPitcherWinProbabilityAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var pcodeExpression = "COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode)";
        var nameExpression = "COALESCE(NULLIF(pa.FinalPitcherName,''),pa.PitcherName)";
        var identity = GetPitcherRecordRoomIdentity(
            query.Grouping,
            pcodeExpression,
            nameExpression,
            "pa.FieldingTeamCode",
            "g");
        var rows = new List<PitcherWinProbabilityRecordRow>();
        var gmLi = await QueryPitcherGmLiAsync(query, league, cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   COUNT(DISTINCT pa.GameId),
                   SUM(CASE WHEN -COALESCE(pa.WpaByPlate,0)>0 THEN -COALESCE(pa.WpaByPlate,0) ELSE 0 END),
                   SUM(CASE WHEN -COALESCE(pa.WpaByPlate,0)<0 THEN -COALESCE(pa.WpaByPlate,0) ELSE 0 END),
                   SUM(-COALESCE(pa.WpaByPlate,0)),
                   SUM(ABS(COALESCE(pa.WpaByPlate,0))),
                   SUM(CASE WHEN pa.WpaByPlate IS NOT NULL THEN 1 ELSE 0 END)
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            {identity.JoinClause}
            WHERE pa.IsOfficial=1
              AND COALESCE({pcodeExpression},'')<>''
              AND ($resultTeam='' OR pa.FieldingTeamCode=$resultTeam)
              {situation.Sql}
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pcode = reader.GetString(0);
            var name = reader.GetString(1);
            var team = reader.GetString(2);
            var games = ReadInt32(reader, 3);
            var positive = ReadDouble(reader, 4);
            var negative = ReadDouble(reader, 5);
            var wpa = ReadDouble(reader, 6);
            var absoluteWpa = ReadDouble(reader, 7);
            var wpaCount = ReadInt32(reader, 8);
            var pLi = wpaCount > 0 && league.AverageAbsoluteWpa > 0
                ? absoluteWpa / wpaCount / league.AverageAbsoluteWpa
                : (double?)null;
            gmLi.TryGetValue(PitcherRecordRoomKey(pcode, team), out var gmLiValue);
            rows.Add(new PitcherWinProbabilityRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                Games = games,
                AverageLeverageIndex = pLi,
                GmLi = gmLiValue,
                PositiveWpa = positive,
                NegativeWpa = negative,
                Wpa = wpa,
                WpaPerLi = pLi.HasValue && pLi.Value > 0 ? wpa / pLi.Value : null,
            });
        }

        rows = rows.OrderByDescending(row => row.Wpa ?? double.MinValue)
            .ThenByDescending(row => row.Games)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    internal async Task<IReadOnlyList<PitcherRunnerRecordRow>> QueryPitcherRunnerAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var pcodeExpression = "COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode)";
        var nameExpression = "COALESCE(NULLIF(pa.FinalPitcherName,''),pa.PitcherName)";
        var identity = GetPitcherRecordRoomIdentity(
            query.Grouping,
            pcodeExpression,
            nameExpression,
            "pa.FieldingTeamCode",
            "g");
        var line = await QueryPitcherLineSummaryAsync(query, cancellationToken).ConfigureAwait(false);
        var rowsByKey = new Dictionary<string, PitcherRunnerRecordRow>(StringComparer.Ordinal);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   SUM(CASE WHEN re.Reason=$stolenReason THEN 1 ELSE 0 END),
                   SUM(CASE WHEN re.Reason=$caughtReason OR re.EventType=$caughtEvent THEN 1 ELSE 0 END),
                   SUM(CASE WHEN re.Reason=$stolenReason AND re.ToBase=2 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN re.Reason=$stolenReason AND re.ToBase=3 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN re.Reason=$stolenReason AND re.ToBase=4 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN (re.Reason=$caughtReason OR re.EventType=$caughtEvent) AND re.ToBase=2 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN (re.Reason=$caughtReason OR re.EventType=$caughtEvent) AND re.ToBase=3 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN (re.Reason=$caughtReason OR re.EventType=$caughtEvent) AND re.ToBase=4 THEN 1 ELSE 0 END)
            FROM RunnerEvents re
            INNER JOIN PlateAppearances pa ON pa.PlateAppearanceId=re.PlateAppearanceId
            INNER JOIN FilteredGames g ON g.GameId=re.GameId
            {identity.JoinClause}
            WHERE COALESCE({pcodeExpression},'')<>''
              AND ($resultTeam='' OR pa.FieldingTeamCode=$resultTeam)
              {situation.Sql}
              AND (re.Reason IN ($stolenReason,$caughtReason) OR re.EventType=$caughtEvent)
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        command.Parameters.AddWithValue("$stolenReason", (int)RunnerAdvanceReason.StolenBase);
        command.Parameters.AddWithValue("$caughtReason", (int)RunnerAdvanceReason.CaughtStealing);
        command.Parameters.AddWithValue("$caughtEvent", (int)RunnerEventType.CaughtStealing);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pcode = reader.GetString(0);
            var name = reader.GetString(1);
            var team = reader.GetString(2);
            var stolen = ReadInt32(reader, 3);
            var caught = ReadInt32(reader, 4);
            line.TryGetValue(PitcherRecordRoomKey(pcode, team), out var summary);
            rowsByKey[PitcherRecordRoomKey(pcode, team)] = new PitcherRunnerRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                Games = summary?.Games ?? 0,
                InningsPitched = summary is null ? null : summary.InningsOuts / 3.0,
                StolenBases = stolen,
                CaughtStealing = caught,
                StolenBaseSuccessRate = RecordRoomDivide(stolen, stolen + caught),
                StolenBaseAttempts = stolen + caught,
                StolenSecond = ReadInt32(reader, 5),
                StolenThird = ReadInt32(reader, 6),
                StolenHome = ReadInt32(reader, 7),
                CaughtAtSecond = ReadInt32(reader, 8),
                CaughtAtThird = ReadInt32(reader, 9),
                CaughtAtHome = ReadInt32(reader, 10),
                WildPitches = summary?.WildPitches ?? 0,
            };
        }

        foreach (var (key, summary) in line)
        {
            if (rowsByKey.ContainsKey(key)) continue;
            rowsByKey[key] = new PitcherRunnerRecordRow
            {
                Pcode = summary.Pcode,
                Name = summary.Name,
                TeamCode = summary.TeamCode,
                Games = summary.Games,
                InningsPitched = summary.InningsOuts / 3.0,
                WildPitches = summary.WildPitches,
            };
        }

        var rows = rowsByKey.Values
            .OrderByDescending(row => row.StolenBaseAttempts)
            .ThenByDescending(row => row.InningsPitched ?? 0.0)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    internal async Task<IReadOnlyList<PitcherStarterRecordRow>> QueryPitcherStarterAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var identity = GetPitcherRecordRoomIdentity(query.Grouping, "pg.Pcode", "pg.Name", "pg.TeamCode", "g");
        var rows = new List<PitcherStarterRecordRow>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   COUNT(DISTINCT pg.GameId),
                   SUM(pg.IsStarter),
                   SUM(CASE WHEN pg.IsStarter=1 THEN pg.InningsOuts ELSE 0 END),
                   SUM(CASE WHEN pg.IsStarter=1 THEN pg.EarnedRuns ELSE 0 END),
                   SUM(CASE WHEN pg.IsStarter=1 AND pg.InningsOuts>=18 AND pg.EarnedRuns<=3 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pg.IsStarter=1 AND pg.InningsOuts>=21 AND pg.EarnedRuns<=3 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pg.IsStarter=1 THEN CASE WHEN g.HomeTeamCode=pg.TeamCode THEN COALESCE(g.HomeScore,0) ELSE COALESCE(g.AwayScore,0) END ELSE 0 END),
                   SUM(CASE WHEN pg.IsStarter=1 AND ((g.Winner='HOME' AND g.HomeTeamCode=pg.TeamCode) OR (g.Winner='AWAY' AND g.AwayTeamCode=pg.TeamCode)) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pg.IsStarter=1 AND ((g.Winner='HOME' AND g.AwayTeamCode=pg.TeamCode) OR (g.Winner='AWAY' AND g.HomeTeamCode=pg.TeamCode)) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pg.IsStarter=1 THEN pg.FinalPitchCount ELSE 0 END)
            FROM PitcherGameStats pg
            INNER JOIN FilteredGames g ON g.GameId=pg.GameId
            {identity.JoinClause}
            WHERE ($resultTeam='' OR pg.TeamCode=$resultTeam)
            GROUP BY {identity.GroupBy}
            HAVING SUM(pg.IsStarter)>0;
            """;
        AddParameters(command, filter.Parameters);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pcode = reader.GetString(0);
            var name = reader.GetString(1);
            var team = reader.GetString(2);
            var games = ReadInt32(reader, 3);
            var starts = ReadInt32(reader, 4);
            var outs = ReadInt32(reader, 5);
            var er = ReadInt32(reader, 6);
            var qs = ReadInt32(reader, 7);
            var qsPlus = ReadInt32(reader, 8);
            var runSupport = ReadInt32(reader, 9);
            var teamWins = ReadInt32(reader, 10);
            var teamLosses = ReadInt32(reader, 11);
            var pitches = ReadInt32(reader, 12);
            var innings = outs / 3.0;
            rows.Add(new PitcherStarterRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                Games = games,
                GamesStarted = starts,
                InningsPitched = innings,
                ERA = innings > 0 ? er * 9.0 / innings : null,
                QualityStarts = qs,
                QualityStartRate = RecordRoomDivide(qs, starts),
                QualityStartsPlus = qsPlus,
                QualityStartPlusRate = RecordRoomDivide(qsPlus, starts),
                RunSupport = runSupport,
                RunSupportPerNine = innings > 0 ? runSupport * 9.0 / innings : null,
                TeamWins = teamWins,
                TeamLosses = teamLosses,
                TeamWinRate = RecordRoomDivide(teamWins, teamWins + teamLosses),
                InningsPerStart = RecordRoomDivide(innings, starts),
                PitchesPerStart = RecordRoomDivide(pitches, starts),
            });
        }

        rows = rows.OrderByDescending(row => row.QualityStartRate ?? double.MinValue)
            .ThenByDescending(row => row.InningsPitched ?? 0.0)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    internal async Task<IReadOnlyList<PitcherRelieverRecordRow>> QueryPitcherRelieverAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var identity = GetPitcherRecordRoomIdentity(query.Grouping, "pg.Pcode", "pg.Name", "pg.TeamCode", "g");
        var rows = new List<PitcherRelieverRecordRow>();
        var byPitcher = await QueryReliefDatesByPitcherAsync(query, cancellationToken).ConfigureAwait(false);
        var streakTotals = BuildReliefStreakTotals(byPitcher, query.Grouping);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   COUNT(DISTINCT pg.GameId),
                   SUM(pg.IsReliever),
                   SUM(CASE WHEN pg.IsReliever=1 THEN pg.InningsOuts ELSE 0 END),
                   SUM(CASE WHEN pg.IsReliever=1 THEN pg.EarnedRuns ELSE 0 END),
                   SUM(CASE WHEN pg.IsReliever=1 THEN pg.FinalPitchCount ELSE 0 END),
                   SUM(CASE WHEN pg.IsReliever=1 AND pg.InningsOuts>=3 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pg.IsReliever=1 THEN pg.EntryAbsoluteWpaSum ELSE 0 END),
                   SUM(CASE WHEN pg.IsReliever=1 THEN pg.EntryWpaCount ELSE 0 END)
            FROM PitcherGameStats pg
            INNER JOIN FilteredGames g ON g.GameId=pg.GameId
            {identity.JoinClause}
            WHERE ($resultTeam='' OR pg.TeamCode=$resultTeam)
            GROUP BY {identity.GroupBy}
            HAVING SUM(pg.IsReliever)>0;
            """;
        AddParameters(command, filter.Parameters);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pcode = reader.GetString(0);
            var name = reader.GetString(1);
            var team = reader.GetString(2);
            var games = ReadInt32(reader, 3);
            var reliefGames = ReadInt32(reader, 4);
            var outs = ReadInt32(reader, 5);
            var er = ReadInt32(reader, 6);
            var pitches = ReadInt32(reader, 7);
            var onePlus = ReadInt32(reader, 8);
            var entryAbs = ReadDouble(reader, 9);
            var entryCount = ReadInt32(reader, 10);
            var innings = outs / 3.0;
            var streakKey = query.Grouping switch
            {
                AnalyticsGrouping.Team => team,
                AnalyticsGrouping.PlayerCareer => pcode,
                _ => PitcherRecordRoomKey(pcode, team),
            };
            streakTotals.TryGetValue(streakKey, out var streaks);
            rows.Add(new PitcherRelieverRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                Games = games,
                ReliefGames = reliefGames,
                InningsPitched = innings,
                ERA = innings > 0 ? er * 9.0 / innings : null,
                BackToBackAppearances = streaks.Two,
                ThreeDayStreaks = streaks.Three,
                FourDayStreaks = streaks.Four,
                OnePlusInningGames = onePlus,
                InningsPerReliefGame = RecordRoomDivide(innings, reliefGames),
                PitchesPerReliefGame = RecordRoomDivide(pitches, reliefGames),
                GmLi = entryCount > 0 && league.AverageAbsoluteWpa > 0
                    ? Math.Clamp((entryAbs / entryCount) / league.AverageAbsoluteWpa, 0.1, 5.0)
                    : 1.0,
            });
        }

        rows = rows.OrderByDescending(row => row.GmLi ?? 0.0)
            .ThenByDescending(row => row.ReliefGames)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    private async Task<Dictionary<string, PitcherTtoSummary>> QueryPitcherTtoAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var pcodeExpression = "COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode)";
        var nameExpression = "COALESCE(NULLIF(pa.FinalPitcherName,''),pa.PitcherName)";
        var identity = GetPitcherRecordRoomIdentity(query.Grouping, pcodeExpression, nameExpression, "pa.FieldingTeamCode", "g");
        var result = new Dictionary<string, PitcherTtoSummary>(StringComparer.Ordinal);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   COUNT(*),
                   SUM(CASE WHEN pa.ResultType IN (6,7,8,9,10) THEN 1 ELSE 0 END)
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            {identity.JoinClause}
            WHERE pa.IsOfficial=1
              AND COALESCE({pcodeExpression},'')<>''
              AND ($resultTeam='' OR pa.FieldingTeamCode=$resultTeam)
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pcode = reader.GetString(0);
            var team = reader.GetString(2);
            result[PitcherRecordRoomKey(pcode, team)] = new PitcherTtoSummary(ReadInt32(reader, 3), ReadInt32(reader, 4));
        }
        return result;
    }

    private async Task<Dictionary<string, double?>> QueryPitcherGmLiAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var identity = GetPitcherRecordRoomIdentity(query.Grouping, "pg.Pcode", "pg.Name", "pg.TeamCode", "g");
        var result = new Dictionary<string, double?>(StringComparer.Ordinal);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns}, SUM(pg.EntryAbsoluteWpaSum), SUM(pg.EntryWpaCount)
            FROM PitcherGameStats pg
            INNER JOIN FilteredGames g ON g.GameId=pg.GameId
            {identity.JoinClause}
            WHERE ($resultTeam='' OR pg.TeamCode=$resultTeam)
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pcode = reader.GetString(0);
            var team = reader.GetString(2);
            var sum = ReadDouble(reader, 3);
            var count = ReadInt32(reader, 4);
            result[PitcherRecordRoomKey(pcode, team)] = count > 0 && league.AverageAbsoluteWpa > 0
                ? Math.Clamp((sum / count) / league.AverageAbsoluteWpa, 0.1, 5.0)
                : 1.0;
        }
        return result;
    }

    private async Task<Dictionary<string, PitcherLineSummary>> QueryPitcherLineSummaryAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var identity = GetPitcherRecordRoomIdentity(query.Grouping, "pg.Pcode", "pg.Name", "pg.TeamCode", "g");
        var result = new Dictionary<string, PitcherLineSummary>(StringComparer.Ordinal);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   COUNT(DISTINCT pg.GameId), SUM(pg.InningsOuts), SUM(pg.WildPitches)
            FROM PitcherGameStats pg
            INNER JOIN FilteredGames g ON g.GameId=pg.GameId
            {identity.JoinClause}
            WHERE ($resultTeam='' OR pg.TeamCode=$resultTeam)
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pcode = reader.GetString(0);
            var name = reader.GetString(1);
            var team = reader.GetString(2);
            result[PitcherRecordRoomKey(pcode, team)] = new PitcherLineSummary(
                pcode, name, team, ReadInt32(reader, 3), ReadInt32(reader, 4), ReadInt32(reader, 5));
        }
        return result;
    }

    // "연투"는 반드시 같은 투수가 하루도 쉬지 않고 연속 등판했을 때만 성립합니다.
    // 팀 단위(AnalyticsGrouping.Team) 집계에서 예전에는 pg.Pcode를 빼고 팀 코드로만
    // 묶어서 날짜열을 만들었기 때문에, 서로 다른 불펜 투수가 각각 하루씩만 등판해도
    // "팀 전체로 보면 이틀 연속 누군가 던졌다"는 이유로 연투로 잘못 집계됐습니다
    // (팀은 시즌 내내 거의 매일 구원 등판이 있으므로 연투 수가 크게 부풀려짐).
    // 항상 투수별(Pcode)로 날짜를 모아 스트릭을 계산한 뒤, 화면에 맞는 단위
    // (팀/선수 통산/개인·팀별)로 합산합니다.
    private async Task<Dictionary<string, List<(DateTime Date, string Team)>>> QueryReliefDatesByPitcherAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var result = new Dictionary<string, List<(DateTime Date, string Team)>>(StringComparer.Ordinal);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT pg.Pcode, pg.TeamCode, g.GameDate
            FROM PitcherGameStats pg
            INNER JOIN FilteredGames g ON g.GameId=pg.GameId
            WHERE pg.IsReliever=1
              AND ($resultTeam='' OR pg.TeamCode=$resultTeam)
              AND g.GameDate IS NOT NULL
            GROUP BY pg.Pcode, pg.TeamCode, g.GameDate
            ORDER BY pg.Pcode, g.GameDate;
            """;
        AddParameters(command, filter.Parameters);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pcode = reader.GetString(0);
            var team = reader.GetString(1);
            if (!DateTime.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (!result.TryGetValue(pcode, out var list)) result[pcode] = list = new List<(DateTime, string)>();
            list.Add((date.Date, team));
        }
        return result;
    }

    // 투수별 등판일로 스트릭을 센 뒤, 화면 단위(팀/선수 통산/개인+팀)에 맞는 키로 합산합니다.
    private static Dictionary<string, (int Two, int Three, int Four)> BuildReliefStreakTotals(
        Dictionary<string, List<(DateTime Date, string Team)>> byPitcher,
        AnalyticsGrouping grouping)
    {
        var totals = new Dictionary<string, (int Two, int Three, int Four)>(StringComparer.Ordinal);
        void Add(string key, bool three, bool four)
        {
            totals.TryGetValue(key, out var current);
            totals[key] = (current.Two + 1, current.Three + (three ? 1 : 0), current.Four + (four ? 1 : 0));
        }
        foreach (var (pcode, appearances) in byPitcher)
        {
            var dates = appearances
                .GroupBy(x => x.Date)
                .Select(g => (Date: g.Key, Team: g.First().Team))
                .OrderBy(x => x.Date)
                .ToList();
            for (var index = 1; index < dates.Count; index++)
            {
                if ((dates[index].Date - dates[index - 1].Date).TotalDays != 1) continue;
                var isThree = index >= 2 && (dates[index - 1].Date - dates[index - 2].Date).TotalDays == 1;
                var isFour = isThree && index >= 3 && (dates[index - 2].Date - dates[index - 3].Date).TotalDays == 1;
                var key = grouping switch
                {
                    AnalyticsGrouping.Team => dates[index].Team,
                    AnalyticsGrouping.PlayerCareer => pcode,
                    _ => PitcherRecordRoomKey(pcode, dates[index].Team),
                };
                Add(key, isThree, isFour);
            }
        }
        return totals;
    }

    private static RecordRoomIdentitySql GetPitcherRecordRoomIdentity(
        AnalyticsGrouping grouping,
        string pcodeExpression,
        string nameExpression,
        string teamExpression,
        string gameAlias) => grouping switch
    {
        AnalyticsGrouping.PlayerCareer => new RecordRoomIdentitySql(
            $"{pcodeExpression}, MAX(COALESCE({nameExpression},'')), CASE WHEN $resultTeam<>'' THEN $resultTeam ELSE COALESCE(MAX(profile.LatestTeam),'') END",
            $"LEFT JOIN Players profile ON profile.Pcode={pcodeExpression}",
            pcodeExpression),
        AnalyticsGrouping.Team => new RecordRoomIdentitySql(
            $"{teamExpression}, MAX(CASE WHEN {gameAlias}.HomeTeamCode={teamExpression} THEN COALESCE(NULLIF({gameAlias}.HomeTeamName,''),{teamExpression}) ELSE COALESCE(NULLIF({gameAlias}.AwayTeamName,''),{teamExpression}) END), {teamExpression}",
            string.Empty,
            teamExpression),
        _ => new RecordRoomIdentitySql(
            $"{pcodeExpression}, MAX(COALESCE({nameExpression},'')), {teamExpression}",
            string.Empty,
            $"{pcodeExpression}, {teamExpression}"),
    };

    private static string PitchBucketSql(string expression) => $"""
        CASE
            WHEN {expression} LIKE '%투심%' THEN '투심'
            WHEN {expression} LIKE '%포심%' OR {expression} LIKE '%직구%' OR LOWER(COALESCE({expression},'')) LIKE '%four%' OR LOWER(COALESCE({expression},'')) LIKE '%4-seam%' THEN '포심'
            WHEN {expression} LIKE '%커터%' OR {expression} LIKE '%컷%' THEN '커터'
            WHEN {expression} LIKE '%커브%' THEN '커브'
            WHEN {expression} LIKE '%슬라이더%' OR {expression} LIKE '%슬라%' THEN '슬라이더'
            WHEN {expression} LIKE '%체인지%' THEN '체인지업'
            WHEN {expression} LIKE '%싱커%' THEN '싱커'
            WHEN {expression} LIKE '%포크%' OR {expression} LIKE '%스플리터%' OR {expression} LIKE '%스플릿%' THEN '포크'
            WHEN {expression} LIKE '%너클%' THEN '너클'
            ELSE '기타'
        END
        """;

    private static string PitcherRecordRoomKey(string? pcode, string? teamCode) =>
        $"{pcode ?? string.Empty}|{teamCode ?? string.Empty}";

    private static double? RecordRoomDivide(double numerator, double denominator) =>
        Math.Abs(denominator) < 0.0000001 ? null : numerator / denominator;

    private sealed class PitcherExtendedAccumulator
    {
        public PitcherExtendedAccumulator(string pcode, string name, string teamCode)
        {
            Pcode = pcode;
            Name = name;
            TeamCode = teamCode;
        }
        public string Pcode { get; }
        public string Name { get; }
        public string TeamCode { get; }
        public Dictionary<string, int> PitchCounts { get; } = new(StringComparer.Ordinal);
        public int Get(string pitchType) => PitchCounts.GetValueOrDefault(pitchType);
    }

    private sealed record PitcherTtoSummary(int PlateAppearances, int ThreeTrueOutcomes);
    private sealed record PitcherLineSummary(
        string Pcode,
        string Name,
        string TeamCode,
        int Games,
        int InningsOuts,
        int WildPitches);
}
