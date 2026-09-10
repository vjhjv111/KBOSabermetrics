using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    /// <summary>
    /// Experimental only. Builds a conservative multi-year run-based park factor and compares
    /// pitcher WAR calibration against the current (legacy) park factor without changing WAR v3.
    /// </summary>
    public async Task<ParkFactorV2ExperimentBundle> GetParkFactorV2ExperimentAsync(
        CancellationToken cancellationToken = default)
    {
        var global = await GetLeagueReferenceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var parkDiagnostics = await GetParkFactorDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var stadiumRows = BuildParkFactorV2Rows(parkDiagnostics, global.ParkFactors);
        var seasonRows = BuildParkFactorV2SeasonRows(stadiumRows);
        var v2BySeasonStadium = stadiumRows
            .Where(x => x.KboParkFactorV2.HasValue)
            .ToDictionary(x => (x.Year, x.Stadium), x => x.KboParkFactorV2!.Value, SeasonStadiumComparer.Instance);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var totals = await ReadDiagnosticSeasonTotalsAsync(connection, cancellationToken).ConfigureAwait(false);
        var roleLines = await ReadDiagnosticRoleLinesAsync(connection, global.ParkFactors, cancellationToken).ConfigureAwait(false);
        var comparisons = BuildWarAbComparisons(totals, roleLines, global, v2BySeasonStadium);

        return new ParkFactorV2ExperimentBundle
        {
            Seasons = seasonRows,
            Stadiums = stadiumRows,
            WarComparisons = comparisons,
        };
    }

    private static List<ParkFactorV2StadiumRow> BuildParkFactorV2Rows(
        ParkFactorDiagnosticBundle diagnostics,
        IReadOnlyList<ParkFactorGridRow> legacyFactors)
    {
        var legacy = legacyFactors
            .Where(x => !string.IsNullOrWhiteSpace(x.Stadium))
            .GroupBy(x => x.Stadium, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().UsedFipFactor ?? 100.0, StringComparer.OrdinalIgnoreCase);

        var provisional = new List<ParkFactorV2StadiumWorkRow>();
        foreach (var target in diagnostics.Stadiums.OrderBy(x => x.Year).ThenBy(x => x.Stadium, StringComparer.OrdinalIgnoreCase))
        {
            // Current-year first, then four prior seasons. More recent years receive more weight.
            var history = diagnostics.Stadiums
                .Where(x => string.Equals(x.Stadium, target.Stadium, StringComparison.OrdinalIgnoreCase)
                            && x.Year <= target.Year && x.Year >= target.Year - 4
                            && x.SimpleRunParkFactor.HasValue && x.Games > 0)
                .ToList();
            double weighted = 0.0, weightedGames = 0.0, totalGames = 0.0;
            foreach (var h in history)
            {
                var age = target.Year - h.Year;
                var recency = age switch { 0 => 0.30, 1 => 0.25, 2 => 0.20, 3 => 0.15, _ => 0.10 };
                var w = recency * h.Games;
                weighted += h.SimpleRunParkFactor!.Value * w;
                weightedGames += w;
                totalGames += h.Games;
            }
            var raw = weightedGames > 0 ? weighted / weightedGames : 100.0;
            // About 100 games of prior information toward a neutral park. With 5 seasons of
            // regular samples the observed park dominates, while small/temporary parks stay near 100.
            var reliability = totalGames > 0 ? totalGames / (totalGames + 100.0) : 0.0;
            var regressed = 100.0 + (raw - 100.0) * reliability;
            var conservative = Math.Clamp(regressed, 85.0, 115.0);
            provisional.Add(new ParkFactorV2StadiumWorkRow
            {
                Year = target.Year,
                Stadium = target.Stadium,
                Games = target.Games,
                Innings = target.Innings,
                Legacy = legacy.TryGetValue(target.Stadium, out var l) ? l : 100.0,
                AnnualRun = target.SimpleRunParkFactor,
                Raw = raw,
                EffectiveGames = totalGames,
                Reliability = reliability,
                Regressed = regressed,
                PreNormalized = conservative,
                Final = conservative,
            });
        }

        // Re-center each season to exactly 100 on an innings-weighted basis. Re-apply conservative
        // bounds iteratively so extreme parks cannot reappear because of normalization.
        foreach (var yearGroup in provisional.GroupBy(x => x.Year))
        {
            var group = yearGroup.ToList();
            for (var pass = 0; pass < 5; pass++)
            {
                var weight = group.Sum(x => Math.Max(0.0, x.Innings));
                if (weight <= 0) break;
                var mean = group.Sum(x => x.Final * Math.Max(0.0, x.Innings)) / weight;
                if (mean <= 0) break;
                var scale = 100.0 / mean;
                foreach (var row in group)
                    row.Final = Math.Clamp(row.Final * scale, 85.0, 115.0);
            }
        }

        return provisional.Select(x => new ParkFactorV2StadiumRow
        {
            Year = x.Year,
            Stadium = x.Stadium,
            Games = x.Games,
            Innings = x.Innings,
            LegacyParkFactor = x.Legacy,
            AnnualRunParkFactor = x.AnnualRun,
            RollingRawParkFactor = x.Raw,
            EffectiveGames = x.EffectiveGames,
            Reliability = x.Reliability,
            RegressedParkFactor = x.Regressed,
            PreNormalizedParkFactor = x.PreNormalized,
            KboParkFactorV2 = x.Final,
            V2MinusLegacy = x.Final - x.Legacy,
        }).ToList();
    }

    private static List<ParkFactorV2SeasonRow> BuildParkFactorV2SeasonRows(IReadOnlyList<ParkFactorV2StadiumRow> rows)
        => rows.GroupBy(x => x.Year).OrderBy(g => g.Key).Select(g =>
        {
            var list = g.ToList();
            return new ParkFactorV2SeasonRow
            {
                Year = g.Key,
                Stadiums = list.Count,
                Games = list.Sum(x => x.Games),
                LegacyWeightedMean = WeightedV2(list, x => x.LegacyParkFactor),
                RawWeightedMean = WeightedV2(list, x => x.RollingRawParkFactor),
                RegressedWeightedMean = WeightedV2(list, x => x.RegressedParkFactor),
                FinalWeightedMean = WeightedV2(list, x => x.KboParkFactorV2),
                MinV2 = list.Where(x => x.KboParkFactorV2.HasValue).Select(x => x.KboParkFactorV2!.Value).DefaultIfEmpty().Min(),
                MedianV2 = PercentileV2(list.Where(x => x.KboParkFactorV2.HasValue).Select(x => x.KboParkFactorV2!.Value), 0.50),
                MaxV2 = list.Where(x => x.KboParkFactorV2.HasValue).Select(x => x.KboParkFactorV2!.Value).DefaultIfEmpty().Max(),
            };
        }).ToList();

    private static double? WeightedV2(IEnumerable<ParkFactorV2StadiumRow> rows, Func<ParkFactorV2StadiumRow, double?> value)
    {
        double sum = 0, weight = 0;
        foreach (var row in rows)
        {
            var v = value(row);
            if (!v.HasValue || row.Innings <= 0) continue;
            sum += v.Value * row.Innings;
            weight += row.Innings;
        }
        return weight > 0 ? sum / weight : null;
    }

    private static double? PercentileV2(IEnumerable<double> values, double p)
    {
        var a = values.Where(double.IsFinite).OrderBy(x => x).ToArray();
        if (a.Length == 0) return null;
        if (a.Length == 1) return a[0];
        var pos = Math.Clamp(p, 0, 1) * (a.Length - 1);
        var lo = (int)Math.Floor(pos); var hi = (int)Math.Ceiling(pos);
        return lo == hi ? a[lo] : a[lo] + (a[hi] - a[lo]) * (pos - lo);
    }

    private static List<PitcherWarParkFactorComparisonRow> BuildWarAbComparisons(
        IReadOnlyList<DiagnosticSeasonTotals> totals,
        IReadOnlyList<DiagnosticRoleLine> allLines,
        LeagueReference global,
        IReadOnlyDictionary<(int Year, string Stadium), double> v2)
    {
        var legacyByStadium = global.ParkFactors
            .Where(x => !string.IsNullOrWhiteSpace(x.Stadium))
            .GroupBy(x => x.Stadium, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().UsedFipFactor ?? 100.0, StringComparer.OrdinalIgnoreCase);
        var result = new List<PitcherWarParkFactorComparisonRow>();
        foreach (var season in totals.OrderBy(x => x.Year))
        {
            if (season.Innings <= 0) continue;
            var leagueEra = season.EarnedRuns * 9.0 / season.Innings;
            var leagueRa9 = season.RunsAllowed * 9.0 / season.Innings;
            var core = (13.0 * season.HomeRuns + 3.0 * (season.Walks + season.HitBatters) -
                        2.0 * (season.Strikeouts + season.InfieldFlies)) / season.Innings;
            var ifFipConstant = leagueEra - core;
            var ra9Adjustment = leagueRa9 - leagueEra;
            var leagueFipR9 = leagueRa9;
            var averageAbsWpa = season.WpaCount > 0 ? season.WpaAbsSum / season.WpaCount : global.AverageAbsoluteWpa;
            if (averageAbsWpa <= 0) averageAbsWpa = 1.0;
            var lines = allLines.Where(x => x.Year == season.Year).ToList();

            var legacy = EvaluatePfScenario(lines, season.Year, leagueFipR9, ifFipConstant, ra9Adjustment,
                averageAbsWpa, (y, stadium) => legacyByStadium.TryGetValue(stadium, out var pf) ? pf : 100.0);
            var experimental = EvaluatePfScenario(lines, season.Year, leagueFipR9, ifFipConstant, ra9Adjustment,
                averageAbsWpa, (y, stadium) => v2.TryGetValue((y, stadium), out var pf) ? pf : 100.0);
            var target = KboPitcherWarMath.ComputeTargetPitcherWar(season.Games);
            var legacyWarIp = (target - legacy.PreWar) / season.Innings;
            var v2WarIp = (target - experimental.PreWar) / season.Innings;
            var reduction = Math.Abs(legacyWarIp) > 1e-12
                ? 100.0 * (Math.Abs(legacyWarIp) - Math.Abs(v2WarIp)) / Math.Abs(legacyWarIp) : (double?)null;
            result.Add(new PitcherWarParkFactorComparisonRow
            {
                Year = season.Year,
                Games = season.Games,
                LeagueInnings = season.Innings,
                TargetWar = target,
                LegacyWeightedParkFactor = legacy.WeightedParkFactor,
                V2WeightedParkFactor = experimental.WeightedParkFactor,
                LegacyStarterReplacementFipMinus = legacy.StarterReplacementFipMinus,
                V2StarterReplacementFipMinus = experimental.StarterReplacementFipMinus,
                LegacyRelieverReplacementFipMinus = legacy.RelieverReplacementFipMinus,
                V2RelieverReplacementFipMinus = experimental.RelieverReplacementFipMinus,
                LegacyPreWar = legacy.PreWar,
                V2PreWar = experimental.PreWar,
                PreWarDelta = experimental.PreWar - legacy.PreWar,
                LegacyTargetRatio = target > 0 ? legacy.PreWar / target : null,
                V2TargetRatio = target > 0 ? experimental.PreWar / target : null,
                LegacyWarIp = legacyWarIp,
                V2WarIp = v2WarIp,
                WarIpReductionPercent = reduction,
                LegacyCorrectionAt180Ip = legacyWarIp * 180.0,
                V2CorrectionAt180Ip = v2WarIp * 180.0,
            });
        }
        return result;
    }

    private static PfScenarioResult EvaluatePfScenario(
        IReadOnlyList<DiagnosticRoleLine> lines,
        int year,
        double leagueFipR9,
        double ifFipConstant,
        double ra9Adjustment,
        double averageAbsWpa,
        Func<int, string, double> parkLookup)
    {
        double totalPfOuts = 0.0, totalOuts = 0.0;
        foreach (var line in lines)
        {
            if (line.Innings <= 0) continue;
            var weightedPf = 0.0;
            var pfOuts = 0;
            foreach (var pair in line.StadiumOuts)
            {
                weightedPf += pair.Value * parkLookup(year, pair.Key);
                pfOuts += pair.Value;
            }
            line.ParkFactor = pfOuts > 0 ? weightedPf / pfOuts : 100.0;
            totalPfOuts += line.ParkFactor * line.InningsOuts;
            totalOuts += line.InningsOuts;
            var ifFip = (13.0 * line.HomeRuns + 3.0 * (line.Walks + line.HitBatters) -
                         2.0 * (line.Strikeouts + line.InfieldFlies)) / line.Innings + ifFipConstant;
            var fipR9 = ifFip + ra9Adjustment;
            line.ParkAdjustedFipR9 = KboPitcherWarMath.ParkAdjust(fipR9, line.ParkFactor);
            line.GmLi = !line.IsStarter && line.WpaCount > 0 && averageAbsWpa > 0
                ? Math.Clamp((line.WpaAbsSum / line.WpaCount) / averageAbsWpa, 0.1, 5.0)
                : 1.0;
        }
        var starterPool = SelectDiagnosticReplacementPool(lines.Where(x => x.IsStarter).ToList(), true);
        var relieverPool = SelectDiagnosticReplacementPool(lines.Where(x => !x.IsStarter).ToList(), false);
        var starterEmpirical = EstimateDiagnosticRate(starterPool, x => x.ParkAdjustedFipR9, leagueFipR9, 40.0);
        var relieverEmpirical = EstimateDiagnosticRate(relieverPool, x => x.ParkAdjustedFipR9, leagueFipR9, 20.0);
        var rpw = Math.Max(1.0, (leagueFipR9 + 2.0) * 1.5);
        var starterFloor = leagueFipR9 + 0.12 * rpw;
        var relieverFloor = leagueFipR9 + 0.03 * rpw;
        var starterReplacement = ClampDiagnosticReplacementRate(Math.Max(starterEmpirical, starterFloor), leagueFipR9);
        var relieverReplacement = ClampDiagnosticReplacementRate(Math.Max(relieverEmpirical, relieverFloor), leagueFipR9);
        var preWar = lines.Sum(line => ComputeDiagnosticRoleWar(line, line.ParkAdjustedFipR9, leagueFipR9,
            line.IsStarter ? starterReplacement : relieverReplacement));
        return new PfScenarioResult
        {
            WeightedParkFactor = totalOuts > 0 ? totalPfOuts / totalOuts : 100.0,
            StarterReplacementFipMinus = Percent(starterReplacement, leagueFipR9),
            RelieverReplacementFipMinus = Percent(relieverReplacement, leagueFipR9),
            PreWar = preWar,
        };
    }

    private sealed class PfScenarioResult
    {
        public double WeightedParkFactor { get; init; }
        public double StarterReplacementFipMinus { get; init; }
        public double RelieverReplacementFipMinus { get; init; }
        public double PreWar { get; init; }
    }

    private sealed class ParkFactorV2StadiumWorkRow
    {
        public int Year { get; init; }
        public string Stadium { get; init; } = string.Empty;
        public int Games { get; init; }
        public double Innings { get; init; }
        public double Legacy { get; init; }
        public double? AnnualRun { get; init; }
        public double Raw { get; init; }
        public double EffectiveGames { get; init; }
        public double Reliability { get; init; }
        public double Regressed { get; init; }
        public double PreNormalized { get; init; }
        public double Final { get; set; }
    }

    private sealed class SeasonStadiumComparer : IEqualityComparer<(int Year, string Stadium)>
    {
        public static readonly SeasonStadiumComparer Instance = new();
        public bool Equals((int Year, string Stadium) x, (int Year, string Stadium) y)
            => x.Year == y.Year && string.Equals(x.Stadium, y.Stadium, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((int Year, string Stadium) obj)
            => HashCode.Combine(obj.Year, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Stadium ?? string.Empty));
    }

    public async Task<ParkFactorSensitivityBundle> GetParkFactorSensitivityAsync(
        CancellationToken cancellationToken = default)
    {
        var global = await GetLeagueReferenceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var parkDiagnostics = await GetParkFactorDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var totals = await ReadDiagnosticSeasonTotalsAsync(connection, cancellationToken).ConfigureAwait(false);
        var roleLines = await ReadDiagnosticRoleLinesAsync(connection, global.ParkFactors, cancellationToken).ConfigureAwait(false);

        var details = new List<ParkFactorSensitivityRow>();
        foreach (var prior in new[] { 50, 100, 150, 200 })
        foreach (var bounds in new[] { (Low: 80.0, High: 120.0), (Low: 85.0, High: 115.0), (Low: 90.0, High: 110.0) })
        foreach (var policy in new[] { "균등", "최근가중" })
        {
            var pfRows = BuildSensitivityParkFactors(parkDiagnostics, prior, bounds.Low, bounds.High, policy == "최근가중");
            var lookup = pfRows.ToDictionary(x => (x.Year, x.Stadium), x => x.ParkFactor, SeasonStadiumComparer.Instance);
            foreach (var season in totals.OrderBy(x => x.Year))
            {
                if (season.Innings <= 0) continue;
                var leagueEra = season.EarnedRuns * 9.0 / season.Innings;
                var leagueRa9 = season.RunsAllowed * 9.0 / season.Innings;
                var core = (13.0 * season.HomeRuns + 3.0 * (season.Walks + season.HitBatters) -
                            2.0 * (season.Strikeouts + season.InfieldFlies)) / season.Innings;
                var ifFipConstant = leagueEra - core;
                var ra9Adjustment = leagueRa9 - leagueEra;
                var averageAbsWpa = season.WpaCount > 0 ? season.WpaAbsSum / season.WpaCount : global.AverageAbsoluteWpa;
                if (averageAbsWpa <= 0) averageAbsWpa = 1.0;
                var lines = roleLines.Where(x => x.Year == season.Year).ToList();
                var scenario = EvaluatePfScenario(lines, season.Year, leagueRa9, ifFipConstant, ra9Adjustment,
                    averageAbsWpa, (y, stadium) => lookup.TryGetValue((y, stadium), out var pf) ? pf : 100.0);
                var target = KboPitcherWarMath.ComputeTargetPitcherWar(season.Games);
                var warIp = (target - scenario.PreWar) / season.Innings;
                var seasonPfs = pfRows.Where(x => x.Year == season.Year).ToList();
                details.Add(new ParkFactorSensitivityRow
                {
                    PriorGames = prior,
                    Clamp = $"{bounds.Low:0}-{bounds.High:0}",
                    WeightPolicy = policy,
                    Year = season.Year,
                    Games = season.Games,
                    WeightedParkFactor = scenario.WeightedParkFactor,
                    MinParkFactor = seasonPfs.Count > 0 ? seasonPfs.Min(x => x.ParkFactor) : 100.0,
                    MaxParkFactor = seasonPfs.Count > 0 ? seasonPfs.Max(x => x.ParkFactor) : 100.0,
                    StarterReplacementFipMinus = scenario.StarterReplacementFipMinus,
                    RelieverReplacementFipMinus = scenario.RelieverReplacementFipMinus,
                    TargetWar = target,
                    PreWar = scenario.PreWar,
                    TargetRatio = target > 0 ? scenario.PreWar / target : null,
                    WarIp = warIp,
                    CorrectionAt180Ip = warIp * 180.0,
                });
            }
        }

        // Completed seasons: use 720-game seasons. The current partial season remains in Details
        // but does not drive policy selection.
        var summaries = details.GroupBy(x => new { x.PriorGames, x.Clamp, x.WeightPolicy })
            .Select(g =>
            {
                var completed = g.Where(x => x.Games >= 720).ToList();
                if (completed.Count == 0) completed = g.ToList();
                return new ParkFactorSensitivitySummaryRow
                {
                    PriorGames = g.Key.PriorGames,
                    Clamp = g.Key.Clamp,
                    WeightPolicy = g.Key.WeightPolicy,
                    CompletedSeasons = completed.Count,
                    MeanTargetRatio = completed.Where(x => x.TargetRatio.HasValue).Select(x => x.TargetRatio!.Value).DefaultIfEmpty().Average(),
                    MinTargetRatio = completed.Where(x => x.TargetRatio.HasValue).Select(x => x.TargetRatio!.Value).DefaultIfEmpty().Min(),
                    MaxTargetRatio = completed.Where(x => x.TargetRatio.HasValue).Select(x => x.TargetRatio!.Value).DefaultIfEmpty().Max(),
                    MeanAbsoluteWarIp = completed.Select(x => Math.Abs(x.WarIp)).DefaultIfEmpty().Average(),
                    MaxAbsoluteWarIp = completed.Select(x => Math.Abs(x.WarIp)).DefaultIfEmpty().Max(),
                    MeanStarterReplacementFipMinus = completed.Select(x => x.StarterReplacementFipMinus).DefaultIfEmpty().Average(),
                    MeanRelieverReplacementFipMinus = completed.Select(x => x.RelieverReplacementFipMinus).DefaultIfEmpty().Average(),
                    MinParkFactor = completed.Select(x => x.MinParkFactor).DefaultIfEmpty().Min(),
                    MaxParkFactor = completed.Select(x => x.MaxParkFactor).DefaultIfEmpty().Max(),
                };
            })
            .OrderBy(x => x.MeanAbsoluteWarIp)
            .ThenBy(x => Math.Abs((x.MeanTargetRatio ?? 1.0) - 1.0))
            .ToList();

        return new ParkFactorSensitivityBundle { Summaries = summaries, Details = details };
    }

    private static List<SensitivityParkFactorRow> BuildSensitivityParkFactors(
        ParkFactorDiagnosticBundle diagnostics, int priorGames, double low, double high, bool recentWeighted)
    {
        var provisional = new List<SensitivityParkFactorRow>();
        foreach (var target in diagnostics.Stadiums.OrderBy(x => x.Year).ThenBy(x => x.Stadium, StringComparer.OrdinalIgnoreCase))
        {
            var history = diagnostics.Stadiums
                .Where(x => string.Equals(x.Stadium, target.Stadium, StringComparison.OrdinalIgnoreCase)
                            && x.Year <= target.Year && x.Year >= target.Year - 4
                            && x.SimpleRunParkFactor.HasValue && x.Games > 0)
                .ToList();
            double weighted = 0.0, weight = 0.0, totalGames = 0.0;
            foreach (var h in history)
            {
                var age = target.Year - h.Year;
                var recency = recentWeighted
                    ? age switch { 0 => 0.30, 1 => 0.25, 2 => 0.20, 3 => 0.15, _ => 0.10 }
                    : 0.20;
                var w = recency * h.Games;
                weighted += h.SimpleRunParkFactor!.Value * w;
                weight += w;
                totalGames += h.Games;
            }
            var raw = weight > 0 ? weighted / weight : 100.0;
            var reliability = totalGames > 0 ? totalGames / (totalGames + priorGames) : 0.0;
            var regressed = 100.0 + (raw - 100.0) * reliability;
            provisional.Add(new SensitivityParkFactorRow
            {
                Year = target.Year, Stadium = target.Stadium, Innings = target.Innings,
                ParkFactor = Math.Clamp(regressed, low, high)
            });
        }
        foreach (var group in provisional.GroupBy(x => x.Year))
        {
            var rows = group.ToList();
            for (var pass = 0; pass < 8; pass++)
            {
                var totalWeight = rows.Sum(x => Math.Max(0.0, x.Innings));
                if (totalWeight <= 0) break;
                var mean = rows.Sum(x => x.ParkFactor * Math.Max(0.0, x.Innings)) / totalWeight;
                if (mean <= 0) break;
                var scale = 100.0 / mean;
                foreach (var row in rows) row.ParkFactor = Math.Clamp(row.ParkFactor * scale, low, high);
                if (Math.Abs(mean - 100.0) < 0.00001) break;
            }
        }
        return provisional;
    }

    private sealed class SensitivityParkFactorRow
    {
        public int Year { get; init; }
        public string Stadium { get; init; } = string.Empty;
        public double Innings { get; init; }
        public double ParkFactor { get; set; }
    }

}
