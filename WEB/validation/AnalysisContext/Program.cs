using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverSabermetrics.Web;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
static double Number(Dictionary<string,object?> row,string key)=>Convert.ToDouble(row[key],CultureInfo.InvariantCulture);
static AnalysisTable Table(AnalysisResult r,string key)=>r.Tables.Single(t=>t.Key==key);

var temp=Path.Combine(Path.GetTempPath(),"analysis-context-"+Guid.NewGuid().ToString("N")+".db");
var cache=new DatabaseCacheService(temp);
await cache.InitializeAsync();
await using(var c=new SqliteConnection($"Data Source={temp}"))
{
    await c.OpenAsync();
    // Populate the production schema's required fields, then override only the fixture facts.
    async Task Insert(string table,Dictionary<string,object?> facts)
    {
        await using var schema=c.CreateCommand();schema.CommandText=$"PRAGMA table_info({table})";
        var values=new Dictionary<string,object?>();
        await using(var reader=await schema.ExecuteReaderAsync()) while(await reader.ReadAsync())
            if(reader.GetInt32(3)==1 && reader.IsDBNull(4)) values[reader.GetString(1)]=reader.GetString(2)=="TEXT"?"":0;
        foreach(var pair in facts) values[pair.Key]=pair.Value;
        await using var cmd=c.CreateCommand();
        cmd.CommandText=$"INSERT INTO {table} ({string.Join(',',values.Keys)}) VALUES ({string.Join(',',values.Keys.Select((_,i)=>"$v"+i))})";
        var i=0;foreach(var value in values.Values)cmd.Parameters.AddWithValue("$v"+i++,value??DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
    foreach(var game in new[]{"complete","walkoff","broken"})
        await Insert("Games",new(){["GameId"]=game,["GameDate"]="2026-05-01",["SeasonYear"]=2026,["RoundCode"]="kbo_r",["AwayTeamCode"]="HT",["HomeTeamCode"]="SK"});
    async Task Group(string game,string id,int order,int beforeOuts,int afterOuts,int beforeScore,int afterScore,int mask,bool pa)
    {
        await Insert("RelayGroups",new(){["GameId"]=game,["RelayGroupId"]=id,["ChronologicalIndex"]=order,["GroupType"]=pa?2:3,["PlateAppearanceId"]=pa?id+":pa":null,
            ["Inning"]=1,["BattingSide"]=0,["BattingTeamCode"]="HT",["BeforeOuts"]=beforeOuts,["AfterOuts"]=afterOuts,["BeforeHomeScore"]=0,["AfterHomeScore"]=0,
            ["BeforeAwayScore"]=beforeScore,["AfterAwayScore"]=afterScore,["BeforeFirstRunnerPcode"]=(mask&1)>0?"runner":null,["BeforeSecondRunnerPcode"]=(mask&2)>0?"runner":null,["BeforeThirdRunnerPcode"]=(mask&4)>0?"runner":null});
        if(pa) await Insert("PlateAppearances",new(){["GameId"]=game,["RelayGroupId"]=id,["PlateAppearanceId"]=id+":pa",["SequenceNumber"]=order,["BatterPcode"]="batter",["BatterName"]="타자",["PitcherPcode"]="pitcher",["PitcherName"]="투수",["Inning"]=1,["BattingSide"]=0,["ResultText"]="타자 : 안타",["ResultType"]=1,["ResultEventId"]=id+":result"});
    }
    await Group("complete","a",1,0,0,0,0,0,true);
    await Group("complete","b",2,0,0,0,0,1,false); // A standalone steal contributes a state observation.
    await Group("complete","c",3,0,0,0,1,2,false); // Run scores outside any plate appearance.
    await Group("complete","d",4,0,3,1,1,0,true);
    await Insert("RunnerEvents",new(){["GameId"]="complete",["RunnerEventId"]="steal",["RelayGroupId"]="b",["Reason"]=2,["FromBase"]=1,["ToBase"]=2,["WasParsed"]=1,["RunnerPcode"]="runner"});
    await Insert("RunnerEvents",new(){["GameId"]="complete",["RunnerEventId"]="run",["RelayGroupId"]="c",["Reason"]=4,["FromBase"]=2,["ToBase"]=4,["IsRun"]=1,["WasParsed"]=1,["RunnerPcode"]="runner"});
    await Group("walkoff","w",1,0,1,0,10,0,true);
    await Group("broken","x",1,0,1,0,0,0,true);
    await Group("broken","y",2,2,3,0,0,0,true); // Unexplained out => reject complete half.
    await Insert("NormalizedEvents",new(){["GameId"]="complete",["EventId"]="a:result",["RelayGroupId"]="a",["RawText"]="타자 : 안타",["EventType"]=6});
    await Insert("Pitches",new(){["GameId"]="complete",["PitchEventId"]="pitch1",["RelayGroupId"]="a",["PlateAppearanceId"]="a:pa",["ActualPitchIndex"]=1,["PitchType"]="직구",["PitchResult"]=5,["BatterPcode"]="batter",["PitcherPcode"]="pitcher",
        ["X0"]=1.0,["Y0"]=50.0,["Z0"]=6.0,["Vx0"]=-2.0,["Vy0"]=-125.0,["Vz0"]=-5.0,["Ax"]=0.0,["Ay"]=0.0,["Az"]=0.0,
        ["TimeToPlateSeconds"]=0.4,["CrossPlateY"]=0.0,["CalculatedCrossPlateX"]=0.2,["CalculatedCrossPlateZ"]=4.0,["HasPtsTracking"]=1});
    const string officialGame="20260501HTSK02026";
    await Insert("Games",new(){["GameId"]=officialGame,["GameDate"]="2026-05-01",["SeasonYear"]=2026,["RoundCode"]="kbo_r",["AwayTeamCode"]="HT",["HomeTeamCode"]="SK"});
    await Insert("PlateAppearances",new(){["GameId"]=officialGame,["RelayGroupId"]="official-relay",["PlateAppearanceId"]="official-pa",["SequenceNumber"]=1,["OfficialSequenceNumber"]=1,["BatterPcode"]="batter",["BatterName"]="타자",["Inning"]=1,["BattingSide"]=0,["BatOrder"]=1,["ResultType"]=20,["ResultEventId"]="original-result"});
    await Insert("NormalizedEvents",new(){["GameId"]=officialGame,["EventId"]="original-result",["RelayGroupId"]="official-relay",["RawText"]="타자 : 좌익수 앞 1루타",["EventType"]=6});
    await using var officialCmd=c.CreateCommand();
    officialCmd.CommandText="CREATE TABLE OfficialPlayLogs(GameId TEXT PRIMARY KEY,Json TEXT,ImportedUtc TEXT); INSERT INTO OfficialPlayLogs VALUES($game,$json,'2026-05-02')";
    officialCmd.Parameters.AddWithValue("$game",officialGame);
    officialCmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(new { gameId="20260501HTSK0",sourceUrl="javascript:alert(1)",privateAudit="NEVER_PUBLIC_SOURCE",order="chronological",count=4,
        logs=new[]{new{sequence=1,text="1회초 KIA 공격"},new{sequence=2,text="1번타자 타자"},new{sequence=3,text="타자 : 좌익수 앞 1루타"},new{sequence=4,text="경기종료"}} }));
    await officialCmd.ExecuteNonQueryAsync();
}

var service=new AnalysisWebService(new DatabaseCacheService(temp,webReadOnly:true),new SiteOptions());
var expectancy=await service.QueryAsync(new("expectancy"),default);
var states=Table(expectancy,"states").Rows;
Check(states.Count==24,"RE must contain all24 states");
Check(Convert.ToDouble(expectancy.Summary[0].Value)==1,"Only the uninterrupted three-out half is complete");
var first=states.Single(x=>Number(x,"Outs")==0&&Number(x,"BaseMask")==0);
Check(Number(first,"N")==2&&Math.Abs(Number(first,"RE")-.5)<1e-8,"Runner-only run must count toward inning-end score; walkoff excluded");
Check(states.Single(x=>Number(x,"Outs")==0&&Number(x,"BaseMask")==1)["RE"] is double re && re==1,"Standalone steal before-state is included");
Check(states.Single(x=>Number(x,"Outs")==2&&Number(x,"BaseMask")==7)["RE"]==null,"Empty states must be null, not zero");
Check(Table(expectancy,"actions").Rows.Sum(x=>Number(x,"Steals"))==1,"Steal outcome appears in observed action group");
var pitchingTeam=await service.QueryAsync(new("expectancy",Team:"HT",Role:"pitcher"),default);
Check(Table(pitchingTeam,"actions").Rows.Count==0,"Pitcher team must exclude its own batting half");
var fieldingTeam=await service.QueryAsync(new("expectancy",Team:"SK",Role:"pitcher"),default);
Check(Table(fieldingTeam,"actions").Rows.Count==2,"Pitcher team includes opposing batting half");
var replay=await service.QueryAsync(new("replay",GameId:"complete",PaId:"a:pa"),default);
var chart=JsonSerializer.SerializeToElement(replay.Chart);
var points=chart.GetProperty("pitches")[0].GetProperty("points");
Check(points.GetArrayLength()==25,"Replay samples capped at25");
Check(points[0].GetProperty("y").GetDouble()==50&&Math.Abs(points[24].GetProperty("y").GetDouble())<1e-8,"Replay segment ends at stored plate plane");
var provenance=await service.QueryAsync(new("provenance",GameId:"complete"),default);
Check(Table(provenance,"plateComparisons").Rows.Any(x=>Equals(x["Naver"],"타자 : 안타")),"Preserved original event must remain visible");
Check(!JsonSerializer.Serialize(provenance).Contains("StatsJson"),"Never expose source JSON");
Check(provenance.Notes.Any(x=>x.Contains("정정 공지 테이블이 없습니다")),"Legacy optional table absence should be explained");
var changed=await service.QueryAsync(new("provenance",GameId:"20260501HTSK02026"),default);
var changedPa=Table(changed,"plateComparisons").Rows.Single();
Check(Equals(changedPa["OfficialResult"],"단타")&&Equals(changedPa["Final"],"실책 출루")&&Equals(changedPa["Status"],"공식·최종 분류 차이"),"Official versus final classification change must be reported even when original and official texts match");
var publicJson=JsonSerializer.Serialize(changed);
Check(!publicJson.Contains("NEVER_PUBLIC_SOURCE")&&!publicJson.Contains("javascript:"),"Private raw JSON and untrusted source URL must not escape");
await using(var c=new SqliteConnection($"Data Source={temp}"))
{
    await c.OpenAsync();
    await using(var schema=c.CreateCommand()) { schema.CommandText="CREATE TABLE OfficialCorrections(Id TEXT PRIMARY KEY,Year INTEGER,Json TEXT,CheckedUtc TEXT)";await schema.ExecuteNonQueryAsync(); }
    await using var transaction=await c.BeginTransactionAsync();
    await using var insert=c.CreateCommand();insert.Transaction=(SqliteTransaction)transaction;
    insert.CommandText="INSERT INTO OfficialCorrections VALUES($id,2026,$json,'2026-05-02')";
    var idParam=insert.Parameters.Add("$id",SqliteType.Text);var jsonParam=insert.Parameters.Add("$json",SqliteType.Text);
    for(var i=0;i<=520;i++)
    {
        idParam.Value=$"2026:0:{i:D4}";
        jsonParam.Value=JsonSerializer.Serialize(new { Date=i==0?"2026-05-01":"2026-04-01",Match="KIA:SSG",Inning="1초",Players="타자",Before="안타",After="실책",Content="타자 안타→실책" });
        await insert.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync();
}
var notices=await service.QueryAsync(new("provenance",GameId:"20260501HTSK02026"),default);
var selectedNotice=Table(notices,"corrections").Rows.Single();
Check(Equals(selectedNotice["Id"],"2026:0:0000"),"Selected-date notice must survive more than500 other-date notices");
Check(Equals(selectedNotice["Status"],"경기 식별 불가 · 동일 날짜·대진 복수 경기"),"Date/match alone must not assign a notice to one doubleheader game");
Check(notices.Notes.Any(x=>x.Contains("후보 공지")),"Ambiguous correction candidate requires an explanation");
var invalidTrack=new Dictionary<string,object?>{["X0"]=0d,["Y0"]=50d,["Z0"]=6d,["Vx0"]=0d,["Vy0"]=-100d,["Vz0"]=0d,["Ax"]=0d,["Ay"]=0d,["Az"]=0d,["Duration"]=.4d,["TargetY"]=0d};
Check(AnalysisWebService.ContextTrajectory(invalidTrack).Count==0,"Wrong reference-plane endpoint must be rejected");
invalidTrack["Duration"]=double.NaN;
Check(AnalysisWebService.ContextTrajectory(invalidTrack).Count==0,"NaN tracking must be rejected");
Console.WriteLine("PASS: complete-half RE, standalone runner scoring/steals, walkoff/gap exclusion, missing-state nulls, bounded replay, source provenance, optional table absence, correction date-before-cap filtering, doubleheader ambiguity.");

if(args.Length>0)
{
    var dbPath=Path.GetFullPath(args[0]);
    var hashBefore=SHA256.HashData(await File.ReadAllBytesAsync(dbPath));
    var live=new AnalysisWebService(new DatabaseCacheService(dbPath,webReadOnly:true),new SiteOptions());
    foreach(var section in new[]{"expectancy","replay","provenance"})
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        var result=await live.QueryAsync(new(section,Code:"65653"),default);
        Check(result.Tables.Length>0,$"Actual season {section} must return tables");
        Console.WriteLine($"PASS actual season {section}: {clock.ElapsedMilliseconds} ms; {string.Join(", ",result.Summary.Select(x=>$"{x.Label}={x.Value}"))}");
        if(section=="replay")Check(JsonSerializer.SerializeToElement(result.Chart).GetProperty("pitches").EnumerateArray().All(x=>x.GetProperty("points").GetArrayLength()<=25),"Actual replay point bound");
    }
    Check(hashBefore.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(dbPath))),"Season DB must not change");
    Console.WriteLine("PASS season DB SHA256 unchanged.");
}
SqliteConnection.ClearAllPools();
File.Delete(temp);
