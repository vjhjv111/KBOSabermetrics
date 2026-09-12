using System.Net;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
using NaverRelay.Application.Queries;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

internal static class TeamErTests
{
    static readonly (string Code, string Name)[] Teams = [("HH","한화"),("SS","삼성"),("HT","KIA"),("KT","KT"),("NC","NC"),("LG","LG"),("SK","SSG"),("OB","두산"),("LT","롯데"),("WO","키움")];
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static string Html(int er=1) => """
        <html><select id="cphContents_cphContents_cphContents_ddlSeason_ddlSeason" name="ctl00$ctl00$ctl00$cphContents$cphContents$cphContents$ddlSeason$ddlSeason"><option selected="selected" value="2026">2026</option></select>
        <select id="cphContents_cphContents_cphContents_ddlSeries_ddlSeries" name="ctl00$ctl00$ctl00$cphContents$cphContents$cphContents$ddlSeries$ddlSeries"><option selected="selected" value="0">KBO 정규시즌</option></select>
        <table><thead><tr><th>순위</th><th>팀명</th><th>ERA</th><th>G</th><th>W</th><th>L</th><th>SV</th><th>HLD</th><th>WPCT</th><th>IP</th><th>H</th><th>HR</th><th>BB</th><th>HBP</th><th>SO</th><th>R</th><th>ER</th><th>WHIP</th></tr></thead><tbody>
        """ + string.Join("",Teams.Select((t,i)=>$"<tr><td>{i+1}</td><td>{t.Name}</td><td>{er}.00</td><td>1</td><td>0</td><td>0</td><td>0</td><td>0</td><td>0.000</td><td>9</td><td>0</td><td>0</td><td>0</td><td>0</td><td>0</td><td>3</td><td>{er}</td><td>0.00</td></tr>")) + "</tbody></table></html>";

