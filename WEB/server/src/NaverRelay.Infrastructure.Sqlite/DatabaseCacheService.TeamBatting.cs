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
        var identity = query.Grouping switch
        {
            AnalyticsGrouping.PlayerCareer => (
                PlateAppearance: "pa.BatterPcode",
                GameStats: "b.Pcode",
                GameStatsGroup: "b.Pcode"),
            AnalyticsGrouping.Team => (
                PlateAppearance: "pa.BattingTeamCode",
                GameStats: "b.TeamCode",
                GameStatsGroup: "b.TeamCode"),
            _ => (
                PlateAppearance: "pa.BatterPcode||char(31)||pa.BattingTeamCode",
                GameStats: "b.Pcode||char(31)||b.TeamCode",
                GameStatsGroup: "b.Pcode, b.TeamCode"),
        };
        var leftOnBase = query.Grouping == AnalyticsGrouping.Team
            ? "MAX(tt.LeftOnBase)"
            : "SUM(CASE WHEN pa.IsOut=1 AND pa.ReachedBase=0 " +
              "THEN MAX(0, " + RunnerCountSql("pa") + "-pa.RunsScored-MAX(0,pa.OutsRecorded-1)) ELSE 0 END)";

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte},
            TeamTotals AS (
                SELECT {identity.GameStats} AS ContextKey,
                       SUM(b.PA)-SUM(b.Runs)-SUM(b.OutsRecorded) AS LeftOnBase
                FROM BatterGameStats b
                INNER JOIN FilteredGames tfg ON tfg.GameId=b.GameId
                WHERE ($resultTeam='' OR b.TeamCode=$resultTeam)
                GROUP BY {identity.GameStatsGroup}
            )
            SELECT {identity.PlateAppearance} AS ContextKey,
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
                   CASE WHEN $hasSituation=0 THEN {leftOnBase} ELSE NULL END AS LeftOnBase
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            LEFT JOIN TeamTotals tt ON tt.ContextKey={identity.PlateAppearance}
            WHERE pa.IsOfficial=1
              AND COALESCE(NULLIF(TRIM(pa.BattingTeamCode),''),'')<>''
              AND COALESCE(NULLIF(TRIM(pa.BatterPcode),''),'')<>''
              AND ($resultTeam='' OR pa.BattingTeamCode=$resultTeam)
              {situation.Sql}
            GROUP BY {identity.PlateAppearance};
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

    private static string RunnerCountSql(string alias) => $"""
        (CASE WHEN COALESCE(NULLIF(TRIM({alias}.BeforeFirstRunnerPcode),''),NULLIF(TRIM({alias}.BeforeFirstRunnerName),'')) IS NOT NULL THEN 1 ELSE 0 END+
         CASE WHEN COALESCE(NULLIF(TRIM({alias}.BeforeSecondRunnerPcode),''),NULLIF(TRIM({alias}.BeforeSecondRunnerName),'')) IS NOT NULL THEN 1 ELSE 0 END+
         CASE WHEN COALESCE(NULLIF(TRIM({alias}.BeforeThirdRunnerPcode),''),NULLIF(TRIM({alias}.BeforeThirdRunnerName),'')) IS NOT NULL THEN 1 ELSE 0 END)
        """;
}
