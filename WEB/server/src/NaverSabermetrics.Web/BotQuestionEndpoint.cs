using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NaverSabermetrics.Web;

public static class BotQuestionEndpoint
{
    public static void Authorize(string? expected,string supplied)
    {
        if(string.IsNullOrWhiteSpace(expected)||expected.Length<32)throw new RequestError("봇 질문 API 인증 설정이 필요합니다.",503,"BOT_NOT_READY");
        if(string.IsNullOrEmpty(supplied)||!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            throw new RequestError("봇 인증에 실패했습니다.",401,"BOT_UNAUTHORIZED");
    }
    private static string CompactConditions(string value)
    {
        var parts=QuestionTeams.Display(value).Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        var hasDates=Regex.IsMatch(value,@"\d{4}-\d{2}-\d{2}");
        return string.Join(" · ",parts.Where(p=>p is not ("전체 회차" or "팀 전체" or "선수 전체" or "최소 0타석" or "최소 0이닝")
            && !(hasDates&&Regex.IsMatch(p,@"^\d{4}$"))
            && !Regex.IsMatch(p,@"^(?:InningsPitched|PA) gte ")
            && !p.Contains("타자 시점") && !p.Contains("투수 시점")
            && !p.Contains("부호 있는 WPA") && !p.Contains("절댓값"))
            .Select(p=>Regex.Replace(p,@"\b(\d{4})-(\d{2})-(\d{2})", "$2/$3")));
    }
    public static string Format(object result)
    {
        var data=JsonSerializer.SerializeToElement(result,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
        var answer=data.GetProperty("answer").GetString()??"";
        if(data.GetProperty("clarification").GetBoolean())return answer;
        var applied=data.TryGetProperty("applied",out var a)?a.GetString()??"":"";
        var header=CompactConditions(applied);
        var year=Regex.Match(applied,@"\b\d{4}\b").Value;
        if(year.Length>0&&!header.Contains(year))header=year+" · "+header;
        var lines=new List<string>();
        if(header.Length>0)lines.Add(header);
        if(data.TryGetProperty("rows",out var rows)&&data.TryGetProperty("columns",out var columns))
        {
            if(rows.GetArrayLength()==0)lines.Add(answer);
            int i=0;
            foreach(var row in rows.EnumerateArray().Take(10))
            {
                var cells=row.GetProperty("cells");
                string Cell(string key)=>cells.TryGetProperty(key,out var v)?v.GetString()??"":"";
                string Team(string key)=>QuestionTeams.Display(Cell(key));
                var prefix=rows.GetArrayLength()>1?$"{++i}. ":"";
                if(cells.TryGetProperty("WPA",out _)&&cells.TryGetProperty("Result",out _))
                {
                    var date=Cell("Date");if(date.Length==10)date=date[5..].Replace('-','/');
                    lines.Add(prefix+$"{date} {Team("Away")} vs {Team("Home")} · {Cell("Inning")}회");
                    var play=Cell("Result");var batter=Cell("Batter");
                    if(play.StartsWith(batter+" : ",StringComparison.Ordinal))play=play[(batter.Length+3)..];
                    play=Regex.Replace(play,@"\s*\(홈런거리:[^)]*\)","");
                    lines.Add($"{batter}({Team("TeamCode")}) · {play}");
                    var wpa=Cell("WPA");if(double.TryParse(wpa,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number))wpa=number.ToString("+0.###;-0.###;0",System.Globalization.CultureInfo.InvariantCulture);
                    lines.Add($"WPA {wpa}%p · 상대 투수 {Cell("Pitcher")}"+(Cell("WpaSource")=="FanGraphs WE 4.5"?" · FG 4.5":Cell("WpaSource")=="기존 추산"?" · 기존 추산":""));
                }
                else
                {
                    var name=Cell("Name");var team=Team("TeamCode");
                    var identity=string.IsNullOrEmpty(name)?team:name==team?name:$"{name}({team})";
                    var stats=new List<string>();
                    foreach(var column in columns.EnumerateArray())
                    {
                        var key=column.GetProperty("key").GetString()!;
                        if(key is "Name" or "TeamCode")continue;
                        var value=Cell(key);if(value.Length==0||value is "-" or "—")continue;
                        stats.Add(key=="PA"?value+"타석":key=="InningsPitched"?value+"이닝":(column.GetProperty("label").GetString()??key)+" "+value);
                    }
                    lines.Add(prefix+identity+" · "+string.Join(" · ",stats));
                }
            }
            if(rows.GetArrayLength()>10)lines.Add("상위 10개만 표시");
            if(rows.EnumerateArray().Any(r=>r.GetProperty("cells").TryGetProperty("WPA",out _)))
                lines.Add("WPA: "+(applied.Contains("투수 시점")?"투수":"타자")+" 시점"+(applied.Contains("절댓값")?" · 절댓값 순위":"")+" · 산출 가능한 타석 기준");
        }
        if(data.TryGetProperty("warnings",out var warnings)&&warnings.EnumerateArray().Any(w=>(w.GetString()??"").Contains("근사치")))lines.Add("* 상황별 기록은 근사치");
        if(data.TryGetProperty("warnings",out var positionWarnings)&&positionWarnings.EnumerateArray().Any(w=>(w.GetString()??"").Contains("조회 기간의 주 포지션")))lines.Add("* 조회 기간의 주 포지션 기준");
        if(data.TryGetProperty("asOf",out var asOf))lines.Add("기준 "+asOf.GetString());
        var text=string.Join("\n",lines);
        return text.Length>3500?text[..3450]+"\n(이하 생략)":text;
    }
}