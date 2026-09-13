using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using NaverSabermetrics.Web;

if(args.Length >= 4 && args[0] == "--balance")
{
    var gameLib=args[1];var scratch=args[2];var realSource=args[3];
    var directory=Path.Combine(Path.GetFullPath(scratch),"balance-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
    var catalog=new DiamondRosterService(Path.GetFullPath(realSource));var rosterData=catalog.Get(args.Length>4?int.Parse(args[4]):null);var seed=args.Length>5?int.Parse(args[5]):193742;var random=new Random(seed);
    var serviceBalance=new DiamondSeasonService(directory,Path.GetFullPath(gameLib),catalog,random:random.NextDouble);
    var initial=JsonSerializer.SerializeToElement(new{op="create",requestId="balance-create-001",season=rosterData.Season,team="KT",pace="full",seriesPerPair=1},DiamondJson.Options);
    var state=serviceBalance.Post(initial,"balance-owner");var timer=Stopwatch.StartNew();
    while(!state.Save!.Complete)
        state=serviceBalance.Post(JsonSerializer.SerializeToElement(new{op="sim-day",requestId=Guid.NewGuid().ToString("N"),version=state.Save.Version},DiamondJson.Options),"balance-owner");
    var stats=state.Save.PlayerStats.Values;var teamGames=state.Save.Schedule.Count*2d;
    var result=new{season=rosterData.Season,seed,games=state.Save.Schedule.Count,runsPerTeamGame=stats.Sum(x=>x.R)/teamGames,
        avg=stats.Sum(x=>x.H)/(double)stats.Sum(x=>x.AB),hrPerTeamGame=stats.Sum(x=>x.HR)/teamGames,
        bbPerTeamGame=stats.Sum(x=>x.BB)/teamGames,kPerTeamGame=stats.Sum(x=>x.K)/teamGames,paPerTeamGame=stats.Sum(x=>x.PA)/teamGames,
        originalLineupAvg=state.Save.Teams.SelectMany(t=>t.Lineup.Select(id=>t.Batters.Single(b=>b.Id==id).Avg)).Average(),seconds=timer.Elapsed.TotalSeconds};
    File.WriteAllText(Path.Combine(directory,"balance.json"),JsonSerializer.Serialize(result,DiamondJson.Options));
    Console.WriteLine(JsonSerializer.Serialize(result,DiamondJson.Options));Console.WriteLine(directory);
    if(result.runsPerTeamGame is <2 or >10 || result.avg is <.19 or >.37 || result.hrPerTeamGame>=3)
        throw new InvalidDataException("Full-game balance drifted outside regression bounds.");
    return;
}
if (args.Length < 2) throw new ArgumentException("Usage: <game-lib-directory> <scratch-directory> [--short]");
var output = Path.Combine(Path.GetFullPath(args[1]), "season-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
var source = Path.Combine(output, "source.db"); RosterFixture.Create(source);
using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Pooling = false }.ToString()))
{
    c.Open(); using var tx = c.BeginTransaction(); using var clear = c.CreateCommand(); clear.Transaction = tx;
    clear.CommandText = "DELETE FROM Games; DELETE FROM Players; DELETE FROM GamePlayers; DELETE FROM BatterGameStats; DELETE FROM PitcherGameStats; DELETE FROM Pitches; DELETE FROM BattingGameLines; DELETE FROM PitchingGameLines;"; clear.ExecuteNonQuery();
    void Insert(string table, params object[] values)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO {table} VALUES({string.Join(',', values.Select((_, i) => "$p" + i))})";
        for (var i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("$p" + i, values[i]); cmd.ExecuteNonQuery();
    }
    string[] teams = ["HH", "HT", "KT", "LG", "LT", "NC", "OB", "SK", "SS", "WO"];
    for (var t = 0; t < 10; t++)
    {
        var team = teams[t]; var gid = "g" + t;
        Insert("Games", gid, 2025, "2025-09-01", "kbo_r", "RESULT", team, teams[(t + 1) % 10]);
        for (var n = 0; n < 12; n++)
        {
            var id = team + "b" + n; Insert("Players", id, id, n % 2 == 0 ? "우투우타" : "우투좌타", "외야수");
            Insert("BatterGameStats", gid, id, team, id, 400,350,90,10,35,5,10,85,145,1600,800,800,520,180,440,100);
        }
        for (var n = 0; n < 8; n++)
        {
            var id = team + "p" + n; Insert("Players", id, id, n % 2 == 0 ? "우투우타" : "좌투좌타", "투수");
            Insert("PitcherGameStats", gid,id,team,id,500,1,45,120,45,120,380-n*20,110,40,2000,1000,1000,650,230,500,140);
            Insert("Pitches", gid,id,"직구",145d+n*.2); Insert("Pitches",gid,id,"슬라이더",133d);
        }
    }
    tx.Commit();
}
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
var checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new InvalidDataException(message); }
void Error(Action action, int status, string message) { try { action(); throw new InvalidDataException(message + " did not reject"); } catch (DiamondInputError e) { Check(e.Status == status, message + " wrong status " + e.Status); } }
T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, DiamondJson.Options), DiamondJson.Options)!;
JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, DiamondJson.Options);
long now = 1700000000000; var rng = new Random(3817); var completed = new HashSet<string>(); var attempts = 0;
var roster = new DiamondRosterService(source);
DiamondSeasonService Service(Action<string,DiamondSeasonGame>? hook = null) => new(Path.Combine(output, "state"), Path.GetFullPath(args[0]), roster, () => now, rng.NextDouble, gameCompleted: hook);
void Reward(string actor, DiamondSeasonGame game) { attempts++; Check(game.Complete, "only complete games reward"); completed.Add(game.Id); }
var service = Service(Reward);
Check(service.Get("owner").Save == null, "empty owner");
var request = new { op = "create", requestId = "create-0001", season = 2025, team = "HH", pace = "full", seriesPerPair = args.Contains("--short") ? 1 : 8 };
var response = service.Post(Json(request), "owner");
var save = response.Save!;
Check(save.Schedule.Count == 90 * save.SeriesPerPair && save.TotalDays == 18 * save.SeriesPerPair, "full schedule size");
Check(save.Schedule.GroupBy(x => x.Day).All(day => day.Count() == 5 && day.SelectMany(x => new[] { x.HomeTeam, x.AwayTeam }).Distinct().Count() == 10), "one game per team per day");
foreach (var team in save.Teams) foreach (var other in save.Teams.Where(x => x.Code != team.Code))
    Check(save.Schedule.Count(x => x.HomeTeam == team.Code && x.AwayTeam == other.Code) == save.SeriesPerPair, "balanced home/away pairs");
