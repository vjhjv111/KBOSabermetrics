using Microsoft.Data.Sqlite;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    // 신인왕 요건: 해당 시즌 개막 전까지의 1군(퓨처스리그 제외) 통산 누적 기록.
    // 팀 이동과 무관하게 선수(Pcode) 단위로 전 구단 합산합니다.

    public async Task<Dictionary<string, int>> GetCareerPlateAppearancesBeforeSeasonAsync(
        int seasonYear, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.Pcode, SUM(b.PA)
            FROM BatterGameStats b
            INNER JOIN Games g ON g.GameId = b.GameId
            WHERE g.SeasonYear < $year AND g.CompetitionType <> $futures
            GROUP BY b.Pcode;
            """;
        command.Parameters.AddWithValue("$year", seasonYear);
        command.Parameters.AddWithValue("$futures", (int)GameCompetitionType.Futures);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result[reader.GetString(0)] = ReadInt32(reader, 1);
        return result;
    }

    public async Task<Dictionary<string, int>> GetCareerPitchingOutsBeforeSeasonAsync(
        int seasonYear, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.Pcode, SUM(p.InningsOuts)
            FROM PitcherGameStats p
            INNER JOIN Games g ON g.GameId = p.GameId
            WHERE g.SeasonYear < $year AND g.CompetitionType <> $futures
            GROUP BY p.Pcode;
            """;
        command.Parameters.AddWithValue("$year", seasonYear);
        command.Parameters.AddWithValue("$futures", (int)GameCompetitionType.Futures);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result[reader.GetString(0)] = ReadInt32(reader, 1);
        return result;
    }
}
