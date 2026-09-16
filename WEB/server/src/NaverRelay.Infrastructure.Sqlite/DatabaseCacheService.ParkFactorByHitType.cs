using Microsoft.Data.Sqlite;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    /// <summary>
    /// Single-season, per-hit-type park factors ("파크 팩터 (Single)"). Display-only: this never
    /// feeds WAR or wRC+ (those use the 5-year regressed KboParkFactorsV2 via GetLeagueReferenceAsync).
    /// Method mirrors the existing SimpleRunParkFactor diagnostic (see
    /// DatabaseCacheService.ParkFactorDiagnostics.cs): for each stadium-year, PF = 100 * (stat per
    /// game at this park) / (stat per game in the park's primary home club's road games that season).
    /// </summary>
    public async Task<List<ParkFactorByHitTypeRow>> GetParkFactorByHitTypeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var games = await ReadParkHitTypeGamesAsync(connection, cancellationToken).ConfigureAwait(false);
        var result = new List<ParkFactorByHitTypeRow>();

        foreach (var yearGroup in games.GroupBy(x => x.Year).OrderBy(g => g.Key))
        {
            var year = yearGroup.Key;
            var yearGames = yearGroup.ToList();
            foreach (var stadiumGroup in yearGames.GroupBy(x => x.Stadium, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.CurrentCulture))
            {
                var stadium = stadiumGroup.Key;
                var atPark = stadiumGroup.ToList();
                var gameCount = atPark.Count;

                var homeClub = atPark.Where(g => !string.IsNullOrWhiteSpace(g.HomeTeamCode))
                    .GroupBy(g => g.HomeTeamCode, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? string.Empty;

                var roadGames = string.IsNullOrWhiteSpace(homeClub)
                    ? new List<ParkHitTypeGameRow>()
                    : yearGames.Where(g => !string.Equals(g.Stadium, stadium, StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(g.HomeTeamCode, homeClub, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(g.AwayTeamCode, homeClub, StringComparison.OrdinalIgnoreCase))).ToList();

                result.Add(new ParkFactorByHitTypeRow
                {
                    Year = year,
                    Stadium = stadium,
                    Games = gameCount,
                    RunParkFactor = ComponentFactor(atPark, roadGames, g => g.Runs),
                    SingleParkFactor = ComponentFactor(atPark, roadGames, g => g.Singles),
                    DoubleParkFactor = ComponentFactor(atPark, roadGames, g => g.Doubles),
                    TripleParkFactor = ComponentFactor(atPark, roadGames, g => g.Triples),
                    HomeRunParkFactor = ComponentFactor(atPark, roadGames, g => g.HomeRuns),
                    ExtraBaseHitParkFactor = ComponentFactor(atPark, roadGames, g => g.Doubles + g.Triples + g.HomeRuns),
                });
            }
        }
        return result;
    }

    private static double? ComponentFactor(
        IReadOnlyList<ParkHitTypeGameRow> atPark,
        IReadOnlyList<ParkHitTypeGameRow> road,
        Func<ParkHitTypeGameRow, int> selector)
    {
        if (atPark.Count == 0) return null;
        var atParkRate = atPark.Sum(selector) / (double)atPark.Count;
        if (road.Count == 0) return null;
        var roadRate = road.Sum(selector) / (double)road.Count;
        if (roadRate <= 0) return null;
        return 100.0 * atParkRate / roadRate;
    }

    private static async Task<List<ParkHitTypeGameRow>> ReadParkHitTypeGamesAsync(
        SqliteConnection connection, CancellationToken token)
    {
        var result = new List<ParkHitTypeGameRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.SeasonYear, COALESCE(g.Stadium,''), COALESCE(g.HomeTeamCode,''), COALESCE(g.AwayTeamCode,''),
                   SUM(b.Runs), SUM(b.Singles), SUM(b.Doubles), SUM(b.Triples), SUM(b.HR)
            FROM Games g
            INNER JOIN BatterGameStats b ON b.GameId=g.GameId
            WHERE g.SeasonYear IS NOT NULL
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
              AND TRIM(COALESCE(g.Stadium,''))<>''
            GROUP BY g.GameId, g.SeasonYear, g.Stadium, g.HomeTeamCode, g.AwayTeamCode;
            """;
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            result.Add(new ParkHitTypeGameRow
            {
                Year = reader.GetInt32(0),
                Stadium = reader.GetString(1),
                HomeTeamCode = reader.GetString(2),
                AwayTeamCode = reader.GetString(3),
                Runs = ReadInt32(reader, 4),
                Singles = ReadInt32(reader, 5),
                Doubles = ReadInt32(reader, 6),
                Triples = ReadInt32(reader, 7),
                HomeRuns = ReadInt32(reader, 8),
            });
        }
        return result;
    }

    private sealed class ParkHitTypeGameRow
    {
        public int Year { get; init; }
        public string Stadium { get; init; } = string.Empty;
        public string HomeTeamCode { get; init; } = string.Empty;
        public string AwayTeamCode { get; init; } = string.Empty;
        public int Runs { get; init; }
        public int Singles { get; init; }
        public int Doubles { get; init; }
        public int Triples { get; init; }
        public int HomeRuns { get; init; }
    }
}
