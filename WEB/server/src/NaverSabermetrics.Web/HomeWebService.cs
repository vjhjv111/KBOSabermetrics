using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverSabermetrics.Web;

public sealed record HomeRequest(int Year,string Section="standings")
{
    public void Validate(){if(Year is <1900 or >2200 || Section is not ("standings" or "leaders"))throw new RequestError("홈 조회 조건이 잘못되었습니다.");}
}
public sealed record HomePlayer(string Code,string Name,string Team,string Role,string Metric,string Display,double Value);
public sealed partial class RecordService
{
    public async Task<object> HomeLeadersAsync(int year,int games,CancellationToken ct)
    {
        var leaders=new List<object>();var war=new List<HomePlayer>();
        foreach(var role in new[]{"batter","pitcher"})
        {
            var found=new HashSet<string>();
            foreach(var view in new[]{"basic","advanced","value"})
            {
                var def=ViewRegistry.Get(role,view);var version=await _db.GetWebSourceVersionAsync(ct);var key=$"player-v1|{version}|{role}|{year}|정규시즌|{view}";
                var rows=TryRead(key,def.RowType);if(rows is null){rows=await ComputeAsync(new RecordRequest{Room="season",Role=role,Year=year,Competition="정규시즌",View=view},new GameQuery{SeasonYear=year,Competition="정규시즌",Grouping=AnalyticsGrouping.PlayerCareer},def,ct);Store(key,rows);}
                var props=ViewRegistry.Properties(def);var code=def.RowType.GetProperty("Pcode");var name=def.RowType.GetProperty("Name");var team=def.RowType.GetProperty("TeamCode");
                foreach(var p in props)
                {
                    var label=ViewRegistry.Label(p).Replace("*","");var warMetric=label=="WAR";
                    var allowed=role=="batter"?new[]{"AVG","OBP","SLG","OPS","HR","RBI","SB"}:new[]{"ERA","WHIP","FIP","SO","K/9","BB/9"};
                    if((!warMetric&&!allowed.Contains(label))||!ViewRegistry.IsNumber(p)||WarHidden(p)||PublicHidden(p,role)||!found.Add(label))continue;
                    var rate=label is "AVG" or "OBP" or "SLG" or "OPS" or "ERA" or "WHIP" or "FIP" or "K/9" or "BB/9";
                    var volume=props.FirstOrDefault(x=>ViewRegistry.Label(x).Replace("*","")==(role=="batter"?"PA":"IP"));
                    var ranked=new List<HomePlayer>();
                    foreach(var row in rows)
                    {
                        if(p.GetValue(row) is not object raw)continue;var value=Convert.ToDouble(raw,CultureInfo.InvariantCulture);if(!double.IsFinite(value))continue;
                        if(rate&&(games==0||volume is null||Convert.ToDouble(volume.GetValue(row)??0,CultureInfo.InvariantCulture)<games*(role=="batter"?3.1:1)))continue;
                        ranked.Add(new(Convert.ToString(code?.GetValue(row))??"",Convert.ToString(name?.GetValue(row))??"",Convert.ToString(team?.GetValue(row))??"",role,label,ViewRegistry.Display(p,raw),value));
                    }
                    var sorted=(role=="pitcher"&&label is "ERA" or "WHIP" or "FIP" or "BB/9"?ranked.OrderBy(x=>x.Value):ranked.OrderByDescending(x=>x.Value)).ThenBy(x=>x.Code,StringComparer.Ordinal);
                    if(warMetric)war.AddRange(sorted.Take(10));else leaders.Add(new{role,metric=label,players=sorted.Take(3).ToArray()});
                }
            }
        }
        return new{war=war.OrderByDescending(x=>x.Value).ThenBy(x=>x.Code,StringComparer.Ordinal).Take(10).ToArray(),leaders,note="정규시즌 적재 기록 기준. 비율 지표는 최다 팀 경기수 × 3.1타석 / 1이닝 이상. WAR는 사이트 자체 계산값이며 투수는 팬그래프 공식(고정 대체수준, FIP 단독) 기준입니다."};
    }
}

