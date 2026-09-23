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
            var teams=new Dictionary<string,ForecastTeam>();var monthly=new Dictionary<string,ForecastTeam>();string? month=null;var homeMatches=new Dictionary<(string,string),int>();var h2hWins=new Dictionary<(string,string),int>();var h2hRuns=new Dictionary<(string,string),int>();string? last=null;
            await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync(ct);
            await using var cmd=c.CreateCommand();cmd.CommandTimeout=options.QuerySeconds;cmd.CommandText="SELECT HomeTeamCode,AwayTeamCode,HomeScore,AwayScore,GameDate FROM Games WHERE SeasonYear=$year AND LOWER(TRIM(RoundCode))='kbo_r' AND UPPER(StatusCode) IN ('RESULT','ENDED') AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL AND UPPER(HomeTeamCode) NOT IN ('EA','WE') AND UPPER(AwayTeamCode) NOT IN ('EA','WE') ORDER BY GameDate,GameId";cmd.Parameters.AddWithValue("$year",r.Year);using var cancel=ct.Register(cmd.Cancel);
            await using(var reader=await cmd.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct)){
                var a=reader.GetString(0);var b=reader.GetString(1);var ra=reader.GetInt32(2);var rb=reader.GetInt32(3);if(ra<0||rb<0)continue;last=reader.IsDBNull(4)?last:reader.GetString(4);
                var gameMonth=reader.IsDBNull(4)?null:reader.GetString(4)[..7];
                if(gameMonth!=month){monthly.Clear();month=gameMonth;}
                if(gameMonth is not null)foreach(var (t,rf,runs) in new[]{(a,ra,rb),(b,rb,ra)}){var old=monthly.GetValueOrDefault(t)??new ForecastTeam(t,0,0,0,0,0);monthly[t]=old with{W=old.W+(rf>runs?1:0),D=old.D+(rf==runs?1:0),L=old.L+(rf<runs?1:0)};}
                foreach(var (t,rf,runs) in new[]{(a,ra,rb),(b,rb,ra)}){var old=teams.GetValueOrDefault(t)??new ForecastTeam(t,0,0,0,0,0);teams[t]=old with{W=old.W+(rf>runs?1:0),D=old.D+(rf==runs?1:0),L=old.L+(rf<runs?1:0),RF=old.RF+rf,RA=old.RA+runs};}
                homeMatches[(a,b)]=homeMatches.GetValueOrDefault((a,b))+1;
                h2hRuns[(a,b)]=h2hRuns.GetValueOrDefault((a,b))+ra;h2hRuns[(b,a)]=h2hRuns.GetValueOrDefault((b,a))+rb;
                if(ra>rb)h2hWins[(a,b)]=h2hWins.GetValueOrDefault((a,b))+1;else if(rb>ra)h2hWins[(b,a)]=h2hWins.GetValueOrDefault((b,a))+1;
            }
            object result;
            if(r.Section=="leaders")result=await records.HomeLeadersAsync(r.Year,teams.Values.Select(x=>x.G).DefaultIfEmpty(0).Max(),ct);
            else{
                var list=teams.Values.OrderBy(x=>x.Code,StringComparer.Ordinal).ToArray();
                double[]? odds=null;double[]? rank1=null;double[]? rank2=null;double[]? rank3=null;double[]? rank4=null;ForecastDetail[] forecastDetails=[];var reason="";
                if(r.Year!=2026)reason="진출확률은 2026 시즌의 144경기·10팀 체제를 대상으로 제공합니다.";
                else if(list.Length!=10||list.Any(x=>x.G<20))reason="진출확률은 10개 팀 모두 20경기 이상 적재된 뒤 제공합니다.";
                else try{var playedHome=new int[list.Length,list.Length];for(int a=0;a<list.Length;a++)for(int b=0;b<list.Length;b++)playedHome[a,b]=homeMatches.GetValueOrDefault((list[a].Code,list[b].Code));var forecast=PlayoffModel.ComputeCalibrated(list,playedHome,ct);odds=forecast.Odds;forecastDetails=forecast.Teams;rank1=forecast.Rank1;rank2=forecast.Rank2;rank3=forecast.Rank3;rank4=forecast.Rank4;}catch(ArgumentException){reason="적재 전적과 2026 공식 홈·원정 대진 배정이 일치하지 않아 확률을 계산하지 않습니다.";}
                var leader=list.OrderByDescending(x=>x.Pct??-1).ThenByDescending(x=>x.W).FirstOrDefault();
                var rows=list.Select((t,i)=>new{team=t.Code,g=t.G,w=t.W,d=t.D,l=t.L,pct=t.Pct,rf=t.RF,ra=t.RA,rank=1+list.Count(x=>(x.Pct??-1)>(t.Pct??-1)),gb=leader is null?0:((leader.W-t.W)+(t.L-leader.L))/2.0,pyth=t.Pyth,pythWins=t.Pyth*(t.W+t.L),winDifference=t.W-t.Pyth*(t.W+t.L),remaining=Math.Max(0,144-t.G),playoff=odds?[i],rank1=rank1?[i],rank2=rank2?[i],rank3=rank3?[i],rank4=rank4?[i]}).OrderBy(x=>x.rank).ThenByDescending(x=>x.w).ThenBy(x=>x.team).ToArray();
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
                // 승률 동률 시 순위를 가리는 상대전적(→ 상대 다득점) 판정. 남은 맞대결은 DB 예정 경기 수와
                // "16경기 - 치른 경기" 중 큰 값을 씁니다(미편성 순연 경기까지 보수적으로 포함).
                var remainingH2h=(Func<string,string,int>)((x,y)=>Math.Max(remainingBetween(x,y),16-homeMatches.GetValueOrDefault((x,y))-homeMatches.GetValueOrDefault((y,x))));
                var tieWinner=(Func<string,string,string?>)((x,y)=>{
                    int wx=h2hWins.GetValueOrDefault((x,y)),wy=h2hWins.GetValueOrDefault((y,x)),left=Math.Max(0,remainingH2h(x,y));
                    if(left==0){if(wx!=wy)return wx>wy?x:y;int rx=h2hRuns.GetValueOrDefault((x,y)),ry=h2hRuns.GetValueOrDefault((y,x));return rx>ry?x:ry>rx?y:null;}
                    return wx>wy+left?x:wy>wx+left?y:null;
                });
                var magicMatrix=list.Length==10?BuildMagicMatrix(rows.Select(x=>new MagicTeam(x.team,x.w,x.l,x.remaining)).ToArray(),magicRanks,tieWinner):null;
                var latestGames=await LatestResults(c,r.Year,last,ct);
                var (upcomingDate,upcomingGames)=await UpcomingSchedule(c,r.Year,last,ct);
                var monthlyRows=teams.Keys.Select(code=>monthly.GetValueOrDefault(code)??new ForecastTeam(code,0,0,0,0,0)).Select(t=>new{team=t.Code,w=t.W,d=t.D,l=t.L,pct=t.Pct,rank=t.Pct is null?(int?)null:1+monthly.Values.Count(x=>(x.Pct??-1)>t.Pct.Value)}).OrderBy(x=>x.rank??int.MaxValue).ThenByDescending(x=>x.w).ThenBy(x=>x.team).ToArray();
                result=new{rows,latestGames,upcomingDate,upcomingGames,monthlyRows,month,asOf=last,forecastAvailable=odds is not null,forecastDetails,reason,simulations=PlayoffModel.Trials,exponent=1.83,forecastModel=PlayoffModel.CalibrationVersion,forecastParameters=PlayoffModel.SelectedParameters,magicMatrix,magicRanks,magicNote="KBO 순위 기준인 승률(승 ÷ (승+패), 무승부 제외)로 계산합니다. 매직넘버는 다른 팀이 남은 경기를 모두 이겨도 해당 순위 이내가 확정되는 데 필요한 우리 팀 승리 수, 트래직넘버는 경쟁팀들이 우리 팀을 앞지르는 데 필요한 (경쟁팀 승리+우리 팀 패배) 수입니다. 잔여경기 무승부는 없다고 가정합니다. 승률 동률은 1위·5위는 순위결정전, 그 외 순위는 상대전적 → 상대 다득점으로 판정하며, 맞대결이 남아 있으면 상대전적이 이미 확정된 경우에만 반영합니다. 노란 칸(자력 확정 불가)은 남은 경기를 모두 이겨도 다른 팀 결과의 도움이 필요한 경우이며 잔여경기/매직넘버로 표시합니다. 10개 팀이 모두 있을 때만 제공합니다.",note="표의 피타고리안 승률은 득점^1.83 / (득점^1.83 + 실점^1.83), 예상승은 무승부 제외 경기수 기준입니다. PS 진출 추정에는 상대 수준 보정(강도 0.5)과 경기 수에 따른 평균 회귀(강도 G/(G+40))를 추가합니다. 홈 이점 보정도 구현했으나 과거 검증에서 선택된 계수는 0입니다. 2020~2023년으로 보정값을 학습하고 2024년으로 모델을 선택한 뒤, 값을 고정해 2025년의 경기별 예측과 진출확률을 별도로 평가했습니다. 현재 전적은 유지하고 KBO 공식 2026 홈·원정 배정에서 저장된 종료 경기를 뺀 대진을 10,000회 시뮬레이션합니다. 최종 승률 5위 경계 동률은 남은 자리를 균등 배분합니다. 코시 직행·플옵 직행·준플옵 직행·와카 홈은 같은 시뮬레이션에서 정규시즌 최종 순위가 각각 1위·2위·3위·4위로 확정될 확률입니다(1위 한국시리즈 직행, 2위 플레이오프 직행, 3위 준플레이오프 직행, 4위 와일드카드 결정전 홈 어드밴티지 기준; 순위 동률은 해당 순위 구간을 동률 팀 수만큼 균등 배분). 향후 무승부·순위 결정전·부상·선발 변화는 반영하지 않습니다. 과거 6시즌을 이용한 초기 검증이며 공식 확률이 아닙니다. DB 누락은 잔여 경기로 간주되므로 완전한 시즌 데이터가 필요합니다."};
            }
            if(cache.Count>=8)cache.Clear();cache[key]=result;return result;
        }finally{mutex.Release();}
    }

    // 매직/트래직 넘버 — KBO 순위 기준(승률 = 승 / (승 + 패), 무승부 제외)으로 계산합니다.
    // 승수만 비교하면 무승부 수가 다른 팀끼리(예: 77승 3무 vs 77승 5무) 순위가 틀리게 나오므로,
    // 모든 비교는 최종 승률 분수를 정수 교차곱으로 정확히 비교합니다. 잔여경기 동안 무승부는
    // 없다고 가정하므로 최종 승률의 분모는 (승 + 패 + 잔여경기)입니다.
    //
    // 매직넘버(k위): 경쟁팀이 모두 잔여경기를 전승한다고 가정할 때, 나를 앞설 수 있는 팀이
    // k-1팀 이하가 되는 데 필요한 "내 승리" 수(= 9개 팀의 최대 승률 중 k번째 값을 넘는 승수).
    // 트래직넘버(k위): 경쟁팀마다 "나를 앞지르는 데 필요한 (그 팀 승리 + 내 패배)의 최소 합"을
    // 구한 뒤 그중 k번째로 작은 값. 0이 되면 k위 이내 진입 불가입니다.
    //
    // 승률 동률 처리(KBO 규정): 1위·5위 동률은 순위결정전을 치르므로 동률만으로는 확보/탈락이
    // 아닙니다. 그 외 순위는 상대전적 → 상대 다득점 순으로 가리므로, 맞대결 시즌이 끝났으면
    // 실제 상대전적·다득점으로, 남은 맞대결이 있으면 남은 경기를 다 져도 앞서는(상대전적 확정)
    // 경우에만 그 팀이 동률에서 앞선다고 봅니다.
    //
    // 표시: 매직넘버가 내 잔여경기 이하이면 매직(초록), 트래직넘버가 내 잔여경기 이하이면
    // 트래직(분홍), 둘 다면 함께(split), 둘 다 아니면 자력 확정 불가(노랑)로 표시합니다.
    private sealed record MagicTeam(string Team,int W,int L,int Remaining)
    {
        public long Den=>W+L+Remaining;
    }
    private static int ComparePct(long w1,long d1,long w2,long d2)
    {
        if(d1<=0||d2<=0)return 0;
        var v=w1*d2-w2*d1;return v>0?1:v<0?-1:0;
    }
    private static object[] BuildMagicMatrix(IReadOnlyList<MagicTeam> standings,int[] ranks,Func<string,string,string?> tieWinner)
    {
        var teams=standings.ToArray();
        // w가 이만큼이면 최종 승률이 1을 넘어가므로 어떤 팀도 위협이 될 수 없습니다(무한 루프 방지 상한).
        static int Magic(MagicTeam a,MagicTeam[] others,int k,Func<string,string,string?> tieWinner)
        {
            for(var w=0;w<=a.Den+1;w++)
            {
                var threats=0;
                foreach(var j in others)
                {
                    var c=ComparePct(j.W+j.Remaining,j.Den,a.W+w,a.Den);
                    if(c>0||(c==0&&(k is 1 or 5||tieWinner(a.Team,j.Team)!=a.Team)))threats++;
                }
                if(threats<=k-1)return w;
            }
            return int.MaxValue;
        }
        static int Overtake(MagicTeam a,MagicTeam j,int k,Func<string,string,string?> tieWinner)
        {
            for(var x=0;x<=j.Remaining;x++)
                for(var y=0;y<=a.Remaining;y++)
                {
                    var c=ComparePct(j.W+x,j.Den,a.W+a.Remaining-y,a.Den);
                    if(c>0||(c==0&&k is not (1 or 5)&&tieWinner(a.Team,j.Team)==j.Team))return x+y;
                }
            return int.MaxValue;
        }
        return teams.Select(a=>{
            var others=teams.Where(x=>x.Team!=a.Team).ToArray();
            return new{
                team=a.Team,
                cells=ranks.Select(k=>{
                    var magic=Magic(a,others,k,tieWinner);
                    if(magic==0)return new{rank=k,state="secured",value=(int?)null,tragic=(int?)null,ownRemaining=(int?)null};
                    var tragic=others.Select(j=>Overtake(a,j,k,tieWinner)).OrderBy(x=>x).ElementAt(k-1);
                    if(tragic==0)return new{rank=k,state="eliminated",value=(int?)null,tragic=(int?)null,ownRemaining=(int?)null};
                    var showMagic=magic<=a.Remaining;var showTragic=tragic<=a.Remaining;
                    if(showMagic&&showTragic)return new{rank=k,state="split",value=(int?)magic,tragic=(int?)tragic,ownRemaining=(int?)a.Remaining};
                    if(showMagic)return new{rank=k,state="magic",value=(int?)magic,tragic=(int?)null,ownRemaining=(int?)null};
                    if(showTragic)return new{rank=k,state="tragic",value=(int?)tragic,tragic=(int?)null,ownRemaining=(int?)null};
                    return new{rank=k,state="needsHelp",value=(int?)magic,tragic=(int?)null,ownRemaining=(int?)a.Remaining};
                }).ToArray()
            };
        }).Cast<object>().ToArray();
    }
}
