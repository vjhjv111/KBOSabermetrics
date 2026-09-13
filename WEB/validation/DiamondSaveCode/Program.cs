using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using NaverSabermetrics.Web;

if(args.Length<2)throw new ArgumentException("Usage: <game-lib-directory> <isolated-scratch-directory>");
var lib=Path.GetFullPath(args[0]);
var output=Path.Combine(Path.GetFullPath(args[1]),"save-code-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
var absentSource=Path.Combine(output,"source-must-not-be-created.db");
long now=1700000000000;var checks=0;
void Check(bool value,string name){checks++;if(!value)throw new InvalidDataException(name);}
JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value,DiamondJson.Options);
T Copy<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value,DiamondJson.Options),DiamondJson.Options)!;
string Owner(string cookie)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cookie))).ToLowerInvariant();
void InputError(Action operation,int status,string name)
{try{operation();throw new InvalidDataException(name+" did not reject");}catch(DiamondInputError e){Check(e.Status==status,name+" status="+e.Status);}}
async Task<SaveHost> Start()
{
    var career=new DiamondCareerService(output,()=>now);
    var seasons=new DiamondSeasonService(output,lib,new DiamondRosterService(absentSource),()=>now,()=>.5,career.GetRosterOverride,career.ApplyGame);
    var codes=new DiamondSaveCodeService(output,()=>now);
    var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],EnvironmentName="Development"});
    builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,0));
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
    builder.Services.AddSingleton(career);builder.Services.AddSingleton(seasons);builder.Services.AddSingleton(codes);builder.Services.AddSingleton<DiamondSeasonMatchService>();
    builder.Services.AddAntiforgery(o=>{o.HeaderName="X-CSRF-TOKEN";o.Cookie.Name="save-code-test-csrf";o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Strict;});
    var app=builder.Build();
    app.Use(async(context,next)=>
    {
        try{if(context.Request.Method=="POST")await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);await next(context);}
        catch(AntiforgeryValidationException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{code="CSRF"});}
    });
    app.MapGet("/api/session",(HttpContext c,IAntiforgery csrf)=>Results.Json(new{csrfToken=csrf.GetAndStoreTokens(c).RequestToken}));
    app.MapDiamondSeason();app.MapDiamondCareer();app.MapDiamondMatch();app.MapDiamondSaveCode();
    await app.StartAsync();
    var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    return new(app,new Uri(address),career,seasons,codes);
}
void StoreSeason(SaveHost host,string owner,DiamondSeasonSave save)
{
    using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=host.Seasons.DatabasePath}.ToString());c.Open();using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;
    cmd.CommandText="INSERT INTO DiamondSeasons(Owner,State,Version,UpdatedAt) VALUES($o,$s,$v,$t) ON CONFLICT(Owner) DO UPDATE SET State=excluded.State,Version=excluded.Version,UpdatedAt=excluded.UpdatedAt";
    cmd.Parameters.AddWithValue("$o",owner);cmd.Parameters.AddWithValue("$s",JsonSerializer.Serialize(save,DiamondJson.Options));cmd.Parameters.AddWithValue("$v",save.Version);cmd.Parameters.AddWithValue("$t",save.UpdatedAt);cmd.ExecuteNonQuery();tx.Commit();
}
DiamondSeasonSave SeedSeason(SaveHost host,string owner,DiamondCareerPlayer player)
{
    var custom=host.Career.GetRosterOverride(owner,2025)!;var b=custom.Batter!;
    var p=new DiamondPitcher{Id="2025:synthetic-pitcher",PlayerId="synthetic-pitcher",Name="합성투수",Team="LG",Tbf=500,Bb=40,So=100,Outs=330,Whip=1.3,Era=4,Arsenal=[new("fastball",145,100)],Profile=new(){Bats="R",Throws="R",HeightCm=185}};
    var game=new DiamondSeasonGame{Id="S-validation-game",HomeTeam="HH",AwayTeam="LG",Inning=3,Half="bottom",Outs=1,HomeRuns=2,AwayRuns=1,
        HomeLine=[0,2,0],AwayLine=[0,1,0],HomeLineup=Enumerable.Repeat(b.Id,9).ToList(),AwayLineup=Enumerable.Repeat(b.Id,9).ToList(),HomePitcher=p.Id,AwayPitcher=p.Id,
        HomeUsedPitchers=[p.Id],AwayUsedPitchers=[p.Id],PlateAppearances=17,Bases=[new(b.Id,p.Id),null,null]};
    game.Duel=new(){Code=game.Id,Host=owner,Mode="ai",HostRole="batter",Batter=b.Id,Pitcher=p.Id,Pace="full",Balls=2,Strikes=1,Round=17,PitchCount=23,CreatedAt=now,ExpiresAt=now+365L*86400000,
        Roster=new(2025,"synthetic","fixture-revision",b,p),Pitch=new(){Id=23,Type="fastball",Velocity=145,ReleaseAt=now+86400000,FlightMs=458,Target=new(.2,-.3)}};
    var save=new DiamondSeasonSave{Id="S-validation-owner-"+owner[..8],Version=7,Team="HH",Season=2025,Day=4,TotalDays=144,AsOf="synthetic",Revision="fixture-revision",Game=game,
        CharacterId=player.Id,CharacterBatterId=b.Id,CreatedAt=now,UpdatedAt=now,Appearances=new(){[b.Id]=Copy(player.Appearance)},
        Teams=[new(){Code="HH",Name="한화",Batters=[b],Pitchers=[p],Lineup=game.HomeLineup},new(){Code="LG",Name="LG",Batters=[b],Pitchers=[p],Lineup=game.AwayLineup}]};
    StoreSeason(host,owner,save);return save;
}
var host=await Start();
try
{
    using var a=new Browser(host.Address);var empty=await a.Get("save");
    Check(empty.Status==200&&empty.Body.GetProperty("code").ValueKind==JsonValueKind.Null&&!empty.Body.GetProperty("hasData").GetBoolean(),"new owner has no recovery code or data");
    Check(a.Cookie is{Length:64}&&empty.OwnerCookies.Length==1&&empty.OwnerCookies[0].Contains("httponly",StringComparison.OrdinalIgnoreCase),"initial owner cookie is HttpOnly and issued once");
    var rawA=a.Cookie!;var ownerA=Owner(rawA);
    Check((await a.Get("save")).OwnerCookies.Length==0,"existing valid owner GET never overwrites restore cookie");
    Check((await a.Post("save",new{op="save"})).Status==409,"empty owner cannot issue misleading saved-game code");
    var creation=await a.Post("career",new{op="create",requestId="save-code-career-a",name="복원선수A",team="HH",position="CF",bats="L",throws="R",delivery="overhand",archetype="balanced",appearance=new DiamondCareerAppearance()});
    Check(creation.Status==200,"create career through original owner");
    var playerA=creation.Body.GetProperty("player").Deserialize<DiamondCareerPlayer>(DiamondJson.Options)!;
    var careerOnly=await a.Post("save",new{op="save"});var codeA=careerOnly.Body.GetProperty("code").GetString()!;
    Check(careerOnly.Status==200&&careerOnly.Body.GetProperty("hasData").GetBoolean(),"career-only data can issue recovery code without source roster");
    var groups=codeA.Split('-');Check(groups.Length==6&&groups.All(g=>g.Length==4)&&codeA.Replace("-","").Distinct().Count()>1,"recovery code is six four-character random groups");
    var concurrentCodes=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>host.Codes.Save(ownerA).Code)));
    Check(concurrentCodes.All(c=>c==codeA)&&(await a.Get("save")).Body.GetProperty("code").GetString()==codeA,"save and GET reuse stable canonical-owner code across concurrent calls");
    var saveA=SeedSeason(host,ownerA,playerA);
    now+=1500;
    var trained=await a.Post("career",new{op="train",requestId="save-code-training-a",version=playerA.Version,skill="contact"});
    Check(trained.Status==200,"source owner advances career after code issuance");
    playerA=trained.Body.GetProperty("player").Deserialize<DiamondCareerPlayer>(DiamondJson.Options)!;
    saveA.Version++;saveA.Day=9;saveA.Game!.Inning=5;saveA.Game.HomeRuns=7;saveA.UpdatedAt=now;
    var reward=new DiamondSeasonGame{Id="save-code-reward",Complete=true,CompletedAt=now};var customId="2025:"+playerA.Id;
    reward.PlayerStats[customId]=new(){PlayerId=customId,Name=playerA.Name,Team="HH",PA=4,AB=4,H=2};saveA.PendingRewards.Add(reward);StoreSeason(host,ownerA,saveA);
    Check((await a.Get("save")).Body.GetProperty("updatedAt").GetInt64()==now,"recovery metadata tracks newest autosave rather than issue-time snapshot");

    using var b=new Browser(host.Address);await b.Get("save");var rawB=b.Cookie!;var ownerB=Owner(rawB);
    var creationB=await b.Post("career",new{op="create",requestId="save-code-career-b",name="별도선수B",team="LG",position="CF",bats="R",throws="R",delivery="overhand",archetype="balanced",appearance=new DiamondCareerAppearance()});
    var playerB=creationB.Body.GetProperty("player").Deserialize<DiamondCareerPlayer>(DiamondJson.Options)!;var saveB=SeedSeason(host,ownerB,playerB);
    var codeB=(await b.Post("save",new{op="save"})).Body.GetProperty("code").GetString()!;
    Check(codeA!=codeB,"different owners receive different recovery codes");
    var normalized="  "+codeA.ToLowerInvariant().Replace("-"," \t- ")+" \r\n";
    var loaded=await b.Post("save",new{op="load",code=normalized});
    Check(loaded.Status==200&&loaded.Body.GetProperty("loaded").GetBoolean(),"mixed case, spaces and grouping normalize on restore");
    var aliasB=b.Cookie!;
    Check(aliasB!=rawA&&aliasB!=rawB&&host.Codes.ResolveOwner(Owner(aliasB))==ownerA,"restore issues independent alias cookie resolving to original owner");
    Check(loaded.OwnerCookies.Length==1&&!loaded.Body.TryGetProperty("owner",out _)&&!loaded.Body.TryGetProperty("cookie",out _),"restore sends cookie securely without exposing canonical identity");
    var restored=await b.Get("season");var original=await a.Get("season");
    Check(restored.Status==200&&restored.Body.GetProperty("save").GetRawText()==original.Body.GetProperty("save").GetRawText(),"another browser sees identical latest league and active-game state");
    Check(restored.Body.GetProperty("save").GetProperty("day").GetInt32()==9&&restored.Body.GetProperty("action").GetProperty("pitch").GetProperty("id").GetInt32()==23,"restoration retains latest day and live pitch");
    Check(restored.Body.GetProperty("action").GetProperty("role").GetString()=="batter"&&restored.Body.GetProperty("action").GetProperty("balls").GetInt32()==2,"canonical owner retains role and pitch count state");
    var restoredCareer=(await b.Get("career")).Body.GetProperty("player").Deserialize<DiamondCareerPlayer>(DiamondJson.Options)!;
    Check(restoredCareer.Id==playerA.Id&&restoredCareer.Ratings.Contact==playerA.Ratings.Contact&&restoredCareer.Games==1,"career identity, post-issue training and pending reward follow canonical owner");
    Check((await b.Get("save")).Body.GetProperty("code").GetString()==codeA,"alias save metadata retains same stable code");
    foreach(var path in new[]{"save","season","career","match?code=FABCDEFG"})
        Check((await b.Get(path)).OwnerCookies.Length==0,"existing alias is never re-set by "+path+" response");

    var beforeFailure=b.Cookie;var beforeFailedPlayer=(await b.Get("career")).Body.GetRawText();
    foreach(var bad in new[]{"","bad-code",new string('A',24)})
    {
        var failed=await b.Post("save",new{op="load",code=bad});
        Check(failed.Status==400&&failed.OwnerCookies.Length==0&&b.Cookie==beforeFailure,"invalid/empty/unknown code cannot switch current profile: "+bad.Length);
    }
    Check((await b.Get("career")).Body.GetRawText()==beforeFailedPlayer,"failed restoration leaves active career unchanged");
    var temporaryOwner=new string('e',64);
    host.Career.Post(Json(new{op="create",requestId="save-code-temporary-career",name="코드원자성검증",team="HH",position="CF",bats="R",throws="R",delivery="overhand",archetype="balanced",appearance=new DiamondCareerAppearance()}),temporaryOwner);
    Check(host.Codes.Get(temporaryOwner).Code==null,"new populated owner has no code before first save");
    var firstIssuance=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>host.Codes.Save(temporaryOwner).Code)));
    Check(firstIssuance.Distinct().Count()==1&&host.Codes.Get(temporaryOwner).Code==firstIssuance[0],"concurrent first issuance creates one durable code");
    using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=host.Codes.DatabasePath}.ToString()))
    {
        connection.Open();using var command=connection.CreateCommand();command.CommandText="DELETE FROM DiamondCareerPlayers WHERE Owner=$owner";command.Parameters.AddWithValue("$owner",temporaryOwner);command.ExecuteNonQuery();
    }
    var lostData=await b.Post("save",new{op="load",code=firstIssuance[0]});
    Check(lostData.Status==400&&lostData.OwnerCookies.Length==0&&b.Cookie==beforeFailure,"code whose data vanished cannot replace active identity");
    Check(lostData.Body.GetProperty("error").GetString()=="저장 코드를 확인해 주세요.","missing-data recovery uses generic invalid-code response");
    using(var noOwner=new Browser(host.Address))
    {
        var failed=await noOwner.Post("save",new{op="load",code=""});
        Check(failed.Status==400&&noOwner.Cookie==null&&failed.OwnerCookies.Length==0,"failed restoration never creates an anonymous owner cookie");
    }
    using(var oldB=new Browser(host.Address,rawB))
    {
        Check((await oldB.Get("season")).Body.GetProperty("save").GetProperty("id").GetString()==saveB.Id,"old anonymous owner league remains intact after browser changes profile");
        Check((await oldB.Get("career")).Body.GetProperty("player").GetProperty("id").GetString()==playerB.Id,"old raw cookie still addresses its original career");
    }
    using(var recoverB=new Browser(host.Address))
    {
        Check((await recoverB.Post("save",new{op="load",code=codeB})).Status==200,"prior owner remains recoverable through its own saved code");
        Check((await recoverB.Get("career")).Body.GetProperty("player").GetProperty("id").GetString()==playerB.Id,"prior owner code never chains through replacement alias");
    }

    var rewardVersion=host.Career.Get(ownerA).Player!.Version;
    saveA.PendingRewards=[Copy(reward)];StoreSeason(host,ownerA,saveA);
    await Task.WhenAll(a.Get("season"),b.Get("season"));
    Check(host.Career.Get(ownerA).Player!.Version==rewardVersion&&host.Career.Get(ownerA).Player!.Games==1,"same completed game cannot reward twice across restored aliases");
    var shared=host.Career.Get(ownerA).Player!;
    var race=await Task.WhenAll(a.Post("career",new{op="train",requestId="save-code-race-a",version=shared.Version,skill="contact"}),b.Post("career",new{op="train",requestId="save-code-race-b",version=shared.Version,skill="contact"}));
    Check(race.Select(r=>r.Status).Order().SequenceEqual(new[]{200,409}),"same canonical version serializes concurrent browser training");
    Check(host.Career.Get(ownerA).Player!.Ratings.Contact==shared.Ratings.Contact+2&&host.Career.Get(ownerA).Player!.TrainingPoints==shared.TrainingPoints-1,"one shared mutation consumes exactly one training cost");
    var request=new{op="train",requestId="save-code-replay-cross-browser",version=host.Career.Get(ownerA).Player!.Version,skill="power"};
    var once=await a.Post("career",request);var replay=await b.Post("career",request);
    Check(once.Status==200&&replay.Status==200&&once.Body.GetRawText()==replay.Body.GetRawText(),"request idempotency ledger is shared by original and restored browser");

    Check((await b.RawPost("save","{\"op\":\"save\"}",false)).Status==400,"HTTP save code requires CSRF");
    Check((await b.RawPost("save","{\"op\":\"save\"}",true,"https://other.invalid")).Status==403,"HTTP save code rejects cross-origin requests");
    Check((await b.RawPost("save","{\"op\":\"save\"}",true,null,"cross-site")).Status==403,"HTTP save code rejects cross-site fetch metadata");
    Check((await b.RawPost("save",new string(' ',5000),true)).Status==413,"HTTP save code bounds body size");
    Check((await b.RawPost("save","{bad",true)).Status==400,"HTTP save code rejects malformed JSON");
    Check((await b.RawPost("save","[]",true)).Status==400,"HTTP save code rejects nonobject JSON");

    var beforeRestart=(await b.Get("season")).Body.GetProperty("save").GetRawText();var careerBeforeRestart=(await b.Get("career")).Body.GetRawText();
    await host.DisposeAsync();host=await Start();a.Reconnect(host.Address);b.Reconnect(host.Address);
    Check(host.Codes.ResolveOwner(Owner(aliasB))==ownerA,"alias mapping survives service and HTTP server restart");
    Check((await b.Get("season")).Body.GetProperty("save").GetRawText()==beforeRestart&&(await b.Get("career")).Body.GetRawText()==careerBeforeRestart,"active league and career restore unchanged after restart");
    Check((await b.Post("save",new{op="save"})).Body.GetProperty("code").GetString()==codeA,"saved code remains stable after restart");
    using(var afterRestart=new Browser(host.Address))
    {
        Check((await afterRestart.Post("save",new{op="load",code=codeA.Replace("-","")})).Status==200,"original code restores in a fresh browser after restart");
        Check((await afterRestart.Get("career")).Body.GetProperty("player").GetProperty("id").GetString()==playerA.Id,"post-restart code resolves same canonical player");
    }

    // Rate-limit probes use independent synthetic IP keys and an injected clock.
    for(var i=0;i<20;i++)host.Codes.Load(codeA,"192.0.2.10");
    InputError(()=>host.Codes.Load(codeA,"192.0.2.10"),429,"twenty restores per minute boundary");
    now+=60001;Check(host.Codes.Load(codeA,"192.0.2.10").Length==64,"minute limit recovers with time");
    for(var i=0;i<10;i++)InputError(()=>host.Codes.Load("invalid","192.0.2.11"),400,"failed restore attempt "+i);
    InputError(()=>host.Codes.Load(codeA,"192.0.2.11"),429,"failed-code limit also blocks valid code until cooldown");
    var restartedCodes=new DiamondSaveCodeService(output,()=>now);
    InputError(()=>restartedCodes.Load(codeA,"192.0.2.11"),429,"failed-code quota survives restart");
    now+=15*60000+1;Check(restartedCodes.Load(codeA,"192.0.2.11").Length==64,"failed-code cooldown recovers");
    for(var block=0;block<5;block++){for(var i=0;i<20;i++)host.Codes.Load(codeA,"192.0.2.12");now+=60001;}
    InputError(()=>host.Codes.Load(codeA,"192.0.2.12"),429,"hundred restores per hour boundary");
    var sawHttpLimit=false;
    for(var i=0;i<25;i++)
    {
        var previousCookie=b.Cookie;var result=await b.Post("save",new{op="load",code=codeA});
        if(result.Status==429){Check(result.OwnerCookies.Length==0&&b.Cookie==previousCookie,"HTTP rate rejection does not switch active alias");sawHttpLimit=true;break;}
        Check(result.Status==200,"HTTP valid-code attempts succeed below limit");
    }
    Check(sawHttpLimit,"HTTP restore quota is enforced");
    Check(!File.Exists(absentSource),"save-code flows never create or open original record database");
    var resultReport=new{suite="DiamondSaveCode.Validation",checks,result="passed",originalDatabaseOpened=false,scratch=output};
    File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(resultReport,DiamondJson.Options));
    Console.WriteLine($"PASS save-code issuance/normalization/alias/latest/restart/isolation/reward/concurrency/HTTP security/rate limits: {checks} checks\n{output}");
}
finally{await host.DisposeAsync();}

