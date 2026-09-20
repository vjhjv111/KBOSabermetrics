using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace NaverSabermetrics.Web;

public sealed record GameWebRequest(string Section="calendar",int Year=2026,int Month=1,string Id="",string Code="",string Competition="정규시즌",int Page=1)
{
    public void Validate(){if(Section is not("calendar" or "detail" or "locations")||Year is <1900 or >2200||Month is <1 or >12||Page is <1 or >100||Id.Length>40||Code.Length>40||Id.Any(c=>!char.IsAsciiLetterOrDigit(c))||Code.Any(c=>!char.IsAsciiLetterOrDigit(c))||Competition is not("정규시즌" or "포스트시즌" or "시범경기" or "전체")||(Section=="detail"&&Id.Length==0)||(Section=="locations"&&Code.Length==0))throw new RequestError("경기 조회 조건이 잘못되었습니다.");}
}
public sealed class GameWebService(DatabaseCacheService db,SiteOptions options)
{
    async Task<List<Dictionary<string,object?>>> Read(string sql,GameWebRequest r,CancellationToken ct)
    {
        await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync(ct);
        await using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.CommandTimeout=options.QuerySeconds;
        foreach(var (k,v) in new (string,object)[]{("$year",r.Year),("$month",$"{r.Year:0000}-{r.Month:00}"),("$id",r.Id),("$code",r.Code),("$offset",(r.Page-1)*1000)})cmd.Parameters.AddWithValue(k,v);
        using var cancel=ct.Register(cmd.Cancel);await using var reader=await cmd.ExecuteReaderAsync(ct);var rows=new List<Dictionary<string,object?>>();while(await reader.ReadAsync(ct)){var row=new Dictionary<string,object?>();for(int i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);rows.Add(row);}return rows;
    }
    public static async Task<List<object>> Decisions(SqliteConnection c,string id,CancellationToken ct)
    {
        var values=new Dictionary<string,List<string>>();void Add(string label,string? name){if(string.IsNullOrWhiteSpace(name))return;if(!values.TryGetValue(label,out var names))values[label]=names=[];if(!names.Contains(name.Trim()))names.Add(name.Trim());}
        await using var cmd=c.CreateCommand();cmd.CommandTimeout=30;using var cancel=ct.Register(cmd.Cancel);cmd.CommandText="SELECT RawText FROM NormalizedEvents WHERE GameId=$id AND (RawText LIKE '%투수%' OR RawText LIKE '%세이브%' OR RawText LIKE '%홀드%')";cmd.Parameters.AddWithValue("$id",id);
        await using(var reader=await cmd.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct)){if(reader.IsDBNull(0))continue;var m=Regex.Match(reader.GetString(0),@"^\s*(승리투수|패전투수|패배투수|홀드(?:투수)?|세이브(?:투수)?)\s*[:：]\s*(.{1,120})\s*$");if(m.Success){var label=m.Groups[1].Value;Add(label.StartsWith("승리")?"승리투수":label.StartsWith("패")?"패전투수":label.StartsWith("홀드")?"홀드":"세이브",m.Groups[2].Value);}}
        cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='GameMetadata'";
        if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))>0){cmd.CommandText="SELECT WinPitcher,LosePitcher,SavePitcher FROM GameMetadata WHERE GameId=$id";await using var reader=await cmd.ExecuteReaderAsync(ct);if(await reader.ReadAsync(ct))for(int i=0;i<3;i++)if(!reader.IsDBNull(i)&&!string.IsNullOrWhiteSpace(reader.GetString(i))){var label=new[]{"승리투수","패전투수","세이브"}[i];values.Remove(label);Add(label,reader.GetString(i));}}
        return values.SelectMany(k=>k.Value.Select(n=>(object)new{label=k.Key,name=n})).ToList();
    }
    public async Task<object> QueryAsync(GameWebRequest r,CancellationToken ct)
    {
        r.Validate();const string safe="UPPER(g.HomeTeamCode) NOT IN ('EA','WE') AND UPPER(g.AwayTeamCode) NOT IN ('EA','WE')";
        if(r.Section=="locations"){
            var round=r.Competition switch{"정규시즌"=>"LOWER(TRIM(g.RoundCode))='kbo_r'","시범경기"=>"g.CompetitionType=1","포스트시즌"=>"g.CompetitionType=2",_=>"1=1"};
            var rows=await Read($"SELECT COALESCE(NULLIF(p.PitchType,''),'미상') Type,p.CrossPlateX X,p.CalculatedCrossPlateZ Z,p.TopStrikeZone Top,p.BottomStrikeZone Bottom FROM Pitches p JOIN Games g ON g.GameId=p.GameId WHERE p.PitcherPcode=$code AND g.SeasonYear=$year AND {safe} AND {round} ORDER BY p.GameId,p.PitchEventId LIMIT 1001 OFFSET $offset",r,ct);
            return new{rows=rows.Take(1000),hasMore=rows.Count>1000,page=r.Page};
        }
        if(r.Section=="calendar")return new{rows=await Read($"SELECT g.GameId Id,g.GameDate Date,g.GameDateTime Time,g.HomeTeamCode Home,g.AwayTeamCode Away,g.HomeScore HS,g.AwayScore AScore,g.Stadium,g.StatusCode Status,g.RoundCode Round FROM Games g WHERE SUBSTR(g.GameDate,1,7)=$month AND {safe} ORDER BY g.GameDateTime,g.GameId LIMIT 500",r,ct)};
        var games=await Read($"SELECT g.GameId Id,g.GameDate Date,g.GameDateTime Time,g.HomeTeamCode Home,g.AwayTeamCode Away,g.HomeScore HS,g.AwayScore AScore,g.HomeHits HH,g.AwayHits AH,g.HomeErrors HE,g.AwayErrors AE,g.HomeWalks HB,g.AwayWalks AB,g.Stadium,g.StatusCode Status FROM Games g WHERE g.GameId=$id AND {safe}",r,ct);if(games.Count==0)throw new RequestError("경기를 찾을 수 없습니다.",404,"GAME_NOT_FOUND");
        var batters=await Read("SELECT Pcode Code,TeamCode Team,Name,BatOrder,Position,LineupSequence,PlateAppearances PA,AtBats AB,Hits H,HomeRuns HR,Walks BB,HitByPitch HBP,Strikeouts SO,Runs R,RunsBattedIn RBI FROM BattingGameLines WHERE GameId=$id ORDER BY TeamSide,BatOrder,LineupSequence",r,ct);
        var pitchers=await Read("SELECT Pcode Code,TeamCode Team,Name,InningsDisplay IP,PitchCount NP,HitsAllowed H,HomeRunsAllowed HR,Walks BB,HitBatters HBP,Strikeouts SO,RunsAllowed R,EarnedRuns ER FROM PitchingGameLines WHERE GameId=$id ORDER BY TeamSide,AppearanceSequence",r,ct);
        var probability=await Read("SELECT ChronologicalIndex Seq,Inning,Title,HomeWinRateAfter Home,AwayWinRateAfter Away,WpaByPlate WPA FROM RelayGroups WHERE GameId=$id AND HomeWinRateAfter IS NOT NULL AND AwayWinRateAfter IS NOT NULL AND HomeWinRateAfter BETWEEN 0 AND 100 AND AwayWinRateAfter BETWEEN 0 AND 100 AND ABS(HomeWinRateAfter+AwayWinRateAfter-100)<0.1 ORDER BY ChronologicalIndex LIMIT 2000",r,ct);
        var innings=await Read("SELECT Inning,BattingTeamCode Team,MIN(CASE WHEN BattingTeamCode=g.HomeTeamCode THEN BeforeHomeScore ELSE BeforeAwayScore END) Start,MAX(CASE WHEN BattingTeamCode=g.HomeTeamCode THEN AfterHomeScore ELSE AfterAwayScore END) Finish FROM RelayGroups p JOIN Games g ON g.GameId=p.GameId WHERE p.GameId=$id AND Inning BETWEEN 1 AND 30 AND BattingTeamCode IN (g.HomeTeamCode,g.AwayTeamCode) GROUP BY Inning,BattingTeamCode ORDER BY Inning",r,ct);
        await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync(ct);var decisions=await Decisions(c,r.Id,ct);
        object? original=null;var exists=await Read("SELECT name FROM sqlite_master WHERE type='table' AND name='GameMetadata'",r,ct);if(exists.Count>0){var meta=await Read("SELECT HomeInnings,AwayInnings FROM GameMetadata WHERE GameId=$id",r,ct);if(meta.Count>0)original=new{home=meta[0]["HomeInnings"] is string h?JsonSerializer.Deserialize<string[]>(h):null,away=meta[0]["AwayInnings"] is string a?JsonSerializer.Deserialize<string[]>(a):null};}
        var plays=await Read("SELECT r.RelayGroupId Id,r.ChronologicalIndex Seq,r.Inning,r.BattingTeamCode Team,r.Title,r.BeforeHomeScore BH,r.BeforeAwayScore BA,r.AfterHomeScore AH,r.AfterAwayScore AA,r.HomeWinRateAfter Home,r.AwayWinRateAfter Away,r.WpaByPlate WPA FROM RelayGroups r WHERE r.GameId=$id ORDER BY r.ChronologicalIndex",r,ct);
        // 투구 한 줄(예: "1구 볼")에 구종·구속을 덧붙이기 위해 Pitches를 원본 이벤트(SourceEventId=EventId)로
        // LEFT JOIN합니다. 결정(승리투수 등)·주자·교체 등 투구가 아닌 이벤트는 매칭되는 Pitches 행이
        // 없어 PitchType/SpeedKmh가 NULL로 나오므로 그대로 원문만 표시합니다.
        var events=await Read("SELECT ne.RelayGroupId GroupId,ne.RawText Text,p.PitchType PitchType,p.SpeedKmh SpeedKmh FROM NormalizedEvents ne LEFT JOIN Pitches p ON p.SourceEventId=ne.EventId WHERE ne.GameId=$id AND ne.RawText IS NOT NULL AND TRIM(ne.RawText)<>'' ORDER BY ne.ChronologicalIndex",r,ct);
        var grouped=events.ToLookup(e=>Convert.ToString(e["GroupId"]));
        static string FormatEvent(Dictionary<string,object?> e)
        {
            var text=Convert.ToString(e["Text"])??"";
            var pitchType=e["PitchType"] as string;
            double? speed=e["SpeedKmh"] is null?null:Convert.ToDouble(e["SpeedKmh"]);
            var hasType=!string.IsNullOrWhiteSpace(pitchType);
            if(!hasType&&speed is null)return text;
            var detail=hasType&&speed is not null?$"{pitchType}({speed.Value:0}km/h)":hasType?pitchType!:$"{speed!.Value:0}km/h";
            return $"{text} {detail}";
        }
        foreach(var play in plays)play["events"]=grouped[Convert.ToString(play["Id"])].Select(FormatEvent).ToArray();
        // 타석별 투구 위치 미니 스트라이크존 시각화용 — 배경/선수 이미지 없이 좌표·구종·구속·카운트만 내려줍니다.
        var pitchRows=await Read("SELECT RelayGroupId GroupId,DisplayPitchNumber Num,CrossPlateX X,CalculatedCrossPlateZ Z,TopStrikeZone Top,BottomStrikeZone Bottom,PitchResult Result,PitchType Type,SpeedKmh Speed,BallsAfter B,StrikesAfter S FROM Pitches WHERE GameId=$id ORDER BY ActualPitchIndex",r,ct);
        var pitchGrouped=pitchRows.ToLookup(p=>Convert.ToString(p["GroupId"]));
        static string PitchCategory(int result)=>(PitchResultType)result switch
        {
            PitchResultType.Ball=>"ball",
            PitchResultType.Foul or PitchResultType.BuntFoul=>"foul",
            PitchResultType.InPlay=>"inplay",
            PitchResultType.SwingingStrike or PitchResultType.CalledStrike or PitchResultType.BuntSwingingStrike=>"strike",
            _=>"other",
        };
        static object? FormatPitch(Dictionary<string,object?> p)
        {
            if(p["X"] is not double x||p["Z"] is not double z||p["Top"] is not double top||p["Bottom"] is not double bottom||top<=bottom)return null;
            return new{
                num=p["Num"] is null?(int?)null:Convert.ToInt32(p["Num"]),
                x,z=(z-bottom)/(top-bottom),
                category=PitchCategory(p["Result"] is null?0:Convert.ToInt32(p["Result"])),
                type=p["Type"] as string,
                speed=p["Speed"] is null?(double?)null:Convert.ToDouble(p["Speed"]),
                balls=p["B"] is null?(int?)null:Convert.ToInt32(p["B"]),
                strikes=p["S"] is null?(int?)null:Convert.ToInt32(p["S"])
            };
        }
        foreach(var play in plays)play["pitches"]=pitchGrouped[Convert.ToString(play["Id"])].Select(FormatPitch).Where(x=>x!=null).ToArray();
        return new{game=games[0],batters,pitchers,probability,innings,original,decisions,plays};
    }
}
