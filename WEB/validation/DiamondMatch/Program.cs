using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using NaverSabermetrics.Web;

if(args.Length<2)throw new ArgumentException("Usage: <game-lib-directory> <scratch-directory>");
var output=Path.Combine(Path.GetFullPath(args[1]),"match-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
var source=Path.Combine(output,"source.db");RosterFixture.Create(source);
using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=source,Pooling=false}.ToString()))
{
 c.Open();using var tx=c.BeginTransaction();using var clear=c.CreateCommand();clear.Transaction=tx;
 clear.CommandText="DELETE FROM Games; DELETE FROM Players; DELETE FROM GamePlayers; DELETE FROM BatterGameStats; DELETE FROM PitcherGameStats; DELETE FROM Pitches; DELETE FROM BattingGameLines; DELETE FROM PitchingGameLines;";clear.ExecuteNonQuery();
 void Insert(string table,params object[] values){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=$"INSERT INTO {table} VALUES({string.Join(',',values.Select((_,i)=>"$p"+i))})";for(var i=0;i<values.Length;i++)cmd.Parameters.AddWithValue("$p"+i,values[i]);cmd.ExecuteNonQuery();}
 string[] teams=["HH","HT","KT","LG","LT","NC","OB","SK","SS","WO"];
 for(var t=0;t<10;t++){var team=teams[t];var gid="g"+t;Insert("Games",gid,2025,"2025-09-01","kbo_r","RESULT",team,teams[(t+1)%10]);
  for(var n=0;n<12;n++){var id=team+"b"+n;Insert("Players",id,id,n%2==0?"우투우타":"우투좌타","외야수");Insert("BatterGameStats",gid,id,team,id,400,350,90,10,35,5,10,85,145,1600,800,800,520,180,440,100);}
  for(var n=0;n<8;n++){var id=team+"p"+n;Insert("Players",id,id,n%2==0?"우투우타":"좌투좌타","투수");Insert("PitcherGameStats",gid,id,team,id,500,1,45,120,45,120,380-n*20,110,40,2000,1000,1000,650,230,500,140);Insert("Pitches",gid,id,"직구",145d+n*.2);Insert("Pitches",gid,id,"슬라이더",133d);}
 }tx.Commit();
}
var sourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));long now=1700000000000;var random=new Random(138);var rewards=0;var checks=0;
void Check(bool ok,string reason){checks++;if(!ok)throw new InvalidDataException(reason);}
var career=new DiamondCareerService(Path.Combine(output,"state"));
var seasons=new DiamondSeasonService(Path.Combine(output,"state"),Path.GetFullPath(args[0]),new(source),()=>now,random.NextDouble,rosterOverride:career.GetRosterOverride,gameCompleted:(_,_)=>rewards++);
var service=new DiamondSeasonMatchService(seasons);
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,0));builder.Services.AddSingleton(service);builder.Services.AddSingleton(seasons);builder.Services.AddSingleton(career);
await using var app=builder.Build();app.MapDiamondMatch();app.MapDiamondSeason();app.MapDiamondCareer();await app.StartAsync();
var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
HttpClient Client()=>new(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri(address)};
using var host=Client();using var guest=Client();using var stranger=Client();
var hostLook=new DiamondCareerAppearance{HeightCm=205,BodyType="power",SkinTone="#986432",HairStyle="flow",HairColor="#402020",GloveColor="#aabbcc",BatColor="#987654",JerseyNumber="42"};
var guestLook=new DiamondCareerAppearance{HeightCm=163,BodyType="lean",SkinTone="#bbaa88",HairStyle="buzz",HairColor="#333333",GloveColor="#553311",BatColor="#345678",JerseyNumber="07"};
async Task<DiamondCareerPlayer> MakePlayer(HttpClient client,string name,string team,string position,DiamondCareerAppearance appearance){using var r=await client.PostAsJsonAsync("/api/diamond/career",new{op="create",requestId=Guid.NewGuid().ToString("N"),name,team,position,bats="L",throws="L",delivery="sidearm",archetype="balanced",appearance},DiamondJson.Options);Check(r.IsSuccessStatusCode,"HTTP custom player creation");return (await r.Content.ReadFromJsonAsync<DiamondCareerResponse>(DiamondJson.Options))!.Player!;}
var hostPlayer=await MakePlayer(host,"홈 투수","HH","P",hostLook);var guestPlayer=await MakePlayer(guest,"원정 포수","LG","C",guestLook);
var hostPlayerId="2025:"+hostPlayer.Id;var guestPlayerId="2025:"+guestPlayer.Id;
async Task<JsonElement> Request(HttpClient client,object? body=null,int status=200,string? code=null)
{
 using var response=body==null?await client.GetAsync("/api/diamond/match?code="+code):await client.PostAsJsonAsync("/api/diamond/match",body,DiamondJson.Options);
 var text=await response.Content.ReadAsStringAsync();Check((int)response.StatusCode==status,$"HTTP {status} expected, got {(int)response.StatusCode}: {text[..Math.Min(text.Length,400)]}");
 Check(response.Headers.CacheControl?.NoStore==true,"match response cache must be private/no-store");using var doc=JsonDocument.Parse(text);return doc.RootElement.Clone();
}
DiamondSeasonMatchResponse Decode(JsonElement body){var state=body.Deserialize<DiamondSeasonMatchResponse>(DiamondJson.Options)!;var game=state.Save.Game!;var batting=game.Half=="top"?game.AwayTeam:game.HomeTeam;Check(state.Action!.Role==(batting==state.Match.Team?"batter":"pitcher"),"viewer role follows current half");return state;}
Dictionary<string,object?> Command(DiamondSeasonMatchResponse state,string op)=>new(){["op"]=op,["code"]=state.Match.Code,["version"]=state.Match.Version,["requestId"]=Guid.NewGuid().ToString("N")};
var create=new{op="create",requestId="match-create-host",season=2025,hostTeam="HH",guestTeam="LG",mode="pvp",pace="full"};
var raw=await Request(host,create);var state=Decode(raw);var code=state.Match.Code;
Check(code.Length==8&&state.Match.Waiting&&state.Action is{Mode:"pvp",Waiting:true,Role:"pitcher"},"host waits as home defense");
Check(state.Save.Schedule.Count==1&&state.Save.Teams.Count==2&&state.Save.Team=="HH","standalone single fixture");
Check(state.Appearances.Count==1&&state.Appearances[hostPlayerId].JerseyNumber=="42","create snapshots host avatar only");
Check(!raw.GetProperty("save").GetProperty("game").TryGetProperty("duel",out _)&&!raw.GetProperty("match").TryGetProperty("host",out _),"no participant IDs/hidden duel");
Check(raw.GetProperty("serverSentAt").GetInt64()>=raw.GetProperty("serverReceivedAt").GetInt64(),"clock envelope");
Check(Decode(await Request(host,create)).Match.Version==state.Match.Version,"create retry once");
await Request(host,new{op="create",requestId="match-create-host",season=2025,hostTeam="HH",guestTeam="NC",mode="pvp",pace="full"},409);
await Request(host,new{op="create",requestId="match-same-team1",season=2025,hostTeam="HH",guestTeam="HH",mode="pvp",pace="full"},400);
await Request(host,Command(state,"pitch"),409);await Request(guest,null,403,code);
var joined=Decode(await Request(guest,new{op="join",requestId="match-guest-join1",code}));
Check(joined.Match.Team=="LG"&&joined.Save.Team=="LG"&&!joined.Match.Waiting&&joined.Action!.Role=="batter","guest joins away offense");
state=Decode(await Request(host,null,200,code));Check(state.Match.Version==joined.Match.Version&&!state.Match.Waiting,"two owners share match version");
Check(state.Appearances.Count==2&&state.Appearances[guestPlayerId].GloveColor==guestLook.GloveColor&&state.Appearances[guestPlayerId].HairStyle==guestLook.HairStyle,"join snapshots guest visual fields");
Check(state.Save.Game!.HomePitcher==hostPlayerId&&state.Save.Game.AwayLineup.Contains(guestPlayerId),"both custom players join actual game");
Check(JsonSerializer.Serialize(state.Appearances,DiamondJson.Options)==JsonSerializer.Serialize(joined.Appearances,DiamondJson.Options),"both participants receive same avatar snapshot");
using(var edit=await guest.PostAsJsonAsync("/api/diamond/career",new{op="appearance",version=guestPlayer.Version,requestId="match-edit-appearance",name=guestPlayer.Name,appearance=new DiamondCareerAppearance{HeightCm=215,BodyType="power",SkinTone="#111111",JerseyNumber="99"}},DiamondJson.Options))Check(edit.IsSuccessStatusCode,"career appearance editable outside live match");
Check(Decode(await Request(host,null,200,code)).Appearances[guestPlayerId].HeightCm==163,"live friendly preserves joined appearance after career edit");
string etag;
using(var poll=await host.GetAsync("/api/diamond/match?code="+code)){etag=poll.Headers.ETag!.ToString();Check(etag.Contains("HH"),"ETag contains viewer team");}
using(var request=new HttpRequestMessage(HttpMethod.Get,"/api/diamond/match?code="+code)){request.Headers.IfNoneMatch.ParseAdd(etag);using var r=await host.SendAsync(request);Check(r.StatusCode==HttpStatusCode.NotModified&&(await r.Content.ReadAsStringAsync()).Length==0,"unchanged conditional poll sends no JSON");}
await Request(stranger,new{op="join",requestId="match-third-join1",code},409);await Request(stranger,null,403,code);
foreach(var op in new[]{"ready","sim-half","sim-game"})await Request(host,Command(state,op),403);
await Request(guest,Command(joined,"pitch"),403);await Request(host,Command(state,"swing"),403);
var hostTeam=state.Save.Teams.Single(t=>t.Code=="HH");var guestTeam=state.Save.Teams.Single(t=>t.Code=="LG");
var wrongSub=Command(state,"substitute");wrongSub["slot"]=2;wrongSub["playerId"]=guestTeam.Batters.First(b=>!guestTeam.Lineup.Contains(b.Id)).Id;await Request(host,wrongSub,400);
var sub=Command(state,"substitute");sub["slot"]=2;sub["playerId"]=hostTeam.Batters.First(b=>!hostTeam.Lineup.Contains(b.Id)).Id;state=Decode(await Request(host,sub));Check(state.Save.Game!.HomeLineup[2]==(string)sub["playerId"]!,"host substitutes only own lineup");
var relief=Command(state,"change-pitcher");relief["playerId"]=guestTeam.Pitchers.First(p=>p.Id!=state.Save.Game.AwayPitcher).Id;joined=Decode(await Request(guest,relief));Check(joined.Save.Game!.AwayPitcher==(string)relief["playerId"]!,"guest changes own pitcher");state=Decode(await Request(host,null,200,code));

