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
    // 오늘(또는 마지막 종료일) 다음으로 예정 경기가 있는 날짜 하나를 찾아 그날의 경기 일정을
    // 반환합니다. RenderCollectorWorker가 채워 넣는 자리표시자 행(RoundCode='kbo_scheduled',
    // StatusCode='BEFORE')만 대상이며, 박스스코어가 없으므로 팀·시간·구장만 제공합니다.
    async Task<(string? Date,object[] Games)> UpcomingSchedule(SqliteConnection c,int year,string? after,CancellationToken ct)
    {
        await using var find=c.CreateCommand();find.CommandTimeout=options.QuerySeconds;
        find.CommandText="SELECT MIN(GameDate) FROM Games WHERE SeasonYear=$year AND LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_scheduled' AND UPPER(StatusCode)='BEFORE' AND ($after IS NULL OR GameDate>$after) AND UPPER(HomeTeamCode) NOT IN ('EA','WE') AND UPPER(AwayTeamCode) NOT IN ('EA','WE')";
        find.Parameters.AddWithValue("$year",year);find.Parameters.AddWithValue("$after",(object?)after??DBNull.Value);
        using var cancelFind=ct.Register(find.Cancel);
        var raw=await find.ExecuteScalarAsync(ct);
        if(raw is null or DBNull)return (null,[]);
        var date=Convert.ToString(raw)![..10];
        await using var cmd=c.CreateCommand();cmd.CommandTimeout=options.QuerySeconds;
        cmd.CommandText="SELECT GameId,Stadium,AwayTeamCode,HomeTeamCode,GameDateTime FROM Games WHERE SeasonYear=$year AND SUBSTR(GameDate,1,10)=$date AND LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_scheduled' AND UPPER(StatusCode)='BEFORE' ORDER BY GameDateTime,GameId";
        cmd.Parameters.AddWithValue("$year",year);cmd.Parameters.AddWithValue("$date",date);
        using var cancelList=ct.Register(cmd.Cancel);
        var rows=new List<object>();
        await using(var reader=await cmd.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct))
            // 익명 타입 속성은 ASP.NET Core 기본 JSON 옵션(camelCase 네이밍 정책)의 영향을 받으므로,
            // 프런트가 읽는 필드명과 맞추기 위해 소문자로 시작하는 camelCase로 씁니다(Dictionary 키를
            // 쓰는 LatestResults와 달리 이 메서드는 익명 타입을 쓰기 때문에 정책이 적용됩니다).
            rows.Add(new{gameId=reader.GetString(0),stadium=reader.IsDBNull(1)?null:reader.GetString(1),awayTeamCode=reader.GetString(2),homeTeamCode=reader.GetString(3),gameDateTime=reader.IsDBNull(4)?null:reader.GetString(4)});
        return (date,rows.ToArray());
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
                var magicRanks=Enumerable.Range(1,9).Reverse().ToArray();
                // 잔여 맞대결 경기수는 "16경기-이미 치른 경기수" 같은 추정이 아니라, RenderCollectorWorker가
                // 채워 넣는 예정 경기 자리표시자 행(RoundCode='kbo_scheduled', StatusCode='BEFORE')을 그대로
                // 세어 구합니다. DB에 실제로 저장된 잔여 일정이라 우천취소·순연 반영분까지 정확합니다.
                // 이미 종료된 과거 시즌은 이런 행이 없으므로 자연히 0(맞대결 보정 없음)이 됩니다.
                var remainingMatches=new Dictionary<(string,string),int>();
                await using(var remCmd=c.CreateCommand()){remCmd.CommandTimeout=options.QuerySeconds;remCmd.CommandText="SELECT HomeTeamCode,AwayTeamCode FROM Games WHERE SeasonYear=$year AND LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_scheduled' AND UPPER(StatusCode)='BEFORE' AND UPPER(HomeTeamCode) NOT IN ('EA','WE') AND UPPER(AwayTeamCode) NOT IN ('EA','WE')";remCmd.Parameters.AddWithValue("$year",r.Year);using var cancelRem=ct.Register(remCmd.Cancel);
                    await using var remReader=await remCmd.ExecuteReaderAsync(ct);while(await remReader.ReadAsync(ct)){var ra=remReader.GetString(0);var rb=remReader.GetString(1);remainingMatches[(ra,rb)]=remainingMatches.GetValueOrDefault((ra,rb))+1;}
                }
                var remainingBetween=(Func<string,string,int>)((x,y)=>remainingMatches.GetValueOrDefault((x,y))+remainingMatches.GetValueOrDefault((y,x)));
                var magicMatrix=list.Length==10?BuildMagicMatrix(rows.Select(x=>(Team:x.team,Wins:x.w,Remaining:x.remaining,Rank:x.rank)).ToArray(),magicRanks,remainingBetween):null;
                var latestGames=await LatestResults(c,r.Year,last,ct);
                var (upcomingDate,upcomingGames)=await UpcomingSchedule(c,r.Year,last,ct);
                var monthlyRows=teams.Keys.Select(code=>monthly.GetValueOrDefault(code)??new ForecastTeam(code,0,0,0,0,0)).Select(t=>new{team=t.Code,w=t.W,d=t.D,l=t.L,pct=t.Pct,rank=t.Pct is null?(int?)null:1+monthly.Values.Count(x=>(x.Pct??-1)>t.Pct.Value)}).OrderBy(x=>x.rank??int.MaxValue).ThenByDescending(x=>x.w).ThenBy(x=>x.team).ToArray();
                result=new{rows,latestGames,upcomingDate,upcomingGames,monthlyRows,month,asOf=last,forecastAvailable=odds is not null,forecastDetails,reason,simulations=PlayoffModel.Trials,exponent=1.83,forecastModel=PlayoffModel.CalibrationVersion,forecastParameters=PlayoffModel.SelectedParameters,magicMatrix,magicRanks,magicNote="확보/불가 여부는 9개 팀 전체의 잔여 일정을 동시에 고려하는 최대유량 기반 완전 탈락·확정 계산(Baseball Elimination Problem)으로 정확히 판정합니다. 매직넘버·트래직넘버 숫자 자체는 (경쟁팀 최대승수-내승수+1) 또는 (내최대승수-기준팀승수+1)에서 두 팀 사이 남은 직접 맞대결 경기수×2를 뺀 값으로, 가장 위협적인 경쟁팀 하나를 기준으로 한 근사치입니다(맞대결 승리 1회는 내 승수 +1과 상대 최대승수 -1을 동시에 만족시키는 이중 효과가 있어 반영). 잔여 맞대결 경기수는 DB에 저장된 실제 예정 경기 일정을 그대로 집계한 값입니다. 10개 팀이 모두 있을 때만 제공합니다. 동률 순위·타이브레이커는 아직 반영하지 않았습니다.",note="표의 피타고리안 승률은 득점^1.83 / (득점^1.83 + 실점^1.83), 예상승은 무승부 제외 경기수 기준입니다. PS 진출 추정에는 상대 수준 보정(강도 0.5)과 경기 수에 따른 평균 회귀(강도 G/(G+40))를 추가합니다. 홈 이점 보정도 구현했으나 과거 검증에서 선택된 계수는 0입니다. 2020~2023년으로 보정값을 학습하고 2024년으로 모델을 선택한 뒤, 값을 고정해 2025년의 경기별 예측과 진출확률을 별도로 평가했습니다. 현재 전적은 유지하고 KBO 공식 2026 홈·원정 배정에서 저장된 종료 경기를 뺀 대진을 10,000회 시뮬레이션합니다. 최종 승률 5위 경계 동률은 남은 자리를 균등 배분합니다. 향후 무승부·순위 결정전·부상·선발 변화는 반영하지 않습니다. 과거 6시즌을 이용한 초기 검증이며 공식 확률이 아닙니다. DB 누락은 잔여 경기로 간주되므로 완전한 시즌 데이터가 필요합니다."};
            }
            if(cache.Count>=8)cache.Clear();cache[key]=result;return result;
        }finally{mutex.Release();}
    }

    // 매직/트래직 넘버: (팀,목표순위)마다 그 순위 이내 확정(또는 이탈 확정)에 필요한
    // "내 승리+상대 패배" 조합 수를 계산합니다. 순위는 승률 기준이지만 통상적인 매직넘버 표기
    // 관례를 따라 승수 기준(잔여경기를 전부 승리로 가정한 최대 승수)으로 근사합니다.
    //
    // 팀간 잔여 맞대결(head-to-head)은 반영합니다: 남은 일정에서 두 팀이 직접 맞붙는 경기는,
    // 그 경기를 이기면 "내 승수 +1"과 "상대가 도달 가능한 최대승수 -1"을 동시에 만족시키는
    // 이중 효과가 있으므로(그 경기를 상대가 이길 수 없어졌으므로), 두 팀 사이 잔여 맞대결
    // 경기수 remainingHeadToHead(a,b) × 2 를 원래 매직/트래직 넘버에서 뺍니다.
    //
    // 화면에 찍히는 숫자(magic/tragic 값) 자체는 위 방식대로 "가장 위협적인 경쟁팀 하나"만 보는
    // 근사치입니다(정확히 K팀 이내 확정에는 K-1개 팀을 동시에 따돌려야 하는데, 그 조합까지 정확히
    // 반영한 "숫자"는 일반적으로 잘 정의되지 않습니다 — 실제 매직넘버 관례도 대개 이 수준입니다).
    //
    // 다만 "확보(secured)"/"불가(eliminated)" 여부, 즉 이미 수학적으로 결론이 난 칸인지는 근사가
    // 아니라 정확히 계산합니다. 9개 팀 전체의 잔여 일정을 동시에 고려하는 최대유량 기반 완전
    // 탈락/확정 판정(이른바 Baseball Elimination Problem을 상위 K위 확정까지 일반화한 버전)을
    // CanReachRank/CanBeCaught로 구현해 각 칸의 state를 이걸로 덮어씁니다. 근사치 숫자만으로는
    // "이미 확정됐는데 아직 매직넘버가 남은 것처럼" 보이거나 반대로 "아직 안 끝났는데 확보로"
    // 잘못 표시되는 경우가 있었는데(단일 경쟁팀만 보다 보니, 약한 여러 팀이 동시에 따라붙는
    // 경우를 놓침), 이 부분을 정확한 계산으로 대체한 것입니다. 동률 타이브레이커까지는 아직
    // 반영하지 않았습니다.
    private static object[] BuildMagicMatrix(IReadOnlyList<(string Team,int Wins,int Remaining,int Rank)> standings,int[] ranks,Func<string,string,int> remainingHeadToHead)
    {
        var byPosition=standings.OrderBy(x=>x.Rank).ThenByDescending(x=>x.Wins).ThenBy(x=>x.Team,StringComparer.Ordinal).ToArray();
        var winsByTeam=standings.ToDictionary(x=>x.Team,x=>x.Wins);
        var allTeams=standings.Select(x=>x.Team).ToArray();
        return standings.Select(t=>new{
            team=t.Team,
            cells=ranks.Select(rank=>{
                var others=allTeams.Where(x=>x!=t.Team).ToArray();
                if(t.Rank<=rank)
                {
                    var chasers=byPosition.Skip(rank).ToArray();
                    if(chasers.Length==0)return new{rank,state="secured",value=(int?)null,ownRemaining=(int?)null};
                    var magic=chasers.Select(c=>c.Wins+c.Remaining-t.Wins+1-2*remainingHeadToHead(t.Team,c.Team)).Max();
                    var effectiveWins=others.ToDictionary(o=>o,o=>winsByTeam[o]+remainingHeadToHead(t.Team,o));
                    var secured=!CanBeCaught(t.Wins,rank,others,effectiveWins,remainingHeadToHead);
                    if(secured)return new{rank,state="secured",value=(int?)null,ownRemaining=(int?)null};
                    magic=Math.Max(magic,1);
                    if(magic>t.Remaining)return new{rank,state="needsHelp",value=(int?)magic,ownRemaining=(int?)t.Remaining};
                    return new{rank,state="magic",value=(int?)magic,ownRemaining=(int?)null};
                }
                if(rank-1>=byPosition.Length)return new{rank,state="none",value=(int?)null,ownRemaining=(int?)null};
                var target=byPosition[rank-1];
                var tragic=t.Wins+t.Remaining-target.Wins+1-2*remainingHeadToHead(t.Team,target.Team);
                var ceiling=t.Wins+t.Remaining;
                var eliminated=!CanReachRank(ceiling,rank,others,winsByTeam,remainingHeadToHead);
                if(eliminated)return new{rank,state="eliminated",value=(int?)null,ownRemaining=(int?)null};
                tragic=Math.Max(tragic,1);
                return new{rank,state="tragic",value=(int?)tragic,ownRemaining=(int?)null};
            }).ToArray()
        }).Cast<object>().ToArray();
    }

    // ---- 일반화된 Baseball Elimination Problem (최대유량 기반) ----
    //
    // CanReachRank: 어떤 팀이 자기 잔여경기를 전부 이긴다고 가정했을 때(최대 승수=ceiling),
    // 나머지 팀들끼리의 잔여경기 결과를 어떻게 조합하더라도 그 중 (targetRank-1)팀 이하만
    // ceiling을 넘어서게 만들 수 있는지(=목표 순위 이내 도달이 가능한지)를 검사합니다.
    // 불가능하면(false) 그 팀은 targetRank 이내 진입이 수학적으로 불가능(탈락)합니다.
    //
    // CanBeCaught: 반대로 어떤 팀이 잔여경기를 전부 진다고 가정했을 때(최소 승수=floor),
    // 나머지 팀 중 targetRank팀 이상이 동시에 floor를 넘어서는 조합이 존재할 수 있는지를
    // 검사합니다. 불가능하면(false) 그 팀은 이미 targetRank 이내를 확정(secured)한 것입니다.
    //
    // 두 함수 모두 "어느 팀들을 무제한으로 둘지"를 완전탐색(팀 수가 9개뿐이라 최악의 경우도
    // 최대 C(9,4)=126가지)하면서, 각 경우에 대해 잔여경기를 실제로 배분 가능한지를 최대유량으로
    // 확인하는 방식입니다(고전적인 단일 1위 탈락 판정 알고리즘을 상위 K위 확정까지 확장한 것).
    private static bool CanReachRank(int ceiling,int targetRank,IReadOnlyList<string> others,IReadOnlyDictionary<string,int> otherWins,Func<string,string,int> remainingBetween)
    {
        var caps=new Dictionary<string,int>();var contestable=new List<string>();var alreadyExceed=0;
        foreach(var o in others){var c=ceiling-otherWins[o];if(c<0)alreadyExceed++;else{caps[o]=c;contestable.Add(o);}}
        var leeway=(targetRank-1)-alreadyExceed;
        if(leeway<0)return false;
        if(leeway>=contestable.Count)return true;
        var pairs=new List<(string A,string B,int Games)>();var total=0;
        for(var i=0;i<others.Count;i++)for(var j=i+1;j<others.Count;j++){var g=remainingBetween(others[i],others[j]);if(g>0){pairs.Add((others[i],others[j],g));total+=g;}}
        if(total==0)return true;
        var idxOf=new Dictionary<string,int>();for(var i=0;i<others.Count;i++)idxOf[others[i]]=i;
        const int Big=1_000_000;
        foreach(var combo in Combinations(contestable.Count,leeway))
        {
            var unlimited=new HashSet<string>(combo.Select(i=>contestable[i]));
            var n=1+pairs.Count+others.Count+1;var sink=n-1;var cap=new int[n,n];
            for(var i=0;i<pairs.Count;i++){var node=1+i;cap[0,node]=pairs[i].Games;cap[node,1+pairs.Count+idxOf[pairs[i].A]]+=Big;cap[node,1+pairs.Count+idxOf[pairs[i].B]]+=Big;}
            foreach(var o in others){var ti=1+pairs.Count+idxOf[o];cap[ti,sink]=unlimited.Contains(o)?Big:caps.GetValueOrDefault(o,Big);}
            if(MaxFlow(n,cap,0,sink)==total)return true;
        }
        return false;
    }

    private static bool CanBeCaught(int floor,int targetRank,IReadOnlyList<string> others,IReadOnlyDictionary<string,int> effectiveWins,Func<string,string,int> remainingBetween)
    {
        var alreadyExceed=others.Count(o=>effectiveWins[o]>floor);
        if(alreadyExceed>=targetRank)return true;
        var need=targetRank-alreadyExceed;
        var contestable=others.Where(o=>effectiveWins[o]<=floor).ToList();
        if(need>contestable.Count)return false;
        var idxOf=new Dictionary<string,int>();for(var i=0;i<others.Count;i++)idxOf[others[i]]=i;
        var pairs=new List<(string A,string B,int Games)>();var total=0;
        for(var i=0;i<others.Count;i++)for(var j=i+1;j<others.Count;j++){var g=remainingBetween(others[i],others[j]);if(g>0){pairs.Add((others[i],others[j],g));total+=g;}}
        if(total==0)return false;
        const int Big=1_000_000;
        foreach(var combo in Combinations(contestable.Count,need))
        {
            var chosen=combo.Select(i=>contestable[i]).ToList();
            var demand=chosen.ToDictionary(o=>o,o=>floor-effectiveWins[o]+1);
            var team=1+pairs.Count;var origSink=team+others.Count;var ss=origSink+1;var tt=ss+1;var n=tt+1;
            var cap=new int[n,n];var excess=new int[n];
            void AddEdge(int u,int v,int lo,int hi){cap[u,v]+=hi-lo;excess[v]+=lo;excess[u]-=lo;}
            for(var i=0;i<pairs.Count;i++){var node=1+i;var g=pairs[i].Games;AddEdge(0,node,g,g);AddEdge(node,team+idxOf[pairs[i].A],0,g);AddEdge(node,team+idxOf[pairs[i].B],0,g);}
            foreach(var o in others){var ti=team+idxOf[o];var lo=demand.GetValueOrDefault(o,0);AddEdge(ti,origSink,lo,Big);}
            AddEdge(origSink,0,0,Big);
            var totalLower=0;
            for(var v=0;v<n;v++){if(excess[v]>0){cap[ss,v]+=excess[v];totalLower+=excess[v];}else if(excess[v]<0)cap[v,tt]+=-excess[v];}
            if(MaxFlow(n,cap,ss,tt)==totalLower)return true;
        }
        return false;
    }

    private static int MaxFlow(int n,int[,] cap,int s,int t)
    {
        var flow=new int[n,n];var total=0;
        while(true)
        {
            var parent=new int[n];Array.Fill(parent,-1);parent[s]=s;
            var queue=new Queue<int>();queue.Enqueue(s);
            while(queue.Count>0){var u=queue.Dequeue();for(var v=0;v<n;v++)if(parent[v]==-1&&cap[u,v]-flow[u,v]>0){parent[v]=u;queue.Enqueue(v);}}
            if(parent[t]==-1)break;
            var aug=int.MaxValue;for(var v=t;v!=s;v=parent[v])aug=Math.Min(aug,cap[parent[v],v]-flow[parent[v],v]);
            for(var v=t;v!=s;v=parent[v]){flow[parent[v],v]+=aug;flow[v,parent[v]]-=aug;}
            total+=aug;
        }
        return total;
    }

    // n개 중 k개를 고르는 조합을 사전순으로 나열합니다(n<=9라 최악의 경우도 최대 126가지).
    private static IEnumerable<int[]> Combinations(int n,int k)
    {
        if(k<0||k>n)yield break;
        var idx=new int[k];for(var i=0;i<k;i++)idx[i]=i;
        while(true)
        {
            yield return (int[])idx.Clone();
            var pos=k-1;while(pos>=0&&idx[pos]==n-k+pos)pos--;
            if(pos<0)yield break;
            idx[pos]++;for(var j=pos+1;j<k;j++)idx[j]=idx[pos]+(j-pos);
        }
    }
}
