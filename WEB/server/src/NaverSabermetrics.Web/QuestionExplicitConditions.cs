using System.Globalization;
using System.Text.RegularExpressions;

namespace NaverSabermetrics.Web;

// Recover unambiguous literal conditions before executing an AI-generated plan.
// Never infer an on-field position from a runner's base or silently merge alternatives.
public static class QuestionExplicitConditions
{
    public static readonly (string Name,string Code)[] Positions =
        [("포수","C"),("1루수","1B"),("2루수","2B"),("3루수","3B"),("유격수","SS"),("좌익수","LF"),("중견수","CF"),("우익수","RF"),("지명타자","DH")];
    public static string? Inning(string question)
    {
        var range=Regex.Match(question,@"(?<!\d)([147])\s*[~～\-–]\s*([369])\s*회");
        if(range.Success && int.Parse(range.Groups[2].Value)-int.Parse(range.Groups[1].Value)==2)
            return range.Groups[1].Value+"~"+range.Groups[2].Value+"회";
        var single=Regex.Match(question,@"(?<!\d)([1-9])\s*회");
        return single.Success?single.Groups[1].Value+"회":question.Contains("연장")?"연장":null;
    }
    public static QuestionPlan Normalize(string question,QuestionPlan plan)
    {
        var filters=(plan.Filters??[]).ToList();
        void Set(string key,string value){filters.RemoveAll(f=>f.Key==key);filters.Add(new(key,value));}
        void Match(string pattern,string key,Func<Match,string>? value=null)
        {
            var matches=Regex.Matches(question,pattern,RegexOptions.IgnoreCase);
            var values=matches.Select(m=>value?.Invoke(m)??m.Groups[1].Value).Distinct().ToArray();
            if(values.Length>1)throw new RequestError("서로 다른 "+key+" 조건이 여러 개입니다. 한 조건으로 지정해 주세요.",400,"AI_FILTER_MISMATCH");
            if(values.Length==1)Set(key,values[0]);
        }
        var positions=Positions.Where(p=>question.Contains(p.Name)||Regex.IsMatch(question,@"포지션\s*[:：]?\s*"+p.Code+@"(?![A-Za-z0-9])",RegexOptions.IgnoreCase)).ToArray();
        if(positions.Length>1)throw new RequestError("포지션은 한 개를 지정해 주세요.",400,"AI_FILTER_MISMATCH");
        if(positions.Length==1)Set("position",positions[0].Code);
        Match(@"(국내|외국인\+아쿼|외국인|아시아쿼터)","nationality");
        if(question.Contains("신인왕"))Set("rookieEligible","true");
        var qualified=Regex.Match(question,@"규정\s*(?:타석|이닝)?\s*(\d+(?:\.\d+)?)\s*%");
        if(qualified.Success)Set("qualificationPercent",qualified.Groups[1].Value);
        else if(Regex.IsMatch(question,@"규정\s*(타석|이닝)(?!\s*(?:무관|제외))"))Set("qualificationPercent","100");
        Match(@"([월화수목금토일])요일","weekday");
        Match(@"(?<!\d)([1-9])\s*번\s*(?:타자|타순)","batOrder");
        Match(@"타순\s*[:：]?\s*([1-9])","batOrder");
        Match(@"최근\s*(\d+)\s*경기","recentGames");
        Match(@"최근\s*(\d+)\s*일","recentDays");
        Match(@"([012])\s*아웃","outs");
        if(question.Contains("무사"))Set("outs","0");
        Match(@"([0-3])\s*볼","balls");
        Match(@"([0-2])\s*스트라이크","strikes");
        Match(@"(득점권|만루|주자 없음)","runners");
        Match(@"주자\s*[:：]?\s*([123](?:\s*[·,/]\s*[123])?)루", "runners",m=>Regex.Replace(m.Groups[1].Value,@"\s","").Replace(',','·').Replace('/','·')+"루");
        Match(@"([123])\s*점\s*차\s*이내","score",m=>m.Groups[1].Value+"점차 이내");
        Match(@"(동점|(?:[12]점 |3점 이상 )?(?:리드|열세))","score");
        if(question.Contains("원정"))Set("venue","원정");
        if(Regex.IsMatch(question,@"홈\s*(?:경기|에서|성적|기록)|(?:장소|구분)\s*[:：]\s*홈"))Set("venue","홈");
        foreach(var value in new[]{"정규시즌","시범경기","포스트시즌","올스타전"})if(question.Contains(value))Set("competition",value);
        if(question.Contains("통산"))Set("room","career");
        var minimum=Regex.Match(question,@"최소\s*(?:이닝|타석)?\s*(?:은|는|[:：])?\s*(\d+(?:\.\d+)?)|(?:타석|이닝)\s*[:：]\s*(\d+(?:\.\d+)?)\s*이상|(\d+(?:\.\d+)?)\s*(?:이닝|타석)\s*이상");
        if(minimum.Success)plan=plan with{MinimumVolume=double.Parse(minimum.Groups.Cast<Group>().Skip(1).First(g=>g.Success).Value,CultureInfo.InvariantCulture)};
        var conditions=(plan.Conditions??[]).ToList();
        var thresholdNames=ViewRegistry.Views.Where(v=>v.Role==plan.Role)
            .SelectMany(v=>ViewRegistry.Properties(v).Where(ViewRegistry.IsNumber))
            .SelectMany(p=>new[]{(Name:p.Name,Stat:p.Name),(Name:ViewRegistry.Label(p),Stat:p.Name)})
            .Where(p=>p.Name.Length>=2&&p.Stat is not ("PA" or "InningsPitched"))
            .Distinct().GroupBy(p=>p.Name,StringComparer.OrdinalIgnoreCase);
        foreach(var names in thresholdNames)
        {
            var matches=Regex.Matches(question,@"(?<![A-Za-z])"+Regex.Escape(names.Key)+@"\s*[:：]?\s*(\d+(?:\.\d+)?)\s*(?:개|회|점|km/h)?\s*(이상|이하|초과|미만)",RegexOptions.IgnoreCase);
            if(matches.Count==0)continue;
            var stats=names.Select(n=>n.Stat).Distinct().ToArray();
            if(stats.Length!=1)throw new RequestError("스탯 조건의 지표가 모호합니다: "+names.Key,400,"AI_FILTER_MISMATCH");
            conditions.RemoveAll(c=>c.Stat==stats[0]);
            foreach(Match m in matches)conditions.Add(new(stats[0],m.Groups[2].Value switch{"이상"=>"gte","이하"=>"lte","초과"=>"gt",_=>"lt"},double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)));
        }
        var low=Regex.IsMatch(question,@"낮|적은|최저|최소\s*(?:OPS|ERA|WHIP)|하위",RegexOptions.IgnoreCase);
        var high=Regex.IsMatch(question,@"높|많은|최고|최다|상위",RegexOptions.IgnoreCase);
        if(low&&high)throw new RequestError("정렬 방향을 하나로 지정해 주세요.",400,"AI_FILTER_MISMATCH");
        // A bare OPS request defaults to descending; ERA/WHIP to ascending.
        var descending=high?true:low?false:plan.Metric is "era" or "ERA" or "whip" or "WHIP"?false:true;
        return plan with{Filters=filters.ToArray(),Conditions=conditions.Distinct().ToArray(),Inning=Inning(question),Descending=descending};
    }
}
