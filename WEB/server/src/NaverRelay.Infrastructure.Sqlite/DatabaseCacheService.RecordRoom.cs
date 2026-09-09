using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    private const double RecordRoomWbb = 0.69;
    private const double RecordRoomWhbp = 0.72;
    private const double RecordRoomW1b = 0.88;
    private const double RecordRoomW2b = 1.247;
    private const double RecordRoomW3b = 1.578;
    private const double RecordRoomWhr = 2.031;

    internal async Task<IReadOnlyList<BatterClutchRecordRow>> QueryBatterClutchAsync(
        GameQuery query,
        LeagueReference league,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var identity = GetBatterRecordRoomIdentity(query.Grouping);
        const string lateCondition = "pa.Inning>=7 AND ABS(CASE WHEN pa.BattingSide=1 THEN COALESCE(pa.BeforeHomeScore,0)-COALESCE(pa.BeforeAwayScore,0) ELSE COALESCE(pa.BeforeAwayScore,0)-COALESCE(pa.BeforeHomeScore,0) END)<=3";

        var rows = new List<BatterClutchRecordRow>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   COUNT(*),
                   SUM(CASE WHEN {lateCondition} THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {lateCondition} AND pa.CountsAsAtBat=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {lateCondition} AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {lateCondition} THEN pa.TotalBases ELSE 0 END),
                   SUM(CASE WHEN {lateCondition} AND pa.ResultType IN (7,8) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {lateCondition} AND pa.ResultType=9 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {lateCondition} AND pa.ResultType=17 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN COALESCE(pa.WpaByPlate,0)>0 THEN pa.WpaByPlate ELSE 0 END),
                   SUM(CASE WHEN COALESCE(pa.WpaByPlate,0)<0 THEN pa.WpaByPlate ELSE 0 END),
                   SUM(COALESCE(pa.WpaByPlate,0)),
                   SUM(ABS(COALESCE(pa.WpaByPlate,0))),
                   SUM(CASE WHEN pa.WpaByPlate IS NOT NULL THEN 1 ELSE 0 END)
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            {identity.JoinClause}
            WHERE pa.IsOfficial=1
              AND ($resultTeam='' OR pa.BattingTeamCode=$resultTeam)
              AND COALESCE(pa.BatterPcode,'')<>''
              {situation.Sql}
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            var pcode = reader.GetString(i++);
            var name = reader.GetString(i++);
            var team = reader.GetString(i++);
            var pa = ReadInt32(reader, i++);
            var latePa = ReadInt32(reader, i++);
            var lateAb = ReadInt32(reader, i++);
            var lateHits = ReadInt32(reader, i++);
            var lateTb = ReadInt32(reader, i++);
            var lateWalks = ReadInt32(reader, i++);
            var lateHbp = ReadInt32(reader, i++);
            var lateSf = ReadInt32(reader, i++);
            var wpaPlus = ReadDouble(reader, i++);
            var wpaMinus = ReadDouble(reader, i++);
            var wpa = ReadDouble(reader, i++);
            var absoluteWpa = ReadDouble(reader, i++);
            var wpaCount = ReadInt32(reader, i++);

            var avg = DivideRecordRoom(lateHits, lateAb);
            var obp = DivideRecordRoom(lateHits + lateWalks + lateHbp, lateAb + lateWalks + lateHbp + lateSf);
            var slg = DivideRecordRoom(lateTb, lateAb);
            var pli = wpaCount > 0 && league.AverageAbsoluteWpa > 0
                ? absoluteWpa / wpaCount / league.AverageAbsoluteWpa
                : (double?)null;
            rows.Add(new BatterClutchRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                PA = pa,
                LateClosePA = latePa,
                AVG = avg,
                OBP = obp,
                SLG = slg,
                OPS = obp.HasValue && slg.HasValue ? obp.Value + slg.Value : null,
                AverageLeverageIndex = pli,
                PositiveWpa = wpaPlus,
                NegativeWpa = wpaMinus,
                Wpa = wpa,
                WpaPerLi = pli.HasValue && pli.Value > 0 ? wpa / pli.Value : null,
            });
        }

        rows = rows.OrderByDescending(row => row.Wpa ?? double.MinValue)
            .ThenByDescending(row => row.PA)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    internal async Task<IReadOnlyList<BatterBattedBallRecordRow>> QueryBatterBattedBallAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var identity = GetBatterRecordRoomIdentity(query.Grouping);
        var rows = new List<BatterBattedBallRecordRow>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   COUNT(*),
                   SUM(CASE WHEN pa.BattedBallType<>0 OR pa.ResultType=6 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType=2 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType=3 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType=4 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType=5 OR pa.ResultType IN (3,16,18) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=20 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=3 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.CountsAsAtBat=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=6 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=10 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=17 THEN 1 ELSE 0 END)
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            {identity.JoinClause}
            WHERE pa.IsOfficial=1
              AND ($resultTeam='' OR pa.BattingTeamCode=$resultTeam)
              AND COALESCE(pa.BatterPcode,'')<>''
              {situation.Sql}
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            var pcode = reader.GetString(i++);
            var name = reader.GetString(i++);
            var team = reader.GetString(i++);
            var pa = ReadInt32(reader, i++);
            var bip = ReadInt32(reader, i++);
            var gb = ReadInt32(reader, i++);
            var fb = ReadInt32(reader, i++);
            var ld = ReadInt32(reader, i++);
            var popup = ReadInt32(reader, i++);
            var bunts = ReadInt32(reader, i++);
            var roe = ReadInt32(reader, i++);
            var buntHits = ReadInt32(reader, i++);
            var ab = ReadInt32(reader, i++);
            var hits = ReadInt32(reader, i++);
            var hr = ReadInt32(reader, i++);
            var so = ReadInt32(reader, i++);
            var sf = ReadInt32(reader, i++);
            var airBalls = fb + popup;

            rows.Add(new BatterBattedBallRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                PA = pa,
                BallsInPlay = bip,
                BABIP = DivideRecordRoom(hits - hr, ab - so - hr + sf),
                GroundBallRate = DivideRecordRoom(gb, bip),
                FlyBallRate = DivideRecordRoom(fb, bip),
                LineDriveRate = DivideRecordRoom(ld, bip),
                InfieldFlyRate = DivideRecordRoom(popup, airBalls),
                GroundBallToFlyBall = DivideRecordRoom(gb, airBalls),
                HomeRunPerFlyBall = DivideRecordRoom(hr, airBalls),
                ReachedOnError = roe,
                BuntAttempts = bunts,
                BuntHits = buntHits,
                BuntAverage = DivideRecordRoom(buntHits, bunts),
            });
        }

        rows = rows.OrderByDescending(row => row.BallsInPlay)
            .ThenByDescending(row => row.BABIP ?? double.MinValue)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    internal async Task<IReadOnlyList<BatterDirectionRecordRow>> QueryBatterDirectionAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var identity = GetBatterRecordRoomIdentity(query.Grouping);
        var rows = new List<BatterDirectionRecordRow>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   SUM(CASE WHEN pa.FieldDirection<>0 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=2 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=3 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=4 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=5 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection BETWEEN 6 AND 11 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=1 AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=2 AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=3 AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=4 AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=5 AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.CountsAsAtBat=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=6 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=10 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=17 THEN 1 ELSE 0 END)
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            {identity.JoinClause}
            WHERE pa.IsOfficial=1
              AND ($resultTeam='' OR pa.BattingTeamCode=$resultTeam)
              AND COALESCE(pa.BatterPcode,'')<>''
              {situation.Sql}
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            var pcode = reader.GetString(i++);
            var name = reader.GetString(i++);
            var team = reader.GetString(i++);
            var total = ReadInt32(reader, i++);
            var left = ReadInt32(reader, i++);
            var leftCenter = ReadInt32(reader, i++);
            var center = ReadInt32(reader, i++);
            var rightCenter = ReadInt32(reader, i++);
            var right = ReadInt32(reader, i++);
            var infield = ReadInt32(reader, i++);
            var leftHits = ReadInt32(reader, i++);
            var leftCenterHits = ReadInt32(reader, i++);
            var centerHits = ReadInt32(reader, i++);
            var rightCenterHits = ReadInt32(reader, i++);
            var rightHits = ReadInt32(reader, i++);
            var ab = ReadInt32(reader, i++);
            var hits = ReadInt32(reader, i++);
            var hr = ReadInt32(reader, i++);
            var so = ReadInt32(reader, i++);
            var sf = ReadInt32(reader, i++);

            rows.Add(new BatterDirectionRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                DirectionalBallsInPlay = total,
                LeftRate = DivideRecordRoom(left, total),
                LeftCenterRate = DivideRecordRoom(leftCenter, total),
                CenterRate = DivideRecordRoom(center, total),
                RightCenterRate = DivideRecordRoom(rightCenter, total),
                RightRate = DivideRecordRoom(right, total),
                InfieldRate = DivideRecordRoom(infield, total),
                LeftHits = leftHits,
                LeftCenterHits = leftCenterHits,
                CenterHits = centerHits,
                RightCenterHits = rightCenterHits,
                RightHits = rightHits,
                BABIP = DivideRecordRoom(hits - hr, ab - so - hr + sf),
            });
        }

        rows = rows.OrderByDescending(row => row.DirectionalBallsInPlay)
            .ThenByDescending(row => row.BABIP ?? double.MinValue)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    internal async Task<IReadOnlyList<BatterPitchTypeRecordRow>> QueryBatterPitchTypesAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var identity = GetBatterRecordRoomIdentity(query.Grouping);
        var rows = new List<BatterPitchTypeRecordRow>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   COALESCE(NULLIF(TRIM(lp.PitchType),''),'투구 없음'),
                   COUNT(*),
                   SUM(CASE WHEN pa.CountsAsAtBat=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType IN (1,2,3) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=4 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=5 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=6 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType IN (7,8) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=9 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=10 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=17 THEN 1 ELSE 0 END),
                   SUM(pa.TotalBases)
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            LEFT JOIN Pitches lp ON lp.PitchEventId = (
                SELECT p2.PitchEventId
                FROM Pitches p2
                WHERE p2.PlateAppearanceId=pa.PlateAppearanceId
                ORDER BY p2.ActualPitchIndex DESC,
                         p2.SourceOptionIndex DESC,
                         p2.PitchEventId DESC
                LIMIT 1
            )
            {identity.JoinClause}
            WHERE pa.IsOfficial=1
              AND ($resultTeam='' OR pa.BattingTeamCode=$resultTeam)
              AND COALESCE(pa.BatterPcode,'')<>''
              {situation.Sql}
            GROUP BY {identity.GroupBy}, COALESCE(NULLIF(TRIM(lp.PitchType),''),'투구 없음');
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            var pcode = reader.GetString(i++);
            var name = reader.GetString(i++);
            var team = reader.GetString(i++);
            var pitchType = reader.GetString(i++);
            var pa = ReadInt32(reader, i++);
            var ab = ReadInt32(reader, i++);
            var hits = ReadInt32(reader, i++);
            var singles = ReadInt32(reader, i++);
            var doubles = ReadInt32(reader, i++);
            var triples = ReadInt32(reader, i++);
            var homeRuns = ReadInt32(reader, i++);
            var walks = ReadInt32(reader, i++);
            var hbp = ReadInt32(reader, i++);
            var strikeouts = ReadInt32(reader, i++);
            var sf = ReadInt32(reader, i++);
            var totalBases = ReadInt32(reader, i++);
            var avg = DivideRecordRoom(hits, ab);
            var obp = DivideRecordRoom(hits + walks + hbp, ab + walks + hbp + sf);
            var slg = DivideRecordRoom(totalBases, ab);
            var wobaDenominator = ab + walks + hbp + sf;
            var woba = DivideRecordRoom(
                RecordRoomWbb * walks + RecordRoomWhbp * hbp + RecordRoomW1b * singles +
                RecordRoomW2b * doubles + RecordRoomW3b * triples + RecordRoomWhr * homeRuns,
                wobaDenominator);

            rows.Add(new BatterPitchTypeRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                PitchType = pitchType,
                PA = pa,
                AB = ab,
                Hits = hits,
                Singles = singles,
                Doubles = doubles,
                Triples = triples,
                HomeRuns = homeRuns,
                Walks = walks,
                HitByPitch = hbp,
                Strikeouts = strikeouts,
                AVG = avg,
                OBP = obp,
                SLG = slg,
                OPS = obp.HasValue && slg.HasValue ? obp.Value + slg.Value : null,
                Woba = woba,
                SacrificeFlies = sf,
                TotalBasesRaw = totalBases,
            });
        }

        rows = rows.OrderByDescending(row => row.PA)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ThenBy(row => row.PitchType, StringComparer.CurrentCulture)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    private static RecordRoomIdentitySql GetBatterRecordRoomIdentity(AnalyticsGrouping grouping) => grouping switch
    {
        AnalyticsGrouping.PlayerCareer => new RecordRoomIdentitySql(
            "pa.BatterPcode, MAX(COALESCE(pa.BatterName,'')), CASE WHEN $resultTeam<>'' THEN $resultTeam ELSE COALESCE(MAX(profile.LatestTeam),'') END",
            "LEFT JOIN Players profile ON profile.Pcode=pa.BatterPcode",
            "pa.BatterPcode"),
        AnalyticsGrouping.Team => new RecordRoomIdentitySql(
            "pa.BattingTeamCode, MAX(CASE WHEN g.HomeTeamCode=pa.BattingTeamCode THEN COALESCE(NULLIF(g.HomeTeamName,''),pa.BattingTeamCode) ELSE COALESCE(NULLIF(g.AwayTeamName,''),pa.BattingTeamCode) END), pa.BattingTeamCode",
            string.Empty,
            "pa.BattingTeamCode"),
        _ => new RecordRoomIdentitySql(
            "pa.BatterPcode, MAX(COALESCE(pa.BatterName,'')), pa.BattingTeamCode",
            string.Empty,
            "pa.BatterPcode, pa.BattingTeamCode"),
    };

    private static double? DivideRecordRoom(double numerator, double denominator) =>
        Math.Abs(denominator) < 0.0000001 ? null : numerator / denominator;

    private static void ApplyRecordRoomRanks<T>(IReadOnlyList<T> rows)
    {
        var property = typeof(T).GetProperty("Rank");
        if (property is null || !property.CanWrite) return;
        for (var index = 0; index < rows.Count; index++)
            property.SetValue(rows[index], index + 1);
    }

    private sealed record RecordRoomIdentitySql(string SelectColumns, string JoinClause, string GroupBy);
}
