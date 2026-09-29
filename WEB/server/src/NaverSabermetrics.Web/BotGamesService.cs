using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverSabermetrics.Web;

public sealed record BotGameDecision(string Label,string Name);
public sealed record BotGame(string Id,string Date,string? Time,string? Stadium,string Status,string StatusText,
    string Away,string AwayName,string Home,string HomeName,int? AwayScore,int? HomeScore,
    string? AwayPitcher,string? HomePitcher,BotGameDecision[] Decisions,DateTimeOffset? UpdatedAt)
{
    public bool Finished=>RenderCollectionPolicy.IsFinalStatus(Status);
}
public sealed record BotGamesResponse(string Date,string TimeZone,DateTimeOffset? UpdatedAt,bool HasLiveGames,
    int RefreshSeconds,BotGame[] Games,string Text);

// A bounded score-only query. No forecast/WAR calculation and no per-request upstream scraping.
public sealed class BotGamesService(DatabaseCacheService db,SiteOptions options,HomeLiveService live)
{
    private static readonly Dictionary<string,string> Names=new(StringComparer.OrdinalIgnoreCase)
    { ["HH"]="한화",["HT"]="KIA",["KT"]="KT",["LG"]="LG",["LT"]="롯데",["NC"]="NC",["OB"]="두산",["SK"]="SSG",["SS"]="삼성",["WO"]="키움" };
    private static string Name(string code)=>Names.GetValueOrDefault(code,code);
    public static DateOnly ParseDate(string? date)
    {
        if(date is null)return DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).DateTime);
        if(!DateOnly.TryParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed)||parsed.Year is <1900 or >2200)
            throw new RequestError("날짜 형식이 올바르지 않습니다. (예: /경기 2026-09-29)");
        return parsed;
    }
    public async Task<BotGamesResponse> QueryAsync(DateOnly date,CancellationToken ct)
    {
        var day=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        var snapshot=await live.ReadAsync(date.Year,ct);
        var games=new Dictionary<string,BotGame>(StringComparer.Ordinal);
        await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());
        await c.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandTimeout=options.QuerySeconds;
        cmd.CommandText="SELECT GameId,GameDateTime,Stadium,StatusCode,AwayTeamCode,HomeTeamCode,AwayScore,HomeScore FROM Games WHERE SeasonYear=$year AND SUBSTR(GameDate,1,10)=$date AND LOWER(TRIM(RoundCode)) IN ('kbo_r','kbo_scheduled') AND UPPER(HomeTeamCode) NOT IN ('EA','WE') AND UPPER(AwayTeamCode) NOT IN ('EA','WE') ORDER BY GameDateTime,GameId LIMIT 20";
        cmd.Parameters.AddWithValue("$year",date.Year);cmd.Parameters.AddWithValue("$date",day);using var cancel=ct.Register(cmd.Cancel);
        await using(var reader=await cmd.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct))
        {
            string? Str(int i)=>reader.IsDBNull(i)?null:reader.GetString(i);
            int? Score(int i)=>reader.IsDBNull(i)?null:reader.GetInt32(i);
            var status=(Str(3)??"BEFORE").ToUpperInvariant();var away=reader.GetString(4);var home=reader.GetString(5);
            games[reader.GetString(0)]=new(reader.GetString(0),day,Str(1),Str(2),status,RenderCollectionPolicy.IsFinalStatus(status)?"종료":status=="CANCEL"?"취소":status=="BEFORE"?"예정":"진행 중",away,Name(away),home,Name(home),Score(6),Score(7),null,null,[],null);
        }
        foreach(var g in games.Values.Where(g=>g.Finished).ToArray())
            games[g.Id]=g with{Decisions=Decisions(await GameWebService.Decisions(c,g.Id,ct))};
        foreach(var g in snapshot.Games.Where(g=>g.Date==day))
        {
            if(games.TryGetValue(g.Id,out var stored)&&stored.Finished)continue;
            games[g.Id]=FromLive(g);
        }
        var rows=games.Values.OrderBy(g=>g.Time,StringComparer.Ordinal).ThenBy(g=>g.Id,StringComparer.Ordinal).ToArray();
        var updated=rows.Select(g=>g.UpdatedAt).Max();
        return new(day,"Asia/Seoul",updated,rows.Any(g=>RenderCollectionPolicy.IsLiveStatus(g.Status)),60,rows,Format(day,rows,updated));
    }
    private static BotGameDecision[] Decisions(IEnumerable<object> values)=>values.Select(p=>{
        var json=JsonSerializer.SerializeToElement(p);return new BotGameDecision(json.GetProperty("label").GetString()!,json.GetProperty("name").GetString()!);
    }).ToArray();
    public static BotGame FromLive(HomeLiveGame g)=>new(g.Id,g.Date,g.Time,g.Stadium,g.Status,g.StatusText,
        g.Away,Name(g.Away),g.Home,Name(g.Home),g.AwayScore,g.HomeScore,g.Final?null:g.AwayPitcher,g.Final?null:g.HomePitcher,
        g.Final?Decisions(g.Decisions):[],g.UpdatedAt> DateTimeOffset.UnixEpoch?g.UpdatedAt:null);

    public static string Format(string date,IReadOnlyList<BotGame> games,DateTimeOffset? updated)
    {
        var lines=new List<string>{$"⚾ {date} KBO 경기"};
        if(games.Count==0){lines.Add("해당 날짜에 수집된 경기·일정이 없습니다.");return string.Join('\n',lines);}
        foreach(var g in games)
        {
            if(g.Status=="CANCEL")lines.Add($"{g.AwayName} vs {g.HomeName} | 취소");
            else if(g.Status=="BEFORE")lines.Add($"{g.AwayName} vs {g.HomeName} | {(g.Time is {Length:>=16}?g.Time[11..16]:"시간 미정")} 예정");
            else lines.Add($"{g.AwayName} {g.AwayScore?.ToString()??"—"} : {g.HomeScore?.ToString()??"—"} {g.HomeName} | {g.StatusText}");
            if(g.Finished&&g.Decisions.Length>0)lines.Add("  "+string.Join(" · ",g.Decisions.Select(p=>$"{(p.Label.StartsWith("승리")?"승":p.Label.StartsWith("패")?"패":p.Label)} {p.Name}")));
            else if(g.Status is not ("BEFORE" or "CANCEL")&&(!string.IsNullOrWhiteSpace(g.AwayPitcher)||!string.IsNullOrWhiteSpace(g.HomePitcher)))
                lines.Add($"  투수 {g.AwayName} {g.AwayPitcher??"—"} / {g.HomeName} {g.HomePitcher??"—"}");
        }
        if(updated.HasValue){lines.Add($"수집 기준 {updated.Value.ToOffset(TimeSpan.FromHours(9)):MM-dd HH:mm} (한국시간)");
            if(games.Any(g=>RenderCollectionPolicy.IsLiveStatus(g.Status))&&DateTimeOffset.UtcNow-updated.Value>TimeSpan.FromMinutes(3))lines.Add("수집 갱신이 지연되고 있습니다.");}
        return string.Join('\n',lines);
    }
}
