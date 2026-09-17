using Microsoft.Data.Sqlite;

namespace NaverRelayUI.Parsing
{
    public static class ParsingService
    {
        public static async Task ParseFolderAsync(
            string inputDir,
            string sqlitePath,
            Action<string>? log = null,
            IProgress<(int done, int total)>? progress = null,
            CancellationToken ct = default)
        {
            var files = Directory.Exists(inputDir)
                ? Directory.GetFiles(inputDir, "*.json", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            if (files.Length == 0)
            {
                log?.Invoke($"'{inputDir}' 폴더에서 json 파일을 찾지 못했습니다.");
                return;
            }

            var parent = Path.GetDirectoryName(Path.GetFullPath(sqlitePath));
            if (parent != null) Directory.CreateDirectory(parent);
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sqlitePath }.ToString());
            await conn.OpenAsync(ct);
            SqliteWriter.EnsureSchema(conn);

            int done = 0;
            int ok = 0, skipped = 0, failed = 0;

            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                var gameId = Path.GetFileNameWithoutExtension(path);

                try
                {
                    var json = await File.ReadAllTextAsync(path, ct);
                    var response = RelayDeserializer.Deserialize(json);

                    if (response?.Result?.Game == null || response.Result.TextRelayData == null)
                    {
                        log?.Invoke($"건너뜀 {gameId}: game/textRelayData 없음");
                        skipped++;
                    }
                    else
                    {
                        var (pas, pitches, advances) = RelayParser.Flatten(response);
                        SqliteWriter.WriteGame(conn, response.Result.Game, json, pas, pitches, advances);
                        log?.Invoke($"파싱 {gameId}: 타석 {pas.Count}건, 투구 {pitches.Count}건, 주루 {advances.Count}건");
                        ok++;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    log?.Invoke($"실패 {gameId}: {ex.Message}");
                    failed++;
                }

                done++;
                progress?.Report((done, files.Length));
            }

            log?.Invoke($"완료. 성공 {ok} / 건너뜀 {skipped} / 실패 {failed} (전체 {files.Length})");
        }
    }
}
