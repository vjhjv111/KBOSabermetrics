using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseAnalyticsService
{
    private const double BatterWarShare = 1.0 - KboPitcherWarMath.DefaultPitcherWarShare; // 57%
    private const double BatterRunsPerWin = 10.0;

    private sealed record WarAllocationCalibration(
        int GameCount,
        double TotalWarTarget,
        double BatterTargetWar,
        double PitcherTargetWar,
        double BatterReplacementRunsPerPa,
        double PitcherWarPerInning);

    private async Task<WarAllocationCalibration> GetWarAllocationCalibrationAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken)
    {
        // Allocation is based on the league-wide time scope, not on team/opponent/situation filters.
        // This means every player in the same season/date range receives the same WAR calibration rate.
        var scope = new GameQuery
        {
            Grouping = AnalyticsGrouping.PlayerByTeam,
            SeasonYear = query.SeasonYear,
            Competition = query.Competition,
            StartDate = query.StartDate,
            EndDate = query.EndDate,
            RecentGameCount = query.RecentGameCount,
        };
        var cacheKey = $"common-war-allocation-v1:{scope.CacheKey}";
        var cached = await _database.TryLoadComputedAsync<WarAllocationCalibration>(cacheKey, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null) return cached;

        var leagueData = await _database.GetAggregateDataAsync(scope, progress: null, cancellationToken)
            .ConfigureAwait(false);

        // Each game is present once for each participating team.
        var gameCount = (int)Math.Round(leagueData.TeamGames.Values.Sum() / 2.0, MidpointRounding.AwayFromZero);
        var totalWarTarget = KboPitcherWarMath.ComputeTotalReplacementWar(gameCount);
        var batterTargetWar = totalWarTarget * BatterWarShare;
        var pitcherTargetWar = totalWarTarget * KboPitcherWarMath.DefaultPitcherWarShare;

        // Batter allocation: preserve batting/running/position components and solve only replacement Runs/PA
        // so league batter WAR equals 57% of the common replacement-WAR pool.
        var batterSaber = leagueData.Batters.Select(row => BuildBatterSaber(row, league, query.SeasonYear)).ToList();
        var saberByKey = batterSaber.ToDictionary(
            row => PlayerKey(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var nonReplacementRuns = 0.0;
        var leaguePa = 0;
        foreach (var row in leagueData.Batters)
        {
            var saber = saberByKey.GetValueOrDefault(PlayerKey(row.Pcode, row.TeamCode));
            var runningRuns = row.StolenBases * 0.20 - row.CaughtStealing * 0.40;
            var battingRuns = saber?.Wraa ?? 0.0;
            nonReplacementRuns += battingRuns + runningRuns + row.Position.Runs;
            leaguePa += row.PlateAppearances;
        }
        var replacementRunsNeeded = batterTargetWar * BatterRunsPerWin - nonReplacementRuns;
        var batterReplacementRunsPerPa = leaguePa > 0
            ? replacementRunsNeeded / leaguePa
            : 20.0 / 600.0;
        // Guard only against corrupt/incomplete scope data; normal KBO values remain far inside this range.
        batterReplacementRunsPerPa = Math.Clamp(batterReplacementRunsPerPa, 0.0, 0.10);

        // Pitcher allocation: SP120/RP115 and PF v2 determine Pre-fWAR, then WARIP performs only
        // the final league-wide calibration to the 43% pitcher target for this exact time scope.
        var pitcherIp = 0.0;
        var pitcherPreWar = 0.0;
        foreach (var row in leagueData.Pitchers.Where(x => x.FinalGames > 0))
        {
            var value = BuildPitcherValue(row, league, scope.SeasonYear, pitcherWarPerInning: 0.0);
            pitcherIp += value.InningsPitched ?? 0.0;
            pitcherPreWar += value.WarBeforeCorrection ?? 0.0;
        }
        var pitcherWarPerInning = pitcherIp > 0
            ? (pitcherTargetWar - pitcherPreWar) / pitcherIp
            : 0.0;

        var result = new WarAllocationCalibration(
            gameCount,
            totalWarTarget,
            batterTargetWar,
            pitcherTargetWar,
            batterReplacementRunsPerPa,
            pitcherWarPerInning);
        await _database.SaveComputedAsync(cacheKey, result, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
