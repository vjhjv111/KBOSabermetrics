using NaverRelay.Application.Importing;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    const string PlayLogSchema = "CREATE TABLE IF NOT EXISTS OfficialPlayLogs(GameId TEXT PRIMARY KEY,Json TEXT NOT NULL,ImportedUtc TEXT NOT NULL);";

    public async Task<NormalizedGame> ImportKboPlayLogAsync(string json, CancellationToken ct = default)
    {
        var official = KboPlayLog.Parse(json);
        var id = official.GameId + official.GameId[..4];
        string? source;
        await using (var c = await OpenAsync(ct))
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT SourceDisplay FROM ParsedSources WHERE GameId=$id ORDER BY ParsedUtc DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$id", id);
            source = await cmd.ExecuteScalarAsync(ct) as string;
        }
        if (string.IsNullOrEmpty(source)) throw new InvalidDataException("먼저 해당 경기의 네이버 JSON을 DB에 수집해주세요.");
        var parts = source.Split("  >  ", 2, StringSplitOptions.None);
        if (!File.Exists(parts[0])) throw new FileNotFoundException("기존 네이버 원본 파일이 필요합니다. 네이버 JSON을 다시 수집한 후 공식 JSON을 불러오세요.", parts[0]);
        var input = new InputDocument { Id = id, Kind = parts.Length == 1 ? InputDocumentKind.JsonFile : InputDocumentKind.ZipEntry,
            ContainerPath = parts[0], EntryName = parts.Length == 2 ? parts[1] : null, Length = new FileInfo(parts[0]).Length };
        var game = RelayParser.ParseJson(await input.ReadJsonAsync(ct));
        await SaveGameWithPlayLogAsync(game, input, ct, official);
        return game;
    }

    async Task<KboPlayLog.Document?> LoadPlayLogAsync(string id, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='OfficialPlayLogs'";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 0) return null;
        cmd.CommandText = "SELECT Json FROM OfficialPlayLogs WHERE GameId=$id";
        cmd.Parameters.AddWithValue("$id", id);
        var json = await cmd.ExecuteScalarAsync(ct) as string;
        return json == null ? null : KboPlayLog.Parse(json);
    }
}
