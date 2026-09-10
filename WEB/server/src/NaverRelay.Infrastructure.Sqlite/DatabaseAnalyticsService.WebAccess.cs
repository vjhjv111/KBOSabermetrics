using System.Text.Json;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseAnalyticsService
{
    /// <summary>Web role-specific snapshot using the same KBO Pitcher WAR v4 Build path as desktop.</summary>
    public async Task<AnalyticsSnapshot> GetWebRoleSnapshotAsync(
        GameQuery query,
        LeagueReference league,
        bool pitcher,
        CancellationToken cancellationToken = default)
    {
        var key = $"web-role-v4.1:{pitcher}:{JsonSerializer.Serialize(query)}";
        var cached = await _database.TryLoadComputedAsync<AnalyticsSnapshot>(key, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null) return cached;

        var data = await _database.GetWebRoleAggregateDataAsync(query, pitcher, cancellationToken)
            .ConfigureAwait(false);
        var result = Build(data, league, query.SeasonYear);
        await _database.SaveComputedAsync(key, result, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
