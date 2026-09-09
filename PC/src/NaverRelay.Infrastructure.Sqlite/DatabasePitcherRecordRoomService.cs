using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

/// <summary>
/// 투수 기록실의 세부 탭을 관계형 SQLite에서 조회하고 DataVersion 기반 캐시에 저장합니다.
/// </summary>
public sealed class DatabasePitcherRecordRoomService : IPitcherRecordRoomQueryService
{
    private const string CacheVersion = "pitcher-record-room-v2";
    private readonly DatabaseCacheService _database;

    public DatabasePitcherRecordRoomService(DatabaseCacheService database) => _database = database;

    public Task<IReadOnlyList<PitcherExtendedRecordRow>> GetExtendedAsync(
        GameQuery query,
        CancellationToken cancellationToken = default) =>
        GetCachedAsync($"{CacheVersion}:extended:{query.CacheKey}",
            token => _database.QueryPitcherExtendedAsync(query, token), cancellationToken);

    public Task<IReadOnlyList<PitcherWinProbabilityRecordRow>> GetWinProbabilityAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken = default) =>
        GetCachedAsync($"{CacheVersion}:wp:{query.CacheKey}",
            token => _database.QueryPitcherWinProbabilityAsync(query, league, token), cancellationToken);

    public Task<IReadOnlyList<PitcherRunnerRecordRow>> GetRunnerAsync(
        GameQuery query,
        CancellationToken cancellationToken = default) =>
        GetCachedAsync($"{CacheVersion}:runner:{query.CacheKey}",
            token => _database.QueryPitcherRunnerAsync(query, token), cancellationToken);

    public Task<IReadOnlyList<PitcherStarterRecordRow>> GetStarterAsync(
        GameQuery query,
        CancellationToken cancellationToken = default) =>
        GetCachedAsync($"{CacheVersion}:starter:{query.CacheKey}",
            token => _database.QueryPitcherStarterAsync(query, token), cancellationToken);

    public Task<IReadOnlyList<PitcherRelieverRecordRow>> GetRelieverAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken = default) =>
        GetCachedAsync($"{CacheVersion}:reliever:{query.CacheKey}",
            token => _database.QueryPitcherRelieverAsync(query, league, token), cancellationToken);

    public Task<IReadOnlyList<PitcherBattedBallRecordRow>> GetBattedBallAsync(
        GameQuery query,
        CancellationToken cancellationToken = default) =>
        GetCachedAsync($"{CacheVersion}:batted-ball:{query.CacheKey}",
            token => _database.QueryPitcherBattedBallAsync(query, token), cancellationToken);

    public Task<IReadOnlyList<PitcherDirectionRecordRow>> GetDirectionAsync(
        GameQuery query,
        CancellationToken cancellationToken = default) =>
        GetCachedAsync($"{CacheVersion}:direction:{query.CacheKey}",
            token => _database.QueryPitcherDirectionAsync(query, token), cancellationToken);

    public Task<IReadOnlyList<PitcherPitchProfileRecordRow>> GetPitchProfileAsync(
        GameQuery query,
        CancellationToken cancellationToken = default) =>
        GetCachedAsync($"{CacheVersion}:pitch-profile:{query.CacheKey}",
            token => _database.QueryPitcherPitchProfileAsync(query, token), cancellationToken);

    public Task<IReadOnlyList<PitcherPitchTypeRecordRow>> GetPitchTypesAsync(
        GameQuery query,
        CancellationToken cancellationToken = default) =>
        GetCachedAsync($"{CacheVersion}:pitch-types:{query.CacheKey}",
            token => _database.QueryPitcherPitchTypesAsync(query, token), cancellationToken);

    private async Task<IReadOnlyList<T>> GetCachedAsync<T>(
        string key,
        Func<CancellationToken, Task<IReadOnlyList<T>>> factory,
        CancellationToken cancellationToken)
    {
        var cached = await _database.TryLoadComputedAsync<List<T>>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null) return cached;
        var rows = (await factory(cancellationToken).ConfigureAwait(false)).ToList();
        await _database.SaveComputedAsync(key, rows, cancellationToken).ConfigureAwait(false);
        return rows;
    }
}
