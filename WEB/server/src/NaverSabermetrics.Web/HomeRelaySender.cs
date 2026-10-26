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
        using var stop=new CancellationTokenSource();
        Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
        do {
            var cycle=DateTimeOffset.UtcNow;
            try {
                var today=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date;
                var games=await GameIdCollector.CollectAllKboGamesAsync(source,today.AddDays(-1).ToString("yyyy-MM-dd"),today.ToString("yyyy-MM-dd"),ct:stop.Token);
                foreach(var game in games.Where(g=>g.GameId is {Length:17} && g.GameDate is {Length:10} && g.GameId[..4]!=g.GameDate[..4])) {
                    try {
                        var id=game.GameId!;
                        var root=JsonNode.Parse(await source.GetStringAsync($"https://api-gw.sports.naver.com/schedule/games/{id}/game-polling?isHighlight=false",stop.Token))!;
                        var path=Path.Combine(directory,id+".json");
                        await PostseasonRelayHistory.PopulateAsync(source,root,id,path,NullLogger.Instance,stop.Token);
                        root["collectedAt"]=DateTimeOffset.UtcNow;
                        await UnifiedDocumentStore.SaveAtomicAsync(path,root.ToJsonString(),stop.Token);
                        using var response=await destination.PostAsJsonAsync("https://fanzai.co.kr"+ExternalRelayUpload.Route,root,stop.Token);
                        if(!response.IsSuccessStatusCode)throw new HttpRequestException($"Upload HTTP {(int)response.StatusCode}");
                        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} uploaded {id}: {await response.Content.ReadAsStringAsync(stop.Token)}");
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
