using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed record EntryRosterPlayer(string Role, string? Pcode, string Name, string? Position);
public sealed record DailyEntryRosterRow(string GameDate, string TeamCode, string Role, string Pcode,
    string PlayerName, string? Position, bool IsOfficial, string Source, string UpdatedUtc);

public partial class DatabaseCacheService
{
    public async Task<bool> HasOfficialEntryRosterAsync(string gameDate, string teamCode, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM DailyEntryRosters WHERE GameDate=$date AND TeamCode=$team AND IsOfficial=1 LIMIT 1;";
        command.Parameters.AddWithValue("$date", gameDate);
        command.Parameters.AddWithValue("$team", teamCode);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null;
    }

    public async Task<IReadOnlyList<DailyEntryRosterRow>> GetDailyEntryRosterAsync(string gameDate, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GameDate, TeamCode, Role, Pcode, PlayerName, Position, IsOfficial, Source, UpdatedUtc
            FROM DailyEntryRosters
            WHERE GameDate=$date
            ORDER BY TeamCode, Role DESC, PlayerName;
            """;
        command.Parameters.AddWithValue("$date", gameDate);
        var rows = new List<DailyEntryRosterRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6) != 0, reader.GetString(7), reader.GetString(8)));
        }
        return rows;
    }

    public Task UpsertOfficialEntryRosterAsync(string gameDate, string teamCode, IReadOnlyList<EntryRosterPlayer> players, CancellationToken ct = default)
        => ReplaceEntryRosterAsync(gameDate, teamCode, players, isOfficial: true, source: "official", ct);

    public Task UpsertEstimatedEntryRosterFromPreviousRosterAsync(string gameDate, string teamCode, IReadOnlyList<EntryRosterPlayer> players,
        string sourceDate, CancellationToken ct = default)
        => ReplaceEntryRosterAsync(gameDate, teamCode, players, isOfficial: false, source: $"estimated_previous_roster:{sourceDate}", ct);

    public Task UpsertEstimatedEntryRosterAsync(string gameDate, string teamCode, IReadOnlyList<EntryRosterPlayer> players,
        int lookbackDays, CancellationToken ct = default)
        => ReplaceEntryRosterAsync(gameDate, teamCode, players, isOfficial: false, source: $"estimated_recent_games:{lookbackDays}d", ct);

    private async Task ReplaceEntryRosterAsync(string gameDate, string teamCode, IReadOnlyList<EntryRosterPlayer> players,
        bool isOfficial, string source, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM DailyEntryRosters WHERE GameDate=$date AND TeamCode=$team;";
            delete.Parameters.AddWithValue("$date", gameDate);
            delete.Parameters.AddWithValue("$team", teamCode);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var player in players)
        {
            if (string.IsNullOrWhiteSpace(player.Pcode)) continue; // Pcode is part of the primary key.
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR REPLACE INTO DailyEntryRosters
                    (GameDate, TeamCode, Role, Pcode, PlayerName, Position, IsOfficial, Source, UpdatedUtc)
                VALUES ($date, $team, $role, $pcode, $name, $pos, $official, $source, $now);
                """;
            insert.Parameters.AddWithValue("$date", gameDate);
            insert.Parameters.AddWithValue("$team", teamCode);
            insert.Parameters.AddWithValue("$role", player.Role);
            insert.Parameters.AddWithValue("$pcode", player.Pcode);
            insert.Parameters.AddWithValue("$name", player.Name);
            insert.Parameters.AddWithValue("$pos", (object?)player.Position ?? DBNull.Value);
            insert.Parameters.AddWithValue("$official", isOfficial ? 1 : 0);
            insert.Parameters.AddWithValue("$source", source);
            insert.Parameters.AddWithValue("$now", now);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 네이버 game-polling의 그날 엔트리가 아직 없을 때 쓰는 1순위 대체 추정치의 기준이 되는, 이
    /// 팀의 가장 최근 실제(종료된) 경기를 찾습니다. DailyLineupWorker는 이 경기의 GameId로 네이버
    /// game-polling을 다시 호출해 그 경기 당일 실제로 발표됐던 문자중계 엔트리(homeEntry/awayEntry)를
    /// 가져다 씁니다 — 우리가 이미 저장해 둔 스냅샷을 재사용하는 게 아니라, 그 시점 KBO가 실제로
    /// 발표한 값을 다시 조회하는 것이라 더 정확합니다.
    /// </summary>
    public async Task<(string GameId, string GameDate)?> FindMostRecentPastGameAsync(
        string teamCode, string beforeDate, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GameId, GameDate FROM Games
            WHERE (HomeTeamCode=$team OR AwayTeamCode=$team) AND RoundCode='kbo_r'
              AND GameDate < $date AND StatusCode IN ('RESULT','ENDED')
            ORDER BY GameDate DESC, GameId DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$team", teamCode);
        command.Parameters.AddWithValue("$date", beforeDate);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var gameId = reader.GetString(0);
            var gameDate = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (!string.IsNullOrWhiteSpace(gameId)) return (gameId, gameDate);
        }
        return null;
    }

