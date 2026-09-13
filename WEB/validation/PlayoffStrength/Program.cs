using NaverSabermetrics.Web;
using System.Text.Json;

var checks = 0;
var baseline = new ForecastParameters(0, 0, 0);
var adjusted = new ForecastParameters(0.5, 20, 0.2);

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

void Near(double actual, double expected, string message, double tolerance = 1e-11)
    => Check(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{message}: expected {expected:R}, got {actual:R}");

void Reject(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { checks++; return; }
    throw new InvalidOperationException(message);
}

int[,] BalancedSchedule(int each = 2)
{
    var matrix = new int[10, 10];
    for (var i = 0; i < 10; i++) for (var j = 0; j < 10; j++) if (i != j) matrix[i, j] = each;
    return matrix;
}

StrengthTeam[] Teams(int[,] matrix, Func<int, (double RF, double RA)> runs)
    => Enumerable.Range(0, 10).Select(i =>
    {
        var games = Enumerable.Range(0, 10).Sum(j => matrix[i, j] + matrix[j, i]);
        var totals = games == 0 ? (RF: 0.0, RA: 0.0) : runs(i);
        return new StrengthTeam($"T{i}", games / 2, 0, games - games / 2, totals.RF, totals.RA);
    }).ToArray();

// Equal strengths stay neutral; an equal opponent at home gets exactly the fitted home term.
var balanced = BalancedSchedule();
var equal = Teams(balanced, _ => (180, 180));
var equalRatings = PlayoffStrengthModel.Estimate(equal, balanced, adjusted);
foreach (var rating in equalRatings) Near(rating, 0, "Equal balanced teams");
Near(PlayoffStrengthModel.Probability(equalRatings, 0, 1, adjusted), 1 / (1 + Math.Exp(-0.2)), "Home advantage at equal strength");

// With all adjustments disabled, every matchup must agree with the existing Log5 model.
var varied = Teams(balanced, i => (120 + i * 21, 270 - i * 13));
var rawRatings = PlayoffStrengthModel.Estimate(varied, balanced, baseline);
double Pyth(StrengthTeam team)
{
    var rf = Math.Pow(team.RF, 1.83);
    var ra = Math.Pow(team.RA, 1.83);
    return Math.Clamp(rf / (rf + ra), 0.000001, 0.999999);
}
for (var i = 0; i < 10; i++) for (var j = i + 1; j < 10; j++)
{
    var a = Pyth(varied[i]);
    var b = Pyth(varied[j]);
    Near(PlayoffStrengthModel.Probability(rawRatings, i, j, baseline), a * (1 - b) / (a * (1 - b) + b * (1 - a)), "Baseline Log5 equivalence");
}

// More prior games reduce the strength difference and move the matchup toward .500.
var prior20 = baseline with { PriorGames = 20 };
var prior80 = baseline with { PriorGames = 80 };
var shrink20 = PlayoffStrengthModel.Estimate(varied, balanced, prior20);
var shrink80 = PlayoffStrengthModel.Estimate(varied, balanced, prior80);
var pRaw = PlayoffStrengthModel.Probability(rawRatings, 9, 0, baseline);
var p20 = PlayoffStrengthModel.Probability(shrink20, 9, 0, prior20);
var p80 = PlayoffStrengthModel.Probability(shrink80, 9, 0, prior80);
Check(pRaw > p20 && p20 > p80 && p80 > 0.5, "Increasing prior games must shrink a favorite toward .500");

// Identical run ratios against stronger opponents warrant a higher adjusted strength.
// The two isolated, balanced pairs also have a closed-form solution for an independent check.
var opposition = new int[10, 10];
opposition[0, 2] = opposition[2, 0] = opposition[1, 3] = opposition[3, 1] = 10;
var oppositionTeams = Teams(opposition, i => i switch { 2 => (200, 100), 3 => (100, 200), _ => (100, 100) });
var opponentParameters = new ForecastParameters(0.5, 20, 0);
var opponentRatings = PlayoffStrengthModel.Estimate(oppositionTeams, opposition, opponentParameters);
var expectedNeutralAgainstStrong = 0.25 * 0.5 * 1.83 * Math.Log(2) / (1 - 0.25 * 0.25);
Near(opponentRatings[0], expectedNeutralAgainstStrong, "Closed-form opponent adjustment");
Near(opponentRatings[1], -expectedNeutralAgainstStrong, "Closed-form weak-opponent adjustment");
Check(opponentRatings[0] > opponentRatings[1], "Stronger opponents must raise strength at the same observed run ratio");

// A team with more home games has that advantage removed from its season-to-date strength.
var unbalanced = BalancedSchedule();
unbalanced[0, 2] += 4;
unbalanced[1, 3] += 2;
var unbalancedEqual = Teams(unbalanced, _ => (180, 180));
var homeCorrectionParameters = new ForecastParameters(0, 20, 0.2);
var homeCorrected = PlayoffStrengthModel.Estimate(unbalancedEqual, unbalanced, homeCorrectionParameters);
Check(homeCorrected[0] < homeCorrected[2], "A home-heavy record needs a downward venue correction");

// Reordering the input cannot change the identity of any team's estimated strength.
var unbalancedTeams = Teams(unbalanced, i => (120 + i * 21, 270 - i * 13));
var ratings = PlayoffStrengthModel.Estimate(unbalancedTeams, unbalanced, adjusted);
int[] permutation = [2, 7, 0, 9, 5, 3, 8, 1, 6, 4];
var permutedTeams = permutation.Select(i => unbalancedTeams[i]).ToArray();
var permutedMatrix = new int[10, 10];
for (var i = 0; i < 10; i++) for (var j = 0; j < 10; j++) permutedMatrix[i, j] = unbalanced[permutation[i], permutation[j]];
var permutedRatings = PlayoffStrengthModel.Estimate(permutedTeams, permutedMatrix, adjusted);
for (var i = 0; i < 10; i++) Near(permutedRatings[i], ratings[permutation[i]], "Team permutation equivariance");
Near(ratings.Sum(), 0, "Centered ratings");

// H=0 removes every effect of home/away assignment; it must not leave a hidden venue bias.
var selectedParameters = new ForecastParameters(0.5, 40, 0);
var selectedRatings = PlayoffStrengthModel.Estimate(unbalancedTeams, unbalanced, selectedParameters);
var reversedVenues = new int[10, 10];
for (var i = 0; i < 10; i++) for (var j = 0; j < 10; j++) reversedVenues[i, j] = unbalanced[j, i];
var reversedRatings = PlayoffStrengthModel.Estimate(unbalancedTeams, reversedVenues, selectedParameters);
for (var i = 0; i < 10; i++) Near(reversedRatings[i], selectedRatings[i], "Zero home coefficient is venue invariant");
Near(PlayoffStrengthModel.Probability(selectedRatings, 0, 1, selectedParameters)
    + PlayoffStrengthModel.Probability(selectedRatings, 1, 0, selectedParameters), 1, "Zero home coefficient has no residual venue bias");

// Swapping the outcome of the SAME venue flips the sign of H as well as the teams.
for (var i = 0; i < 9; i++)
{
    var p = PlayoffStrengthModel.Probability(ratings, i, i + 1, adjusted);
    var complement = PlayoffStrengthModel.Probability(ratings, i + 1, i, adjusted with { HomeLogOdds = -adjusted.HomeLogOdds });
    Near(p + complement, 1, "Same-venue complementary outcomes");
    Check(p is >= 0 and <= 1, "Probability bounds");
}

// Empty seasons are neutral, including when there is no prior; impossible run totals are rejected.
var emptySchedule = new int[10, 10];
var emptyTeams = Teams(emptySchedule, _ => (0, 0));
foreach (var value in PlayoffStrengthModel.Estimate(emptyTeams, emptySchedule, baseline)) Near(value, 0, "Unplayed neutral team");
var extremeTeams = varied.ToArray();
extremeTeams[0] = extremeTeams[0] with { RF = 0, RA = 100 };
extremeTeams[1] = extremeTeams[1] with { RF = 100, RA = 0 };
extremeTeams[2] = extremeTeams[2] with { RF = double.MaxValue, RA = double.MaxValue };
var extremeRatings = PlayoffStrengthModel.Estimate(extremeTeams, balanced, baseline);
Check(extremeRatings.All(double.IsFinite), "Zero runs and very large finite totals must be numerically safe");
Near(PlayoffStrengthModel.Probability(extremeRatings, 1, 0, baseline), 1 / (1 + Math.Pow(0.000001 / 0.999999, 2)), "Clipped extreme Log5");
Near(PlayoffStrengthModel.Probability([1000, -1000], 0, 1, baseline), 1, "Extreme positive logit");
Near(PlayoffStrengthModel.Probability([1000, -1000], 1, 0, baseline), 0, "Extreme negative logit");

Reject(() => PlayoffStrengthModel.Estimate(equal, balanced, new ForecastParameters(1, 0, 0)), "Unregularized full adjustment is singular");
foreach (var parameters in new[] { new ForecastParameters(-0.1, 20, 0), new ForecastParameters(1.1, 20, 0), new ForecastParameters(0.5, -1, 0), new ForecastParameters(double.NaN, 20, 0), new ForecastParameters(0.5, double.PositiveInfinity, 0), new ForecastParameters(0.5, 20, double.NaN) })
    Reject(() => PlayoffStrengthModel.Estimate(equal, balanced, parameters), "Invalid parameters must fail");
var badMatrix = (int[,])balanced.Clone();
badMatrix[0, 1]++;
Reject(() => PlayoffStrengthModel.Estimate(equal, badMatrix, adjusted), "Inconsistent totals must fail");
badMatrix = (int[,])balanced.Clone();
badMatrix[0, 0] = 1;
Reject(() => PlayoffStrengthModel.Estimate(equal, badMatrix, adjusted), "Self matchups must fail");
badMatrix = (int[,])balanced.Clone();
badMatrix[0, 1] = -1;
Reject(() => PlayoffStrengthModel.Estimate(equal, badMatrix, adjusted), "Negative matchups must fail");
var badTeams = equal.ToArray();
badTeams[0] = badTeams[0] with { RF = double.NaN };
Reject(() => PlayoffStrengthModel.Estimate(badTeams, balanced, adjusted), "Nonfinite run totals must fail");
badTeams[0] = equal[0] with { RF = 0, RA = 0 };
Reject(() => PlayoffStrengthModel.Estimate(badTeams, balanced, adjusted), "Played games without runs must fail");
badTeams = emptyTeams.ToArray();
badTeams[0] = badTeams[0] with { RF = 1 };
Reject(() => PlayoffStrengthModel.Estimate(badTeams, emptySchedule, adjusted), "Unplayed games with runs must fail");
Reject(() => PlayoffStrengthModel.Probability([double.NaN, 0], 0, 1, adjusted), "Nonfinite ratings must fail");
Reject(() => PlayoffStrengthModel.Probability(ratings, 0, 0, adjusted), "A team cannot play itself");

// Optional fixtures exported from the independent Python calibration implementation.
if (args.Length > 0)
{
    if (args.Length != 2 || args[0] != "--fixtures") throw new ArgumentException("Usage: --fixtures path/to/fixtures.json");
    using var document = JsonDocument.Parse(File.ReadAllText(args[1]));
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    foreach (var fixture in document.RootElement.EnumerateArray())
    {
        var name = fixture.GetProperty("name").GetString();
        var fixtureTeams = fixture.GetProperty("teams").Deserialize<StrengthTeam[]>(jsonOptions)!;
        var fixtureParameters = fixture.GetProperty("parameters").Deserialize<ForecastParameters>(jsonOptions)!;
        var counts = fixture.GetProperty("homeGames").Deserialize<int[][]>()!;
        var fixtureMatrix = new int[10, 10];
        for (var i = 0; i < 10; i++) for (var j = 0; j < 10; j++) fixtureMatrix[i, j] = counts[i][j];
        var fixtureRatings = PlayoffStrengthModel.Estimate(fixtureTeams, fixtureMatrix, fixtureParameters);
        var expectedRatings = fixture.GetProperty("ratings").Deserialize<double[]>()!;
        for (var i = 0; i < 10; i++) Near(fixtureRatings[i], expectedRatings[i], $"Python rating parity: {name}, team {i}");
        foreach (var pair in fixture.GetProperty("probabilities").EnumerateArray())
        {
            var home = pair.GetProperty("home").GetInt32();
            var away = pair.GetProperty("away").GetInt32();
            Near(PlayoffStrengthModel.Probability(fixtureRatings, home, away, fixtureParameters), pair.GetProperty("probability").GetDouble(), $"Python probability parity: {name}, {home}-{away}");
        }
    }
}

Console.WriteLine($"PASS PlayoffStrength: {checks} deterministic assertions covering Log5 parity, home effects, shrinkage, opponent correction, permutation, complements, numeric extremes and invalid inputs.");
