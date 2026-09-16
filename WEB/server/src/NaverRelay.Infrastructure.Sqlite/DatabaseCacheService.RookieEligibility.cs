using Microsoft.Data.Sqlite;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    // 신인왕 요건: 해당 시즌 개막 전까지의 정규시즌 통산 누적 기록.
    // 팀 이동과 무관하게 선수(Pcode) 단위로 전 구단 합산합니다.
    //
    // 퓨처스리그 제외뿐 아니라 정규시즌으로 한정합니다. 시범경기 기록 중 일부는
    // (파싱 시점 문제로) Games.SeasonYear가 0으로 들어간 사례가 있어, 단순히
    // "퓨처스가 아니면 전부 포함" 식으로 계산하면 그 시범경기 기록까지 "이전
    // 시즌"으로 잘못 합산되어 실제로는 요건을 충족하는 선수가 걸러지는 문제가
    // 있었습니다(예: SeasonYear=0인 시범경기 40타석이 잘못 합산됨). 화면에 표시되는
    // "연도별" 기록도 기본이 정규시즌이므로, 여기서도 정규시즌만 기준으로 삼는 것이
    // 데이터 오염에 안전하고 실제 표시값과도 일치합니다.
    public async Task<Dictionary<string, int>> GetCareerPlateAppearancesBeforeSeasonAsync(
        int seasonYear, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.Pcode, SUM(b.PA)
            FROM BatterGameStats b
            INNER JOIN Games g ON g.GameId = b.GameId
            WHERE g.SeasonYear < $year AND g.SeasonYear > 0 AND g.CompetitionType = $regularSeason
            GROUP BY b.Pcode;
            """;
        command.Parameters.AddWithValue("$year", seasonYear);
        command.Parameters.AddWithValue("$regularSeason", (int)GameCompetitionType.RegularSeason);
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
            WHERE g.SeasonYear < $year AND g.SeasonYear > 0 AND g.CompetitionType = $regularSeason
            GROUP BY p.Pcode;
            """;
        command.Parameters.AddWithValue("$year", seasonYear);
        command.Parameters.AddWithValue("$regularSeason", (int)GameCompetitionType.RegularSeason);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result[reader.GetString(0)] = ReadInt32(reader, 1);
        return result;
    }
}
