using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverSabermetrics.Web;
using System.Reflection;

void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
var path=Path.Combine(Path.GetTempPath(),"catalog-opponents-"+Guid.NewGuid().ToString("N")+".db");
await using var connection=new SqliteConnection("Data Source="+path);
await connection.OpenAsync();
await using(var command=connection.CreateCommand())
{
    command.CommandText="""
    CREATE TABLE Games(GameId TEXT,GameDate TEXT,SeasonYear INT,RoundCode TEXT,StatusCode TEXT,HomeTeamCode TEXT,AwayTeamCode TEXT,Stadium TEXT);
    INSERT INTO Games VALUES('g','2026-05-01',2026,'kbo_r','RESULT','OB','NC','잠실');
    CREATE TABLE PlateAppearances(GameId TEXT, BatterPcode TEXT,BatterName TEXT,PitcherPcode TEXT,PitcherName TEXT,FinalPitcherPcode TEXT,FinalPitcherName TEXT,FieldingTeamCode TEXT,BattingTeamCode TEXT,IsOfficial INT,CountsAsAtBat INT,IsHit INT,TotalBases INT,IsWalk INT,IsIntentionalWalk INT,ResultType INT,IsStrikeout INT,ActualPitchCount INT);
    INSERT INTO PlateAppearances VALUES('g','a','A','p','P',NULL,NULL,'OB','NC',1,1,1,2,0,0,0,0,3);
    INSERT INTO PlateAppearances VALUES('g','b','B','p','P',NULL,NULL,'OB','NC',1,1,0,0,0,0,0,1,4);
    INSERT INTO PlateAppearances VALUES('g','b','B','p','P',NULL,NULL,'OB','NC',1,1,0,0,0,0,0,1,4);
    INSERT INTO PlateAppearances VALUES('g','c','C','p','P',NULL,NULL,'OB','NC',1,0,0,0,1,0,0,0,4);
    """;
    await command.ExecuteNonQueryAsync();
}
var db=new DatabaseCacheService(path,webReadOnly:true);
var catalog=new CatalogService(db);
using var gate=new QueryGate(new SiteOptions{ConcurrentQueries=1,QuerySeconds=60});
var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var blocked=gate.RunAsync(async ct=>{started.SetResult();await release.Task.WaitAsync(ct);return 1;},default);
await started.Task;
try
{
    var results=await Task.WhenAll(Enumerable.Range(0,25).Select(_=>catalog.GetAsync(default))).WaitAsync(TimeSpan.FromSeconds(3));
    Check(results.All(x=>ReferenceEquals(x,results[0]))&&results[0].GameCount==1,"조회 슬롯 포화 중 목록 응답 및 동시 요청 캐시 공유");
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();
    try{await catalog.GetAsync(cancelled.Token);}catch(OperationCanceledException){}
    Check((await catalog.GetAsync(default)).GameCount==1,"호출자 취소 후 공유 캐시 유지");
}
finally{release.TrySetResult();await blocked;}
var player=new PlayerWebService(db,null!,new SiteOptions(),null!);
var method=typeof(PlayerWebService).GetMethod("Events",BindingFlags.NonPublic|BindingFlags.Instance)!;
Task<PlayerTable> Read(PlayerWebRequest r)=>(Task<PlayerTable>)method.Invoke(player,[r,CancellationToken.None])!;
var request=new PlayerWebRequest{Code="p",Role="pitcher",Section="opponents",Year=2026,Opponent="NC",PageSize=1};
var first=await Read(request);var second=await Read(request with{Page=2,Sort="AVG",Descending=false});
Check(first.Rows.Count==1&&first.HasMore&&Convert.ToInt64(first.OpponentTotal!["PA"])==4,"팀 합계는 선수별 첫 페이지 밖 타석까지 포함");
Check(Convert.ToInt64(second.OpponentTotal!["PA"])==4,"정렬·페이지 변경 후 팀 합계 고정");
Check(Convert.ToString(first.OpponentTotal["AVG"])==".333"||Convert.ToString(first.OpponentTotal["AVG"])=="0.333","팀 AVG를 전체 안타/타수로 재계산");
Check((await Read(request with{Start="2026-06-01"})).OpponentTotal is null,"날짜 범위에 기록이 없으면 합계 미표시");
Check((await Read(request with{Opponent=null})).OpponentTotal is null,"팀 미선택 시 기존 선수 목록 유지");
var batter=await Read(request with{Code="a",Role="batter",Opponent="OB"});
Check(Convert.ToInt64(batter.OpponentTotal!["H"])==1,"타자 관점 상대 팀 합계");
Console.WriteLine("Validation DB: "+path);
