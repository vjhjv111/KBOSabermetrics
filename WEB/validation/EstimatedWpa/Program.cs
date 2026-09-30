using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

int checks=0;
void Check(bool condition,string description){if(!condition)throw new Exception(description);checks++;}
var fixture=new EstimatedWpaModel{Year=2018,TrainingGames=100,Samples=Enumerable.Repeat(100,24).ToArray(),
    Runs=Enumerable.Range(0,24).Select(_=>new[]{0.6,0.3,0.1}).ToArray()};
Check(fixture.HomeExpectation(9,true,0,0,1)==1,"walk off");
Check(fixture.HomeExpectation(9,true,3,0,-1)==0,"home loses after last out");
Check(fixture.HomeExpectation(9,false,3,0,1)==1,"no bottom ninth with home lead");
Check(fixture.HomeExpectation(12,true,3,0,0)==0.5,"draw half credit");
Check(fixture.HomeExpectation(9,true,3,0,0,9)==0.5,"2021 nine-inning limit");
var low=fixture.HomeExpectation(8,false,1,3,-1);var high=fixture.HomeExpectation(8,false,1,3,1);
Check(low<high && low>=0 && high<=1,"score monotonicity and probability range");
Check(Math.Abs(fixture.HomeExpectation(1,false,0,0,0)-0.5)<1e-10,"symmetric teams begin at 0.5");
var walkoff=new NormalizedGame{GameId="test",SeasonYear=2018,GameDate="2018-06-30",RoundCode="kbo_r",StatusCode="RESULT",
    HomeTeam=new(){FinalScore=2},AwayTeam=new(){FinalScore=1}};
var pa=new PlateAppearance{PlateAppearanceId="pa",RelayGroupId="r",IsOfficialPlateAppearance=true,SequenceNumber=1,
    Inning=9,BattingSide=TeamSide.Home,StartEventId="start",ResultEventId="result",
    StateBefore=new(){HomeScore=0,AwayScore=1,Outs=1,FirstBaseSlot=1},StateAfter=new(){HomeScore=2,AwayScore=1,Outs=1}};
walkoff.PlateAppearances.Add(pa);walkoff.Events.Add(new(){EventId="start",RawType=8});
var calculated=EstimatedWpa.Calculate(walkoff,fixture).Single();
Check(calculated.HomeAfter==100 && calculated.Wpa>0 && Math.Abs(calculated.Wpa-(100-calculated.HomeBefore))<1e-9,"WPA percentage point scale");
pa.WpaByPlate=0;Check(EstimatedWpa.Calculate(walkoff,fixture).Count==0,"preserve original zero WPA");pa.WpaByPlate=null;
pa.StateBefore.Outs=null;Check(EstimatedWpa.Calculate(walkoff,fixture).Count==0,"missing state excluded");pa.StateBefore.Outs=1;
pa.StateAfter.Outs=0;Check(EstimatedWpa.Calculate(walkoff,fixture).Count==0,"invalid outs excluded");pa.StateAfter.Outs=1;
pa.Inning=7;Check(EstimatedWpa.Calculate(walkoff,fixture).Count==0,"shortened game excluded");pa.Inning=9;
walkoff.SeasonYear=2024;Check(EstimatedWpa.Calculate(walkoff,fixture).Count==0,"outside 2016-2023 excluded");walkoff.SeasonYear=2018;
Console.WriteLine($"PASS {checks} mathematical/state checks");
string? Option(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
var path=Option("--db");if(path is null)return;
var service=new DatabaseCacheService(path);
if(args.Contains("--train"))Console.WriteLine("Models added: "+await service.BuildEstimatedWpaModelsAsync());
if(Option("--preview") is string source)
{
    var game=RelayParser.ParseFile(source);
    await using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly}.ToString());await db.OpenAsync();
    await using var cmd=db.CreateCommand();cmd.CommandText="SELECT Json FROM EstimatedWpaModels WHERE SeasonYear=$year";cmd.Parameters.AddWithValue("$year",game.SeasonYear);
    var model=JsonSerializer.Deserialize<EstimatedWpaModel>((string)(await cmd.ExecuteScalarAsync())!)!;
    var values=EstimatedWpa.Calculate(game,model);
    if(args.Contains("--compare-db"))
    {
        var restored=await service.LoadGameForEstimatedWpaAsync(game.GameId)??throw new Exception("DB game missing");
        foreach(var p in restored.PlateAppearances)p.WpaByPlate=null;
        var expected=values.ToDictionary(v=>v.PlateAppearanceId);
        var actual=EstimatedWpa.Calculate(restored,model).Where(v=>expected.ContainsKey(v.PlateAppearanceId)).ToArray();
        Check(actual.Length==values.Count,"DB-only coverage equals source JSON");
        Check(actual.All(v=>Math.Abs(v.Wpa-expected[v.PlateAppearanceId].Wpa)<1e-9),"DB-only WPA equals source JSON");
        Console.WriteLine($"DB/source parity: {actual.Length} plate appearances");
    }
    if(args.Contains("--audit"))
    {
        foreach(var p in game.PlateAppearances.Where(p=>p.IsOfficialPlateAppearance).TakeLast(6))
        {
            cmd.CommandText="SELECT ResultText,BeforeHomeScore,BeforeAwayScore,BeforeOuts,AfterHomeScore,AfterAwayScore,AfterOuts FROM PlateAppearances WHERE PlateAppearanceId=$id";
            cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$id",p.PlateAppearanceId);
            using var rd=cmd.ExecuteReader();object[]? original=null;if(rd.Read()){original=new object[rd.FieldCount];rd.GetValues(original);}
            Console.WriteLine(JsonSerializer.Serialize(new{p.PlateAppearanceId,p.SequenceNumber,Calculated=values.Any(v=>v.PlateAppearanceId==p.PlateAppearanceId),p.ResultText,
                Before=p.StateBefore,After=p.StateAfter,Db=original}));
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new{game.GameId,Year=game.SeasonYear,Official=game.PlateAppearances.Count(p=>p.IsOfficialPlateAppearance),Calculated=values.Count,
        Plays=values.OrderByDescending(v=>Math.Abs(v.Wpa)).Take(5).Select(v=>new{v,Text=game.PlateAppearances.Single(p=>p.PlateAppearanceId==v.PlateAppearanceId).ResultText})},new JsonSerializerOptions{WriteIndented=true}));
}
if(args.Contains("--backfill"))
{
    var report=await service.BackfillEstimatedWpaAsync(Option("--year") is string y?int.Parse(y):null,new Progress<string>(Console.WriteLine));
    var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);
    if(Option("--report") is string output)await File.WriteAllTextAsync(output,json);
    if(report.Errors.Count>0)Environment.ExitCode=2;
}
