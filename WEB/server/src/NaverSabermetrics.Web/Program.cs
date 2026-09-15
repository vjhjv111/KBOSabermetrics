using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverSabermetrics.Web;

try { if (await Maintenance.ExecuteAsync(args)) return; }
catch (Exception ex) when (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal) && Maintenance.IsCommand(args[0]))
{
    Console.Error.WriteLine(ex.ToString()); Environment.ExitCode=1; return;
}
var webRoot=Path.Combine(AppContext.BaseDirectory,"wwwroot");
if (!Directory.Exists(webRoot))
    throw new DirectoryNotFoundException($"웹 정적 파일 폴더가 없습니다: {webRoot}. 솔루션을 다시 빌드하세요.");
foreach (var asset in new[] { "index.html", "app.css", "home.css", "forecast-method.css", "forecast-method.js", "player-profile.css", "app.js", "games.js", "diamond.js", "analysis.js", "analysis.css", "comparison.js", "comparison.css", "diamond/index.html" })
{
    var assetPath = Path.Combine(webRoot, asset);
    if (!File.Exists(assetPath) || new FileInfo(assetPath).Length == 0)
        throw new FileNotFoundException("필수 프런트 파일이 없습니다. frontend 파일을 확인하고 다시 빌드하세요.", assetPath);
}
var isRender=string.Equals(Environment.GetEnvironmentVariable("RENDER"),"true",StringComparison.OrdinalIgnoreCase);
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=args,ContentRootPath=AppContext.BaseDirectory,WebRootPath=webRoot});
var localConfig=Environment.GetEnvironmentVariable("SABER_LOCAL_CONFIG");
if (!string.IsNullOrWhiteSpace(localConfig)) builder.Configuration.AddJsonFile(Path.GetFullPath(localConfig),optional:false,reloadOnChange:false);
else builder.Configuration.AddJsonFile("appsettings.Local.json",optional:true,reloadOnChange:false);
builder.Configuration.AddEnvironmentVariables(); // Deployment secrets override local examples.
builder.Configuration.AddCommandLine(args);
var settings=builder.Configuration.GetSection("Site").Get<SiteOptions>()??new SiteOptions();
settings.DatabasePath=Environment.GetEnvironmentVariable("NAVER_SABERMETRICS_DB")??settings.DatabasePath;
if(isRender)
{
    if(string.IsNullOrWhiteSpace(settings.DatabasePath)) settings.DatabasePath="/var/data/sabermetrics_v2.db";
    if(string.IsNullOrWhiteSpace(settings.StateDirectory) || string.Equals(settings.StateDirectory,"App_Data",StringComparison.OrdinalIgnoreCase))
        settings.StateDirectory="/var/data/state";
}
settings.StateDirectory=Path.GetFullPath(settings.StateDirectory,builder.Environment.ContentRootPath);
if(!string.IsNullOrEmpty(settings.DatabasePath))settings.DatabasePath=Path.GetFullPath(settings.DatabasePath,builder.Environment.ContentRootPath);
if(!string.IsNullOrWhiteSpace(settings.PlayerPhotoDirectory))settings.PlayerPhotoDirectory=Path.GetFullPath(settings.PlayerPhotoDirectory,builder.Environment.ContentRootPath);
if(settings.MaxPageSize is < 1 or > 100 || settings.QuerySeconds is < 1 or > 120 || settings.ConcurrentQueries is <1 or >4
    || settings.RequestsPerMinute<1 || settings.IpRequestsPerMinute<1 || settings.DailyQueries<1 || settings.DailyRows<1
    || settings.MaxAccessibleRows is <1 or >10000)throw new InvalidOperationException("Site 조회 제한 설정이 유효하지 않습니다.");
var development=builder.Environment.IsDevelopment();
if(!development && (settings.AllowDevelopmentGuest || settings.Demo))throw new InvalidOperationException("운영 환경에서는 Demo/AllowDevelopmentGuest를 꺼야 합니다.");

