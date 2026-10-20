using System.Text.Json;
using System.Text.Json.Nodes;

namespace NaverSabermetrics.Web;

// Polling only returns the current half-inning. Fetch inning history independently,
// retaining successful innings across collection cycles and process restarts.
public static class PostseasonRelayHistory
{
    public static async Task PopulateAsync(HttpClient http,JsonNode root,string id,string path,
        ILogger logger,CancellationToken ct)
    {
        var result=root["result"];
        if(result?["textRelayData"] is not JsonObject relay)return;
        var inning=HomeLiveService.I(relay,"inn");
        if(inning is null or <1 or >30)return;
        JsonNode? previous=null;
        if(File.Exists(path))try { previous=JsonNode.Parse(await File.ReadAllTextAsync(path,ct)); }
        catch(Exception ex) when(ex is IOException or JsonException) {logger.LogWarning(ex,"이전 포스트시즌 중계 읽기 실패: {Id}",id);}
        if(previous?["result"]?["game"]?["gameId"]?.ToString()!=id)previous=null;
        var rows=new SortedDictionary<int,JsonNode>();
        void Merge(JsonNode? items) {
            if(items is not JsonArray array)return;
            foreach(var p in array)if(p is not null&&HomeLiveService.I(p,"no") is {} no&&HomeLiveService.I(p,"inn") is >=1 and <=30)
                rows[no]=p.DeepClone();
        }
        Merge(previous?["result"]?["textRelayData"]?["textRelays"]);
        Merge(relay["textRelays"]);
        var completed=new HashSet<int>();
        if(previous?["relayHistory"]?["completedInnings"] is JsonArray saved)
            foreach(var n in saved)if(int.TryParse(n?.ToString(),out var i)&&i>=1&&i<inning)completed.Add(i);
        for(var i=1;i<=inning;i++)
        {
            if(completed.Contains(i))continue;
            try {
                using var response=await http.GetAsync($"https://api-gw.sports.naver.com/schedule/games/{id}/relay?inning={i}",ct);
                response.EnsureSuccessStatusCode();
                var fetched=JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["result"]?["textRelayData"];
                if(fetched?["gameId"]?.ToString()!=id||fetched["textRelays"] is not JsonArray plays||
                    plays.Count==0||plays.Any(p=>HomeLiveService.I(p,"inn")!=i))
                    throw new InvalidDataException("이닝 중계 경기 ID/이닝 불일치 또는 빈 응답");
                Merge(plays);
                if(i<inning)completed.Add(i);
            }
            catch(Exception ex) when(ex is HttpRequestException or IOException or JsonException or InvalidOperationException ||
                ex is OperationCanceledException && !ct.IsCancellationRequested)
            {logger.LogWarning(ex,"포스트시즌 {Id} {Inning}회 중계 재시도 대기",id,i);}
        }
        relay["textRelays"]=new JsonArray(rows.Values.Select(p=>(JsonNode?)p).ToArray());
        root["relayHistory"]=new JsonObject {
            ["completedInnings"]=JsonSerializer.SerializeToNode(completed.OrderBy(x=>x)),
            ["throughInning"]=inning.Value
        };
    }
}
