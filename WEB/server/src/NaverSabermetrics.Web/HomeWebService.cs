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
                    var label=ViewRegistry.Label(p).Replace("*","");var warMetric=role=="batter"?label=="WAR":label=="KBO fWAR";
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
        return new{war=war.OrderByDescending(x=>x.Value).ThenBy(x=>x.Code,StringComparer.Ordinal).Take(10).ToArray(),leaders,note="정규시즌 적재 기록 기준. 비율 지표는 최다 팀 경기수 × 3.1타석 / 1이닝 이상. WAR는 사이트 자체 계산값이며 투수는 KBO fWAR 기준입니다."};
    }
}

public sealed record ForecastTeam(string Code,int W,int D,int L,double RF,double RA)
{
    public int G=>W+D+L;
    public double? Pct=>W+L>0?(double)W/(W+L):null;
    public double? Pyth=>RF+RA>0?Math.Pow(RF,1.83)/(Math.Pow(RF,1.83)+Math.Pow(RA,1.83)):null;
}
public static class PlayoffModel
{
    public const int Trials=10000;
    public static double[] Simulate(IReadOnlyList<ForecastTeam> teams,int[,] played,CancellationToken ct)
    {
        if(teams.Count!=10||teams.Any(t=>t.G>144||t.D>=144||t.Pyth is null))throw new ArgumentException("Invalid season");
        var remaining=new List<(int A,int B,double P)>();
        for(int a=0;a<10;a++)for(int b=a+1;b<10;b++){
            if(played[a,b] is <0 or >16)throw new ArgumentException("Invalid matchup");
            var pa=Math.Clamp(teams[a].Pyth!.Value,0.000001,0.999999);var pb=Math.Clamp(teams[b].Pyth!.Value,0.000001,0.999999);var p=pa*(1-pb)/(pa*(1-pb)+pb*(1-pa));
            for(int g=played[a,b];g<16;g++)remaining.Add((a,b,p));
        }
        for(int a=0;a<10;a++){int count=0;for(int b=0;b<10;b++)count+=played[Math.Min(a,b),Math.Max(a,b)];if(count!=teams[a].G)throw new ArgumentException("Incomplete matchups");}
        var random=new Random(20260912);var qualified=new double[10];var wins=new int[10];
        for(int trial=0;trial<Trials;trial++){
            if(trial%100==0)ct.ThrowIfCancellationRequested();for(int i=0;i<10;i++)wins[i]=teams[i].W;
            foreach(var match in remaining)wins[random.NextDouble()<match.P?match.A:match.B]++;
            var values=Enumerable.Range(0,10).Select(i=>(Index:i,Pct:(double)wins[i]/(144-teams[i].D))).OrderByDescending(x=>x.Pct).ToArray();
            var cutoff=values[4].Pct;var above=values.Count(x=>x.Pct>cutoff+1e-12);var tied=values.Count(x=>Math.Abs(x.Pct-cutoff)<1e-12);
            foreach(var x in values)qualified[x.Index]+=x.Pct>cutoff+1e-12?1:Math.Abs(x.Pct-cutoff)<1e-12?(5.0-above)/tied:0;
        }
        return qualified.Select(x=>x/Trials).ToArray();
    }
}
public sealed class HomeWebService(DatabaseCacheService db,RecordService records,SiteOptions options)
{
    readonly Dictionary<string,object> cache=new();
    readonly SemaphoreSlim mutex=new(1,1);
    public async Task<object> QueryAsync(HomeRequest r,CancellationToken ct)
    {
        r.Validate();var version=await db.GetWebSourceVersionAsync(ct);var key=$"{version}|{r.Year}|{r.Section}";
        await mutex.WaitAsync(ct);try{
            if(cache.TryGetValue(key,out var hit))return hit;
            var teams=new Dictionary<string,ForecastTeam>();var matches=new Dictionary<(string,string),int>();string? last=null;
            await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync(ct);
            await using var cmd=c.CreateCommand();cmd.CommandTimeout=options.QuerySeconds;cmd.CommandText="SELECT HomeTeamCode,AwayTeamCode,HomeScore,AwayScore,GameDate FROM Games WHERE SeasonYear=$year AND LOWER(TRIM(RoundCode))='kbo_r' AND UPPER(StatusCode)='RESULT' AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL AND UPPER(HomeTeamCode) NOT IN ('EA','WE') AND UPPER(AwayTeamCode) NOT IN ('EA','WE') ORDER BY GameDate,GameId";cmd.Parameters.AddWithValue("$year",r.Year);using var cancel=ct.Register(cmd.Cancel);
            await using(var reader=await cmd.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct)){
                var a=reader.GetString(0);var b=reader.GetString(1);var ra=reader.GetInt32(2);var rb=reader.GetInt32(3);if(ra<0||rb<0)continue;last=reader.IsDBNull(4)?last:reader.GetString(4);
                foreach(var (t,rf,runs) in new[]{(a,ra,rb),(b,rb,ra)}){var old=teams.GetValueOrDefault(t)??new ForecastTeam(t,0,0,0,0,0);teams[t]=old with{W=old.W+(rf>runs?1:0),D=old.D+(rf==runs?1:0),L=old.L+(rf<runs?1:0),RF=old.RF+rf,RA=old.RA+runs};}
                var pair=string.CompareOrdinal(a,b)<0?(a,b):(b,a);matches[pair]=matches.GetValueOrDefault(pair)+1;
            }
            object result;
            if(r.Section=="leaders")result=await records.HomeLeadersAsync(r.Year,teams.Values.Select(x=>x.G).DefaultIfEmpty(0).Max(),ct);
            else{
                var list=teams.Values.OrderBy(x=>x.Code,StringComparer.Ordinal).ToArray();var played=new int[list.Length,list.Length];for(int a=0;a<list.Length;a++)for(int b=a+1;b<list.Length;b++)played[a,b]=matches.GetValueOrDefault((list[a].Code,list[b].Code));
                double[]? odds=null;var reason="";
                if(r.Year!=2026)reason="진출확률은 2026 시즌의 144경기·10팀 체제를 대상으로 제공합니다.";
                else if(list.Length!=10||list.Any(x=>x.G<20))reason="진출확률은 10개 팀 모두 20경기 이상 적재된 뒤 제공합니다.";
                else try{odds=PlayoffModel.Simulate(list,played,ct);}catch(ArgumentException){reason="적재 전적과 상대별 16경기 체제가 일치하지 않아 확률을 계산하지 않습니다.";}
                var leader=list.OrderByDescending(x=>x.Pct??-1).ThenByDescending(x=>x.W).FirstOrDefault();
                var rows=list.Select((t,i)=>new{team=t.Code,g=t.G,w=t.W,d=t.D,l=t.L,pct=t.Pct,rf=t.RF,ra=t.RA,rank=1+list.Count(x=>(x.Pct??-1)>(t.Pct??-1)),gb=leader is null?0:((leader.W-t.W)+(t.L-leader.L))/2.0,pyth=t.Pyth,pythWins=t.Pyth*(t.W+t.L),winDifference=t.W-t.Pyth*(t.W+t.L),remaining=Math.Max(0,144-t.G),playoff=odds?[i]}).OrderBy(x=>x.rank).ThenByDescending(x=>x.w).ThenBy(x=>x.team).ToArray();
                result=new{rows,asOf=last,forecastAvailable=odds is not null,reason,simulations=PlayoffModel.Trials,exponent=1.83,note="피타고리안 승률 = 득점^1.83 / (득점^1.83 + 실점^1.83). 예상승은 무승부 제외 경기수 기준. 진출확률은 현재 전적을 유지하고 상대별 16경기에서 적재된 종료 경기를 뺀 남은 대진을 10,000회 계산한 상위 5위 비율입니다. 상대 승률은 Log5로 보정합니다. 남은 경기 무승부·홈 이점·부상·선발투수 변화는 반영하지 않고, 최종 승률 동률은 남은 진출 자리를 균등 배분합니다. 지수 1.83은 KBO에 맞춰 별도 보정하지 않은 기본 가정입니다. 공식 확률이 아닌 자체 모델 추정이며, DB 누락은 남은 경기로 간주되므로 완전한 시즌 데이터가 필요합니다."};
            }
            if(cache.Count>=8)cache.Clear();cache[key]=result;return result;
        }finally{mutex.Release();}
    }
}
