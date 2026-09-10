using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    /// <summary>
    /// Diagnostic only. Keeps KBO PF v2 fixed (Prior=100, Clamp=85~115, recent weighting)
    /// and varies only SP/RP replacement FIP- to inspect WAR allocation and WARIP.
    /// The production WAR v3 formula is not modified.
    /// </summary>
    public async Task<ReplacementSensitivityBundle> GetReplacementSensitivityAsync(
        CancellationToken cancellationToken = default)
    {
        var global = await GetLeagueReferenceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var parkDiagnostics = await GetParkFactorDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var pfRows = BuildSensitivityParkFactors(parkDiagnostics, 100, 85.0, 115.0, true);
        var pfLookup = pfRows.ToDictionary(
            x => (x.Year, x.Stadium), x => x.ParkFactor, SeasonStadiumComparer.Instance);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var totals = await ReadDiagnosticSeasonTotalsAsync(connection, cancellationToken).ConfigureAwait(false);
        var allRoleLines = await ReadDiagnosticRoleLinesAsync(connection, global.ParkFactors, cancellationToken).ConfigureAwait(false);

        var details = new List<ReplacementSensitivitySeasonRow>();
        foreach (var season in totals.OrderBy(x => x.Year))
        {
            if (season.Innings <= 0) continue;

            var leagueEra = season.EarnedRuns * 9.0 / season.Innings;
            var leagueRa9 = season.RunsAllowed * 9.0 / season.Innings;
            var core = (13.0 * season.HomeRuns + 3.0 * (season.Walks + season.HitBatters) -
                        2.0 * (season.Strikeouts + season.InfieldFlies)) / season.Innings;
            var ifFipConstant = leagueEra - core;
            var ra9Adjustment = leagueRa9 - leagueEra;
            var averageAbsWpa = season.WpaCount > 0
                ? season.WpaAbsSum / season.WpaCount
                : global.AverageAbsoluteWpa;
            if (averageAbsWpa <= 0) averageAbsWpa = 1.0;

            var lines = allRoleLines.Where(x => x.Year == season.Year).ToList();
            // Initialize PF-v2 adjusted FIPR9 and gmLI. The dynamic replacement result from this
            // call is deliberately ignored; the grid below supplies fixed replacement FIP- values.
            _ = EvaluatePfScenario(lines, season.Year, leagueRa9, ifFipConstant, ra9Adjustment,
                averageAbsWpa, (y, stadium) => pfLookup.TryGetValue((y, stadium), out var pf) ? pf : 100.0);

            var target = KboPitcherWarMath.ComputeTargetPitcherWar(season.Games);
            var starterIp = lines.Where(x => x.IsStarter).Sum(x => x.Innings);
            var relieverIp = lines.Where(x => !x.IsStarter).Sum(x => x.Innings);

            foreach (var spMinus in new[] { 120.0, 125.0, 130.0 })
            foreach (var rpMinus in new[] { 105.0, 110.0, 115.0, 120.0, 125.0 })
            {
                var spReplacement = leagueRa9 * spMinus / 100.0;
                var rpReplacement = leagueRa9 * rpMinus / 100.0;

                var starterPre = 0.0;
                var relieverPre = 0.0;
                var preByLine = new List<(DiagnosticRoleLine Line, double War)>();
                foreach (var line in lines)
                {
                    var replacement = line.IsStarter ? spReplacement : rpReplacement;
                    var war = ComputeDiagnosticRoleWar(line, line.ParkAdjustedFipR9, leagueRa9, replacement);
                    preByLine.Add((line, war));
                    if (line.IsStarter) starterPre += war;
                    else relieverPre += war;
                }

                var totalPre = starterPre + relieverPre;
                var warIp = (target - totalPre) / season.Innings;
                var starterFinal = starterPre + warIp * starterIp;
                var relieverFinal = relieverPre + warIp * relieverIp;
                var finalByLine = preByLine.Select(x => (x.Line, War: x.War + warIp * x.Line.Innings)).ToList();
                var topSp = finalByLine.Where(x => x.Line.IsStarter).Select(x => x.War).DefaultIfEmpty(double.NaN).Max();
                var topRp = finalByLine.Where(x => !x.Line.IsStarter).Select(x => x.War).DefaultIfEmpty(double.NaN).Max();

                details.Add(new ReplacementSensitivitySeasonRow
                {
                    StarterReplacementFipMinus = spMinus,
                    RelieverReplacementFipMinus = rpMinus,
                    Year = season.Year,
                    Games = season.Games,
                    LeagueInnings = season.Innings,
                    StarterInnings = starterIp,
                    RelieverInnings = relieverIp,
                    TargetWar = target,
                    StarterPreWar = starterPre,
                    RelieverPreWar = relieverPre,
                    TotalPreWar = totalPre,
                    TargetRatio = target > 0 ? totalPre / target : null,
                    WarIp = warIp,
                    StarterFinalWar = starterFinal,
                    RelieverFinalWar = relieverFinal,
                    StarterWarShare = target > 0 ? starterFinal / target : null,
                    RelieverWarShare = target > 0 ? relieverFinal / target : null,
                    CorrectionAt180Ip = warIp * 180.0,
                    TopStarterWar = double.IsNaN(topSp) ? null : topSp,
                    TopRelieverWar = double.IsNaN(topRp) ? null : topRp,
                    NonPositiveStarterCount = finalByLine.Count(x => x.Line.IsStarter && x.War <= 0.0),
                    NonPositiveRelieverCount = finalByLine.Count(x => !x.Line.IsStarter && x.War <= 0.0),
                });
            }
        }

        var summaries = details
            .GroupBy(x => new { x.StarterReplacementFipMinus, x.RelieverReplacementFipMinus })
            .Select(g =>
            {
                var completed = g.Where(x => x.Games >= 720).ToList();
                if (completed.Count == 0) completed = g.ToList();
                return new ReplacementSensitivitySummaryRow
                {
                    StarterReplacementFipMinus = g.Key.StarterReplacementFipMinus,
                    RelieverReplacementFipMinus = g.Key.RelieverReplacementFipMinus,
                    CompletedSeasons = completed.Count,
                    MeanTargetRatio = Mean(completed.Select(x => x.TargetRatio)),
                    MeanAbsoluteWarIp = Mean(completed.Select(x => (double?)Math.Abs(x.WarIp))),
                    MaxAbsoluteWarIp = completed.Count > 0 ? completed.Max(x => Math.Abs(x.WarIp)) : null,
                    MeanStarterWarShare = Mean(completed.Select(x => x.StarterWarShare)),
                    MeanRelieverWarShare = Mean(completed.Select(x => x.RelieverWarShare)),
                    MeanStarterFinalWar = Mean(completed.Select(x => (double?)x.StarterFinalWar)),
                    MeanRelieverFinalWar = Mean(completed.Select(x => (double?)x.RelieverFinalWar)),
                    MeanTopStarterWar = Mean(completed.Select(x => x.TopStarterWar)),
                    MeanTopRelieverWar = Mean(completed.Select(x => x.TopRelieverWar)),
                    MeanNonPositiveStarterCount = Mean(completed.Select(x => (double?)x.NonPositiveStarterCount)),
                    MeanNonPositiveRelieverCount = Mean(completed.Select(x => (double?)x.NonPositiveRelieverCount)),
                };
            })
            .OrderBy(x => x.MeanAbsoluteWarIp)
            .ThenBy(x => Math.Abs((x.MeanTargetRatio ?? 1.0) - 1.0))
            .ToList();

        return new ReplacementSensitivityBundle { Summaries = summaries, Details = details };
    }

    private static double? Mean(IEnumerable<double?> values)
    {
        var a = values.Where(x => x.HasValue && double.IsFinite(x.Value)).Select(x => x!.Value).ToArray();
        return a.Length == 0 ? null : a.Average();
    }
}
