using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Players;
using NaverRelay.Parsing;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    public async Task<DatabaseCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        DateTime? minDate = null;
        DateTime? maxDate = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*), MIN(GameDate), MAX(GameDate) FROM Games;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                count = Convert.ToInt32(reader.GetInt64(0), CultureInfo.InvariantCulture);
                minDate = ParseDate(reader.IsDBNull(1) ? null : reader.GetString(1));
                maxDate = ParseDate(reader.IsDBNull(2) ? null : reader.GetString(2));
            }
        }

        var years = await ReadDistinctIntsAsync(connection,
            "SELECT DISTINCT SeasonYear FROM Games WHERE SeasonYear IS NOT NULL ORDER BY SeasonYear DESC;",
            cancellationToken).ConfigureAwait(false);
        var teams = await ReadDistinctStringsAsync(connection,
            """
            SELECT TeamCode FROM (
                SELECT HomeTeamCode AS TeamCode FROM Games
                UNION
                SELECT AwayTeamCode AS TeamCode FROM Games
            ) WHERE TeamCode IS NOT NULL AND TRIM(TeamCode)<>'' ORDER BY TeamCode;
            """, cancellationToken).ConfigureAwait(false);
        var stadiums = await ReadDistinctStringsAsync(connection,
            "SELECT DISTINCT Stadium FROM Games WHERE Stadium IS NOT NULL AND TRIM(Stadium)<>'' ORDER BY Stadium;",
            cancellationToken).ConfigureAwait(false);

        return new DatabaseCatalog
        {
            GameCount = count,
            MinGameDate = minDate,
            MaxGameDate = maxDate,
            Years = years,
            Teams = teams,
            Stadiums = stadiums,
        };
    }

    public async Task<DatabaseFilterOptions> GetFilterOptionsAsync(
        GameQuery query,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = query with
        {
            TeamCode = null,
            OpponentCode = null,
            Venue = null,
            Stadium = null,
            Weekday = null,
            StartDate = null,
            EndDate = null,
            RecentGameCount = null,
        };
        var filter = BuildFilteredGamesCte(baseQuery);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var teams = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                {filter.Cte}
                SELECT TeamCode FROM (
                    SELECT HomeTeamCode AS TeamCode FROM FilteredGames
                    UNION
                    SELECT AwayTeamCode AS TeamCode FROM FilteredGames
                ) WHERE TeamCode IS NOT NULL AND TRIM(TeamCode)<>'' ORDER BY TeamCode;
                """;
            AddParameters(command, filter.Parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) teams.Add(reader.GetString(0));
        }

        var stadiums = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"{filter.Cte} SELECT DISTINCT Stadium FROM FilteredGames WHERE Stadium IS NOT NULL AND TRIM(Stadium)<>'' ORDER BY Stadium;";
            AddParameters(command, filter.Parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) stadiums.Add(reader.GetString(0));
        }

        DateTime? minDate = null;
        DateTime? maxDate = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"{filter.Cte} SELECT MIN(GameDate), MAX(GameDate) FROM FilteredGames;";
            AddParameters(command, filter.Parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                minDate = ParseDate(reader.IsDBNull(0) ? null : reader.GetString(0));
                maxDate = ParseDate(reader.IsDBNull(1) ? null : reader.GetString(1));
            }
        }
        return new DatabaseFilterOptions { Teams = teams, Stadiums = stadiums, MinGameDate = minDate, MaxGameDate = maxDate };
    }

    public async Task<DatabaseSummary> GetSummaryAsync(GameQuery query, CancellationToken cancellationToken = default)
    {
        var filter = BuildFilteredGamesCte(query);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT COUNT(*), COALESCE(SUM(CompletedPaCount),0), COALESCE(SUM(PitchCount),0),
                   COALESCE(SUM(RunnerCount),0), COALESCE(SUM(PlayerChangeCount),0),
                   COALESCE(SUM(AdministrativeCount),0), COALESCE(SUM(WarningCount),0),
                   COALESCE(SUM(ErrorCount),0)
            FROM FilteredGames;
            """;
        AddParameters(command, filter.Parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new DatabaseSummary();
        return new DatabaseSummary
        {
            Games = Convert.ToInt32(reader.GetInt64(0), CultureInfo.InvariantCulture),
            PlateAppearances = reader.GetInt64(1),
            Pitches = reader.GetInt64(2),
            RunnerEvents = reader.GetInt64(3),
            PlayerChanges = reader.GetInt64(4),
            AdministrativeEvents = reader.GetInt64(5),
            Warnings = reader.GetInt64(6),
            Errors = reader.GetInt64(7),
        };
    }

    public async Task<IReadOnlyList<DatabaseGameHeader>> GetGameHeadersAsync(
        GameQuery query,
        CancellationToken cancellationToken = default)
    {
        var filter = BuildFilteredGamesCte(query);
        var rows = new List<DatabaseGameHeader>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT GameId, SeasonYear, GameDate, RoundCode, CompetitionType, Stadium,
                   HomeTeamCode, AwayTeamCode, HomeTeamName, AwayTeamName, HomeScore, AwayScore,
                   CompletedPaCount, PitchCount, RunnerCount, PlayerChangeCount,
                   AdministrativeCount, WarningCount, ErrorCount
            FROM FilteredGames ORDER BY GameDate DESC, GameId DESC;
            """;
        AddParameters(command, filter.Parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new DatabaseGameHeader
            {
                GameId = reader.GetString(0),
                SeasonYear = NullableInt(reader, 1),
                GameDate = NullableString(reader, 2),
                RoundCode = NullableString(reader, 3),
                CompetitionType = (GameCompetitionType)ReadInt32(reader, 4),
                Stadium = NullableString(reader, 5),
                HomeTeamCode = NullableString(reader, 6),
                AwayTeamCode = NullableString(reader, 7),
                HomeTeamName = NullableString(reader, 8),
                AwayTeamName = NullableString(reader, 9),
                HomeScore = NullableInt(reader, 10),
                AwayScore = NullableInt(reader, 11),
                PlateAppearances = ReadInt32(reader, 12),
                Pitches = ReadInt32(reader, 13),
                RunnerEvents = ReadInt32(reader, 14),
                PlayerChanges = ReadInt32(reader, 15),
                AdministrativeEvents = ReadInt32(reader, 16),
                Warnings = ReadInt32(reader, 17),
                Errors = ReadInt32(reader, 18),
            });
        }
        return rows;
    }

    public async Task<IReadOnlyList<PlayerSearchItem>> SearchPlayersAsync(
        string? query,
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        var text = query?.Trim() ?? string.Empty;
        var rows = new List<PlayerSearchItem>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.Pcode, p.Name, p.BirthDate,
                   COALESCE((
                       SELECT gp.TeamCode
                       FROM GamePlayers gp INNER JOIN Games g ON g.GameId=gp.GameId
                       WHERE gp.Pcode=p.Pcode
                         AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
                         AND TRIM(COALESCE(gp.TeamCode,''))<>''
                       ORDER BY g.GameDate DESC, g.GameId DESC
                       LIMIT 1
                   ), '-') AS LatestRegularTeam,
                   p.PrimaryPosition, p.Role, p.BatsThrows, p.FirstSeason, p.LastSeason
            FROM Players p
            WHERE $query='' OR p.Name LIKE $contains COLLATE NOCASE
                            OR p.Pcode LIKE $contains COLLATE NOCASE
                            OR EXISTS (
                                SELECT 1 FROM GamePlayers gp INNER JOIN Games g ON g.GameId=gp.GameId
                                WHERE gp.Pcode=p.Pcode
                                  AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
                                  AND gp.TeamCode LIKE $contains COLLATE NOCASE
                            )
            ORDER BY CASE WHEN p.Name=$query COLLATE NOCASE THEN 0
                          WHEN p.Name LIKE $prefix COLLATE NOCASE THEN 1 ELSE 2 END,
                     p.Name COLLATE NOCASE, p.BirthDate, p.Pcode
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", text);
        command.Parameters.AddWithValue("$contains", $"%{text}%");
        command.Parameters.AddWithValue("$prefix", $"{text}%");
        command.Parameters.AddWithValue("$limit", Math.Max(1, maxResults));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var first = NullableInt(reader, 7);
            var last = NullableInt(reader, 8);
            rows.Add(new PlayerSearchItem
            {
                Pcode = reader.GetString(0),
                Name = reader.GetString(1),
                BirthDate = NullableString(reader, 2) ?? "미상",
                LatestTeam = NullableString(reader, 3) ?? "-",
                PrimaryPosition = NullableString(reader, 4) ?? "-",
                Role = NullableString(reader, 5) ?? "-",
                BatsThrows = NullableString(reader, 6) ?? "-",
                ActiveYears = first.HasValue && last.HasValue
                    ? first == last ? first.Value.ToString(CultureInfo.InvariantCulture) : $"{first}-{last}"
                    : "-",
            });
        }
        return rows;
    }

    public async Task<PlayerSearchItem?> GetPlayerAsync(string pcode, CancellationToken cancellationToken = default)
    {
        var rows = await SearchPlayersAsync(pcode, 20, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault(row => string.Equals(row.Pcode, pcode, StringComparison.Ordinal));
    }

    internal async Task<WarehouseAnalyticsData> GetAggregateDataAsync(
        GameQuery query,
        IProgress<DatabaseLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (query.HasSituationFilters)
            return await GetSituationAggregateDataAsync(query, progress, cancellationToken).ConfigureAwait(false);

        var filter = BuildFilteredGamesCte(query);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new DatabaseLoadProgress(1, 4, "타자 경기 집계 테이블 조회 중"));
        var batters = await ReadBatterAggregatesAsync(connection, filter, query.TeamCode, query.Grouping, cancellationToken).ConfigureAwait(false);
        progress?.Report(new DatabaseLoadProgress(2, 4, "투수 경기 집계 테이블 조회 중"));
        var pitchers = await ReadPitcherAggregatesAsync(connection, filter, query.TeamCode, query.Grouping, cancellationToken).ConfigureAwait(false);
        progress?.Report(new DatabaseLoadProgress(3, 4, "구장별 투구이닝 조회 중"));
        await AttachPitcherStadiumOutsAsync(connection, filter, query.TeamCode, query.Grouping, pitchers, cancellationToken).ConfigureAwait(false);
        progress?.Report(new DatabaseLoadProgress(4, 4, "팀 경기 수 조회 중"));
        var teamGames = await ReadTeamGamesAsync(connection, filter, cancellationToken).ConfigureAwait(false);
        return new WarehouseAnalyticsData { Batters = batters, Pitchers = pitchers, TeamGames = teamGames };
    }

    private static async Task<List<BatterAggregateRecord>> ReadBatterAggregatesAsync(
        SqliteConnection connection,
        SqlFilter filter,
        string? resultTeam,
        AnalyticsGrouping grouping,
        CancellationToken cancellationToken)
    {
        var rows = new List<BatterAggregateRecord>();
        await using var command = connection.CreateCommand();
        var identity = grouping switch
        {
            AnalyticsGrouping.PlayerCareer => (
                "b.Pcode, MAX(b.Name), CASE WHEN $resultTeam<>'' THEN $resultTeam ELSE COALESCE(MAX(pl.LatestTeam),'') END",
                "LEFT JOIN Players pl ON pl.Pcode=b.Pcode",
                "b.Pcode"),
            AnalyticsGrouping.Team => (
                "b.TeamCode, MAX(CASE WHEN g.HomeTeamCode=b.TeamCode THEN COALESCE(NULLIF(g.HomeTeamName,''),b.TeamCode) ELSE COALESCE(NULLIF(g.AwayTeamName,''),b.TeamCode) END), b.TeamCode",
                string.Empty,
                "b.TeamCode"),
            _ => (
                "b.Pcode, MAX(b.Name), b.TeamCode",
                string.Empty,
                "b.Pcode, b.TeamCode"),
        };

        command.CommandText = $"""
            {filter.Cte}
            SELECT {identity.Item1}, COUNT(DISTINCT b.GameId),
                   SUM(b.PA), SUM(b.AB), SUM(b.H), SUM(b.Singles), SUM(b.Doubles), SUM(b.Triples),
                   SUM(b.HR), SUM(b.BB), SUM(b.IBB), SUM(b.HBP), SUM(b.SO), SUM(b.SF), SUM(b.SH),
                   SUM(b.GDP), SUM(b.TB), SUM(b.Runs), SUM(b.RBI), SUM(b.SB), SUM(b.CS),
                   SUM(b.OutsRecorded), SUM(b.RunsScoredOnPlays), SUM(b.FlyBalls), SUM(b.WPA),
                   SUM(b.Pitches), SUM(b.Swings), SUM(b.Contacts), SUM(b.Whiffs), SUM(b.CalledStrikes),
                   SUM(b.CSW), SUM(b.InZone), SUM(b.OutZone), SUM(b.ZoneSwings), SUM(b.ChaseSwings),
                   SUM(b.ZoneContacts), SUM(b.OutZoneContacts), SUM(b.FirstPitches), SUM(b.FirstPitchSwings),
                   SUM(b.CatcherInnings), SUM(b.FirstBaseInnings), SUM(b.SecondBaseInnings),
                   SUM(b.ThirdBaseInnings), SUM(b.ShortstopInnings), SUM(b.LeftFieldInnings),
                   SUM(b.CenterFieldInnings), SUM(b.RightFieldInnings), SUM(b.DhPa)
            FROM BatterGameStats b
            INNER JOIN FilteredGames g ON g.GameId=b.GameId
            {identity.Item2}
            WHERE ($resultTeam='' OR b.TeamCode=$resultTeam)
            GROUP BY {identity.Item3};
            """;
        AddParameters(command, filter.Parameters);
        command.Parameters.AddWithValue("$resultTeam", resultTeam ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            rows.Add(new BatterAggregateRecord
            {
                Pcode = reader.GetString(i++), Name = reader.GetString(i++), TeamCode = reader.GetString(i++), Games = ReadInt32(reader, i++),
                PlateAppearances = ReadInt32(reader, i++), AtBats = ReadInt32(reader, i++), Hits = ReadInt32(reader, i++),
                Singles = ReadInt32(reader, i++), Doubles = ReadInt32(reader, i++), Triples = ReadInt32(reader, i++),
                HomeRuns = ReadInt32(reader, i++), Walks = ReadInt32(reader, i++), IntentionalWalks = ReadInt32(reader, i++),
                HitByPitch = ReadInt32(reader, i++), Strikeouts = ReadInt32(reader, i++), SacrificeFlies = ReadInt32(reader, i++),
                SacrificeBunts = ReadInt32(reader, i++), DoublePlays = ReadInt32(reader, i++), TotalBases = ReadInt32(reader, i++),
                Runs = ReadInt32(reader, i++), RunsBattedIn = ReadInt32(reader, i++), StolenBases = ReadInt32(reader, i++),
                CaughtStealing = ReadInt32(reader, i++), OutsRecorded = ReadInt32(reader, i++), RunsScoredOnPlays = ReadInt32(reader, i++),
                FlyBalls = ReadInt32(reader, i++), Wpa = ReadDouble(reader, i++), Pitches = ReadInt32(reader, i++),
                Swings = ReadInt32(reader, i++), Contacts = ReadInt32(reader, i++), Whiffs = ReadInt32(reader, i++),
                CalledStrikes = ReadInt32(reader, i++), Csw = ReadInt32(reader, i++), InZone = ReadInt32(reader, i++),
                OutZone = ReadInt32(reader, i++), ZoneSwings = ReadInt32(reader, i++), ChaseSwings = ReadInt32(reader, i++),
                ZoneContacts = ReadInt32(reader, i++), OutZoneContacts = ReadInt32(reader, i++), FirstPitches = ReadInt32(reader, i++),
                FirstPitchSwings = ReadInt32(reader, i++), CatcherInnings = ReadDouble(reader, i++), FirstBaseInnings = ReadDouble(reader, i++),
                SecondBaseInnings = ReadDouble(reader, i++), ThirdBaseInnings = ReadDouble(reader, i++), ShortstopInnings = ReadDouble(reader, i++),
                LeftFieldInnings = ReadDouble(reader, i++), CenterFieldInnings = ReadDouble(reader, i++), RightFieldInnings = ReadDouble(reader, i++),
                DesignatedHitterPlateAppearances = ReadInt32(reader, i++),
            });
        }
        return rows;
    }

    private static async Task<List<PitcherAggregateRecord>> ReadPitcherAggregatesAsync(
        SqliteConnection connection,
        SqlFilter filter,
        string? resultTeam,
        AnalyticsGrouping grouping,
        CancellationToken cancellationToken)
    {
        var rows = new List<PitcherAggregateRecord>();
        await using var command = connection.CreateCommand();
        var identity = grouping switch
        {
            AnalyticsGrouping.PlayerCareer => (
                "p.Pcode, MAX(p.Name), CASE WHEN $resultTeam<>'' THEN $resultTeam ELSE COALESCE(MAX(pl.LatestTeam),'') END",
                "LEFT JOIN Players pl ON pl.Pcode=p.Pcode",
                "p.Pcode"),
            AnalyticsGrouping.Team => (
                "p.TeamCode, MAX(CASE WHEN g.HomeTeamCode=p.TeamCode THEN COALESCE(NULLIF(g.HomeTeamName,''),p.TeamCode) ELSE COALESCE(NULLIF(g.AwayTeamName,''),p.TeamCode) END), p.TeamCode",
                string.Empty,
                "p.TeamCode"),
            _ => (
                "p.Pcode, MAX(p.Name), p.TeamCode",
                string.Empty,
                "p.Pcode, p.TeamCode"),
        };

        command.CommandText = $"""
            {filter.Cte},
            TeamGameOuts AS (
                SELECT pg.GameId, pg.TeamCode, SUM(pg.InningsOuts) AS TeamOuts
                FROM PitcherGameStats pg
                INNER JOIN FilteredGames tfg ON tfg.GameId=pg.GameId
                GROUP BY pg.GameId, pg.TeamCode
            ),
            OppPaStats AS (
                SELECT pa.GameId,
                       COALESCE(NULLIF(pa.PitcherPcode,''),'') AS Pcode,
                       COALESCE(pa.FieldingTeamCode,'') AS TeamCode,
                       SUM(CASE WHEN pa.CountsAsAtBat=1 THEN 1 ELSE 0 END) AS OppAB,
                       SUM(CASE
                           WHEN pa.ResultType IN (1,2,3) THEN 1
                           WHEN pa.ResultType=4 THEN 2
                           WHEN pa.ResultType=5 THEN 3
                           WHEN pa.ResultType=6 THEN 4
                           ELSE 0 END) AS OppTB
                FROM PlateAppearances pa
                INNER JOIN FilteredGames fg ON fg.GameId=pa.GameId
                WHERE pa.IsOfficial=1
                  AND COALESCE(NULLIF(pa.PitcherPcode,''),'')<>''
                GROUP BY pa.GameId, COALESCE(NULLIF(pa.PitcherPcode,''),''), COALESCE(pa.FieldingTeamCode,'')
            )
            SELECT {identity.Item1}, COUNT(DISTINCT p.GameId),
                   SUM(p.TBF), SUM(p.PaOuts), SUM(p.PaHits), SUM(p.PaHR), SUM(p.PaBB), SUM(p.PaHBP),
                   SUM(p.PaSO), SUM(p.SF), SUM(p.PaRuns),
                   SUM(COALESCE(opa.OppAB,0)), SUM(COALESCE(opa.OppTB,0)),
                   SUM(p.FlyBalls), SUM(p.IFFB),
                   SUM(p.Pitches), SUM(p.Swings), SUM(p.Contacts), SUM(p.Whiffs), SUM(p.CalledStrikes),
                   SUM(p.CSW), SUM(p.InZone), SUM(p.OutZone), SUM(p.ZoneSwings), SUM(p.ChaseSwings),
                   SUM(p.ZoneContacts), SUM(p.OutZoneContacts), SUM(p.FirstPitches), SUM(p.FirstPitchSwings),
                   SUM(p.SpeedSum), SUM(p.SpeedCount), SUM(CASE WHEN p.HasFinalLine=1 THEN 1 ELSE 0 END),
                   SUM(p.IsStarter), SUM(p.IsReliever),
                   SUM(CASE WHEN p.IsStarter=1 AND tgo.TeamOuts>0 AND p.InningsOuts=tgo.TeamOuts THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.IsStarter=1 AND tgo.TeamOuts>0 AND p.InningsOuts=tgo.TeamOuts AND p.RunsAllowed=0 THEN 1 ELSE 0 END),
                   SUM(p.InningsOuts),
                   SUM(CASE WHEN p.IsStarter=1 THEN p.InningsOuts ELSE 0 END),
                   SUM(CASE WHEN p.IsReliever=1 THEN p.InningsOuts ELSE 0 END),
                   SUM(p.HitsAllowed), SUM(p.HomeRunsAllowed), SUM(p.FinalBB), SUM(p.FinalHBP),
                   SUM(p.FinalSO), SUM(p.RunsAllowed), SUM(p.EarnedRuns), SUM(p.WildPitches),
                   SUM(p.FinalPitchCount), SUM(p.EntryAbsoluteWpaSum), SUM(p.EntryWpaCount)
            FROM PitcherGameStats p
            INNER JOIN FilteredGames g ON g.GameId=p.GameId
            LEFT JOIN TeamGameOuts tgo ON tgo.GameId=p.GameId AND tgo.TeamCode=p.TeamCode
            LEFT JOIN OppPaStats opa
              ON opa.GameId=p.GameId AND opa.Pcode=p.Pcode AND opa.TeamCode=p.TeamCode
            {identity.Item2}
            WHERE ($resultTeam='' OR p.TeamCode=$resultTeam)
            GROUP BY {identity.Item3};
            """;
        AddParameters(command, filter.Parameters);
        command.Parameters.AddWithValue("$resultTeam", resultTeam ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            rows.Add(new PitcherAggregateRecord
            {
                Pcode = reader.GetString(i++), Name = reader.GetString(i++), TeamCode = reader.GetString(i++), Games = ReadInt32(reader, i++),
                BattersFaced = ReadInt32(reader, i++), PlateAppearanceOuts = ReadInt32(reader, i++), HitsFromPlateAppearances = ReadInt32(reader, i++),
                HomeRunsFromPlateAppearances = ReadInt32(reader, i++), WalksFromPlateAppearances = ReadInt32(reader, i++),
                HitBattersFromPlateAppearances = ReadInt32(reader, i++), StrikeoutsFromPlateAppearances = ReadInt32(reader, i++),
                SacrificeFlies = ReadInt32(reader, i++), RunsFromPlateAppearances = ReadInt32(reader, i++),
                OpponentAtBats = ReadInt32(reader, i++), TotalBasesAllowed = ReadInt32(reader, i++),
                FlyBalls = ReadInt32(reader, i++), InfieldFlies = ReadInt32(reader, i++), Pitches = ReadInt32(reader, i++), Swings = ReadInt32(reader, i++),
                Contacts = ReadInt32(reader, i++), Whiffs = ReadInt32(reader, i++), CalledStrikes = ReadInt32(reader, i++),
                Csw = ReadInt32(reader, i++), InZone = ReadInt32(reader, i++), OutZone = ReadInt32(reader, i++),
                ZoneSwings = ReadInt32(reader, i++), ChaseSwings = ReadInt32(reader, i++), ZoneContacts = ReadInt32(reader, i++),
                OutZoneContacts = ReadInt32(reader, i++), FirstPitches = ReadInt32(reader, i++), FirstPitchSwings = ReadInt32(reader, i++),
                SpeedSum = ReadDouble(reader, i++), SpeedCount = ReadInt32(reader, i++), FinalGames = ReadInt32(reader, i++),
                GamesStarted = ReadInt32(reader, i++), ReliefGames = ReadInt32(reader, i++),
                CompleteGames = ReadInt32(reader, i++), Shutouts = ReadInt32(reader, i++),
                InningsOuts = ReadInt32(reader, i++),
                StarterInningsOuts = ReadInt32(reader, i++), ReliefInningsOuts = ReadInt32(reader, i++), HitsAllowed = ReadInt32(reader, i++),
                HomeRunsAllowed = ReadInt32(reader, i++), FinalWalks = ReadInt32(reader, i++), FinalHitBatters = ReadInt32(reader, i++),
                FinalStrikeouts = ReadInt32(reader, i++), RunsAllowed = ReadInt32(reader, i++), EarnedRuns = ReadInt32(reader, i++),
                WildPitches = ReadInt32(reader, i++), FinalPitchCount = ReadInt32(reader, i++), EntryAbsoluteWpaSum = ReadDouble(reader, i++),
                EntryWpaCount = ReadInt32(reader, i++),
            });
        }
        return rows;
    }

    private static async Task AttachPitcherStadiumOutsAsync(
        SqliteConnection connection,
        SqlFilter filter,
        string? resultTeam,
        AnalyticsGrouping grouping,
        IReadOnlyList<PitcherAggregateRecord> pitchers,
        CancellationToken cancellationToken)
    {
        static string MakeKey(AnalyticsGrouping mode, string pcode, string teamCode) => mode switch
        {
            AnalyticsGrouping.PlayerCareer => pcode,
            AnalyticsGrouping.Team => teamCode,
            _ => $"{pcode}|{teamCode}",
        };

        var map = pitchers.ToDictionary(
            row => MakeKey(grouping, row.Pcode, row.TeamCode),
            StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        var (entityCode, entityTeam, groupBy) = grouping switch
        {
            AnalyticsGrouping.PlayerCareer => ("p.Pcode", "''", "p.Pcode, g.Stadium"),
            AnalyticsGrouping.Team => ("p.TeamCode", "p.TeamCode", "p.TeamCode, g.Stadium"),
            _ => ("p.Pcode", "p.TeamCode", "p.Pcode, p.TeamCode, g.Stadium"),
        };
        command.CommandText = $"""
            {filter.Cte}
            SELECT {entityCode}, {entityTeam}, COALESCE(g.Stadium,''), SUM(p.InningsOuts)
            FROM PitcherGameStats p
            INNER JOIN FilteredGames g ON g.GameId=p.GameId
            WHERE p.HasFinalLine=1 AND ($resultTeam='' OR p.TeamCode=$resultTeam)
            GROUP BY {groupBy};
            """;
        AddParameters(command, filter.Parameters);
        command.Parameters.AddWithValue("$resultTeam", resultTeam ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var entity = reader.GetString(0);
            var team = reader.GetString(1);
            var key = grouping switch
            {
                AnalyticsGrouping.PlayerCareer => entity,
                AnalyticsGrouping.Team => team,
                _ => $"{entity}|{team}",
            };
            if (!map.TryGetValue(key, out var pitcher)) continue;
            pitcher.StadiumOuts[reader.GetString(2)] = ReadInt32(reader, 3);
        }
    }

    private static async Task<IReadOnlyDictionary<string, int>> ReadTeamGamesAsync(
        SqliteConnection connection,
        SqlFilter filter,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {filter.Cte}
            SELECT TeamCode, COUNT(*) FROM (
                SELECT GameId, HomeTeamCode AS TeamCode FROM FilteredGames
                UNION ALL
                SELECT GameId, AwayTeamCode AS TeamCode FROM FilteredGames
            ) WHERE TeamCode IS NOT NULL AND TRIM(TeamCode)<>'' GROUP BY TeamCode;
            """;
        AddParameters(command, filter.Parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result[reader.GetString(0)] = ReadInt32(reader, 1);
        return result;
    }

    private static async Task<IReadOnlyList<int>> ReadDistinctIntsAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var result = new List<int>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadInt32(reader, 0));
        return result;
    }

    private static async Task<IReadOnlyList<string>> ReadDistinctStringsAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(reader.GetString(0));
        return result;
    }

    private static string? NullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static int? NullableInt(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadInt32(reader, ordinal);
    private static double? NullableDouble(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadDouble(reader, ordinal);
    private static bool? NullableBoolean(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadInt32(reader, ordinal) != 0;
}
