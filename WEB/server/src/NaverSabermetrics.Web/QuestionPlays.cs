using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NaverSabermetrics.Web;

public static class QuestionPlays
{
    public static async Task<object> QueryAsync(QuestionPlan plan,SiteOptions site,int[] years,DateTime? asOf,CancellationToken ct)
    {
        if(plan.Filters.Length!=0||plan.MinimumVolume!=0)throw new RequestError("개별 WPA 플레이는 연도·날짜·팀·선수·회차 필터를 지원합니다. 추가 조건은 생략하지 않고 조회를 중단합니다.",400,"AI_FILTER");
        var validated=RecordQuestionService.ToRequest(plan with{Metric="so"},site,years);
        var start=validated.StartDate??new DateTime(plan.Year,1,1);var end=validated.EndDate??new DateTime(plan.Year,12,31);
        await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=site.DatabasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());
        await connection.OpenAsync(ct);await using var cmd=connection.CreateCommand();cmd.CommandTimeout=site.QuerySeconds;
        cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='EstimatedWpaValues'";
        var hasEstimates=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))>0;
        var source=hasEstimates?"CASE WHEN e.PlateAppearanceId IS NULL THEN '수집' ELSE 'FANZAI 추정' END":"'수집'";
        var sign=plan.Role=="pitcher"?"-p.WpaByPlate":"p.WpaByPlate";
        var order=plan.Metric=="play_wpa_abs"?$"ABS({sign})":sign;
        cmd.CommandText=$"""
            SELECT g.GameId,p.SequenceNumber,SUBSTR(g.GameDate,1,10),g.AwayTeamCode,g.HomeTeamCode,p.Inning,
                   p.BattingTeamCode,p.BatterName,p.PitcherName,p.ResultText,{sign},{source}
            FROM PlateAppearances p JOIN Games g ON g.GameId=p.GameId
            {(hasEstimates?"LEFT JOIN EstimatedWpaValues e ON e.PlateAppearanceId=p.PlateAppearanceId":"")}
            WHERE g.RoundCode='kbo_r' AND SUBSTR(g.GameDate,1,10) BETWEEN $start AND $end
              AND p.IsOfficial=1 AND p.WpaByPlate IS NOT NULL
              AND ($team IS NULL OR (CASE WHEN $pitcher=1 THEN CASE WHEN p.BattingTeamCode=g.HomeTeamCode THEN g.AwayTeamCode ELSE g.HomeTeamCode END ELSE p.BattingTeamCode END)=$team)
              AND ($player IS NULL OR INSTR(CASE WHEN $pitcher=1 THEN p.PitcherName ELSE p.BatterName END,$player)>0)
              AND ($inning IS NULL OR ($inning=10 AND p.Inning>=10) OR p.Inning=$inning)
            ORDER BY {order} {(plan.Descending?"DESC":"ASC")},g.GameId,p.SequenceNumber LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$start",start.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$end",end.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$team",(object?)plan.Team??DBNull.Value);cmd.Parameters.AddWithValue("$player",(object?)plan.Player??DBNull.Value);
        cmd.Parameters.AddWithValue("$pitcher",plan.Role=="pitcher"?1:0);
        cmd.Parameters.AddWithValue("$inning",plan.Inning is null?DBNull.Value:plan.Inning=="연장"?10:int.Parse(plan.Inning.TrimEnd('회'),CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$limit",plan.Limit);
        using var cancel=ct.Register(cmd.Cancel);await using var reader=await cmd.ExecuteReaderAsync(ct);
        var rows=new List<WebRow>();
        var names=new[]{"GameId","Sequence","Date","Away","Home","Inning","TeamCode","Batter","Pitcher","Result","WPA","WpaSource"};
        while(await reader.ReadAsync(ct))
        {
            var cells=new Dictionary<string,string>();for(int i=0;i<names.Length;i++)cells[names[i]]=reader.IsDBNull(i)?"—":Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture)!;
            rows.Add(new(cells["GameId"]+":"+cells["Sequence"],cells));
        }
        var labels=new[]{"경기 ID","타석 번호","날짜","원정","홈","회","공격 팀","타자","투수","결과","WPA(%p)","WPA 출처"};
        var columns=names.Select((n,i)=>new WebColumn(n,labels[i],"text",false)).ToArray();
        return new{answer=rows.Count==0?"조건에 맞는 WPA 타석 기록이 없습니다.":"개별 타석 WPA 기록을 조회했습니다.",clarification=false,columns,rows,asOf=asOf?.ToString("yyyy-MM-dd"),
            applied=$"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd} · 정규시즌 · {plan.Inning??"전체 회차"} · 팀 {plan.Team??"전체"} · 선수 {plan.Player??"전체"} · {(plan.Role=="pitcher"?"투수":"타자")} 시점 · {(plan.Metric=="play_wpa_abs"?"절댓값":"부호 있는 WPA")} {(plan.Descending?"내림차순":"오름차순")}",
            warnings=new[]{"WPA가 있는 공식 타석만 대상입니다. 개별 투구·도루 등 독립 주루 플레이 순위는 포함하지 않습니다.","수집 WPA를 우선 사용하고 누락분은 FANZAI 자체 추정값으로 보완합니다. 추정값은 KBO 득점분포 기반이며 무승부를 0.5승으로 처리합니다. 두 출처의 모델은 동일하지 않습니다.","WPA는 %p 단위입니다(표준 WPA는 표시값÷100). 투수 시점은 부호를 반전합니다. 미산출 기록은 0으로 취급하지 않습니다.","DB 수집 기준일 이후 경기는 포함되지 않습니다."}};
    }
}
