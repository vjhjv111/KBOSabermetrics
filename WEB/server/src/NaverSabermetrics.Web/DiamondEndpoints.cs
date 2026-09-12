using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NaverSabermetrics.Web;

public static class DiamondEndpoints
{
    public static IEndpointRouteBuilder MapDiamondGame(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/diamond/action", async (HttpContext context, DiamondGameService games, ILogger<DiamondGameService> logger) =>
        {
            var receivedAt = games.Now();
            try
            {
                var actor = Actor(context);
                var view = await Task.Run(() => games.Get(context.Request.Query["code"].ToString(), actor.Id, context.RequestAborted), context.RequestAborted);
                return Answer(context, view, actor.Cookie, receivedAt, games.Now());
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception error) { return Failure(error, logger); }
        });
        app.MapPost("/api/diamond/action", async (HttpContext context, DiamondGameService games, ILogger<DiamondGameService> logger) =>
        {
            var receivedAt = games.Now();
            try
            {
                // The site's middleware also validates its normal X-CSRF-TOKEN for this POST.
                var origin = context.Request.Headers.Origin.ToString(); var self = $"{context.Request.Scheme}://{context.Request.Host}";
                if (origin.Length > 0 && !string.Equals(origin, self, StringComparison.OrdinalIgnoreCase) || context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                    throw new DiamondInputError("게임 화면에서 요청해 주세요.", 403);
                if (context.Request.ContentLength > 4096) throw new DiamondInputError("요청이 너무 큽니다.", 413);
                // Bound the stream too: chunked requests may not include Content-Length.
                var bytes = new byte[4097]; var count = 0;
                while (count < bytes.Length)
                {
                    var read = await context.Request.Body.ReadAsync(bytes.AsMemory(count), context.RequestAborted);
                    if (read == 0) break; count += read;
                }
                if (count > 4096) throw new DiamondInputError("요청이 너무 큽니다.", 413);
                JsonDocument document;
                try { document = JsonDocument.Parse(bytes.AsMemory(0, count)); }
                catch (JsonException) { throw new DiamondInputError("잘못된 요청입니다."); }
                using (document)
                {
                    var actor = Actor(context); var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var view = await Task.Run(() => games.Post(document.RootElement, actor.Id, ip, context.RequestAborted), context.RequestAborted);
                    return Answer(context, view, actor.Cookie, receivedAt, games.Now());
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception error) { return Failure(error, logger); }
        });
        return app;
    }
    private static (string Id, string Cookie) Actor(HttpContext context)
    {
        var cookie = context.Request.Cookies["diamond_session"];
        if (cookie == null || !Regex.IsMatch(cookie, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
            cookie = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cookie))).ToLowerInvariant(), cookie);
    }
    private static IResult Answer(HttpContext context, DiamondView view, string cookie, long receivedAt, long sentAt)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Cookies.Append("diamond_session", cookie, new CookieOptions
        { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromDays(1), Secure = context.Request.IsHttps, IsEssential = true });
        var body = JsonSerializer.SerializeToNode(view, DiamondJson.Options)!.AsObject();
        body["serverReceivedAt"] = receivedAt; body["serverSentAt"] = sentAt;
        return Results.Json(body, DiamondJson.Options);
    }
    private static IResult Failure(Exception error, ILogger logger)
    {
        if (error is DiamondInputError expected) return Results.Json(new { error = expected.Message, message = expected.Message }, statusCode: expected.Status);
        logger.LogError(error, "Diamond game request failed");
        return Results.Json(new { error = "서버 연결을 확인해 주세요. 다시 시도할 수 있습니다.", message = "서버 연결을 확인해 주세요. 다시 시도할 수 있습니다." }, statusCode: 503);
    }
}
