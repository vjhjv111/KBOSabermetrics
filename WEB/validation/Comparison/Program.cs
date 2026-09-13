using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverSabermetrics.Web;

var checks = 0;
void Check(bool passed, string label)
{
    if (!passed) throw new InvalidOperationException("FAILED: " + label);
    checks++; Console.WriteLine("PASS " + label);
}
bool Close(double? value, double expected) => value.HasValue && Math.Abs(value.Value - expected) < 1e-9;
var fixtureDirectory = Path.Combine(Path.GetTempPath(), "kbo-comparison-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtureDirectory);
var fixture = Path.Combine(fixtureDirectory, "comparison.db");
await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fixture, Pooling = false }.ToString());
await connection.OpenAsync();
async Task Execute(string sql)
{
    await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
}
await Execute("""
CREATE TABLE Games(GameId TEXT PRIMARY KEY,SeasonYear INTEGER,GameDate TEXT,RoundCode TEXT,StatusCode TEXT,HomeTeamCode TEXT,AwayTeamCode TEXT);
CREATE TABLE BatterGameStats(GameId TEXT,Pcode TEXT,Name TEXT,TeamCode TEXT,PA INTEGER,AB INTEGER,H INTEGER,TB INTEGER,HR INTEGER,BB INTEGER,IBB INTEGER,HBP INTEGER,SO INTEGER,SH INTEGER,SF INTEGER,SB INTEGER);
INSERT INTO Games VALUES('g1',2026,'2026-04-01','kbo_r','RESULT','SS','LG'),('g2',2026,'2026-04-02','kbo_r','RESULT','SS','LG'),
('post',2026,'2026-10-30','kbo_ks','RESULT','SS','LG'),('live',2026,'2026-05-01','kbo_r','PLAY','SS','LG'),
('other',2025,'2025-09-01','kbo_r','RESULT','SS','LG'),('allstar',2026,'2026-07-01','kbo_r','RESULT','EA','WE');
INSERT INTO BatterGameStats VALUES
('g1','10001','가타자','SS',10,8,4,7,1,1,1,1,2,0,0,1),
('g2','10001','가타자','LG',90,80,16,20,0,5,2,2,20,1,2,4),
('g1','10002','같은타자','SS',100,88,20,27,1,6,3,3,22,1,2,5),
('g1','10003','나타자','LG',200,180,60,100,6,10,2,5,10,0,5,0),
('g1','10004','다타자','SS',100,80,8,8,0,10,1,4,30,2,4,2),
('g1','10005','볼넷만','SS',100,0,0,0,0,100,99,0,0,0,0,0),
('g1','10006','희생번트만','SS',100,0,0,0,0,0,0,0,0,100,0,0),
('g1','10007','표본부족','SS',5,4,1,1,0,1,0,0,1,0,0,0),
('g1','anon_name','미상코드','SS',1000,1000,1000,4000,1000,0,0,0,0,0,0,0),
('post','10001','가타자','SS',10,10,10,40,10,0,0,0,0,0,0,0),
('live','10001','가타자','SS',10,10,10,40,10,0,0,0,0,0,0,0),
('other','10001','가타자','SS',10,10,10,40,10,0,0,0,0,0,0,0),
('allstar','10001','가타자','SS',10,10,10,40,10,0,0,0,0,0,0,0);
""");
var service = new ComparisonWebService(new DatabaseCacheService(fixture, webReadOnly: true), new SiteOptions { QuerySeconds = 30 });
var result = await service.QueryAsync(new(Codes: ["10001", "10002"]), default);
var first = result.Players.Single(player => player.Code == "10001");
Check(result.CohortSize == 6 && result.LastGameDate == "2026-04-02", "cohort excludes low PA, anonymous IDs, other season/competition and unfinished games");
Check(first.Teams.SequenceEqual(new[] { "LG", "SS" }) && first.Values["PA"] == 100 && first.Values["AB"] == 88 && first.Values["H"] == 20, "multi-game totals and traded team identities preserved");
Check(Close(first.Values["AVG"], 20d/88) && Close(first.Values["OBP"],29d/99) && Close(first.Values["SLG"],27d/88), "ratios use summed numerators and denominators");
Check(first.Values["BB"] == 6 && Close(first.Values["BBPct"],6) && Close(first.Values["KPct"],22), "IBB counted once; percentage values use 0..100");
Check(Close(first.Values["OPS"],29d/99+27d/88) && Close(first.Values["ISO"],7d/88), "OPS and ISO are unrounded derived ratios");
Check(Close(first.Percentiles["AVG"],50) && Close(result.Players.Single(player=>player.Code=="10002").Percentiles["AVG"],50), "tied values use midrank among valid metric observations");
Check(Close(first.Percentiles["KPct"],100d/3) && result.Players.Single(player=>player.Code=="10003").Percentiles["KPct"] > first.Percentiles["KPct"], "K percentile is reversed so higher means lower strikeout rate");
var noAtBats = result.Players.Single(player=>player.Code=="10005");
var noObpDenominator = result.Players.Single(player=>player.Code=="10006");
Check(noAtBats.Values["AVG"] is null && noAtBats.Values["SLG"] is null && noAtBats.Values["ISO"] is null && noAtBats.Values["OPS"] is null && noAtBats.Values["OBP"] == 1 && noObpDenominator.Values["OBP"] is null, "zero denominators stay null, genuine zero and valid OBP remain values");
Check(result.Similar.First().Code == "10002" && result.Similar.First().Distance == 0 && result.Similar.First().Components.Values.All(value=>value==0), "identical profiles are nearest with zero RMS components");
Check(result.Similar.Length==3 && result.Similar.All(player=>player.Code!="10001" && player.Code!="10005" && player.Code!="10006"), "similarity excludes target and incomplete vectors");
foreach (var similar in result.Similar)
    Check(Close(similar.Distance, Math.Sqrt(similar.Components.Values.Sum(value=>value!.Value*value.Value)/4)), "RMS formula for " + similar.Code);