    /// <summary>
    /// DailyEntryRosters에 이 팀의 이전 스냅샷이 아예 없을 때만(기능 도입 첫 실행 등 부트스트랩
    /// 상황) 쓰는 2순위 대체 추정치입니다. 공식 엔트리 등록/말소 공지를 따로 안 보고, 최근
    /// lookbackDays일 동안 그 팀 경기에 실제로 출전한 타자/투수 전체를 "현재 엔트리에 가깝다"고
    /// 간주합니다. 말소 직후 며칠은 실제 엔트리에 없는 선수가 섞여 나올 수 있고, 콜업 이력이 쌓일수록
    /// 실제 엔트리보다 인원이 부풀 수 있는 근사치입니다.
    /// </summary>
    public async Task<IReadOnlyList<EntryRosterPlayer>?> BuildEstimatedEntryRosterFromRecentGamesAsync(
        string teamCode, string beforeDate, int lookbackDays, CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(beforeDate, CultureInfo.InvariantCulture, out var before)) return null;
        var since = before.AddDays(-lookbackDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var players = new List<EntryRosterPlayer>();

        await using (var battersCommand = connection.CreateCommand())
        {
            battersCommand.CommandText = """
                SELECT DISTINCT b.Pcode, b.Name
                FROM BatterGameStats b
                JOIN Games g ON g.GameId=b.GameId
                WHERE b.TeamCode=$team AND g.RoundCode='kbo_r' AND g.GameDate>=$since AND g.GameDate<$before;
                """;
            battersCommand.Parameters.AddWithValue("$team", teamCode);
            battersCommand.Parameters.AddWithValue("$since", since);
            battersCommand.Parameters.AddWithValue("$before", beforeDate);
            await using var reader = await battersCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var pcode = reader.IsDBNull(0) ? null : reader.GetString(0);
                var name = reader.IsDBNull(1) ? "" : reader.GetString(1);
                if (string.IsNullOrWhiteSpace(pcode) || string.IsNullOrWhiteSpace(name)) continue;
                players.Add(new("batter", pcode, name, null));
            }
        }

