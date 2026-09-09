using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    private sealed record SituationSql(string Sql, IReadOnlyList<(string Name, object Value)> Parameters);

    private static SituationSql BuildPlateAppearanceSituationSql(GameQuery query, string alias = "pa", bool includeCountReached = true)
    {
        var clauses = new List<string>();
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(query.InningFilter))
        {
            switch (query.InningFilter)
            {
                case "1~3회": clauses.Add($"{alias}.Inning BETWEEN 1 AND 3"); break;
                case "4~6회": clauses.Add($"{alias}.Inning BETWEEN 4 AND 6"); break;
                case "7~9회": clauses.Add($"{alias}.Inning BETWEEN 7 AND 9"); break;
                case "연장": clauses.Add($"{alias}.Inning >= 10"); break;
                default:
                {
                    var text = query.InningFilter.Replace("회", string.Empty, StringComparison.Ordinal).Trim();
                    if (int.TryParse(text, out var inning))
                    {
                        clauses.Add($"{alias}.Inning=$ctxInning");
                        parameters.Add(("$ctxInning", inning));
                    }
                    break;
                }
            }
        }

        if (query.OutsBefore.HasValue)
        {
            clauses.Add($"{alias}.BeforeOuts=$ctxOuts");
            parameters.Add(("$ctxOuts", query.OutsBefore.Value));
        }

        if (query.BatOrder.HasValue)
        {
            clauses.Add($"{alias}.BatOrder=$ctxBatOrder");
            parameters.Add(("$ctxBatOrder", query.BatOrder.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.RunnerState))
        {
            var r1 = $"NULLIF(TRIM(COALESCE({alias}.BeforeFirstRunnerPcode,'')),'') IS NOT NULL";
            var r2 = $"NULLIF(TRIM(COALESCE({alias}.BeforeSecondRunnerPcode,'')),'') IS NOT NULL";
            var r3 = $"NULLIF(TRIM(COALESCE({alias}.BeforeThirdRunnerPcode,'')),'') IS NOT NULL";
            var n1 = $"NOT ({r1})";
            var n2 = $"NOT ({r2})";
            var n3 = $"NOT ({r3})";
            clauses.Add(query.RunnerState switch
            {
                "주자 없음" => $"({n1} AND {n2} AND {n3})",
                "1루" => $"({r1} AND {n2} AND {n3})",
                "2루" => $"({n1} AND {r2} AND {n3})",
                "3루" => $"({n1} AND {n2} AND {r3})",
                "1·2루" => $"({r1} AND {r2} AND {n3})",
                "1·3루" => $"({r1} AND {n2} AND {r3})",
                "2·3루" => $"({n1} AND {r2} AND {r3})",
                "만루" => $"({r1} AND {r2} AND {r3})",
                "득점권" => $"({r2} OR {r3})",
                _ => "1=1",
            });
        }

        if (!string.IsNullOrWhiteSpace(query.ScoreSituation))
        {
            var diff = $"(CASE WHEN {alias}.BattingSide=1 THEN COALESCE({alias}.BeforeHomeScore,0)-COALESCE({alias}.BeforeAwayScore,0) ELSE COALESCE({alias}.BeforeAwayScore,0)-COALESCE({alias}.BeforeHomeScore,0) END)";
            clauses.Add(query.ScoreSituation switch
            {
                "동점" => $"{diff}=0",
                "1점 리드" => $"{diff}=1",
                "2점 리드" => $"{diff}=2",
                "3점 이상 리드" => $"{diff}>=3",
                "1점 열세" => $"{diff}=-1",
                "2점 열세" => $"{diff}=-2",
                "3점 이상 열세" => $"{diff}<=-3",
                "1점차 이내" => $"ABS({diff})<=1",
                "2점차 이내" => $"ABS({diff})<=2",
                "3점차 이내" => $"ABS({diff})<=3",
                "리드" => $"{diff}>0",
                "열세" => $"{diff}<0",
                _ => "1=1",
            });
        }

        if (includeCountReached && query.BallsBefore.HasValue && query.StrikesBefore.HasValue)
        {
            clauses.Add($"EXISTS (SELECT 1 FROM Pitches ctxp WHERE ctxp.PlateAppearanceId={alias}.PlateAppearanceId AND ctxp.BallsBefore=$ctxBalls AND ctxp.StrikesBefore=$ctxStrikes)");
            parameters.Add(("$ctxBalls", query.BallsBefore.Value));
            parameters.Add(("$ctxStrikes", query.StrikesBefore.Value));
        }

        return new SituationSql(clauses.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", clauses), parameters);
    }

    private static SituationSql BuildPitchSituationSql(GameQuery query, string alias = "p")
    {
        var clauses = new List<string>();
        var parameters = new List<(string Name, object Value)>();
        if (query.BallsBefore.HasValue && query.StrikesBefore.HasValue)
        {
            clauses.Add($"{alias}.BallsBefore=$ctxPitchBalls AND {alias}.StrikesBefore=$ctxPitchStrikes");
            parameters.Add(("$ctxPitchBalls", query.BallsBefore.Value));
            parameters.Add(("$ctxPitchStrikes", query.StrikesBefore.Value));
        }
        return new SituationSql(clauses.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", clauses), parameters);
    }

    private static void AddSituationParameters(SqliteCommand command, params SituationSql[] filters)
    {
        foreach (var filter in filters)
            AddParameters(command, filter.Parameters);
    }
}
