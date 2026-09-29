using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NaverSabermetrics.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
string Json(string id,string status,int away=3,int home=2,string round="kbo_r") => JsonSerializer.Serialize(new{
    collectedAt=DateTimeOffset.UtcNow,naver=new{result=new{
        game=new{gameId=id,roundCode=round,awayTeamCode=id.Substring(8,2),homeTeamCode=id.Substring(10,2),awayTeamScore=away,homeTeamScore=home,statusCode=status,currentInning="8회초",awayCurrentPitcherName="원정투수",homeCurrentPitcherName="홈투수",winPitcherName="승리투수이름",losePitcherName="패전투수이름",homeTeamScoreByInning=new[]{"0","2"},awayTeamScoreByInning=new[]{"3","0"}},
        textRelayData=new{textRelays=new[]{new{no=1,inn=8,homeOrAway="1",title="타석",textOptions=new[]{new{seqno=1,text="1구 볼",stuff="직구",speed="150"}}}}}
    }}});
var date=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).ToString("yyyyMMdd");
var year=int.Parse(date[..4]);var id=date+"HHSS0"+year;
var playing=HomeLiveService.Parse(Json(id,"PLAY"),id)!;
Check(playing.Playing&&!playing.Final&&playing.StatusText=="8회 초","진행 상태·이닝과 종료 판정");
Check(playing.AwayPitcher=="원정투수"&&playing.HomePitcher=="홈투수","양 팀 현재 투수");
Check(playing.Source["textRelayData"] is null,"홈 캐시는 전체 중계를 메모리에 보관하지 않음");
Check(HomeLiveService.Parse(Json(id,"RESULT",round:"kbo_ps"),id) is null,"포스트시즌 제외");
Check(HomeLiveService.Parse(Json(id,"RESULT"),date+"KTHT0"+year) is null,"경기 ID 불일치 제외");
var ended=HomeLiveService.Parse(Json(id,"ENDED"),id)!;
Check(ended.Final&&ended.Decisions.Length==2,"ENDED 즉시 승패 투수 표시");
var bot=BotGamesService.FromLive(playing);
Check(bot.AwayName=="한화"&&bot.HomeName=="삼성"&&bot.Decisions.Length==0,"봇 팀 이름·진행 경기 승패 숨김");
Check(BotGamesService.Format(playing.Date,[bot],DateTimeOffset.UtcNow).Contains("한화 3 : 2 삼성 | 8회 초"),"봇 한 줄 경기 점수·이닝");
Check(BotGamesService.Format(ended.Date,[BotGamesService.FromLive(ended)],null).Contains("승 승리투수이름"),"봇 종료 경기 승패 투수");
Check(BotGamesService.Format(playing.Date,[],null).Contains("수집된 경기·일정이 없습니다"),"봇 빈 날짜는 과거 경기로 대체하지 않음");
Check(BotGamesService.ParseDate(null)==DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).DateTime),"봇 기본 날짜 한국시간");
try{BotGamesService.ParseDate("2026-02-30");throw new Exception("Invalid date accepted");}catch(RequestError){Console.WriteLine("PASS 봇 잘못된 날짜 거부");}
var start=DateTimeOffset.UtcNow;
Check(RenderCollectionPolicy.NextCollectionAt(true,start,start.AddSeconds(20),10)==start.AddMinutes(1),"진행 중 수집 시작 간격 1분");
Check(RenderCollectionPolicy.NextCollectionAt(false,start,start.AddSeconds(20),10)==start.AddMinutes(10).AddSeconds(20),"비진행 기존 수집 간격 유지");
Check(!RenderCollectionPolicy.AllGamesFinal([new(){StatusCode="ENDED"},new(){StatusCode="PLAY"}]),"일부 경기 종료 시 통계 DB 편입 차단 유지");
var temp=Path.Combine(Path.GetTempPath(),"home-live-validation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try{
    var db=Path.Combine(temp,"stats.db");
    await using(var c=new SqliteConnection("Data Source="+db)){
        await c.OpenAsync();await using var cmd=c.CreateCommand();
        cmd.CommandText="CREATE TABLE Games(GameId TEXT PRIMARY KEY,SeasonYear INTEGER,GameDate TEXT,GameDateTime TEXT,Stadium TEXT,AwayTeamCode TEXT,HomeTeamCode TEXT,AwayScore INTEGER,HomeScore INTEGER,AwayHits INTEGER,HomeHits INTEGER,AwayErrors INTEGER,HomeErrors INTEGER,RoundCode TEXT,StatusCode TEXT); INSERT INTO Games(GameId,SeasonYear,RoundCode,StatusCode,AwayTeamCode,HomeTeamCode) VALUES($id,$year,'kbo_scheduled','BEFORE','HH','SS')";
        cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$year",year);await cmd.ExecuteNonQueryAsync();
    }
    async Task<long> Query(HomeLiveGame[] games,string sql){await using var c=new SqliteConnection("Data Source="+db+";Mode=ReadOnly");await c.OpenAsync();await HomeLiveService.PrepareHomeGamesAsync(c,year,new("test",games),default);await using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToInt64(await cmd.ExecuteScalarAsync());}
    Check(await Query([playing],"SELECT COUNT(*) FROM HomeGames WHERE StatusCode IN ('RESULT','ENDED')")==0,"진행 경기 점수는 순위에 미반영");
    Check(await Query([ended],"SELECT COUNT(*) FROM HomeGames WHERE StatusCode IN ('RESULT','ENDED')")==1,"먼저 종료된 경기만 홈 순위 반영");
    Check(await Query([ended],"SELECT COUNT(*) FROM main.Games WHERE StatusCode='BEFORE'")==1,"실제 통계 DB는 그대로 유지");
    await using(var c=new SqliteConnection("Data Source="+db)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="UPDATE Games SET StatusCode='RESULT',RoundCode='kbo_r',AwayScore=4,HomeScore=2";await cmd.ExecuteNonQueryAsync();}
    Check(await Query([ended],"SELECT COUNT(*) FROM HomeGames")==1,"DB 편입 이후 중복 집계 없음");
    Check(await Query([ended],"SELECT AwayScore FROM HomeGames")==4,"편입된 DB 공식 점수가 JSON보다 우선");
    var file=Path.Combine(temp,id+".json");await File.WriteAllTextAsync(file,Json(id,"PLAY"));
    var live=new HomeLiveService(new(){JsonDirectory=temp},NullLogger<HomeLiveService>.Instance);
    var first=await live.ReadAsync(year,default);Check(first.Games.Length==1,"실제 수집 디렉터리 투영");
    Check(await live.DetailAsync(id,default) is null,"진행 중 상세 미제공");
    await File.WriteAllTextAsync(file,Json(id,"RESULT",10,2));
    var second=await live.ReadAsync(year,default);Check(first.Version!=second.Version&&second.Games[0].Final,"JSON 교체 시 캐시 갱신");
    var detail=JsonSerializer.SerializeToNode(await live.DetailAsync(id,default))!;
    Check(detail["game"]?["AScore"]?.GetValue<int>()==10&&detail["plays"]?.AsArray().Count==1,"DB 편입 전 종료 상세 점수·문자중계");
}finally{SqliteConnection.ClearAllPools();if(!Path.GetFullPath(temp).StartsWith(Path.GetFullPath(Path.GetTempPath())+"home-live-validation-",StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected cleanup path");Directory.Delete(temp,true);}
if(args.Length>0){
    var sample=HomeLiveService.Parse(await File.ReadAllTextAsync(args[0]),Path.GetFileNameWithoutExtension(args[0]),includeRelay:true)!;
    var detail=JsonSerializer.SerializeToNode(HomeLiveService.Detail(sample))!;
    Check(detail["batters"]!.AsArray().Count>0&&detail["pitchers"]!.AsArray().Count>0,"실제 수집 JSON 양 팀 박스스코어");
    Check(detail["plays"]!.AsArray().Count>0&&detail["probability"]!.AsArray().Count>0,"실제 수집 JSON 전체 문자중계·승리확률");
}
Console.WriteLine("All home live checks passed.");
