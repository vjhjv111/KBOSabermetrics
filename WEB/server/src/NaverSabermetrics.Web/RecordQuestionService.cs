using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverSabermetrics.Web;

public sealed class RecordQuestionOptions
{
    public bool Enabled { get; set; }
    public string Model { get; set; } = "gpt-4.1-mini";
    public int DailyLimit { get; set; } = 1000;
    public int PerIpDailyLimit { get; set; } = 10;
    public int MaxInputTokens { get; set; } = 3000;
}
public sealed record RecordQuestion(string Question);
public sealed record QuestionFilter(string Key,string Value);
public sealed record QuestionPlan
{
    public required bool Supported { get; init; }
    public required string Clarification { get; init; }
    public required string Role { get; init; }
    public required int Year { get; init; }
    public required string? StartDate { get; init; }
    public required string? EndDate { get; init; }
    public required string? Team { get; init; }
    public required string? Player { get; init; }
    public required string Metric { get; init; }
    public required bool Descending { get; init; }
    public required double MinimumVolume { get; init; }
    public required string? Inning { get; init; }
    public required QuestionFilter[] Filters { get; init; }
    public required int Limit { get; init; }
    public StatCondition[] Conditions { get; init; } = [];
}
public interface IRecordQuestionPlanner
{
    bool Ready { get; }
    Task<QuestionPlan> PlanAsync(string question, int[] years, CancellationToken ct);
}