Check(result.Players.All(player=>player.PhotoUrl is null), "legacy DB without profile table supports comparisons");
var fewer = await service.QueryAsync(new(Codes:["10001"]), default);
Check(fewer.CohortSize==result.CohortSize && fewer.Players.Single(player=>player.Code=="10001").Percentiles.SequenceEqual(first.Percentiles), "selected display subset never changes league cohort percentiles");
var fallback = await service.QueryAsync(new(Codes:["10007","99999"],TargetCode:"99999"), default);
Check(fallback.SelectedCodes.SequenceEqual(new[]{"10003"}) && fallback.TargetCode=="10003" && fallback.Notes.Any(note=>note.Contains("선택 선수 2명")), "ineligible requested selections fall back deterministically and are explained");
var none = await service.QueryAsync(new(Year:2024), default);
Check(none.CohortSize==0 && none.Players.Length==0 && none.SelectedCodes.Length==0 && none.TargetCode=="" && none.Similar.Length==0 && none.LastGameDate is null, "empty season is safe");
var singleton = await service.QueryAsync(new(MinPa:150), default);
Check(singleton.CohortSize==1 && singleton.Players[0].Percentiles.Values.All(value=>value==50) && singleton.Similar.Length==0, "single-player cohort uses midpoint and has no false neighbor");
var invalid = new ComparisonRequest[] { new(Year:1800),new(MinPa:0),new(MinPa:1001),new(Codes:["10001","10001"]),new(Codes:["10001","10002","10003","10004","10005"]),new(Codes:["../10001"]),new(TargetCode:"bad"),new(TargetCode:null!),new(Codes:[null!]) };
foreach (var request in invalid)
{
    var rejected=false;try{request.Validate();}catch(RequestError){rejected=true;}Check(rejected,"invalid request rejected");
}
using(var canceled=new CancellationTokenSource())
{
    canceled.Cancel();var observed=false;try{await service.QueryAsync(new(),canceled.Token);}catch(OperationCanceledException){observed=true;}Check(observed,"cancellation observed before database reads");
}
await Execute("""
CREATE TABLE OfficialPlayerProfiles(Pcode TEXT PRIMARY KEY,PhotoFileName TEXT);
INSERT INTO OfficialPlayerProfiles VALUES('10001','../secret.png'),('10002','10002.jpg'),('10003','10003.jpg');
""");
var photos=Path.Combine(fixtureDirectory,"player-photos");Directory.CreateDirectory(photos);
await File.WriteAllBytesAsync(Path.Combine(photos,"10002.jpg"),[255,216,255,0,0,0,0,0,255,217]);
var withPhotos=await service.QueryAsync(new(),default);
Check(withPhotos.Players.Single(player=>player.Code=="10002").PhotoUrl=="/api/player-photo/10002" && withPhotos.Players.Where(player=>player.Code!="10002").All(player=>player.PhotoUrl is null), "batch photo lookup accepts only safe existing player files");
await Execute("""
INSERT INTO BatterGameStats SELECT 'g1','10008','동률친구','SS',100,88,20,27,1,6,3,3,22,1,2,5;
""");
var tied=await service.QueryAsync(new(TargetCode:"10001"),default);
Check(tied.Similar.Take(2).Select(player=>player.Code).SequenceEqual(new[]{"10002","10008"}),"distance ties are ordered by player code");
await Execute("""
WITH RECURSIVE n(value) AS (VALUES(20000) UNION ALL SELECT value+1 FROM n WHERE value<20500)
INSERT INTO BatterGameStats SELECT 'g1',CAST(value AS TEXT),'추가선수','SS',100,100,20,20,0,0,0,0,10,0,0,0 FROM n;
""");
var tooLarge=false;try{await service.QueryAsync(new(),default);}catch(RequestError error){tooLarge=error.Code=="COMPARISON_COHORT_TOO_LARGE";}
Check(tooLarge,"oversized cohorts fail explicitly rather than silently truncate");

if(args.Length>0)
{
    var actual=Path.GetFullPath(args[0]);
    var before=SHA256.HashData(await File.ReadAllBytesAsync(actual));
    var actualService=new ComparisonWebService(new DatabaseCacheService(actual,webReadOnly:true),new SiteOptions{QuerySeconds=30});
    var sw=Stopwatch.StartNew();var real=await actualService.QueryAsync(new(),default);sw.Stop();
    Check(real.CohortSize>0 && real.CohortSize<=500,"actual DB cohort returned within bound");
    Check(real.Players.All(player=>player.Values.Values.Concat(player.Percentiles.Values).All(value=>value is null || double.IsFinite(value.Value))),"actual DB has no NaN or infinity");
    Check(real.Similar.Length<=10 && real.Similar.All(player=>player.Code!=real.TargetCode && player.Distance is >=0 and <=100),"actual DB neighbors bounded and target excluded");
    Check(before.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(actual))),"actual DB unchanged after query");
    Console.WriteLine($"Actual cohort={real.CohortSize}, minPA={real.MinPa}, asOf={real.LastGameDate}, photos={real.Players.Count(player=>player.PhotoUrl is not null)}, elapsed={sw.ElapsedMilliseconds}ms");
}
Console.WriteLine($"Comparison validation passed: {checks} checks. Fixture: {fixture}");
