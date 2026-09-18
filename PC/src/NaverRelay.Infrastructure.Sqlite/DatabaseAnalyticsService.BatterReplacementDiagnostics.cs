using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseAnalyticsService
{
    public async Task<BatterReplacementDiagnosticBundle> GetBatterReplacementDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        var catalog = await _database.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var league = await _database.GetLeagueReferenceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var summaries = new List<ReplacementTeamDiagnosticRow>();
        var candidateRows = new List<BatterReplacementCandidateRow>();

        foreach (var year in catalog.Years.Where(x => x >= 2020).OrderBy(x => x))
        {
            var query = new GameQuery
            {
                Grouping = AnalyticsGrouping.PlayerByTeam,
                SeasonYear = year,
                Competition = "kbo_r",
            };
            var data = await _database.GetAggregateDataAsync(query, progress: null, cancellationToken)
                .ConfigureAwait(false);
            if (data.Batters.Count == 0) continue;

            var gameCount = (int)Math.Round(data.TeamGames.Values.Sum() / 2.0, MidpointRounding.AwayFromZero);
            if (gameCount <= 0) continue;

            var wobaConstants = league.GetWobaConstants(year);
            var season = BuildBatterReplacementSeasonContext(data, wobaConstants);
            var eligible = data.Batters.Where(x => x.PlateAppearances >= 20)
                .OrderBy(x => x.PlateAppearances).ThenBy(x => x.Pcode, StringComparer.Ordinal).ToList();
            if (eligible.Count == 0) continue;

            var p35Index = Math.Clamp((int)Math.Floor((eligible.Count - 1) * 0.35), 0, eligible.Count - 1);
            var p35Threshold = Math.Min(150, Math.Max(20, eligible[p35Index].PlateAppearances));

            var metrics = eligible.Select(row => BuildBatterReplacementMetric(row, season, wobaConstants)).ToList();
            foreach (var m in metrics)
            {
                candidateRows.Add(new BatterReplacementCandidateRow
                {
                    Year = year, Pcode = m.Row.Pcode, Name = m.Row.Name, TeamCode = m.Row.TeamCode,
                    Games = m.Row.Games, PlateAppearances = m.Row.PlateAppearances,
                    WrcPlus = m.RawWrcPlus, RegressedWrcPlus = m.RegressedWrcPlus,
                    WraaPer600Pa = m.RawWraaPerPa * 600.0,
                    RegressedBattingRunsPer600 = m.RegressedWraaPerPa * 600.0,
                    RunningRunsPer600 = m.RunningRunsPerPa * 600.0,
                    In20To79 = m.Row.PlateAppearances <= 79 ? "Y" : "",
                    In20To99 = m.Row.PlateAppearances <= 99 ? "Y" : "",
                    In20To149 = m.Row.PlateAppearances <= 149 ? "Y" : "",
                    InBottom35 = m.Row.PlateAppearances <= p35Threshold ? "Y" : "",
                });
            }

            var policies = new[]
            {
                (Name: "PA 20-79", MaxPa: 79),
                (Name: "PA 20-99", MaxPa: 99),
                (Name: "PA 20-149", MaxPa: 149),
                (Name: $"PA 하위35% (<= {p35Threshold})", MaxPa: p35Threshold),
            };

            var totalPitchingOuts = data.Pitchers.Sum(x => x.InningsOuts);
            var starterOuts = data.Pitchers.Sum(x => x.StarterInningsOuts);
            var relieverOuts = data.Pitchers.Sum(x => x.ReliefInningsOuts);
            var spShare = totalPitchingOuts > 0 ? starterOuts / (double)totalPitchingOuts : 0.60;
            var rpShare = totalPitchingOuts > 0 ? relieverOuts / (double)totalPitchingOuts : 0.40;
            var replacementPitchingFipMinus = spShare * 120.0 + rpShare * 115.0;

            var totalRunsAllowed = data.Pitchers.Sum(x => x.RunsAllowed);
            var leagueRaPerGame = totalRunsAllowed > 0
                ? totalRunsAllowed / (gameCount * 2.0)
                : season.LeagueRunsPerGame;

            foreach (var policy in policies)
            {
                var pool = metrics.Where(x => x.Row.PlateAppearances <= policy.MaxPa).ToList();
                if (pool.Count == 0) continue;

                var poolPa = pool.Sum(x => x.Row.PlateAppearances);
                var rawRate = WeightedRate(pool, x => x.RawWraaPerPa);
                var regressedRate = WeightedRate(pool, x => x.RegressedWraaPerPa);
                var runningRate = WeightedRate(pool, x => x.RunningRunsPerPa);
                var rawWrcPlus = season.LeagueRunsPerPa > 0 ? 100.0 * (1.0 + rawRate / season.LeagueRunsPerPa) : (double?)null;
                var regressedWrcPlus = season.LeagueRunsPerPa > 0 ? 100.0 * (1.0 + regressedRate / season.LeagueRunsPerPa) : (double?)null;

                // Use league PA/team-game as the playing-time volume for a replacement lineup.
                var replacementRsG = Math.Max(0.01,
                    season.LeagueRunsPerGame + (regressedRate + runningRate) * season.PaPerTeamGame);

                // SP120/RP115 were independently validated. Weight them by actual KBO role innings.
                var pitchingMultiplier = replacementPitchingFipMinus / 100.0;
                var replacementRaG = Math.Max(0.01, leagueRaPerGame * pitchingMultiplier);

                // PythagenPat exponent: (RS/G + RA/G)^0.287.
                var exponent = Math.Pow(replacementRsG + replacementRaG, 0.287);
                var rsPow = Math.Pow(replacementRsG, exponent);
                var raPow = Math.Pow(replacementRaG, exponent);
                var wpct = rsPow + raPow > 0 ? rsPow / (rsPow + raPow) : 0.5;
                var teamGames = gameCount * 2.0;
                var impliedWar = teamGames * (0.500 - wpct);

                // Runs-based replacement WPct cross-check.
                // Batter deficit is measured directly from regressed wRAA/PA plus baserunning.
                var batterRbaPerGame = -(regressedRate + runningRate) * season.PaPerTeamGame;

                // Pitcher deficit converts the independently validated SP120/RP115 FIP-
                // into runs below average using the season's FIP/RA environment.
                // We intentionally do NOT multiply RA/G by FIP- here.
                var leaguePitchingRunsPerGame = leagueRaPerGame;
                var pitcherRbaPerGame = leaguePitchingRunsPerGame * Math.Max(0.0, replacementPitchingFipMinus / 100.0 - 1.0);

                var totalRbaPerGame = batterRbaPerGame + pitcherRbaPerGame;
                var dynamicRpw = Math.Max(1.0, (season.LeagueRunsPerGame + leaguePitchingRunsPerGame + 2.0) * 0.75);
                // Wins below average per 144 games / 144 gives WPct gap from .500.
                var winsBelowAveragePerGame = totalRbaPerGame / dynamicRpw;
                var runsBasedWpct = Math.Clamp(0.500 - winsBelowAveragePerGame, 0.0, 0.500);
                var runsBasedImpliedWar = teamGames * (0.500 - runsBasedWpct);

                summaries.Add(new ReplacementTeamDiagnosticRow
                {
                    Year = year,
                    CandidatePolicy = policy.Name,
                    CandidateCount = pool.Count,
                    CandidatePa = poolPa,
                    PaUpperBound = policy.MaxPa,
                    RawWrcPlus = rawWrcPlus,
                    RegressedWrcPlus = regressedWrcPlus,
                    RegressedBattingRunsPer600 = regressedRate * 600.0,
                    RunningRunsPer600 = runningRate * 600.0,
                    LeagueRunsPerGame = season.LeagueRunsPerGame,
                    ReplacementRunsScoredPerGame = replacementRsG,
                    StarterInningsShare = spShare,
                    RelieverInningsShare = rpShare,
                    ReplacementPitchingFipMinus = replacementPitchingFipMinus,
                    ReplacementRunsAllowedPerGame = replacementRaG,
                    PythagenPatExponent = exponent,
                    EstimatedReplacementWinningPercentage = wpct,
                    ExpectedWinsPer144 = wpct * 144.0,
                    ExpectedLossesPer144 = (1.0 - wpct) * 144.0,
                    ImpliedLeagueWar = impliedWar,
                    BatterRunsBelowAveragePerGame = batterRbaPerGame,
                    PitcherRunsBelowAveragePerGame = pitcherRbaPerGame,
                    TotalRunsBelowAveragePerGame = totalRbaPerGame,
                    DynamicRunsPerWin = dynamicRpw,
                    RunsBasedReplacementWinningPercentage = runsBasedWpct,
                    RunsBasedExpectedWinsPer144 = runsBasedWpct * 144.0,
                    RunsBasedImpliedLeagueWar = runsBasedImpliedWar,
                });
            }
        }

        return new BatterReplacementDiagnosticBundle
        {
            Summaries = summaries,
            Candidates = candidateRows,
        };
    }

    private static BatterReplacementSeasonContext BuildBatterReplacementSeasonContext(
        WarehouseAnalyticsData data,
        WobaConstants constants)
    {
        var pa = data.Batters.Sum(x => x.PlateAppearances);
        var runs = data.Batters.Sum(x => x.RunsScoredOnPlays);
        var teamGames = Math.Max(1.0, data.TeamGames.Values.Sum());
        return new BatterReplacementSeasonContext
        {
            LeagueWoba = constants.LeagueWoba,
            LeagueRunsPerPa = pa > 0 ? runs / (double)pa : 0.0,
            LeagueRunsPerGame = runs / teamGames,
            PaPerTeamGame = pa / teamGames,
        };
    }

    private static BatterReplacementMetric BuildBatterReplacementMetric(
        BatterAggregateRecord row,
        BatterReplacementSeasonContext season,
        WobaConstants constants)
    {
        var woba = constants.Calculate(
            row.AtBats, row.Walks, row.IntentionalWalks, row.HitByPitch,
            row.SacrificeFlies, row.Singles, row.Doubles, row.Triples, row.HomeRuns)
            ?? season.LeagueWoba;
        var rawWraaPerPa = row.PlateAppearances > 0
            ? (woba - season.LeagueWoba) / constants.Scale
            : 0.0;
        var reliability = row.PlateAppearances > 0
            ? row.PlateAppearances / (row.PlateAppearances + 100.0)
            : 0.0;
        var regressed = rawWraaPerPa * reliability;
        var rawWrcPlus = season.LeagueRunsPerPa > 0
            ? 100.0 * (1.0 + rawWraaPerPa / season.LeagueRunsPerPa)
            : (double?)null;
        var regressedWrcPlus = season.LeagueRunsPerPa > 0
            ? 100.0 * (1.0 + regressed / season.LeagueRunsPerPa)
            : (double?)null;
        var running = row.PlateAppearances > 0
            ? (row.StolenBases * 0.20 - row.CaughtStealing * 0.40) / row.PlateAppearances
            : 0.0;
        return new BatterReplacementMetric(row, rawWraaPerPa, regressed, running, rawWrcPlus, regressedWrcPlus);
    }

    private static double WeightedRate(
        IReadOnlyList<BatterReplacementMetric> pool,
        Func<BatterReplacementMetric, double> selector)
    {
        var pa = pool.Sum(x => x.Row.PlateAppearances);
        return pa > 0 ? pool.Sum(x => selector(x) * x.Row.PlateAppearances) / pa : 0.0;
    }

    private sealed record BatterReplacementMetric(
        BatterAggregateRecord Row,
        double RawWraaPerPa,
        double RegressedWraaPerPa,
        double RunningRunsPerPa,
        double? RawWrcPlus,
        double? RegressedWrcPlus);

    private sealed class BatterReplacementSeasonContext
    {
        public double LeagueWoba { get; init; }
        public double LeagueRunsPerPa { get; init; }
        public double LeagueRunsPerGame { get; init; }
        public double PaPerTeamGame { get; init; }
    }
}
