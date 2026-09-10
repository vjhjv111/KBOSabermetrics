using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    private async Task<PitcherWarCalibration> BuildPitcherWarCalibrationAsync(
        SqliteConnection connection,
        LeagueReference league,
        CancellationToken cancellationToken)
    {
        var roleLines = await ReadPitcherWarRoleLinesAsync(connection, league, cancellationToken).ConfigureAwait(false);
        var v2ByYearStadium = league.KboParkFactorsV2
            .ToDictionary(x => (x.Year, x.Stadium), x => x.Factor, SeasonStadiumComparer.Instance);
        foreach (var line in roleLines)
        {
            double weighted = 0; var outs = 0;
            foreach (var pair in line.StadiumOuts)
            {
                var pf = v2ByYearStadium.TryGetValue((line.SeasonYear, pair.Key), out var v) ? v : 100.0;
                weighted += pf * pair.Value; outs += pair.Value;
            }
            line.ParkFactor = outs > 0 ? weighted / outs : 100.0;
            var ifFip = (13.0 * line.HomeRuns + 3.0 * (line.Walks + line.HitBatters) -
                         2.0 * (line.Strikeouts + line.InfieldFlies)) / line.Innings + league.IfFipConstant;
            line.ParkAdjustedFipR9 = KboPitcherWarMath.ParkAdjust(ifFip + league.Ra9Adjustment, line.ParkFactor);
        }
        if (roleLines.Count == 0 || league.PitchingInnings <= 0)
        {
            return BuildFallbackPitcherWarCalibration(league);
        }

        var latestSeasons = roleLines
            .Where(line => line.SeasonYear > 0)
            .Select(line => line.SeasonYear)
            .Distinct()
            .OrderByDescending(year => year)
            .Take(3)
            .OrderBy(year => year)
            .ToArray();
        var recentSeasonSet = latestSeasons.ToHashSet();
        var recentLines = latestSeasons.Length > 0
            ? roleLines.Where(line => recentSeasonSet.Contains(line.SeasonYear)).ToList()
            : roleLines;

        var starterPool = SelectReplacementPool(recentLines.Where(line => line.IsStarter).ToList(), true);
        var relieverPool = SelectReplacementPool(recentLines.Where(line => !line.IsStarter).ToList(), false);

        // KBO Pitcher WAR v4: validated 2020-2025 replacement levels.
        // SP/RP candidate median WAR was approximately zero at 120/115 FIP-.
        var starterEmpiricalFip = league.LeagueFipR9 * 1.20;
        var relieverEmpiricalFip = league.LeagueFipR9 * 1.15;
        var starterReplacementFip = league.LeagueFipR9 * 1.20;
        var relieverReplacementFip = league.LeagueFipR9 * 1.15;

        // RA9-WAR remains the existing internal/diagnostic model; v4 designation applies to KBO fWAR.
        var starterEmpiricalRa9 = EstimateReplacementRate(
            starterPool, line => line.ParkAdjustedRa9, league.LeagueRa9, regressionInnings: 50.0);
        var relieverEmpiricalRa9 = EstimateReplacementRate(
            relieverPool, line => line.ParkAdjustedRa9, league.LeagueRa9, regressionInnings: 25.0);
        var typicalRa9Rpw = Math.Max(1.0, (league.LeagueRa9 + 2.0) * 1.5);
        var starterRa9Floor = league.LeagueRa9 + 0.12 * typicalRa9Rpw;
        var relieverRa9Floor = league.LeagueRa9 + 0.03 * typicalRa9Rpw;
        var starterReplacementRa9 = ClampReplacementRate(Math.Max(starterEmpiricalRa9, starterRa9Floor), league.LeagueRa9);
        var relieverReplacementRa9 = ClampReplacementRate(Math.Max(relieverEmpiricalRa9, relieverRa9Floor), league.LeagueRa9);

        var targetWar = KboPitcherWarMath.ComputeTargetPitcherWar(league.GameCount);
        var totalInnings = roleLines.Sum(line => line.Innings);
        var preFipWar = roleLines.Sum(line => ComputeRoleWar(
            line,
            line.ParkAdjustedFipR9,
            league.LeagueFipR9,
            line.IsStarter ? starterReplacementFip : relieverReplacementFip));
        var preRa9War = roleLines.Sum(line => ComputeRoleWar(
            line,
            line.ParkAdjustedRa9,
            league.LeagueRa9,
            line.IsStarter ? starterReplacementRa9 : relieverReplacementRa9));

        var fipWarIp = totalInnings > 0 ? (targetWar - preFipWar) / totalInnings : 0.0;
        var ra9WarIp = totalInnings > 0 ? (targetWar - preRa9War) / totalInnings : 0.0;

        return new PitcherWarCalibration
        {
            ReplacementWinningPercentage = KboPitcherWarMath.DefaultReplacementWinningPercentage,
            PitcherWarShare = KboPitcherWarMath.DefaultPitcherWarShare,
            TargetPitcherWar = targetWar,
            PreCorrectionFipWar = preFipWar,
            FipWarPerInning = fipWarIp,
            PreCorrectionRa9War = preRa9War,
            Ra9WarPerInning = ra9WarIp,
            EmpiricalStarterReplacementFipR9 = starterEmpiricalFip,
            EmpiricalRelieverReplacementFipR9 = relieverEmpiricalFip,
            StarterReplacementFipR9 = starterReplacementFip,
            RelieverReplacementFipR9 = relieverReplacementFip,
            StarterReplacementFipMinus = league.LeagueFipR9 > 0
                ? 100.0 * starterReplacementFip / league.LeagueFipR9
                : 0.0,
            RelieverReplacementFipMinus = league.LeagueFipR9 > 0
                ? 100.0 * relieverReplacementFip / league.LeagueFipR9
                : 0.0,
            EmpiricalStarterReplacementRa9 = starterEmpiricalRa9,
            EmpiricalRelieverReplacementRa9 = relieverEmpiricalRa9,
            StarterReplacementRa9 = starterReplacementRa9,
            RelieverReplacementRa9 = relieverReplacementRa9,
            ReplacementSampleSeasons = latestSeasons.Length == 0
                ? "전체"
                : latestSeasons.Length == 1
                    ? latestSeasons[0].ToString()
                    : $"{latestSeasons[0]}-{latestSeasons[^1]}",
            StarterSamplePitchers = starterPool.Select(line => line.Pcode).Distinct(StringComparer.Ordinal).Count(),
            RelieverSamplePitchers = relieverPool.Select(line => line.Pcode).Distinct(StringComparer.Ordinal).Count(),
            StarterSampleInnings = starterPool.Sum(line => line.Innings),
            RelieverSampleInnings = relieverPool.Sum(line => line.Innings),
            TotalPitchingInnings = totalInnings,
            BlendFipWeight = KboPitcherWarMath.DefaultBlendFipWeight,
            BlendRa9Weight = KboPitcherWarMath.DefaultBlendRa9Weight,
        };
    }

    private static PitcherWarCalibration BuildFallbackPitcherWarCalibration(LeagueReference league)
    {
        var fipRpw = Math.Max(1.0, (league.LeagueFipR9 + 2.0) * 1.5);
        var ra9Rpw = Math.Max(1.0, (league.LeagueRa9 + 2.0) * 1.5);
        var starterFip = league.LeagueFipR9 * 1.20;
        var relieverFip = league.LeagueFipR9 * 1.15;
        var starterRa9 = league.LeagueRa9 + 0.12 * ra9Rpw;
        var relieverRa9 = league.LeagueRa9 + 0.03 * ra9Rpw;
        return new PitcherWarCalibration
        {
            TargetPitcherWar = KboPitcherWarMath.ComputeTargetPitcherWar(league.GameCount),
            StarterReplacementFipR9 = starterFip,
            RelieverReplacementFipR9 = relieverFip,
            StarterReplacementFipMinus = league.LeagueFipR9 > 0 ? 100.0 * starterFip / league.LeagueFipR9 : 0.0,
            RelieverReplacementFipMinus = league.LeagueFipR9 > 0 ? 100.0 * relieverFip / league.LeagueFipR9 : 0.0,
            StarterReplacementRa9 = starterRa9,
            RelieverReplacementRa9 = relieverRa9,
            EmpiricalStarterReplacementFipR9 = starterFip,
            EmpiricalRelieverReplacementFipR9 = relieverFip,
            EmpiricalStarterReplacementRa9 = starterRa9,
            EmpiricalRelieverReplacementRa9 = relieverRa9,
            TotalPitchingInnings = league.PitchingInnings,
            ReplacementSampleSeasons = "표본 부족: FG 하한 사용",
        };
    }

    private static double ComputeRoleWar(
        PitcherWarRoleLine line,
        double pitcherRunRate,
        double leagueRunRate,
        double replacementRunRate)
    {
        if (line.Innings <= 0) return 0.0;
        var runsPerWin = KboPitcherWarMath.DynamicRunsPerWin(
            leagueRunRate,
            pitcherRunRate,
            line.Innings,
            line.Games);
        var leverage = KboPitcherWarMath.LeverageMultiplier(line.GmLi, !line.IsStarter);
        var quality = KboPitcherWarMath.WinsAboveAverage(
            leagueRunRate,
            pitcherRunRate,
            line.Innings,
            runsPerWin,
            leverage);
        var replacement = KboPitcherWarMath.ReplacementWins(
            replacementRunRate,
            leagueRunRate,
            line.Innings,
            runsPerWin,
            leverage);
        return quality + replacement;
    }

    private static List<PitcherWarRoleLine> SelectReplacementPool(
        IReadOnlyList<PitcherWarRoleLine> roleLines,
        bool starter)
    {
        if (roleLines.Count == 0) return new List<PitcherWarRoleLine>();
        var minimumInnings = starter ? 9.0 : 5.0;
        var eligible = roleLines.Where(line => line.Innings >= minimumInnings).ToList();
        if (eligible.Count == 0)
            eligible = roleLines.Where(line => line.Innings > 0).ToList();
        if (eligible.Count == 0) return new List<PitcherWarRoleLine>();

        var ordered = eligible.OrderBy(line => line.Innings).ToList();
        var percentileIndex = Math.Clamp((int)Math.Floor((ordered.Count - 1) * 0.40), 0, ordered.Count - 1);
        var percentileInnings = ordered[percentileIndex].Innings;
        var hardCap = starter ? 50.0 : 30.0;
        var threshold = Math.Min(hardCap, Math.Max(minimumInnings, percentileInnings));
        var candidates = ordered.Where(line => line.Innings <= threshold).ToList();

        var desiredCount = Math.Min(ordered.Count, Math.Max(8, (int)Math.Ceiling(ordered.Count * 0.35)));
        if (candidates.Count < desiredCount)
            candidates = ordered.Take(desiredCount).ToList();
        return candidates;
    }

    private static double EstimateReplacementRate(
        IReadOnlyList<PitcherWarRoleLine> pool,
        Func<PitcherWarRoleLine, double> selector,
        double leagueRate,
        double regressionInnings)
    {
        if (pool.Count == 0) return leagueRate;
        var numerator = 0.0;
        var denominator = 0.0;
        foreach (var line in pool)
        {
            if (line.Innings <= 0) continue;
            var observed = selector(line);
            if (double.IsNaN(observed) || double.IsInfinity(observed) || observed <= 0) continue;
            var reliability = line.Innings / (line.Innings + regressionInnings);
            var regressed = leagueRate + reliability * (observed - leagueRate);
            var weight = Math.Min(line.Innings, regressionInnings);
            numerator += regressed * weight;
            denominator += weight;
        }
        return denominator > 0 ? numerator / denominator : leagueRate;
    }

    private static double ClampReplacementRate(double value, double leagueRate)
    {
        var minimum = Math.Max(0.01, leagueRate * 1.01);
        var maximum = Math.Max(minimum, leagueRate + 3.50);
        return Math.Clamp(value, minimum, maximum);
    }

    private async Task<List<PitcherWarRoleLine>> ReadPitcherWarRoleLinesAsync(
        SqliteConnection connection,
        LeagueReference league,
        CancellationToken cancellationToken)
    {
        var parkByStadium = league.ParkFactors
            .Where(row => !string.IsNullOrWhiteSpace(row.Stadium))
            .GroupBy(row => row.Stadium, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().UsedFipFactor ?? 100.0,
                StringComparer.OrdinalIgnoreCase);
        var lines = new Dictionary<string, PitcherWarRoleLine>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(g.SeasonYear,0), p.Pcode,
                   CASE WHEN p.IsStarter=1 THEN 1 ELSE 0 END AS StarterRole,
                   COALESCE(g.Stadium,''), COUNT(*),
                   COALESCE(SUM(p.InningsOuts),0), COALESCE(SUM(p.HomeRunsAllowed),0),
                   COALESCE(SUM(p.FinalBB),0), COALESCE(SUM(p.FinalHBP),0),
                   COALESCE(SUM(p.FinalSO),0), COALESCE(SUM(p.IFFB),0),
                   COALESCE(SUM(p.RunsAllowed),0), COALESCE(SUM(p.EarnedRuns),0),
                   COALESCE(SUM(p.EntryAbsoluteWpaSum),0), COALESCE(SUM(p.EntryWpaCount),0)
            FROM PitcherGameStats p
            INNER JOIN Games g ON g.GameId=p.GameId
            WHERE p.HasFinalLine=1
              AND p.InningsOuts>0
              AND (p.IsStarter=1 OR p.IsReliever=1)
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            GROUP BY COALESCE(g.SeasonYear,0), p.Pcode, StarterRole, COALESCE(g.Stadium,'');
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var season = ReadInt32(reader, 0);
            var pcode = reader.GetString(1);
            var isStarter = ReadInt32(reader, 2) == 1;
            var stadium = reader.GetString(3);
            var key = $"{season}|{pcode}|{(isStarter ? 1 : 0)}";
            if (!lines.TryGetValue(key, out var line))
            {
                line = new PitcherWarRoleLine
                {
                    SeasonYear = season,
                    Pcode = pcode,
                    IsStarter = isStarter,
                };
                lines[key] = line;
            }

            line.Games += ReadInt32(reader, 4);
            var outs = ReadInt32(reader, 5);
            line.InningsOuts += outs;
            line.HomeRuns += ReadInt32(reader, 6);
            line.Walks += ReadInt32(reader, 7);
            line.HitBatters += ReadInt32(reader, 8);
            line.Strikeouts += ReadInt32(reader, 9);
            line.InfieldFlies += ReadInt32(reader, 10);
            line.RunsAllowed += ReadInt32(reader, 11);
            line.EarnedRuns += ReadInt32(reader, 12);
            line.EntryAbsoluteWpaSum += ReadDouble(reader, 13);
            line.EntryWpaCount += ReadInt32(reader, 14);
            var parkFactor = parkByStadium.TryGetValue(stadium, out var factor) ? factor : 100.0;
            line.WeightedParkFactorOuts += outs * parkFactor;
        }

        foreach (var line in lines.Values)
        {
            if (line.Innings <= 0) continue;
            line.ParkFactor = line.InningsOuts > 0
                ? line.WeightedParkFactorOuts / line.InningsOuts
                : 100.0;
            var ifFip = (13.0 * line.HomeRuns + 3.0 * (line.Walks + line.HitBatters) -
                         2.0 * (line.Strikeouts + line.InfieldFlies)) / line.Innings + league.IfFipConstant;
            var fipR9 = ifFip + league.Ra9Adjustment;
            line.ParkAdjustedFipR9 = KboPitcherWarMath.ParkAdjust(fipR9, line.ParkFactor);
            var ra9 = line.RunsAllowed * 9.0 / line.Innings;
            // 별도 득점 파크팩터가 없으므로 현재 FIP PF를 RA9의 구장 보정 대용값으로 사용합니다.
            line.ParkAdjustedRa9 = KboPitcherWarMath.ParkAdjust(ra9, line.ParkFactor);
            line.GmLi = !line.IsStarter && line.EntryWpaCount > 0 && league.AverageAbsoluteWpa > 0
                ? Math.Clamp((line.EntryAbsoluteWpaSum / line.EntryWpaCount) / league.AverageAbsoluteWpa, 0.1, 5.0)
                : 1.0;
        }

        return lines.Values.Where(line => line.Innings > 0).ToList();
    }

    private sealed class PitcherWarRoleLine
    {
        public int SeasonYear { get; init; }
        public string Pcode { get; init; } = string.Empty;
        public bool IsStarter { get; init; }
        public int Games { get; set; }
        public int InningsOuts { get; set; }
        public int HomeRuns { get; set; }
        public int Walks { get; set; }
        public int HitBatters { get; set; }
        public int Strikeouts { get; set; }
        public int InfieldFlies { get; set; }
        public int RunsAllowed { get; set; }
        public int EarnedRuns { get; set; }
        public double EntryAbsoluteWpaSum { get; set; }
        public int EntryWpaCount { get; set; }
        public double WeightedParkFactorOuts { get; set; }
        public double ParkFactor { get; set; } = 100.0;
        public double ParkAdjustedFipR9 { get; set; }
        public double ParkAdjustedRa9 { get; set; }
        public double GmLi { get; set; } = 1.0;
        public double Innings => InningsOuts / 3.0;
    }
}
