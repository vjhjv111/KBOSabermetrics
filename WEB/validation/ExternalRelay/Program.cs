using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NaverSabermetrics.Web;
using System.Text;
using System.Text.Json.Nodes;
var key=new string('x',40);
Environment.SetEnvironmentVariable("FANZAI_COLLECTOR_KEY",key);
void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
try{ExternalRelayUpload.Authorize("wrong");throw new Exception("accepted");}catch(RequestError e){Check(e.Status==401,"wrong key rejected");}
var date=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date;
var id="5555"+date.ToString("MMdd")+"HTSS0"+date.Year;
var root=new JsonObject{["collectedAt"]=DateTimeOffset.UtcNow,["result"]=new JsonObject{["game"]=new JsonObject{
["gameId"]=id,["gameDate"]=date.ToString("yyyy-MM-dd"),["seasonYear"]=date.Year,["categoryId"]="kbo",["roundCode"]="kbo_ps_po",
["homeTeamCode"]="SS",["awayTeamCode"]="HT",["homeTeamScore"]=1,["awayTeamScore"]=5,["statusCode"]="RESULT"}}};
var dir=Path.Combine(Path.GetTempPath(),"external-relay-"+Guid.NewGuid());
var options=new RenderCollectorOptions{JsonDirectory=dir};
async Task Receive(JsonNode json){
 var context=new DefaultHttpContext();context.Request.Headers["X-Fanzai-Collector-Key"]=key;
 context.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
 await ExternalRelayUpload.ReceiveAsync(context,options);
}
await Receive(root);
var live=new HomeLiveService(options,NullLogger<HomeLiveService>.Instance);
Check(await live.DetailAsync(id,default)!=null,"uploaded final visible in detail");
Check((await live.ReadMonthAsync(date.Year,date.Month,default)).Single().AwayScore==5,"uploaded score visible in calendar");
root["collectedAt"]=DateTimeOffset.UtcNow.AddMinutes(-20);
try{await Receive(root);throw new Exception("stale accepted");}catch(RequestError){Console.WriteLine("PASS stale upload rejected");}
root["collectedAt"]=DateTimeOffset.UtcNow;root["result"]!["game"]!["statusCode"]="READY";
await Receive(root);
Check((await live.ReadMonthAsync(date.Year,date.Month,default)).Single().Final,"final never downgraded");
root["result"]!["game"]!["gameId"]="../../bad";
try{await Receive(root);throw new Exception("id accepted");}catch(RequestError){Console.WriteLine("PASS invalid ID rejected");}
Console.WriteLine("External upload checks passed.");
