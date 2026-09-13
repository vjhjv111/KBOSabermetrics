using System.Reflection;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Infrastructure.Sqlite;

// Execute the production builder, including its selection of official or PA-based inputs.
// SourceTree=PC selects the PC project; the default selects WEB. Optional argument: read-only DB.
var assembly = typeof(DatabaseAnalyticsService).Assembly;
var aggregateType = assembly.GetType("NaverRelay.Infrastructure.Sqlite.PitcherAggregateRecord", true)!;
var builder = typeof(DatabaseAnalyticsService).GetMethod("BuildPitcherSaber", BindingFlags.NonPublic | BindingFlags.Static)!;
var projection = assembly.GetType("NaverRelay.Infrastructure.Sqlite.WarehouseProjectionBuilder", true)!;
var parse = projection.GetMethod("ParseInningsOuts", BindingFlags.NonPublic | BindingFlags.Static)!;
var checks = 0;

void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS " + label);
}

PitcherSabermetricGridRow Rates(int finalGames, int officialOuts, int officialSo, int paOuts, int paSo,
    int officialBb = 0, int paBb = 0, int officialHr = 0, int paHr = 0)
{
    var aggregate = Activator.CreateInstance(aggregateType)!;
    foreach (var (name, value) in new (string, int)[] {
        ("FinalGames", finalGames), ("InningsOuts", officialOuts), ("FinalStrikeouts", officialSo),
        ("PlateAppearanceOuts", paOuts), ("StrikeoutsFromPlateAppearances", paSo),
        ("FinalWalks", officialBb), ("WalksFromPlateAppearances", paBb),
        ("HomeRunsAllowed", officialHr), ("HomeRunsFromPlateAppearances", paHr) })
        aggregateType.GetProperty(name)!.SetValue(aggregate, value);
    return (PitcherSabermetricGridRow)builder.Invoke(null, [aggregate, new LeagueReference()])!;
}

double? K9(int finalGames, int officialOuts, int officialSo, int paOuts, int paSo) =>
    Rates(finalGames, officialOuts, officialSo, paOuts, paSo).StrikeoutsPerNine;
bool Near(double? actual, double expected) => actual.HasValue && Math.Abs(actual.Value - expected) < 1e-12;
int Outs(string text) => (int)parse.Invoke(null, [text])!;

Check(Outs("148.1") == 445, "baseball .1 means one out, not decimal .1 innings");
Check(Outs("148 1/3") == 445, "fraction notation maps to the same 445 outs");
Check(Outs("148.2") == 446, "baseball .2 means two outs");
const double expected = 9.647191011235955;
Check(Near(K9(26, 445, 159, 444, 159), expected), "official 148 1/3 IP and 159 SO; PA outs are one short");
Check(Near(K9(26, 445, 159, 444, 158), expected), "official strikeouts and innings use the same source");
Check(!Near(K9(26, 445, 159, 444, 159), 159 * 9 / 148.1), "decimal 148.1 is never the K/9 divisor");
Check(Near(K9(0, 0, 0, 5, 3), 16.2), "PA-only records retain the PA-outs fallback");
Check(K9(1, 0, 0, 3, 1) is null, "zero official innings yields null without switching denominator");
Check(Near(K9(1, 1, 0, 1, 0), 0), "zero strikeouts with positive innings yields zero");
var officialRates = Rates(26, 445, 159, 444, 158, 48, 47, 12, 11);
Check(Near(officialRates.WalksPerNine, 2.912359550561798), "BB/9 uses official walks and official innings when PA figures differ");
Check(Near(officialRates.HomeRunsPerNine, 0.7280898876404495), "HR/9 uses official home runs and official innings when PA figures differ");
var fallbackRates = Rates(0, 0, 0, 5, 3, 0, 2, 0, 1);
Check(Near(fallbackRates.WalksPerNine, 10.8), "PA-only BB/9 retains PA walks and PA-outs fallback");
Check(Near(fallbackRates.HomeRunsPerNine, 5.4), "PA-only HR/9 retains PA home runs and PA-outs fallback");
var zeroInnings = Rates(1, 0, 0, 3, 1, 1, 2, 1, 1);
Check(zeroInnings.WalksPerNine is null, "zero official innings makes BB/9 null despite PA outs");
Check(zeroInnings.HomeRunsPerNine is null, "zero official innings makes HR/9 null despite PA outs");
var zeroEvents = Rates(1, 1, 0, 1, 0);
Check(Near(zeroEvents.WalksPerNine, 0), "no walks in positive official innings yields BB/9 zero");
Check(Near(zeroEvents.HomeRunsPerNine, 0), "no home runs in positive official innings yields HR/9 zero");

if (args.Length > 0)
{
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
        DataSource = Path.GetFullPath(args[0]), Mode = SqliteOpenMode.ReadOnly
    }.ToString());
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT SUM(HasFinalLine),SUM(InningsOuts),SUM(FinalSO),SUM(PaOuts),SUM(PaSO),
               SUM(FinalBB),SUM(PaBB),SUM(HomeRunsAllowed),SUM(PaHR)
        FROM PitcherGameStats WHERE Pcode='55633' AND TeamCode='HT' AND substr(GameId,1,4)='2026'
        """;
    using var reader = command.ExecuteReader();
    Check(reader.Read() && !reader.IsDBNull(0), "2026 Oller fixture exists in supplied DB");
    var games = reader.GetInt32(0);
    var outs = reader.GetInt32(1);
    var so = reader.GetInt32(2);
    var paOuts = reader.GetInt32(3);
    var paSo = reader.GetInt32(4);
    Check(outs == 445 && so == 159 && paOuts == 444, "real fixture retains the official/PA one-out discrepancy");
    var actual = Rates(games, outs, so, paOuts, paSo, reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8));
    Check(Near(actual.StrikeoutsPerNine, expected), "production K/9 returns 9.647191011235955 for the real fixture");
    Check(Near(actual.WalksPerNine, 2.912359550561798), "production BB/9 returns 2.912359550561798 for the real fixture");
    Check(Near(actual.HomeRunsPerNine, 0.7280898876404495), "production HR/9 returns 0.7280898876404495 for the real fixture");
    Console.WriteLine($"REAL: outs={outs}, innings={outs / 3.0:R}, SO={so}, PA outs={paOuts}, K/9={actual.StrikeoutsPerNine:R}, BB/9={actual.WalksPerNine:R}, HR/9={actual.HomeRunsPerNine:R}");
}
Console.WriteLine($"PASS {checks} per-nine checks");
