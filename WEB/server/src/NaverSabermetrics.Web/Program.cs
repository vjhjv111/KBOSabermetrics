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
foreach (var asset in new[] { "index.html", "app.css", "app.js" })
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
builder.Services.AddSingleton<TeamWebService>();
builder.Services.AddSingleton<HomeWebService>();
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
    c.Response.Headers["X-Frame-Options"]="DENY";
    c.Response.Headers["Referrer-Policy"]="no-referrer";
    c.Response.Headers["Permissions-Policy"]="camera=(), microphone=(), geolocation=()";
    c.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
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
        if (context.File.Name is "index.html" or "app.js")
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
app.MapPost("/api/home", async (HomeRequest input, HttpContext c, HomeWebService home, QueryGate gate, QuotaStore quotas) =>
{
    if (!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    input.Validate();quotas.Consume(Ip(c),Math.Min(50,settings.MaxPageSize));
    return Results.Ok(await gate.RunAsync(t=>home.QueryAsync(input,t),c.RequestAborted));
});
app.MapPost("/api/team", async (TeamWebRequest input, HttpContext c, TeamWebService teams, QueryGate gate, QuotaStore quotas) =>
{
    if (!databaseReady) throw new RequestError("DB가 아직 준비되지 않았습니다.",503,"DB_NOT_READY");
    input.Validate();
    quotas.Consume(Ip(c),Math.Min(100,settings.MaxPageSize));
    return Results.Ok(await gate.RunAsync(t=>teams.QueryAsync(input,t),c.RequestAborted));
});
// Player logs expose only a bounded display projection; no raw JSON, DB download or export.
app.MapFallback((HttpContext c)=>{c.Response.StatusCode=404;return c.Response.WriteAsJsonAsync(new{code="NOT_FOUND"});});
app.Run();

static string Ip(HttpContext c)=>c.Connection.RemoteIpAddress?.ToString()??"unknown";
public sealed record PlayerSearchRequest(string Query);
public partial class Program { }