Check(service.Post(Json(request), "owner").Save!.Id == save.Id, "create replay idempotent");
Error(() => service.Post(Json(new { op = "create", requestId = "create-0001", season = 2025, team = "LG" }), "owner"), 409, "request identifier payload mismatch");
Check(service.Get("intruder").Save == null, "ownership isolated");
Error(() => service.Post(Json(new { op = "start-game", version = save.Version, requestId = "attacker-0001" }), "intruder"), 404, "other actor cannot mutate save");
DiamondSeasonResponse Post(string op, Dictionary<string,object?>? values = null)
{
    var body = values ?? []; body["op"] = op; body["version"] = response.Save!.Version; body["requestId"] = Guid.NewGuid().ToString("N");
    response = service.Post(Json(body), "owner"); return response;
}
Post("start-game"); Check(response.Action is { Done: false } && response.Save!.Game!.HomeLineup.Count == 9 && response.Save.Game.AwayLineup.Count == 9, "full game start nine batters");
var started = response; var initialGame = Copy(response.Save!.Game!);
var rulesSave = JsonSerializer.Deserialize<DiamondSeasonSave>(JsonSerializer.Serialize(response.Save, DiamondJson.Options), DiamondJson.Options)!;
DiamondSeasonGame Scenario(int inning = 1, string half = "top", int outs = 0, int home = 0, int away = 0)
{
    var g = Copy(initialGame); g.Inning = inning; g.Half = half; g.Outs = outs; g.HomeRuns = home; g.AwayRuns = away;
    g.HomeLine = Enumerable.Repeat(0, inning).ToList(); g.AwayLine = Enumerable.Repeat(0, inning).ToList();
    g.Bases = new DiamondSeasonRunner?[3]; g.PlayerStats.Clear(); return g;
}
void Plate(DiamondSeasonGame g, string outcome, string? trajectory = null, double distance = 0, double random = .99) =>
    DiamondSeasonRules.ApplyPlate(rulesSave, g, new() { PlateEnded = true, Outcome = outcome, Label = outcome, Trajectory = trajectory, Distance = distance }, () => random, now);
