using System.Text.Json;
using System.Text.Json.Nodes;

namespace NaverSabermetrics.Web;

public static class DiamondSeasonMatchEndpoints
{
    public static IEndpointRouteBuilder MapDiamondMatch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/diamond/match", async (HttpContext context, DiamondSeasonMatchService matches, ILogger<DiamondSeasonMatchService> logger) =>
        {
            var received = matches.Now(); context.Response.Headers.CacheControl = "no-store";
            try
            {
                var actor = DiamondSeasonIdentity.Actor(context); DiamondSeasonIdentity.SetCookie(context, actor.Cookie);
                var response = await Task.Run(() => matches.Get(context.Request.Query["code"].ToString(), actor.Id, context.RequestAborted), context.RequestAborted);
                return Answer(context, response, received, matches.Now());
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception error) { return Failure(error, logger); }
        });
        app.MapPost("/api/diamond/match", async (HttpContext context, DiamondSeasonMatchService matches, ILogger<DiamondSeasonMatchService> logger) =>
        {
            var received = matches.Now(); context.Response.Headers.CacheControl = "no-store";
            try
            {
                var origin = context.Request.Headers.Origin.ToString();
                if (origin.Length > 0 && !string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase)
                    || context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                    throw new DiamondInputError("같은 경기 화면에서 진행해 주세요.", 403);
                if (context.Request.ContentLength > 4096) throw new DiamondInputError("친선 경기 요청이 너무 큽니다.", 413);
                var bytes = new byte[4097]; var count = 0;
                while (count < bytes.Length)
                { var n = await context.Request.Body.ReadAsync(bytes.AsMemory(count), context.RequestAborted); if (n == 0) break; count += n; }
                if (count > 4096) throw new DiamondInputError("친선 경기 요청이 너무 큽니다.", 413);
                using var body = JsonDocument.Parse(bytes.AsMemory(0, count), new() { MaxDepth = 12 });
                var actor = DiamondSeasonIdentity.Actor(context); DiamondSeasonIdentity.SetCookie(context, actor.Cookie);
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var response = await Task.Run(() => matches.Post(body.RootElement, actor.Id, ip, context.RequestAborted), context.RequestAborted);
                return Answer(context, response, received, matches.Now());
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception error) { return Failure(error, logger); }
        });
        return app;
    }
    private static IResult Answer(HttpContext context, DiamondSeasonMatchResponse response, long received, long sent)
    {
        var etag = $"\"{response.Match.Code}-{response.Match.Version}-{response.Match.Team}\"";
        context.Response.Headers.ETag = etag;
        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Headers.IfNoneMatch.Any(value => value?.Split(',').Any(tag => tag.Trim() == etag) == true))
            return Results.StatusCode(StatusCodes.Status304NotModified);
        var body = JsonSerializer.SerializeToNode(response, DiamondJson.Options)!.AsObject();
        body["serverReceivedAt"] = received; body["serverSentAt"] = sent;
        // The persisted duel carries participant ownership and engine-only state.
        if (body["save"]?["game"] is JsonObject game) game.Remove("duel");
        return Results.Json(body, DiamondJson.Options);
    }
    private static IResult Failure(Exception error, ILogger logger)
    {
        if (error is DiamondInputError input) return Results.Json(new { error = input.Message, message = input.Message }, statusCode: input.Status);
        if (error is JsonException) return Results.Json(new { error = "JSON 입력을 확인해 주세요." }, statusCode: 400);
        logger.LogError(error, "Diamond friendly match request failed");
        return Results.Json(new { error = "친선 경기 연결을 확인하지 못했습니다. 다시 시도해 주세요." }, statusCode: 503);
    }
}
