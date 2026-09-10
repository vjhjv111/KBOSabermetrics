using Microsoft.Data.Sqlite;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    /// <summary>
    /// WAR 공식 자체를 변경하지 않고, DB에 적재된 각 정규시즌의 대체선수 풀과 분포를 관찰하기 위한 진단 전용 조회입니다.
    /// 결과를 CSV로 내보내 KBO WAR 정책을 재보정할 때 사용합니다.
    /// </summary>
    public async Task<PitcherWarDiagnosticBundle> GetPitcherWarDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        var global = await GetLeagueReferenceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var seasonTotals = await ReadDiagnosticSeasonTotalsAsync(connection, cancellationToken).ConfigureAwait(false);
        var rawLines = await ReadDiagnosticRoleLinesAsync(connection, global.ParkFactors, cancellationToken).ConfigureAwait(false);

        var candidates = new List<PitcherReplacementCandidateRow>();
        var seasonState = new Dictionary<int, DiagnosticSeasonState>();

        foreach (var totals in seasonTotals.OrderBy(row => row.Year))
        {
            if (totals.Innings <= 0) continue;
            var leagueEra = totals.EarnedRuns * 9.0 / totals.Innings;
            var leagueRa9 = totals.RunsAllowed * 9.0 / totals.Innings;
            var core = (13.0 * totals.HomeRuns + 3.0 * (totals.Walks + totals.HitBatters) -
                        2.0 * (totals.Strikeouts + totals.InfieldFlies)) / totals.Innings;
            var ifFipConstant = leagueEra - core;
            var ra9Adjustment = leagueRa9 - leagueEra;
            var leagueFipR9 = leagueEra + ra9Adjustment;
            var averageAbsWpa = totals.WpaCount > 0 ? totals.WpaAbsSum / totals.WpaCount : global.AverageAbsoluteWpa;
            if (averageAbsWpa <= 0) averageAbsWpa = 1.0;

            var lines = rawLines.Where(line => line.Year == totals.Year).ToList();
            foreach (var line in lines)
            {
                if (line.Innings <= 0) continue;
                line.ParkFactor = line.InningsOuts > 0 ? line.WeightedParkFactorOuts / line.InningsOuts : 100.0;
                var ifFip = (13.0 * line.HomeRuns + 3.0 * (line.Walks + line.HitBatters) -
                             2.0 * (line.Strikeouts + line.InfieldFlies)) / line.Innings + ifFipConstant;
                var fipR9 = ifFip + ra9Adjustment;
                line.ParkAdjustedFipR9 = KboPitcherWarMath.ParkAdjust(fipR9, line.ParkFactor);
                var ra9 = line.RunsAllowed * 9.0 / line.Innings;
                line.ParkAdjustedRa9 = KboPitcherWarMath.ParkAdjust(ra9, line.ParkFactor);
                line.GmLi = !line.IsStarter && line.WpaCount > 0 && averageAbsWpa > 0
                    ? Math.Clamp((line.WpaAbsSum / line.WpaCount) / averageAbsWpa, 0.1, 5.0)
                    : 1.0;
            }

            var starterPool = SelectDiagnosticReplacementPool(lines.Where(line => line.IsStarter).ToList(), true);
            var relieverPool = SelectDiagnosticReplacementPool(lines.Where(line => !line.IsStarter).ToList(), false);
            var starterRows = BuildDiagnosticCandidates(totals.Year, starterPool, leagueFipR9, leagueRa9, true);
            var relieverRows = BuildDiagnosticCandidates(totals.Year, relieverPool, leagueFipR9, leagueRa9, false);
            candidates.AddRange(starterRows);
            candidates.AddRange(relieverRows);

            var starterEmpiricalFip = EstimateDiagnosticRate(starterPool, x => x.ParkAdjustedFipR9, leagueFipR9, 40.0);
            var relieverEmpiricalFip = EstimateDiagnosticRate(relieverPool, x => x.ParkAdjustedFipR9, leagueFipR9, 20.0);
            var starterEmpiricalRa9 = EstimateDiagnosticRate(starterPool, x => x.ParkAdjustedRa9, leagueRa9, 50.0);
            var relieverEmpiricalRa9 = EstimateDiagnosticRate(relieverPool, x => x.ParkAdjustedRa9, leagueRa9, 25.0);

            var fipRpw = Math.Max(1.0, (leagueFipR9 + 2.0) * 1.5);
            var ra9Rpw = Math.Max(1.0, (leagueRa9 + 2.0) * 1.5);
            var starterFipFloor = leagueFipR9 + 0.12 * fipRpw;
            var relieverFipFloor = leagueFipR9 + 0.03 * fipRpw;
            var starterRa9Floor = leagueRa9 + 0.12 * ra9Rpw;
            var relieverRa9Floor = leagueRa9 + 0.03 * ra9Rpw;

            var starterReplacementFip = ClampDiagnosticReplacementRate(Math.Max(starterEmpiricalFip, starterFipFloor), leagueFipR9);
            var relieverReplacementFip = ClampDiagnosticReplacementRate(Math.Max(relieverEmpiricalFip, relieverFipFloor), leagueFipR9);
            var starterReplacementRa9 = ClampDiagnosticReplacementRate(Math.Max(starterEmpiricalRa9, starterRa9Floor), leagueRa9);
            var relieverReplacementRa9 = ClampDiagnosticReplacementRate(Math.Max(relieverEmpiricalRa9, relieverRa9Floor), leagueRa9);

            var preFipWar = lines.Sum(line => ComputeDiagnosticRoleWar(
                line, line.ParkAdjustedFipR9, leagueFipR9,
                line.IsStarter ? starterReplacementFip : relieverReplacementFip));
            var preRa9War = lines.Sum(line => ComputeDiagnosticRoleWar(
                line, line.ParkAdjustedRa9, leagueRa9,
                line.IsStarter ? starterReplacementRa9 : relieverReplacementRa9));
            var target = KboPitcherWarMath.ComputeTargetPitcherWar(totals.Games);
            var warIp = totals.Innings > 0 ? (target - preFipWar) / totals.Innings : 0.0;
            var ra9WarIp = totals.Innings > 0 ? (target - preRa9War) / totals.Innings : 0.0;

            seasonState[totals.Year] = new DiagnosticSeasonState
            {
                Totals = totals,
                LeagueEra = leagueEra,
                LeagueRa9 = leagueRa9,
                LeagueFipR9 = leagueFipR9,
                StarterRows = starterRows,
                RelieverRows = relieverRows,
                StarterRoleLines = lines.Count(x => x.IsStarter),
                RelieverRoleLines = lines.Count(x => !x.IsStarter),
                StarterFgFloorFipMinus = Percent(starterFipFloor, leagueFipR9),
                RelieverFgFloorFipMinus = Percent(relieverFipFloor, leagueFipR9),
                StarterReplacementFipMinus = Percent(starterReplacementFip, leagueFipR9),
                RelieverReplacementFipMinus = Percent(relieverReplacementFip, leagueFipR9),
                StarterReplacementRa9 = starterReplacementRa9,
                RelieverReplacementRa9 = relieverReplacementRa9,
                TargetWar = target,
                PreFipWar = preFipWar,
                FipWarIp = warIp,
                PreRa9War = preRa9War,
                Ra9WarIp = ra9WarIp,
            };
        }

        var summaries = new List<PitcherWarDiagnosticSeasonRow>();
        foreach (var year in seasonState.Keys.OrderBy(y => y))
        {
            var s = seasonState[year];
            var sp = s.StarterRows;
            var rp = s.RelieverRows;
            summaries.Add(new PitcherWarDiagnosticSeasonRow
            {
                Year = year,
                Games = s.Totals.Games,
                LeagueInnings = s.Totals.Innings,
                LeagueEra = s.LeagueEra,
                LeagueRa9 = s.LeagueRa9,
                LeagueFipR9 = s.LeagueFipR9,

                StarterRoleLines = s.StarterRoleLines,
                StarterCandidates = sp.Count,
                StarterPoolMaxInnings = sp.Count > 0 ? sp.Max(x => x.Innings) : null,
                StarterCandidateInnings = sp.Sum(x => x.Innings),
                StarterObservedMeanFipMinus = WeightedMean(sp, x => x.ObservedFipMinus, x => Math.Min(x.Innings, x.RegressionInnings)),
                StarterRegressedMeanFipMinus = WeightedMean(sp, x => x.RegressedFipMinus, x => Math.Min(x.Innings, x.RegressionInnings)),
                StarterP25FipMinus = Percentile(sp.Select(x => x.RegressedFipMinus), 0.25),
                StarterP50FipMinus = Percentile(sp.Select(x => x.RegressedFipMinus), 0.50),
                StarterP75FipMinus = Percentile(sp.Select(x => x.RegressedFipMinus), 0.75),
                StarterP90FipMinus = Percentile(sp.Select(x => x.RegressedFipMinus), 0.90),
                StarterRolling3P75FipMinus = RollingPercentile(candidates, year, "SP", 3, 0.75),
                StarterRolling5P75FipMinus = RollingPercentile(candidates, year, "SP", 5, 0.75),
                StarterFgFloorFipMinus = s.StarterFgFloorFipMinus,
                StarterDiagnosticReplacementFipMinus = s.StarterReplacementFipMinus,

                RelieverRoleLines = s.RelieverRoleLines,
                RelieverCandidates = rp.Count,
                RelieverPoolMaxInnings = rp.Count > 0 ? rp.Max(x => x.Innings) : null,
                RelieverCandidateInnings = rp.Sum(x => x.Innings),
                RelieverObservedMeanFipMinus = WeightedMean(rp, x => x.ObservedFipMinus, x => Math.Min(x.Innings, x.RegressionInnings)),
                RelieverRegressedMeanFipMinus = WeightedMean(rp, x => x.RegressedFipMinus, x => Math.Min(x.Innings, x.RegressionInnings)),
                RelieverP25FipMinus = Percentile(rp.Select(x => x.RegressedFipMinus), 0.25),
                RelieverP50FipMinus = Percentile(rp.Select(x => x.RegressedFipMinus), 0.50),
                RelieverP75FipMinus = Percentile(rp.Select(x => x.RegressedFipMinus), 0.75),
                RelieverP90FipMinus = Percentile(rp.Select(x => x.RegressedFipMinus), 0.90),
                RelieverRolling3P75FipMinus = RollingPercentile(candidates, year, "RP", 3, 0.75),
                RelieverRolling5P75FipMinus = RollingPercentile(candidates, year, "RP", 5, 0.75),
                RelieverFgFloorFipMinus = s.RelieverFgFloorFipMinus,
                RelieverDiagnosticReplacementFipMinus = s.RelieverReplacementFipMinus,

                StarterP75Ra9 = Percentile(sp.Select(x => x.RegressedRa9), 0.75),
                RelieverP75Ra9 = Percentile(rp.Select(x => x.RegressedRa9), 0.75),
                StarterDiagnosticReplacementRa9 = s.StarterReplacementRa9,
                RelieverDiagnosticReplacementRa9 = s.RelieverReplacementRa9,

                TargetPitcherWar = s.TargetWar,
                PreCorrectionFipWar = s.PreFipWar,
                FipWarTargetRatio = s.TargetWar > 0 ? s.PreFipWar / s.TargetWar : null,
                FipWarPerInning = s.FipWarIp,
                FinalFipWar = s.PreFipWar + s.FipWarIp * s.Totals.Innings,
                PreCorrectionRa9War = s.PreRa9War,
                Ra9WarTargetRatio = s.TargetWar > 0 ? s.PreRa9War / s.TargetWar : null,
                Ra9WarPerInning = s.Ra9WarIp,
                FinalRa9War = s.PreRa9War + s.Ra9WarIp * s.Totals.Innings,
                CurrentV3StarterReplacementFipMinus = global.PitcherWar.StarterReplacementFipMinus,
                CurrentV3RelieverReplacementFipMinus = global.PitcherWar.RelieverReplacementFipMinus,
            });
        }

        return new PitcherWarDiagnosticBundle
        {
            Seasons = summaries,
            Candidates = candidates
                .OrderByDescending(x => x.Year)
                .ThenBy(x => x.Role)
                .ThenByDescending(x => x.RegressedFipMinus)
                .ToList(),
        };
    }

    private static IReadOnlyList<PitcherReplacementCandidateRow> BuildDiagnosticCandidates(
        int year,
        IReadOnlyList<DiagnosticRoleLine> pool,
        double leagueFipR9,
        double leagueRa9,
        bool starter)
    {
        var fipRegression = starter ? 40.0 : 20.0;
        var ra9Regression = starter ? 50.0 : 25.0;
        return pool.Select(line =>
        {
            var fipReliability = line.Innings / (line.Innings + fipRegression);
            var regressedFip = leagueFipR9 + fipReliability * (line.ParkAdjustedFipR9 - leagueFipR9);
            var ra9Reliability = line.Innings / (line.Innings + ra9Regression);
            var regressedRa9 = leagueRa9 + ra9Reliability * (line.ParkAdjustedRa9 - leagueRa9);
            return new PitcherReplacementCandidateRow
            {
                Year = year,
                Role = starter ? "SP" : "RP",
                Pcode = line.Pcode,
                Name = line.Name,
                TeamCodes = string.Join("/", line.TeamCodes.OrderBy(x => x, StringComparer.Ordinal)),
                Games = line.Games,
                Innings = line.Innings,
                ParkFactor = line.ParkFactor,
                ParkAdjustedFipR9 = line.ParkAdjustedFipR9,
                ObservedFipMinus = Percent(line.ParkAdjustedFipR9, leagueFipR9),
                RegressedFipMinus = Percent(regressedFip, leagueFipR9),
                ParkAdjustedRa9 = line.ParkAdjustedRa9,
                RegressedRa9 = regressedRa9,
                GmLi = line.GmLi,
                RegressionInnings = fipRegression,
            };
        }).ToList();
    }

    private static List<DiagnosticRoleLine> SelectDiagnosticReplacementPool(
        IReadOnlyList<DiagnosticRoleLine> roleLines,
        bool starter)
    {
        if (roleLines.Count == 0) return new List<DiagnosticRoleLine>();
        var minimumInnings = starter ? 9.0 : 5.0;
        var eligible = roleLines.Where(line => line.Innings >= minimumInnings).ToList();
        if (eligible.Count == 0) eligible = roleLines.Where(line => line.Innings > 0).ToList();
        if (eligible.Count == 0) return new List<DiagnosticRoleLine>();
        var ordered = eligible.OrderBy(line => line.Innings).ToList();
        var percentileIndex = Math.Clamp((int)Math.Floor((ordered.Count - 1) * 0.40), 0, ordered.Count - 1);
        var percentileInnings = ordered[percentileIndex].Innings;
        var hardCap = starter ? 50.0 : 30.0;
        var threshold = Math.Min(hardCap, Math.Max(minimumInnings, percentileInnings));
        var candidates = ordered.Where(line => line.Innings <= threshold).ToList();
        var desiredCount = Math.Min(ordered.Count, Math.Max(8, (int)Math.Ceiling(ordered.Count * 0.35)));
        if (candidates.Count < desiredCount) candidates = ordered.Take(desiredCount).ToList();
        return candidates;
    }

    private static double EstimateDiagnosticRate(
        IReadOnlyList<DiagnosticRoleLine> pool,
        Func<DiagnosticRoleLine, double> selector,
        double leagueRate,
        double regressionInnings)
    {
        if (pool.Count == 0) return leagueRate;
        double numerator = 0, denominator = 0;
        foreach (var line in pool)
        {
            if (line.Innings <= 0) continue;
            var observed = selector(line);
            if (observed <= 0 || double.IsNaN(observed) || double.IsInfinity(observed)) continue;
            var reliability = line.Innings / (line.Innings + regressionInnings);
            var regressed = leagueRate + reliability * (observed - leagueRate);
            var weight = Math.Min(line.Innings, regressionInnings);
            numerator += regressed * weight;
            denominator += weight;
        }
        return denominator > 0 ? numerator / denominator : leagueRate;
    }

    private static double ComputeDiagnosticRoleWar(
        DiagnosticRoleLine line,
        double pitcherRunRate,
        double leagueRunRate,
        double replacementRunRate)
    {
        if (line.Innings <= 0) return 0.0;
        var rpw = KboPitcherWarMath.DynamicRunsPerWin(leagueRunRate, pitcherRunRate, line.Innings, line.Games);
        var leverage = KboPitcherWarMath.LeverageMultiplier(line.GmLi, !line.IsStarter);
        return KboPitcherWarMath.WinsAboveAverage(leagueRunRate, pitcherRunRate, line.Innings, rpw, leverage) +
               KboPitcherWarMath.ReplacementWins(replacementRunRate, leagueRunRate, line.Innings, rpw, leverage);
    }

    private static double ClampDiagnosticReplacementRate(double value, double leagueRate)
    {
        var minimum = Math.Max(0.01, leagueRate * 1.01);
        var maximum = Math.Max(minimum, leagueRate + 3.50);
        return Math.Clamp(value, minimum, maximum);
    }

    private static double Percent(double value, double baseline) => baseline > 0 ? 100.0 * value / baseline : 0.0;

    private static double? WeightedMean<T>(IReadOnlyList<T> rows, Func<T, double> value, Func<T, double> weight)
    {
        if (rows.Count == 0) return null;
        double sum = 0, weights = 0;
        foreach (var row in rows)
        {
            var w = Math.Max(0, weight(row));
            sum += value(row) * w;
            weights += w;
        }
        return weights > 0 ? sum / weights : null;
    }

    private static double? Percentile(IEnumerable<double> values, double percentile)
    {
        var list = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToList();
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0];
        var position = Math.Clamp(percentile, 0, 1) * (list.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return list[lower];
        var fraction = position - lower;
        return list[lower] + (list[upper] - list[lower]) * fraction;
    }

    private static double? RollingPercentile(
        IReadOnlyList<PitcherReplacementCandidateRow> rows,
        int year,
        string role,
        int seasons,
        double percentile) => Percentile(
            rows.Where(row => row.Role == role && row.Year <= year && row.Year >= year - seasons + 1)
                .Select(row => row.RegressedFipMinus),
            percentile);

    private async Task<List<DiagnosticSeasonTotals>> ReadDiagnosticSeasonTotalsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<DiagnosticSeasonTotals>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(g.SeasonYear,0), COUNT(DISTINCT g.GameId),
                   COALESCE(SUM(p.InningsOuts),0), COALESCE(SUM(p.EarnedRuns),0),
                   COALESCE(SUM(p.RunsAllowed),0), COALESCE(SUM(p.HomeRunsAllowed),0),
                   COALESCE(SUM(p.FinalBB),0), COALESCE(SUM(p.FinalHBP),0),
                   COALESCE(SUM(p.FinalSO),0), COALESCE(SUM(p.IFFB),0),
                   COALESCE(SUM(p.EntryAbsoluteWpaSum),0), COALESCE(SUM(p.EntryWpaCount),0)
            FROM PitcherGameStats p
            INNER JOIN Games g ON g.GameId=p.GameId
            WHERE p.HasFinalLine=1 AND p.InningsOuts>0
              AND (p.IsStarter=1 OR p.IsReliever=1)
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
              AND COALESCE(g.SeasonYear,0)>0
            GROUP BY COALESCE(g.SeasonYear,0)
            ORDER BY COALESCE(g.SeasonYear,0);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new DiagnosticSeasonTotals
            {
                Year = ReadInt32(reader, 0), Games = ReadInt32(reader, 1), InningsOuts = ReadInt32(reader, 2),
                EarnedRuns = ReadInt32(reader, 3), RunsAllowed = ReadInt32(reader, 4), HomeRuns = ReadInt32(reader, 5),
                Walks = ReadInt32(reader, 6), HitBatters = ReadInt32(reader, 7), Strikeouts = ReadInt32(reader, 8),
                InfieldFlies = ReadInt32(reader, 9), WpaAbsSum = ReadDouble(reader, 10), WpaCount = ReadInt32(reader, 11),
            });
        }
        return rows;
    }

    private async Task<List<DiagnosticRoleLine>> ReadDiagnosticRoleLinesAsync(
        SqliteConnection connection,
        IReadOnlyList<ParkFactorGridRow> parkFactors,
        CancellationToken cancellationToken)
    {
        var parkByStadium = parkFactors.Where(row => !string.IsNullOrWhiteSpace(row.Stadium))
            .GroupBy(row => row.Stadium, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().UsedFipFactor ?? 100.0, StringComparer.OrdinalIgnoreCase);
        var lines = new Dictionary<string, DiagnosticRoleLine>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(g.SeasonYear,0), p.Pcode, p.Name, p.TeamCode,
                   CASE WHEN p.IsStarter=1 THEN 1 ELSE 0 END AS StarterRole,
                   COALESCE(g.Stadium,''), COUNT(*),
                   COALESCE(SUM(p.InningsOuts),0), COALESCE(SUM(p.HomeRunsAllowed),0),
                   COALESCE(SUM(p.FinalBB),0), COALESCE(SUM(p.FinalHBP),0),
                   COALESCE(SUM(p.FinalSO),0), COALESCE(SUM(p.IFFB),0),
                   COALESCE(SUM(p.RunsAllowed),0), COALESCE(SUM(p.EarnedRuns),0),
                   COALESCE(SUM(p.EntryAbsoluteWpaSum),0), COALESCE(SUM(p.EntryWpaCount),0)
            FROM PitcherGameStats p
            INNER JOIN Games g ON g.GameId=p.GameId
            WHERE p.HasFinalLine=1 AND p.InningsOuts>0
              AND (p.IsStarter=1 OR p.IsReliever=1)
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
              AND COALESCE(g.SeasonYear,0)>0
            GROUP BY COALESCE(g.SeasonYear,0), p.Pcode, p.Name, p.TeamCode, StarterRole, COALESCE(g.Stadium,'');
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var year = ReadInt32(reader, 0);
            var pcode = reader.GetString(1);
            var name = reader.GetString(2);
            var team = reader.GetString(3);
            var isStarter = ReadInt32(reader, 4) == 1;
            var stadium = reader.GetString(5);
            var key = $"{year}|{pcode}|{(isStarter ? 1 : 0)}";
            if (!lines.TryGetValue(key, out var line))
            {
                line = new DiagnosticRoleLine { Year = year, Pcode = pcode, Name = name, IsStarter = isStarter };
                lines[key] = line;
            }
            if (!string.IsNullOrWhiteSpace(team)) line.TeamCodes.Add(team);
            line.Games += ReadInt32(reader, 6);
            var outs = ReadInt32(reader, 7);
            line.InningsOuts += outs;
            line.HomeRuns += ReadInt32(reader, 8);
            line.Walks += ReadInt32(reader, 9);
            line.HitBatters += ReadInt32(reader, 10);
            line.Strikeouts += ReadInt32(reader, 11);
            line.InfieldFlies += ReadInt32(reader, 12);
            line.RunsAllowed += ReadInt32(reader, 13);
            line.EarnedRuns += ReadInt32(reader, 14);
            line.WpaAbsSum += ReadDouble(reader, 15);
            line.WpaCount += ReadInt32(reader, 16);
            var pf = parkByStadium.TryGetValue(stadium, out var value) ? value : 100.0;
            line.WeightedParkFactorOuts += outs * pf;
        }
        return lines.Values.ToList();
    }

    private sealed class DiagnosticSeasonTotals
    {
        public int Year { get; init; }
        public int Games { get; init; }
        public int InningsOuts { get; init; }
        public int EarnedRuns { get; init; }
        public int RunsAllowed { get; init; }
        public int HomeRuns { get; init; }
        public int Walks { get; init; }
        public int HitBatters { get; init; }
        public int Strikeouts { get; init; }
        public int InfieldFlies { get; init; }
        public double WpaAbsSum { get; init; }
        public int WpaCount { get; init; }
        public double Innings => InningsOuts / 3.0;
    }

    private sealed class DiagnosticRoleLine
    {
        public int Year { get; init; }
        public string Pcode { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool IsStarter { get; init; }
        public HashSet<string> TeamCodes { get; } = new(StringComparer.Ordinal);
        public int Games { get; set; }
        public int InningsOuts { get; set; }
        public int HomeRuns { get; set; }
        public int Walks { get; set; }
        public int HitBatters { get; set; }
        public int Strikeouts { get; set; }
        public int InfieldFlies { get; set; }
        public int RunsAllowed { get; set; }
        public int EarnedRuns { get; set; }
        public double WpaAbsSum { get; set; }
        public int WpaCount { get; set; }
        public double WeightedParkFactorOuts { get; set; }
        public double ParkFactor { get; set; } = 100.0;
        public double ParkAdjustedFipR9 { get; set; }
        public double ParkAdjustedRa9 { get; set; }
        public double GmLi { get; set; } = 1.0;
        public double Innings => InningsOuts / 3.0;
    }

    private sealed class DiagnosticSeasonState
    {
        public DiagnosticSeasonTotals Totals { get; init; } = new();
        public double LeagueEra { get; init; }
        public double LeagueRa9 { get; init; }
        public double LeagueFipR9 { get; init; }
        public IReadOnlyList<PitcherReplacementCandidateRow> StarterRows { get; init; } = Array.Empty<PitcherReplacementCandidateRow>();
        public int StarterRoleLines { get; init; }
        public int RelieverRoleLines { get; init; }
        public IReadOnlyList<PitcherReplacementCandidateRow> RelieverRows { get; init; } = Array.Empty<PitcherReplacementCandidateRow>();
        public double StarterFgFloorFipMinus { get; init; }
        public double RelieverFgFloorFipMinus { get; init; }
        public double StarterReplacementFipMinus { get; init; }
        public double RelieverReplacementFipMinus { get; init; }
        public double StarterReplacementRa9 { get; init; }
        public double RelieverReplacementRa9 { get; init; }
        public double TargetWar { get; init; }
        public double PreFipWar { get; init; }
        public double FipWarIp { get; init; }
        public double PreRa9War { get; init; }
        public double Ra9WarIp { get; init; }
    }
}