DiamondSeasonRunner Runner(DiamondSeasonGame g, int index) => new((g.Half == "top" ? g.AwayLineup : g.HomeLineup)[index], DiamondSeasonRules.Pitcher(g));
foreach (var outcome in new[] { "BB", "HBP" })
{
    var g = Scenario(); g.Bases = [Runner(g,1),Runner(g,2),Runner(g,3)]; Plate(g,outcome);
    Check(g.AwayRuns == 1 && g.Bases.All(x => x != null) && g.Outs == 0, outcome + " forced bases-loaded run");
    Check(g.PlayerStats.Values.Sum(x=>x.RBI) == 1 && g.PlayerStats.Values.Sum(x=>x.AB) == 0, outcome + " RBI and no AB");
    g = Scenario(); g.Bases[2] = Runner(g,3); Plate(g,outcome); Check(g.AwayRuns == 0 && g.Bases[2] != null, outcome + " unforced third stays");
}
foreach (var (outcome,bases) in new[]{("1B",1),("2B",2),("3B",3),("HR",4)})
{
    var g=Scenario(); Plate(g,outcome); Check(g.PlayerStats.Values.Sum(x=>x.H)==1 && (bases==4?g.AwayRuns==1:g.Bases[bases-1]!=null),outcome+" advancement");
}
{
    var g=Scenario(); g.Bases=[Runner(g,1),Runner(g,2),Runner(g,3)]; Plate(g,"HR"); Check(g.AwayRuns==4&&g.Bases.All(x=>x==null),"grand slam four runs");
    g=Scenario(9,"bottom",0,2,2); g.Bases=[Runner(g,1),Runner(g,2),Runner(g,3)]; Plate(g,"2B"); Check(g.Complete&&g.HomeRuns==3&&g.EndReason=="walkoff","non-HR walkoff only winning run");
    g=Scenario(9,"bottom",0,2,2); g.Bases=[Runner(g,1),Runner(g,2),Runner(g,3)]; Plate(g,"HR"); Check(g.Complete&&g.HomeRuns==6,"walkoff HR counts all runs");
    g=Scenario(9,"top",2,3,2); Plate(g,"K"); Check(g.Complete&&g.EndReason=="home-ahead","skip bottom ninth when home ahead");
    g=Scenario(9,"bottom",2,2,3); Plate(g,"K"); Check(g.Complete&&g.EndReason=="regulation","away wins after bottom ninth");
    g=Scenario(9,"bottom",2,2,2); Plate(g,"K"); Check(!g.Complete&&g.Inning==10&&g.Half=="top"&&g.Outs==0,"tied ninth goes extra innings");
    g=Scenario(12,"bottom",2,2,2); Plate(g,"K"); Check(g.Complete&&g.EndReason=="tie-12","tied twelve ends draw");
    g=Scenario(1,"top",1); g.Bases=[Runner(g,1),null,Runner(g,3)]; Plate(g,"OUT","ground",0,.01); Check(g.Half=="bottom"&&g.AwayRuns==0&&g.PlayerStats.Values.Sum(x=>x.GIDP)==1,"force third DP cancels run");
    g=Scenario(1,"top",0); g.Bases=[Runner(g,1),null,Runner(g,3)]; Plate(g,"OUT","ground",0,.01); Check(g.Outs==2&&g.AwayRuns==1&&g.PlayerStats.Values.Sum(x=>x.RBI)==0,"zero-out DP can score without RBI");
    g=Scenario(1,"top",1); g.Bases[2]=Runner(g,3); Plate(g,"OUT","fly",65); Check(g.Outs==2&&g.AwayRuns==1&&g.PlayerStats.Values.Sum(x=>x.SF)==1&&g.PlayerStats.Values.Sum(x=>x.AB)==0,"sac fly counts SF not AB");
    g=Scenario(1,"top",2); g.Bases[2]=Runner(g,3); Plate(g,"OUT","fly",65); Check(g.AwayRuns==0&&g.PlayerStats.Values.Sum(x=>x.SF)==0,"two-out fly cannot sacrifice");
    g=Scenario(); var inherited=Runner(g,3); g.Bases[2]=inherited;
    var otherPitcher=rulesSave.Teams.Single(x=>x.Code==g.HomeTeam).Pitchers[1].Id;g.HomePitcher=otherPitcher;
    Plate(g,"1B");Check(g.PlayerStats[inherited.PitcherId].RunsAllowed==1&&g.PlayerStats[otherPitcher].RunsAllowed==0,"inherited run charged to original pitcher");
    g=Scenario();for(var i=0;i<9;i++)Plate(g,"HR");Check(g.AwayOrder==0&&g.PlateAppearances==9&&!g.Complete,"batting order wraps nine and game does not end at six PA");
    var slow=Scenario();var fast=Scenario();slow.Bases[0]=Runner(slow,1);fast.Bases[0]=Runner(fast,1);
    var runnerProfile=rulesSave.Teams.SelectMany(x=>x.Batters).Single(x=>x.Id==slow.Bases[0]!.PlayerId).Profile!;
    runnerProfile.GameRatings=new(){Speed=25};Plate(slow,"2B",null,0,.65);
    runnerProfile.GameRatings=new(){Speed=95};Plate(fast,"2B",null,0,.65);
    Check(slow.AwayRuns==0&&fast.AwayRuns==1,"speed rating changes first-to-home advancement");runnerProfile.GameRatings=null;
}
Console.WriteLine($"PASS schedule/base/outs/walkoff/extra innings ({checks} checks)");
double FairHitRate(double battingAverage,double whip,double quality,bool ai)
{
    var g=Copy(initialGame);g.Duel.HostRole=ai?"pitcher":"batter";var r=g.Duel.Roster!;var b=Copy(r.Batter);var p=Copy(r.Pitcher);
    b.Ab=500;b.Pa=560;b.H=500*battingAverage;b.So=100;b.Hr=12;b.Slg=battingAverage+.14;
    p.Tbf=600;p.Outs=450;p.Bb=50;p.So=120;p.Whip=whip;
    g.Duel.Roster=r with{Batter=b,Pitcher=p};var random=new Random(18273);var hits=0;
    for(var i=0;i<12000;i++)
    {
        var result=new DiamondResult{PlateEnded=true,Outcome="1B",Kind="hit",Quality=quality,Trajectory="line",ExitSpeed=145,LaunchAngle=20,Distance=85,Direction=0};
        DiamondSeasonFairBall.Resolve(rulesSave,g,result,ai,random.NextDouble);if(result.Kind=="hit")hits++;
    }
    return hits/12000d;
}
Check(FairHitRate(.34,1.3,.65,true)>FairHitRate(.20,1.3,.65,true)+.08,"AI contact hit probability responds to actual batting record");
Check(FairHitRate(.27,1.7,.65,true)>FairHitRate(.27,.9,.65,true)+.08,"opponent WHIP controls AI hit probability");
Check(FairHitRate(.27,1.3,.95,false)>FairHitRate(.27,1.3,.4,false)+.25,"manual aiming/timing quality is strongly rewarded");
{
    var g=Copy(initialGame);g.Duel.HostRole="batter";
    var inside=new DiamondResult{PlateEnded=true,Outcome="HR",Kind="hit",Quality=1,Trajectory="line",ExitSpeed=170,LaunchAngle=24,Distance=110,Direction=0};
    DiamondSeasonFairBall.Resolve(rulesSave,g,inside,false,()=>.99);Check(inside.Outcome!="HR","110m center contact cannot clear 122m fence");
    var over=new DiamondResult{PlateEnded=true,Outcome="HR",Kind="hit",Quality=1,Trajectory="fly",ExitSpeed=180,LaunchAngle=30,Distance=135,Direction=0};
    DiamondSeasonFairBall.Resolve(rulesSave,g,over,false,()=>.99);Check(over.Outcome=="HR","well-hit manual ball over actual fence stays HR");
}
var teamRoster=response.Save!.Teams.Single(x=>x.Code=="HH");
Post("substitute",new(){["slot"]=0,["playerId"]=teamRoster.Batters[9].Id});
Error(()=>Post("substitute",new(){["slot"]=0,["playerId"]=teamRoster.Lineup[0]}),400,"no batter reentry");
Post("change-pitcher",new(){["playerId"]=teamRoster.Pitchers[7].Id});
Error(()=>Post("change-pitcher",new(){["playerId"]=teamRoster.Pitchers[7].Id}),400,"no pitcher reentry");
var oldVersion=response.Save!.Version;
Error(()=>service.Post(Json(new{op="tick",version=oldVersion-1,requestId="stale-0001"}),"owner"),409,"stale version rejected");
var opPitch=response.Action!.Role=="batter"?"ready":"pitch";
Post(opPitch,new(){["previousPitch"]=response.Action.PitchCount,["type"]="fastball",["aim"]=new{x=0,y=0},["quality"]=.7});
Check(response.Action!.Pitch is {Resolved:false},"real pending pitch");
Error(()=>Post("change-pitcher",new(){["playerId"]=teamRoster.Pitchers[6].Id}),409,"cannot substitute during pitch");
var pitchBefore=JsonSerializer.Serialize(response.Action.Pitch,DiamondJson.Options);
service=Service(Reward); response=service.Get("owner"); Check(JsonSerializer.Serialize(response.Action!.Pitch,DiamondJson.Options)==pitchBefore,"pending pitch restart exact restoration");
now=(long)(response.Action.Pitch!.ReleaseAt+response.Action.Pitch.FlightMs+2000); response=service.Get("owner");
Check(response.Action!.Pitch!.Resolved&&response.Save!.Version>oldVersion,"GET persists expiry resolution");
var tickVersion=response.Save!.Version; response=service.Get("owner"); Check(response.Save!.Version==tickVersion,"repeat tick idempotent");
var dayTimer=Stopwatch.StartNew(); Post("sim-half"); Check(response.Save!.Game!.PlateAppearances>=3,"half inning simulation includes real plate appearances");
Post("sim-game"); dayTimer.Stop();
Check(response.Save!.Game!.Complete&&response.Save.Game.Inning>=9&&response.Save.Game.PlateAppearances>=51,"game exceeds six PA and reaches nine innings");
Check(response.Save.Schedule.Count(x=>x.Complete)==5&&response.Save.Day==2,"other teams advance with own game");
Check(response.Save.Standings.Sum(x=>x.Wins)==response.Save.Standings.Sum(x=>x.Losses),"balanced league W/L");
Check(completed.Count==1,"one completed game reward");
var totals=JsonSerializer.Serialize(response.Save.PlayerStats,DiamondJson.Options);
service=Service(Reward); response=service.Get("owner"); Check(JsonSerializer.Serialize(response.Save!.PlayerStats,DiamondJson.Options)==totals&&completed.Count==1,"restart cumulative totals and reward dedupe");
Console.WriteLine($"PASS actions/restart/substitutions/one league day in {dayTimer.Elapsed.TotalSeconds:F2}s ({checks} checks)");
var manual=service.Post(Json(new{op="create",requestId="manual-create-01",season=2025,team="WO",pace="full",seriesPerPair=1}),"manual-owner");
DiamondSeasonResponse Manual(string op,Dictionary<string,object?>? values=null)
{var body=values??[];body["op"]=op;body["version"]=manual.Save!.Version;body["requestId"]=Guid.NewGuid().ToString("N");manual=service.Post(Json(body),"manual-owner");return manual;}
Manual("start-game");Check(manual.Action!.Role=="batter","away team controls batting first");
Error(()=>Manual("pitch",new(){["previousPitch"]=0,["type"]="fastball",["quality"]=1,["aim"]=new{x=0,y=0}}),403,"wrong role cannot pitch");
for(var plate=0;plate<6;plate++)
{
    Manual("ready",new(){["previousPitch"]=manual.Action!.PitchCount});var current=manual.Action!.Pitch!;
    now=(long)(current.ReleaseAt+current.FlightMs-DiamondEngine.SwingContactMs);
    Error(()=>Manual("swing",new(){["pitchId"]=current.Id,["inputAt"]=now+151,["aim"]=current.Target}),409,"future swing timestamp guard");
    Manual("swing",new(){["pitchId"]=current.Id,["inputAt"]=now,["aim"]=current.Target});
    Check(manual.Action!.Pitch!.Resolved&&manual.Action.History[^1].PlateEnded,"manual aimed/timed swing resolves a plate");
    var plateCount=manual.Save!.Game!.PlateAppearances;Manual("swing",new(){["pitchId"]=current.Id,["inputAt"]=now,["aim"]=current.Target});
    Check(manual.Save!.Game!.PlateAppearances==plateCount,"repeat swing cannot duplicate plate stats");now+=2500;
}
Check(manual.Save!.Game!.PlateAppearances==6&&!manual.Action!.Done,"six direct-play PAs do not terminate full game");
Manual("ready",new(){["previousPitch"]=manual.Action!.PitchCount});var taken=manual.Action!.Pitch!;
var versionBeforeTake=manual.Save!.Version;Manual("take");Check(manual.Action!.Pitch is{Resolved:false},"take cannot resolve a ball before arrival");
now=(long)(taken.ReleaseAt+taken.FlightMs+1);Manual("take");Check(manual.Action!.Pitch!.Resolved,"take at plate resolves no-swing judgment");
// Two distinct inputs based on one version cannot both consume the same state.
var raceVersion=response.Save!.Version; var raceResults=new System.Collections.Concurrent.ConcurrentBag<int>();
Parallel.For(0,2,i=>{try{service.Post(Json(new{op="lineup",version=raceVersion,requestId="parallel-000"+i,lineup=teamRoster.Lineup}),"owner");raceResults.Add(200);}catch(DiamondInputError e){raceResults.Add(e.Status);}});
Check(raceResults.Order().SequenceEqual(new[]{200,409}),"atomic concurrent mutation version check"); response=service.Get("owner");

