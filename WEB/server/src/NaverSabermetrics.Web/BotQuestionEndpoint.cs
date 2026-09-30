using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NaverSabermetrics.Web;

public static class BotQuestionEndpoint
{
    public static void Authorize(string? expected,string supplied)
    {
        if(string.IsNullOrWhiteSpace(expected)||expected.Length<32)throw new RequestError("봇 질문 API 인증 설정이 필요합니다.",503,"BOT_NOT_READY");
        if(string.IsNullOrEmpty(supplied)||!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            throw new RequestError("봇 인증에 실패했습니다.",401,"BOT_UNAUTHORIZED");
    }
    public static string Format(object result)
    {
        var data=JsonSerializer.SerializeToElement(result,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
        var lines=new List<string>{data.GetProperty("answer").GetString()??""};
        if(data.GetProperty("clarification").GetBoolean())return lines[0];
        if(data.TryGetProperty("applied",out var applied))lines.Add(QuestionTeams.Display(applied.GetString()??""));
        if(data.TryGetProperty("rows",out var rows)&&data.TryGetProperty("columns",out var columns))
        {
            int i=0;
            foreach(var row in rows.EnumerateArray().Take(10))
            {
                var cells=row.GetProperty("cells");var parts=new List<string>();
                foreach(var column in columns.EnumerateArray())
                {
                    var key=column.GetProperty("key").GetString()!;
                    if(key is "GameId" or "Sequence")continue;
                    if(!cells.TryGetProperty(key,out var value))continue;
                    var text=value.GetString()??"—";
                    if(key is "TeamCode" or "Home" or "Away")text=QuestionTeams.Display(text);
                    parts.Add(key=="Name"?text:(column.GetProperty("label").GetString()??key)+" "+text);
                }
                lines.Add($"{++i}. "+string.Join(" · ",parts));
            }
            if(rows.GetArrayLength()>10)lines.Add("표시는 상위 10개로 제한됩니다.");
        }
        if(data.TryGetProperty("asOf",out var date))lines.Add("DB 기준일: "+date.GetString());
        if(data.TryGetProperty("warnings",out var warnings))
            foreach(var warning in warnings.EnumerateArray().Select(w=>w.GetString()??"").Where(w=>w.Contains("근사치")||w.Contains("공식 타석")||w.Contains("누락 데이터")))lines.Add(warning);
        var answer=string.Join("\n",lines);
        return answer.Length>3500?answer[..3450]+"\n(긴 결과는 일부 생략했습니다.)":answer;
    }
}
