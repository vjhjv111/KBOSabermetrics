namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    // 국내/외국인/아시아쿼터 구분: OfficialPlayerProfiles.DraftText(지명순위)에
    // "아시아쿼터"/"자유선발" 문구가 포함되어 있는지로 판별합니다. 이 테이블은
    // 오프라인 수집기가 채우는 선택 테이블이라 없을 수도 있으므로 존재 여부를
    // 먼저 확인합니다. 분류(문구 매칭) 자체는 상위(Web) 계층에서 수행하고,
    // 이 계층은 Pcode -> DraftText 원본 데이터만 돌려줍니다.

    public async Task<Dictionary<string, string>> GetPlayerDraftTextsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OfficialPlayerProfiles' LIMIT 1";
        if (await existsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            return result;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Pcode, DraftText FROM OfficialPlayerProfiles
            WHERE DraftText IS NOT NULL AND DraftText <> ''
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }
}