var careerPath=Path.Combine(output,"career"); var careerActor=new string('a',64); var otherActor=new string('b',64);
var career=new DiamondCareerService(careerPath,()=>now);
var creation=Json(new{op="create",requestId="career-create-001",name="검증선수",team="HH",position="CF",bats="L",throws="R",delivery="sidearm",archetype="speed",appearance=new DiamondCareerAppearance()});
var character=career.Post(creation,careerActor).Player!;
Check(character.Ratings.Speed==78&&character.TrainingPoints==8&&character.Version==1,"career archetype initial values");
Check(career.Post(creation,careerActor).Player!.Id==character.Id,"career create idempotence");
Check(career.Get(otherActor).Player==null,"career ownership isolation");
var train=Json(new{op="train",requestId="career-train-001",version=character.Version,skill="speed"});
character=career.Post(train,careerActor).Player!; Check(character.Ratings.Speed==80&&character.TrainingPoints==7,"training changes rating and spends one point");
Check(career.Post(train,careerActor).Player!.TrainingPoints==7,"training replay cannot double spend");
Error(()=>career.Post(Json(new{op="train",requestId="career-stale-001",version=1,skill="power"}),careerActor),409,"career stale version conflict");
var custom=career.GetRosterOverride(careerActor,2025)!;
Check(custom.Batter!.Id=="2025:"+character.Id&&custom.Batter.Profile!.GameRatings!.Speed==80,"custom IDs and current ratings exposed in roster override");
var rewardGame=Copy(initialGame); rewardGame.Id="reward-fixture-1";rewardGame.Complete=true;
rewardGame.PlayerStats[custom.Batter.Id]=new(){PlayerId=custom.Batter.Id,Name=character.Name,Team="HH",PA=5,AB=4,H=3,HR=1,RBI=3,BB=1};
career.ApplyGame(careerActor,rewardGame);var rewarded=career.Get(careerActor).Player!;
career.ApplyGame(careerActor,rewardGame);Check(career.Get(careerActor).Player!.Version==rewarded.Version&&rewarded.Games==1&&rewarded.Stats.H==3,"career reward stats and XP exactly once");
career=new DiamondCareerService(careerPath,()=>now);Check(career.Get(careerActor).Player!.Games==1,"career reward restart persistence");

var calls=0;
var integrated=new DiamondSeasonService(careerPath,Path.GetFullPath(args[0]),roster,()=>now,rng.NextDouble,career.GetRosterOverride,
    (actor,g)=>{if(++calls==1)throw new IOException("synthetic reward interruption");career.ApplyGame(actor,g);});
