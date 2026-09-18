using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    private static async Task<(WobaConstants Overall, Dictionary<int, WobaConstants> BySeason)>
        ReadWobaConstantsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rawSeasons = new List<WobaRunValueSeason>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH source AS (
                SELECT g.SeasonYear AS Year,
                       pa.GameId,
                       COALESCE(pa.Inning,0) AS Inning,
                       COALESCE(pa.BattingTeamCode,'') AS BattingTeamCode,
                       COALESCE(pa.OfficialSequenceNumber,pa.SequenceNumber) AS Seq,
                       COALESCE(pa.BeforeOuts,-1) AS BeforeOuts,
                       (CASE WHEN COALESCE(NULLIF(TRIM(pa.BeforeFirstRunnerPcode),''),NULLIF(TRIM(pa.BeforeFirstRunnerName),'')) IS NOT NULL THEN 1 ELSE 0 END +
                        CASE WHEN COALESCE(NULLIF(TRIM(pa.BeforeSecondRunnerPcode),''),NULLIF(TRIM(pa.BeforeSecondRunnerName),'')) IS NOT NULL THEN 2 ELSE 0 END +
                        CASE WHEN COALESCE(NULLIF(TRIM(pa.BeforeThirdRunnerPcode),''),NULLIF(TRIM(pa.BeforeThirdRunnerName),'')) IS NOT NULL THEN 4 ELSE 0 END) AS BaseMask,
                       CASE WHEN pa.BattingTeamCode=g.HomeTeamCode THEN pa.BeforeHomeScore ELSE pa.BeforeAwayScore END AS BeforeBattingScore,
                       CASE WHEN pa.BattingTeamCode=g.HomeTeamCode THEN pa.AfterHomeScore ELSE pa.AfterAwayScore END AS AfterBattingScore,
                       COALESCE(pa.RunsScored,0) AS RunsOnPlay,
                       pa.CountsAsAtBat, pa.IsHit, pa.IsOut, pa.IsWalk, pa.IsIntentionalWalk,
                       pa.TotalBases, pa.ResultType
                FROM PlateAppearances pa
                INNER JOIN Games g ON g.GameId=pa.GameId
                WHERE pa.IsOfficial=1
                  AND pa.WasRecognized=1
                  AND g.SeasonYear IS NOT NULL
                  AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            ), valid AS (
                SELECT *,
                       LEAD(BeforeOuts) OVER (
                           PARTITION BY GameId,Inning,BattingTeamCode ORDER BY Seq) AS NextOuts,
                       LEAD(BaseMask) OVER (
                           PARTITION BY GameId,Inning,BattingTeamCode ORDER BY Seq) AS NextBaseMask,
                       MAX(CASE
                               WHEN AfterBattingScore>=BeforeBattingScore THEN AfterBattingScore
                               ELSE BeforeBattingScore+RunsOnPlay
                           END) OVER (
                           PARTITION BY GameId,Inning,BattingTeamCode) AS HalfFinalScore
                FROM source
                WHERE BeforeOuts BETWEEN 0 AND 2
                  AND BeforeBattingScore IS NOT NULL
            ), run_expectancy AS (
                SELECT Year,BeforeOuts,BaseMask,
                       AVG(HalfFinalScore-BeforeBattingScore) AS ExpectedRuns
                FROM valid
                GROUP BY Year,BeforeOuts,BaseMask
            ), valued AS (
                SELECT v.*,
                       v.RunsOnPlay+COALESCE(after_state.ExpectedRuns,0.0)-before_state.ExpectedRuns AS RunValue
                FROM valid v
                INNER JOIN run_expectancy before_state
                        ON before_state.Year=v.Year
                       AND before_state.BeforeOuts=v.BeforeOuts
                       AND before_state.BaseMask=v.BaseMask
                LEFT JOIN run_expectancy after_state
                       ON after_state.Year=v.Year
                      AND after_state.BeforeOuts=v.NextOuts
                      AND after_state.BaseMask=v.NextBaseMask
            )
            SELECT Year,
                   COUNT(*),
                   SUM(CASE WHEN IsOut=1 AND ResultType<>18 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN IsOut=1 AND ResultType<>18 THEN RunValue ELSE 0 END),
                   SUM(CASE WHEN IsWalk=1 AND IsIntentionalWalk=0 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN IsWalk=1 AND IsIntentionalWalk=0 THEN RunValue ELSE 0 END),
                   SUM(CASE WHEN ResultType=9 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN ResultType=9 THEN RunValue ELSE 0 END),
                   SUM(CASE WHEN IsHit=1 AND TotalBases=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN IsHit=1 AND TotalBases=1 THEN RunValue ELSE 0 END),
                   SUM(CASE WHEN IsHit=1 AND TotalBases=2 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN IsHit=1 AND TotalBases=2 THEN RunValue ELSE 0 END),
                   SUM(CASE WHEN IsHit=1 AND TotalBases=3 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN IsHit=1 AND TotalBases=3 THEN RunValue ELSE 0 END),
                   SUM(CASE WHEN IsHit=1 AND TotalBases=4 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN IsHit=1 AND TotalBases=4 THEN RunValue ELSE 0 END),
                   SUM(CountsAsAtBat), SUM(IsHit), SUM(IsWalk), SUM(IsIntentionalWalk),
                   SUM(CASE WHEN ResultType=9 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN ResultType=17 THEN 1 ELSE 0 END),
                   SUM(RunsOnPlay)
            FROM valued
            GROUP BY Year
            ORDER BY Year;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rawSeasons.Add(new WobaRunValueSeason
            {
                Year = ReadInt32(reader, 0), SamplePlateAppearances = ReadInt32(reader, 1),
                Outs = ReadInt32(reader, 2), OutRunValue = ReadWobaDouble(reader, 3),
                UnintentionalWalks = ReadInt32(reader, 4), UnintentionalWalkRunValue = ReadWobaDouble(reader, 5),
                HitByPitches = ReadInt32(reader, 6), HitByPitchRunValue = ReadWobaDouble(reader, 7),
                Singles = ReadInt32(reader, 8), SingleRunValue = ReadWobaDouble(reader, 9),
                Doubles = ReadInt32(reader, 10), DoubleRunValue = ReadWobaDouble(reader, 11),
                Triples = ReadInt32(reader, 12), TripleRunValue = ReadWobaDouble(reader, 13),
                HomeRuns = ReadInt32(reader, 14), HomeRunRunValue = ReadWobaDouble(reader, 15),
                AtBats = ReadInt32(reader, 16), Hits = ReadInt32(reader, 17),
                Walks = ReadInt32(reader, 18), IntentionalWalks = ReadInt32(reader, 19),
                TotalHitByPitches = ReadInt32(reader, 20), SacrificeFlies = ReadInt32(reader, 21),
                Runs = ReadInt32(reader, 22),
            });
        }

        var bySeason = rawSeasons.ToDictionary(row => row.Year, BuildWobaConstants);
        var reliable = rawSeasons.Where(row => bySeason[row.Year].Source == "RE24").ToList();
        var overall = reliable.Count > 0
            ? BuildWobaConstants(MergeWobaSeasons(reliable))
            : BuildWobaConstants(MergeWobaSeasons(rawSeasons));
        overall.SeasonYear = null;
        overall.Source = overall.Source == "RE24" ? "RE24 (유효 시즌 통합)" : overall.Source;
        return (overall, bySeason);
    }

    private static WobaConstants BuildWobaConstants(WobaRunValueSeason row)
    {
        var obp = Divide(row.Hits + row.Walks + row.TotalHitByPitches,
            row.AtBats + row.Walks + row.TotalHitByPitches + row.SacrificeFlies);
        var denominator = row.AtBats + row.Walks - row.IntentionalWalks +
                          row.TotalHitByPitches + row.SacrificeFlies;
        var fallback = new WobaConstants
        {
            SeasonYear = row.Year > 0 ? row.Year : null,
            Source = "fixed-fallback (점수 전이 부족)",
            SamplePlateAppearances = row.SamplePlateAppearances,
            LeagueObp = obp,
            RunsPerPa = Divide(row.Runs, row.SamplePlateAppearances),
        };
        fallback.LeagueWoba = fallback.Calculate(
            row.AtBats, row.Walks, row.IntentionalWalks, row.TotalHitByPitches,
            row.SacrificeFlies, row.Singles, row.Doubles, row.Triples, row.HomeRuns) ?? 0.0;

        if (row.SamplePlateAppearances < 5_000 || row.Outs <= 0 || row.UnintentionalWalks <= 0 ||
            row.HitByPitches <= 0 || row.Singles <= 0 || row.Doubles <= 0 ||
            row.Triples <= 0 || row.HomeRuns <= 0 || denominator <= 0)
            return fallback;

        var outValue = row.OutRunValue / row.Outs;
        var rawBb = row.UnintentionalWalkRunValue / row.UnintentionalWalks - outValue;
        var rawHbp = row.HitByPitchRunValue / row.HitByPitches - outValue;
        var rawSingle = row.SingleRunValue / row.Singles - outValue;
        var rawDouble = row.DoubleRunValue / row.Doubles - outValue;
        var rawTriple = row.TripleRunValue / row.Triples - outValue;
        var rawHomeRun = row.HomeRunRunValue / row.HomeRuns - outValue;
        var rawLeagueWoba = Divide(
            rawBb * row.UnintentionalWalks + rawHbp * row.HitByPitches +
            rawSingle * row.Singles + rawDouble * row.Doubles + rawTriple * row.Triples +
            rawHomeRun * row.HomeRuns,
            denominator);
        var scale = rawLeagueWoba > 0 ? obp / rawLeagueWoba : 0.0;
        if (!double.IsFinite(scale) || scale < 0.5 || scale > 2.0)
            return fallback;

        return new WobaConstants
        {
            SeasonYear = row.Year > 0 ? row.Year : null,
            Source = "RE24",
            SamplePlateAppearances = row.SamplePlateAppearances,
            Scale = scale,
            UnintentionalWalk = rawBb * scale,
            HitByPitch = rawHbp * scale,
            Single = rawSingle * scale,
            Double = rawDouble * scale,
            Triple = rawTriple * scale,
            HomeRun = rawHomeRun * scale,
            LeagueWoba = rawLeagueWoba * scale,
            LeagueObp = obp,
            RunsPerPa = Divide(row.Runs, row.SamplePlateAppearances),
        };
    }

    private static WobaRunValueSeason MergeWobaSeasons(IEnumerable<WobaRunValueSeason> rows)
    {
        var result = new WobaRunValueSeason();
        foreach (var row in rows)
        {
            result.SamplePlateAppearances += row.SamplePlateAppearances;
            result.Outs += row.Outs; result.OutRunValue += row.OutRunValue;
            result.UnintentionalWalks += row.UnintentionalWalks;
            result.UnintentionalWalkRunValue += row.UnintentionalWalkRunValue;
            result.HitByPitches += row.HitByPitches; result.HitByPitchRunValue += row.HitByPitchRunValue;
            result.Singles += row.Singles; result.SingleRunValue += row.SingleRunValue;
            result.Doubles += row.Doubles; result.DoubleRunValue += row.DoubleRunValue;
            result.Triples += row.Triples; result.TripleRunValue += row.TripleRunValue;
            result.HomeRuns += row.HomeRuns; result.HomeRunRunValue += row.HomeRunRunValue;
            result.AtBats += row.AtBats; result.Hits += row.Hits; result.Walks += row.Walks;
            result.IntentionalWalks += row.IntentionalWalks;
            result.TotalHitByPitches += row.TotalHitByPitches;
            result.SacrificeFlies += row.SacrificeFlies; result.Runs += row.Runs;
        }
        return result;
    }

    private static double ReadWobaDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0.0 : Convert.ToDouble(reader.GetValue(ordinal));

    private sealed class WobaRunValueSeason
    {
        public int Year { get; init; }
        public int SamplePlateAppearances { get; set; }
        public int Outs { get; set; }
        public double OutRunValue { get; set; }
        public int UnintentionalWalks { get; set; }
        public double UnintentionalWalkRunValue { get; set; }
        public int HitByPitches { get; set; }
        public double HitByPitchRunValue { get; set; }
        public int Singles { get; set; }
        public double SingleRunValue { get; set; }
        public int Doubles { get; set; }
        public double DoubleRunValue { get; set; }
        public int Triples { get; set; }
        public double TripleRunValue { get; set; }
        public int HomeRuns { get; set; }
        public double HomeRunRunValue { get; set; }
        public int AtBats { get; set; }
        public int Hits { get; set; }
        public int Walks { get; set; }
        public int IntentionalWalks { get; set; }
        public int TotalHitByPitches { get; set; }
        public int SacrificeFlies { get; set; }
        public int Runs { get; set; }
    }
}
