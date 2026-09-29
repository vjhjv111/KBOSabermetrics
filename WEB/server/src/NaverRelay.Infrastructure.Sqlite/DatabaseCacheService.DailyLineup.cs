using System.Globalization;
using KboRelayDownloader;
using Microsoft.Data.Sqlite;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed record DailyLineupBatterRow(int BatOrder, string? Pcode, string Name, string? Position);
public sealed record DailyLineupPitcherRow(string? Pcode, string Name);

public sealed record EstimatedLineup(
    IReadOnlyList<DailyLineupBatterRow> Batters,
    DailyLineupPitcherRow? StartingPitcher,
    string SourceGameId,
    string SourceGameDate);

public sealed record DailyLineupEntryRow(string GameDate, string TeamCode, string? OpponentTeamCode, string? GameId,
    string Role, int BatOrder, string? Pcode, string PlayerName, string? Position, bool IsOfficial, string Source, string UpdatedUtc);

public partial class DatabaseCacheService
{
    public async Task<bool> HasOfficialDailyLineupAsync(string gameDate, string teamCode, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM DailyLineupEntries WHERE GameDate=$date AND TeamCode=$team AND IsOfficial=1 LIMIT 1;";
        command.Parameters.AddWithValue("$date", gameDate);
        command.Parameters.AddWithValue("$team", teamCode);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null;
    }

