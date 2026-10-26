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


var tomorrow=date.AddDays(1);
var tomorrowId="5555"+tomorrow.ToString("MMdd")+"HTSS0"+tomorrow.Year;
var day=new StarterDay(tomorrow.ToString("yyyy-MM-dd"),DateTimeOffset.UtcNow,[
    new(tomorrowId,tomorrow.ToString("yyyy-MM-dd")+"T18:30:00","KIA","삼성","BEFORE","투수A","투수B",DateTimeOffset.UtcNow,false)]);
ExternalRelayUpload.ValidateDay(day);
var calendar=ExternalRelayUpload.ApplySchedule([],day);
Check(calendar.Count==1 && (string?)calendar[0]["Id"]==tomorrowId,"full schedule adds upcoming game");
var empty=day with{Games=[]};
Check(ExternalRelayUpload.ApplySchedule(calendar,empty).Count==0,"confirmed empty schedule removes vanished fixture");
calendar=ExternalRelayUpload.ApplySchedule([],day);
calendar[0]["Status"]="RESULT";calendar[0]["AScore"]=5;
Check(ExternalRelayUpload.ApplySchedule(calendar,empty).Count==1,"empty schedule preserves played result");
var store=new BotStartersService(new SiteOptions{StateDirectory=Path.Combine(dir,"state")});
var scheduleContext=new DefaultHttpContext();
scheduleContext.Request.Headers["X-Fanzai-Collector-Key"]=key;
scheduleContext.Request.Body=new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(day));
await ExternalRelayUpload.ReceiveScheduleAsync(scheduleContext,store);
Check(store.Get(day.Date)?.Games.Single().AwayStarter=="투수A","uploaded starters available in bot store");
var duplicate=day with{Games=[day.Games[0],day.Games[0]]};
try{ExternalRelayUpload.ValidateDay(duplicate);throw new Exception("duplicate accepted");}catch(RequestError){Console.WriteLine("PASS duplicate ID rejected");}
