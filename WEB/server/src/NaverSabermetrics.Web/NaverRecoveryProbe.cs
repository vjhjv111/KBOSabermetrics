using System.Text.Json.Nodes;
namespace NaverSabermetrics.Web;
public sealed record NaverRecoveryStatus(DateTimeOffset? CheckedAt,bool Reachable,int ConsecutiveSuccesses,string Message);
public sealed class NaverRecoveryProbe(ILogger<NaverRecoveryProbe> logger):BackgroundService
{
    private NaverRecoveryStatus current=new(null,false,0,"아직 확인하지 않았습니다.");
    public NaverRecoveryStatus Status=>Volatile.Read(ref current);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if(Environment.GetEnvironmentVariable("FANZAI_EXTERNAL_COLLECTOR")!="true")return;
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(10)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; KBOSabermetrics-RenderCollector/1.0)");
        while(!ct.IsCancellationRequested) {
            var success=false;
            var message="네이버 응답 확인 실패";
            var relayChecked=false;
            try {
                var date=BotGamesService.ParseDate(null).ToString("yyyy-MM-dd");
                var data=JsonNode.Parse(await http.GetStringAsync($"https://api-gw.sports.naver.com/schedule/games?upperCategoryId=kbaseball&fromDate={date}&toDate={date}&page=1",ct));
                success=data?["success"]?.ToString()=="true"&&data?["result"]?["games"] is JsonArray;
                message=success?"네이버 일정 정상 응답 (당일 KBO 경기 없어 중계 미확인)":"네이버 일정 응답 형식 불일치";
                var game=(data?["result"]?["games"] as JsonArray)?.FirstOrDefault(g=>g?["categoryId"]?.ToString()=="kbo");
                var id=game?["gameId"]?.ToString();
                if(success && id is not null && System.Text.RegularExpressions.Regex.IsMatch(id,@"^\d{8}[A-Z]{4}\d{5}$")) {
                    var polling=JsonNode.Parse(await http.GetStringAsync($"https://api-gw.sports.naver.com/schedule/games/{id}/game-polling?isHighlight=false",ct));
                    success=polling?["result"]?["game"]?["gameId"]?.ToString()==id;
                    relayChecked=success;
                    message=success?"네이버 일정·중계 API 정상 응답":"네이버 중계 응답 확인 실패";
                }
            } catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            { message=ex is OperationCanceledException?"네이버 접속 10초 시간 초과":"네이버 접속 실패: "+ex.GetType().Name; }
            var previous=Status;
            var next=new NaverRecoveryStatus(DateTimeOffset.UtcNow,success,success&&relayChecked?Math.Min(previous.ConsecutiveSuccesses+1,100):0,message);
            Volatile.Write(ref current,next);
            if(previous.Reachable!=next.Reachable || previous.CheckedAt is null || next.ConsecutiveSuccesses==2)
                logger.LogInformation("네이버 복구 점검: {Message}, 연속 성공={Count}",message,next.ConsecutiveSuccesses);
            try{await Task.Delay(TimeSpan.FromMinutes(5),ct);}catch(OperationCanceledException){break;}
        }
    }
}
