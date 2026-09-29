using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace NaverSabermetrics.Web;

// Read-only projection of collected JSON. Never imports incomplete games into the statistics DB.
public sealed record HomeLiveGame(string Id, string Date, string Time, string Away, string Home,
    string? Stadium, string Status, string StatusText, int? AwayScore, int? HomeScore,
    string? AwayPitcher, string? HomePitcher, DateTimeOffset UpdatedAt, JsonObject Source)
{
    public bool Final => RenderCollectionPolicy.IsFinalStatus(Status);
    public bool Playing => RenderCollectionPolicy.IsLiveStatus(Status);
    public object[] Decisions => HomeLiveService.Decisions(Source);
    public Dictionary<string, object?> Card() => new()
    {
        ["GameId"]=Id, ["Stadium"]=Stadium, ["AwayTeamCode"]=Away, ["HomeTeamCode"]=Home,
        ["AwayScore"]=AwayScore, ["HomeScore"]=HomeScore, ["StatusCode"]=Status,
        ["StatusText"]=StatusText, ["AwayPitcher"]=AwayPitcher, ["HomePitcher"]=HomePitcher,
        ["decisions"]=Final ? Decisions : [], ["UpdatedAt"]=UpdatedAt
    };
}

public sealed record HomeLiveSnapshot(string Version, HomeLiveGame[] Games);

public sealed class HomeLiveService(RenderCollectorOptions options, ILogger<HomeLiveService> logger)
{
    private readonly SemaphoreSlim mutex = new(1,1);
    private readonly Dictionary<string,(long Stamp,long Length,HomeLiveGame? Game)> files = new();
    public async Task<HomeLiveSnapshot> ReadAsync(int year, CancellationToken ct)
    {
        // Only the collector's active lookback window is overlaid. Historical records stay DB-backed.
        var today=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date;
        if(year!=today.Year || !Directory.Exists(options.JsonDirectory))return new("",[]);
        await mutex.WaitAsync(ct);
        try
        {
            var paths=Enumerable.Range(0,Math.Max(1,options.LookbackDays)+1)
                .SelectMany(days=>Directory.EnumerateFiles(options.JsonDirectory,$"{today.AddDays(-days):yyyyMMdd}*.json"))
                .OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var result=new List<HomeLiveGame>();var version=new List<string>();
            foreach(var path in paths)
            {
                ct.ThrowIfCancellationRequested();var info=new FileInfo(path);
                var stamp=info.LastWriteTimeUtc.Ticks;var length=info.Length;
                if(!files.TryGetValue(path,out var cached)||cached.Stamp!=stamp||cached.Length!=length)
                {
                    try { cached=(stamp,length,Parse(await File.ReadAllTextAsync(path,ct),Path.GetFileNameWithoutExtension(path))); files[path]=cached; }
                    catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
                    { logger.LogWarning(ex,"홈 경기 JSON 읽기 보류: {File}",Path.GetFileName(path)); continue; }
                }
                version.Add($"{info.Name}:{stamp}:{length}");if(cached.Game is {} game)result.Add(game);
            }
            foreach(var old in files.Keys.Except(paths).ToArray())files.Remove(old);
            return new(string.Join('|',version),result.ToArray());
        }
        finally { mutex.Release(); }
    }

    public static HomeLiveGame? Parse(string json,string expectedId,bool includeRelay=false)
    {
        var root=JsonNode.Parse(json);var result=(root?["naver"]??root)?["result"] as JsonObject;
        var g=result?["game"];
        if(g is null || !Regex.IsMatch(expectedId,@"^\d{8}[A-Z]{4}\d\d{4}$") || S(g,"gameId")!=expectedId || S(g,"roundCode")!="kbo_r")return null;
        var home=S(g,"homeTeamCode");var away=S(g,"awayTeamCode");
        if(home!=expectedId.Substring(10,2)||away!=expectedId.Substring(8,2)||home is "EA" or "WE"||away is "EA" or "WE")return null;
        var status=S(g,"statusCode")??"";
        if(S(g,"cancel")=="true")status="CANCEL";
        var final=RenderCollectionPolicy.IsFinalStatus(status);
        var inning=S(g,"currentInning")??S(g,"statusInfo");
        var label=final?"종료":status=="BEFORE"?"예정":status=="CANCEL"?"취소":S(g,"suspended")=="true"?"중단":string.IsNullOrWhiteSpace(inning)?"진행 중":Regex.Replace(inning,@"회\s*(초|말)","회 $1");
        var date=DateTime.ParseExact(expectedId[..8],"yyyyMMdd",CultureInfo.InvariantCulture).ToString("yyyy-MM-dd");
        DateTimeOffset.TryParse(S(root,"collectedAt"),out var updated);
        var source=includeRelay?result!:new JsonObject{["game"]=g.DeepClone(),["decisionSummary"]=System.Text.Json.JsonSerializer.SerializeToNode(Decisions(result!))};
        return new(expectedId,date,S(g,"gameDateTime")??date,away!,home!,S(g,"stadium"),status,label,
            I(g,"awayTeamScore"),I(g,"homeTeamScore"),S(g,"awayCurrentPitcherName"),S(g,"homeCurrentPitcherName"),updated,source);
    }

