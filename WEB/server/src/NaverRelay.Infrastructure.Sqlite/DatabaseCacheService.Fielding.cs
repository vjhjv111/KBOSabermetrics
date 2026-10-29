using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    /// <summary>
    /// 수비 집계에 필요한 경기 원본(라인업·타석·주자 이벤트·교체)을 경기 필터 범위에서 읽습니다.
    /// 선수 선택(SelectedPlayerCodes)은 타자 기준 CTE라 수비 집계에는 적용하지 않습니다.
    /// </summary>
    public async Task<IReadOnlyList<FieldingGameSource>> QueryFieldingGamesAsync(GameQuery query, CancellationToken cancellationToken = default)
    {
        var filter = BuildFilteredGamesCte(query with { SelectedPlayerCodes = null, PitcherStartsOnly = null });
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        var games = new Dictionary<string, (string? Home, string? Away, int? HomeErrors, int? AwayErrors)>(StringComparer.Ordinal);
        var order = new List<string>();
        var lineups = new Dictionary<string, List<FieldingLineupSource>>(StringComparer.Ordinal);
        var plateAppearances = new Dictionary<string, List<FieldingPlateAppearanceSource>>(StringComparer.Ordinal);
        var runners = new Dictionary<string, List<FieldingRunnerSource>>(StringComparer.Ordinal);
        var changes = new Dictionary<string, List<FieldingChangeSource>>(StringComparer.Ordinal);

        await ReadAsync("SELECT GameId,HomeTeamCode,AwayTeamCode,HomeErrors,AwayErrors FROM FilteredGames ORDER BY GameDate,GameId", reader =>
        {
            var id = reader.GetString(0);
            if (games.TryAdd(id, (Text(reader, 1), Text(reader, 2), Int(reader, 3), Int(reader, 4)))) order.Add(id);
        }).ConfigureAwait(false);
        await ReadAsync("""
            SELECT gp.GameId,gp.Pcode,gp.TeamCode,gp.Name,gp.Position,gp.LineupSequence FROM GamePlayers gp
            WHERE gp.GameId IN (SELECT GameId FROM FilteredGames)
            """, reader => Add(lineups, reader.GetString(0), new FieldingLineupSource(reader.GetString(1), reader.GetString(2), reader.GetString(3),
            Text(reader, 4), Int(reader, 5)))).ConfigureAwait(false);
        await ReadAsync("""
            SELECT pa.GameId,pa.PlateAppearanceId,pa.SequenceNumber,pa.SourceRelayNo,pa.FieldingTeamCode,pa.BattingTeamCode,
              pa.ResultText,pa.PrimaryFielder,pa.IsOut,pa.OutsRecorded,pa.IsStrikeout
            FROM PlateAppearances pa WHERE pa.GameId IN (SELECT GameId FROM FilteredGames)
            """, reader => Add(plateAppearances, reader.GetString(0), new FieldingPlateAppearanceSource(reader.GetString(1), reader.GetInt32(2),
            Int(reader, 3), Text(reader, 4), Text(reader, 5), Text(reader, 6), Text(reader, 7),
            reader.GetInt32(8) != 0, reader.GetInt32(9), reader.GetInt32(10) != 0))).ConfigureAwait(false);
        await ReadAsync("""
            SELECT r.GameId,r.PlateAppearanceId,r.SourceRelayNo,r.SourceSeqNo,r.TeamCode,r.Reason,r.IsOut,r.RawText
            FROM RunnerEvents r WHERE r.GameId IN (SELECT GameId FROM FilteredGames)
            """, reader => Add(runners, reader.GetString(0), new FieldingRunnerSource(Text(reader, 1), Int(reader, 2), Int(reader, 3),
            Text(reader, 4), reader.GetInt32(5), reader.GetInt32(6) != 0, Text(reader, 7)))).ConfigureAwait(false);
        await ReadAsync("""
            SELECT c.GameId,c.SourceRelayNo,c.SourceSeqNo,c.SourceOptionIndex,c.TeamCode,c.ChangeType,
              c.OutPlayerPcode,c.OutPosition,c.InPlayerPcode,c.InPosition,c.ShiftPlayerPcode,c.OldPosition,c.NewPosition
            FROM PlayerChanges c WHERE c.GameId IN (SELECT GameId FROM FilteredGames)
            """, reader => Add(changes, reader.GetString(0), new FieldingChangeSource(Int(reader, 1), Int(reader, 2), reader.GetInt32(3),
            Text(reader, 4), reader.GetInt32(5), Text(reader, 6), Text(reader, 7), Text(reader, 8), Text(reader, 9), Text(reader, 10), Text(reader, 11), Text(reader, 12)))).ConfigureAwait(false);

        return order.Select(id => new FieldingGameSource(id, games[id].Home, games[id].Away, games[id].HomeErrors, games[id].AwayErrors,
            lineups.GetValueOrDefault(id) ?? [], plateAppearances.GetValueOrDefault(id) ?? [],
            runners.GetValueOrDefault(id) ?? [], changes.GetValueOrDefault(id) ?? [])).ToList();

        async Task ReadAsync(string sql, Action<SqliteDataReader> row)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = filter.Cte + " " + sql;
            AddParameters(command, filter.Parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) row(reader);
        }
        static void Add<T>(Dictionary<string, List<T>> target, string gameId, T item)
        {
            if (!target.TryGetValue(gameId, out var list)) target[gameId] = list = new List<T>();
            list.Add(item);
        }
        static string? Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
        static int? Int(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
    }
}
