using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed class DatabaseBatterRecordRoomService : IBatterRecordRoomQueryService
{
    private const string CacheVersion = "batter-record-room-v2-lastpitch-index";
    private readonly DatabaseCacheService _database;

    public DatabaseBatterRecordRoomService(DatabaseCacheService database) => _database = database;

    public async Task<IReadOnlyList<BatterClutchRecordRow>> GetClutchAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken = default)
    {
        var key = $"{CacheVersion}:clutch:{query.CacheKey}";
        var cached = await _database.TryLoadComputedAsync<List<BatterClutchRecordRow>>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null) return cached;
        var rows = (await _database.QueryBatterClutchAsync(query, league, cancellationToken).ConfigureAwait(false)).ToList();
        await _database.SaveComputedAsync(key, rows, cancellationToken).ConfigureAwait(false);
        return rows;
    }

    public async Task<IReadOnlyList<BatterBattedBallRecordRow>> GetBattedBallAsync(
        GameQuery query,
        CancellationToken cancellationToken = default)
    {
        var key = $"{CacheVersion}:batted-ball:{query.CacheKey}";
        var cached = await _database.TryLoadComputedAsync<List<BatterBattedBallRecordRow>>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null) return cached;
        var rows = (await _database.QueryBatterBattedBallAsync(query, cancellationToken).ConfigureAwait(false)).ToList();
        await _database.SaveComputedAsync(key, rows, cancellationToken).ConfigureAwait(false);
        return rows;
    }

    public async Task<IReadOnlyList<BatterDirectionRecordRow>> GetDirectionAsync(
        GameQuery query,
        CancellationToken cancellationToken = default)
    {
        var key = $"{CacheVersion}:direction:{query.CacheKey}";
        var cached = await _database.TryLoadComputedAsync<List<BatterDirectionRecordRow>>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null) return cached;
        var rows = (await _database.QueryBatterDirectionAsync(query, cancellationToken).ConfigureAwait(false)).ToList();
        await _database.SaveComputedAsync(key, rows, cancellationToken).ConfigureAwait(false);
        return rows;
    }

    public async Task<IReadOnlyList<BatterPitchTypeRecordRow>> GetPitchTypesAsync(
        GameQuery query,
        CancellationToken cancellationToken = default)
    {
        var key = $"{CacheVersion}:pitch-types:{query.CacheKey}";
        var cached = await _database.TryLoadComputedAsync<List<BatterPitchTypeRecordRow>>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null) return cached;
        var rows = (await _database.QueryBatterPitchTypesAsync(query, cancellationToken).ConfigureAwait(false)).ToList();
        await _database.SaveComputedAsync(key, rows, cancellationToken).ConfigureAwait(false);
        return rows;
    }
}
