using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

namespace NaverSabermetrics.Web;

public sealed record PlayerWebRequest
{
    public string Code { get; init; } = "";
    public string Section { get; init; } = "profile";
    public string Role { get; init; } = "batter";
    public int? Year { get; init; }
    public string Competition { get; init; } = "정규시즌";
    public string View { get; init; } = "basic";
    public string? Opponent { get; init; }
    public string? Start { get; init; }
    public string? End { get; init; }
    public string Sort { get; init; } = "";
    public bool Descending { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public void Validate(SiteOptions options)
    {
        if (Code.Length is < 1 or > 40 || Code.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new RequestError("선수 코드가 잘못되었습니다.");
        if (Role is not ("batter" or "pitcher") || !new[]{"profile","summary","career","years","games","opponents","situations","plays","pitches","arsenal","direction","trend"}.Contains(Section))
            throw new RequestError("지원하지 않는 선수 페이지입니다.");
        if (Year is < 1900 or > 2200 || (Section is not ("profile" or "career" or "years" or "trend") && Year is null))
            throw new RequestError("연도를 선택하세요.");
        if (!new[]{"정규시즌","포스트시즌","시범경기","전체"}.Contains(Competition)) throw new RequestError("경기 구분이 잘못되었습니다.");
        if (Page < 1 || PageSize < 1 || PageSize > options.MaxPageSize || (long)(Page-1)*PageSize >= options.MaxAccessibleRows)
            throw new RequestError("열람 가능한 페이지를 초과했습니다.");
        if (Opponent?.Length > 20 || Sort.Length > 40) throw new RequestError("필터 값이 잘못되었습니다.");
        foreach (var d in new[]{Start,End})
            if (!string.IsNullOrEmpty(d) && !DateOnly.TryParseExact(d,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
                throw new RequestError("날짜 형식은 YYYY-MM-DD입니다.");
        if (!string.IsNullOrEmpty(Start) && !string.IsNullOrEmpty(End) && string.CompareOrdinal(Start,End)>0) throw new RequestError("날짜 범위가 잘못되었습니다.");
        if (Section is "summary" or "career" or "years") _ = ViewRegistry.Get(Role,View);
    }
}
public sealed record PlayerMetric(string Key,string Label,string Display,double? Value,double? Percentile,int Population,bool LowerIsBetter);
public sealed record PlayerTable(IReadOnlyList<string> Columns,IReadOnlyList<Dictionary<string,object?>> Rows,int Page,bool HasMore,string Note);

public sealed partial class RecordService
{
    // Use the same factories and formula/version-aware cache as the record rooms.
    public async Task<IReadOnlyList<PlayerMetric>> PlayerMetricsAsync(string code,string role,int? year,string competition,string view,bool calculatePercentiles,CancellationToken ct)
    {
        var def=ViewRegistry.Get(role,view);
        var r=new RecordRequest{Room="season",Role=role,Year=year,Competition=competition,View=view};
        var q=new GameQuery{SeasonYear=year,Competition=competition,Grouping=AnalyticsGrouping.PlayerCareer};
        var version=await _db.GetWebSourceVersionAsync(ct);
        var key=$"player-v1|{version}|{role}|{year}|{competition}|{view}";
        var rows=TryRead(key,def.RowType);
        if(rows is null){rows=await ComputeAsync(r,q,def,ct);Store(key,rows);}
        var pc=def.RowType.GetProperty("Pcode");
        var target=rows.FirstOrDefault(x=>Convert.ToString(pc?.GetValue(x))==code);
        if(target is null)return Array.Empty<PlayerMetric>();
        var allowed=role=="batter" ? new[]{"AVG","OBP","SLG","OPS","ISO","BABIP","wRC+","wOBA","HR","BB%","K%","WAR"}
            : new[]{"ERA","FIP","WHIP","K/9","BB/9","K%","BB%","K-BB%","피OPS","KBO fWAR","KBO fWAR v4"};
        var result=new List<PlayerMetric>();
        foreach(var p in ViewRegistry.Properties(def))
        {
            if(p.Name is "Rank" or "Name" || PublicHidden(p,role) || WarHidden(p))continue;
            var label=ViewRegistry.Label(p).Replace("*","");
            var raw=p.GetValue(target);
            double? value=raw is not null && ViewRegistry.IsNumber(p)?Convert.ToDouble(raw,CultureInfo.InvariantCulture):null;
            if(value.HasValue && !double.IsFinite(value.Value))value=null;
            var lower=role=="batter" ? label=="K%" : new[]{"ERA","FIP","WHIP","BB/9","BB%","피OPS"}.Contains(label);
            var population=new List<double>();
            if(calculatePercentiles && allowed.Contains(label))
                foreach(var row in rows)
                    if(p.GetValue(row) is object v){var n=Convert.ToDouble(v,CultureInfo.InvariantCulture);if(double.IsFinite(n))population.Add(n);}
            double? pct=value.HasValue && population.Count>=2
                ? 100.0*(population.Count(x=>lower?x>value.Value:x<value.Value)+0.5*population.Count(x=>Math.Abs(x-value.Value)<1e-10))/population.Count : null;
            result.Add(new(p.Name,label,ViewRegistry.Display(p,raw),value,pct,population.Count,lower));
        }
        return result;
    }
}

public sealed class PlayerWebService(DatabaseCacheService db,RecordService records,SiteOptions options,OfficialPlayerProfileService officialProfiles)
{
    private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    private static string Round(string competition)=>competition switch{"정규시즌"=>"LOWER(TRIM(g.RoundCode))='kbo_r'","시범경기"=>$"g.CompetitionType={(int)GameCompetitionType.Preseason}","포스트시즌"=>$"g.CompetitionType={(int)GameCompetitionType.Postseason}",_=>"1=1"};
    private static string Season(PlayerWebRequest r)=>Round(r.Competition)+" AND ($year IS NULL OR g.SeasonYear=$year) AND ($start IS NULL OR g.GameDate >= $start) AND ($end IS NULL OR g.GameDate < date($end,'+1 day'))";
    private async Task<List<Dictionary<string,object?>>> Sql(string sql,PlayerWebRequest r,CancellationToken ct,int? limit=null)
    {
        await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Cache=SqliteCacheMode.Private}.ToString());
        await c.OpenAsync(ct);
        await using var cmd=c.CreateCommand();cmd.CommandTimeout=options.QuerySeconds;cmd.CommandText=sql;
        cmd.Parameters.AddWithValue("$code",r.Code);cmd.Parameters.AddWithValue("$year",(object?)r.Year??DBNull.Value);
        cmd.Parameters.AddWithValue("$start",string.IsNullOrEmpty(r.Start)?DBNull.Value:r.Start);
        cmd.Parameters.AddWithValue("$end",string.IsNullOrEmpty(r.End)?DBNull.Value:r.End);
        cmd.Parameters.AddWithValue("$opponent",string.IsNullOrEmpty(r.Opponent)?DBNull.Value:r.Opponent);
        cmd.Parameters.AddWithValue("$limit",limit??r.PageSize+1);cmd.Parameters.AddWithValue("$offset",(r.Page-1)*r.PageSize);
        using var cancel=ct.Register(cmd.Cancel);
        var rows=new List<Dictionary<string,object?>>();
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();var row=new Dictionary<string,object?>();
            for(int i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
    public async Task<object> QueryAsync(PlayerWebRequest r,CancellationToken ct)
    {
        var profile=await db.GetPlayerAsync(r.Code,ct)??throw new RequestError("선수를 찾을 수 없습니다.",404,"PLAYER_NOT_FOUND");
        if(r.Section=="profile")
        {
            var seasons=await Sql($"SELECT g.SeasonYear Year, s.Role, GROUP_CONCAT(DISTINCT s.TeamCode) Team FROM (SELECT GameId,TeamCode,'batter' Role FROM BatterGameStats WHERE Pcode=$code UNION ALL SELECT GameId,TeamCode,'pitcher' Role FROM PitcherGameStats WHERE Pcode=$code) s JOIN Games g ON g.GameId=s.GameId WHERE {Round(r.Competition)} AND g.SeasonYear IS NOT NULL GROUP BY g.SeasonYear,s.Role ORDER BY Year DESC LIMIT 100",r,ct);
            var details=await Sql("SELECT s.BackNumber,s.Height,s.Weight,g.GameDate Date FROM (SELECT GameId,Pcode,BackNumber,Height,Weight FROM BattingGameLines UNION ALL SELECT GameId,Pcode,BackNumber,Height,Weight FROM PitchingGameLines) s JOIN Games g ON g.GameId=s.GameId WHERE s.Pcode=$code ORDER BY g.GameDate DESC,g.GameId DESC LIMIT 1",r,ct);
            var officialProfile=await officialProfiles.GetAsync(r.Code,ct);
            return new{profile,seasons,details=details.FirstOrDefault(),officialProfile,notes=new[]{"경기 자료의 신체정보·등번호는 마지막 수집 경기 기준입니다. KBO 공식 프로필은 별도 수집 시점의 정보이며 선택 시즌의 과거 정보와 다를 수 있습니다.","퍼센타일은 자체 DB 기준이며 외부 사이트의 평가값과 다를 수 있습니다."}};
        }
        if(r.Section=="career")
        {
            var metrics=await records.PlayerMetricsAsync(r.Code,r.Role,null,r.Competition,r.View,false,ct);
            return new{metrics};
        }
        if(r.Section=="arsenal")
        {
            var player=r.Role=="batter"?"p.BatterPcode":"p.PitcherPcode";
            var rows=await Sql($"WITH counts AS (SELECT COALESCE(NULLIF(p.PitchType,''),'미상') PitchType,COUNT(*) NP,AVG(p.SpeedKmh) Velocity FROM Pitches p JOIN Games g ON g.GameId=p.GameId WHERE {player}=$code AND {Season(r)} GROUP BY COALESCE(NULLIF(p.PitchType,''),'미상')) SELECT PitchType,NP,ROUND(100.0*NP/SUM(NP) OVER (),1) AS 'Usage%',ROUND(Velocity,1) AS 'km/h' FROM counts ORDER BY NP DESC,PitchType LIMIT $limit OFFSET $offset",r,ct);
            return Table(rows,r,"선택 시즌의 실제 투구 수 기준 구종 비중입니다. 구속이 없는 투구는 평균 구속에서 제외합니다. 타자는 상대 투수의 구종 분포입니다.");
        }
        if(r.Section=="summary")
        {
            var combined=new List<PlayerMetric>();
            foreach(var view in new[]{"basic","advanced","value"})
                combined.AddRange(await records.PlayerMetricsAsync(r.Code,r.Role,r.Year,r.Competition,view,true,ct));
            var metrics=combined.DistinctBy(m=>m.Label).OrderBy(m=>ViewRegistry.IsWarMetric(m.Label)?1:0).ToArray();
            return new{metrics,reference="선택 시즌·경기 구분에 기록이 있는 전체 타자 또는 전체 투수 기준입니다. 규정타석·규정이닝 제한 없이 지표별 유효값을 비교합니다. 동률은 중간 순위, 높을수록 우수. 비교군 2명 미만이면 표시하지 않습니다."};
        }
        if(r.Section=="years")
        {
            var source=r.Role=="batter"?"BatterGameStats":"PitcherGameStats";
            var years=await Sql($"SELECT DISTINCT g.SeasonYear Year FROM {source} s JOIN Games g ON g.GameId=s.GameId WHERE s.Pcode=$code AND {Round(r.Competition)} AND g.SeasonYear IS NOT NULL ORDER BY Year DESC LIMIT 51",r with{Year=null},ct);
            var rows=new List<Dictionary<string,object?>>();
            foreach(var year in years.Skip((r.Page-1)*5).Take(5))
            {
                var y=Convert.ToInt32(year["Year"]);var metrics=await records.PlayerMetricsAsync(r.Code,r.Role,y,r.Competition,r.View,false,ct);
                if(metrics.Count==0)continue;
                var row=new Dictionary<string,object?>{{"Year",y}};foreach(var m in metrics)row[m.Label]=m.Display;rows.Add(row);
            }
            return new PlayerTable(rows.FirstOrDefault()?.Keys.ToArray()??new[]{"Year"},rows,r.Page,years.Count>r.Page*5,"연도별 5시즌씩 표시합니다. 이적한 시즌은 선수 전체 기록으로 합산합니다. WAR는 사이트 자체 계산값입니다.");
        }
        if(r.Section is "games" or "trend")return await Games(r,ct);
        return await Events(r,ct);
    }
    private async Task<PlayerTable> Games(PlayerWebRequest r,CancellationToken ct)
    {
        var bat=r.Role=="batter";var source=bat?"BatterGameStats":"PitcherGameStats";
        var stats=bat?"s.PA,s.AB,s.H,s.Doubles AS '2B',s.Triples AS '3B',s.HR,s.Runs AS R,s.RBI,s.SB,s.CS,s.BB,s.HBP,s.SO,s.SF,s.SH,s.TB,s.WPA,s.Pitches AS NP"
            :"s.InningsOuts AS Outs,s.HitsAllowed AS H,s.HomeRunsAllowed AS HR,s.RunsAllowed AS R,s.EarnedRuns AS ER,s.FinalBB AS BB,s.FinalHBP AS HBP,s.FinalSO AS SO,s.FinalPitchCount AS NP,s.IsStarter AS GS";
        var opponent="CASE WHEN s.TeamCode=g.HomeTeamCode THEN g.AwayTeamCode ELSE g.HomeTeamCode END";
        if(r.Section=="trend")
        {
            var aggregate=bat?"SUM(s.PA) PA,SUM(s.AB) AB,SUM(s.H) H,SUM(s.HR) HR,SUM(s.BB) BB,SUM(s.HBP) HBP,SUM(s.SF) SF,SUM(s.TB) TB,SUM(s.SO) SO,SUM(s.RBI) RBI"
                :"SUM(s.InningsOuts) Outs,SUM(s.HitsAllowed) H,SUM(s.FinalBB) BB,SUM(s.EarnedRuns) ER,SUM(s.FinalSO) SO";
            var rows=await Sql($"SELECT g.SeasonYear Year,{aggregate} FROM {source} s JOIN Games g ON g.GameId=s.GameId WHERE s.Pcode=$code AND {Round(r.Competition)} {(bat?"":"AND s.HasFinalLine=1")} GROUP BY g.SeasonYear ORDER BY Year LIMIT 50",r,ct);
            foreach(var row in rows)Rates(row,bat);return Table(rows,r,"연도별 기본 기록 추이 · 비율은 합산 분자/분모로 계산합니다.",false);
        }
        var where=$"s.Pcode=$code AND {Season(r)} {(bat?"":"AND s.HasFinalLine=1")} AND ($opponent IS NULL OR {opponent}=$opponent)";
        var sql=$"SELECT substr(g.GameDate,1,10) Date,s.TeamCode Team,{opponent} Opponent,g.Stadium,CASE WHEN s.TeamCode=g.HomeTeamCode THEN '홈' ELSE '원정' END Venue,CASE WHEN g.HomeScore IS NULL OR g.AwayScore IS NULL THEN '-' WHEN g.HomeScore=g.AwayScore THEN 'D' WHEN (g.HomeScore>g.AwayScore AND s.TeamCode=g.HomeTeamCode) OR (g.AwayScore>g.HomeScore AND s.TeamCode=g.AwayTeamCode) THEN 'W' ELSE 'L' END Result,g.AwayScore||':'||g.HomeScore AS '원정:홈',{stats} FROM {source} s JOIN Games g ON g.GameId=s.GameId WHERE {where} ORDER BY g.GameDate DESC,g.GameId DESC LIMIT $limit OFFSET $offset";
        var data=await Sql(sql,r,ct);foreach(var row in data)Rates(row,bat);
        return Table(data,r,"날짜별 비율은 해당 경기 기록입니다. 최종 투구 기록이 없는 경기는 투수 기록에서 제외합니다.");
    }
    private static double N(Dictionary<string,object?> r,string key)=>r.TryGetValue(key,out var value)&&value is not null?Convert.ToDouble(value,Inv):0;
    private static object? Ratio(double n,double d,int digits=3)=>d>0?Math.Round(n/d,digits):null;
    private static void Rates(Dictionary<string,object?> row,bool bat)
    {
        if(bat){var ab=N(row,"AB");var denom=ab+N(row,"BB")+N(row,"HBP")+N(row,"SF");row["AVG"]=Ratio(N(row,"H"),ab);row["OBP"]=Ratio(N(row,"H")+N(row,"BB")+N(row,"HBP"),denom);row["SLG"]=Ratio(N(row,"TB"),ab);row["OPS"]=ab>0&&denom>0?Math.Round((N(row,"H")+N(row,"BB")+N(row,"HBP"))/denom+N(row,"TB")/ab,3):null;}
        else{var outs=(int)N(row,"Outs");row["IP"]=$"{outs/3}.{outs%3}";row["ERA"]=Ratio(N(row,"ER")*27,outs,2);row["WHIP"]=Ratio((N(row,"H")+N(row,"BB"))*3,outs,2);row.Remove("Outs");}
    }
    private PlayerTable Table(List<Dictionary<string,object?>> rows,PlayerWebRequest r,string note,bool paginate=true)
    {
        var more=paginate && rows.Count>r.PageSize && (long)r.Page*r.PageSize<options.MaxAccessibleRows;
        if(paginate)rows=rows.Take(Math.Min(r.PageSize,options.MaxAccessibleRows-(r.Page-1)*r.PageSize)).ToList();
        return new(rows.FirstOrDefault()?.Keys.ToArray()??Array.Empty<string>(),rows,r.Page,more,note);
    }
    private async Task<PlayerTable> Events(PlayerWebRequest r,CancellationToken ct)
    {
        var bat=r.Role=="batter";
        var player=bat?"pa.BatterPcode":"COALESCE(pa.FinalPitcherPcode,pa.PitcherPcode)";
        var opponent=bat?"pa.FieldingTeamCode":"pa.BattingTeamCode";
        var where=$"{player}=$code AND pa.IsOfficial=1 AND {Season(r)} AND ($opponent IS NULL OR {opponent}=$opponent)";
        var from="FROM PlateAppearances pa JOIN Games g ON g.GameId=pa.GameId";
        if(r.Section=="plays")
        {
            var order=r.Sort=="WPA"?"WPA":"Date";var dir=r.Descending?"DESC":"ASC";
            var sql=$"SELECT substr(g.GameDate,1,10) Date,{opponent} Opponent,pa.Inning||CASE WHEN pa.BattingSide=0 THEN '초' ELSE '말' END Inning,COALESCE(pa.FinalPitcherName,pa.PitcherName) Pitcher,pa.BatterName Batter,pa.BatOrder BatOrder,pa.ResultText Result,pa.ActualPitchCount NP,pa.BeforeOuts Outs,COALESCE(pa.BeforeFirstRunnerName,'-')||' / '||COALESCE(pa.BeforeSecondRunnerName,'-')||' / '||COALESCE(pa.BeforeThirdRunnerName,'-') Runners,pa.BeforeAwayScore||':'||pa.BeforeHomeScore BeforeScore,pa.AfterAwayScore||':'||pa.AfterHomeScore AfterScore,pa.RunsScored Runs,{(bat?"pa.WpaByPlate":"-pa.WpaByPlate")} WPA {from} WHERE {where} ORDER BY {order} {dir},g.GameId DESC,pa.SequenceNumber DESC LIMIT $limit OFFSET $offset";
            return Table(await Sql(sql,r,ct),r,"공식 타석만 표시합니다. 점수는 원정:홈, 주자는 1루/2루/3루 순서입니다. WPA는 원본 타석 WPA이며 투수 관점은 부호를 반전합니다. 날짜 또는 WPA로 정렬할 수 있습니다.");
        }
        string group,label,join="";
        switch(r.Section)
        {
            case "opponents":group=bat?"COALESCE(pa.FinalPitcherPcode,pa.PitcherPcode)":"pa.BatterPcode";label=bat?"COALESCE(pa.FinalPitcherName,pa.PitcherName,'미상')":"COALESCE(pa.BatterName,'미상')";break;
            case "situations":group=r.View switch{"inning"=>"CAST(pa.Inning AS TEXT)||'회'","outs"=>"COALESCE(CAST(pa.BeforeOuts AS TEXT),'미상')||'아웃'","venue"=>"CASE WHEN "+(bat?"pa.BattingTeamCode":"pa.FieldingTeamCode")+"=g.HomeTeamCode THEN '홈' ELSE '원정' END","score"=>"CASE WHEN pa.BeforeHomeScore IS NULL OR pa.BeforeAwayScore IS NULL THEN '미상' WHEN pa.BeforeHomeScore=pa.BeforeAwayScore THEN '동점' WHEN (pa.BattingSide=1 AND pa.BeforeHomeScore>pa.BeforeAwayScore) OR (pa.BattingSide=0 AND pa.BeforeAwayScore>pa.BeforeHomeScore) THEN '공격팀 리드' ELSE '공격팀 열세' END",_=>"CASE WHEN pa.BeforeSecondRunnerPcode IS NOT NULL OR pa.BeforeThirdRunnerPcode IS NOT NULL THEN '득점권' WHEN pa.BeforeFirstRunnerPcode IS NOT NULL THEN '1루' ELSE '주자 없음' END"};label=group;break;
            case "pitches":join=" LEFT JOIN Pitches p ON p.PitchEventId=(SELECT p2.PitchEventId FROM Pitches p2 WHERE p2.PlateAppearanceId=pa.PlateAppearanceId ORDER BY p2.ActualPitchIndex DESC,p2.PitchEventId DESC LIMIT 1)";group="COALESCE(NULLIF(p.PitchType,''),'미상')";label=group;break;
            case "direction":group="pa.FieldDirection";label="CAST(pa.FieldDirection AS TEXT)";where+=" AND pa.IsHit=1";break;
            default:throw new RequestError("지원하지 않는 탭입니다.");
        }
        var aggregate=$"COUNT(*) PA,SUM(pa.CountsAsAtBat) AB,SUM(pa.IsHit) H,SUM(CASE WHEN pa.TotalBases=2 THEN 1 ELSE 0 END) AS '2B',SUM(CASE WHEN pa.TotalBases=3 THEN 1 ELSE 0 END) AS '3B',SUM(CASE WHEN pa.TotalBases=4 THEN 1 ELSE 0 END) HR,SUM(CASE WHEN pa.IsWalk=1 OR pa.IsIntentionalWalk=1 THEN 1 ELSE 0 END) BB,SUM(CASE WHEN pa.ResultType={(int)BattingResultType.HitByPitch} THEN 1 ELSE 0 END) HBP,SUM(pa.IsStrikeout) SO,SUM(CASE WHEN pa.ResultType={(int)BattingResultType.SacrificeFly} THEN 1 ELSE 0 END) SF,SUM(pa.TotalBases) TB,SUM(pa.ActualPitchCount) NP";
        var expr=$"SELECT {label} Name,{aggregate} {from}{join} WHERE {where} GROUP BY {group}";
        var sortable=new[]{"PA","AB","H","HR","BB","SO","OPS","AVG","OBP","SLG","Name"};var sort=sortable.Contains(r.Sort)?r.Sort:"PA";
        var sql2=$"WITH totals AS ({expr}), ratios AS (SELECT *,1.0*H/NULLIF(AB,0) AVG,1.0*(H+BB+HBP)/NULLIF(AB+BB+HBP+SF,0) OBP,1.0*TB/NULLIF(AB,0) SLG FROM totals) SELECT *,OBP+SLG OPS FROM ratios ORDER BY {sort} {(r.Descending?"DESC":"ASC")},Name LIMIT $limit OFFSET $offset";
        var data=await Sql(sql2,r,ct);
        foreach(var row in data)
        {
            Rates(row,true);
            if(r.Section=="direction")row["Name"]=Convert.ToInt32(row["Name"]) switch{1=>"좌익",2=>"좌중간",3=>"중견",4=>"우중간",5=>"우익",6=>"투수",7=>"포수",8=>"1루",9=>"2루",10=>"3루",11=>"유격",_=>"방향 미상"};
        }
        return Table(data,r,r.Section=="pitches"?"구종은 타석 마지막 투구 기준입니다. AVG·OBP·SLG·OPS는 해당 구종으로 끝난 공식 타석의 결과이며, 투수는 피타격 기록입니다.":r.Section=="direction"?"안타 방향별 집계입니다. 정확한 착지 좌표가 없어 타구 점 위치는 표시하지 않습니다.":"공식 타석 결과 기준입니다. OBP 분모에 희생플라이(SF)를 포함하며, 투수는 피타격 기록입니다.");
    }
}
