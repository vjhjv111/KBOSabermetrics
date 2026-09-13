using System.Text.Json;

namespace NaverSabermetrics.Web;

public static class DiamondSaveCodeEndpoints
{
    public static IEndpointRouteBuilder MapDiamondSaveCode(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/diamond/save", async (HttpContext context, DiamondSaveCodeService saves, ILogger<DiamondSaveCodeService> logger) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var actor = DiamondSeasonIdentity.Actor(context); DiamondSeasonIdentity.SetCookie(context, actor.Cookie);
                var result = await Task.Run(() => saves.Get(actor.Id, context.RequestAborted), context.RequestAborted);
                return Results.Json(result, DiamondJson.Options);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception error) { return Failure(context, error, logger); }
        });
        app.MapPost("/api/diamond/save", async (HttpContext context, DiamondSaveCodeService saves, ILogger<DiamondSaveCodeService> logger) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var origin = context.Request.Headers.Origin.ToString();
                if (origin.Length > 0 && !string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase)
                    || context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                    throw new DiamondInputError("같은 게임 화면에서 진행해 주세요.", 403);
                if (context.Request.ContentLength > 4096) throw new DiamondInputError("저장 요청이 너무 큽니다.", 413);
                var bytes = new byte[4097]; var count = 0;
                while (count < bytes.Length)
                { var read = await context.Request.Body.ReadAsync(bytes.AsMemory(count), context.RequestAborted); if (read == 0) break; count += read; }
                if (count > 4096) throw new DiamondInputError("저장 요청이 너무 큽니다.", 413);
                using var body = JsonDocument.Parse(bytes.AsMemory(0, count), new() { MaxDepth = 8 });
                if (body.RootElement.ValueKind != JsonValueKind.Object || !body.RootElement.TryGetProperty("op", out var op) || op.ValueKind != JsonValueKind.String)
                    throw new DiamondInputError("저장 요청을 확인해 주세요.");
                if (op.GetString() == "save")
                {
                    var actor = DiamondSeasonIdentity.Actor(context);
                    var result = await Task.Run(() => saves.Save(actor.Id, context.RequestAborted), context.RequestAborted);
                    DiamondSeasonIdentity.SetCookie(context, actor.Cookie); return Results.Json(result, DiamondJson.Options);
                }
                if (op.GetString() == "load")
                {
                    // Do not issue the old/current cookie on a load path, including failures.
                    var code = body.RootElement.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var cookie = await Task.Run(() => saves.Load(code, ip, context.RequestAborted), context.RequestAborted);
                    DiamondSeasonIdentity.SetCookie(context, cookie); return Results.Json(new { loaded = true }, DiamondJson.Options);
                }
                throw new DiamondInputError("지원하지 않는 저장 요청입니다.");
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception error) { return Failure(context, error, logger); }
        });
        return app;
    }
    private static IResult Failure(HttpContext context, Exception error, ILogger logger)
    {
        if (error is DiamondInputError input)
        {
            if (input.Status == 429) context.Response.Headers.RetryAfter = "60";
            return Results.Json(new { error = input.Message, message = input.Message }, statusCode: input.Status);
        }
        if (error is JsonException) return Results.Json(new { error = "JSON 입력을 확인해 주세요." }, statusCode: 400);
        // Exception text can contain SQL/input context. Only the type is safe to record here.
        logger.LogError("Diamond save-code request failed ({ErrorType})", error.GetType().Name);
        return Results.Json(new { error = "저장 연결을 확인하지 못했습니다. 다시 시도해 주세요." }, statusCode: 503);
    }
}
