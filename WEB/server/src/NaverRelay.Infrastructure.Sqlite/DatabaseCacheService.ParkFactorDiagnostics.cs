using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    public async Task<ParkFactorDiagnosticBundle> GetParkFactorDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var league = await GetLeagueReferenceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var usedPf = league.ParkFactors
            .Where(x => !string.IsNullOrWhiteSpace(x.Stadium) && x.UsedFipFactor.HasValue)
            .GroupBy(x => x.Stadium, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().UsedFipFactor!.Value, StringComparer.OrdinalIgnoreCase);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var roleRows = await ReadParkDiagnosticRoleRowsAsync(connection, cancellationToken).ConfigureAwait(false);
        var games = await ReadParkDiagnosticGamesAsync(connection, cancellationToken).ConfigureAwait(false);
        var details = new List<ParkFactorDiagnosticStadiumRow>();

        foreach (var yearGroup in roleRows.GroupBy(x => x.Year).OrderBy(g => g.Key))
        {
            var year = yearGroup.Key;
            var yearRows = yearGroup.ToList();
            var yearGames = games.Where(x => x.Year == year).ToList();

            foreach (var stadiumGroup in yearRows.GroupBy(x => x.Stadium, StringComparer.OrdinalIgnoreCase))
            {
                var stadium = stadiumGroup.Key;
                var atPark = SumParkRows(stadiumGroup);
                var atParkRate = ParkFipComponent(atPark);

                // Mirrors the current PF concept: compare the park environment with all other parks
                // for the same season. This intentionally exposes how far the current method moves.
                var otherParks = SumParkRows(yearRows.Where(x => !string.Equals(x.Stadium, stadium, StringComparison.OrdinalIgnoreCase)));
                var otherRate = ParkFipComponent(otherParks);
                var annualPf = SafeFactor(atParkRate, otherRate);

                var stadiumGames = yearGames.Where(g => string.Equals(g.Stadium, stadium, StringComparison.OrdinalIgnoreCase)).ToList();
                var homeClub = stadiumGames.Where(g => !string.IsNullOrWhiteSpace(g.HomeTeamCode))
                    .GroupBy(g => g.HomeTeamCode, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? string.Empty;
                var gameCount = stadiumGames.Count;
                var totalRuns = stadiumGames.Sum(g => g.TotalRuns);
                var rpg = gameCount > 0 ? totalRuns / (double)gameCount : (double?)null;

                // A simple run-environment cross-check: total runs in this park vs total runs in the
                // primary home club's road games. It is diagnostic only and is NOT used in WAR.
                var homeClubRoad = string.IsNullOrWhiteSpace(homeClub)
                    ? new List<ParkDiagnosticGameRow>()
                    : yearGames.Where(g => !string.Equals(g.Stadium, stadium, StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(g.HomeTeamCode, homeClub, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(g.AwayTeamCode, homeClub, StringComparison.OrdinalIgnoreCase))).ToList();
                var roadRpg = homeClubRoad.Count > 0 ? homeClubRoad.Sum(g => g.TotalRuns) / (double)homeClubRoad.Count : (double?)null;
                var runPf = rpg.HasValue && roadRpg.HasValue && roadRpg.Value > 0
                    ? 100.0 * rpg.Value / roadRpg.Value : (double?)null;

                var innings = atPark.InningsOuts / 3.0;
                var ra9 = innings > 0 ? atPark.Runs * 9.0 / innings : (double?)null;
                var current = usedPf.TryGetValue(stadium, out var currentPf) ? currentPf : (double?)null;
                var warning = BuildParkWarning(gameCount, annualPf, runPf, current);

                details.Add(new ParkFactorDiagnosticStadiumRow
                {
                    Year = year, Stadium = stadium, HomeClub = homeClub, Games = gameCount,
                    Innings = innings, Runs = atPark.Runs, EarnedRuns = atPark.EarnedRuns,
                    RunsPerGame = rpg, Ra9 = ra9, HomeRuns = atPark.HomeRuns, Walks = atPark.Walks,
                    HitBatters = atPark.HitBatters, Strikeouts = atPark.Strikeouts, InfieldFlies = atPark.InfieldFlies,
                    StadiumFipComponent = atParkRate, OtherParksFipComponent = otherRate,
                    AnnualFipParkFactor = annualPf, CurrentUsedFipParkFactor = current,
                    UsedMinusAnnual = current.HasValue && annualPf.HasValue ? current.Value - annualPf.Value : null,
                    HomeRunsPerGame = rpg, HomeClubRoadRunsPerGame = roadRpg, SimpleRunParkFactor = runPf,
                    FipMinusRunParkFactor = annualPf.HasValue && runPf.HasValue ? annualPf.Value - runPf.Value : null,
                    Warning = warning,
                });
            }
        }

        var summaries = details.GroupBy(x => x.Year).OrderBy(g => g.Key).Select(g =>
        {
            var rows = g.ToList();
            var fip = rows.Where(x => x.AnnualFipParkFactor.HasValue).ToList();
            var run = rows.Where(x => x.SimpleRunParkFactor.HasValue).ToList();
            var used = rows.Where(x => x.CurrentUsedFipParkFactor.HasValue).ToList();
            var gameRows = games.Where(x => x.Year == g.Key).ToList();
            return new ParkFactorDiagnosticSeasonRow
            {
                Year = g.Key,
                Stadiums = rows.Count,
                Games = gameRows.Count,
                LeagueRunsPerGame = gameRows.Count > 0 ? gameRows.Sum(x => x.TotalRuns) / (double)gameRows.Count : null,
                WeightedFipParkFactor = Weighted(rows, x => x.AnnualFipParkFactor, x => x.Innings),
                MinFipParkFactor = Min(rows.Select(x => x.AnnualFipParkFactor)),
                P10FipParkFactor = PercentilePf(rows.Select(x => x.AnnualFipParkFactor), 0.10),
                MedianFipParkFactor = PercentilePf(rows.Select(x => x.AnnualFipParkFactor), 0.50),
                P90FipParkFactor = PercentilePf(rows.Select(x => x.AnnualFipParkFactor), 0.90),
                MaxFipParkFactor = Max(rows.Select(x => x.AnnualFipParkFactor)),
                WeightedRunParkFactor = Weighted(rows, x => x.SimpleRunParkFactor, x => x.Games),
                MinRunParkFactor = Min(rows.Select(x => x.SimpleRunParkFactor)),
                MedianRunParkFactor = PercentilePf(rows.Select(x => x.SimpleRunParkFactor), 0.50),
                MaxRunParkFactor = Max(rows.Select(x => x.SimpleRunParkFactor)),
                WeightedUsedParkFactor = Weighted(rows, x => x.CurrentUsedFipParkFactor, x => x.Innings),
                MeanAbsoluteFipDeviation = fip.Count > 0 ? fip.Average(x => Math.Abs(x.AnnualFipParkFactor!.Value - 100.0)) : null,
                Under80Count = fip.Count(x => x.AnnualFipParkFactor!.Value < 80.0),
                Over120Count = fip.Count(x => x.AnnualFipParkFactor!.Value > 120.0),
            };
        }).ToList();

        return new ParkFactorDiagnosticBundle { Seasons = summaries, Stadiums = details.OrderBy(x => x.Year).ThenBy(x => x.Stadium).ToList() };
    }

    private static async Task<List<ParkDiagnosticRoleRow>> ReadParkDiagnosticRoleRowsAsync(SqliteConnection connection, CancellationToken token)
    {
        var result = new List<ParkDiagnosticRoleRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.SeasonYear, COALESCE(g.Stadium,''), p.TeamCode,
                   COALESCE(SUM(p.InningsOuts),0), COALESCE(SUM(p.RunsAllowed),0), COALESCE(SUM(p.EarnedRuns),0),
                   COALESCE(SUM(p.HomeRunsAllowed),0), COALESCE(SUM(p.FinalBB),0), COALESCE(SUM(p.FinalHBP),0),
                   COALESCE(SUM(p.FinalSO),0), COALESCE(SUM(p.IFFB),0)
            FROM PitcherGameStats p
            INNER JOIN Games g ON g.GameId=p.GameId
            WHERE p.HasFinalLine=1 AND g.SeasonYear IS NOT NULL
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
              AND TRIM(COALESCE(g.Stadium,''))<>''
            GROUP BY g.SeasonYear, g.Stadium, p.TeamCode
            ORDER BY g.SeasonYear, g.Stadium, p.TeamCode;
            """;
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            result.Add(new ParkDiagnosticRoleRow
            {
                Year = reader.GetInt32(0), Stadium = reader.GetString(1), TeamCode = reader.GetString(2),
                InningsOuts = Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
                Runs = Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
                EarnedRuns = Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture),
                HomeRuns = Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
                Walks = Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture),
                HitBatters = Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture),
                Strikeouts = Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture),
                InfieldFlies = Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture),
            });
        }
        return result;
    }

    private static async Task<List<ParkDiagnosticGameRow>> ReadParkDiagnosticGamesAsync(SqliteConnection connection, CancellationToken token)
    {
        var result = new List<ParkDiagnosticGameRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SeasonYear, COALESCE(Stadium,''), COALESCE(HomeTeamCode,''), COALESCE(AwayTeamCode,''),
                   COALESCE(HomeScore,0), COALESCE(AwayScore,0)
            FROM Games
            WHERE SeasonYear IS NOT NULL
              AND LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_r'
              AND TRIM(COALESCE(Stadium,''))<>''
              AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL;
            """;
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            result.Add(new ParkDiagnosticGameRow
            {
                Year = reader.GetInt32(0), Stadium = reader.GetString(1), HomeTeamCode = reader.GetString(2), AwayTeamCode = reader.GetString(3),
                TotalRuns = Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture) + Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture),
            });
        }
        return result;
    }

    private static ParkDiagnosticTotals SumParkRows(IEnumerable<ParkDiagnosticRoleRow> rows)
    {
        var t = new ParkDiagnosticTotals();
        foreach (var row in rows)
        {
            t.InningsOuts += row.InningsOuts; t.Runs += row.Runs; t.EarnedRuns += row.EarnedRuns;
            t.HomeRuns += row.HomeRuns; t.Walks += row.Walks; t.HitBatters += row.HitBatters;
            t.Strikeouts += row.Strikeouts; t.InfieldFlies += row.InfieldFlies;
        }
        return t;
    }

    private static double? ParkFipComponent(ParkDiagnosticTotals t)
    {
        var innings = t.InningsOuts / 3.0;
        if (innings <= 0) return null;
        return (13.0 * t.HomeRuns + 3.0 * (t.Walks + t.HitBatters) - 2.0 * (t.Strikeouts + t.InfieldFlies)) / innings;
    }

    private static double? SafeFactor(double? numerator, double? denominator)
    {
        if (!numerator.HasValue || !denominator.HasValue || Math.Abs(denominator.Value) < 1e-9) return null;
        var value = 100.0 * numerator.Value / denominator.Value;
        return double.IsFinite(value) ? value : null;
    }

    private static string BuildParkWarning(int games, double? fip, double? run, double? used)
    {
        var flags = new List<string>();
        if (games < 30) flags.Add("표본<30G");
        if (fip is < 80 or > 120) flags.Add("FIP PF 극단");
        if (run is < 80 or > 120) flags.Add("득점 PF 극단");
        if (fip.HasValue && run.HasValue && Math.Abs(fip.Value - run.Value) >= 15) flags.Add("FIP/득점 괴리>=15");
        if (used.HasValue && fip.HasValue && Math.Abs(used.Value - fip.Value) >= 15) flags.Add("사용/연도 PF 괴리>=15");
        return flags.Count == 0 ? string.Empty : string.Join(" · ", flags);
    }

    private static double? Weighted<T>(IEnumerable<T> rows, Func<T,double?> value, Func<T,double> weight)
    {
        double sum = 0, weights = 0;
        foreach (var row in rows)
        {
            var v = value(row); var w = weight(row);
            if (!v.HasValue || w <= 0) continue;
            sum += v.Value * w; weights += w;
        }
        return weights > 0 ? sum / weights : null;
    }

    private static double? PercentilePf(IEnumerable<double?> values, double p)
    {
        var a = values.Where(x => x.HasValue && double.IsFinite(x.Value)).Select(x => x!.Value).OrderBy(x => x).ToArray();
        if (a.Length == 0) return null;
        if (a.Length == 1) return a[0];
        var pos = Math.Clamp(p,0,1) * (a.Length - 1); var lo=(int)Math.Floor(pos); var hi=(int)Math.Ceiling(pos);
        return lo == hi ? a[lo] : a[lo] + (a[hi]-a[lo])*(pos-lo);
    }
    private static double? Min(IEnumerable<double?> values) { var a=values.Where(x=>x.HasValue).Select(x=>x!.Value).ToArray(); return a.Length==0?null:a.Min(); }
    private static double? Max(IEnumerable<double?> values) { var a=values.Where(x=>x.HasValue).Select(x=>x!.Value).ToArray(); return a.Length==0?null:a.Max(); }

    private sealed class ParkDiagnosticRoleRow
    {
        public int Year { get; init; } public string Stadium { get; init; }=string.Empty; public string TeamCode { get; init; }=string.Empty;
        public int InningsOuts { get; init; } public int Runs { get; init; } public int EarnedRuns { get; init; }
        public int HomeRuns { get; init; } public int Walks { get; init; } public int HitBatters { get; init; } public int Strikeouts { get; init; } public int InfieldFlies { get; init; }
    }
    private sealed class ParkDiagnosticGameRow
    { public int Year { get; init; } public string Stadium { get; init; }=string.Empty; public string HomeTeamCode { get; init; }=string.Empty; public string AwayTeamCode { get; init; }=string.Empty; public int TotalRuns { get; init; } }
    private sealed class ParkDiagnosticTotals
    { public int InningsOuts; public int Runs; public int EarnedRuns; public int HomeRuns; public int Walks; public int HitBatters; public int Strikeouts; public int InfieldFlies; }
}
