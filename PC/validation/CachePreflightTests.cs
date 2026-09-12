using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

internal static class CachePreflightTests
{
    // v8.1 changes preparation, not parsing. Existing v8 source fingerprints must stay reusable.
    const string Version = "sabermetrics-v2-combined-official-v8";
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }

    sealed class ImmediateProgress(Action<SourceCacheProgress>? onReport = null) : IProgress<SourceCacheProgress>
    {
        public List<SourceCacheProgress> Events { get; } = [];
        public void Report(SourceCacheProgress value) { Events.Add(value); onReport?.Invoke(value); }
        public int ContentReads => Events.Count == 0 ? 0 : Events[^1].ContentReads;
    }

    static string NewRun(string root, string name)
    {
        var run = Path.Combine(root, name + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(run);
        return run;
    }
    static InputDocument JsonInput(string path, string id) => new() { Id = id, Kind = InputDocumentKind.JsonFile, ContainerPath = path, Length = File.Exists(path) ? new FileInfo(path).Length : 0 };
    static InputDocument ZipInput(string path, string entry, string id, long length) => new() { Id = id, Kind = InputDocumentKind.ZipEntry, ContainerPath = path, EntryName = entry, Length = length };
    static SqliteConnection Connection(string path, bool readOnly = false) => new(new SqliteConnectionStringBuilder { DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
    static string Fingerprint(string json) => Version + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    static string SourceKey(InputDocument input)
    {
        // SQL-only benchmark uses the documented canonical source identity, while functional tests seed through SaveGameAndSourceAsync.
        var canonical = string.Join("|", input.Kind, Path.GetFullPath(input.ContainerPath).ToUpperInvariant(), (input.EntryName ?? "").Replace('\\', '/').ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    static async Task Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync();
    }
    static async Task SetFingerprint(SqliteConnection connection, InputDocument input, string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE ParsedSources SET Fingerprint=$fingerprint, ParserVersion=$version WHERE SourceDisplay=$display";
        cmd.Parameters.AddWithValue("$fingerprint", value);
        cmd.Parameters.AddWithValue("$version", value.StartsWith("unverified:", StringComparison.Ordinal) ? Version : value[..value.IndexOf(':')]);
        cmd.Parameters.AddWithValue("$display", input.SourceDisplay);
        if (await cmd.ExecuteNonQueryAsync() != 1) throw new InvalidDataException("캐시 fixture가 정확히 한 행이어야 합니다.");
    }
    static async Task<string> CacheSnapshot(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT SourceKey,Fingerprint,GameId,SourceDisplay,ParserVersion,ParsedUtc FROM ParsedSources ORDER BY SourceKey";
        using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<string[]>();
        while (await reader.ReadAsync()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : reader.GetString(i)).ToArray());
        return JsonSerializer.Serialize(rows);
    }
    static void WriteZip(string path, params (string Name, string Text)[] entries)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); writer.Write(text);
        }
    }
    static void VerifyProgress(ImmediateProgress progress, IReadOnlyList<InputDocument> inputs, string label)
    {
        Check(progress.Events.Count == inputs.Count * 2, label + " 모든 파일의 시작·완료 진행 보고");
        for (var i = 0; i < inputs.Count; i++)
        {
            var start = progress.Events[2 * i]; var done = progress.Events[2 * i + 1];
            if (!start.IsChecking || done.IsChecking || start.Completed != i || done.Completed != i + 1 ||
                start.Total != inputs.Count || done.Total != inputs.Count || start.DocumentId != inputs[i].Id ||
                done.DocumentId != inputs[i].Id || start.DocumentName != inputs[i].DisplayName ||
                done.DocumentName != inputs[i].DisplayName || done.ContentReads < start.ContentReads)
                throw new InvalidDataException(label + " 진행 순서/대상/합계 불일치");
        }
        Check(true, label + " 완료 수·전체 수·문서명·읽기 수 진행 일치");
    }

    public static async Task Run(string sampleJsonPath, string outputRoot)
    {
        checks = 0;
        var run = NewRun(outputRoot, "cache-preflight"); var db = Path.Combine(run, "cache.db");
        var store = new DatabaseCacheService(db); await store.InitializeAsync();
        using var connection = Connection(db); await connection.OpenAsync();
        var a = await File.ReadAllTextAsync(sampleJsonPath) + "\n "; var b = a[..^1] + "\t";
        var byteCount = Encoding.UTF8.GetByteCount(a);
        Check(byteCount == Encoding.UTF8.GetByteCount(b) && a != b, "동일 크기·유효 JSON의 원문 변경 fixture");

        var jsonCurrent = JsonInput(Path.Combine(run, "current.json"), "current-json");
        var jsonOld = JsonInput(Path.Combine(run, "old.json"), "old-json");
        var jsonUnverified = JsonInput(Path.Combine(run, "unverified.json"), "unverified-json");
        var zipPath = Path.Combine(run, "sources.zip");
        var zipCurrent = ZipInput(zipPath, "current.json", "current-zip", byteCount);
        var zipOld = ZipInput(zipPath, "old.json", "old-zip", byteCount);
        var zipUnverified = ZipInput(zipPath, "unverified.json", "unverified-zip", byteCount);
        foreach (var input in new[] { jsonCurrent, jsonOld, jsonUnverified }) await File.WriteAllTextAsync(input.ContainerPath, a);
        WriteZip(zipPath, ("current.json", a), ("old.json", a), ("unverified.json", a));
        foreach (var input in new[] { jsonCurrent, jsonOld, jsonUnverified, zipCurrent, zipOld, zipUnverified })
            await store.SaveGameAndSourceAsync(RelayParser.ParseJson(a), input);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM ParsedSources WHERE Fingerprint LIKE $prefix"; cmd.Parameters.AddWithValue("$prefix", Version + ":%");
            Check(Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 6, "실제 저장 경로가 기존 v8 본문 해시를 유지");
        }
        foreach (var input in new[] { jsonOld, zipOld }) await SetFingerprint(connection, input, Fingerprint(a).Replace("official-v8:", "official-v7:"));
        foreach (var input in new[] { jsonUnverified, zipUnverified }) await SetFingerprint(connection, input, "unverified:fixture");
        var before = await CacheSnapshot(connection);

        var currentInputs = new[] { jsonCurrent, zipCurrent }; var progress = new ImmediateProgress();
        var unchanged = await store.GetUnchangedSourceKeysAsync(currentInputs, progress: progress);
        Check(unchanged.SetEquals(currentInputs.Select(x => x.Id)) && progress.ContentReads == 2, "v8.1에서도 동일한 v8 JSON·ZIP을 본문 확인 후 건너뜀");
        VerifyProgress(progress, currentInputs, "현재 버전");

        var jsonStamp = File.GetLastWriteTimeUtc(jsonCurrent.ContainerPath); var jsonSize = new FileInfo(jsonCurrent.ContainerPath).Length;
        await File.WriteAllTextAsync(jsonCurrent.ContainerPath, b); File.SetLastWriteTimeUtc(jsonCurrent.ContainerPath, jsonStamp);
        Check(new FileInfo(jsonCurrent.ContainerPath).Length == jsonSize && File.GetLastWriteTimeUtc(jsonCurrent.ContainerPath) == jsonStamp, "JSON 크기와 수정시각 동일 조건 유지");
        progress = new ImmediateProgress(); unchanged = await store.GetUnchangedSourceKeysAsync([jsonCurrent], progress: progress);
        Check(unchanged.Count == 0 && progress.ContentReads == 1, "현재 JSON은 동일 크기·시각의 내용 변경을 감지하여 재수집");
        await File.WriteAllTextAsync(jsonCurrent.ContainerPath, a); File.SetLastWriteTimeUtc(jsonCurrent.ContainerPath, jsonStamp);

        var zipStamp = File.GetLastWriteTimeUtc(zipPath); var zipSize = new FileInfo(zipPath).Length;
        WriteZip(zipPath, ("current.json", b), ("old.json", a), ("unverified.json", a)); File.SetLastWriteTimeUtc(zipPath, zipStamp);
        Check(new FileInfo(zipPath).Length == zipSize && File.GetLastWriteTimeUtc(zipPath) == zipStamp, "ZIP 크기와 수정시각 동일 조건 유지");
        progress = new ImmediateProgress(); unchanged = await store.GetUnchangedSourceKeysAsync([zipCurrent], progress: progress);
        Check(unchanged.Count == 0 && progress.ContentReads == 1, "현재 ZIP 엔트리도 동일 크기·시각의 내용 변경 감지");
        WriteZip(zipPath, ("current.json", a), ("old.json", b), ("unverified.json", a)); File.SetLastWriteTimeUtc(zipPath, zipStamp);
        progress = new ImmediateProgress(); unchanged = await store.GetUnchangedSourceKeysAsync([zipCurrent], progress: progress);
        Check(unchanged.Contains(zipCurrent.Id), "같은 ZIP의 다른 엔트리 변경은 동일 엔트리의 v8 캐시를 무효화하지 않음");

        InputDocument[] mustParse = [jsonOld, jsonUnverified, zipOld, zipUnverified,
            JsonInput(Path.Combine(run, "not-created.json"), "new-json"),
            ZipInput(Path.Combine(run, "not-created.zip"), "game.json", "new-zip", 1)];
        // Exclusive handles make these fixtures unreadable even though the metadata exists.
        using (var jsonOldLock = File.Open(jsonOld.ContainerPath, FileMode.Open, FileAccess.Read, FileShare.None))
        using (var jsonUnverifiedLock = File.Open(jsonUnverified.ContainerPath, FileMode.Open, FileAccess.Read, FileShare.None))
        using (var zipLock = File.Open(zipPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            progress = new ImmediateProgress(); unchanged = await store.GetUnchangedSourceKeysAsync(mustParse, progress: progress);
            Check(unchanged.Count == 0 && progress.ContentReads == 0, "신규·구버전·미검증 JSON/ZIP은 접근 불가여도 사전 본문 읽기 0회");
            Check(progress.Events.All(x => x.ContentReads == 0 && x.Unchanged == 0), "재파싱 대상 진행 로그에 본문 검사·건너뜀을 잘못 보고하지 않음");
            VerifyProgress(progress, mustParse, "재파싱 대상");
        }
        using (var currentLock = File.Open(jsonCurrent.ContainerPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            progress = new ImmediateProgress(); unchanged = await store.GetUnchangedSourceKeysAsync([jsonCurrent], progress: progress);
            Check(unchanged.Count == 0 && progress.ContentReads == 1, "현재 버전 파일이 접근 불가이면 캐시로 건너뛰지 않음");
        }

        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); progress = new ImmediateProgress(); var cancelled = false;
            try { await store.GetUnchangedSourceKeysAsync(currentInputs, cts.Token, progress); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && progress.ContentReads == 0, "시작 전 취소 토큰으로 파일 읽기 없이 종료");
        }
        using (var cts = new CancellationTokenSource())
        {
            progress = new ImmediateProgress(x => { if (!x.IsChecking && x.Completed == 1) cts.Cancel(); }); var cancelled = false;
            try { await store.GetUnchangedSourceKeysAsync(currentInputs, cts.Token, progress); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && progress.ContentReads == 1 && progress.Events.Count == 2, "첫 파일 검사 후 취소하면 다음 파일 본문을 읽지 않음");
        }
        using (var cts = new CancellationTokenSource())
        {
            progress = new ImmediateProgress(x => { if (x.IsChecking) cts.Cancel(); }); var cancelled = false;
            try { await store.GetUnchangedSourceKeysAsync(currentInputs, cts.Token, progress); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && progress.ContentReads == 0, "검사 시작 진행 알림에서 취소하면 현재 파일 본문도 읽지 않음");
        }
        progress = new ImmediateProgress(); unchanged = await store.GetUnchangedSourceKeysAsync([], progress: progress);
        Check(unchanged.Count == 0 && progress.ContentReads == 0, "빈 입력은 본문 읽기 없이 종료");
        Check(await CacheSnapshot(connection) == before, "사전 검사·취소·파일 접근 오류가 저장된 수집 이력을 변경하지 않음");
        Console.WriteLine($"RESULT cache preflight {checks} PASS / 0 FAIL; {run}");
    }

    sealed record CacheRow(string SourceKey, string Fingerprint, string? GameId, string SourceDisplay, string ParserVersion, string ParsedUtc);
    sealed record Measurement(string Mode, int Documents, long SourceBytes, long Milliseconds, int ContentReads, int Unchanged);
    static async Task<List<CacheRow>> LoadCache(string readonlyDb)
    {
        using var connection = Connection(readonlyDb, true); await connection.OpenAsync();
        using var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT SourceKey,Fingerprint,GameId,SourceDisplay,ParserVersion,ParsedUtc FROM ParsedSources ORDER BY SourceDisplay";
        using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<CacheRow>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return rows;
    }
    static async Task CopyCache(SqliteConnection connection, IEnumerable<CacheRow> rows)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var row in rows)
        {
            using var cmd = connection.CreateCommand(); cmd.Transaction = transaction;
            cmd.CommandText = "INSERT INTO ParsedSources VALUES($key,$fingerprint,$gameId,$display,$version,$utc)";
            cmd.Parameters.AddWithValue("$key", row.SourceKey); cmd.Parameters.AddWithValue("$fingerprint", row.Fingerprint);
            cmd.Parameters.AddWithValue("$gameId", (object?)row.GameId ?? DBNull.Value); cmd.Parameters.AddWithValue("$display", row.SourceDisplay);
            cmd.Parameters.AddWithValue("$version", row.ParserVersion); cmd.Parameters.AddWithValue("$utc", row.ParsedUtc); await cmd.ExecuteNonQueryAsync();
        }
        transaction.Commit();
    }

    public static async Task Benchmark(string readonlySeasonDb, string sourceFolder, string outputRoot)
    {
        checks = 0;
        var run = NewRun(outputRoot, "cache-benchmark"); var db = Path.Combine(run, "cache.db");
        var sourceDbStamp = File.GetLastWriteTimeUtc(readonlySeasonDb); var sourceDbLength = new FileInfo(readonlySeasonDb).Length;
        var originals = await LoadCache(readonlySeasonDb);
        var byPath = originals.ToDictionary(x => Path.GetFullPath(x.SourceDisplay), StringComparer.OrdinalIgnoreCase);
        var inputs = Directory.GetFiles(sourceFolder, "*.json").Select(Path.GetFullPath).Where(byPath.ContainsKey)
            .OrderBy(x => x, StringComparer.Ordinal).Select(x => JsonInput(x, Path.GetFileNameWithoutExtension(x))).ToArray();
        Check(inputs.Length == 622 && originals.Count == inputs.Length, "전체 시즌 검증 DB의 동일 원본 경로 622개를 읽기 전용으로 확인");
        Check(inputs.All(x => byPath[x.ContainerPath].SourceKey == SourceKey(x)), "벤치마크 입력의 캐시 키가 실제 수집한 경로와 일치");
        Check(originals.All(x => x.Fingerprint.StartsWith(Version + ":", StringComparison.Ordinal)), "기존 622경기 v8 캐시 버전 유지 확인");
        var manifest = inputs.ToDictionary(x => x.ContainerPath, x => (new FileInfo(x.ContainerPath).Length, File.GetLastWriteTimeUtc(x.ContainerPath)));
        var sourceBytes = manifest.Values.Sum(x => x.Length);
        var store = new DatabaseCacheService(db); await store.InitializeAsync();
        using var connection = Connection(db); await connection.OpenAsync(); await CopyCache(connection, originals);
        var measurements = new List<Measurement>();

        await Execute(connection, "UPDATE ParsedSources SET Fingerprint=REPLACE(Fingerprint,'official-v8:','official-v7:'),ParserVersion=REPLACE(ParserVersion,'official-v8','official-v7')");
        var outdatedSnapshot = await CacheSnapshot(connection);
        // Reproduce the previous sequential preflight: read+hash every source before checking whether its parser version can be reused.
        var watch = Stopwatch.StartNew(); var legacyReads = 0; var legacyUnchanged = 0;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Fingerprint FROM ParsedSources WHERE SourceKey=$key LIMIT 1"; var parameter = cmd.Parameters.Add("$key", SqliteType.Text);
            foreach (var input in inputs)
            {
                var hash = Fingerprint(await input.ReadJsonAsync(CancellationToken.None)); legacyReads++; parameter.Value = SourceKey(input);
                if ((string?)await cmd.ExecuteScalarAsync() == hash) legacyUnchanged++;
            }
        }
        watch.Stop(); measurements.Add(new("before_outdated_v7", inputs.Length, sourceBytes, watch.ElapsedMilliseconds, legacyReads, legacyUnchanged));
        Console.WriteLine(JsonSerializer.Serialize(measurements[^1]));
        var progress = new ImmediateProgress(); watch.Restart(); var unchanged = await store.GetUnchangedSourceKeysAsync(inputs, progress: progress); watch.Stop();
        measurements.Add(new("after_outdated_v7", inputs.Length, sourceBytes, watch.ElapsedMilliseconds, progress.ContentReads, unchanged.Count));
        Console.WriteLine(JsonSerializer.Serialize(measurements[^1]));
        Check(legacyReads == 622 && legacyUnchanged == 0 && progress.ContentReads == 0 && unchanged.Count == 0, "구버전 622파일 사전 본문 읽기를 622회에서 0회로 줄이고 모두 재파싱 유지");
        Check(await CacheSnapshot(connection) == outdatedSnapshot, "구버전 사전 검사로 캐시 행을 바꾸지 않음");
        await Execute(connection, "DELETE FROM ParsedSources"); await CopyCache(connection, originals);
        var currentSnapshot = await CacheSnapshot(connection);
        progress = new ImmediateProgress(); watch.Restart(); unchanged = await store.GetUnchangedSourceKeysAsync(inputs, progress: progress); watch.Stop();
        measurements.Add(new("after_current_v8", inputs.Length, sourceBytes, watch.ElapsedMilliseconds, progress.ContentReads, unchanged.Count));
        Console.WriteLine(JsonSerializer.Serialize(measurements[^1]));
        Check(progress.ContentReads == 622 && unchanged.Count == 622, "같은 622경기 v8 캐시는 전체 해시 확인 후 모두 건너뜀");
        Check(await CacheSnapshot(connection) == currentSnapshot, "현재 버전 사전 검사도 캐시 행을 바꾸지 않음");
        Check(await LoadCache(readonlySeasonDb).ContinueWith(t => JsonSerializer.Serialize(t.Result)) == JsonSerializer.Serialize(originals) &&
            File.GetLastWriteTimeUtc(readonlySeasonDb) == sourceDbStamp && new FileInfo(readonlySeasonDb).Length == sourceDbLength,
            "전체 시즌 검증 DB의 크기·수정시각·수집 이력 원본 유지");
        Check(inputs.All(x => (new FileInfo(x.ContainerPath).Length, File.GetLastWriteTimeUtc(x.ContainerPath)) == manifest[x.ContainerPath]), "원본 622파일 크기·수정시각 유지");
        await File.WriteAllTextAsync(Path.Combine(run, "report.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"RESULT cache benchmark {checks} PASS / 0 FAIL; {run}");
    }
}
