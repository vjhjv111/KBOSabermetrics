using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NaverSabermetrics.Web;

public static class DiamondSeasonIdentity
{
    public static (string Id, string Cookie) Actor(HttpContext context)
    {
        var cookie = context.Request.Cookies["diamond_owner"];
        if (cookie == null || !Regex.IsMatch(cookie, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
            cookie = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cookie))).ToLowerInvariant();
        var saves = context.RequestServices?.GetService<DiamondSaveCodeService>();
        return (saves?.ResolveOwner(hash) ?? hash, cookie);
    }
    public static void SetCookie(HttpContext context, string cookie)
    {
        // An older in-flight GET must not overwrite the fresh cookie issued by a successful restore.
        if (string.Equals(context.Request.Cookies["diamond_owner"], cookie, StringComparison.Ordinal)) return;
        context.Response.Cookies.Append("diamond_owner", cookie,
            new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromDays(365), Secure = context.Request.IsHttps, IsEssential = true });
    }
}
public static class DiamondSeasonEndpoints
{
    public static IEndpointRouteBuilder MapDiamondSeason(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/diamond/season", async (HttpContext context, DiamondSeasonService seasons, ILogger<DiamondSeasonService> logger) =>
        {
            var received = seasons.Now();
            try
            {
                var actor = DiamondSeasonIdentity.Actor(context);
                DiamondSeasonIdentity.SetCookie(context, actor.Cookie);
                var response = await Task.Run(() => seasons.Get(actor.Id, context.RequestAborted), context.RequestAborted);
                return Answer(context, response, received, seasons.Now());
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception error) { return Failure(error, logger); }
        });
        app.MapPost("/api/diamond/season", async (HttpContext context, DiamondSeasonService seasons, ILogger<DiamondSeasonService> logger) =>
        {
            var received = seasons.Now();
            try
            {
                var origin = context.Request.Headers.Origin.ToString();
                if (origin.Length > 0 && !string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))
                    throw new DiamondInputError("같은 사이트에서 진행해 주세요.", 403);
                if (context.Request.ContentLength > 16384) throw new DiamondInputError("리그 요청이 너무 큽니다.", 413);
                var actor = DiamondSeasonIdentity.Actor(context); DiamondSeasonIdentity.SetCookie(context, actor.Cookie);
                var bytes = new byte[16385]; var count = 0;
                while (count < bytes.Length)
                { var n = await context.Request.Body.ReadAsync(bytes.AsMemory(count), context.RequestAborted); if (n == 0) break; count += n; }
                if (count > 16384) throw new DiamondInputError("리그 요청이 너무 큽니다.", 413);
                using var body = JsonDocument.Parse(bytes.AsMemory(0, count), new() { MaxDepth = 12 });
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var response = await Task.Run(() => seasons.Post(body.RootElement, actor.Id, context.RequestAborted, ip), context.RequestAborted);
                return Answer(context, response, received, seasons.Now());
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception error) { return Failure(error, logger); }
        });
        return app;
    }
    private static IResult Answer(HttpContext context, DiamondSeasonResponse response, long received, long sent)
    {
        context.Response.Headers.CacheControl = "no-store";
        var etag = response.Save == null ? "\"diamond-season-empty\"" : $"\"diamond-season-{response.Save.Id}-{response.Save.Version}\"";
        context.Response.Headers.ETag = etag;
        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Headers.IfNoneMatch.ToString() == etag)
            return Results.StatusCode(StatusCodes.Status304NotModified);
        var body = JsonSerializer.SerializeToNode(response, DiamondJson.Options)!.AsObject();
        body["serverReceivedAt"] = received; body["serverSentAt"] = sent;
        // Actor ownership, pending rewards and hidden engine state never form part of the frontend game DTO.
        if (body["save"]?["game"] is JsonObject game) game.Remove("duel");
        return Results.Json(body, DiamondJson.Options);
    }
    private static IResult Failure(Exception error, ILogger logger)
    {
        if (error is DiamondInputError input) return Results.Json(new { error = input.Message, message = input.Message }, statusCode: input.Status);
        if (error is JsonException) return Results.Json(new { error = "JSON 입력을 확인해 주세요." }, statusCode: 400);
        logger.LogError(error, "Diamond season request failed");
        return Results.Json(new { error = "리그 저장을 확인하지 못했습니다. 다시 시도해 주세요." }, statusCode: 503);
    }
}
