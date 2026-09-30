using System.Text.Json;
using NaverSabermetrics.Web;
using NaverRelay.Infrastructure.Sqlite;

var checks=0;
void Check(bool pass,string name){if(!pass)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
var testBotKey=new string('x',32);
BotQuestionEndpoint.Authorize(testBotKey,testBotKey);
try{BotQuestionEndpoint.Authorize(testBotKey,"wrong");throw new Exception("Bad key accepted");}catch(RequestError e){Check(e.Code=="BOT_UNAUTHORIZED","봇 인증 실패 차단");}
try{BotQuestionEndpoint.Authorize(null,testBotKey);throw new Exception("Unconfigured accepted");}catch(RequestError e){Check(e.Code=="BOT_NOT_READY","미설정 봇 API 차단");}
Check(BotQuestionEndpoint.Format(new{answer="연도를 알려 주세요.",clarification=true})=="연도를 알려 주세요.","봇 조건 확인 답변");
var formatted=BotQuestionEndpoint.Format(new{answer="조회 완료",clarification=false,applied="팀 HH",asOf="2026-09-23",columns=new[]{new WebColumn("TeamCode","팀","text",false),new WebColumn("ERA","ERA","text",true)},rows=new[]{new WebRow("test",new(){["TeamCode"]="HH",["ERA"]="0.00*"})},warnings=new[]{"근사치입니다."}});
Check(formatted.Contains("한화")&&formatted.Contains("0.00*")&&formatted.Contains("근사치")&&formatted.Contains("2026-09-23"),"봇 답변 구단명/지표/근사치/기준일 보존");
var previousKey=Environment.GetEnvironmentVariable("OPENAI_API_KEY");
try
{
    Environment.SetEnvironmentVariable("OPENAI_API_KEY","local-mocked-key");
    foreach(var sample in new[]{(3000,true),(3001,false),(-1,false)})
    {
        var handler=new TokenHandler(sample.Item1);
        using var planner=new OpenAiRecordPlanner(new RecordQuestionOptions(),handler);
        try{await planner.PlanAsync("2026년 홈런 1위",[2026],default);Check(sample.Item2,"입력 토큰 경계 허용");}
        catch(RequestError e){Check(!sample.Item2 && e.Code==(sample.Item1<0?"AI_TOKEN_CHECK":"AI_INPUT_TOO_LONG"),"입력 초과/계수 실패 차단");}
        Check(handler.Generations==(sample.Item2?1:0),"차단 시 답변 생성 호출 없음");
    }
}
finally{Environment.SetEnvironmentVariable("OPENAI_API_KEY",previousKey);}
var plan=OpenAiRecordPlanner.Parse("""{"supported":true,"clarification":"","role":"batter","year":2026,"startDate":"2026-05-01","endDate":"2026-05-31","team":null,"player":null,"metric":"hr","descending":true,"filters":[],"inning":null,"minimumVolume":0,"limit":1}""");
var site=new SiteOptions{QuerySeconds=120,StateDirectory=Path.Combine(Path.GetTempPath(),"record-questions-"+Guid.NewGuid())};
var request=RecordQuestionService.ToRequest(plan,site,[2026]);
Check(new RecordQuestionOptions().DailyLimit==1000 && new RecordQuestionOptions().PerIpDailyLimit==10,"운영 기본 한도 전체 1000/IP 10");
var ninth=plan with{Role="pitcher",Metric="era",Inning="9회",MinimumVolume=0.1,Limit=5};
foreach(var example in new[]{("한화이글스","HH"),("한화 이글스","HH"),("KIA타이거즈","HT"),("기아","HT"),("KT 위즈","KT"),("LG트윈스","LG"),("롯데자이언츠","LT"),("NC다이노스","NC"),("두산베어스","OB"),("SSG랜더스","SK"),("삼성라이온즈","SS"),("키움히어로즈","WO")})
    Check(QuestionTeams.Normalize($"2026년 {example.Item1} ERA",ninth with{Team="HT"}).Team==example.Item2,$"구단명 매핑 {example.Item1}");
Check(QuestionTeams.Mentions("2026 strikeouts ERA").Length==0,"영문 단어 안의 팀 코드 오인 방지");
Check(QuestionTeams.Display("팀 HH · VS HT · 9회")=="팀 한화 · VS KIA · 9회","적용 조건 구단명 표시");
try{QuestionTeams.Normalize("2026년 한화를 상대로 홈런",plan with{Team="HH"});throw new Exception("Opponent used as team");}catch(RequestError){Check(true,"상대 팀을 기준 팀으로 바꾸지 않음");}
try{QuestionTeams.Normalize("한화와 KIA 비교",plan with{Team="HH"});throw new Exception("Multiple teams dropped");}catch(RequestError){Check(true,"다중 구단 누락 차단");}
Check(QuestionTeams.Normalize("한화가 KIA를 상대로 홈런",plan with{Team="HH",Filters=[new("opponent","HT")]}).Team=="HH","기준/상대 구단 구분 유지");
var ninthRequest=RecordQuestionService.ToRequest(ninth,site,[2026]);
Check(ninthRequest.Inning=="9회"&&ninthRequest.Conditions[0].Value==0.1,"9회와 최소 0.1이닝 보존");
RecordQuestionService.ValidateQuestion("2026년 시즌 전체로 최소이닝은 0.1 9회(초 말 안가리고)에 era가 가장 높은 투수 5명을 알려줘",ninth);
try{RecordQuestionService.ValidateQuestion("2026년 9회 최소 0.1이닝",ninth with{Inning=null});throw new Exception("Inning dropped");}catch(RequestError){Check(true,"모델이 회차를 삭제하면 거절");}
try{RecordQuestionService.ValidateQuestion("2026년 9회 최소 0.1이닝",ninth with{MinimumVolume=1});throw new Exception("Minimum changed");}catch(RequestError){Check(true,"모델이 0.1을 1로 바꾸면 거절");}
Check(RecordQuestionService.UnsupportedQuestion("2026년 WPA가 가장 높았던 플레이") is null,"개별 WPA 조회 지원");
Check(RecordQuestionService.UnsupportedQuestion("2026년 9회(초 말 안가리고) ERA") is null,"초말 합산 회차 지원");
Check(RecordQuestionService.UnsupportedQuestion("2026년 9회초 ERA") is not null,"초말 세부 조건 누락 차단");
Check(request.SortBy=="HomeRuns"&&request.StartDate==new DateTime(2026,5,1)&&request.EndDate==new DateTime(2026,5,31),"5월 홈런 질문을 기존 기록 조회 조건으로 변환");
void Reject(QuestionPlan bad,string name){try{RecordQuestionService.ToRequest(bad,site,[2026]);throw new Exception(name);}catch(RequestError){Check(true,name);}}
Reject(plan with{Metric="DROP TABLE Games"},"임의 SQL/지표 거절");
Reject(plan with{Role="pitcher",Metric="hr"},"역할별 지표 검증");
Reject(plan with{StartDate="2025-05-01"},"연도를 벗어난 날짜 거절");
Reject(plan with{StartDate="2026-05-31",EndDate="2026-05-01"},"역전 기간 거절");
Reject(plan with{EndDate=null},"불완전 날짜 거절");
Reject(plan with{Year=2027},"DB 미보유 연도 거절");
Reject(plan with{Metric="avg"},"최소 타석 없는 비율 순위 거절");
var teamOps=plan with{Metric="ops",Team="WO",StartDate=null,EndDate=null,Filters=[new("room","team"),new("runners","만루")]};
var teamOpsRequest=RecordQuestionService.ToRequest(teamOps,site,[2026]);
Check(teamOpsRequest.Room=="team"&&teamOpsRequest.Conditions.Count==0,"팀 만루 OPS는 최소 타석 없이 조회");
Check(RecordQuestionService.ToRequest(teamOps with{Role="pitcher",Metric="era",Team=null},site,[2026]).Conditions.Count==0,"팀 ERA 순위도 최소 이닝 없이 조회");
Check(RecordQuestionService.MinimumWaived("타석수 무관 2026기준 키움히어로즈 만루 ops알려줘"),"스크린샷의 타석수 무관 인식");
Check(RecordQuestionService.ToRequest(plan with{Metric="ops"},site,[2026],minimumWaived:true).Conditions.Count==0,"개인 순위도 명시한 표본 제한 해제 허용");
Reject(plan with{Team="invalid"},"허용하지 않은 팀 거절");
try{OpenAiRecordPlanner.Parse("{}");throw new Exception("Missing fields accepted");}catch(JsonException){Check(true,"필수 해석 필드 누락 거절");}
try{OpenAiRecordPlanner.Parse(JsonSerializer.Serialize(plan,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})[..^1]+",\"sql\":\"SELECT 1\"}");throw new Exception("Extra fields accepted");}catch(JsonException){Check(true,"추가 모델 필드 거절");}
var fake=new FakePlanner(plan);
site.DatabasePath=Environment.GetEnvironmentVariable("SABER_QA_DB")??Path.Combine(site.StateDirectory,"missing.db");
var db=new DatabaseCacheService(site.DatabasePath,webReadOnly:true);
using var gate=new QueryGate(site);var records=new RecordService(db,site);
var options=new RecordQuestionOptions{Enabled=false,DailyLimit=3,PerIpDailyLimit=1};
var service=new RecordQuestionService(options,site,db,records,gate,fake);
try{await service.AskAsync("test","test",default);throw new Exception("Disabled accepted");}catch(RequestError e){Check(e.Code=="AI_NOT_READY","키/활성화 준비 전 조회 차단");}
if(File.Exists(site.DatabasePath))
{
    try
    {
        options.Enabled=true;
        try{await service.AskAsync(new string('가',601),"test",default);throw new Exception("Long question accepted");}catch(RequestError){Check(fake.Calls==0,"600자 초과 질문은 AI 호출 전 차단");}
        var result=JsonSerializer.SerializeToElement(await service.AskAsync("2026년 5월 홈런 1위","test",default));
        var first=result.GetProperty("rows")[0].GetProperty("Cells");
        var direct=await records.QueryAsync(request,default);
        Check(first.GetProperty("HomeRuns").GetString()==direct.Rows[0].Cells["HomeRuns"] && first.GetProperty("Name").GetString()==direct.Rows[0].Cells["Name"],"실제 DB 직접 조회와 질문 결과 일치");
        Check(!result.GetProperty("clarification").GetBoolean()&&result.GetProperty("asOf").GetString() is not null,"출처 기준일과 결과 제공");
        Console.WriteLine("DB EXAMPLE "+first.GetProperty("Name").GetString()+" HR="+first.GetProperty("HomeRuns").GetString());
        var restarted=new RecordQuestionService(options,site,db,records,gate,fake);
        try{await restarted.AskAsync("또 조회","test",default);throw new Exception("Limit bypass");}catch(RequestError e){Check(e.Code=="AI_LIMIT","재시작 후 IP 일일 한도 유지");}
        Check(fake.Calls==1,"한도 차단 시 추가 AI 호출 없음");
        var ninthPage=await records.QueryAsync(ninthRequest,default);
        Check(ninthPage.Applied.Contains("9회") && ninthPage.Rows.Count>0,"실제 DB 9회 조회 실행");
        var hanwhaPlan=QuestionTeams.Normalize("2026년 한화이글스 9회 3점차 이내 era 순위를 알려줘 최소이닝은 0.1로",ninth with{Team="HT",Descending=false,Filters=[new("score","3점차 이내")]});
        var hanwhaPage=await records.QueryAsync(RecordQuestionService.ToRequest(hanwhaPlan,site,[2026]),default);
        Check(hanwhaPage.Rows.Count>0 && hanwhaPage.Rows.All(r=>r.Cells["TeamCode"]=="HH"),"스크린샷 질문의 실제 DB 결과가 한화 선수만 포함");
        var teamOpsPage=await records.QueryAsync(teamOpsRequest,default);
        Check(teamOpsPage.Rows.Count==1&&teamOpsPage.Rows[0].Cells["TeamCode"]=="WO"&&teamOpsPage.Rows[0].Cells["OPS"]!="-","실제 DB 키움 만루 팀 OPS 한 행 조회");
        Console.WriteLine("KIWOOM BASES LOADED OPS="+teamOpsPage.Rows[0].Cells["OPS"]);
        Console.WriteLine("9TH INNING "+JsonSerializer.Serialize(ninthPage.Rows[0].Cells.Where(x=>new[]{"Name","ERA","InningsPitched"}.Contains(x.Key)).ToDictionary()));
        var plays=JsonSerializer.SerializeToElement(await QuestionPlays.QueryAsync(plan with{Metric="play_wpa",StartDate=null,EndDate=null},site,[2026],new DateTime(2026,9,23),default));
        Check(plays.GetProperty("rows").GetArrayLength()==1,"실제 DB 시즌 최고 WPA 타석 조회");
        Console.WriteLine("WPA PLAY "+plays.GetProperty("rows")[0].GetProperty("Cells").GetRawText());
        var extended=RecordQuestionService.ToRequest(plan with{Metric="Wpa",Filters=[new("room","team"),new("runners","득점권")]},site,[2026]);
        Check(extended.Room=="team"&&extended.Runners=="득점권"&&extended.SortBy=="Wpa"&&extended.View!="basic","기록실 WPA 지표와 팀/상황 필터 연결");
        fake.Plan=plan with{Supported=false,Clarification="지원 연도와 한 가지 기본 지표를 알려 주세요."};
        var clarification=JsonSerializer.SerializeToElement(await service.AskAsync("비교해줘","other",default));
        Check(clarification.GetProperty("clarification").GetBoolean(),"미지원 질문은 조건 확인으로 반환");
        await service.AskAsync("봇 질문","test",default,authenticatedBot:true);
        Check(fake.Calls==3,"인증된 봇은 이미 소진된 IP 한도에도 조회 가능");
        var botRestarted=new RecordQuestionService(options,site,db,records,gate,fake);
        try{await botRestarted.AskAsync("추가 봇 질문","test",default,authenticatedBot:true);throw new Exception("Bot bypassed global limit");}
        catch(RequestError e){Check(e.Code=="AI_LIMIT"&&fake.Calls==3,"봇도 전체 일일 한도는 재시작 후 유지");}
    }
    finally{if(Directory.Exists(site.StateDirectory))Directory.Delete(site.StateDirectory,true);}
}
else Console.WriteLine("SKIP actual DB check: set SABER_QA_DB to a local DB path.");
Console.WriteLine($"Record question checks passed ({checks}); no OpenAI requests made.");

sealed class FakePlanner(QuestionPlan plan):IRecordQuestionPlanner
{
    public QuestionPlan Plan=plan;public int Calls;public bool Ready=>true;
    public Task<QuestionPlan> PlanAsync(string question,int[] years,CancellationToken ct){Calls++;return Task.FromResult(Plan);}
}

sealed class TokenHandler(int count):HttpMessageHandler
{
    public int Generations;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        var counting=request.RequestUri!.AbsolutePath.EndsWith("/input_tokens");
        using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        if(!body.RootElement.TryGetProperty("instructions",out _)||!body.RootElement.TryGetProperty("tools",out _))throw new Exception("Incomplete token-count payload");
        if(counting)return new(count<0?System.Net.HttpStatusCode.ServiceUnavailable:System.Net.HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{input_tokens=count}))};
        Generations++;
        var arguments="""{"supported":false,"clarification":"조건 확인","role":"batter","year":2026,"startDate":null,"endDate":null,"team":null,"player":null,"metric":"hr","descending":true,"filters":[],"inning":null,"minimumVolume":0,"limit":1}""";
        return new(System.Net.HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{status="completed",output=new[]{new{type="function_call",name="plan_record_query",arguments}}}))};
    }
}
