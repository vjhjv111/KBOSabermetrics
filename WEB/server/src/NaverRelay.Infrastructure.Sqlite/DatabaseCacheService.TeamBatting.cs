using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    internal async Task<IReadOnlyDictionary<string, TeamBattingContext>> GetTeamBattingContextAsync(
        GameQuery query,
        CancellationToken cancellationToken = default)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var rows = new Dictionary<string, TeamBattingContext>(StringComparer.Ordinal);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte},
            TeamTotals AS (
                SELECT b.TeamCode,
                       SUM(b.PA)-SUM(b.Runs)-SUM(b.OutsRecorded) AS LeftOnBase
                FROM BatterGameStats b
                INNER JOIN FilteredGames tfg ON tfg.GameId=b.GameId
                WHERE ($resultTeam='' OR b.TeamCode=$resultTeam)
                GROUP BY b.TeamCode
            )
            SELECT pa.BattingTeamCode,
                   SUM(CASE
                       WHEN pa.BeforeOuts BETWEEN 0 AND 1
                        AND (NULLIF(TRIM(COALESCE(pa.BeforeFirstRunnerPcode,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeFirstRunnerName,'')),'') IS NOT NULL)
                       THEN 1 ELSE 0 END) AS DoublePlayOpportunities,
                   SUM(CASE
                       WHEN pa.BeforeOuts BETWEEN 0 AND 1
                        AND (NULLIF(TRIM(COALESCE(pa.BeforeFirstRunnerPcode,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeFirstRunnerName,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeSecondRunnerPcode,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeSecondRunnerName,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeThirdRunnerPcode,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeThirdRunnerName,'')),'') IS NOT NULL)
                        AND (pa.ResultType=$buntOut
                          OR (pa.ResultType=$strikeout
                           AND (COALESCE(pa.NormalizedResultText,'') LIKE '%쓰리%번트%'
                             OR COALESCE(pa.ResultText,'') LIKE '%쓰리%번트%')))
                       THEN 1 ELSE 0 END) AS SacrificeBuntFailures,
                   CASE WHEN $hasSituation=0 THEN MAX(tt.LeftOnBase) ELSE NULL END AS LeftOnBase
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            LEFT JOIN TeamTotals tt ON tt.TeamCode=pa.BattingTeamCode
            WHERE pa.IsOfficial=1
              AND COALESCE(NULLIF(TRIM(pa.BattingTeamCode),''),'')<>''
              AND COALESCE(NULLIF(TRIM(pa.BatterPcode),''),'')<>''
              AND ($resultTeam='' OR pa.BattingTeamCode=$resultTeam)
              {situation.Sql}
            GROUP BY pa.BattingTeamCode;
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        command.Parameters.AddWithValue("$buntOut", (int)BattingResultType.BuntOut);
        command.Parameters.AddWithValue("$strikeout", (int)BattingResultType.Strikeout);
        command.Parameters.AddWithValue("$hasSituation", query.HasSituationFilters ? 1 : 0);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows[reader.GetString(0)] = new TeamBattingContext(
                ReadInt32(reader, 1),
                ReadInt32(reader, 2),
                reader.IsDBNull(3) ? null : Math.Max(0, ReadInt32(reader, 3)));
        }

        return rows;
    }
}
