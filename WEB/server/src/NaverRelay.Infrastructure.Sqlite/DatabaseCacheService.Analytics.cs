using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Statistics;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    private const double LeagueWbb = 0.69;
    private const double LeagueWhbp = 0.72;
    private const double LeagueW1b = 0.88;
    private const double LeagueW2b = 1.247;
    private const double LeagueW3b = 1.578;
    private const double LeagueWhr = 2.031;
    private const double LeagueWobaScale = 1.20;

    public async Task<LeagueReference> GetLeagueReferenceAsync(
        IProgress<DatabaseLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{LeagueReferenceCacheVersion}:kbo_r:all";
        var cached = await TryLoadComputedAsync<LeagueReference>(cacheKey, cancellationToken).ConfigureAwait(false);
        if (cached is not null) return cached;

        progress?.Report(new DatabaseLoadProgress(1, 5, "전체 kbo_r 타격 집계 조회 중"));
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var batting = await ReadLeagueBattingAsync(connection, cancellationToken).ConfigureAwait(false);

        progress?.Report(new DatabaseLoadProgress(2, 5, "전체 kbo_r 투수 최종 기록 조회 중"));
        var pitching = await ReadLeaguePitchingAsync(connection, cancellationToken).ConfigureAwait(false);

        progress?.Report(new DatabaseLoadProgress(3, 5, "원본 WPA와 구장별 FIP 환경 조회 중"));
        var averageAbsoluteWpa = await ReadAverageAbsoluteWpaAsync(connection, cancellationToken).ConfigureAwait(false);
        var parkFactors = await BuildParkFactorsAsync(connection, cancellationToken).ConfigureAwait(false);
        var parkFactorsV2 = await BuildProductionParkFactorsV2Async(connection, cancellationToken).ConfigureAwait(false);

        var plateAppearanceInnings = batting.Outs / 3.0;
        var wobaDenominator = batting.AtBats + batting.Walks - batting.IntentionalWalks +
                              batting.SacrificeFlies + batting.HitByPitch;
        var woba = Divide(
            LeagueWbb * (batting.Walks - batting.IntentionalWalks) +
            LeagueWhbp * batting.HitByPitch +
            LeagueW1b * batting.Singles +
            LeagueW2b * batting.Doubles +
            LeagueW3b * batting.Triples +
            LeagueWhr * batting.HomeRuns,
            wobaDenominator);
        var obp = Divide(
            batting.Hits + batting.Walks + batting.HitByPitch,
            batting.AtBats + batting.Walks + batting.HitByPitch + batting.SacrificeFlies);
        var slg = Divide(
            batting.Singles + 2.0 * batting.Doubles + 3.0 * batting.Triples + 4.0 * batting.HomeRuns,
            batting.AtBats);
        var ra9 = plateAppearanceInnings > 0 ? batting.Runs * 9.0 / plateAppearanceInnings : 0.0;
        var fipCore = plateAppearanceInnings > 0
            ? (13.0 * batting.HomeRuns + 3.0 * (batting.Walks + batting.HitByPitch) -
               2.0 * batting.Strikeouts) / plateAppearanceInnings
            : 0.0;

        var pitchingInnings = pitching.InningsOuts / 3.0;
        var leagueEra = pitchingInnings > 0 ? pitching.EarnedRuns * 9.0 / pitchingInnings : 0.0;
        var leagueRa9 = pitchingInnings > 0 ? pitching.RunsAllowed * 9.0 / pitchingInnings : 0.0;
        var ifFipCore = pitchingInnings > 0
            ? (13.0 * pitching.HomeRuns + 3.0 * (pitching.Walks + pitching.HitBatters) -
               2.0 * (pitching.Strikeouts + pitching.InfieldFlies)) / pitchingInnings
            : 0.0;
        var ifFipConstant = leagueEra - ifFipCore;
        var ra9Adjustment = leagueRa9 - leagueEra;
        var leagueFipR9 = leagueEra + ra9Adjustment;

        var reference = new LeagueReference
        {
            GameCount = batting.GameCount,
            PlateAppearances = batting.PlateAppearances,
            AtBats = batting.AtBats,
            Hits = batting.Hits,
            Singles = batting.Singles,
            Doubles = batting.Doubles,
            Triples = batting.Triples,
            HomeRuns = batting.HomeRuns,
            Walks = batting.Walks,
            IntentionalWalks = batting.IntentionalWalks,
            HitByPitch = batting.HitByPitch,
            Strikeouts = batting.Strikeouts,
            SacrificeFlies = batting.SacrificeFlies,
            Outs = batting.Outs,
            Runs = batting.Runs,
            FlyBalls = batting.FlyBalls,
            Woba = woba,
            Obp = obp,
            Slg = slg,
            Ops = obp + slg,
            RunsPerPa = Divide(batting.Runs, batting.PlateAppearances),
            Ra9 = ra9,
            FipConstant = ra9 - fipCore,
            HrPerFlyBall = Divide(batting.HomeRuns, batting.FlyBalls),
            PitchingInnings = pitchingInnings,
            EarnedRuns = pitching.EarnedRuns,
            RunsAllowed = pitching.RunsAllowed,
            PitchingHomeRuns = pitching.HomeRuns,
            PitchingWalks = pitching.Walks,
            PitchingHitBatters = pitching.HitBatters,
            PitchingStrikeouts = pitching.Strikeouts,
            InfieldFlies = pitching.InfieldFlies,
            LeagueEra = leagueEra,
            LeagueRa9 = leagueRa9,
            IfFipConstant = ifFipConstant,
            Ra9Adjustment = ra9Adjustment,
            LeagueFipR9 = leagueFipR9,
            AverageAbsoluteWpa = averageAbsoluteWpa > 0 ? averageAbsoluteWpa : 1.0,
            ParkFactors = parkFactors,
            KboParkFactorsV2 = parkFactorsV2,
        };

        progress?.Report(new DatabaseLoadProgress(4, 5, "KBO 투수 대체수준과 WARIP 계산 중"));
        reference.PitcherWar = await BuildPitcherWarCalibrationAsync(connection, reference, cancellationToken)
            .ConfigureAwait(false);
        reference.Constants = BuildLeagueConstantRows(reference);

        progress?.Report(new DatabaseLoadProgress(5, 5, "리그 상수와 파크 팩터 저장 중"));
        if (!WebReadOnly)
            await SaveLeagueReferenceTablesAsync(connection, reference, cancellationToken).ConfigureAwait(false);
        await SaveComputedAsync(cacheKey, reference, cancellationToken).ConfigureAwait(false);
        return reference;
    }

    private static async Task<LeagueBattingTotals> ReadLeagueBattingAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(DISTINCT g.GameId),
                   COALESCE(SUM(b.PA),0), COALESCE(SUM(b.AB),0), COALESCE(SUM(b.H),0),
                   COALESCE(SUM(b.Singles),0), COALESCE(SUM(b.Doubles),0),
                   COALESCE(SUM(b.Triples),0), COALESCE(SUM(b.HR),0),
                   COALESCE(SUM(b.BB),0), COALESCE(SUM(b.IBB),0), COALESCE(SUM(b.HBP),0),
                   COALESCE(SUM(b.SO),0), COALESCE(SUM(b.SF),0),
                   COALESCE(SUM(b.OutsRecorded),0), COALESCE(SUM(b.RunsScoredOnPlays),0),
                   COALESCE(SUM(b.FlyBalls),0)
            FROM Games g
            LEFT JOIN BatterGameStats b ON b.GameId=g.GameId
            WHERE LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r';
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new LeagueBattingTotals();
        return new LeagueBattingTotals
        {
            GameCount = ReadInt32(reader, 0),
            PlateAppearances = ReadInt32(reader, 1), AtBats = ReadInt32(reader, 2), Hits = ReadInt32(reader, 3),
            Singles = ReadInt32(reader, 4), Doubles = ReadInt32(reader, 5), Triples = ReadInt32(reader, 6),
            HomeRuns = ReadInt32(reader, 7), Walks = ReadInt32(reader, 8), IntentionalWalks = ReadInt32(reader, 9),
            HitByPitch = ReadInt32(reader, 10), Strikeouts = ReadInt32(reader, 11),
            SacrificeFlies = ReadInt32(reader, 12), Outs = ReadInt32(reader, 13),
            Runs = ReadInt32(reader, 14), FlyBalls = ReadInt32(reader, 15),
        };
    }

    private static async Task<LeaguePitchingTotals> ReadLeaguePitchingAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(p.InningsOuts),0), COALESCE(SUM(p.EarnedRuns),0),
                   COALESCE(SUM(p.RunsAllowed),0), COALESCE(SUM(p.HomeRunsAllowed),0),
                   COALESCE(SUM(p.FinalBB),0), COALESCE(SUM(p.FinalHBP),0),
                   COALESCE(SUM(p.FinalSO),0), COALESCE(SUM(p.IFFB),0)
            FROM PitcherGameStats p
            INNER JOIN Games g ON g.GameId=p.GameId
            WHERE p.HasFinalLine=1
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r';
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new LeaguePitchingTotals();
        return new LeaguePitchingTotals
        {
            InningsOuts = ReadInt32(reader, 0), EarnedRuns = ReadInt32(reader, 1),
            RunsAllowed = ReadInt32(reader, 2), HomeRuns = ReadInt32(reader, 3),
            Walks = ReadInt32(reader, 4), HitBatters = ReadInt32(reader, 5),
            Strikeouts = ReadInt32(reader, 6), InfieldFlies = ReadInt32(reader, 7),
        };
    }

    private static async Task<double> ReadAverageAbsoluteWpaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(AVG(ABS(r.WpaByPlate)),0)
            FROM RelayGroups r
            INNER JOIN Games g ON g.GameId=r.GameId
            WHERE LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
              AND r.WpaByPlate IS NOT NULL
              AND ABS(r.WpaByPlate)>0.0001;
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? 0.0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static async Task<List<ParkFactorGridRow>> BuildParkFactorsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var entries = new List<ParkEnvironmentRow>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT COALESCE(g.Stadium,''), p.TeamCode,
                       MIN(g.SeasonYear), MAX(g.SeasonYear), COUNT(DISTINCT g.GameId),
                       COALESCE(SUM(p.InningsOuts),0), COALESCE(SUM(p.HomeRunsAllowed),0),
                       COALESCE(SUM(p.FinalBB),0), COALESCE(SUM(p.FinalHBP),0),
                       COALESCE(SUM(p.FinalSO),0), COALESCE(SUM(p.IFFB),0)
                FROM PitcherGameStats p
                INNER JOIN Games g ON g.GameId=p.GameId
                WHERE p.HasFinalLine=1
                  AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
                  AND TRIM(COALESCE(g.Stadium,''))<>''
                  AND TRIM(COALESCE(p.TeamCode,''))<>''
                GROUP BY g.Stadium, p.TeamCode;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add(new ParkEnvironmentRow
                {
                    Stadium = reader.GetString(0), TeamCode = reader.GetString(1),
                    FirstSeason = NullableInt(reader, 2), LastSeason = NullableInt(reader, 3),
                    Games = ReadInt32(reader, 4), InningsOuts = ReadInt32(reader, 5),
                    HomeRuns = ReadInt32(reader, 6), Walks = ReadInt32(reader, 7),
                    HitBatters = ReadInt32(reader, 8), Strikeouts = ReadInt32(reader, 9),
                    InfieldFlies = ReadInt32(reader, 10),
                });
            }
        }

        var result = new List<ParkFactorGridRow>();
        foreach (var stadiumGroup in entries.GroupBy(row => row.Stadium, StringComparer.OrdinalIgnoreCase))
        {
            var stadium = Merge(stadiumGroup);
            var teams = stadiumGroup.Select(row => row.TeamCode).ToHashSet(StringComparer.Ordinal);
            var road = Merge(entries.Where(row =>
                teams.Contains(row.TeamCode) &&
                !string.Equals(row.Stadium, stadiumGroup.Key, StringComparison.OrdinalIgnoreCase)));
            var stadiumRate = FipComponentRate(stadium);
            var roadRate = FipComponentRate(road);
            var factor = road.InningsOuts > 0 && Math.Abs(roadRate) > 1e-9
                ? 100.0 * stadiumRate / roadRate
                : 100.0;
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor < 50.0 || factor > 150.0)
                factor = 100.0;

            var first = stadiumGroup.Where(row => row.FirstSeason.HasValue).Select(row => row.FirstSeason!.Value).DefaultIfEmpty().Min();
            var last = stadiumGroup.Where(row => row.LastSeason.HasValue).Select(row => row.LastSeason!.Value).DefaultIfEmpty().Max();
            var seasons = first > 0 && last > 0 ? (first == last ? first.ToString(CultureInfo.InvariantCulture) : $"{first}-{last}") : "-";
            var gameCount = stadiumGroup.Max(row => row.Games);
            result.Add(new ParkFactorGridRow
            {
                Stadium = stadiumGroup.Key,
                Seasons = seasons,
                Games = gameCount,
                Innings = stadium.InningsOuts / 3.0,
                HomeRuns = stadium.HomeRuns,
                Walks = stadium.Walks,
                HitBatters = stadium.HitBatters,
                Strikeouts = stadium.Strikeouts,
                InfieldFlies = stadium.InfieldFlies,
                RawFipFactor = factor,
                UsedFipFactor = factor,
                Confidence = gameCount >= 100 ? "높음" : gameCount >= 30 ? "보통" : "낮음",
            });
        }
        return result.OrderBy(row => row.Stadium, StringComparer.CurrentCulture).ToList();
    }

    private async Task SaveLeagueReferenceTablesAsync(
        SqliteConnection connection,
        LeagueReference reference,
        CancellationToken cancellationToken)
    {
        var sourceVersion = await GetSourceVersionAsync(cancellationToken).ConfigureAwait(false);
        var updatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM LeagueConstants; DELETE FROM ParkFactors;";
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO LeagueConstants(Metric, Value, Description, SourceVersion, UpdatedUtc)
                VALUES($metric, $value, $description, $version, $utc);
                """;
            foreach (var row in reference.Constants)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$metric", row.Metric);
                command.Parameters.AddWithValue("$value", DbValue(row.Value));
                command.Parameters.AddWithValue("$description", row.Description);
                command.Parameters.AddWithValue("$version", sourceVersion);
                command.Parameters.AddWithValue("$utc", updatedUtc);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ParkFactors(
                    Stadium, Seasons, Games, Innings, HomeRuns, Walks, HitBatters,
                    Strikeouts, InfieldFlies, RawFipFactor, UsedFipFactor, Confidence,
                    SourceVersion, UpdatedUtc)
                VALUES($stadium, $seasons, $games, $innings, $hr, $bb, $hbp,
                       $so, $iffb, $raw, $used, $confidence, $version, $utc);
                """;
            foreach (var row in reference.ParkFactors)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$stadium", row.Stadium);
                command.Parameters.AddWithValue("$seasons", row.Seasons);
                command.Parameters.AddWithValue("$games", row.Games);
                command.Parameters.AddWithValue("$innings", DbValue(row.Innings));
                command.Parameters.AddWithValue("$hr", row.HomeRuns);
                command.Parameters.AddWithValue("$bb", row.Walks);
                command.Parameters.AddWithValue("$hbp", row.HitBatters);
                command.Parameters.AddWithValue("$so", row.Strikeouts);
                command.Parameters.AddWithValue("$iffb", row.InfieldFlies);
                command.Parameters.AddWithValue("$raw", DbValue(row.RawFipFactor));
                command.Parameters.AddWithValue("$used", DbValue(row.UsedFipFactor));
                command.Parameters.AddWithValue("$confidence", row.Confidence);
                command.Parameters.AddWithValue("$version", sourceVersion);
                command.Parameters.AddWithValue("$utc", updatedUtc);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static List<LeagueConstantGridRow> BuildLeagueConstantRows(LeagueReference value) =>
    [
        Row("리그 PA", value.PlateAppearances, "전체 roundCode=kbo_r 데이터의 공식 타석"),
        Row("리그 R/PA", value.RunsPerPa, "득점 합계 ÷ 공식 타석"),
        Row("리그 OBP", value.Obp, "전체 정규시즌 출루율"),
        Row("리그 SLG", value.Slg, "전체 정규시즌 장타율"),
        Row("리그 wOBA*", value.Woba, "Phase 1 선형가중치"),
        Row("wOBA Scale*", LeagueWobaScale, "현재 계산 스케일"),
        Row("uBB 가중치*", LeagueWbb, "고의4구 제외 볼넷"),
        Row("HBP 가중치*", LeagueWhbp, "사구"),
        Row("1B 가중치*", LeagueW1b, "단타"),
        Row("2B 가중치*", LeagueW2b, "2루타"),
        Row("3B 가중치*", LeagueW3b, "3루타"),
        Row("HR 가중치*", LeagueWhr, "홈런"),
        Row("리그 RA9*", value.Ra9, "타석 귀속 득점 기반"),
        Row("FIP 상수*", value.FipConstant, "리그 평균 FIP를 RA9에 맞춤"),
        Row("리그 HR/FB*", value.HrPerFlyBall, "문자 중계 타구 유형 기반"),
        Row("투수 리그 IP", value.PitchingInnings, "경기별 투수 최종 기록"),
        Row("투수 리그 ERA", value.LeagueEra, "원본 ER 합계 기반"),
        Row("투수 리그 RA9", value.LeagueRa9, "원본 R 합계 기반"),
        Row("IFFB", value.InfieldFlies, "합의한 내야 뜬공 문장 규칙"),
        Row("ifFIP 상수", value.IfFipConstant, "리그 ifFIP를 리그 ERA에 맞춤"),
        Row("ERA→RA9 보정", value.Ra9Adjustment, "리그 RA9 - 리그 ERA"),
        Row("리그 FIPR9", value.LeagueFipR9, "ifFIP의 RA9 스케일"),
        Row("평균 |WPA|", value.AverageAbsoluteWpa, "유효 metricOption.wpaByPlate 평균"),
        Row("대체선수 승률 기준", value.PitcherWar.ReplacementWinningPercentage, "리그 전체 대체수준 승률 정책"),
        Row("투수 WAR 배분율", value.PitcherWar.PitcherWarShare, "리그 전체 WAR 중 투수 몫"),
        Row("KBO 투수 목표 WAR", value.PitcherWar.TargetPitcherWar, "경기 수×2×(0.500-대체승률)×투수 배분율"),
        Row("KBO fWAR 보정 전 합", value.PitcherWar.PreCorrectionFipWar, "WARIP 적용 전 리그 투수 fWAR 합"),
        Row("KBO fWAR 목표 달성률(보정 전)", value.PitcherWar.TargetPitcherWar > 0 ? value.PitcherWar.PreCorrectionFipWar / value.PitcherWar.TargetPitcherWar : 0.0, "보정 전 fWAR 합÷목표 투수 WAR"),
        Row("KBO fWAR WARIP", value.PitcherWar.FipWarPerInning, "(목표 WAR-보정 전 합)÷리그 IP"),
        Row("KBO fWAR WARIP 총보정", value.PitcherWar.FipWarPerInning * value.PitcherWar.TotalPitchingInnings, "KBO WARIP×리그 전체 IP"),
        Row("KBO fWAR 보정 후 합", value.PitcherWar.PreCorrectionFipWar + value.PitcherWar.FipWarPerInning * value.PitcherWar.TotalPitchingInnings, "WARIP 적용 후 리그 투수 fWAR 합"),
        Row("KBO RA9-WAR 보정 전 합", value.PitcherWar.PreCorrectionRa9War, "RA9 WARIP 적용 전 합"),
        Row("KBO RA9-WAR 목표 달성률(보정 전)", value.PitcherWar.TargetPitcherWar > 0 ? value.PitcherWar.PreCorrectionRa9War / value.PitcherWar.TargetPitcherWar : 0.0, "보정 전 RA9-WAR 합÷목표 투수 WAR"),
        Row("KBO RA9 WARIP", value.PitcherWar.Ra9WarPerInning, "(목표 WAR-RA9 보정 전 합)÷리그 IP"),
        Row("KBO RA9 WARIP 총보정", value.PitcherWar.Ra9WarPerInning * value.PitcherWar.TotalPitchingInnings, "RA9 WARIP×리그 전체 IP"),
        Row("대체수준 표본 선발 수", value.PitcherWar.StarterSamplePitchers, $"{value.PitcherWar.ReplacementSampleSeasons} 저사용 선발 역할 표본; {value.PitcherWar.StarterSampleInnings:0.0} IP"),
        Row("대체수준 표본 구원 수", value.PitcherWar.RelieverSamplePitchers, $"{value.PitcherWar.ReplacementSampleSeasons} 저사용 구원 역할 표본; {value.PitcherWar.RelieverSampleInnings:0.0} IP"),
        Row("KBO 선발 관측 Repl FIPR9", value.PitcherWar.EmpiricalStarterReplacementFipR9, "저사용 선발 표본을 리그 평균으로 회귀한 관측값"),
        Row("KBO 구원 관측 Repl FIPR9", value.PitcherWar.EmpiricalRelieverReplacementFipR9, "저사용 구원 표본을 리그 평균으로 회귀한 관측값"),
        Row("KBO 선발 Repl FIPR9", value.PitcherWar.StarterReplacementFipR9, "최근 3시즌 저사용 선발 표본 회귀값과 FG 하한 중 큰 값"),
        Row("KBO 선발 Repl FIP-", value.PitcherWar.StarterReplacementFipMinus, "100×선발 대체 FIPR9÷리그 FIPR9"),
        Row("KBO 구원 Repl FIPR9", value.PitcherWar.RelieverReplacementFipR9, "최근 3시즌 저사용 구원 표본 회귀값과 FG 하한 중 큰 값"),
        Row("KBO 구원 Repl FIP-", value.PitcherWar.RelieverReplacementFipMinus, "100×구원 대체 FIPR9÷리그 FIPR9"),
        Row("KBO 선발 관측 Repl RA9", value.PitcherWar.EmpiricalStarterReplacementRa9, "저사용 선발 표본의 회귀 pRA9"),
        Row("KBO 구원 관측 Repl RA9", value.PitcherWar.EmpiricalRelieverReplacementRa9, "저사용 구원 표본의 회귀 pRA9"),
        Row("KBO 선발 Repl RA9", value.PitcherWar.StarterReplacementRa9, "RA9-WAR용 선발 대체 실점률"),
        Row("KBO 구원 Repl RA9", value.PitcherWar.RelieverReplacementRa9, "RA9-WAR용 구원 대체 실점률"),
        Row("Blend fWAR 가중치", value.PitcherWar.BlendFipWeight, "KBO Blend WAR에서 fWAR 비중"),
        Row("Blend RA9 가중치", value.PitcherWar.BlendRa9Weight, "KBO Blend WAR에서 RA9-WAR 비중"),
        Row("대체선수 Runs/600PA*", 20.0, "타자 Site WAR v1"),
        Row("Runs Per Win*", 10.0, "타자 Site WAR v1"),
        Row("FG 포지션 기준 이닝", WarehousePositionAdjustment.FullSeasonInnings, "162경기 × 9이닝 (수비 포지션 보정치의 풀타임 기준)"),
        Row("FG 포지션 기준 PA(DH)", WarehousePositionAdjustment.DesignatedHitterFullSeasonPlateAppearances, "지명타자 보정치의 풀타임 기준 타석"),
        Row("포지션 보정 C*", WarehousePositionAdjustment.Rates["C"], "포수, 풀타임(1458이닝) 기준 run/season"),
        Row("포지션 보정 SS*", WarehousePositionAdjustment.Rates["SS"], "유격수, 풀타임(1458이닝) 기준 run/season"),
        Row("포지션 보정 2B*", WarehousePositionAdjustment.Rates["2B"], "2루수, 풀타임(1458이닝) 기준 run/season"),
        Row("포지션 보정 3B*", WarehousePositionAdjustment.Rates["3B"], "3루수, 풀타임(1458이닝) 기준 run/season"),
        Row("포지션 보정 CF*", WarehousePositionAdjustment.Rates["CF"], "중견수, 풀타임(1458이닝) 기준 run/season"),
        Row("포지션 보정 LF*", WarehousePositionAdjustment.Rates["LF"], "좌익수, 풀타임(1458이닝) 기준 run/season"),
        Row("포지션 보정 RF*", WarehousePositionAdjustment.Rates["RF"], "우익수, 풀타임(1458이닝) 기준 run/season"),
        Row("포지션 보정 1B*", WarehousePositionAdjustment.Rates["1B"], "1루수, 풀타임(1458이닝) 기준 run/season"),
        Row("포지션 보정 DH*", WarehousePositionAdjustment.Rates["DH"], "지명타자, 풀타임(600PA) 기준 run/season"),
    ];

    private static LeagueConstantGridRow Row(string metric, double value, string description) =>
        new() { Metric = metric, Value = value, Description = description };

    private static double FipComponentRate(ParkEnvironmentRow row)
    {
        var innings = row.InningsOuts / 3.0;
        return innings > 0
            ? (13.0 * row.HomeRuns + 3.0 * (row.Walks + row.HitBatters) -
               2.0 * (row.Strikeouts + row.InfieldFlies)) / innings
            : 0.0;
    }

    private static ParkEnvironmentRow Merge(IEnumerable<ParkEnvironmentRow> rows)
    {
        var result = new ParkEnvironmentRow();
        foreach (var row in rows)
        {
            result.InningsOuts += row.InningsOuts;
            result.HomeRuns += row.HomeRuns;
            result.Walks += row.Walks;
            result.HitBatters += row.HitBatters;
            result.Strikeouts += row.Strikeouts;
            result.InfieldFlies += row.InfieldFlies;
        }
        return result;
    }

    private static double Divide(double numerator, double denominator) => denominator > 0 ? numerator / denominator : 0.0;

    private sealed class LeagueBattingTotals
    {
        public int GameCount { get; init; }
        public int PlateAppearances { get; init; }
        public int AtBats { get; init; }
        public int Hits { get; init; }
        public int Singles { get; init; }
        public int Doubles { get; init; }
        public int Triples { get; init; }
        public int HomeRuns { get; init; }
        public int Walks { get; init; }
        public int IntentionalWalks { get; init; }
        public int HitByPitch { get; init; }
        public int Strikeouts { get; init; }
        public int SacrificeFlies { get; init; }
        public int Outs { get; init; }
        public int Runs { get; init; }
        public int FlyBalls { get; init; }
    }

    private sealed class LeaguePitchingTotals
    {
        public int InningsOuts { get; init; }
        public int EarnedRuns { get; init; }
        public int RunsAllowed { get; init; }
        public int HomeRuns { get; init; }
        public int Walks { get; init; }
        public int HitBatters { get; init; }
        public int Strikeouts { get; init; }
        public int InfieldFlies { get; init; }
    }

    private sealed class ParkEnvironmentRow
    {
        public string Stadium { get; init; } = string.Empty;
        public string TeamCode { get; init; } = string.Empty;
        public int? FirstSeason { get; init; }
        public int? LastSeason { get; init; }
        public int Games { get; init; }
        public int InningsOuts { get; set; }
        public int HomeRuns { get; set; }
        public int Walks { get; set; }
        public int HitBatters { get; set; }
        public int Strikeouts { get; set; }
        public int InfieldFlies { get; set; }
    }
}
