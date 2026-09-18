using System.Net;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

internal static class OfficialBattingTests
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
    public static async Task Run(string rawFolder, string fixtureFolder, string dbPath)
    {
        var store = new DatabaseCacheService(dbPath); await store.InitializeAsync();
        var cases = new[] { ("20260514SKKT02026", "50854", 2), ("20260705OBWO02026", "77532", 1) };
        foreach (var (id, code, rbi) in cases)
        {
            var path = Path.Combine(rawFolder, id + ".json");
            var source = new InputDocument { Id = id, Kind = InputDocumentKind.JsonFile, ContainerPath = path, Length = new FileInfo(path).Length };
            await store.SaveGameAndSourceAsync(RelayParser.ParseFile(path), source);
            using var http = new HttpClient(new FixtureHandler(fixtureFolder));
            var sync = await store.SyncOfficialRbiAsync(2026, transport: http, playerCodes: new[] { code });
            Check(sync.Players == 1 && sync.Pending == 0, "공식 HTTP 파싱/대조 " + code);
            await using var c = new SqliteConnection("Data Source=" + dbPath); await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT RBI FROM BatterGameStats WHERE GameId=$id AND Pcode=$code";
            cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$code", code);
            Check(Convert.ToInt32(await cmd.ExecuteScalarAsync()) == rbi, "공식 타점 DB 반영 " + code);
            var parsed = RelayParser.ParseFile(path); await store.SaveGameAndSourceAsync(parsed, source);
            Check(parsed.BattingLines.Single(b => b.Pcode == code).RunsBattedIn == rbi, "원본 재수집 시 타점 보정 유지 " + code);
            var repeat = await store.SyncOfficialRbiAsync(2026, transport: http, playerCodes: new[] { code });
            Check(repeat.Updated == 0, "타점 재검증 멱등성 " + code);
            var rows = DatabaseCacheService.ParseOfficialDailyBatting(File.ReadAllText(Path.Combine(fixtureFolder,"daily-" + code + ".html")),2026);
            var corrupt = rows.Select(x => x.Date == parsed.GameDate ? x with { H=x.H+1,RBI=99 } : x).ToArray();
            var rejected = await store.StoreAndReconcileOfficialRbiAsync(2026,code,corrupt);
            Check(rejected.Pending == 1 && Convert.ToInt32(await cmd.ExecuteScalarAsync()) == rbi, "안타 불일치 시 타점 덮어쓰기 차단 " + code);
            await store.StoreAndReconcileOfficialRbiAsync(2026,code,rows);
            var duplicate = rows.Concat(rows.Where(x => x.Date == parsed.GameDate)).ToArray();
            rejected = await store.StoreAndReconcileOfficialRbiAsync(2026,code,duplicate);
            Check(rejected.Pending == 1 && rejected.Updated == 0, "같은 날짜 복수 공식 행 보류 " + code);
            await store.StoreAndReconcileOfficialRbiAsync(2026,code,rows);
        }
        bool invalid = false;
        try { DatabaseCacheService.ParseOfficialDailyBatting("<html>접속 오류</html>",2026); } catch (InvalidDataException) { invalid = true; }
        Check(invalid,"공식 사이트 오류 응답 거부");
        using var failureHttp = new HttpClient(new ErrorHandler());
        var failed = await store.SyncOfficialRbiAsync(2026,transport: failureHttp,playerCodes: cases.Select(x=>x.Item2).ToArray());
        Check(failed.Players == 0 && failed.Pending == 2,"공식 조회 실패 시 성공으로 표시하지 않음");
        Check(failed.PendingPlayerCodes.SequenceEqual(cases.Select(x=>x.Item2).OrderBy(x=>x,StringComparer.Ordinal)),"공식 조회 실패 선수 재시도 목록 보존");
    }
    sealed class FixtureHandler(string folder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var code = request.RequestUri!.Query.Split('=')[1];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(Path.Combine(folder,"daily-"+code+".html"))) });
        }
    }
    sealed class ErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