public sealed class HomeWebService(DatabaseCacheService db,RecordService records,SiteOptions options)
{
    async Task<object[]> LatestResults(SqliteConnection c,int year,string? date,CancellationToken ct)
    {
        if(string.IsNullOrEmpty(date))return [];
        await using var cmd=c.CreateCommand();cmd.CommandTimeout=options.QuerySeconds;
        cmd.CommandText="SELECT GameId,Stadium,AwayTeamCode,HomeTeamCode,AwayScore,HomeScore,AwayHits,HomeHits,AwayErrors,HomeErrors FROM Games WHERE SeasonYear=$year AND SUBSTR(GameDate,1,10)=$date AND LOWER(TRIM(RoundCode))='kbo_r' AND UPPER(StatusCode) IN ('RESULT','ENDED') AND AwayScore IS NOT NULL AND HomeScore IS NOT NULL AND UPPER(AwayTeamCode) NOT IN ('EA','WE') AND UPPER(HomeTeamCode) NOT IN ('EA','WE') ORDER BY GameDateTime,GameId";
        cmd.Parameters.AddWithValue("$year",year);cmd.Parameters.AddWithValue("$date",date[..Math.Min(10,date.Length)]);using var cancel=ct.Register(cmd.Cancel);
        var games=new List<Dictionary<string,object?>>();
        await using(var reader=await cmd.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct)){
            var row=new Dictionary<string,object?>();for(int i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);games.Add(row);
        }
        foreach(var game in games){
            game["decisions"]=await GameWebService.Decisions(c,Convert.ToString(game["GameId"])!,ct);
        }
        return games.Cast<object>().ToArray();
    }
    readonly Dictionary<string,object> cache=new();
    readonly SemaphoreSlim mutex=new(1,1);
    public async Task<object> QueryAsync(HomeRequest r,CancellationToken ct)
    {
        r.Validate();var version=await db.GetWebSourceVersionAsync(ct);var key=$"{version}|{r.Year}|{r.Section}";
        await mutex.WaitAsync(ct);try{
            if(cache.TryGetValue(key,out var hit))return hit;
            var teams=new Dictionary<string,ForecastTeam>();var monthly=new Dictionary<string,ForecastTeam>();string? month=null;var homeMatches=new Dictionary<(string,string),int>();string? last=null;
            await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync(ct);
            await using var cmd=c.CreateCommand();cmd.CommandTimeout=options.QuerySeconds;cmd.CommandText="SELECT HomeTeamCode,AwayTeamCode,HomeScore,AwayScore,GameDate FROM Games WHERE SeasonYear=$year AND LOWER(TRIM(RoundCode))='kbo_r' AND UPPER(StatusCode) IN ('RESULT','ENDED') AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL AND UPPER(HomeTeamCode) NOT IN ('EA','WE') AND UPPER(AwayTeamCode) NOT IN ('EA','WE') ORDER BY GameDate,GameId";cmd.Parameters.AddWithValue("$year",r.Year);using var cancel=ct.Register(cmd.Cancel);
            await using(var reader=await cmd.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct)){
                var a=reader.GetString(0);var b=reader.GetString(1);var ra=reader.GetInt32(2);var rb=reader.GetInt32(3);if(ra<0||rb<0)continue;last=reader.IsDBNull(4)?last:reader.GetString(4);
                var gameMonth=reader.IsDBNull(4)?null:reader.GetString(4)[..7];
                if(gameMonth!=month){monthly.Clear();month=gameMonth;}
                if(gameMonth is not null)foreach(var (t,rf,runs) in new[]{(a,ra,rb),(b,rb,ra)}){var old=monthly.GetValueOrDefault(t)??new ForecastTeam(t,0,0,0,0,0);monthly[t]=old with{W=old.W+(rf>runs?1:0),D=old.D+(rf==runs?1:0),L=old.L+(rf<runs?1:0)};}
                foreach(var (t,rf,runs) in new[]{(a,ra,rb),(b,rb,ra)}){var old=teams.GetValueOrDefault(t)??new ForecastTeam(t,0,0,0,0,0);teams[t]=old with{W=old.W+(rf>runs?1:0),D=old.D+(rf==runs?1:0),L=old.L+(rf<runs?1:0),RF=old.RF+rf,RA=old.RA+runs};}
                homeMatches[(a,b)]=homeMatches.GetValueOrDefault((a,b))+1;
            }
            object result;
            if(r.Section=="leaders")result=await records.HomeLeadersAsync(r.Year,teams.Values.Select(x=>x.G).DefaultIfEmpty(0).Max(),ct);
            else{
                var list=teams.Values.OrderBy(x=>x.Code,StringComparer.Ordinal).ToArray();
                double[]? odds=null;ForecastDetail[] forecastDetails=[];var reason="";
                if(r.Year!=2026)reason="진출확률은 2026 시즌의 144경기·10팀 체제를 대상으로 제공합니다.";
                else if(list.Length!=10||list.Any(x=>x.G<20))reason="진출확률은 10개 팀 모두 20경기 이상 적재된 뒤 제공합니다.";
                else try{var playedHome=new int[list.Length,list.Length];for(int a=0;a<list.Length;a++)for(int b=0;b<list.Length;b++)playedHome[a,b]=homeMatches.GetValueOrDefault((list[a].Code,list[b].Code));var forecast=PlayoffModel.ComputeCalibrated(list,playedHome,ct);odds=forecast.Odds;forecastDetails=forecast.Teams;}catch(ArgumentException){reason="적재 전적과 2026 공식 홈·원정 대진 배정이 일치하지 않아 확률을 계산하지 않습니다.";}
                var leader=list.OrderByDescending(x=>x.Pct??-1).ThenByDescending(x=>x.W).FirstOrDefault();
                var rows=list.Select((t,i)=>new{team=t.Code,g=t.G,w=t.W,d=t.D,l=t.L,pct=t.Pct,rf=t.RF,ra=t.RA,rank=1+list.Count(x=>(x.Pct??-1)>(t.Pct??-1)),gb=leader is null?0:((leader.W-t.W)+(t.L-leader.L))/2.0,pyth=t.Pyth,pythWins=t.Pyth*(t.W+t.L),winDifference=t.W-t.Pyth*(t.W+t.L),remaining=Math.Max(0,144-t.G),playoff=odds?[i]}).OrderBy(x=>x.rank).ThenByDescending(x=>x.w).ThenBy(x=>x.team).ToArray();
                var latestGames=await LatestResults(c,r.Year,last,ct);
                var monthlyRows=teams.Keys.Select(code=>monthly.GetValueOrDefault(code)??new ForecastTeam(code,0,0,0,0,0)).Select(t=>new{team=t.Code,w=t.W,d=t.D,l=t.L,pct=t.Pct,rank=t.Pct is null?(int?)null:1+monthly.Values.Count(x=>(x.Pct??-1)>t.Pct.Value)}).OrderBy(x=>x.rank??int.MaxValue).ThenByDescending(x=>x.w).ThenBy(x=>x.team).ToArray();
                result=new{rows,latestGames,monthlyRows,month,asOf=last,forecastAvailable=odds is not null,forecastDetails,reason,simulations=PlayoffModel.Trials,exponent=1.83,forecastModel=PlayoffModel.CalibrationVersion,forecastParameters=PlayoffModel.SelectedParameters,note="표의 피타고리안 승률은 득점^1.83 / (득점^1.83 + 실점^1.83), 예상승은 무승부 제외 경기수 기준입니다. PS 진출 추정에는 상대 수준 보정(강도 0.5)과 경기 수에 따른 평균 회귀(강도 G/(G+40))를 추가합니다. 홈 이점 보정도 구현했으나 과거 검증에서 선택된 계수는 0입니다. 2020~2023년으로 보정값을 학습하고 2024년으로 모델을 선택한 뒤, 값을 고정해 2025년의 경기별 예측과 진출확률을 별도로 평가했습니다. 현재 전적은 유지하고 KBO 공식 2026 홈·원정 배정에서 저장된 종료 경기를 뺀 대진을 10,000회 시뮬레이션합니다. 최종 승률 5위 경계 동률은 남은 자리를 균등 배분합니다. 향후 무승부·순위 결정전·부상·선발 변화는 반영하지 않습니다. 과거 6시즌을 이용한 초기 검증이며 공식 확률이 아닙니다. DB 누락은 잔여 경기로 간주되므로 완전한 시즌 데이터가 필요합니다."};
            }
            if(cache.Count>=8)cache.Clear();cache[key]=result;return result;
        }finally{mutex.Release();}
    }
}