var databaseReady=!string.IsNullOrWhiteSpace(settings.DatabasePath) && File.Exists(settings.DatabasePath);
if(settings.DatabasePath.StartsWith(Path.GetFullPath(webRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
    || settings.StateDirectory.StartsWith(Path.GetFullPath(webRoot),StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("DB 및 상태 디렉터리를 wwwroot에 넣을 수 없습니다.");
Directory.CreateDirectory(settings.StateDirectory);
builder.WebHost.ConfigureKestrel(o=>{o.Limits.MaxRequestBodySize=16*1024;o.AddServerHeader=false;});
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(_=>new DatabaseCacheService(settings.DatabasePath,webReadOnly:true));
builder.Services.AddSingleton<RecordService>();builder.Services.AddSingleton<QueryGate>();builder.Services.AddSingleton<QuotaStore>();
builder.Services.AddSingleton<PlayerWebService>();
builder.Services.AddSingleton<OfficialPlayerProfileService>();
builder.Services.AddSingleton<TeamWebService>();
builder.Services.AddSingleton<HomeWebService>();
builder.Services.AddSingleton<AnalysisWebService>();
builder.Services.AddSingleton<ComparisonWebService>();
builder.Services.AddSingleton(_ => new DiamondRosterService(settings.DatabasePath));
builder.Services.AddSingleton(services => new DiamondGameService(settings.StateDirectory, Path.Combine(AppContext.BaseDirectory,"diamond-data"),
    roster: services.GetRequiredService<DiamondRosterService>()));
builder.Services.AddSingleton(_ => new DiamondCareerService(settings.StateDirectory));
builder.Services.AddSingleton(services => new DiamondSeasonService(settings.StateDirectory, Path.Combine(AppContext.BaseDirectory,"diamond-data"),
    services.GetRequiredService<DiamondRosterService>(),
    rosterOverride: services.GetRequiredService<DiamondCareerService>().GetRosterOverride,
    gameCompleted: services.GetRequiredService<DiamondCareerService>().ApplyGame));
builder.Services.AddSingleton<DiamondSeasonMatchService>();
builder.Services.AddSingleton(_ => new DiamondSaveCodeService(settings.StateDirectory));
// Opt-in for isolated local UI/API verification where Windows user-profile keys are unavailable.
if(development && builder.Configuration.GetValue<bool>("Site:UseEphemeralDevelopmentKeys"))
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddAntiforgery(o=>
{
    o.HeaderName="X-CSRF-TOKEN";o.Cookie.Name="saber.csrf";o.Cookie.HttpOnly=true;
    o.Cookie.SameSite=SameSiteMode.Strict;
    o.Cookie.SecurePolicy=development?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;
});
builder.Services.Configure<ForwardedHeadersOptions>(o=>
{
    o.ForwardedHeaders=ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();o.KnownProxies.Clear();o.ForwardLimit=1;
    foreach(var p in settings.TrustedProxies)o.KnownProxies.Add(IPAddress.Parse(p));
    // Render terminates TLS and forwards the original client/protocol headers.
    // A Render web service is only reached through Render's proxy, so accept those headers there.
    if(isRender) o.ForwardLimit=1;
});
builder.Services.AddRateLimiter(o=>
{
    o.RejectionStatusCode=429;
    o.OnRejected=async (c,t)=>
    {
        c.HttpContext.Response.Headers.RetryAfter="60";
        await c.HttpContext.Response.WriteAsJsonAsync(new{code="RATE_LIMIT",message="요청이 너무 빠릅니다. 잠시 후 다시 조회해 주세요."},t);
    };
    o.GlobalLimiter=PartitionedRateLimiter.Create<HttpContext,string>(c=>!c.Request.Path.StartsWithSegments("/api")
        ? RateLimitPartition.GetNoLimiter("static")
        : c.Request.Path.StartsWithSegments("/api/diamond")
          ? RateLimitPartition.GetFixedWindowLimiter("diamond:"+Ip(c),_=>new(){PermitLimit=900,Window=TimeSpan.FromMinutes(1),QueueLimit=0})
          : RateLimitPartition.GetFixedWindowLimiter("ip:"+Ip(c),_=>new(){PermitLimit=settings.IpRequestsPerMinute,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
var app=builder.Build();
var db=app.Services.GetRequiredService<DatabaseCacheService>();
if(databaseReady)
{
    await db.InitializeAsync(); // Read-only validation. No CREATE TABLE, no ALTER, no source cache writes.
}
else
{
    app.Logger.LogWarning("Database is not ready at {DatabasePath}. Upload it to the persistent disk and restart the service.",settings.DatabasePath);
}
_ = app.Services.GetRequiredService<QuotaStore>();
if(isRender || settings.TrustedProxies.Length>0)app.UseForwardedHeaders();
app.Use(async(c,next)=>
{
    c.Response.Headers["X-Content-Type-Options"]="nosniff";
    var diamondPage=c.Request.Path.StartsWithSegments("/diamond");
    c.Response.Headers["X-Frame-Options"]=diamondPage?"SAMEORIGIN":"DENY";
    c.Response.Headers["Referrer-Policy"]="no-referrer";
    c.Response.Headers["Permissions-Policy"]="camera=(), microphone=(), geolocation=()";
    c.Response.Headers["Content-Security-Policy"]=diamondPage
        ? "default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'self'; form-action 'self'"
        : "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    if(c.Request.Path.StartsWithSegments("/api"))c.Response.Headers.CacheControl="no-store";
    if(!development && c.Request.IsHttps)c.Response.Headers["Strict-Transport-Security"]="max-age=31536000";
    try { await next(c); }
    catch(RequestError e){c.Response.StatusCode=e.Status;await c.Response.WriteAsJsonAsync(new{code=e.Code,message=e.Message,requestId=c.TraceIdentifier});}
    catch(AntiforgeryValidationException){c.Response.StatusCode=400;await c.Response.WriteAsJsonAsync(new{code="CSRF",message="요청 검증에 실패했습니다. 페이지를 새로 고침하세요."});}
    catch(BadHttpRequestException){c.Response.StatusCode=400;await c.Response.WriteAsJsonAsync(new{code="BAD_REQUEST",message="요청 형식이 올바르지 않습니다."});}
    catch(OperationCanceledException)when(c.RequestAborted.IsCancellationRequested){c.Response.StatusCode=499;}
    catch(Exception e)
    {
        app.Logger.LogError(e,"Query failed. request={RequestId}",c.TraceIdentifier);
        c.Response.StatusCode=500;await c.Response.WriteAsJsonAsync(new{code="SERVER_ERROR",message="서버 조회에 실패했습니다. 관리자에게 요청 ID를 전달하세요.",requestId=c.TraceIdentifier});
    }
});
app.Use(async(c,next)=>
{
    if(c.Request.Path.StartsWithSegments("/api") && !development && !c.Request.IsHttps)
        throw new RequestError("HTTPS 연결이 필요합니다.",400,"HTTPS_REQUIRED");
    if(c.Request.Method=="POST" && c.Request.Path.StartsWithSegments("/api"))
    {
        if(c.Request.ContentLength>16*1024)throw new RequestError("요청 크기를 초과했습니다.",413,"BODY_TOO_LARGE");
        var origin=c.Request.Headers.Origin.ToString();
        var self=$"{c.Request.Scheme}://{c.Request.Host}";
        if((origin.Length>0 && !string.Equals(origin,self,StringComparison.OrdinalIgnoreCase) && !settings.AllowedOrigins.Contains(origin,StringComparer.OrdinalIgnoreCase))
            || c.Request.Headers["Sec-Fetch-Site"]=="cross-site")throw new RequestError("다른 사이트에서 보낸 요청은 허용하지 않습니다.",403,"ORIGIN");
    }
    await next(c);
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // DefaultFiles rewrites / to index.html before static files are served.
        // Keep the HTML entry point and tooltip script fresh across deployments.
        if (context.File.Name is "index.html" or "app.js" or "forecast-method.js" or "forecast-method.css" or "games.js" or "diamond.js" or "analysis.js" or "analysis.css" or "comparison.js" or "comparison.css")
        {
            context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            context.Context.Response.Headers.Pragma = "no-cache";
            context.Context.Response.Headers.Expires = "0";
        }
    }
});
app.UseRouting();app.UseRateLimiter();
app.Use(async(c,next)=>
{
    if(c.Request.Method=="POST" && c.Request.Path.StartsWithSegments("/api"))
        await c.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(c);
    await next(c);
});
app.MapGet("/api/session",(IAntiforgery antiforgery,HttpContext c)=>Results.Ok(new
{
    csrfToken=antiforgery.GetAndStoreTokens(c).RequestToken,
    demo=settings.Demo
}));
app.MapDiamondGame();
app.MapDiamondSeason();
app.MapDiamondCareer();
app.MapDiamondMatch();
app.MapDiamondSaveCode();
app.MapGet("/api/health",()=>Results.Ok(new{status=databaseReady?"ok":"waiting_for_database",databaseReady,databasePath=settings.DatabasePath}));
app.MapGet("/api/ready",()=>databaseReady
    ? Results.Ok(new{status="ready"})
    : Results.Json(new{status="waiting_for_database",message="Upload sabermetrics_v2.db to the persistent disk, then restart the service."},statusCode:503));
app.MapGet("/api/catalog",async (HttpContext c,QueryGate gate,QuotaStore quotas)=>
{
    if(!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다. /var/data/sabermetrics_v2.db를 업로드한 뒤 서비스를 재시작하세요.",503,"DB_NOT_READY");
    quotas.Consume(Ip(c),0);
    var catalog=await gate.RunAsync(t=>db.GetCatalogAsync(t),c.RequestAborted);
    return Results.Ok(new
    {
        catalog.Years,Teams=catalog.Teams.Where(t=>!new[]{"EA","WE"}.Contains(t,StringComparer.OrdinalIgnoreCase)),catalog.Stadiums,
        minDate=catalog.MinGameDate?.ToString("yyyy-MM-dd"),maxDate=catalog.MaxGameDate?.ToString("yyyy-MM-dd"),
        games=catalog.GameCount,demo=settings.Demo,formulaVersion=RecordService.FormulaVersion,
        limits=new{maxPageSize=settings.MaxPageSize,maxAccessibleRows=settings.MaxAccessibleRows,dailyQueries=settings.DailyQueries,dailyRows=settings.DailyRows},
        views=ViewRegistry.Views.Select(v=>new{v.Role,v.Key,v.Title})
    });
});
app.MapGet("/api/schema/{role}/{view}",(string role,string view,RecordService records)=>
    Results.Ok(records.Schema(role=="constants"?"constants":"season",role,view)));
app.MapPost("/api/query",async(RecordRequest query,HttpContext c,RecordService records,QueryGate gate,QuotaStore quotas)=>
{
    if(!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    query.Validate(settings);
    quotas.Consume(Ip(c),query.PageSize);
    var result=await gate.RunAsync(t=>records.QueryAsync(query,t),c.RequestAborted);
    app.Logger.LogInformation("Record query ip={User} room={Room} role={Role} view={View} rows={Rows} elapsedMs={Ms} cached={Cached} request={RequestId}",
        Ip(c),query.Room,query.Role,query.View,result.Rows.Count,result.ElapsedMs,result.Cached,c.TraceIdentifier);
    return Results.Ok(result);
});
app.MapPost("/api/players/search",async(PlayerSearchRequest input,HttpContext c,QueryGate gate,QuotaStore quotas)=>
{
    if(!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    if(string.IsNullOrWhiteSpace(input.Query)||input.Query.Length<2||input.Query.Length>40||input.Query.Any(ch=>ch is '%' or '_' || char.IsControl(ch)))
        throw new RequestError("선수명 또는 선수 코드를 2~40자로 입력하세요.");
    var limit=Math.Min(20,settings.MaxPageSize);
    quotas.Consume(Ip(c),limit);
    return Results.Ok(await gate.RunAsync(t=>db.SearchPlayersAsync(input.Query,limit,t),c.RequestAborted));
});
app.MapPost("/api/player", async (PlayerWebRequest input, HttpContext c, PlayerWebService players, QueryGate gate, QuotaStore quotas) =>
{
    if (!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.", 503, "DB_NOT_READY");
    input.Validate(settings);
    quotas.Consume(Ip(c), input.Section == "years" ? 6 : input.PageSize);
    return Results.Ok(await gate.RunAsync(t => players.QueryAsync(input, t), c.RequestAborted));
});
app.MapGet("/api/player-photo/{code}", async (string code, HttpContext c, OfficialPlayerProfileService profiles, QueryGate gate) =>
{
    if (!databaseReady) return Results.NotFound();
    var photo = await gate.RunAsync(t => profiles.GetPhotoAsync(code,t),c.RequestAborted);
    if (photo is null) return Results.NotFound();
    c.Response.Headers.CacheControl = "private, max-age=3600";
    return Results.File(photo.Bytes,photo.ContentType);
});
app.MapPost("/api/home", async (HomeRequest input, HttpContext c, HomeWebService home, QueryGate gate, QuotaStore quotas) =>
{
    if (!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    input.Validate();quotas.Consume(Ip(c),Math.Min(50,settings.MaxPageSize));
    return Results.Ok(await gate.RunAsync(t=>home.QueryAsync(input,t),c.RequestAborted));
});
app.MapPost("/api/games",async(GameWebRequest input,HttpContext c,QueryGate gate,QuotaStore quotas)=>
{
    if(!databaseReady)throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    input.Validate();quotas.Consume(Ip(c),Math.Min(50,settings.MaxPageSize));
    return Results.Ok(await gate.RunAsync(t=>new GameWebService(db,settings).QueryAsync(input,t),c.RequestAborted));
});
app.MapPost("/api/analysis", async (AnalysisRequest input, HttpContext c, AnalysisWebService analysis, QueryGate gate, QuotaStore quotas) =>
{
    if (!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    input.Validate();
    // Like /api/catalog, selector metadata consumes a query but no record-page budget.
    quotas.Consume(Ip(c), input.Section == "catalog" ? 0 : Math.Min(50, settings.MaxPageSize));
    return Results.Ok(await gate.RunAsync(t => analysis.QueryAsync(input,t),c.RequestAborted));
});
app.MapPost("/api/comparison", async (ComparisonRequest input, HttpContext c, ComparisonWebService comparison, QueryGate gate, QuotaStore quotas) =>
{
    if (!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    input.Validate();
    quotas.ConsumeComparison(Ip(c));
    return Results.Ok(await gate.RunAsync(t => comparison.QueryAsync(input,t),c.RequestAborted));
});
app.MapPost("/api/team", async (TeamWebRequest input, HttpContext c, TeamWebService teams, QueryGate gate, QuotaStore quotas) =>
{
    if (!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    input.Validate();
    quotas.Consume(Ip(c),Math.Min(100,settings.MaxPageSize));
    return Results.Ok(await gate.RunAsync(t=>teams.QueryAsync(input,t),c.RequestAborted));
});
// -----------------------------------------------------------------------------
// KakaoTalk bot integration (KakaoBotServer).
// Plain GET, so these skip the POST-only antiforgery/Origin checks above —
// the bot is a server process, not a browser, and cannot carry a CSRF cookie.
// Read-only, same QueryGate/QuotaStore budget as the site itself uses.
// -----------------------------------------------------------------------------
var botTeamNames=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
{ ["HH"]="한화",["HT"]="KIA",["KT"]="KT",["LG"]="LG",["LT"]="롯데",["NC"]="NC",["OB"]="두산",["SK"]="SSG",["SS"]="삼성",["WO"]="키움" };
app.MapGet("/api/bot/standings", async (int? year, HttpContext c, HomeWebService home, QueryGate gate, QuotaStore quotas) =>
{
    if (!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    var request=new HomeRequest(year ?? DateTime.UtcNow.Year,"standings");
    request.Validate();
    quotas.Consume(Ip(c),10);
    dynamic result=await gate.RunAsync(t=>home.QueryAsync(request,t),c.RequestAborted);
    var rows=new List<object>();
    foreach (dynamic r in result.rows)
    {
        string code=r.team;
        rows.Add(new{team=code,teamName=botTeamNames.TryGetValue(code,out var n)?n:code,rank=(int)r.rank,g=(int)r.g,w=(int)r.w,d=(int)r.d,l=(int)r.l,pct=(double?)r.pct});
    }
    return Results.Ok(new{year=request.Year,rows});
});
app.MapGet("/api/bot/record", async (string role, string? team, string? stat, bool? desc, double? qualPercent, int? year, int? limit,
    HttpContext c, RecordService records, QueryGate gate, QuotaStore quotas) =>
{
    if (!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    var request=new RecordRequest
    {
        Room="season",Role=role,View="basic",Year=year ?? DateTime.UtcNow.Year,
        Team=string.IsNullOrWhiteSpace(team)?null:team,QualificationPercent=qualPercent ?? 0,
        SortBy=stat,Descending=desc ?? true,Page=1,PageSize=Math.Clamp(limit ?? 10,1,20)
    };
    request.Validate(settings);
    quotas.Consume(Ip(c),request.PageSize);
    return Results.Ok(await gate.RunAsync(t=>records.QueryAsync(request,t),c.RequestAborted));
});
// 하루치 경기들 중 승부에 가장 큰 영향을 준 장면(|WPA| 상위) — 예전 /wpa5가 Statiz를 스크래핑하던 것을
// 대체. RelayGroups.WpaByPlate(플레이별 승리확률 변동, %p 단위)를 그대로 사용합니다.
app.MapGet("/api/bot/wpa", async (string date, int? limit, HttpContext c, QueryGate gate, QuotaStore quotas) =>
{
    if (!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    if (!DateTime.TryParseExact(date,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _))
        throw new RequestError("날짜 형식이 올바르지 않습니다. (예: 2026-03-28)");
    var take=Math.Clamp(limit ?? 5,1,20);
    quotas.Consume(Ip(c),10);
    var rows=await gate.RunAsync(async t=>
    {
        await using var conn=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());
        await conn.OpenAsync(t);
        await using var cmd=conn.CreateCommand();cmd.CommandTimeout=settings.QuerySeconds;
        cmd.CommandText="SELECT g.GameId Id,g.HomeTeamCode Home,g.AwayTeamCode Away,r.Inning,r.BattingTeamCode Team,r.Title,r.WpaByPlate Wpa,r.HomeWinRateAfter HomeWinRate,r.AwayWinRateAfter AwayWinRate "
            +"FROM RelayGroups r JOIN Games g ON g.GameId=r.GameId "
            +"WHERE SUBSTR(g.GameDate,1,10)=$date AND UPPER(g.HomeTeamCode) NOT IN ('EA','WE') AND UPPER(g.AwayTeamCode) NOT IN ('EA','WE') AND r.WpaByPlate IS NOT NULL "
            +"ORDER BY ABS(r.WpaByPlate) DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$date",date);cmd.Parameters.AddWithValue("$limit",take);
        using var cancel=t.Register(cmd.Cancel);
        var list=new List<object>();
        await using var reader=await cmd.ExecuteReaderAsync(t);
        while(await reader.ReadAsync(t))
        {
            string? home=reader.IsDBNull(1)?null:Convert.ToString(reader.GetValue(1));
            string? away=reader.IsDBNull(2)?null:Convert.ToString(reader.GetValue(2));
            string? team=reader.IsDBNull(4)?null:Convert.ToString(reader.GetValue(4));
            list.Add(new{
                gameId=reader.IsDBNull(0)?null:Convert.ToString(reader.GetValue(0)),
                home,away,
                homeName=home!=null&&botTeamNames.TryGetValue(home,out var hn)?hn:home,
                awayName=away!=null&&botTeamNames.TryGetValue(away,out var an)?an:away,
                inning=reader.IsDBNull(3)?(int?)null:Convert.ToInt32(reader.GetValue(3)),
                team,
                teamName=team!=null&&botTeamNames.TryGetValue(team,out var tn)?tn:team,
                title=reader.IsDBNull(5)?null:Convert.ToString(reader.GetValue(5)),
                wpa=reader.IsDBNull(6)?(double?)null:Convert.ToDouble(reader.GetValue(6)),
                homeWinRate=reader.IsDBNull(7)?(double?)null:Convert.ToDouble(reader.GetValue(7)),
                awayWinRate=reader.IsDBNull(8)?(double?)null:Convert.ToDouble(reader.GetValue(8))
            });
        }
        return list;
    },c.RequestAborted);
    return Results.Ok(new{date,rows});
});
// Player logs expose only a bounded display projection; no raw JSON, DB download or export.
app.MapFallback((HttpContext c)=>{c.Response.StatusCode=404;return c.Response.WriteAsJsonAsync(new{code="NOT_FOUND"});});
app.Run();

static string Ip(HttpContext c)=>c.Connection.RemoteIpAddress?.ToString()??"unknown";
public sealed record PlayerSearchRequest(string Query);
public partial class Program { }
