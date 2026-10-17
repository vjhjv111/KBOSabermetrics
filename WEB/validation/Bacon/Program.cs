using System.ComponentModel;
using System.Reflection;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Infrastructure.Sqlite;
using NaverSabermetrics.Web;

var assembly = typeof(DatabaseAnalyticsService).Assembly;
object Aggregate(string name, params (string, int)[] values)
{
    var type = assembly.GetType("NaverRelay.Infrastructure.Sqlite." + name, true)!;
    var row = Activator.CreateInstance(type)!;
    foreach (var (key, value) in values) type.GetProperty(key)!.SetValue(row, value);
    return row;
}
object Build(string name, params object?[] args) => typeof(DatabaseAnalyticsService)
    .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
void Check(bool pass, string name)
{
    if (!pass) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
bool Near(double? value, double expected) => value.HasValue && Math.Abs(value.Value - expected) < 1e-12;
var batter = Aggregate("BatterAggregateRecord", ("AtBats", 400), ("Strikeouts", 100),
    ("SacrificeFlies", 5), ("Hits", 110), ("HomeRuns", 20));
var classic = (BatterSummaryGridRow)Build("BuildBatterClassic", batter);
var advanced = (BatterSabermetricGridRow)Build("BuildBatterSaber", batter, new LeagueReference(), 2026);
Check(Near(classic.BACON, 110.0 / 305) && Near(advanced.BACON, 110.0 / 305), "hits include HR; SF remains in denominator in both batting builders");
Check(Near(classic.BABIP, 90.0 / 285), "existing BABIP is unchanged");
var pitcher = Aggregate("PitcherAggregateRecord", ("OpponentAtBats", 400),
    ("StrikeoutsFromPlateAppearances", 100), ("SacrificeFlies", 5),
    ("HitsFromPlateAppearances", 110), ("BattersFaced", 460), ("HomeRunsFromPlateAppearances", 20));
var pitching = (PitcherSabermetricGridRow)Build("BuildPitcherSaber", pitcher, new LeagueReference(), false);
Check(Near(pitching.BACON, 110.0 / 305), "pitching uses actual opponent AB, not TBF");
var empty = (BatterSummaryGridRow)Build("BuildBatterClassic", Aggregate("BatterAggregateRecord", ("AtBats", 10), ("Strikeouts", 10)));
Check(empty.BACON is null, "zero contact denominator is null");
var homer = (BatterSummaryGridRow)Build("BuildBatterClassic", Aggregate("BatterAggregateRecord", ("AtBats", 1), ("Hits", 1), ("HomeRuns", 1)));
Check(homer.BACON == 1 && homer.BABIP is null, "HR-only sample gives BACON 1 with undefined BABIP");
foreach (var type in typeof(BatterSummaryGridRow).Assembly.GetTypes())
{
    var properties = type.GetProperties();
    var index = Array.FindIndex(properties, p => p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName == "BABIP");
    if (index < 0) continue;
    Check(properties[index + 1].Name == "BACON", type.Name + " BACON immediately follows BABIP");
    Check(ViewRegistry.Kind(properties[index + 1]) == "decimal3", type.Name + " uses three decimals");
}
Check(ViewRegistry.Display(typeof(BatterSummaryGridRow).GetProperty("BACON")!, classic.BACON) == "0.361", "display rounds to 0.361");
