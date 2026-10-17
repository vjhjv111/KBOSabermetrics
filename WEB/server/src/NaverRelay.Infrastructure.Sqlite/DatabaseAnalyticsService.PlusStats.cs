using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseAnalyticsService
{
    // The comparison population deliberately ignores team, opponent, player, venue,
    // qualification and situation filters. Dates/competition define the reference period.
    public static GameQuery PlusReferenceQuery(GameQuery q) => new()
    {
        SeasonYear = q.SeasonYear, Competition = q.Competition,
        StartDate = q.StartDate, EndDate = q.EndDate,
        Grouping = AnalyticsGrouping.PlayerByTeam
    };

    private async Task AttachPlusStatsAsync(GameQuery query, WarehouseAnalyticsData data,
        AnalyticsSnapshot result, bool pitcher, CancellationToken token)
    {
        var referenceQuery = PlusReferenceQuery(query);
        var key = $"plus-reference-v1:{pitcher}:{referenceQuery.CacheKey}";
        var reference = await _database.TryLoadComputedAsync<PlusStatsReference>(key, token).ConfigureAwait(false);
        if (reference is null)
        {
            var population = await _database.GetWebRoleAggregateDataAsync(referenceQuery, pitcher, token).ConfigureAwait(false);
            if (pitcher)
            {
                var p = population.Pitchers;
                reference = new()
                {
                    K = PlusStatsMath.Rate(p.Sum(x => (double)x.StrikeoutsFromPlateAppearances), p.Sum(x => (double)x.BattersFaced)),
                    BB = PlusStatsMath.Rate(p.Sum(x => (double)x.WalksFromPlateAppearances), p.Sum(x => (double)x.BattersFaced)),
                    ERA = PlusStatsMath.Rate(p.Sum(x => (double)x.EarnedRuns) * 27, p.Sum(x => (double)x.InningsOuts))
                };
            }
            else
            {
                var p = population.Batters;
                var ab = p.Sum(x => (double)x.AtBats); var h = p.Sum(x => (double)x.Hits);
                var bb = p.Sum(x => (double)x.Walks); var hbp = p.Sum(x => (double)x.HitByPitch);
                var sf = p.Sum(x => (double)x.SacrificeFlies); var k = p.Sum(x => (double)x.Strikeouts);
                var hr = p.Sum(x => (double)x.HomeRuns); var pa = p.Sum(x => (double)x.PlateAppearances);
                var avg = PlusStatsMath.Rate(h, ab); var slg = PlusStatsMath.Rate(p.Sum(x => (double)x.TotalBases), ab);
                reference = new()
                {
                    AVG = avg, SLG = slg, ISO = slg - avg,
                    OBP = PlusStatsMath.Rate(h + bb + hbp, ab + bb + hbp + sf),
                    BABIP = PlusStatsMath.Rate(h - hr, ab - k - hr + sf),
                    BB = PlusStatsMath.Rate(bb, pa), K = PlusStatsMath.Rate(k, pa)
                };
            }
            await _database.SaveComputedAsync(key, reference, token).ConfigureAwait(false);
        }
        if (!pitcher)
        {
            var classic = result.BatterClassic.ToDictionary(x => PlayerKey(x.Pcode, x.TeamCode));
            foreach (var row in result.BatterSabermetrics)
            {
                classic.TryGetValue(PlayerKey(row.Pcode, row.TeamCode), out var c);
                row.AveragePlus = PlusStatsMath.Index(c?.AVG, reference.AVG);
                row.OnBasePlus = PlusStatsMath.Index(c?.OBP, reference.OBP);
                row.SluggingPlus = PlusStatsMath.Index(c?.SLG, reference.SLG);
                row.IsoPlus = PlusStatsMath.Index(row.Iso, reference.ISO);
                row.BabipPlus = PlusStatsMath.Index(row.Babip, reference.BABIP);
                row.WalkRatePlus = PlusStatsMath.Index(row.WalkRate, reference.BB);
                row.StrikeoutRatePlus = PlusStatsMath.Index(row.StrikeoutRate, reference.K);
            }
            return;
        }

        // Run PF, not the FIP-based WAR PF. Missing/unsupported park samples must not
        // silently become an assumed neutral park. Career ERA- needs season exposures.
        List<ParkFactorByHitTypeRow> parks = [];
        if (!query.HasSituationFilters && query.SeasonYear.HasValue && query.Competition == "정규시즌")
        {
            const string parkKey = "plus-run-parks-v1";
            parks = await _database.TryLoadComputedAsync<List<ParkFactorByHitTypeRow>>(parkKey, token).ConfigureAwait(false)
                ?? await _database.GetParkFactorByHitTypeAsync(token).ConfigureAwait(false);
            await _database.SaveComputedAsync(parkKey, parks, token).ConfigureAwait(false);
        }
        var parkMap = parks.Where(x => x.Year == query.SeasonYear)
            .ToDictionary(x => x.Stadium, x => x.RunParkFactor, StringComparer.OrdinalIgnoreCase);
        var raw = data.Pitchers.ToDictionary(x => PlayerKey(x.Pcode, x.TeamCode));
        foreach (var row in result.PitcherSabermetrics)
        {
            row.StrikeoutRatePlus = PlusStatsMath.Index(row.StrikeoutRate, reference.K);
            row.WalkRatePlus = PlusStatsMath.Index(row.WalkRate, reference.BB);
            if (!raw.TryGetValue(PlayerKey(row.Pcode, row.TeamCode), out var p) || query.HasSituationFilters || p.InningsOuts <= 0) continue;
            var exposure = p.StadiumOuts.Where(x => x.Value > 0).ToArray();
            if (exposure.Length == 0 || exposure.Sum(x => x.Value) != p.InningsOuts ||
                exposure.Any(x => !parkMap.TryGetValue(x.Key, out var pf) || pf is null or <= 0 or >= 200)) continue;
            var pfWeighted = exposure.Sum(x => x.Value * parkMap[x.Key]!.Value) / p.InningsOuts;
            row.EraMinus = PlusStatsMath.EraMinus(p.EarnedRuns * 27.0 / p.InningsOuts, reference.ERA, pfWeighted);
        }
    }
}