var pitch=Command(state,"pitch");pitch["previousPitch"]=0;pitch["type"]="fastball";pitch["aim"]=new{x=0,y=0};pitch["quality"]=1;
var both=await Task.WhenAll(Request(host,pitch),Request(host,pitch));state=Decode(both[0]);Check(Decode(both[1]).Action!.PitchCount==1&&state.Action!.PitchCount==1,"concurrent identical pitch requests commit once");
Check(state.Action!.Pitch is{AiBatterSwing:null,AiBatterSwingPrepared:false}&&state.Action.Pitch.ReleaseAt==now+2600,"PvP has no AI choice and synchronized windup");
using(var request=new HttpRequestMessage(HttpMethod.Get,"/api/diamond/match?code="+code)){request.Headers.IfNoneMatch.ParseAdd(etag);using var r=await host.SendAsync(request);Check(r.IsSuccessStatusCode&&r.Headers.ETag!.ToString()!=etag,"new pitch invalidates conditional poll");}
await Request(host,Command(joined,"tick"),409);
var ball=state.Action.Pitch!;now=(long)Math.Ceiling(ball.ReleaseAt+ball.FlightMs-95);
var swing=Command(state,"swing");swing["pitchId"]=ball.Id;swing["inputAt"]=now;swing["aim"]=ball.Target;
joined=Decode(await Request(guest,swing));Check(joined.Action!.Pitch!.Resolved,"guest swing resolves once");
Check(Decode(await Request(guest,swing)).Action!.History.Count==joined.Action.History.Count,"swing replay preserves result");
state=Decode(await Request(host,null,200,code));Check(JsonSerializer.Serialize(state.Action!.Pitch!.Reaction,DiamondJson.Options)==JsonSerializer.Serialize(joined.Action.Pitch.Reaction,DiamondJson.Options),"both owners see identical result");
now+=2000;
// Distinct racing requests at one version: exactly one may start the next pitch.
var p1=Command(state,"pitch");p1["previousPitch"]=state.Action.PitchCount;p1["type"]="fastball";p1["aim"]=new{x=0,y=0};p1["quality"]=1;
var p2=new Dictionary<string,object?>(p1){["requestId"]=Guid.NewGuid().ToString("N")};
var races=await Task.WhenAll(host.PostAsJsonAsync("/api/diamond/match",p1),host.PostAsJsonAsync("/api/diamond/match",p2));Check(races.Count(x=>x.IsSuccessStatusCode)==1&&races.Count(x=>x.StatusCode==HttpStatusCode.Conflict)==1,"distinct concurrent pitch rejects stale input");foreach(var r in races)r.Dispose();
state=Decode(await Request(host,null,200,code));
// Actual HTTP pitch/take commands complete a 9+ inning game, changing both owners' roles each half.
var manualPitches=0;while(!state.Save.Game!.Complete)
{
 if(++manualPitches>500)throw new InvalidDataException("manual match failed to finish");
 var fieldClient=state.Save.Game.Half=="top"?host:guest;var batClient=state.Save.Game.Half=="top"?guest:host;
 if(state.Action!.Pitch is not{Resolved:false}){now+=2000;var next=Command(state,"pitch");next["previousPitch"]=state.Action.PitchCount;next["type"]="fastball";next["aim"]=new{x=0,y=0};next["quality"]=1;state=Decode(await Request(fieldClient,next));}
 var current=state.Action!.Pitch!;now=(long)Math.Ceiling(current.ReleaseAt+current.FlightMs+5);state=Decode(await Request(batClient,Command(state,"take")));
 if(state.Save.Game!.PlateAppearances==6)Check(!state.Action!.Done&&!state.Save.Complete,"full match continues after six plate appearances");
 if(manualPitches%50==0)Console.WriteLine($"HTTP game {state.Save.Game.Inning}{state.Save.Game.Half}, {manualPitches} pitches");
}
var finalHost=Decode(await Request(host,null,200,code));var finalGuest=Decode(await Request(guest,null,200,code));
Check(finalHost.Save.Game!.Inning>=9&&finalHost.Save.Complete&&finalHost.Action!.Done,"PvP ends only after regulation/extra innings");
Check(finalHost.Save.Game.HomeRuns==finalGuest.Save.Game!.HomeRuns&&finalHost.Save.Game.AwayRuns==finalGuest.Save.Game.AwayRuns&&finalHost.Match.Version==finalGuest.Match.Version,"final scores/version agree");
await Request(host,Command(finalHost,"sim-game"),403);

