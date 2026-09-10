using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    /// <summary>
    /// Diagnostic only. Uses fixed experimental policy: KBO PF v2 (Prior 100, Clamp 85-115,
    /// recent weighting) + SP Replacement FIP-=120 + RP Replacement FIP-=115.
    /// Production WAR v3 is not changed.
    /// </summary>
    public async Task<WarDistributionDiagnosticBundle> GetWarDistributionDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        const double spMinus = 120.0;
        const double rpMinus = 115.0;

        var global = await GetLeagueReferenceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var parkDiagnostics = await GetParkFactorDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var pfRows = BuildSensitivityParkFactors(parkDiagnostics, 100, 85.0, 115.0, true);
        var pfLookup = pfRows.ToDictionary(
            x => (x.Year, x.Stadium), x => x.ParkFactor, SeasonStadiumComparer.Instance);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var totals = await ReadDiagnosticSeasonTotalsAsync(connection, cancellationToken).ConfigureAwait(false);
        var allRoleLines = await ReadDiagnosticRoleLinesAsync(connection, global.ParkFactors, cancellationToken).ConfigureAwait(false);

        var players = new List<WarDistributionPlayerRow>();
        var summaries = new List<WarDistributionSummaryRow>();

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
            _ = EvaluatePfScenario(lines, season.Year, leagueRa9, ifFipConstant, ra9Adjustment,
                averageAbsWpa, (y, stadium) => pfLookup.TryGetValue((y, stadium), out var pf) ? pf : 100.0);

            var spPool = SelectDiagnosticReplacementPool(lines.Where(x => x.IsStarter).ToList(), true);
            var rpPool = SelectDiagnosticReplacementPool(lines.Where(x => !x.IsStarter).ToList(), false);
            var candidates = new HashSet<DiagnosticRoleLine>(spPool.Concat(rpPool));

            var spReplacement = leagueRa9 * spMinus / 100.0;
            var rpReplacement = leagueRa9 * rpMinus / 100.0;
            var pre = new List<(DiagnosticRoleLine Line, double War)>();
            foreach (var line in lines)
            {
                var replacement = line.IsStarter ? spReplacement : rpReplacement;
                var war = ComputeDiagnosticRoleWar(line, line.ParkAdjustedFipR9, leagueRa9, replacement);
                pre.Add((line, war));
            }

            var target = KboPitcherWarMath.ComputeTargetPitcherWar(season.Games);
            var totalPre = pre.Sum(x => x.War);
            var warIp = (target - totalPre) / season.Innings;

            var seasonRows = new List<WarDistributionPlayerRow>();
            foreach (var x in pre)
            {
                var correction = warIp * x.Line.Innings;
                var finalWar = x.War + correction;
                seasonRows.Add(new WarDistributionPlayerRow
                {
                    Year = season.Year,
                    Role = x.Line.IsStarter ? "SP" : "RP",
                    Candidate = candidates.Contains(x.Line) ? "Y" : "",
                    PlayerCode = x.Line.Pcode,
                    Name = x.Line.Name,
                    Teams = string.Join("/", x.Line.TeamCodes.OrderBy(v => v, StringComparer.Ordinal)),
                    Games = x.Line.Games,
                    Innings = x.Line.Innings,
                    ParkFactor = x.Line.ParkFactor,
                    ParkAdjustedFipR9 = x.Line.ParkAdjustedFipR9,
                    GmLi = x.Line.GmLi,
                    PreWar = x.War,
                    WarIpCorrection = correction,
                    FinalWar = finalWar,
                    WarBand = WarBand(finalWar),
                });
            }
            players.AddRange(seasonRows);

            foreach (var role in new[] { "SP", "RP" })
            {
                var roleRows = seasonRows.Where(x => x.Role == role).ToList();
                var candidateRows = roleRows.Where(x => x.Candidate == "Y").ToList();
                var wars = roleRows.Select(x => x.FinalWar).OrderBy(x => x).ToArray();
                var candidateWars = candidateRows.Select(x => x.FinalWar).OrderBy(x => x).ToArray();
                summaries.Add(new WarDistributionSummaryRow
                {
                    Year = season.Year,
                    Role = role,
                    PlayerCount = roleRows.Count,
                    TotalWar = roleRows.Sum(x => x.FinalWar),
                    MeanWar = wars.Length > 0 ? wars.Average() : 0.0,
                    MedianWar = PercentileWar(wars, 0.50) ?? 0.0,
                    P25War = PercentileWar(wars, 0.25) ?? 0.0,
                    P75War = PercentileWar(wars, 0.75) ?? 0.0,
                    P90War = PercentileWar(wars, 0.90) ?? 0.0,
                    WarAtLeast5 = roleRows.Count(x => x.FinalWar >= 5.0),
                    WarAtLeast4 = roleRows.Count(x => x.FinalWar >= 4.0),
                    WarAtLeast3 = roleRows.Count(x => x.FinalWar >= 3.0),
                    WarAtLeast2 = roleRows.Count(x => x.FinalWar >= 2.0),
                    WarAtLeast1 = roleRows.Count(x => x.FinalWar >= 1.0),
                    WarZeroToOne = roleRows.Count(x => x.FinalWar >= 0.0 && x.FinalWar < 1.0),
                    WarBelowZero = roleRows.Count(x => x.FinalWar < 0.0),
                    CandidateCount = candidateRows.Count,
                    CandidateMeanWar = candidateWars.Length > 0 ? candidateWars.Average() : null,
                    CandidateMedianWar = PercentileWar(candidateWars, 0.50),
                    CandidateP25War = PercentileWar(candidateWars, 0.25),
                    CandidateP75War = PercentileWar(candidateWars, 0.75),
                    CandidateWithin025 = CandidatePercent(candidateWars, x => Math.Abs(x) <= 0.25),
                    CandidateWithin050 = CandidatePercent(candidateWars, x => Math.Abs(x) <= 0.50),
                    CandidateBelowZeroPercent = CandidatePercent(candidateWars, x => x < 0.0),
                    TopWar = wars.Length > 0 ? wars[^1] : null,
                    StarterReplacementFipMinus = spMinus,
                    RelieverReplacementFipMinus = rpMinus,
                    WarIp = warIp,
                });
            }
        }

        return new WarDistributionDiagnosticBundle
        {
            Summaries = summaries.OrderBy(x => x.Year).ThenBy(x => x.Role).ToList(),
            Players = players.OrderByDescending(x => x.Year).ThenBy(x => x.Role).ThenByDescending(x => x.FinalWar).ToList(),
        };
    }

    private static double? PercentileWar(IEnumerable<double> values, double p)
    {
        var a = values.Where(double.IsFinite).OrderBy(x => x).ToArray();
        if (a.Length == 0) return null;
        if (a.Length == 1) return a[0];
        var pos = Math.Clamp(p, 0.0, 1.0) * (a.Length - 1);
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return lo == hi ? a[lo] : a[lo] + (a[hi] - a[lo]) * (pos - lo);
    }

    private static double? CandidatePercent(IReadOnlyCollection<double> values, Func<double, bool> predicate)
        => values.Count == 0 ? null : 100.0 * values.Count(predicate) / values.Count;

    private static string WarBand(double war) => war switch
    {
        >= 5.0 => "5+",
        >= 4.0 => "4~5",
        >= 3.0 => "3~4",
        >= 2.0 => "2~3",
        >= 1.0 => "1~2",
        >= 0.0 => "0~1",
        _ => "<0",
    };
}
