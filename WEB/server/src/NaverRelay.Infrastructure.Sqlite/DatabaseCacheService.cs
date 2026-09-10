using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
using NaverRelay.Parsing;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

/// <summary>
/// JSON은 최초 import에서만 사용하고 이후 모든 조회를 관계형 SQLite 테이블에서 수행하는 저장소입니다.
/// Games.NormalizedJson 같은 대형 JSON 열은 만들지 않습니다.
/// </summary>
public sealed partial class DatabaseCacheService : IWarehouseReadService
{
    private const string WarehouseSchemaVersion = "3";
    private const string ParserCacheVersion = "sabermetrics-v2-relational-player-profile-v2";
    private const string LeagueReferenceCacheVersion = "sabermetrics-v2-league-reference-kbo-war-v4";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public string DatabasePath { get; }

    public DatabaseCacheService(string? databasePath = null)
    {
        if (!string.IsNullOrWhiteSpace(databasePath))
        {
            DatabasePath = Path.GetFullPath(databasePath);
            var customDirectory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrWhiteSpace(customDirectory))
                Directory.CreateDirectory(customDirectory);
            return;
        }

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NaverSabermetrics",
            "Data");
        Directory.CreateDirectory(dataDirectory);
        // V2는 구형 JSON-cache/warehouse DB와 완전히 분리된 관계형 DB를 사용합니다.
        DatabasePath = Path.Combine(dataDirectory, "sabermetrics_v2.db");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var isNew = !File.Exists(DatabasePath) || new FileInfo(DatabasePath).Length == 0;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        if (isNew)
            await ExecuteAsync(connection, "PRAGMA page_size=32768;", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA temp_store=MEMORY;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA busy_timeout=10000;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA cache_size=-131072;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA mmap_size=536870912;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, SchemaSql, cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "Players", "LastClubGameDate", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await SetMetadataIfMissingAsync(connection, "SchemaVersion", WarehouseSchemaVersion, cancellationToken).ConfigureAwait(false);
        await SetMetadataIfMissingAsync(connection, "DataVersion", "0", cancellationToken).ConfigureAwait(false);
        await SetMetadataIfMissingAsync(connection, "StorageMode", "RelationalWarehouse", cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await check.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }
        await reader.DisposeAsync().ConfigureAwait(false);
        await ExecuteAsync(connection, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};", cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureReadIndexesAsync(
        IProgress<DatabaseIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new DatabaseIndexProgress(1, 1, "관계형 SQLite 스키마 준비 완료"));
    }

    public async Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "ANALYZE;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA optimize;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "VACUUM;", cancellationToken).ConfigureAwait(false);
    }

    public async Task<HashSet<string>> GetUnchangedSourceKeysAsync(
        IEnumerable<InputDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Fingerprint FROM ParsedSources WHERE SourceKey=$key LIMIT 1;";
        var keyParameter = command.Parameters.Add("$key", SqliteType.Text);

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            keyParameter.Value = GetSourceKey(document);
            var stored = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (string.Equals(stored, GetFingerprint(document), StringComparison.Ordinal))
                result.Add(document.Id);
        }
        return result;
    }

    public async Task SaveGameAndSourceAsync(
        NormalizedGame game,
        InputDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(document);
        var projection = WarehouseProjectionBuilder.Build(game);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await DeleteExistingGameAsync(connection, transaction, game.GameId, cancellationToken).ConfigureAwait(false);
        await InsertGameAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertGameSummaryAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertRelayGroupsAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertEventsAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertPlateAppearancesAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertPitchesAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertRunnerEventsAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertPlayerChangesAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertAdministrativeEventsAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertBattingLinesAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertPitchingLinesAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertDiagnosticsAsync(connection, transaction, game, cancellationToken).ConfigureAwait(false);
        await InsertGamePlayersAsync(connection, transaction, game, projection.Players, cancellationToken).ConfigureAwait(false);
        await InsertBatterGameStatsAsync(connection, transaction, projection.BatterGames, cancellationToken).ConfigureAwait(false);
        await InsertPitcherGameStatsAsync(connection, transaction, projection.PitcherGames, cancellationToken).ConfigureAwait(false);
        await UpsertPlayerProfilesAsync(connection, transaction, game, projection.Players, cancellationToken).ConfigureAwait(false);
        await UpsertParsedSourceAsync(connection, transaction, game.GameId, document, cancellationToken).ConfigureAwait(false);
        await BumpDataVersionAndInvalidateCachesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> TryLoadComputedAsync<T>(string cacheKey, CancellationToken cancellationToken = default)
    {
        var sourceVersion = await GetSourceVersionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT JsonValue FROM ComputedCache WHERE CacheKey=$key AND SourceVersion=$version LIMIT 1;";
        command.Parameters.AddWithValue("$key", cacheKey);
        command.Parameters.AddWithValue("$version", sourceVersion);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(value)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(value, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public async Task SaveComputedAsync<T>(string cacheKey, T value, CancellationToken cancellationToken = default)
    {
        var sourceVersion = await GetSourceVersionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ComputedCache(CacheKey, SourceVersion, JsonValue, UpdatedUtc)
            VALUES($key, $version, $json, $utc)
            ON CONFLICT(CacheKey) DO UPDATE SET
                SourceVersion=excluded.SourceVersion,
                JsonValue=excluded.JsonValue,
                UpdatedUtc=excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$key", cacheKey);
        command.Parameters.AddWithValue("$version", sourceVersion);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(value, JsonOptions));
        command.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 10,
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task<string> GetSourceVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MetaValue FROM Metadata WHERE MetaKey='DataVersion' LIMIT 1;";
        return (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string) ?? "0";
    }

    private static async Task BumpDataVersionAndInvalidateCachesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Metadata(MetaKey, MetaValue) VALUES('DataVersion', '1')
            ON CONFLICT(MetaKey) DO UPDATE SET MetaValue=CAST(CAST(Metadata.MetaValue AS INTEGER) + 1 AS TEXT);
            DELETE FROM ComputedCache;
            DELETE FROM LeagueConstants;
            DELETE FROM ParkFactors;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SetMetadataIfMissingAsync(
        SqliteConnection connection,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO Metadata(MetaKey, MetaValue) VALUES($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string GetSourceKey(InputDocument document)
    {
        var canonical = string.Join("|",
            document.Kind,
            Path.GetFullPath(document.ContainerPath).ToUpperInvariant(),
            (document.EntryName ?? string.Empty).Replace('\\', '/').ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string GetFingerprint(InputDocument document)
    {
        var info = new FileInfo(document.ContainerPath);
        var canonical = string.Join("|",
            document.Length,
            info.Exists ? info.Length : 0,
            info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
            document.EntryName ?? string.Empty,
            ParserCacheVersion);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static object DbValue(object? value) => value ?? DBNull.Value;
    private static int Bool(bool value) => value ? 1 : 0;
    private static int? NullableBool(bool? value) => value.HasValue ? Bool(value.Value) : null;
    private static DateTime? ParseDate(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.Date : null;
    private static string? NormalizeDate(string? value) => ParseDate(value)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string WeekdayNumber(string value) => value switch
    {
        "일" => "0", "월" => "1", "화" => "2", "수" => "3", "목" => "4", "금" => "5", "토" => "6", _ => "",
    };

    private sealed record SqlFilter(string Cte, IReadOnlyList<(string Name, object Value)> Parameters);

    private static SqlFilter BuildFilteredGamesCte(GameQuery query)
    {
        var where = BuildWhereClause(query, out var parameters);
        var limit = query.RecentGameCount.HasValue ? " ORDER BY GameDate DESC, GameId DESC LIMIT $recentLimit" : string.Empty;
        if (query.RecentGameCount.HasValue)
            parameters.Add(("$recentLimit", Math.Max(1, query.RecentGameCount.Value)));
        var cte = $"WITH FilteredGames AS (SELECT * FROM Games {where}{limit})";
        return new SqlFilter(cte, parameters);
    }

    private static string BuildWhereClause(GameQuery query, out List<(string Name, object Value)> parameters)
    {
        var values = new List<(string Name, object Value)>();
        var clauses = new List<string> { "1=1" };
        if (query.SeasonYear.HasValue)
        {
            clauses.Add("SeasonYear=$year");
            values.Add(("$year", query.SeasonYear.Value));
        }

        switch (query.Competition)
        {
            case "정규시즌": clauses.Add("LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_r'"); break;
            case "시범경기": AddCompetition(GameCompetitionType.Preseason); break;
            case "포스트시즌": AddCompetition(GameCompetitionType.Postseason); break;
            case "올스타전": AddCompetition(GameCompetitionType.AllStar); break;
            case "퓨처스리그": AddCompetition(GameCompetitionType.Futures); break;
            case "기타": AddCompetition(GameCompetitionType.Other); break;
        }

        if (!string.IsNullOrWhiteSpace(query.TeamCode))
        {
            clauses.Add("(HomeTeamCode=$team OR AwayTeamCode=$team)");
            values.Add(("$team", query.TeamCode!));
        }
        if (!string.IsNullOrWhiteSpace(query.OpponentCode))
        {
            clauses.Add(!string.IsNullOrWhiteSpace(query.TeamCode)
                ? "((HomeTeamCode=$team AND AwayTeamCode=$opponent) OR (AwayTeamCode=$team AND HomeTeamCode=$opponent))"
                : "(HomeTeamCode=$opponent OR AwayTeamCode=$opponent)");
            values.Add(("$opponent", query.OpponentCode!));
        }
        if (!string.IsNullOrWhiteSpace(query.Venue) && !string.IsNullOrWhiteSpace(query.TeamCode))
            clauses.Add(query.Venue == "홈" ? "HomeTeamCode=$team" : "AwayTeamCode=$team");
        if (!string.IsNullOrWhiteSpace(query.Stadium))
        {
            clauses.Add("Stadium=$stadium COLLATE NOCASE");
            values.Add(("$stadium", query.Stadium!));
        }
        if (!string.IsNullOrWhiteSpace(query.Weekday))
        {
            clauses.Add("strftime('%w', GameDate)=$weekday");
            values.Add(("$weekday", WeekdayNumber(query.Weekday!)));
        }
        if (query.StartDate.HasValue)
        {
            clauses.Add("GameDate >= $startDate");
            values.Add(("$startDate", query.StartDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        if (query.EndDate.HasValue)
        {
            clauses.Add("GameDate <= $endDate");
            values.Add(("$endDate", query.EndDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        parameters = values;
        return "WHERE " + string.Join(" AND ", clauses);

        void AddCompetition(GameCompetitionType type)
        {
            clauses.Add("CompetitionType=$competitionType");
            values.Add(("$competitionType", (int)type));
        }
    }

    private static void AddParameters(SqliteCommand command, IEnumerable<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            if (command.Parameters.Contains(name)) continue;
            command.Parameters.AddWithValue(name, value);
        }
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS Metadata (
            MetaKey TEXT PRIMARY KEY NOT NULL,
            MetaValue TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ParsedSources (
            SourceKey TEXT PRIMARY KEY NOT NULL,
            Fingerprint TEXT NOT NULL,
            GameId TEXT NULL,
            SourceDisplay TEXT NOT NULL,
            ParserVersion TEXT NOT NULL,
            ParsedUtc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_ParsedSources_GameId ON ParsedSources(GameId);

        CREATE TABLE IF NOT EXISTS Games (
            GameId TEXT PRIMARY KEY NOT NULL,
            SeasonYear INTEGER NULL,
            GameDate TEXT NULL,
            GameDateTime TEXT NULL,
            SuperCategoryId TEXT NULL,
            UpperCategoryId TEXT NULL,
            UpperCategoryName TEXT NULL,
            CategoryId TEXT NULL,
            CategoryName TEXT NULL,
            RoundCode TEXT NULL,
            CompetitionType INTEGER NOT NULL,
            Stadium TEXT NULL,
            StatusCode TEXT NULL,
            Winner TEXT NULL,
            AwayTeamCode TEXT NULL,
            AwayTeamName TEXT NULL,
            AwayScore INTEGER NULL,
            AwayHits INTEGER NULL,
            AwayErrors INTEGER NULL,
            AwayWalks INTEGER NULL,
            HomeTeamCode TEXT NULL,
            HomeTeamName TEXT NULL,
            HomeScore INTEGER NULL,
            HomeHits INTEGER NULL,
            HomeErrors INTEGER NULL,
            HomeWalks INTEGER NULL,
            LastHomeWinRate REAL NULL,
            LastAwayWinRate REAL NULL,
            LastWpaByPlate REAL NULL,
            CompletedPaCount INTEGER NOT NULL DEFAULT 0,
            PitchCount INTEGER NOT NULL DEFAULT 0,
            MissingPtsCount INTEGER NOT NULL DEFAULT 0,
            RunnerCount INTEGER NOT NULL DEFAULT 0,
            PlayerChangeCount INTEGER NOT NULL DEFAULT 0,
            AdministrativeCount INTEGER NOT NULL DEFAULT 0,
            WarningCount INTEGER NOT NULL DEFAULT 0,
            ErrorCount INTEGER NOT NULL DEFAULT 0,
            UpdatedUtc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Games_SeasonDate ON Games(SeasonYear, GameDate);
        CREATE INDEX IF NOT EXISTS IX_Games_RoundDate ON Games(RoundCode, GameDate);
        CREATE INDEX IF NOT EXISTS IX_Games_HomeTeamDate ON Games(HomeTeamCode, GameDate);
        CREATE INDEX IF NOT EXISTS IX_Games_AwayTeamDate ON Games(AwayTeamCode, GameDate);
        CREATE INDEX IF NOT EXISTS IX_Games_StadiumDate ON Games(Stadium, GameDate);
        CREATE INDEX IF NOT EXISTS IX_Games_TeamSeasonDate ON Games(SeasonYear, RoundCode, HomeTeamCode, AwayTeamCode, GameDate);

        CREATE TABLE IF NOT EXISTS GameSummaries (
            GameId TEXT PRIMARY KEY NOT NULL,
            RawRelayGroupCount INTEGER NOT NULL,
            RawEventCount INTEGER NOT NULL,
            RawPitchEventCount INTEGER NOT NULL,
            RawPtsCount INTEGER NOT NULL,
            DuplicateSourceSeqNoOccurrenceCount INTEGER NOT NULL,
            CompletedPlateAppearanceCount INTEGER NOT NULL,
            InterruptedPlateAppearanceCount INTEGER NOT NULL,
            PrePlateSubstitutionGroupCount INTEGER NOT NULL,
            InningMarkerGroupCount INTEGER NOT NULL,
            GameSummaryGroupCount INTEGER NOT NULL,
            PitchEventCount INTEGER NOT NULL,
            PtsMatchedPitchCount INTEGER NOT NULL,
            PtsMissingPitchCount INTEGER NOT NULL,
            RunnerEventCount INTEGER NOT NULL,
            PlayerChangeEventCount INTEGER NOT NULL,
            AdministrativeEventCount INTEGER NOT NULL,
            UnknownRelayGroupCount INTEGER NOT NULL,
            UnknownRawEventTypeCount INTEGER NOT NULL,
            UnknownBattingResultCount INTEGER NOT NULL,
            UnparsedRunnerEventCount INTEGER NOT NULL,
            UnparsedPlayerChangeCount INTEGER NOT NULL,
            UnknownAdministrativeEventCount INTEGER NOT NULL,
            PtsCalculationFailureCount INTEGER NOT NULL,
            FinalLineBattingMismatchCount INTEGER NOT NULL,
            WarningCount INTEGER NOT NULL,
            ErrorCount INTEGER NOT NULL,
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS RelayGroups (
            RelayGroupId TEXT PRIMARY KEY NOT NULL,
            GameId TEXT NOT NULL,
            ChronologicalIndex INTEGER NOT NULL,
            SourceRelayNo INTEGER NULL,
            Title TEXT NULL,
            TitleStyle TEXT NULL,
            Inning INTEGER NULL,
            RawHomeOrAway TEXT NULL,
            BattingSide INTEGER NOT NULL,
            BattingTeamCode TEXT NULL,
            SourceStatusCode INTEGER NULL,
            GroupType INTEGER NOT NULL,
            PlateAppearanceId TEXT NULL,
            HomeWinRateAfter REAL NULL,
            AwayWinRateAfter REAL NULL,
            WpaByPlate REAL NULL,
            BeforeHomeScore INTEGER NULL,
            BeforeAwayScore INTEGER NULL,
            BeforeOuts INTEGER NULL,
            BeforeFirstRunnerPcode TEXT NULL,
            BeforeFirstRunnerName TEXT NULL,
            BeforeSecondRunnerPcode TEXT NULL,
            BeforeSecondRunnerName TEXT NULL,
            BeforeThirdRunnerPcode TEXT NULL,
            BeforeThirdRunnerName TEXT NULL,
            AfterHomeScore INTEGER NULL,
            AfterAwayScore INTEGER NULL,
            AfterOuts INTEGER NULL,
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_RelayGroups_GameOrder ON RelayGroups(GameId, ChronologicalIndex);
        CREATE INDEX IF NOT EXISTS IX_RelayGroups_Wpa ON RelayGroups(WpaByPlate);

        CREATE TABLE IF NOT EXISTS NormalizedEvents (
            EventId TEXT PRIMARY KEY NOT NULL,
            GameId TEXT NOT NULL,
            RelayGroupId TEXT NOT NULL,
            PlateAppearanceId TEXT NULL,
            ChronologicalIndex INTEGER NOT NULL,
            SourceRelayNo INTEGER NULL,
            SourceOptionIndex INTEGER NOT NULL,
            SourceSeqNo INTEGER NULL,
            IsDuplicateSourceSeqNo INTEGER NOT NULL,
            RawType INTEGER NULL,
            EventType INTEGER NOT NULL,
            RawText TEXT NULL,
            RawStuff TEXT NULL,
            BattingSide INTEGER NOT NULL,
            Inning INTEGER NULL,
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_NormalizedEvents_GameOrder ON NormalizedEvents(GameId, ChronologicalIndex);
        CREATE INDEX IF NOT EXISTS IX_NormalizedEvents_Relay ON NormalizedEvents(RelayGroupId);

        CREATE TABLE IF NOT EXISTS PlateAppearances (
            PlateAppearanceId TEXT PRIMARY KEY NOT NULL,
            GameId TEXT NOT NULL,
            RelayGroupId TEXT NOT NULL,
            SequenceNumber INTEGER NOT NULL,
            OfficialSequenceNumber INTEGER NULL,
            SourceRelayNo INTEGER NULL,
            Inning INTEGER NULL,
            RawHomeOrAway TEXT NULL,
            BattingSide INTEGER NOT NULL,
            BattingTeamCode TEXT NULL,
            FieldingTeamCode TEXT NULL,
            Status INTEGER NOT NULL,
            IsOfficial INTEGER NOT NULL,
            StartEventId TEXT NULL,
            ResultEventId TEXT NULL,
            BatterPcode TEXT NULL,
            BatterName TEXT NULL,
            BatOrder INTEGER NULL,
            PitcherPcode TEXT NULL,
            PitcherName TEXT NULL,
            FinalPitcherPcode TEXT NULL,
            FinalPitcherName TEXT NULL,
            ResultRawType INTEGER NULL,
            ResultText TEXT NULL,
            ResultType INTEGER NOT NULL,
            NormalizedResultText TEXT NULL,
            BattedBallType INTEGER NOT NULL,
            FieldDirection INTEGER NOT NULL,
            PrimaryFielder TEXT NULL,
            HomeRunDistanceMeters INTEGER NULL,
            CountsAsAtBat INTEGER NOT NULL,
            IsHit INTEGER NOT NULL,
            IsOut INTEGER NOT NULL,
            IsSacrifice INTEGER NOT NULL,
            IsWalk INTEGER NOT NULL,
            IsIntentionalWalk INTEGER NOT NULL,
            IsStrikeout INTEGER NOT NULL,
            ReachedBase INTEGER NOT NULL,
            TotalBases INTEGER NOT NULL,
            WasRecognized INTEGER NOT NULL,
            RunsScored INTEGER NOT NULL,
            OutsRecorded INTEGER NOT NULL,
            ActualPitchCount INTEGER NOT NULL,
            MaximumDisplayPitchNumber INTEGER NULL,
            HomeWinRateAfter REAL NULL,
            AwayWinRateAfter REAL NULL,
            WpaByPlate REAL NULL,
            BeforeHomeScore INTEGER NULL,
            BeforeAwayScore INTEGER NULL,
            BeforeOuts INTEGER NULL,
            BeforeFirstRunnerPcode TEXT NULL,
            BeforeFirstRunnerName TEXT NULL,
            BeforeSecondRunnerPcode TEXT NULL,
            BeforeSecondRunnerName TEXT NULL,
            BeforeThirdRunnerPcode TEXT NULL,
            BeforeThirdRunnerName TEXT NULL,
            AfterHomeScore INTEGER NULL,
            AfterAwayScore INTEGER NULL,
            AfterOuts INTEGER NULL,
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_GameSequence ON PlateAppearances(GameId, SequenceNumber);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_Batter ON PlateAppearances(BatterPcode, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_Pitcher ON PlateAppearances(PitcherPcode, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_Teams ON PlateAppearances(BattingTeamCode, FieldingTeamCode);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_BatterSituation ON PlateAppearances(BatterPcode, Inning, BeforeOuts, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_PitcherSituation ON PlateAppearances(PitcherPcode, Inning, BeforeOuts, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_FinalPitcher ON PlateAppearances(FinalPitcherPcode, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_BattingTeamGame ON PlateAppearances(BattingTeamCode, GameId, PlateAppearanceId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_FieldingTeamGame ON PlateAppearances(FieldingTeamCode, GameId, PlateAppearanceId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_BatterBattedBall ON PlateAppearances(BatterPcode, BattedBallType, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_BatterDirection ON PlateAppearances(BatterPcode, FieldDirection, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_TeamBattedBall ON PlateAppearances(BattingTeamCode, BattedBallType, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_TeamDirection ON PlateAppearances(BattingTeamCode, FieldDirection, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_PitcherBattedBall ON PlateAppearances(FinalPitcherPcode, BattedBallType, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_PitcherDirection ON PlateAppearances(FinalPitcherPcode, FieldDirection, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_PitcherResult ON PlateAppearances(FinalPitcherPcode, ResultType, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_Context ON PlateAppearances(GameId, Inning, BeforeOuts, BatOrder);
        CREATE INDEX IF NOT EXISTS IX_PlateAppearances_Runners ON PlateAppearances(GameId, BeforeSecondRunnerPcode, BeforeThirdRunnerPcode, BeforeFirstRunnerPcode);

        CREATE TABLE IF NOT EXISTS Pitches (
            PitchEventId TEXT PRIMARY KEY NOT NULL,
            SourceEventId TEXT NOT NULL,
            GameId TEXT NOT NULL,
            RelayGroupId TEXT NOT NULL,
            PlateAppearanceId TEXT NULL,
            SourceRelayNo INTEGER NULL,
            SourceSeqNo INTEGER NULL,
            SourceOptionIndex INTEGER NOT NULL,
            ActualPitchIndex INTEGER NOT NULL,
            DisplayPitchNumber INTEGER NULL,
            Inning INTEGER NULL,
            BattingSide INTEGER NOT NULL,
            PitcherPcode TEXT NULL,
            PitcherName TEXT NULL,
            BatterPcode TEXT NULL,
            BatterName TEXT NULL,
            BallsBefore INTEGER NULL,
            StrikesBefore INTEGER NULL,
            BallsAfter INTEGER NULL,
            StrikesAfter INTEGER NULL,
            OutsBefore INTEGER NULL,
            RawPitchResult TEXT NULL,
            PitchResult INTEGER NOT NULL,
            PitchType TEXT NULL,
            SpeedKmh REAL NULL,
            PtsPitchId TEXT NULL,
            HasPtsTracking INTEGER NOT NULL,
            CrossPlateX REAL NULL,
            CrossPlateY REAL NULL,
            CalculatedCrossPlateX REAL NULL,
            CalculatedCrossPlateZ REAL NULL,
            TimeToPlateSeconds REAL NULL,
            TopStrikeZone REAL NULL,
            BottomStrikeZone REAL NULL,
            IsInNominalStrikeZone INTEGER NULL,
            X0 REAL NULL,
            Y0 REAL NULL,
            Z0 REAL NULL,
            Vx0 REAL NULL,
            Vy0 REAL NULL,
            Vz0 REAL NULL,
            Ax REAL NULL,
            Ay REAL NULL,
            Az REAL NULL,
            BatterStance TEXT NULL,
            IsSwing INTEGER NOT NULL,
            IsWhiff INTEGER NOT NULL,
            IsContact INTEGER NOT NULL,
            IsInPlay INTEGER NOT NULL,
            IsCalledStrike INTEGER NOT NULL,
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_Pitches_GameOrder ON Pitches(GameId, ActualPitchIndex);
        CREATE INDEX IF NOT EXISTS IX_Pitches_PlateAppearance ON Pitches(PlateAppearanceId, ActualPitchIndex);
        CREATE INDEX IF NOT EXISTS IX_Pitches_PlateAppearance_Last ON Pitches(PlateAppearanceId, ActualPitchIndex DESC, SourceOptionIndex DESC, PitchEventId DESC);
        CREATE INDEX IF NOT EXISTS IX_Pitches_Pitcher ON Pitches(PitcherPcode, GameId);
        CREATE INDEX IF NOT EXISTS IX_Pitches_Batter ON Pitches(BatterPcode, GameId);
        CREATE INDEX IF NOT EXISTS IX_Pitches_TypeResult ON Pitches(PitchType, PitchResult);
        CREATE INDEX IF NOT EXISTS IX_Pitches_PitcherType ON Pitches(PitcherPcode, PitchType, GameId);
        CREATE INDEX IF NOT EXISTS IX_Pitches_BatterType ON Pitches(BatterPcode, PitchType, GameId);
        CREATE INDEX IF NOT EXISTS IX_Pitches_PitcherZone ON Pitches(PitcherPcode, IsInNominalStrikeZone, GameId);
        CREATE INDEX IF NOT EXISTS IX_Pitches_PitcherPaOrder ON Pitches(PitcherPcode, PlateAppearanceId, ActualPitchIndex);
        CREATE INDEX IF NOT EXISTS IX_Pitches_PlateCount ON Pitches(PlateAppearanceId, BallsBefore, StrikesBefore, ActualPitchIndex);

        CREATE TABLE IF NOT EXISTS RunnerEvents (
            RunnerEventId TEXT PRIMARY KEY NOT NULL,
            SourceEventId TEXT NOT NULL,
            GameId TEXT NOT NULL,
            RelayGroupId TEXT NOT NULL,
            PlateAppearanceId TEXT NULL,
            SourceRelayNo INTEGER NULL,
            SourceSeqNo INTEGER NULL,
            SourceOptionIndex INTEGER NOT NULL,
            Inning INTEGER NULL,
            BattingSide INTEGER NOT NULL,
            TeamCode TEXT NULL,
            RunnerPcode TEXT NULL,
            RunnerName TEXT NULL,
            FromBase INTEGER NULL,
            ToBase INTEGER NULL,
            EventType INTEGER NOT NULL,
            Reason INTEGER NOT NULL,
            IsOut INTEGER NOT NULL,
            IsRun INTEGER NOT NULL,
            WasParsed INTEGER NOT NULL,
            RawText TEXT NULL,
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_RunnerEvents_Game ON RunnerEvents(GameId, SourceRelayNo, SourceOptionIndex);
        CREATE INDEX IF NOT EXISTS IX_RunnerEvents_Runner ON RunnerEvents(RunnerPcode, GameId);
        CREATE INDEX IF NOT EXISTS IX_RunnerEvents_PlateAppearance ON RunnerEvents(PlateAppearanceId, Reason, ToBase);

        CREATE TABLE IF NOT EXISTS PlayerChanges (
            PlayerChangeEventId TEXT PRIMARY KEY NOT NULL,
            SourceEventId TEXT NOT NULL,
            GameId TEXT NOT NULL,
            RelayGroupId TEXT NOT NULL,
            PlateAppearanceId TEXT NULL,
            SourceRelayNo INTEGER NULL,
            SourceSeqNo INTEGER NULL,
            SourceOptionIndex INTEGER NOT NULL,
            Inning INTEGER NULL,
            BattingSide INTEGER NOT NULL,
            ChangedTeamSide INTEGER NOT NULL,
            TeamCode TEXT NULL,
            ChangeType INTEGER NOT NULL,
            RawChangeType TEXT NULL,
            RawText TEXT NULL,
            OutPlayerPcode TEXT NULL,
            OutPlayerName TEXT NULL,
            OutPosition TEXT NULL,
            InPlayerPcode TEXT NULL,
            InPlayerName TEXT NULL,
            InPosition TEXT NULL,
            ShiftPlayerPcode TEXT NULL,
            ShiftPlayerName TEXT NULL,
            OldPosition TEXT NULL,
            NewPosition TEXT NULL,
            SourceOutPlayerTurn INTEGER NULL,
            BatOrder INTEGER NULL,
            IsPitcherChange INTEGER NOT NULL,
            IsPinchHitter INTEGER NOT NULL,
            IsPinchRunner INTEGER NOT NULL,
            WasParsed INTEGER NOT NULL,
            BeforeHomeScore INTEGER NULL,
            BeforeAwayScore INTEGER NULL,
            BeforeOuts INTEGER NULL,
            BeforeFirstRunnerPcode TEXT NULL,
            BeforeSecondRunnerPcode TEXT NULL,
            BeforeThirdRunnerPcode TEXT NULL,
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_PlayerChanges_Game ON PlayerChanges(GameId, SourceRelayNo, SourceOptionIndex);
        CREATE INDEX IF NOT EXISTS IX_PlayerChanges_InPlayer ON PlayerChanges(InPlayerPcode, GameId);
        CREATE INDEX IF NOT EXISTS IX_PlayerChanges_OutPlayer ON PlayerChanges(OutPlayerPcode, GameId);

        CREATE TABLE IF NOT EXISTS AdministrativeEvents (
            AdministrativeEventId TEXT PRIMARY KEY NOT NULL,
            SourceEventId TEXT NOT NULL,
            GameId TEXT NOT NULL,
            RelayGroupId TEXT NOT NULL,
            PlateAppearanceId TEXT NULL,
            SourceRelayNo INTEGER NULL,
            SourceSeqNo INTEGER NULL,
            SourceOptionIndex INTEGER NOT NULL,
            Inning INTEGER NULL,
            BattingSide INTEGER NOT NULL,
            EventType INTEGER NOT NULL,
            RawText TEXT NULL,
            AutomaticBallDelta INTEGER NOT NULL,
            AutomaticStrikeDelta INTEGER NOT NULL,
            ReviewOriginalCall TEXT NULL,
            ReviewFinalCall TEXT NULL,
            ReviewOverturned INTEGER NULL,
            WasRecognized INTEGER NOT NULL,
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_AdministrativeEvents_Game ON AdministrativeEvents(GameId, SourceRelayNo, SourceOptionIndex);

        CREATE TABLE IF NOT EXISTS BattingGameLines (
            GameId TEXT NOT NULL,
            Pcode TEXT NOT NULL,
            TeamCode TEXT NOT NULL,
            LineupSequence INTEGER NOT NULL DEFAULT 0,
            TeamSide INTEGER NOT NULL,
            Name TEXT NULL,
            BatOrder INTEGER NULL,
            Position TEXT NULL,
            BirthDate TEXT NULL,
            Height TEXT NULL,
            Weight TEXT NULL,
            BackNumber TEXT NULL,
            HitType TEXT NULL,
            EnteredAsSubstitute INTEGER NOT NULL,
            LeftGame INTEGER NOT NULL,
            PlateAppearances INTEGER NULL,
            AtBats INTEGER NULL,
            Hits INTEGER NULL,
            HomeRuns INTEGER NULL,
            Walks INTEGER NULL,
            HitByPitch INTEGER NULL,
            Strikeouts INTEGER NULL,
            Runs INTEGER NULL,
            RunsBattedIn INTEGER NULL,
            PRIMARY KEY(GameId, Pcode, TeamCode, LineupSequence),
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_BattingGameLines_Player ON BattingGameLines(Pcode, GameId);

        CREATE TABLE IF NOT EXISTS PitchingGameLines (
            GameId TEXT NOT NULL,
            Pcode TEXT NOT NULL,
            TeamCode TEXT NOT NULL,
            AppearanceSequence INTEGER NOT NULL DEFAULT 0,
            TeamSide INTEGER NOT NULL,
            Name TEXT NULL,
            BirthDate TEXT NULL,
            Height TEXT NULL,
            Weight TEXT NULL,
            BackNumber TEXT NULL,
            HitType TEXT NULL,
            InningsDisplay TEXT NULL,
            InningsOuts INTEGER NOT NULL DEFAULT 0,
            PitchCount INTEGER NULL,
            HitsAllowed INTEGER NULL,
            HomeRunsAllowed INTEGER NULL,
            Walks INTEGER NULL,
            HitBatters INTEGER NULL,
            Strikeouts INTEGER NULL,
            RunsAllowed INTEGER NULL,
            EarnedRuns INTEGER NULL,
            WildPitches INTEGER NULL,
            PRIMARY KEY(GameId, Pcode, TeamCode, AppearanceSequence),
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_PitchingGameLines_Player ON PitchingGameLines(Pcode, GameId);

        CREATE TABLE IF NOT EXISTS Diagnostics (
            DiagnosticId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameId TEXT NOT NULL,
            Severity INTEGER NOT NULL,
            Code TEXT NOT NULL,
            Message TEXT NOT NULL,
            RelayGroupId TEXT NULL,
            EventId TEXT NULL,
            SourceRelayNo INTEGER NULL,
            SourceSeqNo INTEGER NULL,
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_Diagnostics_Game ON Diagnostics(GameId, Severity);

        CREATE TABLE IF NOT EXISTS GamePlayers (
            GameId TEXT NOT NULL,
            Pcode TEXT NOT NULL,
            TeamCode TEXT NOT NULL,
            Name TEXT NOT NULL,
            BirthDate TEXT NULL,
            Position TEXT NULL,
            HitType TEXT NULL,
            IsBatter INTEGER NOT NULL,
            IsPitcher INTEGER NOT NULL,
            BatOrder INTEGER NULL,
            LineupSequence INTEGER NULL,
            SeasonYear INTEGER NULL,
            GameDate TEXT NULL,
            PRIMARY KEY(GameId, Pcode, TeamCode),
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_GamePlayers_PlayerDate ON GamePlayers(Pcode, GameDate);
        CREATE INDEX IF NOT EXISTS IX_GamePlayers_Name ON GamePlayers(Name);

        CREATE TABLE IF NOT EXISTS Players (
            Pcode TEXT PRIMARY KEY NOT NULL,
            Name TEXT NOT NULL,
            BirthDate TEXT NULL,
            LatestTeam TEXT NULL,
            PrimaryPosition TEXT NULL,
            Role TEXT NULL,
            BatsThrows TEXT NULL,
            FirstSeason INTEGER NULL,
            LastSeason INTEGER NULL,
            LastGameDate TEXT NULL,
            LastClubGameDate TEXT NULL,
            UpdatedUtc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Players_Name ON Players(Name);
        CREATE INDEX IF NOT EXISTS IX_Players_LatestTeam ON Players(LatestTeam);

        CREATE TABLE IF NOT EXISTS BatterGameStats (
            GameId TEXT NOT NULL,
            Pcode TEXT NOT NULL,
            TeamCode TEXT NOT NULL,
            Name TEXT NOT NULL,
            PA INTEGER NOT NULL,
            AB INTEGER NOT NULL,
            H INTEGER NOT NULL,
            Singles INTEGER NOT NULL,
            Doubles INTEGER NOT NULL,
            Triples INTEGER NOT NULL,
            HR INTEGER NOT NULL,
            BB INTEGER NOT NULL,
            IBB INTEGER NOT NULL,
            HBP INTEGER NOT NULL,
            SO INTEGER NOT NULL,
            SF INTEGER NOT NULL,
            SH INTEGER NOT NULL,
            GDP INTEGER NOT NULL,
            TB INTEGER NOT NULL,
            Runs INTEGER NOT NULL,
            RBI INTEGER NOT NULL,
            SB INTEGER NOT NULL,
            CS INTEGER NOT NULL,
            OutsRecorded INTEGER NOT NULL,
            RunsScoredOnPlays INTEGER NOT NULL,
            FlyBalls INTEGER NOT NULL,
            WPA REAL NOT NULL,
            Pitches INTEGER NOT NULL,
            Swings INTEGER NOT NULL,
            Contacts INTEGER NOT NULL,
            Whiffs INTEGER NOT NULL,
            CalledStrikes INTEGER NOT NULL,
            CSW INTEGER NOT NULL,
            InZone INTEGER NOT NULL,
            OutZone INTEGER NOT NULL,
            ZoneSwings INTEGER NOT NULL,
            ChaseSwings INTEGER NOT NULL,
            ZoneContacts INTEGER NOT NULL,
            OutZoneContacts INTEGER NOT NULL,
            FirstPitches INTEGER NOT NULL,
            FirstPitchSwings INTEGER NOT NULL,
            CatcherInnings REAL NOT NULL,
            FirstBaseInnings REAL NOT NULL,
            SecondBaseInnings REAL NOT NULL,
            ThirdBaseInnings REAL NOT NULL,
            ShortstopInnings REAL NOT NULL,
            LeftFieldInnings REAL NOT NULL,
            CenterFieldInnings REAL NOT NULL,
            RightFieldInnings REAL NOT NULL,
            DhPa INTEGER NOT NULL,
            PRIMARY KEY(GameId, Pcode, TeamCode),
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_BatterGameStats_Player ON BatterGameStats(Pcode, TeamCode, GameId);
        CREATE INDEX IF NOT EXISTS IX_BatterGameStats_Team ON BatterGameStats(TeamCode, GameId);

        CREATE TABLE IF NOT EXISTS PitcherGameStats (
            GameId TEXT NOT NULL,
            Pcode TEXT NOT NULL,
            TeamCode TEXT NOT NULL,
            Name TEXT NOT NULL,
            TBF INTEGER NOT NULL,
            PaOuts INTEGER NOT NULL,
            PaHits INTEGER NOT NULL,
            PaHR INTEGER NOT NULL,
            PaBB INTEGER NOT NULL,
            PaHBP INTEGER NOT NULL,
            PaSO INTEGER NOT NULL,
            SF INTEGER NOT NULL,
            PaRuns INTEGER NOT NULL,
            FlyBalls INTEGER NOT NULL,
            IFFB INTEGER NOT NULL,
            Pitches INTEGER NOT NULL,
            Swings INTEGER NOT NULL,
            Contacts INTEGER NOT NULL,
            Whiffs INTEGER NOT NULL,
            CalledStrikes INTEGER NOT NULL,
            CSW INTEGER NOT NULL,
            InZone INTEGER NOT NULL,
            OutZone INTEGER NOT NULL,
            ZoneSwings INTEGER NOT NULL,
            ChaseSwings INTEGER NOT NULL,
            ZoneContacts INTEGER NOT NULL,
            OutZoneContacts INTEGER NOT NULL,
            FirstPitches INTEGER NOT NULL,
            FirstPitchSwings INTEGER NOT NULL,
            SpeedSum REAL NOT NULL,
            SpeedCount INTEGER NOT NULL,
            HasFinalLine INTEGER NOT NULL,
            AppearanceSequence INTEGER NOT NULL,
            IsStarter INTEGER NOT NULL,
            IsReliever INTEGER NOT NULL,
            InningsOuts INTEGER NOT NULL,
            HitsAllowed INTEGER NOT NULL,
            HomeRunsAllowed INTEGER NOT NULL,
            FinalBB INTEGER NOT NULL,
            FinalHBP INTEGER NOT NULL,
            FinalSO INTEGER NOT NULL,
            RunsAllowed INTEGER NOT NULL,
            EarnedRuns INTEGER NOT NULL,
            WildPitches INTEGER NOT NULL,
            FinalPitchCount INTEGER NOT NULL,
            EntryAbsoluteWpaSum REAL NOT NULL,
            EntryWpaCount INTEGER NOT NULL,
            PRIMARY KEY(GameId, Pcode, TeamCode),
            FOREIGN KEY(GameId) REFERENCES Games(GameId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_PitcherGameStats_Player ON PitcherGameStats(Pcode, TeamCode, GameId);
        CREATE INDEX IF NOT EXISTS IX_PitcherGameStats_Team ON PitcherGameStats(TeamCode, GameId);

        CREATE TABLE IF NOT EXISTS ComputedCache (
            CacheKey TEXT PRIMARY KEY NOT NULL,
            SourceVersion TEXT NOT NULL,
            JsonValue TEXT NOT NULL,
            UpdatedUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS LeagueConstants (
            Metric TEXT PRIMARY KEY NOT NULL,
            Value REAL NULL,
            Description TEXT NOT NULL,
            SourceVersion TEXT NOT NULL,
            UpdatedUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ParkFactors (
            Stadium TEXT PRIMARY KEY NOT NULL,
            Seasons TEXT NOT NULL,
            Games INTEGER NOT NULL,
            Innings REAL NULL,
            HomeRuns INTEGER NOT NULL,
            Walks INTEGER NOT NULL,
            HitBatters INTEGER NOT NULL,
            Strikeouts INTEGER NOT NULL,
            InfieldFlies INTEGER NOT NULL,
            RawFipFactor REAL NULL,
            UsedFipFactor REAL NULL,
            Confidence TEXT NOT NULL,
            SourceVersion TEXT NOT NULL,
            UpdatedUtc TEXT NOT NULL
        );
        """;
}
