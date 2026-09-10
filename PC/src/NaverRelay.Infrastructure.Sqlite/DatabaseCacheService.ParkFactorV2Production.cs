using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    // KBO Pitcher WAR v4 production park factor.
    // 5Y run environment, 30/25/20/15/10 recency, 100-game neutral prior,
    // 85..115 safety bounds, then innings-weighted re-centering to 100 by season.
    private static async Task<List<KboParkFactorV2Row>> BuildProductionParkFactorsV2Async(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        var roleRows = await ReadParkDiagnosticRoleRowsAsync(connection, cancellationToken).ConfigureAwait(false);
        var games = await ReadParkDiagnosticGamesAsync(connection, cancellationToken).ConfigureAwait(false);
        var annual = new List<ProductionParkWork>();

        foreach (var yearGroup in roleRows.GroupBy(x => x.Year).OrderBy(g => g.Key))
        {
            var year = yearGroup.Key;
            var yearGames = games.Where(x => x.Year == year).ToList();
            foreach (var stadiumGroup in yearGroup.GroupBy(x => x.Stadium, StringComparer.OrdinalIgnoreCase))
            {
                var stadium = stadiumGroup.Key;
                var atPark = SumParkRows(stadiumGroup);
                var stadiumGames = yearGames.Where(g => string.Equals(g.Stadium, stadium, StringComparison.OrdinalIgnoreCase)).ToList();
                var homeClub = stadiumGames.Where(g => !string.IsNullOrWhiteSpace(g.HomeTeamCode))
                    .GroupBy(g => g.HomeTeamCode, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? string.Empty;
                var road = string.IsNullOrWhiteSpace(homeClub)
                    ? new List<ParkDiagnosticGameRow>()
                    : yearGames.Where(g => !string.Equals(g.Stadium, stadium, StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(g.HomeTeamCode, homeClub, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(g.AwayTeamCode, homeClub, StringComparison.OrdinalIgnoreCase))).ToList();
                var homeRpg = stadiumGames.Count > 0 ? stadiumGames.Sum(g => g.TotalRuns) / (double)stadiumGames.Count : 0.0;
                var roadRpg = road.Count > 0 ? road.Sum(g => g.TotalRuns) / (double)road.Count : 0.0;
                var runPf = roadRpg > 0 ? 100.0 * homeRpg / roadRpg : 100.0;
                annual.Add(new ProductionParkWork
                {
                    Year = year, Stadium = stadium, Games = stadiumGames.Count,
                    Innings = atPark.InningsOuts / 3.0, AnnualFactor = runPf
                });
            }
        }

        var result = new List<KboParkFactorV2Row>();
        foreach (var target in annual)
        {
            var history = annual.Where(x => string.Equals(x.Stadium, target.Stadium, StringComparison.OrdinalIgnoreCase)
                    && x.Year <= target.Year && x.Year >= target.Year - 4 && x.Games > 0).ToList();
            double numerator = 0, denominator = 0, totalGames = 0;
            foreach (var h in history)
            {
                var age = target.Year - h.Year;
                var recency = age switch { 0 => 0.30, 1 => 0.25, 2 => 0.20, 3 => 0.15, _ => 0.10 };
                var w = recency * h.Games;
                numerator += h.AnnualFactor * w;
                denominator += w;
                totalGames += h.Games;
            }
            var raw = denominator > 0 ? numerator / denominator : 100.0;
            var reliability = totalGames > 0 ? totalGames / (totalGames + 100.0) : 0.0;
            var regressed = 100.0 + (raw - 100.0) * reliability;
            result.Add(new KboParkFactorV2Row
            {
                Year = target.Year, Stadium = target.Stadium, Games = target.Games, Innings = target.Innings,
                RawFiveYearFactor = raw, Reliability = reliability, Factor = Math.Clamp(regressed, 85.0, 115.0)
            });
        }

        foreach (var group in result.GroupBy(x => x.Year))
        {
            var rows = group.ToList();
            for (var pass = 0; pass < 8; pass++)
            {
                var weight = rows.Sum(x => Math.Max(0.0, x.Innings));
                if (weight <= 0) break;
                var mean = rows.Sum(x => x.Factor * Math.Max(0.0, x.Innings)) / weight;
                if (mean <= 0) break;
                var scale = 100.0 / mean;
                foreach (var row in rows) row.Factor = Math.Clamp(row.Factor * scale, 85.0, 115.0);
                if (Math.Abs(mean - 100.0) < 0.00001) break;
            }
        }
        return result;
    }

    private sealed class ProductionParkWork
    {
        public int Year { get; init; }
        public string Stadium { get; init; } = string.Empty;
        public int Games { get; init; }
        public double Innings { get; init; }
        public double AnnualFactor { get; init; }
    }
}