var joined=integrated.Post(Json(new{op="create",requestId="integrate-create",season=2025,team="HH",pace="full",seriesPerPair=1}),careerActor);
Check(joined.Save!.Teams.Single(x=>x.Code=="HH").Lineup.Contains(custom.Batter.Id),"career inserted into real team nine-person lineup");
try{integrated.Post(Json(new{op="sim-game",requestId="integrate-game01",version=joined.Save.Version}),careerActor);throw new InvalidDataException("outbox interruption expected");}catch(IOException){}
var recovered=new DiamondSeasonService(careerPath,Path.GetFullPath(args[0]),roster,()=>now,rng.NextDouble,career.GetRosterOverride,career.ApplyGame);
var resumed=recovered.Get(careerActor);
Check(resumed.Save!.Game!.Complete&&career.Get(careerActor).Player!.Games==2,"durable game survives failed reward callback and resumes delivery");
var beforeGames=career.Get(careerActor).Player!.Games;recovered.Get(careerActor);Check(career.Get(careerActor).Player!.Games==beforeGames,"outbox redelivery remains idempotent");
var originalCustomSpeed=resumed.Save.Teams.Single(x=>x.Code=="HH").Batters.Single(x=>x.Id==custom.Batter.Id).Profile!.GameRatings!.Speed;
character=career.Get(careerActor).Player!;
character=career.Post(Json(new{op="train",requestId="career-next-game-training",version=character.Version,skill="speed"}),careerActor).Player!;
Check(recovered.Get(careerActor).Save!.Teams.Single(x=>x.Code=="HH").Batters.Single(x=>x.Id==custom.Batter.Id).Profile!.GameRatings!.Speed==originalCustomSpeed,"training leaves saved current game snapshot unchanged");
var nextCustomGame=recovered.Post(Json(new{op="start-game",requestId="career-next-game-start",version=resumed.Save.Version}),careerActor);
Check(nextCustomGame.Save!.Teams.Single(x=>x.Code=="HH").Batters.Single(x=>x.Id==custom.Batter.Id).Profile!.GameRatings!.Speed==character.Ratings.Speed,"next game refreshes only custom ratings");
var atPlayer=recovered.Post(Json(new{op="sim-to-player",requestId="career-until-player01",version=nextCustomGame.Save.Version}),careerActor);
Check(!atPlayer.Save!.Game!.Complete&&atPlayer.Action!.Batter==custom.Batter.Id&&atPlayer.Action.Pitch==null,"simulate to career batter stops before own controllable PA");
var atPlayerAgain=recovered.Post(Json(new{op="sim-to-player",requestId="career-until-player02",version=atPlayer.Save.Version}),careerActor);
Check(atPlayerAgain.Save!.Game!.PlateAppearances==atPlayer.Save.Game.PlateAppearances,"already at own player does not skip turn");

// Career appearance and ratings share the same persisted, per-game refresh boundary.
var appearanceBefore=Copy(atPlayerAgain.Save.Appearances![custom.Batter.Id]);
character=career.Get(careerActor).Player!;var editedAppearance=Copy(character.Appearance);
editedAppearance.HeightCm=201;editedAppearance.BodyType="power";editedAppearance.HairStyle="flow";editedAppearance.GloveColor="#123456";
character=career.Post(Json(new{op="appearance",requestId="career-appearance01",version=character.Version,name="새이름",appearance=editedAppearance}),careerActor).Player!;
Check(character.Name=="새이름"&&character.Stats.Name=="새이름","rename keeps career aggregate identity synchronized");
var stillPlaying=recovered.Get(careerActor);
Check(JsonSerializer.Serialize(stillPlaying.Save!.Appearances![custom.Batter.Id],DiamondJson.Options)==JsonSerializer.Serialize(appearanceBefore,DiamondJson.Options),"live-game appearance stays pinned after edit and reload");
var afterEditGame=recovered.Post(Json(new{op="sim-game",requestId="appearance-game-end",version=stillPlaying.Save.Version}),careerActor);
var afterEditStart=recovered.Post(Json(new{op="start-game",requestId="appearance-next-start",version=afterEditGame.Save!.Version}),careerActor);
var refreshedCustom=afterEditStart.Save!.Teams.Single(x=>x.Code=="HH").Batters.Single(x=>x.Id==custom.Batter.Id);
Check(afterEditStart.Save.Appearances![custom.Batter.Id].GloveColor=="#123456"&&refreshedCustom.Profile!.HeightCm==201&&refreshedCustom.Name=="새이름","next game refreshes cosmetic, collider profile and player name together");
var beforeBenchedGames=career.Get(careerActor).Player!.Games;
var bench=afterEditStart.Save.Teams.Single(x=>x.Code=="HH").Batters.First(x=>!afterEditStart.Save.Game!.HomeLineup.Contains(x.Id)&&!afterEditStart.Save.Game.AwayLineup.Contains(x.Id));
var customSlot=(afterEditStart.Save.Game!.HomeTeam=="HH"?afterEditStart.Save.Game.HomeLineup:afterEditStart.Save.Game.AwayLineup).IndexOf(custom.Batter.Id);
var benched=recovered.Post(Json(new{op="substitute",requestId="bench-custom-before-play",version=afterEditStart.Save.Version,slot=customSlot,playerId=bench.Id}),careerActor);
var skippedBench=recovered.Post(Json(new{op="sim-to-player",requestId="skip-substituted-player",version=benched.Save!.Version}),careerActor);
Check(skippedBench.Save!.Game!.Complete&&!skippedBench.Save.Game.Participants.Contains(custom.Batter.Id)&&career.Get(careerActor).Player!.Games==beforeBenchedGames,"removed before actual play: sim-to-player finishes game without appearance reward");

