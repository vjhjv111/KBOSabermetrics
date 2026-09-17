using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    // Only small calculated results, never NormalizedGame or source documents.
    // The collector changes DataVersion after a committed daily import. Web readers
    // drop the previous version's calculated results without restarting the process.
    private readonly object _webCacheLock = new();
    private readonly Dictionary<string, (byte[] Data, DateTime At)> _webComputed = new(StringComparer.Ordinal);
    private readonly AsyncLocal<string?> _webComputationSourceVersion = new();
    private string? _webComputedVersion;
    private long _webComputedBytes;

    private bool TryReadWebComputed<T>(string sourceVersion, string key, out T? value)
    {
        byte[]? bytes = null;
        lock (_webCacheLock)
        {
            EnsureWebComputedVersionLocked(sourceVersion);
            if (_webComputed.TryGetValue(key, out var item)) bytes = item.Data;
        }
        value = bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        return bytes is not null;
    }
    private void SaveWebComputed<T>(string sourceVersion, string key, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (bytes.Length > 16 * 1024 * 1024) return;
        lock (_webCacheLock)
        {
            EnsureWebComputedVersionLocked(sourceVersion);
            if (_webComputed.Remove(key, out var old)) _webComputedBytes -= old.Data.Length;
            while (_webComputed.Count > 0 && (_webComputed.Count >= 64 || _webComputedBytes + bytes.Length > 64 * 1024 * 1024))
            {
                var oldest = _webComputed.MinBy(x => x.Value.At).Key;
                _webComputedBytes -= _webComputed[oldest].Data.Length;
                _webComputed.Remove(oldest);
            }
            _webComputed[key] = (bytes, DateTime.UtcNow);
            _webComputedBytes += bytes.Length;
        }
    }

    private void EnsureWebComputedVersionLocked(string sourceVersion)
    {
        if (string.Equals(_webComputedVersion, sourceVersion, StringComparison.Ordinal)) return;
        _webComputed.Clear();
        _webComputedBytes = 0;
        _webComputedVersion = sourceVersion;
    }

    public Task<string> GetWebSourceVersionAsync(CancellationToken token = default) => GetSourceVersionAsync(token);

    public async Task ValidateWebSchemaAsync(CancellationToken token = default)
    {
        if (!File.Exists(DatabasePath)) throw new FileNotFoundException("조회 DB가 없습니다.", DatabasePath);
        // Expected DDL is evaluated only in an in-memory connection, never in the user's DB.
        await using var expected = new SqliteConnection("Data Source=:memory:");
        await expected.OpenAsync(token).ConfigureAwait(false);
        await ExecuteAsync(expected, SchemaSql, token).ConfigureAwait(false);
        await using var actual = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3
        }.ToString());
        await actual.OpenAsync(token).ConfigureAwait(false);
        var tables = new List<string>();
        await using (var cmd = expected.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using var r = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await r.ReadAsync(token).ConfigureAwait(false)) tables.Add(r.GetString(0));
        }
        foreach (var table in tables)
        {
            var wanted = Columns(expected, table);
            var found = Columns(actual, table);
            var missing = wanted.Except(found, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length > 0) throw new InvalidDataException($"V3 스키마와 다릅니다: {table} 누락 열 {string.Join(", ", missing)}. 원본은 수정하지 않았습니다.");
        }
        static List<string> Columns(SqliteConnection db, string table)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(\"" + table.Replace("\"", "\"\"") + "\")";
            using var r = cmd.ExecuteReader(); var names = new List<string>();
            while (r.Read()) names.Add(r.GetString(1));
            return names;
        }
    }

    public async Task<(DateTime? Min, DateTime? Max)> GetWebDateBoundsAsync(GameQuery query, CancellationToken token)
    {
        var filter = BuildFilteredGamesCte(query);
        await using var con = await OpenAsync(token).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"{filter.Cte} SELECT MIN(GameDate),MAX(GameDate) FROM FilteredGames";
        AddParameters(cmd, filter.Parameters);
        await using var r = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await r.ReadAsync(token).ConfigureAwait(false)) return (null, null);
        return (ParseDate(r.IsDBNull(0) ? null : r.GetString(0)), ParseDate(r.IsDBNull(1) ? null : r.GetString(1)));
    }

    // 팀 완봉(SHO) 수를 계산합니다. 개인 완봉(한 투수가 9이닝을 혼자 던진 완봉)의 합으로
    // 구하면 안 됩니다 — 그렇게 하면 여러 투수가 이어 던져 무실점으로 막은 "합작 완봉"이
    // 통째로 빠집니다. 여기서는 필터링된 경기(FilteredGames)마다 상대팀 최종 점수가
    // 0점인지만 보고, 그 경기에서 어느 팀이 던졌든(선발 완봉이든 계투 조합이든) 그 팀의
    // 완봉으로 한 번만 셉니다.
    public async Task<IReadOnlyDictionary<string, int>> GetTeamShutoutsAsync(GameQuery query, CancellationToken token = default)
    {
        var filter = BuildFilteredGamesCte(query);
        await using var con = await OpenAsync(token).ConfigureAwait(false);
        await using var command = con.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT TeamCode, SUM(Shutout) FROM (
                SELECT HomeTeamCode AS TeamCode,
                    CASE WHEN HomeScore IS NOT NULL AND AwayScore IS NOT NULL AND AwayScore=0 THEN 1 ELSE 0 END AS Shutout
                FROM FilteredGames
                UNION ALL
                SELECT AwayTeamCode AS TeamCode,
                    CASE WHEN HomeScore IS NOT NULL AND AwayScore IS NOT NULL AND HomeScore=0 THEN 1 ELSE 0 END AS Shutout
                FROM FilteredGames
            ) WHERE TeamCode IS NOT NULL AND TRIM(TeamCode)<>'' GROUP BY TeamCode;
            """;
        AddParameters(command, filter.Parameters);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) result[reader.GetString(0)] = ReadInt32(reader, 1);
        return result;
    }

    internal async Task<WarehouseAnalyticsData> GetWebRoleAggregateDataAsync(GameQuery query, bool pitcher, CancellationToken token)
    {
        var filter = BuildFilteredGamesCte(query);
        await using var con = await OpenAsync(token).ConfigureAwait(false);
        var batters = new List<BatterAggregateRecord>();
        var pitchers = new List<PitcherAggregateRecord>();
        if (pitcher)
        {
            pitchers = query.HasSituationFilters
                ? await ReadSituationPitcherAggregatesAsync(con, filter, query, token).ConfigureAwait(false)
                : await ReadPitcherAggregatesAsync(con, filter, query.TeamCode, query.Grouping, token).ConfigureAwait(false);
            if (!query.HasSituationFilters)
                await AttachPitcherStadiumOutsAsync(con, filter, query.TeamCode, query.Grouping, pitchers, token).ConfigureAwait(false);
        }
        else
        {
            batters = query.HasSituationFilters
                ? await ReadSituationBatterAggregatesAsync(con, filter, query, token).ConfigureAwait(false)
                : await ReadBatterAggregatesAsync(con, filter, query.TeamCode, query.Grouping, token).ConfigureAwait(false);
            if (!query.HasSituationFilters)
                await AttachBatterStadiumPaAsync(con, filter, query.TeamCode, query.Grouping, batters, token).ConfigureAwait(false);
        }
        var teamGames = await ReadTeamGamesAsync(con, filter, token).ConfigureAwait(false);
        return new WarehouseAnalyticsData { Batters=batters, Pitchers=pitchers, TeamGames=teamGames };
    }
}
