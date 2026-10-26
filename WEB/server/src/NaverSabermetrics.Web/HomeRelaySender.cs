using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NaverRelayUI.Collection;
namespace NaverSabermetrics.Web;
public static class HomeRelaySender
{
    public static async Task RunAsync(string directory,bool once)
    {
        var key=Environment.GetEnvironmentVariable("FANZAI_COLLECTOR_KEY");
        if(string.IsNullOrWhiteSpace(key)||key.Length<32)throw new ArgumentException("FANZAI_COLLECTOR_KEY is required (32+ characters).");
        Directory.CreateDirectory(directory);
        using var source=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
        source.DefaultRequestHeaders.UserAgent.ParseAdd("FANZAI-HomeCollector/1.0");
        using var destination=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(45)};
        destination.DefaultRequestHeaders.Add("X-Fanzai-Collector-Key",key);
        var starterStore=new BotStartersService(new SiteOptions{StateDirectory=directory});
        var ackPath=Path.Combine(directory,"uploaded-status.json");
        Dictionary<string,string> acknowledged;
        try { acknowledged=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(await File.ReadAllTextAsync(ackPath))??new(); }
        catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException){acknowledged=new();}
        var lastProbe=DateTimeOffset.MinValue;
        string? lastProbeMessage=null;
        using var stop=new CancellationTokenSource();
        Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
        do {
            var cycle=DateTimeOffset.UtcNow;
            try {
                if(cycle-lastProbe>=TimeSpan.FromMinutes(5)) {
                    lastProbe=cycle;
                    try {
                        var health=await destination.GetFromJsonAsync<NaverRecoveryStatus>("https://fanzai.co.kr/api/collector/status",stop.Token);
                        var message=health?.ConsecutiveSuccesses>=2
                            ? "=== Render 네이버 일정·중계 연결 복구: 2회 이상 연속 성공! 집 PC 수집은 계속 유지합니다. ==="
                            : health?.Reachable==true?"[Render 연결] "+health.Message+" · 다음 점검 대기":"[Render 연결] "+(health?.Message??"점검 결과 없음");
                        if(message!=lastProbeMessage){Console.WriteLine(message);lastProbeMessage=message;}
                    } catch(Exception ex) when(!stop.IsCancellationRequested){Console.WriteLine("[Render 복구 점검 조회 실패] "+ex.GetType().Name);}
                }
                var today=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date;
                var schedulePath=Path.Combine(directory,"live-schedule.json");
                List<NaverRelayUI.Models.ScheduleGame> known;
                try {known=System.Text.Json.JsonSerializer.Deserialize<List<NaverRelayUI.Models.ScheduleGame>>(await File.ReadAllTextAsync(schedulePath,stop.Token))??new();}
                catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException){known=new();}
                List<NaverRelayUI.Models.ScheduleGame> games;
                var fresh=true;
                try {
                    games=await GameIdCollector.CollectAllKboGamesAsync(source,today.ToString("yyyy-MM-dd"),today.AddDays(1).ToString("yyyy-MM-dd"),ct:stop.Token);
                    var unfinished=known.Where(g=>g.GameDate==today.AddDays(-1).ToString("yyyy-MM-dd") &&
                        (RenderCollectionPolicy.IsLiveStatus(g.StatusCode) || RenderCollectionPolicy.IsFinalStatus(g.StatusCode)) && !RenderCollectionPolicy.IsFinalStatus(acknowledged.GetValueOrDefault(g.GameId??"")));
                    games.AddRange(unfinished);
                    await UnifiedDocumentStore.SaveAtomicAsync(schedulePath,System.Text.Json.JsonSerializer.Serialize(games),stop.Token);
                } catch(Exception ex) when(!stop.IsCancellationRequested) {
                    Console.Error.WriteLine($"Schedule unavailable; keep previous schedule: {ex.Message}");
                    games=known;fresh=false;
                }
                for(var offset=0;fresh&&offset<=1;offset++) {
                    var date=today.AddDays(offset).ToString("yyyy-MM-dd");
                    var schedule=games.Where(g=>g.GameDate==date).ToArray();
                    var oldDay=starterStore.Get(date);
                    var changed=oldDay is null || !schedule.Select(g=>$"{g.GameId}:{(g.Cancel?"CANCEL":g.StatusCode)}:{g.GameDateTime}").Order().SequenceEqual(
                        oldDay.Games.Select(g=>$"{g.Id}:{g.Status}:{g.Time}").Order());
                    try {
                    if(changed || cycle-(oldDay?.UpdatedAt??DateTimeOffset.MinValue)>=TimeSpan.FromMinutes(5))
                        await BotStartersWorker.RefreshDayAsync(starterStore,NullLogger.Instance,source,date,schedule,stop.Token);
                    // Full day includes an empty array on a confirmed no-game day.
                    var day=starterStore.Get(date)!;
                    using var scheduleResponse=await destination.PostAsJsonAsync("https://fanzai.co.kr"+ExternalRelayUpload.ScheduleRoute,day with{UpdatedAt=DateTimeOffset.UtcNow},stop.Token);
                    if(!scheduleResponse.IsSuccessStatusCode)throw new HttpRequestException($"Schedule upload HTTP {(int)scheduleResponse.StatusCode}");
                    Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} schedule {date}: {day.Games.Length} games");
                    } catch(Exception ex) when(!stop.IsCancellationRequested) {
                        Console.Error.WriteLine($"Schedule upload {date}: {ex.Message}");
                        if(once)Environment.ExitCode=1;
                    }
                }
                foreach(var game in games.Where(g=>g.GameId is {Length:17} && (g.GameDate==today.ToString("yyyy-MM-dd") || g.GameDate==today.AddDays(-1).ToString("yyyy-MM-dd") && (RenderCollectionPolicy.IsLiveStatus(g.StatusCode)||RenderCollectionPolicy.IsFinalStatus(g.StatusCode))))) {
                    var scheduledStatus=game.Cancel?"CANCEL":game.StatusCode??"BEFORE";
                    if(scheduledStatus is "BEFORE" or "READY")continue;
                    if((RenderCollectionPolicy.IsFinalStatus(scheduledStatus)||scheduledStatus=="CANCEL") &&
                        acknowledged.GetValueOrDefault(game.GameId!)==scheduledStatus)continue;
                    try {
                        var id=game.GameId!;
                        var root=JsonNode.Parse(await source.GetStringAsync($"https://api-gw.sports.naver.com/schedule/games/{id}/game-polling?isHighlight=false",stop.Token))!;
                        var path=Path.Combine(directory,id+".json");
                        await PostseasonRelayHistory.PopulateAsync(source,root,id,path,NullLogger.Instance,stop.Token);
                        root["collectedAt"]=DateTimeOffset.UtcNow;
                        await UnifiedDocumentStore.SaveAtomicAsync(path,root.ToJsonString(),stop.Token);
                        using var response=await destination.PostAsJsonAsync("https://fanzai.co.kr"+ExternalRelayUpload.Route,root,stop.Token);
                        if(!response.IsSuccessStatusCode)throw new HttpRequestException($"Upload HTTP {(int)response.StatusCode}");
                        var returned=await response.Content.ReadAsStringAsync(stop.Token);
                        var storedStatus=root["result"]?["game"]?["statusCode"]?.ToString()??"";
                        if(root["result"]?["game"]?["cancel"]?.ToString()=="true")storedStatus="CANCEL";
                        acknowledged[id]=storedStatus;
                        await UnifiedDocumentStore.SaveAtomicAsync(ackPath,System.Text.Json.JsonSerializer.Serialize(acknowledged),stop.Token);
                        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} uploaded {id}: {returned}");
                    } catch(Exception ex) when(!stop.IsCancellationRequested) {
                        Console.Error.WriteLine($"{game.GameId}: {ex.GetType().Name}: {ex.Message}");
                        if(once)Environment.ExitCode=1;
                    }
                }
            } catch(Exception ex) when(!stop.IsCancellationRequested) {
                Console.Error.WriteLine($"Collection failed: {ex.GetType().Name}: {ex.Message}");
                if(once)Environment.ExitCode=1;
            } catch(OperationCanceledException) when(stop.IsCancellationRequested) {break;}
            if(once)break;
            var delay=TimeSpan.FromMinutes(1)-(DateTimeOffset.UtcNow-cycle);
            try {await Task.Delay(delay>TimeSpan.Zero?delay:TimeSpan.FromSeconds(5),stop.Token);}
            catch(OperationCanceledException){break;}
        } while(!stop.IsCancellationRequested);
    }
}
