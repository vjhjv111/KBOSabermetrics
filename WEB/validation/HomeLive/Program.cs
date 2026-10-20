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
var postseasonRaw=JsonNode.Parse(Json(id,"PLAY",round:"kbo_ps"))!;
postseasonRaw["naver"]!["result"]!["game"]!["categoryId"]="kbo";
postseasonRaw["naver"]!["result"]!["game"]!["gameDate"]=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd");
postseasonRaw["naver"]!["result"]!["game"]!["seasonYear"]=year;
var postseason=HomeLiveService.Parse(postseasonRaw.ToJsonString(),id)!;
Check(postseason.Playing&&postseason.RoundCode=="kbo_ps","포스트시즌 실시간 중계 표시");
Check(HomeLiveService.Parse(Json(id,"RESULT"),date+"KTHT0"+year) is null,"경기 ID 불일치 제외");
var ended=HomeLiveService.Parse(Json(id,"ENDED"),id)!;
Check(ended.Final&&ended.Decisions.Length==2,"ENDED 즉시 승패 투수 표시");
var bot=BotGamesService.FromLive(playing);
Check(bot.AwayName=="한화"&&bot.HomeName=="삼성"&&bot.Decisions.Length==0,"봇 팀 이름·진행 경기 승패 숨김");
Check(BotGamesService.Format(playing.Date,[bot],DateTimeOffset.UtcNow)=="한화 3 : 2 삼성 8회초","봇 한 줄 경기 점수·이닝");
Check(BotGamesService.Format(ended.Date,[BotGamesService.FromLive(ended)],null)=="한화 3 : 2 삼성 종료","봇 종료 경기 한 줄·승패 투수 생략");
Check(BotGamesService.Format(playing.Date,[],null).Contains("수집된 경기·일정이 없습니다"),"봇 빈 날짜는 과거 경기로 대체하지 않음");
Check(BotGamesService.ParseDate(null)==DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).DateTime),"봇 기본 날짜 한국시간");
try{BotGamesService.ParseDate("2026-02-30");throw new Exception("Invalid date accepted");}catch(RequestError){Console.WriteLine("PASS 봇 잘못된 날짜 거부");}
var start=DateTimeOffset.UtcNow;
Check(RenderCollectionPolicy.NextCollectionAt(true,start,start.AddSeconds(20),10)==start.AddMinutes(1),"진행 중 수집 시작 간격 1분");
Check(RenderCollectionPolicy.NextCollectionAt(false,start,start.AddSeconds(20),10)==start.AddMinutes(10).AddSeconds(20),"비진행 기존 수집 간격 유지");
Check(!RenderCollectionPolicy.AllGamesFinal([new(){StatusCode="ENDED"},new(){StatusCode="PLAY"}]),"일부 경기 종료 시 통계 DB 편입 차단 유지");
var postseasonMetadata = new NaverRelay.Models.GameInfo { GameId="44441014OBLG02026", GameDate="2026-10-14", SeasonYear=2026, CategoryId="kbo", RoundCode="kbo_ps_wd", StatusCode="BEFORE", HomeTeamCode="LG", AwayTeamCode="OB", Stadium="잠실" };
var postseasonPlaceholder=RenderCollectionPolicy.ScheduledPlaceholder(postseasonMetadata);
Check(postseasonPlaceholder is {DatabaseGameId:"44441014OBLG02026",SeasonYear:2026,GameDate:"2026-10-14",CompetitionType:NaverRelay.Parsing.GameCompetitionType.Postseason},"네이버 4444 포스트시즌 ID 보존·실제 날짜 및 시즌 사용");
Check(RenderCollectionPolicy.GameDateKey(new NaverRelayUI.Models.ScheduleGame{GameId="44441014OBLG02026",GameDate="2026-10-14"})=="20261014","포스트시즌 일정·선발 조회에서 응답 날짜 우선");
postseasonMetadata.SeasonYear=2025;
Check(RenderCollectionPolicy.ScheduledPlaceholder(postseasonMetadata) is null,"포스트시즌 응답 시즌 불일치 제외");
postseasonMetadata.SeasonYear=2026;postseasonMetadata.RoundCode="kbo_allstar";
Check(RenderCollectionPolicy.ScheduledPlaceholder(postseasonMetadata) is null,"올스타 예정 일정 제외");
postseasonMetadata.GameId="20261014OBLG02026";postseasonMetadata.RoundCode="kbo_r";
Check(RenderCollectionPolicy.ScheduledPlaceholder(postseasonMetadata) is {CompetitionType:NaverRelay.Parsing.GameCompetitionType.RegularSeason},"정규시즌 일정 ID 호환 유지");
var temp=Path.Combine(Path.GetTempPath(),"home-live-validation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try{
    var db=Path.Combine(temp,"stats.db");
    await using(var c=new SqliteConnection("Data Source="+db)){
        await c.OpenAsync();await using var cmd=c.CreateCommand();
        cmd.CommandText="CREATE TABLE Games(GameId TEXT PRIMARY KEY,SeasonYear INTEGER,GameDate TEXT,GameDateTime TEXT,Stadium TEXT,AwayTeamCode TEXT,HomeTeamCode TEXT,AwayScore INTEGER,HomeScore INTEGER,AwayHits INTEGER,HomeHits INTEGER,AwayErrors INTEGER,HomeErrors INTEGER,RoundCode TEXT,CompetitionType INTEGER,StatusCode TEXT); INSERT INTO Games(GameId,SeasonYear,CompetitionType,RoundCode,StatusCode,AwayTeamCode,HomeTeamCode) VALUES($id,$year,0,'kbo_scheduled','BEFORE','HH','SS')";
        cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$year",year);await cmd.ExecuteNonQueryAsync();
    }
    async Task<long> Query(HomeLiveGame[] games,string sql){await using var c=new SqliteConnection("Data Source="+db+";Mode=ReadOnly");await c.OpenAsync();await HomeLiveService.PrepareHomeGamesAsync(c,year,new("test",games),default);await using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToInt64(await cmd.ExecuteScalarAsync());}
    await using(var c=new SqliteConnection("Data Source="+db)){
        await c.OpenAsync();await using var cmd=c.CreateCommand();
        cmd.CommandText="UPDATE Games SET CompetitionType=$regular; INSERT INTO Games(GameId,SeasonYear,CompetitionType,RoundCode,StatusCode,AwayTeamCode,HomeTeamCode) VALUES($postId,$year,$post,'kbo_scheduled','BEFORE','HH','SS')";
        cmd.Parameters.AddWithValue("$regular",(int)NaverRelay.Parsing.GameCompetitionType.RegularSeason);
        cmd.Parameters.AddWithValue("$post",(int)NaverRelay.Parsing.GameCompetitionType.Postseason);
        cmd.Parameters.AddWithValue("$postId",date+"HHSS1"+year);cmd.Parameters.AddWithValue("$year",year);
        await cmd.ExecuteNonQueryAsync();
    }
    Check(await Query([],"SELECT COUNT(*) FROM HomeGames WHERE RoundCode='kbo_scheduled' AND StatusCode='BEFORE'")==2,"정규시즌·포스트시즌 예정 일정 모두 유지");
    Check(await Query([],$"SELECT COUNT(*) FROM HomeGames WHERE CompetitionType={(int)NaverRelay.Parsing.GameCompetitionType.RegularSeason} AND RoundCode='kbo_scheduled' AND StatusCode='BEFORE'")==1,"홈 임시 테이블 시즌 구분 열 보존·잔여 맞대결에서 포스트시즌 제외");
    await using(var c=new SqliteConnection("Data Source="+db)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="DELETE FROM Games WHERE CompetitionType=$post";cmd.Parameters.AddWithValue("$post",(int)NaverRelay.Parsing.GameCompetitionType.Postseason);await cmd.ExecuteNonQueryAsync();}
    Check(await Query([playing],"SELECT COUNT(*) FROM HomeGames WHERE StatusCode IN ('RESULT','ENDED')")==0,"진행 경기 점수는 순위에 미반영");
    Check(await Query([ended],"SELECT COUNT(*) FROM HomeGames WHERE StatusCode IN ('RESULT','ENDED')")==1,"먼저 종료된 경기만 홈 순위 반영");
    Check(await Query([ended],"SELECT COUNT(*) FROM main.Games WHERE StatusCode='BEFORE'")==1,"실제 통계 DB는 그대로 유지");
    await using(var c=new SqliteConnection("Data Source="+db)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="UPDATE Games SET StatusCode='RESULT',RoundCode='kbo_r',AwayScore=4,HomeScore=2";await cmd.ExecuteNonQueryAsync();}
    Check(await Query([ended],"SELECT COUNT(*) FROM HomeGames")==1,"DB 편입 이후 중복 집계 없음");
    Check(await Query([ended],"SELECT AwayScore FROM HomeGames")==4,"편입된 DB 공식 점수가 JSON보다 우선");
    var file=Path.Combine(temp,id+".json");await File.WriteAllTextAsync(file,Json(id,"PLAY"));
    var live=new HomeLiveService(new(){JsonDirectory=temp},NullLogger<HomeLiveService>.Instance);
    var psId="4444"+date[4..]+"HHSS0"+year;
    postseasonRaw["naver"]!["result"]!["game"]!["gameId"]=psId;
    postseasonRaw["naver"]!["result"]!["game"]!["statusCode"]="RESULT";
    var psFile=Path.Combine(temp,date+"_"+psId+".json");
    await File.WriteAllTextAsync(psFile,postseasonRaw.ToJsonString());
    var psSnapshot=await live.ReadAsync(year,default);
    var psGame=psSnapshot.Games.Single(g=>g.Id==psId);
    Check(psGame.Date==playing.Date && psGame.Final,"4444 포스트시즌 파일을 실제 경기 날짜로 탐색·판독");
    Check(await Query([psGame],"SELECT AwayScore FROM HomeGames")==4,"포스트시즌 종료 점수가 정규시즌 전적을 덮어쓰지 않음");
    Check(await live.DetailAsync(psId,default) is not null,"4444 포스트시즌 종료 상세 파일 조회");
    var historicDate=new DateTime(year-1,10,14);var historicId=$"44441014HHSS0{year-1}";
    var historicRaw=postseasonRaw.DeepClone();
    historicRaw["naver"]!["result"]!["game"]!["gameId"]=historicId;
    historicRaw["naver"]!["result"]!["game"]!["gameDate"]=historicDate.ToString("yyyy-MM-dd");
    historicRaw["naver"]!["result"]!["game"]!["seasonYear"]=year-1;
    var historicPath=Path.Combine(temp,$"{historicDate:yyyyMMdd}_{historicId}.json");
    await File.WriteAllTextAsync(historicPath,historicRaw.ToJsonString());
    Check(await live.DetailAsync(historicId,default) is not null,"최근 수집 기간 밖 과거 시즌 포스트시즌 상세 조회");
    var monthGames=await live.ReadMonthAsync(year-1,10,default);
    Check(monthGames.Length==1&&monthGames[0].Id==historicId,"월별 달력은 과거 포스트시즌 JSON 포함");
    Check((await live.ReadMonthAsync(year-1,9,default)).Length==0,"다른 월 경기 제외");
    var stored=new List<Dictionary<string,object?>>{new(){["Id"]=historicId[..13],["Date"]=historicDate.ToString("yyyy-MM-dd"),["Status"]="BEFORE",["Time"]=""}};
    var merged=GameWebService.MergeCalendar(stored,monthGames);
    Check(merged.Count==1&&Equals(merged[0]["Id"],historicId)&&Equals(merged[0]["Status"],"RESULT"),"짧은 ID 일정과 포스트시즌 결과 중복 제거");
    stored[0]["Status"]="RESULT";stored[0]["HS"]=99;
    Check(Equals(GameWebService.MergeCalendar(stored,monthGames)[0]["HS"],99),"달력에서 DB 종료 기록 우선");
    Check((await live.ReadMonthAsync(year-1,10,default,new HashSet<string>{historicId})).Length==0,"DB 편입 완료 경기는 JSON 재조회 생략");
    await using(var c=new SqliteConnection("Data Source="+db)) {
        await c.OpenAsync();await using var cmd=c.CreateCommand();
        cmd.CommandText="ALTER TABLE Games ADD COLUMN HomeWalks INTEGER; ALTER TABLE Games ADD COLUMN AwayWalks INTEGER;";
        await cmd.ExecuteNonQueryAsync();
    }
    var calendarService=new GameWebService(new NaverRelay.Infrastructure.Sqlite.DatabaseCacheService(db,webReadOnly:true),new SiteOptions(),live);
    var actualCalendar=JsonSerializer.SerializeToNode(await calendarService.QueryAsync(new("calendar",year-1,10),default))!;
    Check(actualCalendar["rows"]!.AsArray().Count==1,"경기일정 서비스 DB 미편입 포스트시즌 표시");
    var actualDetail=JsonSerializer.SerializeToNode(await calendarService.QueryAsync(new("detail",Id:historicId),default))!;
    Check(actualDetail["game"]!["Id"]!.GetValue<string>()==historicId,"경기 상세 서비스 과거 포스트시즌 fallback");
    historicRaw["naver"]!["result"]!["game"]!["statusCode"]="PLAY";
    await File.WriteAllTextAsync(historicPath,historicRaw.ToJsonString());
    var ongoing=JsonSerializer.SerializeToNode(await calendarService.QueryAsync(new("detail",Id:historicId),default))!;
    Check(ongoing["game"]!["StatusText"]!.GetValue<string>()=="8회 초"&&ongoing["decisions"]!.AsArray().Count==0,
        "진행 중 포스트시즌 상세·이닝 표시 및 승패 투수 숨김");
    var upcomingId=date+"LGLT0"+year;
    await using(var c=new SqliteConnection("Data Source="+db)) {
        await c.OpenAsync();await using var cmd=c.CreateCommand();
        cmd.CommandText="INSERT INTO Games(GameId,SeasonYear,GameDate,GameDateTime,HomeTeamCode,AwayTeamCode,StatusCode) VALUES($id,$year,$date,$date,'LT','LG','BEFORE')";
        cmd.Parameters.AddWithValue("$id",upcomingId[..13]);cmd.Parameters.AddWithValue("$year",year);
        cmd.Parameters.AddWithValue("$date",playing.Date);await cmd.ExecuteNonQueryAsync();
    }
    var startersStore=new BotStartersService(new SiteOptions{StateDirectory=Path.Combine(temp,"starters")});
    await startersStore.SaveAsync(new StarterDay(playing.Date,DateTimeOffset.UtcNow,
        [new StarterGame(upcomingId,playing.Date,"LG","롯데","BEFORE","원정선발","홈선발",DateTimeOffset.UtcNow,false)]),default);
    var previewService=new GameWebService(new NaverRelay.Infrastructure.Sqlite.DatabaseCacheService(db,webReadOnly:true),new SiteOptions(),live,startersStore);
    var preview=JsonSerializer.SerializeToNode(await previewService.QueryAsync(new("detail",Id:upcomingId[..13]),default))!;
    Check(preview["preview"]!.GetValue<bool>()&&preview["lineups"]!["homePitcher"]!.GetValue<string>()=="홈선발",
        "예정 경기 짧은 ID 상세와 예고 선발 표시");
    historicRaw["naver"]!["result"]!["game"]!["statusCode"]="BEFORE";
    await File.WriteAllTextAsync(historicPath,historicRaw.ToJsonString());
    Check((await live.ReadMonthAsync(year-1,10,default)).Length==0,"사라진 예정 경기를 오래된 JSON에서 복원하지 않음");
    Check(await live.DetailAsync(historicId,default) is null,"종료 전 JSON 상세 제외 유지");
    File.Delete(historicPath);
    File.Delete(psFile);
    var first=await live.ReadAsync(year,default);Check(first.Games.Length==1,"실제 수집 디렉터리 투영");
    Check(await live.DetailAsync(id,default) is not null,"진행 중 상세 제공");
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
    if(args.Contains("--collector"))
    {
        var collectedDir=Path.Combine(Path.GetTempPath(),"postseason-collector-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(collectedDir);
        var worker=new RenderCollectorWorker(NullLogger<RenderCollectorWorker>.Instance,new SiteOptions(),new RenderCollectorOptions{JsonDirectory=collectedDir});
        var method=typeof(RenderCollectorWorker).GetMethod("CollectAndPublishAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(worker,[sample.Date,sample.Date,CancellationToken.None])!;
        var collected=new HomeLiveService(new RenderCollectorOptions{JsonDirectory=collectedDir},NullLogger<HomeLiveService>.Instance);
        var result=await collected.ReadAsync(int.Parse(sample.Date[..4]),default);
        Check(result.Games.Any(g=>g.Id==sample.Id&&g.Date==sample.Date),"실제 일정→수집기→날짜별 JSON→홈 실시간 판독 전체 경로");
        Console.WriteLine("Collected JSON: "+collectedDir);
    }
}
Console.WriteLine("All home live checks passed.");
