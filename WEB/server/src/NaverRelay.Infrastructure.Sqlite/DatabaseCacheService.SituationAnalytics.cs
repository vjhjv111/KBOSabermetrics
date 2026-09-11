using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    private sealed class SituationPitchStats
    {
        public int Pitches { get; set; }
        public int Swings { get; set; }
        public int Contacts { get; set; }
        public int Whiffs { get; set; }
        public int CalledStrikes { get; set; }
        public int Csw { get; set; }
        public int InZone { get; set; }
        public int OutZone { get; set; }
        public int ZoneSwings { get; set; }
        public int ChaseSwings { get; set; }
        public int ZoneContacts { get; set; }
        public int OutZoneContacts { get; set; }
        public int FirstPitches { get; set; }
        public int FirstPitchSwings { get; set; }
        public double SpeedSum { get; set; }
        public int SpeedCount { get; set; }
    }

    private async Task<WarehouseAnalyticsData> GetSituationAggregateDataAsync(
        GameQuery query,
        IProgress<DatabaseLoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new DatabaseLoadProgress(1, 3, "상황 조건 타석 재집계 중"));
        var batters = await ReadSituationBatterAggregatesAsync(connection, filter, query, cancellationToken).ConfigureAwait(false);
        progress?.Report(new DatabaseLoadProgress(2, 3, "상황 조건 상대 타자 재집계 중"));
        var pitchers = await ReadSituationPitcherAggregatesAsync(connection, filter, query, cancellationToken).ConfigureAwait(false);
        progress?.Report(new DatabaseLoadProgress(3, 3, "팀 경기 수 조회 중"));
        var teamGames = await ReadTeamGamesAsync(connection, filter, cancellationToken).ConfigureAwait(false);
        return new WarehouseAnalyticsData { Batters = batters, Pitchers = pitchers, TeamGames = teamGames };
    }

    private static async Task<List<BatterAggregateRecord>> ReadSituationBatterAggregatesAsync(
        SqliteConnection connection,
        SqlFilter gameFilter,
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var pitchSituation = BuildPitchSituationSql(query, "p");
        var (selectIdentity, joinIdentity, groupIdentity) = query.Grouping switch
        {
            AnalyticsGrouping.PlayerCareer => (
                "pa.BatterPcode, MAX(COALESCE(pa.BatterName,'')), CASE WHEN $resultTeam<>'' THEN $resultTeam ELSE COALESCE(MAX(pl.LatestTeam),'') END",
                "LEFT JOIN Players pl ON pl.Pcode=pa.BatterPcode",
                "pa.BatterPcode"),
            AnalyticsGrouping.Team => (
                "pa.BattingTeamCode, MAX(CASE WHEN g.HomeTeamCode=pa.BattingTeamCode THEN COALESCE(NULLIF(g.HomeTeamName,''),pa.BattingTeamCode) ELSE COALESCE(NULLIF(g.AwayTeamName,''),pa.BattingTeamCode) END), pa.BattingTeamCode",
                string.Empty,
                "pa.BattingTeamCode"),
            _ => (
                "pa.BatterPcode, MAX(COALESCE(pa.BatterName,'')), pa.BattingTeamCode",
                string.Empty,
                "pa.BatterPcode, pa.BattingTeamCode"),
        };

        var primaryPositions = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var posCommand = connection.CreateCommand())
        {
            posCommand.CommandText = "SELECT Pcode, COALESCE(PrimaryPosition,'-') FROM Players";
            await using var posReader = await posCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await posReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                primaryPositions[posReader.GetString(0)] = posReader.GetString(1);
        }

        var pitchStats = new Dictionary<string, SituationPitchStats>(StringComparer.Ordinal);
        await using (var pitchCommand = connection.CreateCommand())
        {
            pitchCommand.CommandText = $"""
                {gameFilter.Cte},
                FilteredPA AS (
                    SELECT pa.*
                    FROM PlateAppearances pa
                    INNER JOIN FilteredGames g ON g.GameId=pa.GameId
                    WHERE pa.IsOfficial=1
                      AND ($resultTeam='' OR pa.BattingTeamCode=$resultTeam)
                      AND COALESCE(pa.BatterPcode,'')<>''
                      {situation.Sql}
                )
                SELECT {selectIdentity},
                       COUNT(p.PitchEventId),
                       SUM(CASE WHEN p.IsSwing=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsContact=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsWhiff=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsCalledStrike=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsCalledStrike=1 OR p.IsWhiff=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=0 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=1 AND p.IsSwing=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=0 AND p.IsSwing=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=1 AND p.IsContact=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=0 AND p.IsContact=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.ActualPitchIndex=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.ActualPitchIndex=1 AND p.IsSwing=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.SpeedKmh IS NOT NULL THEN p.SpeedKmh ELSE 0 END),
                       SUM(CASE WHEN p.SpeedKmh IS NOT NULL THEN 1 ELSE 0 END)
                FROM FilteredPA pa
                INNER JOIN FilteredGames g ON g.GameId=pa.GameId
                INNER JOIN Pitches p ON p.PlateAppearanceId=pa.PlateAppearanceId
                {joinIdentity}
                WHERE 1=1 {pitchSituation.Sql}
                GROUP BY {groupIdentity};
                """;
            AddParameters(pitchCommand, gameFilter.Parameters);
            AddSituationParameters(pitchCommand, situation, pitchSituation);
            pitchCommand.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
            await using var reader = await pitchCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var code = reader.GetString(0);
                var name = reader.GetString(1);
                var team = reader.GetString(2);
                var key = SituationKey(query.Grouping, code, team);
                pitchStats[key] = new SituationPitchStats
                {
                    Pitches = ReadInt32(reader, 3), Swings = ReadInt32(reader, 4), Contacts = ReadInt32(reader, 5),
                    Whiffs = ReadInt32(reader, 6), CalledStrikes = ReadInt32(reader, 7), Csw = ReadInt32(reader, 8),
                    InZone = ReadInt32(reader, 9), OutZone = ReadInt32(reader, 10), ZoneSwings = ReadInt32(reader, 11),
                    ChaseSwings = ReadInt32(reader, 12), ZoneContacts = ReadInt32(reader, 13), OutZoneContacts = ReadInt32(reader, 14),
                    FirstPitches = ReadInt32(reader, 15), FirstPitchSwings = ReadInt32(reader, 16),
                    SpeedSum = ReadDouble(reader, 17), SpeedCount = ReadInt32(reader, 18),
                };
            }
        }

        var rows = new List<BatterAggregateRecord>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {gameFilter.Cte}
            SELECT {selectIdentity},
                   COUNT(DISTINCT pa.GameId), COUNT(*),
                   SUM(CASE WHEN pa.CountsAsAtBat=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType IN (1,2,3) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=4 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=5 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=6 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType IN (7,8) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=8 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=9 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=10 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=17 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType IN (16,18) THEN 1 ELSE 0 END),
                   0,
                   SUM(pa.TotalBases),
                   0,
                   SUM(pa.RunsScored),
                   0, 0,
                   SUM(pa.OutsRecorded), SUM(pa.RunsScored),
                   SUM(CASE WHEN pa.BattedBallType IN (2,4) THEN 1 ELSE 0 END),
                   SUM(COALESCE(pa.WpaByPlate,0))
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            {joinIdentity}
            WHERE pa.IsOfficial=1
              AND ($resultTeam='' OR pa.BattingTeamCode=$resultTeam)
              AND COALESCE(pa.BatterPcode,'')<>''
              {situation.Sql}
            GROUP BY {groupIdentity};
            """;
        AddParameters(command, gameFilter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        await using var paReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await paReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            var code = paReader.GetString(i++);
            var name = paReader.GetString(i++);
            var team = paReader.GetString(i++);
            var key = SituationKey(query.Grouping, code, team);
            pitchStats.TryGetValue(key, out var ps);
            ps ??= new SituationPitchStats();
            primaryPositions.TryGetValue(code, out var primaryPosition);
            primaryPosition ??= "-";
            rows.Add(new BatterAggregateRecord
            {
                Pcode = code, Name = name, TeamCode = team, Games = ReadInt32(paReader, i++),
                PlateAppearances = ReadInt32(paReader, i++), AtBats = ReadInt32(paReader, i++), Hits = ReadInt32(paReader, i++),
                Singles = ReadInt32(paReader, i++), Doubles = ReadInt32(paReader, i++), Triples = ReadInt32(paReader, i++),
                HomeRuns = ReadInt32(paReader, i++), Walks = ReadInt32(paReader, i++), IntentionalWalks = ReadInt32(paReader, i++),
                HitByPitch = ReadInt32(paReader, i++), Strikeouts = ReadInt32(paReader, i++), SacrificeFlies = ReadInt32(paReader, i++),
                SacrificeBunts = ReadInt32(paReader, i++), DoublePlays = ReadInt32(paReader, i++), TotalBases = ReadInt32(paReader, i++),
                Runs = ReadInt32(paReader, i++), RunsBattedIn = ReadInt32(paReader, i++), StolenBases = ReadInt32(paReader, i++),
                CaughtStealing = ReadInt32(paReader, i++), OutsRecorded = ReadInt32(paReader, i++), RunsScoredOnPlays = ReadInt32(paReader, i++),
                FlyBalls = ReadInt32(paReader, i++), Wpa = ReadDouble(paReader, i++),
                Pitches = ps.Pitches, Swings = ps.Swings, Contacts = ps.Contacts, Whiffs = ps.Whiffs,
                CalledStrikes = ps.CalledStrikes, Csw = ps.Csw, InZone = ps.InZone, OutZone = ps.OutZone,
                ZoneSwings = ps.ZoneSwings, ChaseSwings = ps.ChaseSwings, ZoneContacts = ps.ZoneContacts,
                OutZoneContacts = ps.OutZoneContacts, FirstPitches = ps.FirstPitches, FirstPitchSwings = ps.FirstPitchSwings,
                CatcherInnings = primaryPosition == "C" ? 1.0 : 0.0,
                FirstBaseInnings = primaryPosition == "1B" ? 1.0 : 0.0,
                SecondBaseInnings = primaryPosition == "2B" ? 1.0 : 0.0,
                ThirdBaseInnings = primaryPosition == "3B" ? 1.0 : 0.0,
                ShortstopInnings = primaryPosition == "SS" ? 1.0 : 0.0,
                LeftFieldInnings = primaryPosition == "LF" ? 1.0 : 0.0,
                CenterFieldInnings = primaryPosition == "CF" ? 1.0 : 0.0,
                RightFieldInnings = primaryPosition == "RF" ? 1.0 : 0.0,
                DesignatedHitterPlateAppearances = primaryPosition == "DH" ? 1 : 0,
            });
        }
        return rows;
    }

    private static async Task<List<PitcherAggregateRecord>> ReadSituationPitcherAggregatesAsync(
        SqliteConnection connection,
        SqlFilter gameFilter,
        GameQuery query,
        CancellationToken cancellationToken)
    {
        var situation = BuildPlateAppearanceSituationSql(query, "pa", includeCountReached: true);
        var pitchSituation = BuildPitchSituationSql(query, "p");
        var pitcherCode = "COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode)";
        var pitcherName = "COALESCE(NULLIF(pa.FinalPitcherName,''),pa.PitcherName,'')";
        var (selectIdentity, joinIdentity, groupIdentity) = query.Grouping switch
        {
            AnalyticsGrouping.PlayerCareer => (
                $"{pitcherCode}, MAX({pitcherName}), CASE WHEN $resultTeam<>'' THEN $resultTeam ELSE COALESCE(MAX(pl.LatestTeam),'') END",
                $"LEFT JOIN Players pl ON pl.Pcode={pitcherCode}",
                pitcherCode),
            AnalyticsGrouping.Team => (
                $"pa.FieldingTeamCode, MAX(CASE WHEN g.HomeTeamCode=pa.FieldingTeamCode THEN COALESCE(NULLIF(g.HomeTeamName,''),pa.FieldingTeamCode) ELSE COALESCE(NULLIF(g.AwayTeamName,''),pa.FieldingTeamCode) END), pa.FieldingTeamCode",
                string.Empty,
                "pa.FieldingTeamCode"),
            _ => (
                $"{pitcherCode}, MAX({pitcherName}), pa.FieldingTeamCode",
                string.Empty,
                $"{pitcherCode}, pa.FieldingTeamCode"),
        };

        var pitchStats = new Dictionary<string, SituationPitchStats>(StringComparer.Ordinal);
        await using (var pitchCommand = connection.CreateCommand())
        {
            pitchCommand.CommandText = $"""
                {gameFilter.Cte},
                FilteredPA AS (
                    SELECT pa.*
                    FROM PlateAppearances pa
                    INNER JOIN FilteredGames g ON g.GameId=pa.GameId
                    WHERE pa.IsOfficial=1
                      AND ($resultTeam='' OR pa.FieldingTeamCode=$resultTeam)
                      AND COALESCE(COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode),'')<>''
                      {situation.Sql}
                )
                SELECT {selectIdentity},
                       COUNT(p.PitchEventId),
                       SUM(CASE WHEN p.IsSwing=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsContact=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsWhiff=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsCalledStrike=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsCalledStrike=1 OR p.IsWhiff=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=0 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=1 AND p.IsSwing=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=0 AND p.IsSwing=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=1 AND p.IsContact=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.IsInNominalStrikeZone=0 AND p.IsContact=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.ActualPitchIndex=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.ActualPitchIndex=1 AND p.IsSwing=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN p.SpeedKmh IS NOT NULL THEN p.SpeedKmh ELSE 0 END),
                       SUM(CASE WHEN p.SpeedKmh IS NOT NULL THEN 1 ELSE 0 END)
                FROM FilteredPA pa
                INNER JOIN FilteredGames g ON g.GameId=pa.GameId
                INNER JOIN Pitches p ON p.PlateAppearanceId=pa.PlateAppearanceId
                {joinIdentity}
                WHERE 1=1 {pitchSituation.Sql}
                GROUP BY {groupIdentity};
                """;
            AddParameters(pitchCommand, gameFilter.Parameters);
            AddSituationParameters(pitchCommand, situation, pitchSituation);
            pitchCommand.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
            await using var reader = await pitchCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var code = reader.GetString(0);
                var name = reader.GetString(1);
                var team = reader.GetString(2);
                pitchStats[SituationKey(query.Grouping, code, team)] = new SituationPitchStats
                {
                    Pitches = ReadInt32(reader, 3), Swings = ReadInt32(reader, 4), Contacts = ReadInt32(reader, 5),
                    Whiffs = ReadInt32(reader, 6), CalledStrikes = ReadInt32(reader, 7), Csw = ReadInt32(reader, 8),
                    InZone = ReadInt32(reader, 9), OutZone = ReadInt32(reader, 10), ZoneSwings = ReadInt32(reader, 11),
                    ChaseSwings = ReadInt32(reader, 12), ZoneContacts = ReadInt32(reader, 13), OutZoneContacts = ReadInt32(reader, 14),
                    FirstPitches = ReadInt32(reader, 15), FirstPitchSwings = ReadInt32(reader, 16),
                    SpeedSum = ReadDouble(reader, 17), SpeedCount = ReadInt32(reader, 18),
                };
            }
        }

        var rows = new List<PitcherAggregateRecord>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {gameFilter.Cte}
            SELECT {selectIdentity},
                   COUNT(DISTINCT pa.GameId), COUNT(*), SUM(pa.OutsRecorded),
                   SUM(CASE WHEN pa.IsHit=1 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=6 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType IN (7,8) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=9 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=10 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.ResultType=17 THEN 1 ELSE 0 END),
                   SUM(pa.RunsScored),
                   SUM(CASE WHEN pa.CountsAsAtBat=1 THEN 1 ELSE 0 END),
                   SUM(CASE
                       WHEN pa.ResultType IN (1,2,3) THEN 1
                       WHEN pa.ResultType=4 THEN 2
                       WHEN pa.ResultType=5 THEN 3
                       WHEN pa.ResultType=6 THEN 4
                       ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType IN (2,4) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN pa.BattedBallType=4 OR pa.ResultType IN (3,16,18) THEN 1 ELSE 0 END)
            FROM PlateAppearances pa
            INNER JOIN FilteredGames g ON g.GameId=pa.GameId
            {joinIdentity}
            WHERE pa.IsOfficial=1
              AND ($resultTeam='' OR pa.FieldingTeamCode=$resultTeam)
              AND COALESCE(COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode),'')<>''
              {situation.Sql}
            GROUP BY {groupIdentity};
            """;
        AddParameters(command, gameFilter.Parameters);
        AddSituationParameters(command, situation);
        command.Parameters.AddWithValue("$resultTeam", query.TeamCode ?? string.Empty);
        await using var reader2 = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader2.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            var code = reader2.GetString(i++);
            var name = reader2.GetString(i++);
            var team = reader2.GetString(i++);
            pitchStats.TryGetValue(SituationKey(query.Grouping, code, team), out var ps);
            ps ??= new SituationPitchStats();
            rows.Add(new PitcherAggregateRecord
            {
                Pcode = code, Name = name, TeamCode = team, Games = ReadInt32(reader2, i++),
                BattersFaced = ReadInt32(reader2, i++), PlateAppearanceOuts = ReadInt32(reader2, i++),
                HitsFromPlateAppearances = ReadInt32(reader2, i++), HomeRunsFromPlateAppearances = ReadInt32(reader2, i++),
                WalksFromPlateAppearances = ReadInt32(reader2, i++), HitBattersFromPlateAppearances = ReadInt32(reader2, i++),
                StrikeoutsFromPlateAppearances = ReadInt32(reader2, i++), SacrificeFlies = ReadInt32(reader2, i++),
                RunsFromPlateAppearances = ReadInt32(reader2, i++),
                OpponentAtBats = ReadInt32(reader2, i++), TotalBasesAllowed = ReadInt32(reader2, i++),
                FlyBalls = ReadInt32(reader2, i++), InfieldFlies = ReadInt32(reader2, i++),
                Pitches = ps.Pitches, Swings = ps.Swings, Contacts = ps.Contacts, Whiffs = ps.Whiffs,
                CalledStrikes = ps.CalledStrikes, Csw = ps.Csw, InZone = ps.InZone, OutZone = ps.OutZone,
                ZoneSwings = ps.ZoneSwings, ChaseSwings = ps.ChaseSwings, ZoneContacts = ps.ZoneContacts,
                OutZoneContacts = ps.OutZoneContacts, FirstPitches = ps.FirstPitches, FirstPitchSwings = ps.FirstPitchSwings,
                SpeedSum = ps.SpeedSum, SpeedCount = ps.SpeedCount,
                // 상황 필터에서는 ER/최종 IP를 타석별로 분해할 수 없으므로 최종 라인 기반 WAR/ERA는 의도적으로 비웁니다.
                FinalGames = 0, GamesStarted = 0, ReliefGames = 0, InningsOuts = 0,
            });
        }
        return rows;
    }

    private static string SituationKey(AnalyticsGrouping grouping, string code, string team) => grouping switch
    {
        AnalyticsGrouping.PlayerCareer => code,
        AnalyticsGrouping.Team => team,
        _ => $"{code}|{team}",
    };
}