// Only a schema-constrained query plan crosses the model boundary. No SQL or DB rows.
public sealed class OpenAiRecordPlanner(RecordQuestionOptions options, HttpMessageHandler? handler = null) : IRecordQuestionPlanner, IDisposable
{
    private readonly HttpClient http = new(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(40) };
    public bool Ready => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
    public static QuestionPlan Parse(string json) => JsonSerializer.Deserialize<QuestionPlan>(json,new JsonSerializerOptions {
        PropertyNamingPolicy=JsonNamingPolicy.CamelCase, UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow
    }) ?? throw new JsonException("Missing plan");
    public async Task<QuestionPlan> PlanAsync(string question, int[] years, CancellationToken ct)
    {
        if (!Ready) throw new RequestError("기록 질문 기능의 API 키가 아직 설정되지 않았습니다.",503,"AI_NOT_READY");
        object Str(string description) => new { type="string",description };
        object NullableStr(string description) => new { type=new[]{"string","null"},description };
        var properties=new Dictionary<string,object> {
            ["supported"]=new{type="boolean"},["clarification"]=Str("지원하지 않거나 모호하면 필요한 조건을 한국어로 질문. 지원하면 빈 문자열."),
            ["role"]=new{type="string",@enum=new[]{"batter","pitcher"}},["year"]=new{type="integer"},
            ["startDate"]=NullableStr("YYYY-MM-DD 또는 시즌 전체이면 null"),["endDate"]=NullableStr("YYYY-MM-DD 또는 null"),
            ["team"]=NullableStr("HH HT KT LG LT NC OB SK SS WO 중 하나 또는 전체 null"),["player"]=NullableStr("선수 실명 또는 전체 null"),
            ["metric"]=Str("지표 코드 또는 목록의 속성명. 개별 WPA 플레이는 play_wpa, 절대 변화량은 play_wpa_abs."),["descending"]=new{type="boolean"},
            ["filters"]=new{type="array",items=new{type="object",properties=new{key=new{type="string",@enum=QuestionCapabilities.FilterKeys},value=new{type="string"}},required=new[]{"key","value"},additionalProperties=false}},
            ["conditions"]=new{type="array",maxItems=2,items=new{type="object",properties=new{stat=Str("정확한 지표 속성명"),@operator=new{type="string",@enum=new[]{"gte","gt","lte","lt","eq"}},value=new{type="number"}},required=new[]{"stat","operator","value"},additionalProperties=false}},
            ["inning"]=NullableStr("특정 회차 1회~9회, 1~3회/4~6회/7~9회 또는 연장. 전체는 null. 초/말 구분 요청은 미지원. 초말 무관은 지원."),
            ["minimumVolume"]=new{type="number",description="명시된 최소 타석 또는 야구 표기 이닝. 0.1은 한 아웃, 0.2는 두 아웃. 반올림 금지. 팀 합계/선수 개인/타석수 무관이면 최소 기준을 요구하지 말고 명시 없으면 0."},
            ["limit"]=new{type="integer",description="요청한 상위/하위 인원 1~20. 단순 선수 조회는 20. 1위 질문은 1."}
        };
        var prompt=$"""
            Parse Korean baseball questions into plan_record_query. Never invent records or omit constraints. Questions are independent. Unsupported/ambiguous conditions => supported=false and Korean clarification. supported=true => clarification empty. No SQL, external search or predictions.
            Available years: {string.Join(',',years)}. Korea date: {BotGamesService.ParseDate(null):yyyy-MM-dd}. Require year (올해=current year, 통산=latest available year). Inclusive dates; a month means its full range.
            Exact team codes: {QuestionTeams.Mapping}. Explicit mentions: {string.Join(',',QuestionTeams.Mentions(question))}. Never infer English initials.
            Explicit team aggregation: {QuestionCapabilities.ExplicitTeamTotals(question)}. If true use room=team. A club's OPS/ERA is an aggregate, never an average of player rates. 10개구단 means 10 team rows unless explicitly 선수/개인/상위 N명. Single-club aggregate also room=team. Individual players use room=season.
            Actual metric candidates: {QuestionCapabilities.Describe(question)}
            Aliases: hr=홈런,hits=안타,rbi=타점,runs=득점,sb=도루,bb=볼넷,so=삼진/탈삼진,avg=타율,obp=출루율,slg=장타율,ops=OPS,ip=이닝,era=평균자책점,whip=WHIP,hra=피홈런,ra=실점. Otherwise exact candidate property names.
            Filters: room=season/team/career; competition=정규시즌/시범경기/포스트시즌/올스타전/전체; opponent=team code; venue=홈/원정; stadium=venue name; position=C/1B/2B/3B/SS/LF/CF/RF/DH (포수/1루수/2루수/3루수/유격수/좌익수/중견수/우익수/지명타자); nationality=국내/외국인/아시아쿼터/외국인+아쿼; rookieEligible=true; qualificationPercent=0..100; weekday=월/화/수/목/금/토/일; batOrder=1..9; playerCode=ID.
            Situation filters: outs=0/1/2; runners=주자 없음/1루/2루/3루/1·2루/1·3루/2·3루/만루/득점권; score=동점/리드/열세/1점 리드/2점 리드/3점 이상 리드/1점 열세/2점 열세/3점 이상 열세/1점차 이내/2점차 이내/3점차 이내; balls=0..3 AND strikes=0..2; recentGames=5/10/20/30 OR recentDays=7/14/30/60/90. Preserve position even with RISP; it means period primary position, not at-bat defensive position.
            Optional view filter tab keys: basic,advanced,value,extended,clutch,power,team-batting,steal,baserunning,batted-ball,direction,discipline,pitch-types,wp,runner,starter,reliever. Use if a specific tab is requested. conditions: up to 2 additional numeric thresholds using exact stat/operator/value; exclude minimumVolume.
            inning=1회..9회/1~3회/4~6회/7~9회/연장 or null. 초말 무관 is supported; individual 초/말 unsupported. minimumVolume preserves baseball notation (0.1=one out,0.2=two), never round. Default 0. Only individual AVG/OBP/SLG/OPS/ERA/WHIP rankings need minimum or qualification; waive if 타석수/이닝 무관. Team totals or named player need no minimum.
            Highest/most descending=true; lowest/least false. Default descending except ERA/WHIP ascending. Explicit highest ERA=true. limit=1..20; 1위=1.
            Individual WPA play: play_wpa=signed, play_wpa_abs=biggest absolute swing; pitcher viewpoint role=pitcher. Supports year/date/team/player/inning ONLY: filters=[],conditions=[],minimumVolume=0. Aggregate player WPA=Wpa. Never substitute aggregate for plays.
            """;
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.openai.com/v1/responses");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        var payload=new {model=options.Model,store=false,max_output_tokens=900,parallel_tool_calls=false,
            instructions=prompt,input=question,tool_choice=new{type="function",name="plan_record_query"},
            tools=new[]{new{type="function",name="plan_record_query",description="검증할 기록 조회 조건을 반환합니다.",strict=true,
                parameters=new{type="object",properties,required=properties.Keys.ToArray(),additionalProperties=false}}}};
        request.Content=JsonContent.Create(payload);
        try
        {
            // Count the same instructions, question and tool schema before generating any response.
            using var countRequest=new HttpRequestMessage(HttpMethod.Post,"https://api.openai.com/v1/responses/input_tokens");
            countRequest.Headers.Authorization=request.Headers.Authorization;
            countRequest.Content=JsonContent.Create(new {payload.model,payload.instructions,payload.input,payload.tools,payload.tool_choice,payload.parallel_tool_calls});
            using var countResponse=await http.SendAsync(countRequest,ct);
            if(!countResponse.IsSuccessStatusCode)throw new RequestError("입력 길이를 확인하지 못해 AI 요청을 중단했습니다. 관리자에게 문의해 주세요.",503,"AI_TOKEN_CHECK");
            using var countDoc=JsonDocument.Parse(await countResponse.Content.ReadAsStringAsync(ct));
            if(!countDoc.RootElement.TryGetProperty("input_tokens",out var countValue)||!countValue.TryGetInt32(out var count)||count<0)
                throw new RequestError("입력 길이를 확인하지 못해 AI 요청을 중단했습니다.",503,"AI_TOKEN_CHECK");
            if(count>options.MaxInputTokens)throw new RequestError($"전체 입력이 {options.MaxInputTokens:N0}토큰 한도를 넘었습니다. 질문을 짧게 줄여 주세요.",400,"AI_INPUT_TOO_LONG");
            using var response=await http.SendAsync(request,ct);
            if(!response.IsSuccessStatusCode)throw new RequestError(response.StatusCode==System.Net.HttpStatusCode.TooManyRequests?
                "AI 사용량 또는 결제 한도를 확인해 주세요. 잠시 후 다시 시도해 주세요.":"AI 연결 설정을 확인해 주세요. 관리자에게 문의해 주세요.",503,"AI_PROVIDER_ERROR");
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if(doc.RootElement.GetProperty("status").GetString()!="completed")throw new JsonException("Incomplete response");
            var calls=doc.RootElement.GetProperty("output").EnumerateArray().Where(x=>x.GetProperty("type").GetString()=="function_call").ToArray();
            if(calls.Length!=1||calls[0].GetProperty("name").GetString()!="plan_record_query")throw new JsonException("Unexpected tool");
            return Parse(calls[0].GetProperty("arguments").GetString()!);
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new RequestError("AI 응답 시간이 초과되었습니다. 다시 시도해 주세요.",504,"AI_TIMEOUT");}
        catch(HttpRequestException){throw new RequestError("AI 서버에 연결하지 못했습니다.",503,"AI_PROVIDER_ERROR");}
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException){throw new RequestError("질문을 조회 조건으로 해석하지 못했습니다. 연도와 지표를 구체적으로 적어 주세요.",502,"AI_PLAN_ERROR");}
    }
    public void Dispose()=>http.Dispose();
}

