using System.Net;
using KboRelayDownloader;
using NaverSabermetrics.Web;

static class StarterChecks
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var parsed = ProbableStarterFetcher.Parse("""{"result":{"game":{"homeStarterName":" 곽빈 ","awayStarterName":"류현진","homePitcherName":"구원투수"}}}""");
        check(parsed.HomeStarterName == "곽빈" && parsed.AwayStarterName == "류현진", "선발 필드만 사용·공백 제거");
        check(ProbableStarterFetcher.Parse("""{"result":{"game":{}}}""").HomeStarterName is null, "정상 응답 미발표");
        var failed = false;
        try { ProbableStarterFetcher.Parse("{}"); } catch(System.Text.Json.JsonException) { failed = true; }
        check(failed, "불완전 응답을 미발표로 오인하지 않음");
        var calls = new List<string>();
        using var http = new HttpClient(new Stub(req => {
            calls.Add(req.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) {Content = new StringContent("""{"result":{"game":{"homeStarterName":"곽빈"}}}""")};
        }));
        await ProbableStarterFetcher.TryFetchAsync(http,"20260929NCOB02026");
        check(calls.Single().EndsWith("20260929NCOB02026/game-polling"), "긴 경기 ID에 연도 중복 부착 방지");
        using var bad = new HttpClient(new Stub(_ => new(HttpStatusCode.InternalServerError)));
        failed = false;
        try { await ProbableStarterFetcher.TryFetchAsync(bad,"20260929NCOB0"); } catch(HttpRequestException) { failed = true; }
        check(failed, "네트워크 오류는 수집 실패로 전달");
        var now = DateTimeOffset.UtcNow;
        var date = BotGamesService.ParseDate(null).ToString("yyyy-MM-dd");
        var row = new StarterGame("20260929NCOB02026",date+"T18:30:00","NC","두산","BEFORE",null,"곽빈",now,false);
        var day = new StarterDay(date,now,[row]);
        check(BotStartersService.Format(date,day,now).Text == "NC 미발표 vs 곽빈 두산", "한 줄 선발·한쪽만 미발표");
        check(!BotStartersService.Format(date,null,now).Available, "미수집은 경기 없음과 구분");
        check(BotStartersService.Format(date,new(date,now,[]),now).Text.Contains("예정된 경기가 없습니다"), "확인된 빈 일정");
        check(BotStartersService.Format(date,day,now.AddMinutes(16)).Stale, "오래된 캐시 안내");
        check(BotStartersService.Format(date,day with {Games=[row with {FetchFailed=true}]},now).Text.Contains("조회 실패"), "미발표와 조회 실패 구분");
        check(!BotStartersService.Format(date,day with {Games=[row with {Status="CANCEL"}]},now).Text.Contains("곽빈"), "취소 경기 선발 숨김");
        var directory = Path.Combine(Path.GetTempPath(),"starter-validation-"+Guid.NewGuid());
        try {
            var options = new SiteOptions {StateDirectory=directory};
            var store = new BotStartersService(options);
            await store.SaveAsync(day,CancellationToken.None);
            var tomorrow = BotGamesService.ParseDate(null).AddDays(1).ToString("yyyy-MM-dd");
            await store.SaveAsync(new(tomorrow,now,[]),CancellationToken.None);
            var reloaded = new BotStartersService(options);
            check(reloaded.Query(0).Games.Length==1 && reloaded.Query(1).Date==tomorrow && reloaded.Query(1).Available,"오늘·내일 캐시 보존 및 재시작 복원");
            failed=false;
            try {reloaded.Query(2);}catch(RequestError){failed=true;}
            check(failed,"잘못된 날짜 범위 거절");
        } finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => Task.FromResult(send(request));
    }
}