    public static async Task Run(string outputFolder)
    {
        var folder = Path.Combine(outputFolder,"team-er-"+Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(folder);
        var db = Path.Combine(folder,"team-er.db"); var store = new DatabaseCacheService(db); await store.InitializeAsync();
        for(int i=0;i<Teams.Length;i+=2)
        {
            var id="20260911"+Teams[i].Code+Teams[i+1].Code+"02026";
            var game=new NormalizedGame {GameId=id,GameDate="2026-09-11",SeasonYear=2026,RoundCode="kbo_r",CompetitionType=GameCompetitionType.RegularSeason,StatusCode="RESULT",
                AwayTeam=new(){TeamCode=Teams[i].Code,TeamName=Teams[i].Name,Side=TeamSide.Away,FinalScore=3},HomeTeam=new(){TeamCode=Teams[i+1].Code,TeamName=Teams[i+1].Name,Side=TeamSide.Home,FinalScore=3}};
            for(int j=0;j<2;j++)game.PitchingLines.Add(new(){GameId=id,TeamCode=Teams[i+j].Code,TeamSide=j==0?TeamSide.Away:TeamSide.Home,Pcode="test"+(i+j),Name="검증투수"+(i+j),AppearanceSequence=1,InningsDisplay="9",RunsAllowed=3,EarnedRuns=2});
            var source=Path.Combine(folder,id+".json");await File.WriteAllTextAsync(source,"{}");
            await store.SaveGameAndSourceAsync(game,new InputDocument{Id=id,Kind=InputDocumentKind.JsonFile,ContainerPath=source,Length=2});
        }
        var html=Html();var rows=DatabaseCacheService.ParseOfficialTeamPitching(html,2026);
        Check(rows.Count==10 && rows.Sum(x=>x.EarnedRuns)==10,"공식 팀 투수 HTML 10팀 자책점 파싱");
        var captured=Path.Combine(Directory.GetParent(Path.GetFullPath(outputFolder))!.FullName,"team-pitcher-source-20260912.html");
        if(File.Exists(captured))
        {
            var actual=DatabaseCacheService.ParseOfficialTeamPitching(File.ReadAllText(captured),2026);
            Check(actual.Count==10 && actual.Sum(x=>x.EarnedRuns)==5706 && actual.Sum(x=>x.Games)==1244,"실제 공식 HTML 622경기 / 팀 자책점 5,706 검증");
        }
        bool rejected=false;try{DatabaseCacheService.ParseOfficialTeamPitching(html,2025);}catch(InvalidDataException){rejected=true;}
        Check(rejected,"다른 시즌 공식 HTML 거부");
        rejected=false;try{DatabaseCacheService.ParseOfficialTeamPitching("<html>점검 중</html>",2026);}catch(InvalidDataException){rejected=true;}
        Check(rejected,"사이트 오류 페이지를 빈 팀 기록으로 저장하지 않음");
        var query=new GameQuery{SeasonYear=2026,Competition="정규시즌",Grouping=AnalyticsGrouping.Team};
        var analytics=new DatabaseAnalyticsService(store);var league=new LeagueReference();
        var initialSnapshot=await analytics.GetSnapshotAsync(query,league);
        var initialPlayers=await analytics.GetSnapshotAsync(query with{Grouping=AnalyticsGrouping.PlayerByTeam},league);
        Check(initialSnapshot.PitcherValues.Count==10 && initialSnapshot.PitcherValues.All(x=>x.EarnedRuns==2),"공식 팀 자책점 수집 전 개인 합계 표시");
        var result=await store.StoreAndReconcileOfficialTeamPitchingAsync(2026,html);
        Check(result.Teams==10 && result.Pending==0,"DB G/IP/R와 같은 범위 공식 팀 자책점 수집");
        var applicable=await store.GetApplicableOfficialTeamPitchingAsync(query);
        Check(applicable.Count==10 && applicable.Values.All(x=>x.EarnedRuns==1),"개인 자책 합계와 다른 공식 팀 자책점 선택");
        var officialSnapshot=await analytics.GetSnapshotAsync(query,league);
        Check(officialSnapshot.PitcherValues.All(x=>x.EarnedRuns==1 && x.Games==1),"팀 자책점 수집 시 화면 캐시 갱신 / 팀 경기 수 보존");
        var players=await analytics.GetSnapshotAsync(query with{Grouping=AnalyticsGrouping.PlayerByTeam},league);
        Check(players.PitcherValues.All(x=>x.EarnedRuns==2),"팀 자책점 보정 후 개인 자책점 유지");
        Check(officialSnapshot.PitcherValues.All(x=>x.EarnedRuns*9.0/x.InningsPitched==1),"공식 팀 자책점으로 ERA 1.00 계산");
        Check(officialSnapshot.PitcherValues.All(x=>x.War==initialSnapshot.PitcherValues.Single(b=>b.Pcode==x.Pcode).War && x.Ra9War==initialSnapshot.PitcherValues.Single(b=>b.Pcode==x.Pcode).Ra9War),"팀 자책점 표시 보정으로 팀 WAR를 재작성하지 않음");
        Check(players.PitcherValues.All(x=>x.War==initialPlayers.PitcherValues.Single(b=>b.Pcode==x.Pcode).War && x.Ra9War==initialPlayers.PitcherValues.Single(b=>b.Pcode==x.Pcode).Ra9War),"팀 자책점 표시 보정으로 개인 WAR를 재작성하지 않음");
        var teamPage=await new DatabaseTeamPageService(store).GetTeamPageAsync("HH");
        Check(teamPage?.PitchingSeasons.Single().ERA==1 && teamPage.PitchingSeasons.Single().EarnedRuns==1,"팀 개인 페이지 시즌 ERA/자책점도 공식값 적용");
        var filteredSnapshot=await analytics.GetSnapshotAsync(query with{StartDate=new DateTime(2026,9,1)},league);
        Check(filteredSnapshot.PitcherValues.All(x=>x.EarnedRuns==2),"월간 화면은 공식 시즌 누적값 대신 해당 경기 합계 사용");
        var handler=new YearPostbackHandler();
        using(var http=new HttpClient(handler))
        {
            var fetched=await store.SyncOfficialTeamPitchingAsync(2026,transport:http);
            Check(fetched.Teams==10 && fetched.Pending==0 && handler.ValidPostback,"공식 페이지 선택 연도가 다르면 숨은 폼 값과 연도/대회를 POST하여 조회: "+fetched.Message+" / POST "+handler.ValidPostback);
        }
        foreach(var filtered in new[]{query with{StartDate=new DateTime(2026,9,1)},query with{EndDate=new DateTime(2026,9,11)},query with{OpponentCode="HH"},query with{Venue="홈"},query with{Stadium="잠실"},query with{Weekday="금"},query with{RecentGameCount=5},query with{InningFilter="1"},query with{Grouping=AnalyticsGrouping.PlayerByTeam},query with{SeasonYear=null},query with{Competition="전체"}})
            Check((await store.GetApplicableOfficialTeamPitchingAsync(filtered)).Count==0,"필터/개인/복수범위에 시즌 팀 자책점 혼용 차단 "+filtered.CacheKey);
        using(var http=new HttpClient(new ErrorHandler()))
        {
            var failure=await store.SyncOfficialTeamPitchingAsync(2026,transport:http);
            Check(failure.Pending>0 && (await store.GetApplicableOfficialTeamPitchingAsync(query)).Count==10,"공식 접속 실패 시 검증된 동일범위 이전 스냅숏 유지");
        }
        using var c=new SqliteConnection("Data Source="+db);await c.OpenAsync();
        using(var cmd=c.CreateCommand()){cmd.CommandText="UPDATE PitcherGameStats SET InningsOuts=InningsOuts+1 WHERE TeamCode='HH'";await cmd.ExecuteNonQueryAsync();}
        Check((await store.GetApplicableOfficialTeamPitchingAsync(query)).Count==0,"DB 이닝 변경 시 오래된 공식 팀 자책점 사용 중단");
        var mismatch=await store.StoreAndReconcileOfficialTeamPitchingAsync(2026,Html(2));
        Check(mismatch.Pending>0 && (await store.GetApplicableOfficialTeamPitchingAsync(query)).Count==0,"범위 불일치 공식 스냅숏으로 덮어쓰기 차단");
        using(var cmd=c.CreateCommand()){cmd.CommandText="UPDATE PitcherGameStats SET InningsOuts=InningsOuts-1 WHERE TeamCode='HH'";await cmd.ExecuteNonQueryAsync();}
        await store.StoreAndReconcileOfficialTeamPitchingAsync(2026,html);
        Check((await store.GetApplicableOfficialTeamPitchingAsync(query)).Count==10,"같은 범위로 재검증 후 공식 자책점 사용 복원");
        using(var cmd=c.CreateCommand()){cmd.CommandText="UPDATE Games SET GameDate='2026-09-10' WHERE AwayTeamCode='HH'";await cmd.ExecuteNonQueryAsync();}
        Check((await store.GetApplicableOfficialTeamPitchingAsync(query)).Count==0,"합계가 같아도 경기 집합 변경 시 기존 스냅숏 무효화");
        Console.WriteLine("팀 자책점 검증 DB: "+db);
    }
    sealed class ErrorHandler:HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));}
    sealed class YearPostbackHandler:HttpMessageHandler
    {
        public bool ValidPostback {get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            if(request.Method==HttpMethod.Get)return new(HttpStatusCode.OK){Content=new StringContent(Html().Replace("2026","2025").Replace("<option selected=\"selected\" value=\"2025\">2025</option>","<option selected=\"selected\" value=\"2025\">2025</option><option value=\"2026\">2026</option>").Replace("<html>","<html><input type=\"hidden\" name=\"__VIEWSTATE\" value=\"fixture-state\"><input type=\"hidden\" name=\"__EVENTVALIDATION\" value=\"fixture-validation\">"))};
            var form=(await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&').Select(x=>x.Split('=',2)).ToDictionary(x=>WebUtility.UrlDecode(x[0]),x=>WebUtility.UrlDecode(x.Length>1?x[1]:""));
            ValidPostback=form.GetValueOrDefault("__VIEWSTATE")=="fixture-state" && form.GetValueOrDefault("__EVENTVALIDATION")=="fixture-validation" && form.Any(x=>x.Key.EndsWith("$ddlSeason")&&x.Value=="2026") && form.Any(x=>x.Key.EndsWith("$ddlSeries")&&x.Value=="0") && form.GetValueOrDefault("__EVENTTARGET")?.EndsWith("$ddlSeason")==true;
            return new(HttpStatusCode.OK){Content=new StringContent(Html())};
        }
    }
}