    internal static string? S(JsonNode? n,string key)=>n?[key]?.ToString();
    internal static int? I(JsonNode? n,string key)=>int.TryParse(S(n,key),out var value)?value:null;
    static double? D(JsonNode? n,string key)=>double.TryParse(S(n,key),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:null;
    static JsonNode[] Array(JsonNode? n)=>n is JsonArray a?a.Where(x=>x is not null).Cast<JsonNode>().ToArray():[];
    public static object[] Decisions(JsonObject result)
    {
        if(result["decisionSummary"] is JsonArray saved)return saved.Select(x=>(object)new{label=S(x,"label"),name=S(x,"name")}).ToArray();
        var values=new Dictionary<string,List<string>>();
        void Add(string label,string? name){if(string.IsNullOrWhiteSpace(name))return;if(!values.TryGetValue(label,out var names))values[label]=names=[];if(!names.Contains(name))names.Add(name);}
        foreach(var play in Array(result["textRelayData"]?["textRelays"]))foreach(var e in Array(play["textOptions"]))
        {
            var m=Regex.Match(S(e,"text")??"",@"^\s*(승리투수|패전투수|패배투수|홀드(?:투수)?|세이브(?:투수)?)\s*[:：]\s*(.+?)\s*$");
            if(m.Success){var label=m.Groups[1].Value;Add(label.StartsWith("승리")?"승리투수":label.StartsWith("패")?"패전투수":label.StartsWith("홀드")?"홀드":"세이브",m.Groups[2].Value);}
        }
        foreach(var (field,label) in new[]{("winPitcherName","승리투수"),("losePitcherName","패전투수"),("savePitcherName","세이브")})
            if(S(result["game"],field) is {Length:>0} name){values.Remove(label);Add(label,name);}
        return values.SelectMany(x=>x.Value.Select(name=>(object)new{label=x.Key,name})).ToArray();
    }

    // A connection-local table lets every home calculation share the same deduplicated score set.
    // SQLite permits TEMP writes on a read-only main connection; no statistics tables are changed.
    public static async Task PrepareHomeGamesAsync(SqliteConnection c,int year,HomeLiveSnapshot snapshot,CancellationToken ct)
    {
        await using var cmd=c.CreateCommand();
        cmd.CommandText="DROP TABLE IF EXISTS temp.HomeGames; CREATE TEMP TABLE HomeGames AS SELECT GameId,SeasonYear,GameDate,GameDateTime,Stadium,AwayTeamCode,HomeTeamCode,AwayScore,HomeScore,AwayHits,HomeHits,AwayErrors,HomeErrors,RoundCode,StatusCode FROM main.Games WHERE SeasonYear=$year; CREATE UNIQUE INDEX temp.HomeGamesId ON HomeGames(GameId);";
        cmd.Parameters.AddWithValue("$year",year);await cmd.ExecuteNonQueryAsync(ct);
        foreach(var g in snapshot.Games.Where(g=>g.Final&&g.AwayScore>=0&&g.HomeScore>=0))
        {
            cmd.Parameters.Clear();
            cmd.CommandText="INSERT INTO HomeGames(GameId,SeasonYear,GameDate,GameDateTime,Stadium,AwayTeamCode,HomeTeamCode,AwayScore,HomeScore,RoundCode,StatusCode) VALUES($id,$year,$date,$time,$stadium,$away,$home,$a,$h,'kbo_r',$status) ON CONFLICT(GameId) DO UPDATE SET GameDate=excluded.GameDate,GameDateTime=excluded.GameDateTime,Stadium=excluded.Stadium,AwayScore=excluded.AwayScore,HomeScore=excluded.HomeScore,RoundCode='kbo_r',StatusCode=excluded.StatusCode WHERE UPPER(HomeGames.StatusCode) NOT IN ('RESULT','ENDED') OR HomeGames.RoundCode<>'kbo_r';";
            foreach(var (key,value) in new (string,object?)[]{("id",g.Id),("year",year),("date",g.Date),("time",g.Time),("stadium",g.Stadium),("away",g.Away),("home",g.Home),("a",g.AwayScore),("h",g.HomeScore),("status",g.Status)})cmd.Parameters.AddWithValue("$"+key,value??DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<object?> DetailAsync(string id,CancellationToken ct)
    {
        if(!Regex.IsMatch(id,@"^\d{8}[A-Z]{4}\d\d{4}$"))return null;
        var snapshot=await ReadAsync(int.Parse(id[..4]),ct);var game=snapshot.Games.FirstOrDefault(x=>x.Id==id&&x.Final);
        if(game is null)return null;
        var raw=await File.ReadAllTextAsync(Path.Combine(options.JsonDirectory,id+".json"),ct);
        var detailed=Parse(raw,id,includeRelay:true);
        return detailed is {Final:true}?Detail(detailed):null;
    }

    public static object Detail(HomeLiveGame live)
    {
        var g=live.Source["game"];var relay=live.Source["textRelayData"];
        var game=new Dictionary<string,object?>{["Id"]=live.Id,["Date"]=live.Date,["Time"]=live.Time,["Away"]=live.Away,["Home"]=live.Home,["AScore"]=live.AwayScore,["HS"]=live.HomeScore,["Stadium"]=live.Stadium,["Status"]=live.Status};
        foreach(var (side,prefix) in new[]{("away","A"),("home","H")}){var rheb=Array(g?[side+"TeamRheb"]);foreach(var (key,index) in new[]{("H",1),("E",2),("B",3)})game[prefix+key]=index<rheb.Length?rheb[index].ToString():null;}
        var batters=new List<object>();var pitchers=new List<object>();
        foreach(var side in new[]{"away","home"})foreach(var role in new[]{"batter","pitcher"})
            foreach(var p in Array(relay?[side+"Lineup"]?[role]))
            {
                var row=new Dictionary<string,object?>{["Team"]=side=="away"?live.Away:live.Home};
                var fields=role=="batter"?"Code:pcode Name:name BatOrder:batOrder Position:posName PA:pa AB:ab H:hit HR:hr R:run RBI:rbi BB:bb HBP:hbp SO:so":"Code:pcode Name:name IP:inn NP:ballCount H:hit HR:hr R:run ER:er BB:bb HBP:hbp SO:kk";
                foreach(var pair in fields.Split(' ')){var f=pair.Split(':');row[f[0]]=S(p,f[1]);}
                (role=="batter"?batters:pitchers).Add(row);
            }
        var plays=Array(relay?["textRelays"]).OrderBy(p=>I(p,"no")).Select(p=>{
            var events=Array(p["textOptions"]).OrderBy(e=>I(e,"seqno")).ToArray();
            var after=events.LastOrDefault(e=>e["currentGameState"] is not null)?["currentGameState"];
            var metric=p["metricOption"];var home=D(metric,"homeTeamWinRate");var away=D(metric,"awayTeamWinRate");
            var valid=home is >=0 and <=100 && away is >=0 and <=100 && Math.Abs(home.Value+away.Value-100)<0.1;
            return new Dictionary<string,object?>{
                ["Seq"]=I(p,"no"),["Inning"]=I(p,"inn"),["Team"]=S(p,"homeOrAway")=="1"?live.Away:live.Home,["Title"]=S(p,"title"),
                ["AH"]=I(after,"homeScore"),["AA"]=I(after,"awayScore"),["Home"]=valid?home:null,["Away"]=valid?away:null,["WPA"]=D(metric,"wpaByPlate"),
                ["events"]=events.Select(e=>string.Join(' ',new[]{S(e,"text"),S(e,"stuff"),S(e,"speed") is {} speed?speed+"km/h":null}.Where(x=>!string.IsNullOrWhiteSpace(x)))).ToArray()
            };
        }).ToArray();
        var probability=plays.Where(p=>p["Home"] is not null&&p["Away"] is not null).ToArray();
        return new{game,batters,pitchers,probability,innings=System.Array.Empty<object>(),original=new{home=Array(g?["homeTeamScoreByInning"]).Select(x=>x.ToString()),away=Array(g?["awayTeamScoreByInning"]).Select(x=>x.ToString())},decisions=live.Decisions,plays,
            note="종료 직후 수집 JSON 기준입니다. 하루 전체 경기의 통계 DB 편입 후 정규화된 상세 기록으로 전환됩니다."};
    }
}
