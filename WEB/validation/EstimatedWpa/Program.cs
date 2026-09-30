using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;
using NaverRelay.Application.Importing;

try
{
int checks=0;
void Check(bool condition,string description){if(!condition)throw new Exception(description);checks++;}
string? Option(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
var fixture=new EstimatedWpaModel{Year=2018,Cells=new Dictionary<string,double>{
    ["9:A:2:1:-1"]=0.097999997437000275,["9:A:2:3:-2"]=0.090400002896785736,
    ["9:A:1:1:-1"]=0.18,["9:A:1:4:0"]=0.65,["9:A:2:4:0"]=0.6,["9:H:0:0:0"]=0.5,["8:A:0:0:0"]=0.55}};
bool Expect(int inning,bool home,int outs,int bases,int lead,out double value)=>fixture.TryHomeExpectation(inning,home,outs,bases,lead,out value);
Check(Expect(9,true,0,0,1,out var value)&&value==1,"walkoff probability");
Check(Expect(9,true,3,0,-1,out value)&&value==0,"home loss after last out");
Check(Expect(9,false,3,0,1,out value)&&value==1,"home skips bottom ninth");
Check(Expect(12,true,2,1,-1,out value)&&value==0.097999997437000275,"extra innings use >=9 table");
Check(Expect(8,false,3,7,0,out value)&&value==0.55,"half-inning transition clears bases/outs");
Check(Expect(9,true,3,7,0,out value)&&value==0.5,"tie continues to >=9 top, not a draw terminal");
Check(!Expect(8,true,1,0,11,out _),"unsupported score lead is not clamped");
Check(!Expect(8,false,1,0,-11,out _),"unsupported score deficit is not clamped");
var walkoff=new NormalizedGame{GameId="test",SeasonYear=2018,GameDate="2018-06-30",RoundCode="kbo_r",StatusCode="RESULT",
    HomeTeam=new(){FinalScore=2},AwayTeam=new(){FinalScore=1}};
var pa=new PlateAppearance{PlateAppearanceId="pa",RelayGroupId="r",IsOfficialPlateAppearance=true,SequenceNumber=1,
    Inning=9,BattingSide=TeamSide.Home,StartEventId="start",ResultEventId="result",
    StateBefore=new(){HomeScore=0,AwayScore=1,Outs=2,FirstBaseSlot=1},StateAfter=new(){HomeScore=2,AwayScore=1,Outs=2}};
walkoff.PlateAppearances.Add(pa);walkoff.Events.Add(new(){EventId="start",RawType=8});
var first=EstimatedWpa.Calculate(walkoff,fixture).Single();
Check(Math.Abs(first.Wpa-90.20000025629997)<1e-10,"FG 9th two outs down one runner on first");
pa.WpaByPlate=0;Check(EstimatedWpa.Calculate(walkoff,fixture).Count==0,"default missing-only caller remains explicit");
Check(EstimatedWpa.Calculate(walkoff,fixture,true).Single().Wpa==first.Wpa,"replace-existing includes collected zero WPA");
pa.WpaByPlate=87.798;Check(EstimatedWpa.Calculate(walkoff,fixture,true).Single().Wpa==first.Wpa,"replace homemade v1");pa.WpaByPlate=null;
walkoff.HomeTeam.FinalScore=3;walkoff.AwayTeam.FinalScore=2;pa.StateBefore.AwayScore=2;pa.StateBefore.SecondBaseSlot=2;
pa.StateAfter.HomeScore=3;pa.StateAfter.AwayScore=2;
var second=EstimatedWpa.Calculate(walkoff,fixture).Single();
Check(Math.Abs(second.Wpa-90.95999971032143)<1e-10 && second.Wpa>first.Wpa,"FG two-run deficit on first/second has larger walkoff WPA");
walkoff.RoundCode="kbo_ks";Check(EstimatedWpa.Calculate(walkoff,fixture).Count==1,"postseason also included");
pa.StateBefore.Outs=null;Check(EstimatedWpa.Calculate(walkoff,fixture).Count==0,"incomplete state excluded");pa.StateBefore.Outs=2;
pa.Inning=7;Check(EstimatedWpa.Calculate(walkoff,fixture).Count==0,"shortened game excluded");pa.Inning=9;
walkoff.SeasonYear=2024;Check(EstimatedWpa.Calculate(walkoff,fixture,true).Count==0,"outside requested years untouched");walkoff.SeasonYear=2018;
walkoff.HomeTeam.FinalScore=2;pa.StateAfter.HomeScore=2;pa.StateAfter.Outs=3;
Check(EstimatedWpa.Calculate(walkoff,fixture).Count==0,"draw terminal is unsupported, never guessed as 0.5");
// A later runner-only event is not part of the preceding official PA.
walkoff.HomeTeam.FinalScore=0;walkoff.AwayTeam.FinalScore=1;
pa.StateBefore=new(){HomeScore=0,AwayScore=1,Outs=1,FirstBaseSlot=1};
pa.StateAfter=new(){HomeScore=0,AwayScore=1,Outs=2,FirstBaseSlot=1};
walkoff.RelayGroups.Add(new(){RelayGroupId="terminal",ChronologicalIndex=100,Inning=9,BattingSide=TeamSide.Home,
    StateAfter=new(){HomeScore=0,AwayScore=1,Outs=3}});
Check(Math.Abs(EstimatedWpa.Calculate(walkoff,fixture).Single().Wpa-((0.097999997437000275-0.18)*100))<1e-10,"game-ending caught stealing is not assigned to previous PA");
walkoff.HomeTeam.FinalScore=1;walkoff.AwayTeam.FinalScore=0;
pa.StateBefore=new(){HomeScore=0,AwayScore=0,Outs=1,ThirdBaseSlot=3};
pa.StateAfter=new(){HomeScore=0,AwayScore=0,Outs=2,ThirdBaseSlot=3};
walkoff.RelayGroups[0].StateAfter=new(){HomeScore=1,AwayScore=0,Outs=2};
Check(Math.Abs(EstimatedWpa.Calculate(walkoff,fixture).Single().Wpa+5)<1e-10,"later walkoff wild pitch is not assigned to previous PA");
Console.WriteLine($"PASS {checks} formula/state checks");
if(args.Contains("--check-table"))
{
    FanGraphsWeTable.Validate();
    Check(FanGraphsWeTable.Cells.Count==9072,"complete exact state domain");
    Check(Math.Abs(FanGraphsWeTable.Cells["9:A:2:1:-1"]-0.097999997437000275)<1e-15,"bundled first FG example");
    Check(Math.Abs(FanGraphsWeTable.Cells["9:A:2:3:-2"]-0.090400002896785736)<1e-15,"bundled second FG example");
    Console.WriteLine($"PASS full table: {FanGraphsWeTable.Cells.Count} states, SHA256={FanGraphsWeTable.Sha256}");
}
var path=Option("--db");if(path is null)return;
var service=new DatabaseCacheService(path);
if(args.Contains("--train"))Console.WriteLine("Pinned table registrations: "+await service.BuildEstimatedWpaModelsAsync());
if(args.Contains("--backfill"))
{
    var report=await service.BackfillEstimatedWpaAsync(Option("--year") is string y?int.Parse(y):null,new Progress<string>(Console.WriteLine));
    var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);
    if(Option("--report") is string output)await File.WriteAllTextAsync(output,json);
    if(report.Errors.Count>0)Environment.ExitCode=2;
}
if(Option("--preview") is string source)
{
    var game=RelayParser.ParseFile(source);var model=new EstimatedWpaModel{Year=game.SeasonYear!.Value};
    var values=EstimatedWpa.Calculate(game,model,true);
    var restored=await service.LoadGameForEstimatedWpaAsync(game.GameId)??throw new Exception("DB game missing");
    var actual=EstimatedWpa.Calculate(restored,model,true).ToDictionary(v=>v.PlateAppearanceId);
    Check(values.All(v=>actual.TryGetValue(v.PlateAppearanceId,out var a)&&Math.Abs(a.Wpa-v.Wpa)<1e-9),"DB reconstruction equals source JSON");
    Console.WriteLine(JsonSerializer.Serialize(new{game.GameId,Plays=values.OrderByDescending(v=>Math.Abs(v.Wpa)).Take(5)},new JsonSerializerOptions{WriteIndented=true}));
}
if(Option("--reimport") is string reimport)
{
    var game=RelayParser.ParseFile(reimport);
    var expected=EstimatedWpa.Calculate(game,new EstimatedWpaModel{Year=game.SeasonYear!.Value},true).ToDictionary(v=>v.PlateAppearanceId);
    await service.SaveGameAndSourceAsync(game,new InputDocument{Id=reimport,Kind=InputDocumentKind.JsonFile,ContainerPath=reimport});
    var restored=await service.LoadGameForEstimatedWpaAsync(game.GameId)??throw new Exception("Reimport lost the game");
    Check(restored.PlateAppearances.Where(p=>p.IsOfficialPlateAppearance).All(p=>expected.TryGetValue(p.PlateAppearanceId,out var v)?p.WpaByPlate==v.Wpa:p.WpaByPlate is null),"reimport applies pinned table, including unresolved NULLs");
    await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync();
    await using var cmd=c.CreateCommand();cmd.CommandText="SELECT Version FROM EstimatedWpaBackfillGames WHERE GameId=$id";cmd.Parameters.AddWithValue("$id",game.GameId);
    Check((string?)await cmd.ExecuteScalarAsync()==EstimatedWpaModel.Version,"reimport retains converted display marker");
    cmd.CommandText="SELECT COUNT(*) FROM WpaRevisionHistory WHERE GameId=$id AND Reason='before-reimport-backup'";
    Check(Convert.ToInt32(await cmd.ExecuteScalarAsync())>0,"reimport preserves previous DB WPA in history");
}
Console.WriteLine($"PASS {checks} checks");

}
catch(Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode=1;
}