// Field-only, pinch-running and unplayed bench appearances have distinct reward accounting.
var participationSave=Copy(rulesSave);var participationGame=Copy(initialGame);participationGame.PlayerStats.Clear();participationGame.Participants.Clear();
var participantTeam=participationSave.Teams.Single(x=>x.Code==participationGame.HomeTeam);
var defender=Copy(custom.Batter);defender.Team=participantTeam.Code;defender.Profile!.Position="CF";participantTeam.Batters.Add(defender);participationGame.HomeLineup[0]=defender.Id;
DiamondSeasonRules.RegisterParticipants(participationSave,participationGame);
Check(participationGame.Participants.Contains(defender.Id)&&participationGame.PlayerStats[defender.Id].PA==0,"actual pitch registers field-only participation before any PA");
var priorCareer=career.Get(careerActor).Player!;participationGame.Complete=true;participationGame.Id="field-only-reward";
career.ApplyGame(careerActor,participationGame);career.ApplyGame(careerActor,participationGame);
Check(career.Get(careerActor).Player!.Games==priorCareer.Games+1&&career.Get(careerActor).Player!.Stats.PA==priorCareer.Stats.PA,"field-only game counted once without inventing batting stats");
var runnerGame=Copy(participationGame);runnerGame.Id="legacy-pinch-runner";runnerGame.Participants.Clear();runnerGame.PlayerStats.Clear();
runnerGame.PlayerStats[defender.Id]=new(){PlayerId=defender.Id,R=1};career.ApplyGame(careerActor,runnerGame);
Check(career.Get(careerActor).Player!.Games==priorCareer.Games+2,"legacy pinch runner who scored receives appearance credit");
var notPlayed=Copy(runnerGame);notPlayed.Id="unplayed-bench";notPlayed.PlayerStats[defender.Id].R=0;career.ApplyGame(careerActor,notPlayed);
Check(career.Get(careerActor).Player!.Games==priorCareer.Games+2,"zero-stat nonparticipant never earns an appearance reward");

var pitcherCharacter=career.Post(Json(new{op="create",requestId="pitcher-career-create",name="커리어투수",team="HH",position="P",bats="L",throws="L",delivery="underhand",archetype="control",appearance=new DiamondCareerAppearance()}),otherActor).Player!;
var pitcherSeason=recovered.Post(Json(new{op="create",requestId="pitcher-season-create",season=2025,team="HH",pace="full",seriesPerPair=1}),otherActor);
var pitcherTurn=recovered.Post(Json(new{op="sim-to-player",requestId="pitcher-first-turn",version=pitcherSeason.Save!.Version}),otherActor);
Check(pitcherTurn.Save!.Game!.PlateAppearances==0&&pitcherTurn.Action!.Role=="pitcher"&&pitcherTurn.Action.Pitcher=="2025:"+pitcherCharacter.Id,"pitcher career stops immediately on own first defensive turn");
var pitcherPitch=recovered.Post(Json(new{op="pitch",requestId="pitcher-live-pitch",version=pitcherTurn.Save.Version,previousPitch=0,type="fastball",quality=.9,aim=new{x=0,y=0}}),otherActor);
Check(pitcherPitch.Save!.Game!.Participants.Contains("2025:"+pitcherCharacter.Id),"live pitch records actual pitching appearance");
var heldPitch=recovered.Post(Json(new{op="sim-to-player",requestId="pitcher-own-turn-noop",version=pitcherPitch.Save.Version}),otherActor);
Check(heldPitch.Action!.Pitch!.Id==pitcherPitch.Action!.Pitch!.Id&&!heldPitch.Action.Pitch.Resolved,"already on own pitcher turn preserves pending live pitch");
var defensiveHalf=recovered.Post(Json(new{op="sim-half",requestId="pitcher-complete-half",version=heldPitch.Save!.Version}),otherActor);
var nextDefensive=recovered.Post(Json(new{op="sim-to-player",requestId="pitcher-next-defense",version=defensiveHalf.Save!.Version}),otherActor);
Check(nextDefensive.Action!.Role=="pitcher"&&nextDefensive.Action.Pitcher=="2025:"+pitcherCharacter.Id&&nextDefensive.Action.Pitch==null,"pitcher career skips offense and returns to next defensive inning");
while(!nextDefensive.Save!.Complete)
    nextDefensive=recovered.Post(Json(new{op="sim-day",requestId=Guid.NewGuid().ToString("N"),version=nextDefensive.Save.Version}),otherActor);
var pitchingGames=career.Get(otherActor).Player!.Games;
Check(pitchingGames==18,"career pitching appearances reconcile an entire 18-game fixture season");
var pitchingNextYear=recovered.Post(Json(new{op="next-season",requestId="pitcher-next-season",version=nextDefensive.Save.Version}),otherActor);
var pitchingNewStart=recovered.Post(Json(new{op="sim-to-player",requestId="pitcher-next-season-turn",version=pitchingNextYear.Save!.Version}),otherActor);
Check(pitchingNewStart.Save!.SeasonNumber==2&&pitchingNewStart.Action!.Pitcher=="2025:"+pitcherCharacter.Id&&career.Get(otherActor).Player!.Games==pitchingGames,"next season preserves career identity and earned progression");

var lateActor=new string('c',64);
var lateSeason=recovered.Post(Json(new{op="create",requestId="late-season-create",season=2025,team="HH",pace="full",seriesPerPair=1}),lateActor);
lateSeason=recovered.Post(Json(new{op="start-game",requestId="late-game-start",version=lateSeason.Save!.Version}),lateActor);
career.Post(Json(new{op="create",requestId="late-career-create",name="늦게합류",team="HH",position="DH",bats="R",throws="R",delivery="overhand",archetype="balanced",appearance=new DiamondCareerAppearance()}),lateActor);
try{recovered.Post(Json(new{op="sim-to-player",requestId="late-player-turn",version=lateSeason.Save!.Version}),lateActor);throw new InvalidDataException("late career should wait until next game");}
catch(DiamondInputError e){Check(e.Message.Contains("다음 경기"),"career created during game explains next-game eligibility");}
lateSeason=recovered.Post(Json(new{op="sim-game",requestId="late-game-complete",version=lateSeason.Save!.Version}),lateActor);
lateSeason=recovered.Post(Json(new{op="sim-to-player",requestId="late-player-next-game",version=lateSeason.Save!.Version}),lateActor);
Check(lateSeason.Action!.Batter.EndsWith(":"+career.Get(lateActor).Player!.Id)&&career.Get(lateActor).Player!.Games==0,"late-created batter joins next game and never receives previous-game rewards");

