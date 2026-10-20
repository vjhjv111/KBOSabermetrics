using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NaverSabermetrics.Web;

const string id="33331020HTLG02026";
static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
static JsonNode Root(int inning)=>JsonNode.Parse($$$$"""
{"result":{"game":{"gameId":"33331020HTLG02026"},"textRelayData":{"inn":{{{{inning}}}},"textRelays":[{"no":{{{{inning*10}}}},"inn":{{{{inning}}}},"title":"polling"}]}}}
""")!;
var path=Path.Combine(Path.GetTempPath(),"fanzai-history-"+Guid.NewGuid().ToString("N")+".json");
try {
    var handler=new RelayHandler();using var http=new HttpClient(handler);
    var root=Root(2);
    await PostseasonRelayHistory.PopulateAsync(http,root,id,path,NullLogger.Instance,default);
    Check(handler.Calls.SequenceEqual(new[]{1,2}),"initial missing innings fetched");
    Check(root["result"]!["textRelayData"]!["textRelays"]!.AsArray().Count==2,"merge/deduplicate by relay number");
    await File.WriteAllTextAsync(path,root.ToJsonString());
    handler.Calls.Clear();root=Root(2);
    await PostseasonRelayHistory.PopulateAsync(http,root,id,path,NullLogger.Instance,default);
    Check(handler.Calls.SequenceEqual(new[]{2}),"completed inning reused after restart");
    handler.Calls.Clear();handler.Fail=2;root=Root(3);
    await PostseasonRelayHistory.PopulateAsync(http,root,id,path,NullLogger.Instance,default);
    Check(root["result"]!["textRelayData"]!["textRelays"]!.AsArray().Count==3,"network failure retains prior inning");
    Check(root["relayHistory"]!["completedInnings"]!.AsArray().Count==1,"failed inning remains retryable");
    if(args.Contains("--live")){
        using var liveHttp=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
        var actual=JsonNode.Parse(await liveHttp.GetStringAsync($"https://api-gw.sports.naver.com/schedule/games/{id}/game-polling?isHighlight=false"))!;
        // No test fixture state may contaminate actual data.
        File.Delete(path);
        await PostseasonRelayHistory.PopulateAsync(liveHttp,actual,id,path,NullLogger.Instance,default);
        var snapshot=HomeLiveService.Parse(actual.ToJsonString(),id,includeRelay:true)!;
        var detail=System.Text.Json.JsonSerializer.SerializeToNode(HomeLiveService.Detail(snapshot))!;
        var probability=detail["probability"]!.AsArray();
        Check(probability.Any(p=>p!["Inning"]!.GetValue<int>()==1),"actual first inning recovered");
        Check(probability.Any(p=>p!["Inning"]!.GetValue<int>()==2&&p["Team"]!.ToString()=="HT"),"actual second inning top assigned to KIA");
        foreach(var p in probability.Where(p=>p!["Inning"]!.GetValue<int>()==2&&p["Team"]!.ToString()=="HT"))
            Console.WriteLine($"{p!["Title"]}: KIA {p["Away"]}% / LG {p["Home"]}%");
        Console.WriteLine($"Actual probability points: {probability.Count}");
    }
}finally{if(File.Exists(path))File.Delete(path);}

sealed class RelayHandler:HttpMessageHandler
{
    public List<int> Calls {get;}=[];
    public int Fail {get;set;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
        var inning=int.Parse(request.RequestUri!.Query.Split('=').Last());Calls.Add(inning);
        return Task.FromResult(new HttpResponseMessage(inning==Fail?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){
            Content=new StringContent($$$$"""
{"result":{"textRelayData":{"gameId":"33331020HTLG02026","textRelays":[{"no":{{{{inning*10}}}},"inn":{{{{inning}}}},"title":"fetched"}]}}}
""")});
    }
}
