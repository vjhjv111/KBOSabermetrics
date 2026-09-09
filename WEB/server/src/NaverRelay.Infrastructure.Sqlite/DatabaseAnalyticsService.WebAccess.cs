using System.Text.Json;
using NaverRelay.Application.Queries;
namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseAnalyticsService
{
    /// <summary>Same V3 Build method as the desktop; skip the unused role's SQL only.</summary>
    public async Task<AnalyticsSnapshot> GetWebRoleSnapshotAsync(GameQuery query, LeagueReference league,
        bool pitcher, CancellationToken cancellationToken = default)
    {
        var key = $"web-role-v3.1:{pitcher}:{JsonSerializer.Serialize(query)}";
        var cached = await _database.TryLoadComputedAsync<AnalyticsSnapshot>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null) return cached;
        var data = await _database.GetWebRoleAggregateDataAsync(query, pitcher, cancellationToken).ConfigureAwait(false);
        var result = Build(data, league);
        await _database.SaveComputedAsync(key, result, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
