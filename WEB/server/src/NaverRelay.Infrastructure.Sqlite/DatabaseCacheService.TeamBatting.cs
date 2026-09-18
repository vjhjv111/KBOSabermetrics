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
                        AND NOT EXISTS (
                            SELECT 1
                            FROM RunnerEvents early_runner
                            INNER JOIN NormalizedEvents early_event
                                ON early_event.EventId=early_runner.SourceEventId
                            INNER JOIN NormalizedEvents result_event
                                ON result_event.EventId=pa.ResultEventId
                            WHERE early_runner.PlateAppearanceId=pa.PlateAppearanceId
                              AND early_runner.FromBase=1
                              AND early_event.ChronologicalIndex<result_event.ChronologicalIndex)
                       THEN 1 ELSE 0 END) AS DoublePlayOpportunities,
                   SUM(CASE
                       WHEN pa.BeforeOuts BETWEEN 0 AND 1
                        AND (NULLIF(TRIM(COALESCE(pa.BeforeFirstRunnerPcode,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeFirstRunnerName,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeSecondRunnerPcode,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeSecondRunnerName,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeThirdRunnerPcode,'')),'') IS NOT NULL
                          OR NULLIF(TRIM(COALESCE(pa.BeforeThirdRunnerName,'')),'') IS NOT NULL)
                        AND pa.IsOut=1
                        AND pa.IsHit=0
                        AND pa.IsSacrifice=0
                        AND pa.ResultType NOT IN ($walk,$intentionalWalk,$hitByPitch,$reachedOnError,$fieldersChoice)
                        AND (pa.BattedBallType=$bunt
                          OR EXISTS (
                              SELECT 1
                              FROM Pitches bunt_pitch
                              WHERE bunt_pitch.PlateAppearanceId=pa.PlateAppearanceId
                                AND (bunt_pitch.PitchResult IN ($buntFoul,$buntWhiff)
                                  OR UPPER(TRIM(COALESCE(bunt_pitch.RawPitchResult,'')))=$buntWhiffRaw)))
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
        command.Parameters.AddWithValue("$bunt", (int)BattedBallType.Bunt);
        command.Parameters.AddWithValue("$buntFoul", (int)PitchResultType.BuntFoul);
        command.Parameters.AddWithValue("$buntWhiff", (int)PitchResultType.BuntSwingingStrike);
        command.Parameters.AddWithValue("$buntWhiffRaw", "V");
        command.Parameters.AddWithValue("$walk", (int)BattingResultType.Walk);
        command.Parameters.AddWithValue("$intentionalWalk", (int)BattingResultType.IntentionalWalk);
        command.Parameters.AddWithValue("$hitByPitch", (int)BattingResultType.HitByPitch);
        command.Parameters.AddWithValue("$fieldersChoice", (int)BattingResultType.FieldersChoice);
        command.Parameters.AddWithValue("$reachedOnError", (int)BattingResultType.ReachedOnError);
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