public sealed class RecordQuestionService(RecordQuestionOptions options,SiteOptions site,DatabaseCacheService db,
    RecordService records,QueryGate gate,IRecordQuestionPlanner planner)
{
    public static readonly Dictionary<string,string> Metrics=new() {
        ["hr"]="HomeRuns",["hits"]="Hits",["rbi"]="RunsBattedIn",["runs"]="Runs",["sb"]="StolenBases",
        ["bb"]="Walks",["so"]="Strikeouts",["avg"]="AVG",["obp"]="OBP",["slg"]="SLG",["ops"]="OPS",
        ["ip"]="InningsPitched",["era"]="ERA",["whip"]="WHIP",["hra"]="HomeRunsAllowed",["ra"]="RunsAllowed"
    };
    private readonly SemaphoreSlim serial=new(1,1);
    public bool Ready=>options.Enabled && planner.Ready;
    public object Status()=>new{enabled=Ready,message=Ready?"연도·기간·선수·팀별 기본 기록을 질문해 보세요.":"기록 질문 기능은 준비 중입니다. 서버의 API 키와 활성화 설정이 필요합니다."};
    public static RecordRequest ToRequest(QuestionPlan p,SiteOptions site,int[] years,bool minimumWaived=false)
    {
        var metric=Metrics.GetValueOrDefault(p.Metric,p.Metric);
        var view=QuestionCapabilities.Resolve(p.Role,metric);
        if(!p.Supported || !years.Contains(p.Year)||p.Limit is <1 or >20||!double.IsFinite(p.MinimumVolume)||p.MinimumVolume is <0 or >2000)
            throw new RequestError("지원하는 연도·지표·조회 범위를 확인해 주세요.",400,"AI_FILTER");
        if(p.Role=="batter" && p.MinimumVolume!=Math.Truncate(p.MinimumVolume))throw new RequestError("타석 기준은 정수로 입력해 주세요.");
        if(p.Team is not null&&!new[]{"HH","HT","KT","LG","LT","NC","OB","SK","SS","WO"}.Contains(p.Team))throw new RequestError("팀을 확인해 주세요.");
        if(p.Player is not null&&(string.IsNullOrWhiteSpace(p.Player)||p.Player.Length>40||p.Player.Any(char.IsControl)))throw new RequestError("선수 이름을 확인해 주세요.");
        DateTime? Date(string? s) {
            if(s is null)return null;
            if(!DateTime.TryParseExact(s,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)||d.Year!=p.Year)throw new RequestError("조회 기간은 선택한 연도 안에서 지정해 주세요.");
            return d;
        }
        if((p.StartDate is null)!=(p.EndDate is null))throw new RequestError("조회 시작일과 종료일을 함께 지정해 주세요.");
        var r=new RecordRequest{Year=p.Year,Role=p.Role,View=view.Key,Team=p.Team,PlayerName=p.Player,Inning=p.Inning,StartDate=Date(p.StartDate),EndDate=Date(p.EndDate),
            SortBy=metric,Descending=p.Descending,PageSize=Math.Min(50,site.MaxPageSize),
            Conditions=p.MinimumVolume>0?[new(p.Role=="batter"?"PA":"InningsPitched","gte",p.MinimumVolume)]:[]};
        r=QuestionCapabilities.Apply(r,p.Filters);
        if(p.Conditions is null || p.Conditions.Length>2 || p.Conditions.Any(c=>c is null||string.IsNullOrWhiteSpace(c.Stat)))throw new RequestError("스탯 조건은 최대 2개입니다.");
        r=r with{Conditions=r.Conditions.Concat(p.Conditions.Select(c=>c with{Stat=Metrics.GetValueOrDefault(c.Stat,c.Stat)})).Distinct().ToList()};
        r=r with{View=QuestionCapabilities.ResolveRequest(r,p.Filters.Any(f=>f.Key=="view")).Key};
        if(r.Room!="team"&&r.QualificationPercent==0&&!minimumWaived&&new[]{"AVG","OBP","SLG","OPS","ERA","WHIP"}.Contains(metric)&&p.Player is null&&p.MinimumVolume==0)
            throw new RequestError("선수별 비율 순위는 최소 타석·이닝을 알려 주시거나 '타석수/이닝 무관'을 명시해 주세요.",400,"AI_FILTER");
        r.Validate(site);return r;
    }
    public static bool MinimumWaived(string question)=>Regex.IsMatch(question,@"(?:타석|이닝)\s*(?:수|수는|은|는)?\s*(?:무관|상관\s*없|제한\s*없)|최소\s*기준\s*(?:없|무관)");
    public static string? UnsupportedQuestion(string question)
    {
        if(Regex.IsMatch(question,@"초에|말에|회\s*[초말]"))return "초말을 나눈 조건은 아직 연결되지 않았습니다. 초·말 합산 결과로 대체하지 않고 조회를 중단합니다.";
        var remainder=Regex.Replace(question,@"(?<!\d)(?:1\s*[~～\-–]\s*3|4\s*[~～\-–]\s*6|7\s*[~～\-–]\s*9)\s*회","");
        if(Regex.IsMatch(remainder,@"\d+\s*[~～\-–]\s*\d+\s*회"))return "회차 구간은 1~3회, 4~6회, 7~9회를 지원합니다.";
        var innings=Regex.Matches(remainder,@"(?<!\d)(\d+)\s*회");
        if(innings.Count>1 || innings.Any(m=>int.Parse(m.Groups[1].Value) is <1 or >9))return "회차는 1~9회 중 하나 또는 연장 전체로 지정해 주세요.";
        return null;
    }
    public static void ValidateQuestion(string question,QuestionPlan plan)
    {
        if(Regex.IsMatch(question,@"플레이|타석별") && !plan.Metric.StartsWith("play_",StringComparison.Ordinal))throw new RequestError("개별 플레이 질문을 선수 합계로 해석하여 조회를 중단했습니다.",400,"AI_FILTER_MISMATCH");
        QuestionCapabilities.ValidateFilters(question,plan);
        var explicitYear=Regex.Match(question,@"(?<!\d)(20\d{2})\s*(?:년|시즌)");
        if(explicitYear.Success && plan.Year!=int.Parse(explicitYear.Groups[1].Value))throw new RequestError("질문의 연도와 조회 연도가 달라 중단했습니다.",400,"AI_FILTER_MISMATCH");
        var month=Regex.Match(question,@"(?<!\d)(1[0-2]|[1-9])\s*월(?!\s*\d+\s*일)");
        if(month.Success && Regex.Matches(question,@"\d+\s*월").Count==1 && !Regex.IsMatch(question,@"이후|이전|부터|까지"))
        {
            var m=int.Parse(month.Groups[1].Value);var start=new DateTime(plan.Year,m,1);var end=start.AddMonths(1).AddDays(-1);
            if(plan.StartDate!=start.ToString("yyyy-MM-dd")||plan.EndDate!=end.ToString("yyyy-MM-dd"))throw new RequestError("질문의 월별 기간이 조회 조건과 달라 중단했습니다.",400,"AI_FILTER_MISMATCH");
        }
        var expected=QuestionExplicitConditions.Inning(question);
        if(plan.Inning!=expected)throw new RequestError("질문의 회차 조건이 정확히 해석되지 않아 조회를 중단했습니다. 예: 2026년 9회 최소 0.1이닝 ERA 높은 투수 5명",400,"AI_FILTER_MISMATCH");
        var minimum=Regex.Match(question,@"최소\s*(?:이닝|타석)?\s*(?:은|는|:)?\s*([0-9]+(?:\.[0-9]+)?)|([0-9]+(?:\.[0-9]+)?)\s*(?:이닝|타석)\s*이상");
        if(minimum.Success && Math.Abs(double.Parse(minimum.Groups[1].Success?minimum.Groups[1].Value:minimum.Groups[2].Value,CultureInfo.InvariantCulture)-plan.MinimumVolume)>1e-8)
            throw new RequestError("최소 타석·이닝 조건이 원문과 달라 조회를 중단했습니다. 기준을 다시 명시해 주세요.",400,"AI_FILTER_MISMATCH");
    }
    private sealed record Usage(string Date,int Total,Dictionary<string,int> Clients);
    private async Task ReserveAsync(string ip,CancellationToken ct,bool authenticatedBot)
    {
        var day=BotGamesService.ParseDate(null).ToString("yyyy-MM-dd");
        var path=Path.Combine(site.StateDirectory,"record-question-usage.json");
        // Corrupt state fails closed instead of resetting a spending limit.
        var usage=File.Exists(path)?JsonSerializer.Deserialize<Usage>(await File.ReadAllTextAsync(path,ct)):null;
        if(usage?.Date!=day)usage=new(day,0,new());
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(day+"|"+ip)));
        var count=usage!.Clients.GetValueOrDefault(key);
        if(usage.Total>=options.DailyLimit||(!authenticatedBot&&count>=options.PerIpDailyLimit))throw new RequestError("오늘의 기록 질문 이용 한도에 도달했습니다.",429,"AI_LIMIT");
        if(!authenticatedBot)usage.Clients[key]=count+1;
        usage=usage with{Total=usage.Total+1};
        Directory.CreateDirectory(site.StateDirectory);
        await File.WriteAllTextAsync(path+".tmp",JsonSerializer.Serialize(usage),ct);File.Move(path+".tmp",path,true);
    }
    public async Task<object> AskAsync(string question,string ip,CancellationToken ct,bool authenticatedBot=false)
    {
        if(!Ready)throw new RequestError("기록 질문 기능의 API 키와 활성화 설정이 필요합니다.",503,"AI_NOT_READY");
        if(string.IsNullOrWhiteSpace(question)||question.Length>600||question.Any(c=>char.IsControl(c)&&c!='\n'))throw new RequestError("질문은 1~600자로 입력해 주세요.");
        if(UnsupportedQuestion(question) is {} unsupported)return new{answer=unsupported,clarification=true};
        if(!await serial.WaitAsync(0,ct))throw new RequestError("다른 기록 질문을 처리 중입니다. 잠시 후 다시 시도해 주세요.",429,"AI_BUSY");
        try
        {
            var catalog=await db.GetCatalogAsync(ct);var years=catalog.Years.ToArray();
            await ReserveAsync(ip,ct,authenticatedBot);
            var plan=await planner.PlanAsync(question.Trim(),years,ct);
            if(!plan.Supported)return new{answer=string.IsNullOrWhiteSpace(plan.Clarification)?"연도·기간·조회할 기본 지표를 구체적으로 알려 주세요.":plan.Clarification[..Math.Min(500,plan.Clarification.Length)],clarification=true};
            plan=QuestionExplicitConditions.Normalize(question,plan);
            plan=QuestionTeams.Normalize(question,plan);
            plan=QuestionCapabilities.NormalizeAggregation(question,plan);
            if(MinimumWaived(question))plan=plan with{MinimumVolume=0};
            ValidateQuestion(question,plan);
            if(plan.Metric is "play_wpa" or "play_wpa_abs")return await gate.RunAsync(t=>QuestionPlays.QueryAsync(plan,site,years,catalog.MaxGameDate,t),ct);
            var request=ToRequest(plan,site,years,MinimumWaived(question));
            TablePage page;
            try{page=await gate.RunAsync(t=>records.QueryAsync(request,t),ct);}
            catch(RequestError e) when(e.Code=="QUERY_BUSY"){throw new RequestError("기록 조회가 몰려 있습니다. 잠시 후 다시 시도해 주세요.",429,"AI_DB_BUSY");}
            var metric=request.SortBy!;var label=page.Columns.First(x=>x.Key==metric).Label;
            var valid=page.Rows.Where(x=>x.Cells.TryGetValue(metric,out var v)&&v is not ("-" or "—" or "")).ToArray();
            var selected=valid.Take(plan.Limit).ToList();
            // Count stats are exact; extend ties at the cutoff without treating rounded rates as equal.
            var isCount=new[]{"hr","hits","rbi","runs","sb","bb","so","hra"}.Contains(plan.Metric);
            if(isCount&&selected.Count>0)selected.AddRange(valid.Skip(selected.Count).TakeWhile(x=>x.Cells[metric]==selected[^1].Cells[metric]));
            var keys=new[]{"Name","TeamCode","PrimaryPosition",metric,plan.Role=="batter"?"PA":"InningsPitched"}.Distinct().Where(k=>page.Columns.Any(c=>c.Key==k)).ToArray();
            var columns=keys.Select(k=>page.Columns.First(x=>x.Key==k)).ToArray();
            var rows=selected.Select(x=>new WebRow(x.EntityCode,x.Cells.Where(kv=>keys.Contains(kv.Key)).ToDictionary())).ToArray();
            var period=(plan.StartDate is null?$"{plan.Year}년":$"{plan.StartDate} ~ {plan.EndDate}")+(plan.Inning is null?"":$" · {plan.Inning}(초·말 합산)");
            var answer=rows.Length==0?"조건에 맞는 수집 기록이 없습니다.":$"{(request.Room=="career"?"통산":period)} {request.Competition}에서 {label} {(plan.Descending?"높은":"낮은")} 순으로 조회한 결과입니다.";
            var warnings=page.Warnings.ToList();
            if(plan.Inning is not null && plan.Role=="pitcher")warnings.Insert(0,"회차별 투구이닝·ERA·실점(*)은 타석 기록으로 계산한 근사치입니다. 공식 자책점·승계주자 책임을 완전히 재현한 수치가 아닙니다.");
            warnings.Add("수집된 DB 기준이며 AI가 해석한 조건이 질문과 맞는지 확인해 주세요. 각 질문은 독립적으로 처리합니다.");
            if(page.Total>page.Rows.Count)warnings.Add($"최대 {page.Rows.Count}개 원본 행 안에서 결과를 표시합니다. 경계의 동률 선수가 더 있을 수 있습니다.");
            if(catalog.MaxGameDate.HasValue && (request.EndDate??new DateTime(plan.Year,12,31))>catalog.MaxGameDate.Value)warnings.Add("요청 기간 일부가 DB 수집 기준일 이후입니다. 아래 결과는 현재 수집된 경기만 포함합니다.");
            return new{answer,clarification=false,conditions=request,columns,rows,asOf=catalog.MaxGameDate?.ToString("yyyy-MM-dd"),warnings,
                applied=QuestionTeams.Display(page.Applied)+$" · 최소 {plan.MinimumVolume}{(plan.Role=="batter"?"타석":"이닝")} · {label} {(plan.Descending?"내림차순":"오름차순")}"};
        }
        finally{serial.Release();}
    }
}