        await using (var pitchersCommand = connection.CreateCommand())
        {
            pitchersCommand.CommandText = """
                SELECT DISTINCT p.Pcode, p.Name
                FROM PitcherGameStats p
                JOIN Games g ON g.GameId=p.GameId
                WHERE p.TeamCode=$team AND g.RoundCode='kbo_r' AND g.GameDate>=$since AND g.GameDate<$before;
                """;
            pitchersCommand.Parameters.AddWithValue("$team", teamCode);
            pitchersCommand.Parameters.AddWithValue("$since", since);
            pitchersCommand.Parameters.AddWithValue("$before", beforeDate);
            await using var reader = await pitchersCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var pcode = reader.IsDBNull(0) ? null : reader.GetString(0);
                var name = reader.IsDBNull(1) ? "" : reader.GetString(1);
                if (string.IsNullOrWhiteSpace(pcode) || string.IsNullOrWhiteSpace(name)) continue;
                players.Add(new("pitcher", pcode, name, null));
            }
        }

        return players.Count == 0 ? null : players;
    }

    /// <summary>
    /// RegisterAllRosterFetcher.ParseRoster가 만든 (팀,이름,등번호) 쌍마다 pcode를 찾습니다. KBO 공식
    /// "전체 등록 현황" 페이지 자체엔 pcode가 없어서(이름+등번호만), 이 앱 DB의 Players 테이블
    /// (이름+최근 소속팀으로 색인)에서 조회합니다. 이름이 겹치는 동명이인은 먼저 LatestTeam이 요청한
    /// 팀과 일치하는 쪽으로 좁히고, 그래도(또는 팀 일치 후보가 없어도) 후보가 여러 명이면
    /// BattingGameLines/PitchingGameLines의 등번호(선수 프로필 페이지가 등번호를 보여줄 때 쓰는 것과
    /// 같은 열)로 다시 좁힙니다 — 같은 팀에 동명이인이 있을 때(예: 삼성 이승현 2명) 등번호까지 일치하는
    /// 후보가 정확히 1명이면 그 값을 씁니다. 그래도 안 좁혀지면 모호하다고 보고 null을 돌려줍니다
    /// (호출자가 pcode 없는 선수로 처리 — DailyEntryRosters.Pcode는 기본키라 저장에서 제외됩니다).
    /// </summary>
    public async Task<IReadOnlyDictionary<(string Team, string Name, string? BackNo), string?>> ResolvePcodesByTeamAndNameAsync(
        IReadOnlyCollection<(string Team, string Name, string? BackNo)> keys, CancellationToken ct = default)
    {
        var result = new Dictionary<(string Team, string Name, string? BackNo), string?>();
        if (keys.Count == 0) return result;

        var distinctNames = keys.Select(k => k.Name).Distinct().ToList();
        var byName = new Dictionary<string, List<(string Pcode, string? LatestTeam)>>();

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        const int batchSize = 400; // SQLite 파라미터 개수 한도를 넉넉히 피합니다.
        for (var offset = 0; offset < distinctNames.Count; offset += batchSize)
        {
            var batch = distinctNames.Skip(offset).Take(batchSize).ToList();
            await using var command = connection.CreateCommand();
            var placeholders = string.Join(",", batch.Select((_, i) => $"$n{i}"));
            command.CommandText = $"SELECT Pcode, Name, LatestTeam FROM Players WHERE Name IN ({placeholders});";
            for (var i = 0; i < batch.Count; i++) command.Parameters.AddWithValue($"$n{i}", batch[i]);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var pcode = reader.GetString(0);
                var name = reader.GetString(1);
                var latestTeam = reader.IsDBNull(2) ? null : reader.GetString(2);
                if (!byName.TryGetValue(name, out var list)) byName[name] = list = new();
                list.Add((pcode, latestTeam));
            }
        }

        // 이름만으로 후보가 2명 이상인 경우를 위해, 그 후보들의 최근 경기 등번호를 미리 한 번에 조회합니다.
        var ambiguousPcodes = new HashSet<string>();
        foreach (var list in byName.Values)
            if (list.Count > 1)
                foreach (var candidate in list) ambiguousPcodes.Add(candidate.Pcode);
        var backNumbers = ambiguousPcodes.Count > 0
            ? await GetMostRecentBackNumbersByPcodeAsync(ambiguousPcodes, ct).ConfigureAwait(false)
            : new Dictionary<string, string?>();

        foreach (var key in keys.Distinct())
        {
            if (!byName.TryGetValue(key.Name, out var candidates) || candidates.Count == 0) { result[key] = null; continue; }
            if (candidates.Count == 1) { result[key] = candidates[0].Pcode; continue; }

            var teamMatch = candidates.Where(c => string.Equals(c.LatestTeam, key.Team, StringComparison.OrdinalIgnoreCase)).ToList();
            var pool = teamMatch.Count > 0 ? teamMatch : candidates; // 팀 일치 후보가 없으면(최근 트레이드 등) 전체 후보에서 등번호로 좁혀봅니다.

            if (!string.IsNullOrWhiteSpace(key.BackNo))
            {
                var backNoMatch = pool
                    .Where(c => backNumbers.TryGetValue(c.Pcode, out var no) && !string.IsNullOrWhiteSpace(no)
                                && string.Equals(no, key.BackNo, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (backNoMatch.Count == 1) { result[key] = backNoMatch[0].Pcode; continue; }
            }

            if (teamMatch.Count == 1) { result[key] = teamMatch[0].Pcode; continue; }
            if (teamMatch.Count == 0 && candidates.Count == 1) { result[key] = candidates[0].Pcode; continue; }
            result[key] = null; // 팀/등번호로도 못 좁힌 동명이인
        }
        return result;
    }

    /// <summary>
    /// pcode별로 가장 최근 경기 기준 등번호를 찾습니다. BattingGameLines/PitchingGameLines의
    /// BackNumber 열(선수 프로필 페이지의 "등번호" 표시가 쓰는 것과 같은 열, PlayerWebService.cs
    /// 참고)을 Games와 조인해 가장 최근 GameDate/GameId 기준 한 건만 씁니다. 동명이인 후보를 KBO
    /// 등록 현황 표의 등번호와 대조해 구분하는 데 씁니다.
    /// </summary>
    private async Task<Dictionary<string, string?>> GetMostRecentBackNumbersByPcodeAsync(
        IReadOnlyCollection<string> pcodes, CancellationToken ct)
    {
        var result = new Dictionary<string, string?>();
        if (pcodes.Count == 0) return result;

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var list = pcodes.ToList();
        const int batchSize = 400;
        for (var offset = 0; offset < list.Count; offset += batchSize)
        {
            var batch = list.Skip(offset).Take(batchSize).ToList();
            await using var command = connection.CreateCommand();
            var placeholders = string.Join(",", batch.Select((_, i) => $"$p{i}"));
            command.CommandText = $"""
                SELECT s.Pcode, s.BackNumber
                FROM (SELECT GameId, Pcode, BackNumber FROM BattingGameLines WHERE Pcode IN ({placeholders})
                      UNION ALL
                      SELECT GameId, Pcode, BackNumber FROM PitchingGameLines WHERE Pcode IN ({placeholders})) s
                JOIN Games g ON g.GameId = s.GameId
                ORDER BY g.GameDate DESC, g.GameId DESC;
                """;
            for (var i = 0; i < batch.Count; i++) command.Parameters.AddWithValue($"$p{i}", batch[i]);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var pcode = reader.GetString(0);
                if (result.ContainsKey(pcode)) continue; // 이미 더 최근 경기의 값을 찾았습니다(ORDER BY DESC).
                result[pcode] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }
        return result;
    }
}
