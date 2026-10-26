using System.Text.Json;
using System.Text.RegularExpressions;
namespace NaverSabermetrics.Web;
public static partial class ExternalRelayUpload
{
    public const string ScheduleRoute="/api/collector/schedule";
    public static async Task<IResult> ReceiveScheduleAsync(HttpContext context,BotStartersService store)
    {
        Authorize(context.Request.Headers["X-Fanzai-Collector-Key"].ToString());
        if(!await Writer.WaitAsync(0,context.RequestAborted))throw new RequestError("수집 저장 중",429,"COLLECTOR_BUSY");
        try {
            using var bytes=new MemoryStream();
            var buffer=new byte[8192];int count;
            while((count=await context.Request.Body.ReadAsync(buffer,context.RequestAborted))>0) {
                if(bytes.Length+count>256*1024)throw new RequestError("일정 크기 초과",413,"COLLECTOR_SIZE");
                bytes.Write(buffer,0,count);
            }
            StarterDay? day;
            try {day=JsonSerializer.Deserialize<StarterDay>(bytes.ToArray(),new JsonSerializerOptions(JsonSerializerDefaults.Web));}
            catch(JsonException){throw new RequestError("잘못된 일정 JSON");}
            ValidateDay(day);
            if(store.Get(day!.Date)?.UpdatedAt>day.UpdatedAt)return Results.Ok(new{ignored=true});
            await store.SaveAsync(day,context.RequestAborted);
            return Results.Ok(new{date=day.Date,games=day.Games.Length});
        } finally {Writer.Release();}
    }
    public static void ValidateDay(StarterDay? day)
    {
        var today=BotGamesService.ParseDate(null);
        if(day is null || day.Date!=today.ToString("yyyy-MM-dd")&&day.Date!=today.AddDays(1).ToString("yyyy-MM-dd") ||
            day.UpdatedAt<DateTimeOffset.UtcNow.AddMinutes(-10)||day.UpdatedAt>DateTimeOffset.UtcNow.AddMinutes(1) ||
            day.Games is null || day.Games.Length>20)
            throw new RequestError("오늘·내일의 최신 전체 일정만 허용합니다.");
        var ids=new HashSet<string>();
        foreach(var g in day.Games) {
            if(g is null || g.Id is null || !Regex.IsMatch(g.Id, @"^\d{8}[A-Z]{4}\d{5}$") || !ids.Add(g.Id) ||
                g.Id[13..]+g.Id.Substring(4,4)!=day.Date.Replace("-","") ||
                g.Status is not ("BEFORE" or "READY" or "STARTED" or "PLAY" or "RESULT" or "ENDED" or "CANCEL") ||
                g.AwayLineup is null || g.HomeLineup is null || g.AwayLineup.Length>9 || g.HomeLineup.Length>9)
                throw new RequestError("경기 ID·날짜·상태·라인업을 확인하세요.");
        }
    }
    // A complete, successfully fetched day replaces only scheduled entries.
    public static List<Dictionary<string,object?>> ApplySchedule(List<Dictionary<string,object?>> rows,StarterDay day)
    {
        var incoming=day.Games.ToDictionary(g=>g.Id);
        string Key(Dictionary<string,object?> r) {
            var id=Convert.ToString(r["Id"])??"";
            return id.Length==13?id+day.Date[..4]:id;
        }
        rows.RemoveAll(r=>Convert.ToString(r["Date"])?.StartsWith(day.Date)==true &&
            Convert.ToString(r["Status"]) is "BEFORE" or "READY" && !incoming.ContainsKey(Key(r)));
        foreach(var g in day.Games) {
            var existing=rows.FirstOrDefault(r=>Key(r)==g.Id);
            // Live and final relay payloads carry scores; schedule is not a score source.
            if(existing is not null && Convert.ToString(existing["Status"]) is not ("BEFORE" or "READY"))continue;
            if(existing is not null)rows.Remove(existing);
            rows.Add(new(){["Id"]=g.Id,["Date"]=day.Date,["Time"]=g.Time,["Home"]=g.Id.Substring(10,2),
                ["Away"]=g.Id.Substring(8,2),["HS"]=null,["AScore"]=null,["Stadium"]=null,["Status"]=g.Status,
                ["Round"]=g.Id[..4]==day.Date[..4]?"kbo_r":"kbo_ps"});
        }
        return rows.OrderBy(r=>Convert.ToString(r["Time"])).ToList();
    }
}