    public async Task<IReadOnlyList<DailyLineupEntryRow>> GetDailyLineupEntriesAsync(string gameDate, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GameDate, TeamCode, OpponentTeamCode, GameId, Role, BatOrder, Pcode, PlayerName, Position, IsOfficial, Source, UpdatedUtc
            FROM DailyLineupEntries
            WHERE GameDate=$date
            ORDER BY TeamCode, Role DESC, BatOrder;
            """;
        command.Parameters.AddWithValue("$date", gameDate);
        var rows = new List<DailyLineupEntryRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new(
                reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4), reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetInt32(9) != 0, reader.GetString(10), reader.GetString(11)));
        }
        return rows;
    }

    public Task UpsertOfficialDailyLineupAsync(string gameDate, string teamCode, string opponentCode, string gameId,
        TeamLineupPreview lineup, CancellationToken ct = default)
    {
        var batters = lineup.Batters.Select(b =>
        {
            var order = b.RowOrder;
            if (b.Row.TryGetValue("타순", out var orderText) &&
                int.TryParse(orderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and <= 9)
                order = parsed;
            b.Row.TryGetValue("포지션", out var position);
            return new DailyLineupBatterRow(Math.Clamp(order, 1, 9), null, b.Name, position);
        }).ToList();
        var pitcher = lineup.StartingPitcher is { } p ? new DailyLineupPitcherRow(null, p.Name) : null;
        return ReplaceDailyLineupAsync(gameDate, teamCode, opponentCode, gameId, batters, pitcher, isOfficial: true, source: "official", ct);
    }

    public Task UpsertEstimatedDailyLineupAsync(string gameDate, string teamCode, string opponentCode, string gameId,
        EstimatedLineup lineup, CancellationToken ct = default)
        => ReplaceDailyLineupAsync(gameDate, teamCode, opponentCode, gameId, lineup.Batters, lineup.StartingPitcher,
            isOfficial: false, source: $"estimated_previous_game:{lineup.SourceGameDate}", ct);

    /// <summary>그날 공식 라인업(타자 전체)이 아직 발표되지 않았지만, 네이버 game-polling API가
    /// 미리 알려주는 "예고선발"(ProbableStarterFetcher)은 있을 때 씁니다. 직전 경기를 베껴오는
    /// 추측(estimated_previous_game)과 달리 KBO/네이버가 실제로 발표한 값이지만, 아직 확정된
    /// 그날 최종 라인업은 아니라서 IsOfficial=0으로 남겨 둡니다 — 이후 주기에 진짜 공식 라인업이
    /// 뜨면(ApplyTeamAsync의 official 분기) 그대로 덮어써집니다.</summary>
    public Task UpsertProbableStarterAsync(string gameDate, string teamCode, string opponentCode, string gameId,
        string? pcode, string name, CancellationToken ct = default)
        => ReplaceDailyLineupAsync(gameDate, teamCode, opponentCode, gameId, Array.Empty<DailyLineupBatterRow>(),
            new DailyLineupPitcherRow(pcode, name), isOfficial: false, source: "probable_starter", ct);

    /// <summary>그날 공식 선발 라인업이 아직 발표되지 않았을 때 씁니다. 예전엔 직전 경기 라인업을
    /// 추정치로 채웠지만, 이제는 그 팀 라인업을 그냥 비워 둡니다(호출자가 이미 공식 라인업이 없는
    /// 걸 확인한 뒤에만 불러서, 확정된 공식 라인업을 실수로 지우지 않습니다).</summary>
    public async Task ClearDailyLineupAsync(string gameDate, string teamCode, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DailyLineupEntries WHERE GameDate=$date AND TeamCode=$team;";
        command.Parameters.AddWithValue("$date", gameDate);
        command.Parameters.AddWithValue("$team", teamCode);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ReplaceDailyLineupAsync(string gameDate, string teamCode, string? opponentCode, string? gameId,
        IReadOnlyList<DailyLineupBatterRow> batters, DailyLineupPitcherRow? pitcher,
        bool isOfficial, string source, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM DailyLineupEntries WHERE GameDate=$date AND TeamCode=$team;";
            delete.Parameters.AddWithValue("$date", gameDate);
            delete.Parameters.AddWithValue("$team", teamCode);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        async Task InsertAsync(string role, int batOrder, string? pcode, string name, string? position)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            // INSERT OR REPLACE로 둡니다 — 라이브 HTML 파싱(LineupPreviewParser)이 가끔 같은
            // 타순(BatOrder)을 두 번 주는 등 불완전한 값을 내놓을 때, (GameDate,TeamCode,Role,
            // BatOrder) UNIQUE 제약 위반으로 이 트랜잭션 전체가 롤백되고(위의 DELETE까지 포함해서),
            // 그 여파로 DailyLineupWorker의 이번 주기 전체(이 팀 이후의 다른 경기들까지)가 통째로
            // 중단되는 걸 막기 위해서입니다. 중복 타순이 와도 마지막 값으로 덮어쓰고 계속 진행합니다.
            insert.CommandText = """
                INSERT OR REPLACE INTO DailyLineupEntries
                    (GameDate, TeamCode, OpponentTeamCode, GameId, Role, BatOrder, Pcode, PlayerName, Position, IsOfficial, Source, UpdatedUtc)
                VALUES ($date, $team, $opp, $game, $role, $order, $pcode, $name, $pos, $official, $source, $now);
                """;
            insert.Parameters.AddWithValue("$date", gameDate);
            insert.Parameters.AddWithValue("$team", teamCode);
            insert.Parameters.AddWithValue("$opp", (object?)opponentCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$game", (object?)gameId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$role", role);
            insert.Parameters.AddWithValue("$order", batOrder);
            insert.Parameters.AddWithValue("$pcode", (object?)pcode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$name", name);
            insert.Parameters.AddWithValue("$pos", (object?)position ?? DBNull.Value);
            insert.Parameters.AddWithValue("$official", isOfficial ? 1 : 0);
            insert.Parameters.AddWithValue("$source", source);
            insert.Parameters.AddWithValue("$now", now);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var batter in batters)
            await InsertAsync("batter", batter.BatOrder, batter.Pcode, batter.Name, batter.Position).ConfigureAwait(false);
        if (pitcher is not null)
            await InsertAsync("pitcher", 0, pitcher.Pcode, pitcher.Name, null).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 그 구단의 가장 최근 완료된 정규시즌 경기(beforeDate 이전)에서 실제로 나갔던 1~9번
    /// 타순과 선발투수를 그대로 복사해 옵니다. 공식 라인업이 아직 없을 때 하루짜리
    /// 추정치로만 사용합니다.
    /// </summary>
    public async Task<EstimatedLineup?> BuildEstimatedLineupFromRecentGameAsync(string teamCode, string beforeDate, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);

        string? sourceGameId = null, sourceGameDate = null;
        await using (var findGame = connection.CreateCommand())
        {
            findGame.CommandText = """
                SELECT GameId, GameDate FROM Games
                WHERE (HomeTeamCode=$team OR AwayTeamCode=$team) AND RoundCode='kbo_r'
                  AND GameDate < $date AND StatusCode IN ('RESULT','ENDED')
                ORDER BY GameDate DESC, GameId DESC LIMIT 1;
                """;
            findGame.Parameters.AddWithValue("$team", teamCode);
            findGame.Parameters.AddWithValue("$date", beforeDate);
            await using var reader = await findGame.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                sourceGameId = reader.GetString(0);
                sourceGameDate = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }
        if (sourceGameId is null) return null;

        var batters = new List<DailyLineupBatterRow>();
        await using (var battingCommand = connection.CreateCommand())
        {
            battingCommand.CommandText = """
                SELECT p.BatOrder, p.BatterPcode, p.BatterName
                FROM PlateAppearances p
                WHERE p.GameId=$game AND p.BattingTeamCode=$team AND p.BatOrder BETWEEN 1 AND 9
                  AND p.SequenceNumber = (
                      SELECT MIN(p2.SequenceNumber) FROM PlateAppearances p2
                      WHERE p2.GameId=p.GameId AND p2.BattingTeamCode=p.BattingTeamCode AND p2.BatOrder=p.BatOrder)
                ORDER BY p.BatOrder;
                """;
            battingCommand.Parameters.AddWithValue("$game", sourceGameId);
            battingCommand.Parameters.AddWithValue("$team", teamCode);
            await using var reader = await battingCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var order = reader.GetInt32(0);
                var pcode = reader.IsDBNull(1) ? null : reader.GetString(1);
                var name = reader.IsDBNull(2) ? "" : reader.GetString(2);
                if (string.IsNullOrWhiteSpace(name)) continue;
                batters.Add(new(order, pcode, name, null));
            }
        }

        DailyLineupPitcherRow? starter = null;
        await using (var pitcherCommand = connection.CreateCommand())
        {
            pitcherCommand.CommandText = "SELECT Pcode, Name FROM PitcherGameStats WHERE GameId=$game AND TeamCode=$team AND IsStarter=1 LIMIT 1;";
            pitcherCommand.Parameters.AddWithValue("$game", sourceGameId);
            pitcherCommand.Parameters.AddWithValue("$team", teamCode);
            await using var reader = await pitcherCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
                starter = new(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1));
        }

        if (batters.Count == 0 && starter is null) return null;
        return new EstimatedLineup(batters, starter, sourceGameId, sourceGameDate ?? "");
    }
}