var ai=Decode(await Request(host,new{op="create",requestId="match-create-ai01",season=2025,hostTeam="HH",guestTeam="LG",mode="ai",pace="full"}));
Check(!ai.Match.Waiting&&ai.Action!.Mode=="ai","AI nine-inning fixture ready");ai=Decode(await Request(host,Command(ai,"sim-game")));Check(ai.Save.Game!.Complete&&ai.Save.Game.Inning>=9,"AI nine-inning fixture completes");
using(var league=await host.GetAsync("/api/diamond/season")){using var doc=JsonDocument.Parse(await league.Content.ReadAsStringAsync());Check(doc.RootElement.GetProperty("save").ValueKind==JsonValueKind.Null,"friendly match does not create league save");}
Check(rewards==0,"friendly game never awards career/league rewards");
string owner;
using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=service.DatabasePath}.ToString())){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT Owner FROM DiamondSeasonMatches WHERE Code=$code";cmd.Parameters.AddWithValue("$code",code);owner=(string)cmd.ExecuteScalar()!;}
var restarted=new DiamondSeasonMatchService(new(Path.Combine(output,"state"),Path.GetFullPath(args[0]),new(Path.Combine(output,"missing-source.db")),()=>now,random.NextDouble));
Check(restarted.Get(code,owner).Save.Game!.Complete,"same owner re-enters completed match after service restart without source DB");
Check(restarted.Get(code,owner).Appearances[guestPlayerId].JerseyNumber=="07","guest appearance survives restart from persisted match alone");
Check(restarted.Post(JsonSerializer.SerializeToElement(create,DiamondJson.Options),owner,"test").Match.Code==code,"create replay survives source DB outage");
var oldExpiry=finalHost.Match.ExpiresAt;now=oldExpiry-3600000;var active=Decode(await Request(host,null,200,code));Check(active.Match.ExpiresAt>oldExpiry&&active.Match.Version==finalHost.Match.Version,"active GET renews expiry without invalidating inputs");
now=active.Match.ExpiresAt+1;await Request(host,null,404,code);
using(var cross=new HttpRequestMessage(HttpMethod.Post,"/api/diamond/match"){Content=JsonContent.Create(create)}){cross.Headers.Add("Origin","https://unrelated.example");using var r=await host.SendAsync(cross);Check(r.StatusCode==HttpStatusCode.Forbidden,"cross origin rejected");}
using(var invalid=await host.PostAsync("/api/diamond/match",new StringContent("{bad")))Check(invalid.StatusCode==HttpStatusCode.BadRequest,"bad JSON rejected");
using(var large=await host.PostAsync("/api/diamond/match",new StringContent(new string('x',4097))))Check(large.StatusCode==HttpStatusCode.RequestEntityTooLarge,"body bounded");
for(var i=0;i<9;i++)await Request(stranger,new{op="create",requestId="match-rate-limit"+i,season=2025,hostTeam="HH",guestTeam="LG",mode="pvp",pace="full"},i<8?200:429);
// Exhibition games must also work with historical eight-team warehouses.
var historicalSource=Path.Combine(output,"historical-source.db");File.Copy(source,historicalSource);
using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=historicalSource,Pooling=false}.ToString()))
{
 c.Open();using var cmd=c.CreateCommand();
 cmd.CommandText="DELETE FROM Games WHERE AwayTeamCode IN ('KT','NC'); UPDATE Games SET SeasonYear=2012,GameDate='2012-09-01';";cmd.ExecuteNonQuery();
}
var historicalHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(historicalSource)));
var historicalRoster=new DiamondRosterService(historicalSource);Check(historicalRoster.Get(2012).Teams.Count==8,"historical fixture contains eight teams");
var historicalSeasons=new DiamondSeasonService(Path.Combine(output,"historical-state"),Path.GetFullPath(args[0]),historicalRoster,()=>now,new Random(91).NextDouble);
var historicalMatch=new DiamondSeasonMatchService(historicalSeasons);
var historical=historicalMatch.Post(JsonSerializer.SerializeToElement(new{op="create",requestId="historical-match-create",season=2012,hostTeam="HH",guestTeam="LG",mode="ai",pace="full"}),"historical-owner","historical-ip");
Check(historical.Save.Season==2012&&historical.Save.Teams.Count==2&&historical.Save.Schedule.Count==1,"eight-team season creates chosen friendly pair");
historical=historicalMatch.Post(JsonSerializer.SerializeToElement(new{op="sim-game",requestId="historical-match-complete",code=historical.Match.Code,version=historical.Match.Version}),"historical-owner","historical-ip");
Check(historical.Save.Game is{Complete:true,Inning:>=9},"historical friendly completes a full game");
try{historicalSeasons.Post(JsonSerializer.SerializeToElement(new{op="create",requestId="historical-league-create",season=2012,team="HH",pace="full"}),"historical-league-owner");throw new InvalidDataException("eight-team full league did not reject");}
catch(DiamondInputError e){Check(e.Status==409,"full league still requires ten playable teams");}
try{historicalMatch.Post(JsonSerializer.SerializeToElement(new{op="create",requestId="historical-missing-team",season=2012,hostTeam="HH",guestTeam="KT",mode="ai",pace="full"}),"historical-owner","historical-ip");throw new InvalidDataException("missing friendly team did not reject");}
catch(DiamondInputError e){Check(e.Status==409,"friendly still requires both selected teams");}
Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(historicalSource)))==historicalHash,"historical warehouse unchanged");
Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))==sourceHash,"source DB unchanged");
await app.StopAsync();Console.WriteLine($"PASS {checks} real HTTP friendly checks: two owners, races/replays, {manualPitches} manual pitches, 9+ innings, AI, isolation, expiry and endpoint guards");File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{checks,manualPitches,sourceHash}));Console.WriteLine(output);
