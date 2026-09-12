using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;
using NaverSabermetrics.Web;

if (args.Length != 1) throw new ArgumentException("Pass the read-only season.db path.");
var path = Path.GetFullPath(args[0]);
var service = new AnalysisWebService(new DatabaseCacheService(path, true), new SiteOptions { QuerySeconds = 30 });
var request = new AnalysisRequest(Code: "68220", Role: "pitcher");
await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
await connection.OpenAsync();
async Task<long> Scalar(string sql)
{
    await using var command = connection.CreateCommand(); command.CommandText = sql;
    return Convert.ToInt64(await command.ExecuteScalarAsync());
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
long Sum(AnalysisResult result, string table, string column) => result.Tables.Single(x => x.Key == table).Rows.Sum(x => Convert.ToInt64(x[column]));
var results = new Dictionary<string, AnalysisResult>();
foreach (var section in new[] { "catalog", "zones", "sequences", "trend", "workload", "times" })
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var result = await service.QueryAsync(request with { Section = section }, CancellationToken.None); results[section] = result;
    Check(result.Tables.All(x => x.Rows.Count <= 500), section + " bounded tables");
    Check(result.Tables.SelectMany(x => x.Rows).SelectMany(x => x.Values).All(x => x is not double d || double.IsFinite(d)), section + " finite values");
    Console.WriteLine($"  {section}: {watch.ElapsedMilliseconds} ms, {string.Join(", ", result.Tables.Select(t => t.Key + "=" + t.Rows.Count))}");
}
const string filter = "g.SeasonYear=2026 AND lower(trim(g.RoundCode))='kbo_r' AND p.PitcherPcode='68220'";
var pitchCount = await Scalar($"SELECT count(*) FROM Pitches p JOIN Games g ON g.GameId=p.GameId WHERE {filter}");
var paCount = await Scalar("SELECT count(*) FROM PlateAppearances p JOIN Games g ON g.GameId=p.GameId WHERE g.SeasonYear=2026 AND lower(trim(g.RoundCode))='kbo_r' AND p.Status=0 AND p.IsOfficial=1 AND coalesce(nullif(p.FinalPitcherPcode,''),p.PitcherPcode)='68220'");
Check(Sum(results["trend"], "trend", "Pitches") == pitchCount, "trend conserves selected pitch count");
Check(Sum(results["trend"], "trend", "PA") == paCount, "trend uses all official completed PA");
Check(Sum(results["times"], "pitchBands", "Pitches") == pitchCount, "pitch number bands conserve pitches");
Check(Sum(results["times"], "encounters", "PA") == paCount, "encounters conserve official completed PA");
var located = Convert.ToInt64(results["zones"].Summary.Single(x => x.Label == "유효 좌표").Value);
Check(Sum(results["zones"], "zones", "Pitches") == located, "zone cells conserve valid coordinates");
Check(results["zones"].Tables[0].Rows.Count == 25, "all 25 zone cells exist");
var trend = results["trend"].Tables.Single(x => x.Key == "trend").Rows;
for (var i = 0; i < trend.Count; i++)
{
    var recent = trend.Skip(Math.Max(0, i - 4)).Take(Math.Min(5, i + 1)).ToArray();
    var swings = recent.Sum(x => Convert.ToInt64(x["Swings"]));
    var whiffs = recent.Sum(x => Convert.ToInt64(x["Whiffs"]));
    Check(swings == 0 ? trend[i]["RollingWhiffPct"] is null : Math.Abs(Convert.ToDouble(trend[i]["RollingWhiffPct"]) - 100d * whiffs / swings) < 1e-9, $"rolling ratio {i + 1} weighted correctly");
}
var load7 = await Scalar("SELECT sum(l.PitchCount) FROM PitchingGameLines l JOIN Games g ON g.GameId=l.GameId WHERE l.Pcode='68220' AND g.SeasonYear=2026 AND lower(trim(g.RoundCode))='kbo_r' AND g.GameDate BETWEEN date((SELECT max(GameDate) FROM Games WHERE SeasonYear=2026 AND lower(trim(RoundCode))='kbo_r'),'-6 days') AND (SELECT max(GameDate) FROM Games WHERE SeasonYear=2026 AND lower(trim(RoundCode))='kbo_r')");
Check(Convert.ToInt64(results["workload"].Tables[0].Rows.Single()["Pitches7"]) == load7, "last7 workload agrees with independent box score sum");
foreach (var section in new[] { "zones", "sequences", "trend", "times" })
    Check((await service.QueryAsync(request with { Section = section, Code = "" }, CancellationToken.None)).Tables.Length == 0, section + " requests player selection");
foreach (var bad in new[] { request with { Code = "' OR 1=1" }, request with { Start = "2026-02-30" }, request with { Count = "4-0" }, request with { Page = 0 }, request with { PitchType = null! } })
{
    try { bad.Validate(); throw new Exception("Invalid input accepted"); } catch (RequestError) { }
}
Check(true, "strict parameter validation");
var fastball = Convert.ToString(results["catalog"].Tables.Single(x => x.Key == "pitchTypes").Rows[0]["Type"])!;
foreach (var section in new[] { "zones", "sequences", "trend", "times" })
{
    var filtered = await service.QueryAsync(request with { Section = section, PitchType = fastball, Stance = "L", Count = "0-0" }, CancellationToken.None);
    Check(filtered.Tables.All(x => x.Rows.Count <= 500), section + " combined filters execute");
}
var empty = await service.QueryAsync(request with { Section = "trend", Code = "9999999999" }, CancellationToken.None);
Check(empty.Tables.All(x => x.Rows.Count == 0), "missing player yields empty data");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await service.QueryAsync(request, cancelled.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
Check(true, "cancellation honored");
await FixtureCases.RunAsync(connection);
Console.WriteLine("Analysis pitching validation passed.");
