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
        var sign=plan.Role=="pitcher"?"-p.WpaByPlate":"p.WpaByPlate";
        var order=plan.Metric=="play_wpa_abs"?$"ABS({sign})":sign;
        cmd.CommandText=$"""
            SELECT g.GameId,p.SequenceNumber,SUBSTR(g.GameDate,1,10),g.AwayTeamCode,g.HomeTeamCode,p.Inning,
                   p.BattingTeamCode,p.BatterName,p.PitcherName,p.ResultText,{sign}
            FROM PlateAppearances p JOIN Games g ON g.GameId=p.GameId
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
        var names=new[]{"GameId","Sequence","Date","Away","Home","Inning","TeamCode","Batter","Pitcher","Result","WPA"};
        while(await reader.ReadAsync(ct))
        {
            var cells=new Dictionary<string,string>();for(int i=0;i<names.Length;i++)cells[names[i]]=reader.IsDBNull(i)?"—":Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture)!;
            rows.Add(new(cells["GameId"]+":"+cells["Sequence"],cells));
        }
        var labels=new[]{"경기 ID","타석 번호","날짜","원정","홈","회","공격 팀","타자","투수","결과","WPA(원본 단위)"};
        var columns=names.Select((n,i)=>new WebColumn(n,labels[i],"text",false)).ToArray();
        return new{answer=rows.Count==0?"조건에 맞는 WPA 타석 기록이 없습니다.":"개별 타석 WPA 원본 기록을 조회했습니다.",clarification=false,columns,rows,asOf=asOf?.ToString("yyyy-MM-dd"),
            applied=$"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd} · 정규시즌 · {plan.Inning??"전체 회차"} · 팀 {plan.Team??"전체"} · 선수 {plan.Player??"전체"} · {(plan.Role=="pitcher"?"투수":"타자")} 시점 · {(plan.Metric=="play_wpa_abs"?"절댓값":"부호 있는 WPA")} {(plan.Descending?"내림차순":"오름차순")}",
            warnings=new[]{"WPA가 수집된 공식 타석만 대상입니다. 개별 투구·도루 등 독립 주루 플레이 순위는 포함하지 않습니다.","타자 시점 원본 WpaByPlate를 사용하며 투수 시점은 부호를 반전합니다. 누락 데이터는 0으로 취급하지 않습니다.","DB 수집 기준일 이후 경기는 포함되지 않습니다."}};
    }
}
