using System.Globalization;
using System.Text.RegularExpressions;

namespace NaverSabermetrics.Web;

public static class QuestionCapabilities
{
    public static bool ExplicitTeamTotals(string question)=>
        Regex.IsMatch(question,@"(?:10|열)\s*(?:개\s*)?(?:구단|팀)|(?:전체|모든|각)\s*(?:구단|팀)|(?:구단|팀)\s*별")
        && !Regex.IsMatch(question,@"선수|개인|[0-9]+\s*명|플레이|타석별");
    public static QuestionPlan NormalizeAggregation(string question,QuestionPlan plan)
    {
        if(!ExplicitTeamTotals(question))return plan;
        if(plan.Player is not null || plan.Metric.StartsWith("play_",StringComparison.Ordinal))
            throw new RequestError("팀별 집계 질문이 개인 기록으로 해석되어 조회를 중단했습니다.",400,"AI_FILTER_MISMATCH");
        var filters=(plan.Filters??[]).Where(f=>f.Key!="room").Append(new QuestionFilter("room","team")).ToArray();
        var allTeams=Regex.IsMatch(question,@"(?:10|열)\s*(?:개\s*)?(?:구단|팀)|(?:전체|모든)\s*(?:구단|팀)");
        var explicitRanking=Regex.IsMatch(question,@"(?:상위|하위|최고|최저|TOP)\s*[0-9]+",RegexOptions.IgnoreCase);
        return plan with{Filters=filters,Team=allTeams?null:plan.Team,Limit=allTeams&&!explicitRanking?10:plan.Limit};
    }
    public static readonly string[] FilterKeys=["room","competition","opponent","venue","stadium","outs","runners","score","balls","strikes","recentGames","recentDays"];
    public static ViewDefinition Resolve(string role,string metric)=>ViewRegistry.Views.FirstOrDefault(v=>v.Role==role && ViewRegistry.Properties(v).Any(p=>p.Name==metric&&ViewRegistry.IsNumber(p)))
        ?? throw new RequestError("기록실에서 확인할 수 없는 지표입니다. 지표 이름을 구체적으로 적어 주세요.",400,"AI_FILTER");
    public static string Describe(string question)
    {
        var q=Regex.Replace(question,@"\s","").ToLowerInvariant();
        return string.Join(";",ViewRegistry.Views.Where(v=>v.Role is "batter" or "pitcher")
            .SelectMany(v=>ViewRegistry.Properties(v).Where(ViewRegistry.IsNumber).Select(p=>new{v.Role,p.Name,Label=ViewRegistry.Label(p)}))
            .Distinct().Select(p=>new{p,Score=(q.Contains(p.Name.ToLowerInvariant())?4:0)+(q.Contains(Regex.Replace(p.Label,@"\s","").ToLowerInvariant())?3:0)})
            .Where(x=>x.Score>0).OrderByDescending(x=>x.Score).Take(24).Select(x=>$"{x.p.Role}:{x.p.Name}={x.p.Label}"));
    }
    public static RecordRequest Apply(RecordRequest r,QuestionFilter[] filters)
    {
        if(filters is null || filters.Length>FilterKeys.Length || filters.Select(x=>x.Key).Distinct().Count()!=filters.Length)throw new RequestError("조회 조건이 중복되거나 잘못되었습니다.");
        foreach(var f in filters)
        {
            if(f is null || !FilterKeys.Contains(f.Key)||string.IsNullOrWhiteSpace(f.Value)||f.Value.Length>80)throw new RequestError("조회 조건을 확인해 주세요.");
            int Number()=>int.TryParse(f.Value,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n:throw new RequestError("상황 숫자를 확인해 주세요.");
            r=f.Key switch{
                "room"=>r with{Room=f.Value},"competition"=>r with{Competition=f.Value},"opponent"=>r with{Opponent=f.Value},"venue"=>r with{Venue=f.Value},"stadium"=>r with{Stadium=f.Value},
                "outs"=>r with{Outs=Number()},"runners"=>r with{Runners=f.Value},"score"=>r with{Score=f.Value},"balls"=>r with{Balls=Number()},"strikes"=>r with{Strikes=Number()},
                "recentGames"=>r with{RecentGames=Number()},"recentDays"=>r with{RecentDays=Number()},_=>throw new RequestError("조회 조건을 확인해 주세요.")};
        }
        if(r.Room is not ("season" or "career" or "team"))throw new RequestError("기록실 구분을 확인해 주세요.");
        return r.Room=="career"?r with{Year=null}:r;
    }
    public static void ValidateFilters(string question,QuestionPlan plan)
    {
        var filters=plan.Filters??[];
        void Expect(string key,string value){if(!filters.Any(f=>f.Key==key&&f.Value==value))throw new RequestError($"질문의 {value} 조건이 해석 결과와 달라 조회를 중단했습니다.",400,"AI_FILTER_MISMATCH");}
        foreach(var runner in new[]{"득점권","만루","주자 없음"})if(question.Contains(runner))Expect("runners",runner);
        var outs=Regex.Match(question,@"([012])\s*아웃");if(outs.Success)Expect("outs",outs.Groups[1].Value);
        if(question.Contains("무사"))Expect("outs","0");
        if(question.Contains("통산"))Expect("room","career");
        if(question.Contains("포스트시즌"))Expect("competition","포스트시즌");
        if(question.Contains("시범경기"))Expect("competition","시범경기");
        if(question.Contains("원정"))Expect("venue","원정");
    }
}
