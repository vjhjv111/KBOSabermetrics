using System.Text.RegularExpressions;

namespace NaverSabermetrics.Web;

public static class QuestionTeams
{
    private static readonly (string Code,string Name,string[] Aliases)[] Teams = [
        ("HH","한화",["한화 이글스","한화","이글스","Hanwha Eagles","Hanwha","HH"]),
        ("HT","KIA",["KIA 타이거즈","기아 타이거즈","기아","KIA","타이거즈","HT"]),
        ("KT","KT",["KT 위즈","케이티 위즈","케이티","위즈","KT"]),
        ("LG","LG",["LG 트윈스","엘지 트윈스","엘지","트윈스","LG"]),
        ("LT","롯데",["롯데 자이언츠","롯데","자이언츠","Lotte Giants","Lotte","LT"]),
        ("NC","NC",["NC 다이노스","엔씨 다이노스","엔씨","다이노스","NC"]),
        ("OB","두산",["두산 베어스","두산","베어스","Doosan Bears","Doosan","OB"]),
        ("SK","SSG",["SSG 랜더스","에스에스지 랜더스","에스에스지","랜더스","SSG","SK"]),
        ("SS","삼성",["삼성 라이온즈","삼성","라이온즈","Samsung Lions","Samsung","SS"]),
        ("WO","키움",["키움 히어로즈","키움","히어로즈","Kiwoom Heroes","Kiwoom","WO"])
    ];
    public static string Mapping => string.Join("; ",Teams.Select(t=>$"{t.Name}={t.Code}"));
    private static string Pattern(string alias)=>"(?<![A-Za-z])"+string.Join(@"\s*",alias.Split(' ').Select(Regex.Escape))+"(?![A-Za-z])";
    public static string[] Mentions(string question)=>Teams.Where(t=>t.Aliases.Any(a=>Regex.IsMatch(question,Pattern(a),RegexOptions.IgnoreCase))).Select(t=>t.Code).ToArray();
    public static string? Canonical(string? value)=>value is null?null:Teams.FirstOrDefault(t=>t.Aliases.Any(a=>Regex.IsMatch(value,"^"+Pattern(a)+"$",RegexOptions.IgnoreCase))).Code
        ?? throw new RequestError("구단명을 확인해 주세요.",400,"AI_TEAM_MISMATCH");
    public static QuestionPlan Normalize(string question,QuestionPlan plan)
    {
        var team=Canonical(plan.Team);
        var filters=(plan.Filters??[]).Select(f=>f.Key=="opponent"?f with{Value=Canonical(f.Value)!}:f).ToArray();
        var mentions=Mentions(question);
        var opponents=filters.Where(f=>f.Key=="opponent").Select(f=>f.Value).ToArray();
        var opponentLanguage=Regex.IsMatch(question,@"상대|상대로|\bvs\b|\bagainst\b",RegexOptions.IgnoreCase);
        if(mentions.Length==1 && opponents.Length==0 && !opponentLanguage)
            return plan with{Team=mentions[0],Filters=filters}; // Explicit, unambiguous user team wins over a model guess.
        if(mentions.Length==1 && (opponents.Length>0 || opponentLanguage))
            throw new RequestError("상대 팀만 지정된 질문입니다. 기준 팀도 함께 적어 주세요.",400,"AI_TEAM_MISMATCH");
        if(team is not null && !mentions.Contains(team) || opponents.Any(o=>!mentions.Contains(o)))
            throw new RequestError("질문의 구단명과 조회 팀이 일치하지 않아 중단했습니다. 기준 팀과 상대 팀을 구분해 적어 주세요.",400,"AI_TEAM_MISMATCH");
        if(mentions.Length>1)
        {
            // Multiple clubs must not collapse into one club's records or swap their roles.
            var opponentMentions=Teams.Where(t=>t.Aliases.Any(a=>Regex.IsMatch(question,Pattern(a)+@"\s*(?:을|를)?\s*상대로",RegexOptions.IgnoreCase))).Select(t=>t.Code).Distinct().ToArray();
            if(mentions.Length!=2 || opponentMentions.Length!=1 || opponents.Length!=1 || opponents[0]!=opponentMentions[0] || team!=mentions.Single(x=>x!=opponentMentions[0]))
                throw new RequestError("기준 팀과 상대 팀을 명확히 적어 주세요. 예: 한화가 KIA를 상대로 기록한 홈런",400,"AI_TEAM_MISMATCH");
        }
        return plan with{Team=team,Filters=filters};
    }
    public static string Display(string applied)
    {
        foreach(var t in Teams)applied=Regex.Replace(applied,$@"(?<![A-Za-z]){t.Code}(?![A-Za-z])",t.Name);
        return applied;
    }
}
