using System.Text.Json;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseAnalyticsService
{
    public async Task<AnalyticsSnapshot> GetWebRoleSnapshotAsync(
        GameQuery query, LeagueReference league, bool pitcher,
        bool includeTeamBattingContext = false,
        CancellationToken cancellationToken = default)
    {
        var key = $"web-role-common-war-v1:{pitcher}:{JsonSerializer.Serialize(query)}";
        var cached = await _database.TryLoadComputedAsync<AnalyticsSnapshot>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            if (!pitcher && includeTeamBattingContext)
                await AttachTeamBattingContextAsync(query, cached, cancellationToken).ConfigureAwait(false);
            return cached;
        }
        var data = await _database.GetWebRoleAggregateDataAsync(query, pitcher, cancellationToken).ConfigureAwait(false);
        if (!pitcher && includeTeamBattingContext)
            await AttachTeamBattingContextAsync(query, data, cancellationToken).ConfigureAwait(false);
        var allocation = await GetWarAllocationCalibrationAsync(query, league, cancellationToken).ConfigureAwait(false);
        var result = Build(data, league, query.SeasonYear, allocation, query.HasSituationFilters);
        await _database.SaveComputedAsync(key, result, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