var cappedActor=new string('d',64);
var capped=career.Post(Json(new{op="create",requestId="cap-career-create",name="훈련검증",team="HH",position="CF",bats="R",throws="R",delivery="overhand",archetype="speed",appearance=new DiamondCareerAppearance()}),cappedActor).Player!;
for(var i=0;i<4;i++)capped=career.Post(Json(new{op="train",requestId="cap-training-000"+i,version=capped.Version,skill="speed"}),cappedActor).Player!;
Check(capped.Ratings.Speed==86&&capped.TrainingPoints==1,"training above 80 costs two points each");
Error(()=>career.Post(Json(new{op="train",requestId="cap-insufficient-points",version=capped.Version,skill="speed"}),cappedActor),400,"insufficient training points rejected");
Check(career.Get(cappedActor).Player!.Version==capped.Version,"rejected training leaves state untouched");
for(var i=0;i<10;i++)
{
    var award=new DiamondSeasonGame{Id="cap-completed-game-"+i,Complete=true};var id="2025:"+capped.Id;
    award.PlayerStats[id]=new(){PlayerId=id,PA=5,AB=5,H=2};career.ApplyGame(cappedActor,award);
}
capped=career.Get(cappedActor).Player!;
while(capped.Ratings.Speed<95)capped=career.Post(Json(new{op="train",requestId=Guid.NewGuid().ToString("N"),version=capped.Version,skill="speed"}),cappedActor).Player!;
Check(capped.Ratings.Speed==95,"training clips final increment at hard cap 95");
Error(()=>career.Post(Json(new{op="train",requestId="cap-already-maximal",version=capped.Version,skill="speed"}),cappedActor),400,"maximal rating cannot consume more points");
Error(()=>career.Post(Json(new{op="train",requestId="batter-cannot-train-velocity",version=capped.Version,skill="velocity"}),cappedActor),400,"batter cannot spend points on unused pitching skills");
Check(career.Get(cappedActor).Player!.TrainingPoints==capped.TrainingPoints&&career.Get(cappedActor).Player!.Version==capped.Version,"wrong-role batter training preserves points and version");
pitcherCharacter=career.Get(otherActor).Player!;
Error(()=>career.Post(Json(new{op="train",requestId="pitcher-cannot-train-power",version=pitcherCharacter.Version,skill="power"}),otherActor),400,"pitcher cannot spend points on unused batting skills");
var trainedPitcher=career.Post(Json(new{op="train",requestId="pitcher-train-control",version=pitcherCharacter.Version,skill="CONTROL"}),otherActor).Player!;
Check(trainedPitcher.Ratings.Control==pitcherCharacter.Ratings.Control+2&&trainedPitcher.TrainingPoints==pitcherCharacter.TrainingPoints-1,"valid pitcher training uses case-insensitive skill and correct cost");

// Set up precise high-pitch-count / contact fixtures in the test-only state DB, then use public commands.
void SeedState(DiamondSeasonService target,string actor,Action<DiamondSeasonSave> change)
{
    using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=target.DatabasePath}.ToString());c.Open();using var tx=c.BeginTransaction();
    using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT State FROM DiamondSeasons WHERE Owner=$actor";cmd.Parameters.AddWithValue("$actor",actor);
    var s=JsonSerializer.Deserialize<DiamondSeasonSave>((string)cmd.ExecuteScalar()!,DiamondJson.Options)!;change(s);
    cmd.CommandText="UPDATE DiamondSeasons SET State=$state WHERE Owner=$actor";cmd.Parameters.AddWithValue("$state",JsonSerializer.Serialize(s,DiamondJson.Options));cmd.ExecuteNonQuery();tx.Commit();
}
double FatiguedVelocity(int stamina)
{
    var pitcher=Copy(roster.Get(2025).Pitchers.First(x=>x.Team=="HH"));pitcher.Id=$"2025:career_stamina{stamina}";pitcher.PlayerId=$"career_stamina{stamina}";pitcher.Profile!.GameRatings=new(){Stamina=stamina};
    var svc=new DiamondSeasonService(Path.Combine(output,"stamina"+stamina),Path.GetFullPath(args[0]),roster,()=>now,()=>.5,(actor,year)=>new(null,pitcher,pitcher.PlayerId));
    var state=svc.Post(Json(new{op="create",requestId="stamina-create-01",season=2025,team="HH",pace="full",seriesPerPair=1}),"stamina");
    state=svc.Post(Json(new{op="start-game",requestId="stamina-start-001",version=state.Save!.Version}),"stamina");
    SeedState(svc,"stamina",s=>{s.Game!.PlayerStats[pitcher.Id]=new(){PlayerId=pitcher.Id,PitchCount=80};s.Game.Duel.PitchCount=80;});
    state=svc.Post(Json(new{op="pitch",requestId="stamina-pitch-001",version=state.Save!.Version,previousPitch=80,type="fastball",quality=1,aim=new{x=0,y=0}}),"stamina");
    Check(state.Action!.Roster!.Pitcher.Arsenal[0].Velocity==pitcher.Arsenal[0].Velocity,"fatigue never changes source arsenal speed");
    return state.Action.Pitch!.Velocity;
}
Check(FatiguedVelocity(95)>FatiguedVelocity(25)+2,"stamina rating changes actual late-game pitch speed");
string FieldingOutcome(int fielding)
{
    var svc=new DiamondSeasonService(Path.Combine(output,"fielding"+fielding),Path.GetFullPath(args[0]),roster,()=>now,()=>.6);
    var state=svc.Post(Json(new{op="create",requestId="fielding-create01",season=2025,team="WO",pace="full",seriesPerPair=1}),"fielding");
    state=svc.Post(Json(new{op="start-game",requestId="fielding-start001",version=state.Save!.Version}),"fielding");
    SeedState(svc,"fielding",s=>
    {
        var g=s.Game!;foreach(var b in s.Teams.Single(x=>x.Code==g.HomeTeam).Batters)b.Profile!.GameRatings=new(){Fielding=fielding};
        g.Duel.PitchCount=1;g.Duel.Pitch=new(){Id=1,Type="fastball",Velocity=145,ReleaseAt=now-400,FlightMs=400,Target=new(0,0)};
    });
    state=svc.Post(Json(new{op="swing",requestId="fielding-swing001",version=state.Save!.Version,pitchId=1,inputAt=now-95,aim=new{x=0,y=.45}}),"fielding");
    return state.Action!.Pitch!.Reaction!.Outcome;
}
Check(FieldingOutcome(95)=="OUT"&&FieldingOutcome(25)=="1B","fielding rating changes borderline live contact outcome");