sealed record SaveHost(WebApplication App,Uri Address,DiamondCareerService Career,DiamondSeasonService Seasons,DiamondSaveCodeService Codes):IAsyncDisposable
{public async ValueTask DisposeAsync(){await App.StopAsync();await App.DisposeAsync();}}
sealed record Reply(int Status,JsonElement Body,string[] OwnerCookies);
sealed class Browser:IDisposable
{
    private readonly CookieContainer _cookies=new();private HttpClient _client;private Uri _address;private string? _csrf;
    public Browser(Uri address,string? initialCookie=null){_address=address;if(initialCookie!=null)_cookies.Add(address,new Cookie("diamond_owner",initialCookie,"/"));_client=Client(address);}
    private HttpClient Client(Uri address)=>new(new HttpClientHandler{CookieContainer=_cookies}){BaseAddress=address};
    public string? Cookie=>_cookies.GetCookies(_address)["diamond_owner"]?.Value;
    public void Reconnect(Uri address){_client.Dispose();_address=address;_client=Client(address);_csrf=null;}
    public async Task<Reply> Get(string path){using var response=await _client.GetAsync("/api/diamond/"+path);return await Read(response);}
    private async Task Csrf(){if(_csrf!=null)return;var session=await _client.GetFromJsonAsync<JsonElement>("/api/session");_csrf=session.GetProperty("csrfToken").GetString();}
    public Task<Reply> Post(string path,object body)=>RawPost(path,JsonSerializer.Serialize(body,DiamondJson.Options),true);
    public async Task<Reply> RawPost(string path,string body,bool csrf,string? origin=null,string? fetchSite=null)
    {
        if(csrf)await Csrf();using var request=new HttpRequestMessage(HttpMethod.Post,"/api/diamond/"+path){Content=new StringContent(body,Encoding.UTF8,"application/json")};
        if(csrf)request.Headers.Add("X-CSRF-TOKEN",_csrf);if(origin!=null)request.Headers.Add("Origin",origin);if(fetchSite!=null)request.Headers.Add("Sec-Fetch-Site",fetchSite);
        using var response=await _client.SendAsync(request);return await Read(response);
    }
    private static async Task<Reply> Read(HttpResponseMessage response)
    {
        var raw=await response.Content.ReadAsStringAsync();JsonElement body;
        try{using var document=JsonDocument.Parse(raw);body=document.RootElement.Clone();}catch(JsonException){body=JsonSerializer.SerializeToElement(new{raw});}
        var cookies=response.Headers.TryGetValues("Set-Cookie",out var values)?values.Where(x=>x.StartsWith("diamond_owner=",StringComparison.Ordinal)).ToArray():[];
        return new((int)response.StatusCode,body,cookies);
    }
    public void Dispose()=>_client.Dispose();
}
