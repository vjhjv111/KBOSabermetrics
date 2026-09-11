using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

namespace NaverSabermetrics.Web;

public sealed record TeamWebRequest
{
    public string Team { get; init; } = "";
    public int Year { get; init; }
    public string Competition { get; init; } = "정규시즌";
    public string Section { get; init; } = "overview";
    public string Role { get; init; } = "batter";
    public int Page { get; init; } = 1;
    public void Validate()
    {
        if(string.IsNullOrEmpty(Team) || Team.Length>20 || Team.Any(c=>!char.IsAsciiLetterOrDigit(c)))throw new RequestError("팀 코드가 잘못되었습니다.");
        if(Year is <1900 or >2200 || Page is <1 or >20)throw new RequestError("연도 또는 페이지가 잘못되었습니다.");
        if(!new[]{"정규시즌","포스트시즌","시범경기","전체"}.Contains(Competition) || !new[]{"overview","schedule","roster","scores"}.Contains(Section) || Role is not ("batter" or "pitcher"))throw new RequestError("지원하지 않는 팀 조회입니다.");
    }
}

public sealed class TeamWebService(DatabaseCacheService db,SiteOptions options)
{
    static double N(Dictionary<string,object?> r,string k)=>r.GetValueOrDefault(k) is object v?Convert.ToDouble(v,CultureInfo.InvariantCulture):0;
    static string S(Dictionary<string,object?> r,string k)=>Convert.ToString(r.GetValueOrDefault(k),CultureInfo.InvariantCulture)??"";
    static double? Rate(double n,double d)=>d>0?Math.Round(n/d,3):null;
    static string Round(string c)=>c switch{"정규시즌"=>"LOWER(TRIM(g.RoundCode))='kbo_r'","시범경기"=>$"g.CompetitionType={(int)GameCompetitionType.Preseason}","포스트시즌"=>$"g.CompetitionType={(int)GameCompetitionType.Postseason}",_=>"1=1"};
    async Task<List<Dictionary<string,object?>>> Sql(string sql,TeamWebRequest r,CancellationToken ct)
    {
        await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync(ct);
        await using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.CommandTimeout=options.QuerySeconds;
        cmd.Parameters.AddWithValue("$team",r.Team);cmd.Parameters.AddWithValue("$year",r.Year);cmd.Parameters.AddWithValue("$offset",(r.Page-1)*25);
        using var cancel=ct.Register(cmd.Cancel);await using var reader=await cmd.ExecuteReaderAsync(ct);var rows=new List<Dictionary<string,object?>>();
        while(await reader.ReadAsync(ct)){ct.ThrowIfCancellationRequested();var row=new Dictionary<string,object?>();for(int i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);rows.Add(row);}return rows;
    }
    static object Record(IEnumerable<Dictionary<string,object?>> games)
    {
        var a=games.ToArray();var w=a.Count(x=>N(x,"RF")>N(x,"RA"));var l=a.Count(x=>N(x,"RF")<N(x,"RA"));
        return new Dictionary<string,object?>{{"G",a.Length},{"W",w},{"D",a.Length-w-l},{"L",l},{"PCT",Rate(w,w+l)},{"RF",a.Sum(x=>N(x,"RF"))},{"RA",a.Sum(x=>N(x,"RA"))}};
    }
    static bool Final(Dictionary<string,object?> g)=>S(g,"Status").Equals("RESULT",StringComparison.OrdinalIgnoreCase)&&g["RF"] is not null&&g["RA"] is not null;
    async Task<List<Dictionary<string,object?>>> Roster(TeamWebRequest r,CancellationToken ct)
    {
        var batter=r.Role=="batter";var table=batter?"BatterGameStats":"PitcherGameStats";
        var columns=batter?"SUM(s.PA) PA,SUM(s.AB) AB,SUM(s.H) H,SUM(s.HR) HR,SUM(s.Runs) R,SUM(s.RBI) RBI,SUM(s.SB) SB,SUM(s.BB) BB,SUM(s.HBP) HBP,SUM(s.SF) SF,SUM(s.TB) TB,SUM(s.SO) SO":
            "SUM(s.InningsOuts) Outs,SUM(s.EarnedRuns) ER,SUM(s.FinalSO) SO,SUM(s.FinalBB) BB,SUM(s.HitsAllowed) H,SUM(s.HomeRunsAllowed) HR,SUM(s.IsStarter) GS";
        var rows=await Sql($"SELECT s.Pcode Code,MAX(s.Name) Name,COUNT(DISTINCT s.GameId) G,{columns} FROM {table} s JOIN Games g ON g.GameId=s.GameId WHERE s.TeamCode=$team AND g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode)='RESULT' {(batter?"":"AND s.HasFinalLine=1")} GROUP BY s.Pcode ORDER BY {(batter?"PA":"Outs")} DESC,s.Pcode LIMIT 1000",r,ct);
        foreach(var x in rows)if(batter){x["AVG"]=Rate(N(x,"H"),N(x,"AB"));x["OBP"]=Rate(N(x,"H")+N(x,"BB")+N(x,"HBP"),N(x,"AB")+N(x,"BB")+N(x,"HBP")+N(x,"SF"));x["SLG"]=Rate(N(x,"TB"),N(x,"AB"));x["OPS"]=x["OBP"] is null||x["SLG"] is null?null:Math.Round(N(x,"OBP")+N(x,"SLG"),3);}else{x["IP"]=$"{(int)N(x,"Outs")/3}.{(int)N(x,"Outs")%3}";x["ERA"]=Rate(N(x,"ER")*27,N(x,"Outs"));x["WHIP"]=Rate((N(x,"H")+N(x,"BB"))*3,N(x,"Outs"));}
        return rows;
    }
    public async Task<object> QueryAsync(TeamWebRequest r,CancellationToken ct)
    {
        r.Validate();
        if(r.Section=="roster") {var roster=await Roster(r,ct);return new{rows=roster.Skip((r.Page-1)*25).Take(25),hasMore=roster.Count>r.Page*25,columns=r.Role=="batter"?new[]{"Name","G","PA","AB","H","HR","R","RBI","SB","BB","SO","AVG","OBP","SLG","OPS"}:new[]{"Name","G","GS","IP","H","HR","BB","SO","ERA","WHIP"}};}
        var games=await Sql($"SELECT g.GameId Id,g.GameDate Date,g.GameDateTime Time,g.Stadium Stadium,g.StatusCode Status,CASE WHEN g.HomeTeamCode=$team THEN '홈' ELSE '원정' END Venue,CASE WHEN g.HomeTeamCode=$team THEN g.AwayTeamCode ELSE g.HomeTeamCode END Opponent,CASE WHEN g.HomeTeamCode=$team THEN g.HomeScore ELSE g.AwayScore END RF,CASE WHEN g.HomeTeamCode=$team THEN g.AwayScore ELSE g.HomeScore END RA FROM Games g WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND (g.HomeTeamCode=$team OR g.AwayTeamCode=$team) ORDER BY g.GameDate DESC,g.GameId DESC LIMIT 1000",r,ct);
        foreach(var g in games)g["Result"]=Final(g)?N(g,"RF")>N(g,"RA")?"승":N(g,"RF")<N(g,"RA")?"패":"무":S(g,"Status");
        if(r.Section=="schedule")return new{rows=games.Skip((r.Page-1)*25).Take(25),hasMore=games.Count>r.Page*25,columns=new[]{"Date","Time","Opponent","Venue","Stadium","Result","RF","RA"}};
        var completed=games.Where(Final).ToList();
        if(r.Section=="scores")
        {
            object[] Distribution(string key)=>completed.GroupBy(x=>N(x,key)).OrderBy(g=>g.Key).Select(g=>(object)new{score=g.Key,record=Record(g)}).ToArray();
            var innings=await Sql($"WITH ordered AS (SELECT g.GameId,g.HomeTeamCode,g.HomeScore,g.AwayScore,p.Inning,p.BeforeHomeScore,p.BeforeAwayScore,p.AfterHomeScore,p.AfterAwayScore,ROW_NUMBER() OVER(PARTITION BY g.GameId,p.Inning ORDER BY p.ChronologicalIndex) rn FROM Games g JOIN RelayGroups p ON p.GameId=g.GameId WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode)='RESULT' AND (g.HomeTeamCode=$team OR g.AwayTeamCode=$team) AND p.Inning BETWEEN 1 AND 30) SELECT GameId,Inning,CASE WHEN HomeTeamCode=$team THEN HomeScore ELSE AwayScore END RF,CASE WHEN HomeTeamCode=$team THEN AwayScore ELSE HomeScore END RA,CASE WHEN HomeTeamCode=$team THEN BeforeHomeScore-BeforeAwayScore ELSE BeforeAwayScore-BeforeHomeScore END Lead FROM ordered WHERE rn=1 AND BeforeHomeScore IS NOT NULL AND BeforeAwayScore IS NOT NULL AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL ORDER BY Inning,GameId",r,ct);
            var states=innings.GroupBy(x=>(inning:N(x,"Inning"),state:N(x,"Lead")>0?"리드":N(x,"Lead")<0?"열세":"동점")).OrderBy(g=>g.Key.inning).ThenBy(g=>g.Key.state).Select(g=>new{inning=g.Key.inning,state=g.Key.state,record=Record(g)});
            var scoring=await Sql($"SELECT p.GameId,p.Inning,p.BattingTeamCode Team,MIN(CASE WHEN p.BattingTeamCode=g.HomeTeamCode THEN p.BeforeHomeScore ELSE p.BeforeAwayScore END) StartScore,MAX(CASE WHEN p.BattingTeamCode=g.HomeTeamCode THEN p.AfterHomeScore ELSE p.AfterAwayScore END) EndScore FROM RelayGroups p JOIN Games g ON g.GameId=p.GameId WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode)='RESULT' AND (g.HomeTeamCode=$team OR g.AwayTeamCode=$team) AND p.BattingTeamCode IN (g.HomeTeamCode,g.AwayTeamCode) AND p.Inning BETWEEN 1 AND 30 GROUP BY p.GameId,p.Inning,p.BattingTeamCode",r,ct);
            var totals=scoring.Where(x=>x["StartScore"] is not null&&x["EndScore"] is not null&&N(x,"EndScore")>=N(x,"StartScore")).GroupBy(x=>(inning:N(x,"Inning"),side:S(x,"Team")==r.Team?"득점":"실점")).OrderBy(g=>g.Key.inning).ThenBy(g=>g.Key.side).Select(g=>new{inning=g.Key.inning,side=g.Key.side,games=g.Count(),runs=g.Sum(x=>N(x,"EndScore")-N(x,"StartScore")),average=Rate(g.Sum(x=>N(x,"EndScore")-N(x,"StartScore")),g.Count())});
            var inningDistribution=scoring.Where(x=>x["StartScore"] is not null&&x["EndScore"] is not null&&N(x,"EndScore")>=N(x,"StartScore")).GroupBy(x=>(inning:N(x,"Inning"),side:S(x,"Team")==r.Team?"득점":"실점")).OrderBy(g=>g.Key.inning).Select(g=>new{inning=g.Key.inning,side=g.Key.side,bins=Enumerable.Range(0,6).Select(n=>g.Count(x=>n==5?N(x,"EndScore")-N(x,"StartScore")>=5:N(x,"EndScore")-N(x,"StartScore")==n)).ToArray()});
            return new{scored=Distribution("RF"),allowed=Distribution("RA"),innings=totals,inningDistribution,states,note="종료 경기 기준 · 승률은 승/(승+패), 무승부 제외. 이닝 통계는 저장된 중계 점수로 복원하며 점수가 없는 이닝은 제외합니다."};
        }
        var standings=await Sql($"WITH sides AS (SELECT HomeTeamCode Team,HomeScore RF,AwayScore RA FROM Games g WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode)='RESULT' AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL UNION ALL SELECT AwayTeamCode,AwayScore,HomeScore FROM Games g WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode)='RESULT' AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL) SELECT Team,COUNT(*) G,SUM(RF>RA) W,SUM(RF=RA) D,SUM(RF<RA) L,1.0*SUM(RF>RA)/NULLIF(SUM(RF!=RA),0) PCT,SUM(RF) RF,SUM(RA) RA FROM sides WHERE Team IS NOT NULL GROUP BY Team ORDER BY PCT DESC,W DESC,Team LIMIT 40",r,ct);
        foreach(var t in standings)t["Rank"]=t["PCT"] is null?null:1+standings.Count(x=>x["PCT"] is not null&&N(x,"PCT")>N(t,"PCT"));
        var batters=await Roster(r with{Role="batter"},ct);var pitchers=await Roster(r with{Role="pitcher"},ct);var leaders=new List<object>();
        foreach(var metric in new[]{"AVG","HR","RBI","R","SB","OBP","SLG","OPS","ERA","SO"})
        {
            var pitching=metric is "ERA" or "SO";var rate=metric is "AVG" or "OBP" or "SLG" or "OPS" or "ERA";
            var candidates=(pitching?pitchers:batters).Where(x=>x.GetValueOrDefault(metric) is not null&&(!rate||N(x,pitching?"Outs":"PA")>=completed.Count*(pitching?3:3.1)));
            var best=(metric=="ERA"?candidates.OrderBy(x=>N(x,metric)):candidates.OrderByDescending(x=>N(x,metric))).ThenBy(x=>S(x,"Code")).FirstOrDefault();
            if(best is not null)leaders.Add(new{metric,code=S(best,"Code"),name=S(best,"Name"),value=best[metric],role=pitching?"pitcher":"batter"});
        }
        var field=new List<object>();
        foreach(var (pos,column) in new[]{("C","CatcherInnings"),("1B","FirstBaseInnings"),("2B","SecondBaseInnings"),("3B","ThirdBaseInnings"),("SS","ShortstopInnings"),("LF","LeftFieldInnings"),("CF","CenterFieldInnings"),("RF","RightFieldInnings"),("DH","DhPa")})
        {
            var rows=await Sql($"SELECT s.Pcode Code,MAX(s.Name) Name,SUM(s.{column}) Volume FROM BatterGameStats s JOIN Games g ON g.GameId=s.GameId WHERE s.TeamCode=$team AND g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode)='RESULT' GROUP BY s.Pcode HAVING SUM(s.{column})>0 ORDER BY Volume DESC,s.Pcode LIMIT 1",r,ct);
            if(rows.Count>0)field.Add(new{position=pos,code=S(rows[0],"Code"),name=S(rows[0],"Name"),volume=N(rows[0],"Volume")});
        }
        var latest=completed.FirstOrDefault();var line=new List<Dictionary<string,object?>>();
        if(latest is not null){var latestId=S(latest,"Id");var all=await Sql($"SELECT p.GameId,p.Inning,p.BattingTeamCode Team,MIN(CASE WHEN p.BattingTeamCode=g.HomeTeamCode THEN p.BeforeHomeScore ELSE p.BeforeAwayScore END) StartScore,MAX(CASE WHEN p.BattingTeamCode=g.HomeTeamCode THEN p.AfterHomeScore ELSE p.AfterAwayScore END) EndScore FROM RelayGroups p JOIN Games g ON g.GameId=p.GameId WHERE g.GameId=(SELECT g.GameId FROM Games g WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode)='RESULT' AND (g.HomeTeamCode=$team OR g.AwayTeamCode=$team) AND g.HomeScore IS NOT NULL AND g.AwayScore IS NOT NULL ORDER BY g.GameDate DESC,g.GameId DESC LIMIT 1) AND p.Inning BETWEEN 1 AND 30 AND p.BattingTeamCode IN (g.HomeTeamCode,g.AwayTeamCode) GROUP BY p.GameId,p.Inning,p.BattingTeamCode ORDER BY p.Inning",r,ct);line=all;}
        return new{record=Record(completed),standings,leaders,field,latest,line,recent=games.Take(21),next=games.Where(x=>!Final(x)&&DateTime.TryParse(S(x,"Date"),out var d)&&d.Date>=DateTime.UtcNow.AddHours(9).Date).OrderBy(x=>S(x,"Date")).FirstOrDefault(),opponents=completed.GroupBy(x=>S(x,"Opponent")).OrderBy(g=>g.Key).Select(g=>new{team=g.Key,record=Record(g)}),note="적재된 경기 기준 순위·전적입니다. 비율 타이틀은 팀 경기수 × 3.1타석 / 1이닝 이상. 주전은 포지션별 최다 수비 이닝(지명타자는 타석) 기준이며 동일 선수가 여러 포지션에 표시될 수 있습니다."};
    }
}