// Public DTO strips hidden duel owner state and both APIs use the same one-year cookie.
var webBuilder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],EnvironmentName="Development"});
webBuilder.Logging.ClearProviders();webBuilder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,0));
webBuilder.Services.AddSingleton(integrated);webBuilder.Services.AddSingleton(career);
await using(var app=webBuilder.Build())
{
    app.MapDiamondSeason();app.MapDiamondCareer();await app.StartAsync();
    var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client=new HttpClient{BaseAddress=new Uri(address)};
    using var first=await client.GetAsync("/api/diamond/season");
    Check(first.IsSuccessStatusCode&&first.Headers.GetValues("Set-Cookie").Any(x=>x.Contains("diamond_owner=")&&x.Contains("httponly",StringComparison.OrdinalIgnoreCase)&&x.Contains("max-age=31536000")),"season endpoint issues durable HttpOnly owner cookie");
    using var body=JsonDocument.Parse(await first.Content.ReadAsStringAsync());
    Check(body.RootElement.GetProperty("save").ValueKind==JsonValueKind.Null&&body.RootElement.GetProperty("serverSentAt").GetInt64()>=body.RootElement.GetProperty("serverReceivedAt").GetInt64(),"HTTP clock envelope and empty save");
    using var conditional=new HttpRequestMessage(HttpMethod.Get,"/api/diamond/season");conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
    using var unchanged=await client.SendAsync(conditional);Check(unchanged.StatusCode==HttpStatusCode.NotModified&&string.IsNullOrEmpty(await unchanged.Content.ReadAsStringAsync()),"ETag skips unchanged poll body after authoritative tick");
    using var bad=new HttpRequestMessage(HttpMethod.Post,"/api/diamond/season"){Content=JsonContent.Create(new{op="tick"})};bad.Headers.Add("Origin","https://unrelated.example");
    using var denied=await client.SendAsync(bad);Check(denied.StatusCode==HttpStatusCode.Forbidden,"cross-origin mutation denied");
    using var invalid=await client.PostAsync("/api/diamond/season",new StringContent("{bad"));Check(invalid.StatusCode==HttpStatusCode.BadRequest,"malformed JSON 400");
    using var oversized=await client.PostAsync("/api/diamond/season",new StringContent(new string('x',20000)));Check(oversized.StatusCode==HttpStatusCode.RequestEntityTooLarge,"body size bounded");
    using var careerCreated=await client.PostAsJsonAsync("/api/diamond/career",new{op="create",requestId="http-career-create",name="HTTP선수",team="HH",position="CF",bats="R",throws="R",delivery="overhand",archetype="balanced",appearance=new DiamondCareerAppearance()});
    Check(careerCreated.IsSuccessStatusCode,"career HTTP create succeeds in shared owner cookie");
    var httpCareer=JsonSerializer.Deserialize<DiamondCareerResponse>(await careerCreated.Content.ReadAsStringAsync(),DiamondJson.Options)!;
    using var wrongSkill=await client.PostAsJsonAsync("/api/diamond/career",new{op="train",requestId="http-role-training",version=httpCareer.Player!.Version,skill="velocity"});
    using var wrongSkillBody=JsonDocument.Parse(await wrongSkill.Content.ReadAsStringAsync());
    Check(wrongSkill.StatusCode==HttpStatusCode.BadRequest&&wrongSkillBody.RootElement.GetProperty("code").GetString()=="CAREER_INPUT"&&wrongSkillBody.RootElement.GetProperty("error").GetString()!.Contains("타자"),"career wrong-role training returns actionable HTTP 400 code and Korean reason");
    using var careerAgain=await client.GetAsync("/api/diamond/career");
    var httpCareerAgain=JsonSerializer.Deserialize<DiamondCareerResponse>(await careerAgain.Content.ReadAsStringAsync(),DiamondJson.Options)!;
    Check(httpCareerAgain.Player!.Id==httpCareer.Player.Id&&httpCareerAgain.Player.TrainingPoints==8&&httpCareerAgain.Player.Version==1,"HTTP owner persistence and rejected-training point preservation");
    await app.StopAsync();
}
checks+=CreationLimits.Run(Path.GetFullPath(args[0]),output,source);
Console.WriteLine($"PASS concurrent inputs/career training/reward outbox/HTTP contract/durable creation limits ({checks} checks)");
var seasonTimer=Stopwatch.StartNew();
while(!response.Save!.Complete)
{
    Post("sim-day");
    if((response.Save.Day-1)%18==0)Console.WriteLine($"Season progress {response.Save.Day-1}/{response.Save.TotalDays} days, {seasonTimer.Elapsed.TotalSeconds:F1}s");
}
Check(response.Save.Schedule.All(x=>x.Complete)&&response.Save.Standings.All(x=>x.Played==response.Save.TotalDays),"every team plays complete season");
Check(response.Save.PlayerStats.Values.Sum(x=>x.R)==response.Save.Schedule.Sum(x=>x.HomeRuns+x.AwayRuns),"season runs reconcile box scores");
Check(response.Save.PlayerStats.Values.Sum(x=>x.RunsAllowed)==response.Save.PlayerStats.Values.Sum(x=>x.R),"pitcher responsibility reconciles all runs");
Check(response.Save.PlayerStats.Values.All(x=>x.PA==x.AB+x.BB+x.HBP+x.SF&&x.H>=x.HR+x.Double+x.Triple&&x.H<=x.AB),"season batting accounting identities reconcile");
Check(completed.Count==response.Save.TotalDays,"every selected-team game rewarded once");
var seasonNumber=response.Save.SeasonNumber; Post("next-season");
Check(response.Save!.SeasonNumber==seasonNumber+1&&response.Save.Day==1&&response.Save.PlayerStats.Count==0&&response.Save.PreviousSeasons.Count==1,"next season resets totals and archives finish");
var offline=new DiamondSeasonService(Path.Combine(output,"state"),Path.GetFullPath(args[0]),new DiamondRosterService(Path.Combine(output,"missing-source.db")),()=>now,rng.NextDouble);
Check(offline.Get("owner").Save!.Id==response.Save.Id&&offline.Post(Json(request),"owner").Save!.Id==response.Save.Id,"saved season and create retry work when source warehouse is offline");
Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))==hash,"source warehouse DB never modified");
Console.WriteLine($"PASS complete {response.Save.TotalDays}-game season per team in {seasonTimer.Elapsed.TotalSeconds:F2}s ({checks} checks)");
File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{checks,days=response.Save.TotalDays,completed=completed.Count,seconds=seasonTimer.Elapsed.TotalSeconds},DiamondJson.Options));
Console.WriteLine(output);
