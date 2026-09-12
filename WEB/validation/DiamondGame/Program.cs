using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using NaverSabermetrics.Web;

try
{
if (args.Length < 3) throw new ArgumentException("Usage: <game-lib-directory> <original-engine-fixtures.json> <temporary-output-directory>");
var dataDirectory = Path.GetFullPath(args[0]); var fixturePath = Path.GetFullPath(args[1]);
var output = Path.Combine(Path.GetFullPath(args[2]), "diamond-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
var data = new DiamondData(dataDirectory); var checks = 0;
void Check(bool value, string name) { checks++; if (!value) throw new InvalidDataException(name); }
void Equal(JsonElement expected, JsonElement actual, string path)
{
    if (expected.ValueKind == JsonValueKind.Object)
    { foreach (var p in expected.EnumerateObject()) { Check(actual.TryGetProperty(p.Name, out var got), path + "." + p.Name + " missing"); Equal(p.Value, got, path + "." + p.Name); } return; }
    if (expected.ValueKind == JsonValueKind.Array)
    { Check(expected.GetArrayLength() == actual.GetArrayLength(), path + " array length"); for (var i = 0; i < expected.GetArrayLength(); i++) Equal(expected[i], actual[i], path + "[" + i + "]"); return; }
    if (expected.ValueKind == JsonValueKind.Number)
    { var want = expected.GetDouble(); var got = actual.GetDouble(); Check(Math.Abs(want - got) <= (Math.Abs(want) > 1e9 ? .001 : 1e-8), $"{path}: expected {want:R}, actual {got:R}"); return; }
    Check(expected.ToString() == actual.ToString(), $"{path}: expected {expected}, actual {actual}");
}
JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, DiamondJson.Options);
uint seed = 1;
double Random() { seed = unchecked(seed * 1664525 + 1013904223); return seed / 4294967296d; }
using var fixtures = JsonDocument.Parse(File.ReadAllText(fixturePath)); var cases = fixtures.RootElement.GetProperty("cases");
var engine = new DiamondEngine(data, Random);
foreach (var test in cases.EnumerateArray())
{
    var game = test.GetProperty("game").Deserialize<DiamondGame>(DiamondJson.Options)!;
    seed = test.GetProperty("seed").GetUInt32();
    var created = engine.CreatePitch(game, test.GetProperty("type").GetString()!, test.GetProperty("aim").Deserialize<DiamondVec>(DiamondJson.Options)!,
        test.GetProperty("quality").GetDouble(), test.GetProperty("now").GetInt64());
    Equal(test.GetProperty("pitch"), Json(created), "create " + game.Pitcher + "/" + created.Type); game.Pitch = created;
    var a = engine.Attributes(game.Batter, game.Pitcher);
    Equal(test.GetProperty("attributes"), Json(new { a.Contact, a.Power, a.Control, a.Strikeout }), "attributes");
    Equal(test.GetProperty("positions"), Json(new[] { 0, .5, 1, 1.065 }.Select(t => DiamondEngine.BallPosition(created, created.ReleaseAt + created.FlightMs * t))), "positions");
    var swings = test.GetProperty("swings"); var expected = test.GetProperty("results");
    for (var i = 0; i < swings.GetArrayLength(); i++)
    {
        var swing = swings[i].ValueKind == JsonValueKind.Null ? null : swings[i].Deserialize<DiamondSwing>(DiamondJson.Options);
        var result = engine.EvaluatePitch(game, swing, (long)DiamondEngine.Round(created.ReleaseAt + created.FlightMs + 400));
        Equal(expected[i], Json(result), "evaluate " + game.Pitcher + "/" + created.Type + "/" + i);
    }
    seed = test.GetProperty("aiSeed").GetUInt32(); Equal(test.GetProperty("ai"), Json(engine.AiSwing(game)), "AI swing");
}
Console.WriteLine($"PASS original TypeScript parity: {cases.GetArrayLength()} pitches, {cases.GetArrayLength() * 7} outcomes, {checks} numeric/shape checks");

void Error(Action action, int status, string name)
{
    try { action(); throw new InvalidDataException(name + " did not reject"); }
    catch (DiamondInputError ex) { Check(ex.Status == status, name + " status"); }
}
long now = 1700000000000;
var games = new DiamondGameService(output, dataDirectory, () => now, () => .5);
var batterId = cases[0].GetProperty("game").GetProperty("batter").GetString()!;
var pitcherId = cases[0].GetProperty("game").GetProperty("pitcher").GetString()!;
DiamondView Create(string actor, string mode = "pvp", string role = "pitcher", string ip = "tests") =>
    games.Post(Json(new { op = "create", mode, role, pace = "practice", batter = batterId, pitcher = pitcherId }), actor, ip);
var room = Create("host"); Check(room.Waiting && room.Role == "pitcher" && room.PitchCount == 0, "create PvP waiting");
Error(() => games.Get(room.Code, "outsider"), 403, "nonmember GET");
Error(() => games.Post(Json(new { op = "pitch", code = room.Code, previousPitch = 0, type = "fastball", aim = new { x = 0, y = 0 }, quality = 1 }), "host", "tests"), 409, "waiting pitcher");
var joins = new ConcurrentBag<(string Actor, int Status)>();
Parallel.ForEach(new[] { "guest-a", "guest-b" }, actor =>
{ try { games.Post(Json(new { op = "join", code = room.Code }), actor, "tests"); joins.Add((actor, 200)); } catch (DiamondInputError e) { joins.Add((actor, e.Status)); } });
Check(joins.Count(x => x.Status == 200) == 1 && joins.Count(x => x.Status == 409) == 1, "atomic two-person join");
var guest = joins.Single(x => x.Status == 200).Actor;
Check(games.Get(room.Code, guest).Role == "batter", "guest opposite role");
Error(() => games.Post(Json(new { op = "pitch", code = room.Code, previousPitch = 0, type = "fastball", aim = new { x = 0, y = 0 }, quality = 1 }), guest, "tests"), 403, "batter cannot pitch");
Error(() => games.Post(Json(new { op = "pitch", code = room.Code, previousPitch = 0, type = "unknown", aim = new { x = 0, y = 0 }, quality = 1 }), "host", "tests"), 400, "unknown pitch");
Error(() => games.Post(Json(new { op = "pitch", code = room.Code, previousPitch = 0, type = "fastball", aim = new { x = 3, y = 0 }, quality = 1 }), "host", "tests"), 400, "invalid aim");
for (var pa = 0; pa < 6; pa++)
{
    var pitchType = data.Arsenal(pitcherId)[0].Type;
    room = games.Post(Json(new { op = "pitch", code = room.Code, previousPitch = room.PitchCount, type = pitchType, aim = new { x = 0, y = 0 }, quality = 1 }), "host", "tests");
    var p = room.Pitch!; Check(!p.Resolved, "pitch created");
    var repeated = games.Post(Json(new { op = "pitch", code = room.Code, previousPitch = room.PitchCount - 1, type = pitchType, aim = new { x = 0, y = 0 }, quality = 1 }), "host", "tests");
    Check(repeated.PitchCount == room.PitchCount, "idempotent pitch retry");
    Error(() => games.Post(Json(new { op = "swing", code = room.Code, pitchId = p.Id, inputAt = now - 1600, aim = p.Target }), guest, "tests"), 409, "stale swing input");
    now = (long)(p.ReleaseAt + p.FlightMs - DiamondEngine.SwingContactMs);
    room = games.Post(Json(new { op = "swing", code = room.Code, pitchId = p.Id, inputAt = now, aim = p.Target }), guest, "tests");
    Check(room.Pitch!.Resolved && room.Round == pa + 1 && room.History.Count == pa + 1, "completed plate appearance");
    var replay = games.Post(Json(new { op = "swing", code = room.Code, pitchId = p.Id, inputAt = now, aim = p.Target }), guest, "tests");
    Check(replay.Score == room.Score && replay.History.Count == room.History.Count, "swing replay does not score twice"); now += 1100;
}
Check(room.Done && room.Winner == "batter", "six-plate match result");
var restarted = new DiamondGameService(output, dataDirectory, () => now);
Check(restarted.Get(room.Code, "host").Score == room.Score, "match survives service restart");
foreach (var role in new[] { "batter", "pitcher" })
{
    var ai = Create("ai-" + role, "ai", role);
    ai = games.Post(role == "batter" ? Json(new { op = "ready", code = ai.Code, previousPitch = 0 })
        : Json(new { op = "pitch", code = ai.Code, previousPitch = 0, type = data.Arsenal(pitcherId)[0].Type, aim = new { x = 0, y = 0 }, quality = 1 }), "ai-" + role, "tests");
    now = (long)(ai.Pitch!.ReleaseAt + ai.Pitch.FlightMs + 1200);
    Check(games.Get(ai.Code, "ai-" + role).Pitch!.Resolved, "AI timeout resolves " + role);
}
for (var i = 0; i < 16; i++) Create("limited", "ai", "batter", "limited-ip");
Error(() => Create("limited", "ai", "batter", "limited-ip"), 429, "per-IP creation quota");
now += 86400001; Error(() => games.Get(room.Code, "host"), 404, "expired room");
Console.WriteLine("PASS persistent AI/PvP state, simultaneous join, membership, input bounds, retries, timing, score, expiry, creation quota");

// Exercise real HTTP handlers with the same antiforgery header contract used by Program.cs.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Development" });
builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton(new DiamondGameService(Path.Combine(output, "http"), dataDirectory));
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "diamond-test-csrf"; });
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { if (context.Request.Method == "POST") await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); await next(context); }
    catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; }
});
app.MapGet("/api/session", (HttpContext context, IAntiforgery csrf) => Results.Json(new { csrfToken = csrf.GetAndStoreTokens(context).RequestToken }));
app.MapDiamondGame(); await app.StartAsync();
try
{
    var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(url) };
    var create = new { op = "create", mode = "pvp", role = "batter", pace = "full", batter = batterId, pitcher = pitcherId };
    Check((int)(await client.PostAsJsonAsync("/api/diamond/action", create)).StatusCode == 400, "HTTP missing CSRF rejected");
    var session = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
    var response = await client.PostAsJsonAsync("/api/diamond/action", create); Check(response.IsSuccessStatusCode, "HTTP create");
    var view = await response.Content.ReadFromJsonAsync<JsonElement>(); var code = view.GetProperty("code").GetString();
    Check(response.Headers.GetValues("Set-Cookie").Any(x => x.Contains("diamond_session=") && x.Contains("httponly", StringComparison.OrdinalIgnoreCase)), "HTTP actor cookie HttpOnly");
    Check(view.TryGetProperty("serverReceivedAt", out _) && view.TryGetProperty("serverSentAt", out _), "clock sync timestamps");
    Check(!view.TryGetProperty("host", out _) && !view.TryGetProperty("guest", out _), "actor secrets omitted");
    Check((await client.GetAsync("/api/diamond/action?code=" + code)).IsSuccessStatusCode, "cookie-based GET membership");
    using var stranger = new HttpClient { BaseAddress = new Uri(url) };
    Check((int)(await stranger.GetAsync("/api/diamond/action?code=" + code)).StatusCode == 403, "HTTP outsider rejected");
    client.DefaultRequestHeaders.Add("Origin", "https://other.invalid");
    Check((int)(await client.PostAsJsonAsync("/api/diamond/action", create)).StatusCode == 403, "HTTP cross-origin rejected");
    client.DefaultRequestHeaders.Remove("Origin");
    Check((int)(await client.PostAsync("/api/diamond/action", new StringContent("[1]", Encoding.UTF8, "application/json"))).StatusCode == 400, "HTTP array body rejected");
    Check((int)(await client.PostAsync("/api/diamond/action", new StringContent(new string(' ', 5000), Encoding.UTF8, "application/json"))).StatusCode == 413, "HTTP oversized body rejected");
}
finally { await app.StopAsync(); await app.DisposeAsync(); }
Console.WriteLine($"PASS HTTP CSRF/session/membership/origin/body limits; total {checks} checks; scratch output {output}");
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
