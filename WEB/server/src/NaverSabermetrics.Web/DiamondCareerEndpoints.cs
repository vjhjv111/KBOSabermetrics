using System.Text.Json;

namespace NaverSabermetrics.Web;

public static class DiamondCareerEndpoints
{
    public static IEndpointRouteBuilder MapDiamondCareer(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/diamond/career", (HttpContext context, DiamondCareerService career) =>
        {
            var actor = DiamondSeasonIdentity.Actor(context);
            DiamondSeasonIdentity.SetCookie(context, actor.Cookie);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(career.Get(actor.Id), DiamondJson.Options);
        });
        app.MapPost("/api/diamond/career", async (HttpContext context, DiamondCareerService career) =>
        {
            try
            {
                var bytes = new byte[8193]; var count = 0;
                while (count < bytes.Length)
                { var read = await context.Request.Body.ReadAsync(bytes.AsMemory(count), context.RequestAborted); if (read == 0) break; count += read; }
                if (count > 8192) throw new DiamondInputError("요청이 너무 큽니다.", 413);
                using var body = JsonDocument.Parse(bytes.AsMemory(0, count));
                var actor = DiamondSeasonIdentity.Actor(context);
                var result = career.Post(body.RootElement, actor.Id);
                DiamondSeasonIdentity.SetCookie(context, actor.Cookie);
                return Results.Json(result, DiamondJson.Options);
            }
            catch (DiamondInputError e)
            {
                var code = e.Status switch { 400 => "CAREER_INPUT", 403 => "CAREER_SESSION", 404 => "CAREER_NOT_FOUND", 409 => "CAREER_CONFLICT", 413 => "CAREER_BODY_TOO_LARGE", _ => "CAREER_ERROR" };
                return Results.Json(new { error = e.Message, message = e.Message, code }, statusCode: e.Status);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            { return Results.Json(new { error = "선수 설정 요청을 확인해 주세요.", code = "CAREER_INPUT" }, statusCode: 400); }
        });
        return app;
    }
}
