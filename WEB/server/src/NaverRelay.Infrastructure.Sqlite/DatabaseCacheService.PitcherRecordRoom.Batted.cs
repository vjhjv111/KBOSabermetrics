using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    internal async Task<IReadOnlyList<PitcherBattedBallRecordRow>> QueryPitcherBattedBallAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var pcodeExpression = "COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode)";
        var nameExpression = "COALESCE(NULLIF(pa.FinalPitcherName,''),pa.PitcherName)";
        var identity = GetPitcherRecordRoomIdentity(
            query.Grouping, pcodeExpression, nameExpression, "pa.FieldingTeamCode", "g");
        var line = await QueryPitcherLineSummaryAsync(query, cancellationToken).ConfigureAwait(false);
        var rows = new List<PitcherBattedBallRecordRow>();

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.SelectColumns},
                   SUM(CASE WHEN pa.BattedBallType<>$unknownBall OR pa.ResultType=$homeRun THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType=$groundBall THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType=$popup OR (pa.BattedBallType=$flyBall AND pa.FieldDirection BETWEEN $pitcher AND $shortstop) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType=$flyBall AND pa.FieldDirection BETWEEN $left AND $right THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType=$lineDrive THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.CountsAsAtBat=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=$homeRun THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=$strikeout THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=$sacFly THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.IsHit=1 AND pa.FieldDirection BETWEEN $pitcher AND $shortstop THEN 1 ELSE 0 END)
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            {identity.JoinClause}
            WHERE pa.IsOfficial=1
              AND COALESCE({pcodeExpression},'')<>''
              AND ($resultTeam='' OR pa.FieldingTeamCode=$resultTeam)
              {situation.Sql}
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        command.Parameters.AddWithValue("$unknownBall", (int)BattedBallType.Unknown);
        command.Parameters.AddWithValue("$groundBall", (int)BattedBallType.GroundBall);
        command.Parameters.AddWithValue("$flyBall", (int)BattedBallType.FlyBall);
        command.Parameters.AddWithValue("$lineDrive", (int)BattedBallType.LineDrive);
        command.Parameters.AddWithValue("$popup", (int)BattedBallType.PopUp);
        command.Parameters.AddWithValue("$homeRun", (int)BattingResultType.HomeRun);
        command.Parameters.AddWithValue("$strikeout", (int)BattingResultType.Strikeout);
        command.Parameters.AddWithValue("$sacFly", (int)BattingResultType.SacrificeFly);
        command.Parameters.AddWithValue("$left", (int)FieldDirection.Left);
        command.Parameters.AddWithValue("$right", (int)FieldDirection.Right);
        command.Parameters.AddWithValue("$pitcher", (int)FieldDirection.Pitcher);
        command.Parameters.AddWithValue("$shortstop", (int)FieldDirection.Shortstop);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var index = 0;
            var pcode = reader.GetString(index++);
            var name = reader.GetString(index++);
            var team = reader.GetString(index++);
            var bip = ReadInt32(reader, index++);
            var groundBalls = ReadInt32(reader, index++);
            var infieldFlies = ReadInt32(reader, index++);
            var outfieldFlies = ReadInt32(reader, index++);
            var lineDrives = ReadInt32(reader, index++);
            var atBats = ReadInt32(reader, index++);
            var hits = ReadInt32(reader, index++);
            var homeRuns = ReadInt32(reader, index++);
            var strikeouts = ReadInt32(reader, index++);
            var sacrificeFlies = ReadInt32(reader, index++);
            var infieldHits = ReadInt32(reader, index++);
            var flyBalls = infieldFlies + outfieldFlies;
            line.TryGetValue(PitcherRecordRoomKey(pcode, team), out var summary);

            rows.Add(new PitcherBattedBallRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                Games = summary?.Games ?? 0,
                InningsPitched = summary is null ? null : summary.InningsOuts / 3.0,
                BallsInPlay = bip,
                BABIP = RecordRoomDivide(hits - homeRuns, atBats - strikeouts - homeRuns + sacrificeFlies),
                GroundBallRate = RecordRoomDivide(groundBalls, bip),
                InfieldFlyRate = RecordRoomDivide(infieldFlies, bip),
                OutfieldFlyRate = RecordRoomDivide(outfieldFlies, bip),
                FlyBallRate = RecordRoomDivide(flyBalls, bip),
                LineDriveRate = RecordRoomDivide(lineDrives, bip),
                GroundBallToFlyBall = RecordRoomDivide(groundBalls, flyBalls),
                HomeRunPerFlyBall = RecordRoomDivide(homeRuns, flyBalls),
                InfieldHitRate = RecordRoomDivide(infieldHits, groundBalls),
            });
        }

        rows = rows.OrderByDescending(row => row.BallsInPlay)
            .ThenBy(row => row.BABIP ?? double.MaxValue)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    internal async Task<IReadOnlyList<PitcherDirectionRecordRow>> QueryPitcherDirectionAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var pcodeExpression = "COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode)";
        var nameExpression = "COALESCE(NULLIF(pa.FinalPitcherName,''),pa.PitcherName)";
        var identity = GetPitcherRecordRoomIdentity(
            query.Grouping, pcodeExpression, nameExpression, "pa.FieldingTeamCode", "g");
        var line = await QueryPitcherLineSummaryAsync(query, cancellationToken).ConfigureAwait(false);
        var rows = new List<PitcherDirectionRecordRow>();

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte},
            RankedStance AS (
                SELECT p.PlateAppearanceId,
                       UPPER(COALESCE(NULLIF(TRIM(p.BatterStance),''),'?')) AS Stance,
                       ROW_NUMBER() OVER (
                           PARTITION BY p.PlateAppearanceId
                           ORDER BY p.ActualPitchIndex DESC, p.SourceOptionIndex DESC, p.PitchEventId DESC
                       ) AS rn
                FROM Pitches p
                INNER JOIN FilteredGames fg ON fg.GameId=p.GameId
                WHERE p.PlateAppearanceId IS NOT NULL
            )
            SELECT {identity.SelectColumns},
                   SUM(CASE WHEN pa.FieldDirection BETWEEN $left AND $right THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=$left THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=$leftCenter THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=$center THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=$rightCenter THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=$right THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=$left AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=$leftCenter AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=$center AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=$rightCenter AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.FieldDirection=$right AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN (rs.Stance='R' AND pa.FieldDirection IN ($left,$leftCenter)) OR (rs.Stance='L' AND pa.FieldDirection IN ($rightCenter,$right)) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN (rs.Stance='R' AND pa.FieldDirection IN ($rightCenter,$right)) OR (rs.Stance='L' AND pa.FieldDirection IN ($left,$leftCenter)) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.IsHit=1 AND ((rs.Stance='R' AND pa.FieldDirection IN ($left,$leftCenter)) OR (rs.Stance='L' AND pa.FieldDirection IN ($rightCenter,$right))) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.IsHit=1 AND ((rs.Stance='R' AND pa.FieldDirection IN ($rightCenter,$right)) OR (rs.Stance='L' AND pa.FieldDirection IN ($left,$leftCenter))) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.CountsAsAtBat=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=$homeRun THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=$strikeout THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=$sacFly THEN 1 ELSE 0 END)
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            LEFT JOIN RankedStance rs ON rs.PlateAppearanceId=pa.PlateAppearanceId AND rs.rn=1
            {identity.JoinClause}
            WHERE pa.IsOfficial=1
              AND COALESCE({pcodeExpression},'')<>''
              AND ($resultTeam='' OR pa.FieldingTeamCode=$resultTeam)
              {situation.Sql}
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        command.Parameters.AddWithValue("$left", (int)FieldDirection.Left);
        command.Parameters.AddWithValue("$leftCenter", (int)FieldDirection.LeftCenter);
        command.Parameters.AddWithValue("$center", (int)FieldDirection.Center);
        command.Parameters.AddWithValue("$rightCenter", (int)FieldDirection.RightCenter);
        command.Parameters.AddWithValue("$right", (int)FieldDirection.Right);
        command.Parameters.AddWithValue("$homeRun", (int)BattingResultType.HomeRun);
        command.Parameters.AddWithValue("$strikeout", (int)BattingResultType.Strikeout);
        command.Parameters.AddWithValue("$sacFly", (int)BattingResultType.SacrificeFly);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var index = 0;
            var pcode = reader.GetString(index++);
            var name = reader.GetString(index++);
            var team = reader.GetString(index++);
            var total = ReadInt32(reader, index++);
            var left = ReadInt32(reader, index++);
            var leftCenter = ReadInt32(reader, index++);
            var center = ReadInt32(reader, index++);
            var rightCenter = ReadInt32(reader, index++);
            var right = ReadInt32(reader, index++);
            var leftHits = ReadInt32(reader, index++);
            var leftCenterHits = ReadInt32(reader, index++);
            var centerHits = ReadInt32(reader, index++);
            var rightCenterHits = ReadInt32(reader, index++);
            var rightHits = ReadInt32(reader, index++);
            var pull = ReadInt32(reader, index++);
            var opposite = ReadInt32(reader, index++);
            var pullHits = ReadInt32(reader, index++);
            var oppositeHits = ReadInt32(reader, index++);
            var atBats = ReadInt32(reader, index++);
            var hits = ReadInt32(reader, index++);
            var homeRuns = ReadInt32(reader, index++);
            var strikeouts = ReadInt32(reader, index++);
            var sacrificeFlies = ReadInt32(reader, index++);
            line.TryGetValue(PitcherRecordRoomKey(pcode, team), out var summary);

            rows.Add(new PitcherDirectionRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                Games = summary?.Games ?? 0,
                InningsPitched = summary is null ? null : summary.InningsOuts / 3.0,
                LeftRate = RecordRoomDivide(left, total),
                LeftCenterRate = RecordRoomDivide(leftCenter, total),
                CenterRate = RecordRoomDivide(center, total),
                RightCenterRate = RecordRoomDivide(rightCenter, total),
                RightRate = RecordRoomDivide(right, total),
                PullRate = RecordRoomDivide(pull, pull + opposite),
                OppositeRate = RecordRoomDivide(opposite, pull + opposite),
                Left = left,
                LeftCenter = leftCenter,
                Center = center,
                RightCenter = rightCenter,
                Right = right,
                Pull = pull,
                Opposite = opposite,
                LeftHits = leftHits,
                LeftCenterHits = leftCenterHits,
                CenterHits = centerHits,
                RightCenterHits = rightCenterHits,
                RightHits = rightHits,
                PullHits = pullHits,
                OppositeHits = oppositeHits,
                LeftAverage = RecordRoomDivide(leftHits, left),
                LeftCenterAverage = RecordRoomDivide(leftCenterHits, leftCenter),
                CenterAverage = RecordRoomDivide(centerHits, center),
                RightCenterAverage = RecordRoomDivide(rightCenterHits, rightCenter),
                RightAverage = RecordRoomDivide(rightHits, right),
                PullAverage = RecordRoomDivide(pullHits, pull),
                OppositeAverage = RecordRoomDivide(oppositeHits, opposite),
                BABIP = RecordRoomDivide(hits - homeRuns, atBats - strikeouts - homeRuns + sacrificeFlies),
            });
        }

        rows = rows.OrderByDescending(row => row.Left + row.LeftCenter + row.Center + row.RightCenter + row.Right)
            .ThenBy(row => row.BABIP ?? double.MaxValue)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    internal async Task<IReadOnlyList<PitcherPitchProfileRecordRow>> QueryPitcherPitchProfileAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: false);
        var pitchSituation = BuildPitchSituationSql(query, "p");
        var teamExpression = "COALESCE(pa.FieldingTeamCode, CASE WHEN p.BattingSide=0 THEN g.HomeTeamCode ELSE g.AwayTeamCode END)";
        var identity = GetPitcherRecordRoomIdentity(
            query.Grouping, "p.PitcherPcode", "p.PitcherName", teamExpression, "g");
        var rows = new List<PitcherPitchProfileRecordRow>();

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte},
            RankedPitches AS (
                SELECT p.*,
                       ROW_NUMBER() OVER (
                           PARTITION BY p.PlateAppearanceId
                           ORDER BY p.ActualPitchIndex DESC, p.SourceOptionIndex DESC, p.PitchEventId DESC
                       ) AS reverse_rank
                FROM Pitches p
                INNER JOIN FilteredGames fg ON fg.GameId=p.GameId
            )
            SELECT {identity.SelectColumns},
                   COUNT(DISTINCT p.GameId),
                   COUNT(*),
                   SUM(CASE WHEN p.PitchResult IN ($foul,$inPlay,$whiff,$called,$buntFoul,$buntWhiff) THEN 1 ELSE 0 END),
                   SUM(p.IsCalledStrike),
                   SUM(p.IsWhiff),
                   SUM(CASE WHEN p.IsWhiff=1 OR p.IsCalledStrike=1 THEN 1 ELSE 0 END),
                   SUM(p.IsSwing),
                   SUM(p.IsContact),
                   SUM(CASE WHEN p.ActualPitchIndex=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.ActualPitchIndex=1 AND p.PitchResult IN ($foul,$inPlay,$whiff,$called,$buntFoul,$buntWhiff) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.ActualPitchIndex=1 AND p.IsWhiff=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.StrikesBefore=2 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.reverse_rank=1 AND pa.ResultType=$strikeout AND (p.IsWhiff=1 OR p.IsCalledStrike=1) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.IsInNominalStrikeZone=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.IsInNominalStrikeZone=1 AND p.IsSwing=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.IsInNominalStrikeZone=1 AND p.IsContact=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.IsInNominalStrikeZone=0 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.IsInNominalStrikeZone=0 AND p.IsSwing=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.IsInNominalStrikeZone=0 AND p.IsContact=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.HasPtsTracking=1
                                  AND ABS(COALESCE(p.CalculatedCrossPlateX,p.CrossPlateX))<=0.25
                                  AND p.CalculatedCrossPlateZ IS NOT NULL
                                  AND p.TopStrikeZone IS NOT NULL
                                  AND p.BottomStrikeZone IS NOT NULL
                                  AND p.CalculatedCrossPlateZ BETWEEN p.BottomStrikeZone + (p.TopStrikeZone-p.BottomStrikeZone)*0.30
                                                                AND p.BottomStrikeZone + (p.TopStrikeZone-p.BottomStrikeZone)*0.70
                            THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.HasPtsTracking=1
                                  AND ABS(COALESCE(p.CalculatedCrossPlateX,p.CrossPlateX))<=0.25
                                  AND p.CalculatedCrossPlateZ IS NOT NULL
                                  AND p.TopStrikeZone IS NOT NULL
                                  AND p.BottomStrikeZone IS NOT NULL
                                  AND p.CalculatedCrossPlateZ BETWEEN p.BottomStrikeZone + (p.TopStrikeZone-p.BottomStrikeZone)*0.30
                                                                AND p.BottomStrikeZone + (p.TopStrikeZone-p.BottomStrikeZone)*0.70
                                  AND p.IsSwing=1
                            THEN 1 ELSE 0 END),
                   COUNT(DISTINCT CASE WHEN pa.ResultType=$strikeout AND p.reverse_rank=1 AND p.IsCalledStrike=1 THEN pa.PlateAppearanceId END),
                   COUNT(DISTINCT CASE WHEN pa.ResultType=$strikeout AND p.reverse_rank=1 AND p.IsWhiff=1 THEN pa.PlateAppearanceId END)
            FROM RankedPitches p
            INNER JOIN FilteredGames g ON g.GameId=p.GameId
            LEFT JOIN PlateAppearances pa ON pa.PlateAppearanceId=p.PlateAppearanceId
            {identity.JoinClause}
            WHERE COALESCE(p.PitcherPcode,'')<>''
              AND ($resultTeam='' OR {teamExpression}=$resultTeam)
              {situation.Sql}
              {pitchSituation.Sql}
            GROUP BY {identity.GroupBy};
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation, pitchSituation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        command.Parameters.AddWithValue("$foul", (int)PitchResultType.Foul);
        command.Parameters.AddWithValue("$inPlay", (int)PitchResultType.InPlay);
        command.Parameters.AddWithValue("$whiff", (int)PitchResultType.SwingingStrike);
        command.Parameters.AddWithValue("$called", (int)PitchResultType.CalledStrike);
        command.Parameters.AddWithValue("$buntFoul", (int)PitchResultType.BuntFoul);
        command.Parameters.AddWithValue("$buntWhiff", (int)PitchResultType.BuntSwingingStrike);
        command.Parameters.AddWithValue("$strikeout", (int)BattingResultType.Strikeout);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var index = 0;
            var pcode = reader.GetString(index++);
            var name = reader.GetString(index++);
            var team = reader.GetString(index++);
            var games = ReadInt32(reader, index++);
            var pitches = ReadInt32(reader, index++);
            var strikes = ReadInt32(reader, index++);
            var calledStrikes = ReadInt32(reader, index++);
            var whiffs = ReadInt32(reader, index++);
            var csw = ReadInt32(reader, index++);
            var swings = ReadInt32(reader, index++);
            var contacts = ReadInt32(reader, index++);
            var firstPitches = ReadInt32(reader, index++);
            var firstPitchStrikes = ReadInt32(reader, index++);
            var firstPitchWhiffs = ReadInt32(reader, index++);
            var twoStrikePitches = ReadInt32(reader, index++);
            var putAways = ReadInt32(reader, index++);
            var zonePitches = ReadInt32(reader, index++);
            var zoneSwings = ReadInt32(reader, index++);
            var zoneContacts = ReadInt32(reader, index++);
            var outZonePitches = ReadInt32(reader, index++);
            var outZoneSwings = ReadInt32(reader, index++);
            var outZoneContacts = ReadInt32(reader, index++);
            var heartPitches = ReadInt32(reader, index++);
            var heartSwings = ReadInt32(reader, index++);
            var calledStrikeouts = ReadInt32(reader, index++);
            var swingingStrikeouts = ReadInt32(reader, index++);
            rows.Add(new PitcherPitchProfileRecordRow
            {
                Pcode = pcode,
                Name = name,
                TeamCode = team,
                Games = games,
                Pitches = pitches,
                StrikeRate = RecordRoomDivide(strikes, pitches),
                CalledStrikeRate = RecordRoomDivide(calledStrikes, pitches),
                WhiffPerPitch = RecordRoomDivide(whiffs, pitches),
                CswRate = RecordRoomDivide(csw, pitches),
                SwingRate = RecordRoomDivide(swings, pitches),
                ContactRate = RecordRoomDivide(contacts, swings),
                WhiffRate = RecordRoomDivide(whiffs, swings),
                FirstPitchStrikeRate = RecordRoomDivide(firstPitchStrikes, firstPitches),
                FirstPitchWhiffRate = RecordRoomDivide(firstPitchWhiffs, firstPitches),
                PutAwayRate = RecordRoomDivide(putAways, twoStrikePitches),
                ZonePitchRate = RecordRoomDivide(zonePitches, zonePitches + outZonePitches),
                ZoneSwingRate = RecordRoomDivide(zoneSwings, zonePitches),
                ZoneContactRate = RecordRoomDivide(zoneContacts, zoneSwings),
                OutZonePitchRate = RecordRoomDivide(outZonePitches, zonePitches + outZonePitches),
                ChaseRate = RecordRoomDivide(outZoneSwings, outZonePitches),
                OutZoneContactRate = RecordRoomDivide(outZoneContacts, outZoneSwings),
                HeartPitchRate = RecordRoomDivide(heartPitches, pitches),
                HeartSwingRate = RecordRoomDivide(heartSwings, heartPitches),
                CalledStrikeouts = calledStrikeouts,
                SwingingStrikeouts = swingingStrikeouts,
                CalledStrikeoutRate = RecordRoomDivide(calledStrikeouts, calledStrikeouts + swingingStrikeouts),
            });
        }

        rows = rows.OrderByDescending(row => row.Pitches)
            .ThenByDescending(row => row.CswRate ?? double.MinValue)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    internal async Task<IReadOnlyList<PitcherPitchTypeRecordRow>> QueryPitcherPitchTypesAsync(
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: false);
        var pitchSituation = BuildPitchSituationSql(query, "rp");
        var teamExpression = "COALESCE(pa.FieldingTeamCode, CASE WHEN rp.BattingSide=0 THEN g.HomeTeamCode ELSE g.AwayTeamCode END)";
        var identity = GetPitcherRecordRoomIdentity(
            query.Grouping, "rp.PitcherPcode", "rp.PitcherName", teamExpression, "g");
        var bucket = PitchBucketSql("rp.PitchType");
        var accumulators = new Dictionary<string, PitcherPitchTypeAccumulator>(StringComparer.Ordinal);
        var line = await QueryPitcherLineSummaryAsync(query, cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte},
            RankedPitches AS (
                SELECT p.*,
                       ROW_NUMBER() OVER (
                           PARTITION BY p.PlateAppearanceId
                           ORDER BY p.ActualPitchIndex DESC, p.SourceOptionIndex DESC, p.PitchEventId DESC
                       ) AS reverse_rank
                FROM Pitches p
                INNER JOIN FilteredGames fg ON fg.GameId=p.GameId
            )
            SELECT {identity.SelectColumns},
                   {bucket} AS PitchBucket,
                   COUNT(*),
                   SUM(CASE WHEN rp.SpeedKmh IS NOT NULL THEN rp.SpeedKmh ELSE 0 END),
                   SUM(CASE WHEN rp.SpeedKmh IS NOT NULL THEN 1 ELSE 0 END),
                   SUM(CASE WHEN rp.reverse_rank=1 THEN -COALESCE(pa.WpaByPlate,0) ELSE 0 END),
                   SUM(CASE WHEN rp.reverse_rank=1 AND pa.CountsAsAtBat=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN rp.reverse_rank=1 AND pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN rp.reverse_rank=1 THEN pa.TotalBases ELSE 0 END)
            FROM RankedPitches rp
            INNER JOIN FilteredGames g ON g.GameId=rp.GameId
            LEFT JOIN PlateAppearances pa ON pa.PlateAppearanceId=rp.PlateAppearanceId
            {identity.JoinClause}
            WHERE COALESCE(rp.PitcherPcode,'')<>''
              AND ($resultTeam='' OR {teamExpression}=$resultTeam)
              {situation.Sql}
              {pitchSituation.Sql}
            GROUP BY {identity.GroupBy}, PitchBucket;
            """;
        AddParameters(command, filter.Parameters);
        AddSituationParameters(command, situation, pitchSituation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var index = 0;
            var pcode = reader.GetString(index++);
            var name = reader.GetString(index++);
            var team = reader.GetString(index++);
            var pitchBucket = reader.GetString(index++);
            var count = ReadInt32(reader, index++);
            var speedSum = ReadDouble(reader, index++);
            var speedCount = ReadInt32(reader, index++);
            var value = ReadDouble(reader, index++);
            var atBats = ReadInt32(reader, index++);
            var hits = ReadInt32(reader, index++);
            var totalBases = ReadInt32(reader, index++);
            var key = PitcherRecordRoomKey(pcode, team);
            if (!accumulators.TryGetValue(key, out var accumulator))
            {
                accumulator = new PitcherPitchTypeAccumulator(pcode, name, team);
                accumulators[key] = accumulator;
            }
            accumulator.Buckets[pitchBucket] = new PitcherPitchBucketMetric(count, speedSum, speedCount, value, atBats, hits, totalBases);
        }

        var rows = new List<PitcherPitchTypeRecordRow>();
        foreach (var (key, accumulator) in accumulators)
        {
            line.TryGetValue(key, out var summary);
            var totalPitches = accumulator.Buckets.Values.Sum(item => item.Count);
            var twoSeam = accumulator.Get("투심");
            var fourSeam = accumulator.Get("포심");
            var cutter = accumulator.Get("커터");
            var curve = accumulator.Get("커브");
            var slider = accumulator.Get("슬라이더");
            var changeup = accumulator.Get("체인지업");
            var sinker = accumulator.Get("싱커");
            var fork = accumulator.Get("포크");
            var knuckle = accumulator.Get("너클");
            var other = accumulator.Get("기타");
            rows.Add(new PitcherPitchTypeRecordRow
            {
                Pcode = accumulator.Pcode,
                Name = accumulator.Name,
                TeamCode = accumulator.TeamCode,
                Games = summary?.Games ?? 0,
                InningsPitched = summary is null ? null : summary.InningsOuts / 3.0,
                TwoSeamValue = twoSeam.Value,
                FourSeamValue = fourSeam.Value,
                CutterValue = cutter.Value,
                CurveValue = curve.Value,
                SliderValue = slider.Value,
                ChangeupValue = changeup.Value,
                SinkerValue = sinker.Value,
                ForkballValue = fork.Value,
                KnuckleballValue = knuckle.Value,
                OtherValue = other.Value,
                TwoSeamValuePer100 = twoSeam.ValuePer100,
                FourSeamValuePer100 = fourSeam.ValuePer100,
                CutterValuePer100 = cutter.ValuePer100,
                CurveValuePer100 = curve.ValuePer100,
                SliderValuePer100 = slider.ValuePer100,
                ChangeupValuePer100 = changeup.ValuePer100,
                SinkerValuePer100 = sinker.ValuePer100,
                ForkballValuePer100 = fork.ValuePer100,
                KnuckleballValuePer100 = knuckle.ValuePer100,
                OtherValuePer100 = other.ValuePer100,
                TwoSeamSpeed = twoSeam.AverageSpeed,
                FourSeamSpeed = fourSeam.AverageSpeed,
                CutterSpeed = cutter.AverageSpeed,
                CurveSpeed = curve.AverageSpeed,
                SliderSpeed = slider.AverageSpeed,
                ChangeupSpeed = changeup.AverageSpeed,
                SinkerSpeed = sinker.AverageSpeed,
                ForkballSpeed = fork.AverageSpeed,
                KnuckleballSpeed = knuckle.AverageSpeed,
                OtherSpeed = other.AverageSpeed,
                TwoSeamUsage = RecordRoomDivide(twoSeam.Count, totalPitches),
                FourSeamUsage = RecordRoomDivide(fourSeam.Count, totalPitches),
                CutterUsage = RecordRoomDivide(cutter.Count, totalPitches),
                CurveUsage = RecordRoomDivide(curve.Count, totalPitches),
                SliderUsage = RecordRoomDivide(slider.Count, totalPitches),
                ChangeupUsage = RecordRoomDivide(changeup.Count, totalPitches),
                SinkerUsage = RecordRoomDivide(sinker.Count, totalPitches),
                ForkballUsage = RecordRoomDivide(fork.Count, totalPitches),
                KnuckleballUsage = RecordRoomDivide(knuckle.Count, totalPitches),
                OtherUsage = RecordRoomDivide(other.Count, totalPitches),
                TwoSeamCount = twoSeam.Count,
                FourSeamCount = fourSeam.Count,
                CutterCount = cutter.Count,
                CurveCount = curve.Count,
                SliderCount = slider.Count,
                ChangeupCount = changeup.Count,
                SinkerCount = sinker.Count,
                ForkballCount = fork.Count,
                KnuckleballCount = knuckle.Count,
                OtherCount = other.Count,
                TwoSeamOpponentAverage = twoSeam.OpponentAverage,
                FourSeamOpponentAverage = fourSeam.OpponentAverage,
                CutterOpponentAverage = cutter.OpponentAverage,
                CurveOpponentAverage = curve.OpponentAverage,
                SliderOpponentAverage = slider.OpponentAverage,
                ChangeupOpponentAverage = changeup.OpponentAverage,
                SinkerOpponentAverage = sinker.OpponentAverage,
                ForkballOpponentAverage = fork.OpponentAverage,
                KnuckleballOpponentAverage = knuckle.OpponentAverage,
                OtherOpponentAverage = other.OpponentAverage,
                TwoSeamOpponentSlugging = twoSeam.OpponentSlugging,
                FourSeamOpponentSlugging = fourSeam.OpponentSlugging,
                CutterOpponentSlugging = cutter.OpponentSlugging,
                CurveOpponentSlugging = curve.OpponentSlugging,
                SliderOpponentSlugging = slider.OpponentSlugging,
                ChangeupOpponentSlugging = changeup.OpponentSlugging,
                SinkerOpponentSlugging = sinker.OpponentSlugging,
                ForkballOpponentSlugging = fork.OpponentSlugging,
                KnuckleballOpponentSlugging = knuckle.OpponentSlugging,
                OtherOpponentSlugging = other.OpponentSlugging,
            });
        }

        rows = rows.OrderByDescending(row => row.TwoSeamCount + row.FourSeamCount + row.CutterCount + row.CurveCount +
                                                    row.SliderCount + row.ChangeupCount + row.SinkerCount + row.ForkballCount +
                                                    row.KnuckleballCount + row.OtherCount)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ApplyRecordRoomRanks(rows);
        return rows;
    }

    private sealed class PitcherPitchTypeAccumulator
    {
        public PitcherPitchTypeAccumulator(string pcode, string name, string teamCode)
        {
            Pcode = pcode;
            Name = name;
            TeamCode = teamCode;
        }

        public string Pcode { get; }
        public string Name { get; }
        public string TeamCode { get; }
        public Dictionary<string, PitcherPitchBucketMetric> Buckets { get; } = new(StringComparer.Ordinal);
        public PitcherPitchBucketMetric Get(string bucket) => Buckets.TryGetValue(bucket, out var value) ? value : PitcherPitchBucketMetric.Empty;
    }

    private sealed record PitcherPitchBucketMetric(
        int Count,
        double SpeedSum,
        int SpeedCount,
        double Value,
        int AtBats,
        int Hits,
        int TotalBases)
    {
        public static PitcherPitchBucketMetric Empty { get; } = new(0, 0.0, 0, 0.0, 0, 0, 0);
        public double? AverageSpeed => RecordRoomDivide(SpeedSum, SpeedCount);
        public double? ValuePer100 => RecordRoomDivide(Value * 100.0, Count);
        public double? OpponentAverage => RecordRoomDivide(Hits, AtBats);
        public double? OpponentSlugging => RecordRoomDivide(TotalBases, AtBats);
    }
}
