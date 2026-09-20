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
        if(string.IsNullOrEmpty(Team) || Team.Length>20 || Team.Any(c=>!char.IsAsciiLetterOrDigit(c)) || new[]{"EA","WE"}.Contains(Team,StringComparer.OrdinalIgnoreCase))throw new RequestError("팀 코드가 잘못되었습니다.");
        if(Year is <1900 or >2200 || Page is <1 or >20)throw new RequestError("연도 또는 페이지가 잘못되었습니다.");
        if(!new[]{"정규시즌","포스트시즌","시범경기","전체"}.Contains(Competition) || !new[]{"overview","schedule","roster","scores"}.Contains(Section) || Role is not ("batter" or "pitcher"))throw new RequestError("지원하지 않는 팀 조회입니다.");
    }
}

public sealed class TeamWebService(DatabaseCacheService db,SiteOptions options)
{
    static double N(Dictionary<string,object?> r,string k)=>r.GetValueOrDefault(k) is object v?Convert.ToDouble(v,CultureInfo.InvariantCulture):0;
    static string S(Dictionary<string,object?> r,string k)=>Convert.ToString(r.GetValueOrDefault(k),CultureInfo.InvariantCulture)??"";
    static double? Rate(double n,double d)=>d>0?Math.Round(n/d,3):null;
    // "전체"는 라운드로 거르지 않지만, 경기일정 화면에만 쓰는 예정 경기 자리표시자
    // (RoundCode='kbo_scheduled')는 여기서도 제외합니다 — 그렇지 않으면 팀 페이지의
    // "최근 경기" 목록(날짜 내림차순)에 아직 열리지 않은 경기가 맨 위로 섞여 나옵니다.
    static string Round(string c)=>"UPPER(g.HomeTeamCode) NOT IN ('EA','WE') AND UPPER(g.AwayTeamCode) NOT IN ('EA','WE') AND "+(c switch{"정규시즌"=>"LOWER(TRIM(g.RoundCode))='kbo_r'","시범경기"=>$"g.CompetitionType={(int)GameCompetitionType.Preseason}","포스트시즌"=>$"g.CompetitionType={(int)GameCompetitionType.Postseason}",_=>$"LOWER(TRIM(COALESCE(g.RoundCode,'')))<>'{DatabaseCacheService.ScheduledPlaceholderRoundCode}'"});
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
    static bool Final(Dictionary<string,object?> g)=>(S(g,"Status").Equals("RESULT",StringComparison.OrdinalIgnoreCase)||S(g,"Status").Equals("ENDED",StringComparison.OrdinalIgnoreCase))&&g["RF"] is not null&&g["RA"] is not null;
    async Task<List<Dictionary<string,object?>>> Roster(TeamWebRequest r,CancellationToken ct)
    {
        var batter=r.Role=="batter";var table=batter?"BatterGameStats":"PitcherGameStats";
        var columns=batter?"SUM(s.PA) PA,SUM(s.AB) AB,SUM(s.H) H,SUM(s.HR) HR,SUM(s.Runs) R,SUM(s.RBI) RBI,SUM(s.SB) SB,SUM(s.BB) BB,SUM(s.HBP) HBP,SUM(s.SF) SF,SUM(s.TB) TB,SUM(s.SO) SO":
            "SUM(s.InningsOuts) Outs,SUM(s.EarnedRuns) ER,SUM(s.FinalSO) SO,SUM(s.FinalBB) BB,SUM(s.HitsAllowed) H,SUM(s.HomeRunsAllowed) HR,SUM(s.IsStarter) GS";
        var rows=await Sql($"SELECT s.Pcode Code,MAX(s.Name) Name,COUNT(DISTINCT s.GameId) G,{columns} FROM {table} s JOIN Games g ON g.GameId=s.GameId WHERE s.TeamCode=$team AND g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode) IN ('RESULT','ENDED') {(batter?"":"AND s.HasFinalLine=1")} GROUP BY s.Pcode ORDER BY {(batter?"PA":"Outs")} DESC,s.Pcode LIMIT 1000",r,ct);
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
            var innings=await Sql($"WITH ordered AS (SELECT g.GameId,g.HomeTeamCode,g.HomeScore,g.AwayScore,p.Inning,p.BeforeHomeScore,p.BeforeAwayScore,p.AfterHomeScore,p.AfterAwayScore,ROW_NUMBER() OVER(PARTITION BY g.GameId,p.Inning ORDER BY p.ChronologicalIndex) rn FROM Games g JOIN RelayGroups p ON p.GameId=g.GameId WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode) IN ('RESULT','ENDED') AND (g.HomeTeamCode=$team OR g.AwayTeamCode=$team) AND p.Inning BETWEEN 1 AND 30) SELECT GameId,Inning,CASE WHEN HomeTeamCode=$team THEN HomeScore ELSE AwayScore END RF,CASE WHEN HomeTeamCode=$team THEN AwayScore ELSE HomeScore END RA,CASE WHEN HomeTeamCode=$team THEN BeforeHomeScore-BeforeAwayScore ELSE BeforeAwayScore-BeforeHomeScore END Lead FROM ordered WHERE rn=1 AND BeforeHomeScore IS NOT NULL AND BeforeAwayScore IS NOT NULL AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL ORDER BY Inning,GameId",r,ct);
            var states=innings.GroupBy(x=>(inning:N(x,"Inning"),state:N(x,"Lead")>0?"리드":N(x,"Lead")<0?"열세":"동점")).OrderBy(g=>g.Key.inning).ThenBy(g=>g.Key.state).Select(g=>new{inning=g.Key.inning,state=g.Key.state,record=Record(g)});
            var scoring=await Sql($"SELECT p.GameId,p.Inning,p.BattingTeamCode Team,MIN(CASE WHEN p.BattingTeamCode=g.HomeTeamCode THEN p.BeforeHomeScore ELSE p.BeforeAwayScore END) StartScore,MAX(CASE WHEN p.BattingTeamCode=g.HomeTeamCode THEN p.AfterHomeScore ELSE p.AfterAwayScore END) EndScore FROM RelayGroups p JOIN Games g ON g.GameId=p.GameId WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode) IN ('RESULT','ENDED') AND (g.HomeTeamCode=$team OR g.AwayTeamCode=$team) AND p.BattingTeamCode IN (g.HomeTeamCode,g.AwayTeamCode) AND p.Inning BETWEEN 1 AND 30 GROUP BY p.GameId,p.Inning,p.BattingTeamCode",r,ct);
            var totals=scoring.Where(x=>x["StartScore"] is not null&&x["EndScore"] is not null&&N(x,"EndScore")>=N(x,"StartScore")).GroupBy(x=>(inning:N(x,"Inning"),side:S(x,"Team")==r.Team?"득점":"실점")).OrderBy(g=>g.Key.inning).ThenBy(g=>g.Key.side).Select(g=>new{inning=g.Key.inning,side=g.Key.side,games=g.Count(),runs=g.Sum(x=>N(x,"EndScore")-N(x,"StartScore")),average=Rate(g.Sum(x=>N(x,"EndScore")-N(x,"StartScore")),g.Count())});
            var inningDistribution=scoring.Where(x=>x["StartScore"] is not null&&x["EndScore"] is not null&&N(x,"EndScore")>=N(x,"StartScore")).GroupBy(x=>(inning:N(x,"Inning"),side:S(x,"Team")==r.Team?"득점":"실점")).OrderBy(g=>g.Key.inning).Select(g=>new{inning=g.Key.inning,side=g.Key.side,bins=Enumerable.Range(0,6).Select(n=>g.Count(x=>n==5?N(x,"EndScore")-N(x,"StartScore")>=5:N(x,"EndScore")-N(x,"StartScore")==n)).ToArray()});
            return new{scored=Distribution("RF"),allowed=Distribution("RA"),innings=totals,inningDistribution,states,note="종료 경기 기준 · 승률은 승/(승+패), 무승부 제외. 이닝 통계는 저장된 중계 점수로 복원하며 점수가 없는 이닝은 제외합니다."};
        }
        var standings=await Sql($"WITH sides AS (SELECT HomeTeamCode Team,HomeScore RF,AwayScore RA FROM Games g WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode) IN ('RESULT','ENDED') AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL UNION ALL SELECT AwayTeamCode,AwayScore,HomeScore FROM Games g WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode) IN ('RESULT','ENDED') AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL) SELECT Team,COUNT(*) G,SUM(RF>RA) W,SUM(RF=RA) D,SUM(RF<RA) L,1.0*SUM(RF>RA)/NULLIF(SUM(RF!=RA),0) PCT,SUM(RF) RF,SUM(RA) RA FROM sides WHERE Team IS NOT NULL GROUP BY Team ORDER BY PCT DESC,W DESC,Team LIMIT 40",r,ct);
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
        var mainPitcher=pitchers.FirstOrDefault(x=>N(x,"Outs")>0);
        if(mainPitcher is not null)field.Add(new{position="P",code=S(mainPitcher,"Code"),name=S(mainPitcher,"Name"),volume=N(mainPitcher,"Outs")/3,innings=S(mainPitcher,"IP")});
        // 포지션별로 최다 수비량 선수를 각각 독립적으로 뽑으면(예전 방식) 멀티포지션 선수가
        // 여러 포지션에 동시에 나올 수 있습니다. 그래서 선수별로 9개 포지션의 수비량을 한
        // 번에 모두 구한 뒤, (선수,포지션) 쌍을 수비량이 큰 순서로 정렬해 그리디하게
        // 배정합니다 — 이미 다른 포지션에 배정된 선수나 이미 채워진 포지션은 건너뛰므로
        // 한 선수는 정확히 한 포지션에만 나타납니다.
        var positionColumns=new[]{("C","CatcherInnings"),("1B","FirstBaseInnings"),("2B","SecondBaseInnings"),("3B","ThirdBaseInnings"),("SS","ShortstopInnings"),("LF","LeftFieldInnings"),("CF","CenterFieldInnings"),("RF","RightFieldInnings"),("DH","DhPa")};
        var volumeSelect=string.Join(",",positionColumns.Select(x=>$"SUM(s.{x.Item2}) \"{x.Item1}\""));
        var fieldRows=await Sql($"SELECT s.Pcode Code,MAX(s.Name) Name,{volumeSelect} FROM BatterGameStats s JOIN Games g ON g.GameId=s.GameId WHERE s.TeamCode=$team AND g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode) IN ('RESULT','ENDED') GROUP BY s.Pcode",r,ct);
        var fieldCandidates=new List<(string Pos,string Code,string Name,double Volume)>();
        foreach(var row in fieldRows)
            foreach(var (pos,_) in positionColumns)
            {
                var volume=N(row,pos);
                if(volume>0)fieldCandidates.Add((pos,S(row,"Code"),S(row,"Name"),volume));
            }
        var filled=new Dictionary<string,(string Code,string Name,double Volume)>();
        var usedPlayers=new HashSet<string>(StringComparer.Ordinal);
        foreach(var c in fieldCandidates.OrderByDescending(x=>x.Volume).ThenBy(x=>x.Pos,StringComparer.Ordinal).ThenBy(x=>x.Code,StringComparer.Ordinal))
        {
            if(filled.ContainsKey(c.Pos)||usedPlayers.Contains(c.Code))continue;
            filled[c.Pos]=(c.Code,c.Name,c.Volume);usedPlayers.Add(c.Code);
        }
        foreach(var (pos,_) in positionColumns)
            if(filled.TryGetValue(pos,out var f))field.Add(new{position=pos,code=f.Code,name=f.Name,volume=f.Volume});
        var latest=completed.FirstOrDefault();var line=new List<Dictionary<string,object?>>();
        if(latest is not null){var latestId=S(latest,"Id");var all=await Sql($"SELECT p.GameId,p.Inning,p.BattingTeamCode Team,MIN(CASE WHEN p.BattingTeamCode=g.HomeTeamCode THEN p.BeforeHomeScore ELSE p.BeforeAwayScore END) StartScore,MAX(CASE WHEN p.BattingTeamCode=g.HomeTeamCode THEN p.AfterHomeScore ELSE p.AfterAwayScore END) EndScore FROM RelayGroups p JOIN Games g ON g.GameId=p.GameId WHERE g.GameId=(SELECT g.GameId FROM Games g WHERE g.SeasonYear=$year AND {Round(r.Competition)} AND UPPER(g.StatusCode) IN ('RESULT','ENDED') AND (g.HomeTeamCode=$team OR g.AwayTeamCode=$team) AND g.HomeScore IS NOT NULL AND g.AwayScore IS NOT NULL ORDER BY g.GameDate DESC,g.GameId DESC LIMIT 1) AND p.Inning BETWEEN 1 AND 30 AND p.BattingTeamCode IN (g.HomeTeamCode,g.AwayTeamCode) GROUP BY p.GameId,p.Inning,p.BattingTeamCode ORDER BY p.Inning",r,ct);line=all;}
        return new{record=Record(completed),standings,leaders,field,latest,line,recent=games.Take(21),next=games.Where(x=>!Final(x)&&DateTime.TryParse(S(x,"Date"),out var d)&&d.Date>=DateTime.UtcNow.AddHours(9).Date).OrderBy(x=>S(x,"Date")).FirstOrDefault(),opponents=completed.GroupBy(x=>S(x,"Opponent")).OrderBy(g=>g.Key).Select(g=>new{team=g.Key,record=Record(g)}),note="적재된 경기 기준 순위·전적입니다. 비율 타이틀은 팀 경기수 × 3.1타석 / 1이닝 이상. 주전은 포지션별 수비량(이닝, 지명타자는 타석) 기준으로 배정하며, 한 선수는 가장 수비량이 많은 한 포지션에만 표시됩니다."};
    }
}
