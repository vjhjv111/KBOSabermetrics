using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Parsing;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

/// <summary>
/// 관계형 테이블에서 정규화 객체를 재구성하는 호환 계층입니다.
/// 정규화 JSON 내보내기처럼 객체 형식이 꼭 필요한 작업에서만 사용하며,
/// 통계·탭·선수 페이지 조회 경로에서는 호출하지 않습니다.
/// </summary>
public sealed partial class DatabaseCacheService
{
    public async Task ForEachGameAsync(
        GameQuery query,
        GameDataProjection projection,
        Func<NormalizedGame, CancellationToken, Task> action,
        IProgress<DatabaseLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var ids = await GetFilteredGameIdsAsync(query, cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < ids.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var game = await LoadGameByIdAsync(ids[index], projection, cancellationToken).ConfigureAwait(false);
            if (game is null) continue;
            progress?.Report(new DatabaseLoadProgress(index + 1, ids.Count,
                $"{game.GameId} 관계형 테이블에서 재구성 중"));
            await action(game, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<NormalizedGame>> LoadGamesAsync(
        GameQuery query,
        GameDataProjection projection,
        CancellationToken cancellationToken = default)
    {
        var result = new List<NormalizedGame>();
        await ForEachGameAsync(query, projection, (game, _) =>
        {
            result.Add(game);
            return Task.CompletedTask;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<NormalizedGame>> LoadGamesByIdsAsync(
        IEnumerable<string> gameIds,
        GameDataProjection projection,
        CancellationToken cancellationToken = default)
    {
        var result = new List<NormalizedGame>();
        foreach (var gameId in gameIds.Distinct(StringComparer.Ordinal))
        {
            var game = await LoadGameByIdAsync(gameId, projection, cancellationToken).ConfigureAwait(false);
            if (game is not null) result.Add(game);
        }
        return result;
    }

    private async Task<List<string>> GetFilteredGameIdsAsync(GameQuery query, CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var result = new List<string>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{filter.Cte} SELECT GameId FROM FilteredGames ORDER BY GameDate, GameId;";
        AddParameters(command, filter.Parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(reader.GetString(0));
        return result;
    }

    private async Task<NormalizedGame?> LoadGameByIdAsync(
        string gameId,
        GameDataProjection projection,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var game = await LoadGameMetadataAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (game is null) return null;

        if (projection.HasFlag(GameDataProjection.Summary))
            game.Summary = await LoadSummaryAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (projection.HasFlag(GameDataProjection.RelayGroups))
            game.RelayGroups = await LoadRelayGroupsAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (projection.HasFlag(GameDataProjection.Events))
            game.Events = await LoadEventsAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (projection.HasFlag(GameDataProjection.PlateAppearances))
            game.PlateAppearances = await LoadPlateAppearancesAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (projection.HasFlag(GameDataProjection.PitchEvents))
            game.PitchEvents = await LoadPitchesAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (projection.HasFlag(GameDataProjection.RunnerEvents))
            game.RunnerEvents = await LoadRunnersAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (projection.HasFlag(GameDataProjection.PlayerChanges))
            game.PlayerChanges = await LoadChangesAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (projection.HasFlag(GameDataProjection.AdministrativeEvents))
            game.AdministrativeEvents = await LoadAdministrativeAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (projection.HasFlag(GameDataProjection.BattingLines))
            game.BattingLines = await LoadBattingLinesAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (projection.HasFlag(GameDataProjection.PitchingLines))
            game.PitchingLines = await LoadPitchingLinesAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
        if (projection.HasFlag(GameDataProjection.Diagnostics))
            game.Diagnostics = await LoadDiagnosticsAsync(connection, gameId, cancellationToken).ConfigureAwait(false);

        LinkRehydratedObjects(game);
        return game;
    }

    private static async Task<NormalizedGame?> LoadGameMetadataAsync(
        SqliteConnection connection,
        string gameId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GameId, SeasonYear, GameDate, GameDateTime, SuperCategoryId, UpperCategoryId,
                   UpperCategoryName, CategoryId, CategoryName, RoundCode, CompetitionType, Stadium,
                   StatusCode, Winner, AwayTeamCode, AwayTeamName, AwayScore, AwayHits, AwayErrors,
                   AwayWalks, HomeTeamCode, HomeTeamName, HomeScore, HomeHits, HomeErrors, HomeWalks,
                   LastHomeWinRate, LastAwayWinRate, LastWpaByPlate
            FROM Games WHERE GameId=$gameId LIMIT 1;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new NormalizedGame
        {
            GameId = reader.GetString(0), SeasonYear = NullableInt(reader, 1),
            GameDate = NullableString(reader, 2), GameDateTime = NullableString(reader, 3),
            SuperCategoryId = NullableString(reader, 4), UpperCategoryId = NullableString(reader, 5),
            UpperCategoryName = NullableString(reader, 6), CategoryId = NullableString(reader, 7),
            CategoryName = NullableString(reader, 8), RoundCode = NullableString(reader, 9),
            CompetitionType = (GameCompetitionType)ReadInt32(reader, 10), Stadium = NullableString(reader, 11),
            StatusCode = NullableString(reader, 12), Winner = NullableString(reader, 13),
            AwayTeam = new TeamMetadata
            {
                Side = TeamSide.Away, TeamCode = NullableString(reader, 14), TeamName = NullableString(reader, 15),
                FinalScore = NullableInt(reader, 16), FinalHits = NullableInt(reader, 17),
                FinalErrors = NullableInt(reader, 18), FinalWalks = NullableInt(reader, 19),
            },
            HomeTeam = new TeamMetadata
            {
                Side = TeamSide.Home, TeamCode = NullableString(reader, 20), TeamName = NullableString(reader, 21),
                FinalScore = NullableInt(reader, 22), FinalHits = NullableInt(reader, 23),
                FinalErrors = NullableInt(reader, 24), FinalWalks = NullableInt(reader, 25),
            },
            LastValidHomeWinRate = NullableDouble(reader, 26),
            LastValidAwayWinRate = NullableDouble(reader, 27),
            LastValidWpaByPlate = NullableDouble(reader, 28),
        };
    }

    private static async Task<ParserSummary> LoadSummaryAsync(
        SqliteConnection connection,
        string gameId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM GameSummaries WHERE GameId=$gameId LIMIT 1;";
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new ParserSummary();
        var i = 1;
        return new ParserSummary
        {
            RawRelayGroupCount = ReadInt32(reader, i++), RawEventCount = ReadInt32(reader, i++),
            RawPitchEventCount = ReadInt32(reader, i++), RawPtsCount = ReadInt32(reader, i++),
            DuplicateSourceSeqNoOccurrenceCount = ReadInt32(reader, i++),
            CompletedPlateAppearanceCount = ReadInt32(reader, i++),
            InterruptedPlateAppearanceCount = ReadInt32(reader, i++),
            PrePlateSubstitutionGroupCount = ReadInt32(reader, i++),
            InningMarkerGroupCount = ReadInt32(reader, i++), GameSummaryGroupCount = ReadInt32(reader, i++),
            PitchEventCount = ReadInt32(reader, i++), PtsMatchedPitchCount = ReadInt32(reader, i++),
            PtsMissingPitchCount = ReadInt32(reader, i++), RunnerEventCount = ReadInt32(reader, i++),
            PlayerChangeEventCount = ReadInt32(reader, i++), AdministrativeEventCount = ReadInt32(reader, i++),
            UnknownRelayGroupCount = ReadInt32(reader, i++), UnknownRawEventTypeCount = ReadInt32(reader, i++),
            UnknownBattingResultCount = ReadInt32(reader, i++), UnparsedRunnerEventCount = ReadInt32(reader, i++),
            UnparsedPlayerChangeCount = ReadInt32(reader, i++), UnknownAdministrativeEventCount = ReadInt32(reader, i++),
            PtsCalculationFailureCount = ReadInt32(reader, i++), FinalLineBattingMismatchCount = ReadInt32(reader, i++),
            WarningCount = ReadInt32(reader, i++), ErrorCount = ReadInt32(reader, i++),
        };
    }

    private static async Task<List<RelayGroup>> LoadRelayGroupsAsync(
        SqliteConnection connection, string gameId, CancellationToken cancellationToken)
    {
        var rows = new List<RelayGroup>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT RelayGroupId, GameId, ChronologicalIndex, SourceRelayNo, Title, TitleStyle,
                   Inning, RawHomeOrAway, BattingSide, BattingTeamCode, SourceStatusCode, GroupType,
                   PlateAppearanceId, HomeWinRateAfter, AwayWinRateAfter, WpaByPlate,
                   BeforeHomeScore, BeforeAwayScore, BeforeOuts,
                   BeforeFirstRunnerPcode, BeforeFirstRunnerName, BeforeSecondRunnerPcode,
                   BeforeSecondRunnerName, BeforeThirdRunnerPcode, BeforeThirdRunnerName,
                   AfterHomeScore, AfterAwayScore, AfterOuts
            FROM RelayGroups WHERE GameId=$gameId ORDER BY ChronologicalIndex;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new RelayGroup
            {
                RelayGroupId = reader.GetString(0), GameId = reader.GetString(1),
                ChronologicalIndex = ReadInt32(reader, 2), SourceRelayNo = NullableInt(reader, 3),
                Title = NullableString(reader, 4), TitleStyle = NullableString(reader, 5),
                Inning = NullableInt(reader, 6), RawHomeOrAway = NullableString(reader, 7),
                BattingSide = (TeamSide)ReadInt32(reader, 8), BattingTeamCode = NullableString(reader, 9),
                SourceStatusCode = NullableInt(reader, 10), GroupType = (RelayGroupType)ReadInt32(reader, 11),
                PlateAppearanceId = NullableString(reader, 12), HomeWinRateAfter = NullableDouble(reader, 13),
                AwayWinRateAfter = NullableDouble(reader, 14), WpaByPlate = NullableDouble(reader, 15),
                StateBefore = ReadState(reader, 16, includeRunnerNames: true),
                StateAfter = new GameStateSnapshot
                {
                    HomeScore = NullableInt(reader, 25), AwayScore = NullableInt(reader, 26), Outs = NullableInt(reader, 27),
                },
            });
        }
        return rows;
    }

    private static async Task<List<NormalizedEvent>> LoadEventsAsync(
        SqliteConnection connection, string gameId, CancellationToken cancellationToken)
    {
        var rows = new List<NormalizedEvent>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EventId, GameId, RelayGroupId, PlateAppearanceId, ChronologicalIndex,
                   SourceRelayNo, SourceOptionIndex, SourceSeqNo, IsDuplicateSourceSeqNo,
                   RawType, EventType, RawText, RawStuff, BattingSide, Inning
            FROM NormalizedEvents WHERE GameId=$gameId ORDER BY ChronologicalIndex;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new NormalizedEvent
            {
                EventId = reader.GetString(0), GameId = reader.GetString(1), RelayGroupId = reader.GetString(2),
                PlateAppearanceId = NullableString(reader, 3), ChronologicalIndex = ReadInt32(reader, 4),
                SourceRelayNo = NullableInt(reader, 5), SourceOptionIndex = ReadInt32(reader, 6),
                SourceSeqNo = NullableInt(reader, 7), IsDuplicateSourceSeqNo = ReadInt32(reader, 8) != 0,
                RawType = NullableInt(reader, 9), EventType = (NormalizedEventType)ReadInt32(reader, 10),
                RawText = NullableString(reader, 11), RawStuff = NullableString(reader, 12),
                BattingSide = (TeamSide)ReadInt32(reader, 13), Inning = NullableInt(reader, 14),
            });
        }
        return rows;
    }

    private static async Task<List<PlateAppearance>> LoadPlateAppearancesAsync(
        SqliteConnection connection, string gameId, CancellationToken cancellationToken)
    {
        var rows = new List<PlateAppearance>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PlateAppearanceId, GameId, RelayGroupId, SequenceNumber, OfficialSequenceNumber,
                   SourceRelayNo, Inning, RawHomeOrAway, BattingSide, BattingTeamCode, FieldingTeamCode,
                   Status, IsOfficial, StartEventId, ResultEventId, BatterPcode, BatterName, BatOrder,
                   PitcherPcode, PitcherName, FinalPitcherPcode, FinalPitcherName, ResultRawType,
                   ResultText, ResultType, NormalizedResultText, BattedBallType, FieldDirection,
                   PrimaryFielder, HomeRunDistanceMeters, CountsAsAtBat, IsHit, IsOut, IsSacrifice,
                   IsWalk, IsIntentionalWalk, IsStrikeout, ReachedBase, TotalBases, WasRecognized,
                   RunsScored, OutsRecorded, ActualPitchCount, MaximumDisplayPitchNumber,
                   HomeWinRateAfter, AwayWinRateAfter, WpaByPlate,
                   BeforeHomeScore, BeforeAwayScore, BeforeOuts,
                   BeforeFirstRunnerPcode, BeforeFirstRunnerName, BeforeSecondRunnerPcode,
                   BeforeSecondRunnerName, BeforeThirdRunnerPcode, BeforeThirdRunnerName,
                   AfterHomeScore, AfterAwayScore, AfterOuts
            FROM PlateAppearances WHERE GameId=$gameId ORDER BY SequenceNumber;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            var row = new PlateAppearance
            {
                PlateAppearanceId = reader.GetString(i++), GameId = reader.GetString(i++),
                RelayGroupId = reader.GetString(i++), SequenceNumber = ReadInt32(reader, i++),
                OfficialSequenceNumber = NullableInt(reader, i++), SourceRelayNo = NullableInt(reader, i++),
                Inning = NullableInt(reader, i++), RawHomeOrAway = NullableString(reader, i++),
                BattingSide = (TeamSide)ReadInt32(reader, i++), BattingTeamCode = NullableString(reader, i++),
                FieldingTeamCode = NullableString(reader, i++), Status = (PlateAppearanceStatus)ReadInt32(reader, i++),
                IsOfficialPlateAppearance = ReadInt32(reader, i++) != 0, StartEventId = NullableString(reader, i++),
                ResultEventId = NullableString(reader, i++), BatterPcode = NullableString(reader, i++),
                BatterName = NullableString(reader, i++), BatOrder = NullableInt(reader, i++),
                PitcherPcode = NullableString(reader, i++), PitcherName = NullableString(reader, i++),
                FinalPitcherPcode = NullableString(reader, i++), FinalPitcherName = NullableString(reader, i++),
                ResultRawType = NullableInt(reader, i++), ResultText = NullableString(reader, i++),
            };
            row.Outcome = new BattingOutcome
            {
                ResultType = (BattingResultType)ReadInt32(reader, i++),
                NormalizedResultText = NullableString(reader, i++),
                BattedBallType = (BattedBallType)ReadInt32(reader, i++),
                FieldDirection = (FieldDirection)ReadInt32(reader, i++),
                PrimaryFielder = NullableString(reader, i++),
                HomeRunDistanceMeters = NullableInt(reader, i++),
                CountsAsAtBat = ReadInt32(reader, i++) != 0,
                IsHit = ReadInt32(reader, i++) != 0,
                IsOut = ReadInt32(reader, i++) != 0,
                IsSacrifice = ReadInt32(reader, i++) != 0,
                IsWalk = ReadInt32(reader, i++) != 0,
                IsIntentionalWalk = ReadInt32(reader, i++) != 0,
                IsStrikeout = ReadInt32(reader, i++) != 0,
                ReachedBase = ReadInt32(reader, i++) != 0,
                TotalBases = ReadInt32(reader, i++),
                WasRecognized = ReadInt32(reader, i++) != 0,
            };
            row.RunsScored = ReadInt32(reader, i++);
            row.OutsRecorded = ReadInt32(reader, i++);
            row.ActualPitchCount = ReadInt32(reader, i++);
            row.MaximumDisplayPitchNumber = NullableInt(reader, i++);
            row.HomeWinRateAfter = NullableDouble(reader, i++);
            row.AwayWinRateAfter = NullableDouble(reader, i++);
            row.WpaByPlate = NullableDouble(reader, i++);
            row.StateBefore = ReadState(reader, i, includeRunnerNames: true);
            i += 9;
            row.StateAfter = new GameStateSnapshot
            {
                HomeScore = NullableInt(reader, i++), AwayScore = NullableInt(reader, i++), Outs = NullableInt(reader, i++),
            };
            rows.Add(row);
        }
        return rows;
    }

    private static async Task<List<PitchEvent>> LoadPitchesAsync(
        SqliteConnection connection, string gameId, CancellationToken cancellationToken)
    {
        var rows = new List<PitchEvent>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PitchEventId, SourceEventId, GameId, RelayGroupId, PlateAppearanceId,
                   SourceRelayNo, SourceSeqNo, SourceOptionIndex, ActualPitchIndex, DisplayPitchNumber,
                   Inning, BattingSide, PitcherPcode, PitcherName, BatterPcode, BatterName,
                   BallsBefore, StrikesBefore, BallsAfter, StrikesAfter, OutsBefore,
                   RawPitchResult, PitchResult, PitchType, SpeedKmh, PtsPitchId, HasPtsTracking,
                   CrossPlateX, CrossPlateY, CalculatedCrossPlateX, CalculatedCrossPlateZ,
                   TimeToPlateSeconds, TopStrikeZone, BottomStrikeZone, IsInNominalStrikeZone,
                   X0, Y0, Z0, Vx0, Vy0, Vz0, Ax, Ay, Az, BatterStance,
                   IsSwing, IsWhiff, IsContact, IsInPlay, IsCalledStrike
            FROM Pitches WHERE GameId=$gameId ORDER BY ActualPitchIndex;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            rows.Add(new PitchEvent
            {
                PitchEventId = reader.GetString(i++), SourceEventId = reader.GetString(i++),
                GameId = reader.GetString(i++), RelayGroupId = reader.GetString(i++),
                PlateAppearanceId = NullableString(reader, i++), SourceRelayNo = NullableInt(reader, i++),
                SourceSeqNo = NullableInt(reader, i++), SourceOptionIndex = ReadInt32(reader, i++),
                ActualPitchIndex = ReadInt32(reader, i++), DisplayPitchNumber = NullableInt(reader, i++),
                Inning = NullableInt(reader, i++), BattingSide = (TeamSide)ReadInt32(reader, i++),
                PitcherPcode = NullableString(reader, i++), PitcherName = NullableString(reader, i++),
                BatterPcode = NullableString(reader, i++), BatterName = NullableString(reader, i++),
                BallsBefore = NullableInt(reader, i++), StrikesBefore = NullableInt(reader, i++),
                BallsAfter = NullableInt(reader, i++), StrikesAfter = NullableInt(reader, i++),
                OutsBefore = NullableInt(reader, i++), RawPitchResult = NullableString(reader, i++),
                PitchResult = (PitchResultType)ReadInt32(reader, i++), PitchType = NullableString(reader, i++),
                SpeedKmh = NullableDouble(reader, i++), PtsPitchId = NullableString(reader, i++),
                HasPtsTracking = ReadInt32(reader, i++) != 0, CrossPlateX = NullableDouble(reader, i++),
                CrossPlateY = NullableDouble(reader, i++), CalculatedCrossPlateX = NullableDouble(reader, i++),
                CalculatedCrossPlateZ = NullableDouble(reader, i++), TimeToPlateSeconds = NullableDouble(reader, i++),
                TopStrikeZone = NullableDouble(reader, i++), BottomStrikeZone = NullableDouble(reader, i++),
                IsInNominalStrikeZone = NullableBoolean(reader, i++), X0 = NullableDouble(reader, i++),
                Y0 = NullableDouble(reader, i++), Z0 = NullableDouble(reader, i++),
                Vx0 = NullableDouble(reader, i++), Vy0 = NullableDouble(reader, i++),
                Vz0 = NullableDouble(reader, i++), Ax = NullableDouble(reader, i++),
                Ay = NullableDouble(reader, i++), Az = NullableDouble(reader, i++),
                BatterStance = NullableString(reader, i++), IsSwing = ReadInt32(reader, i++) != 0,
                IsWhiff = ReadInt32(reader, i++) != 0, IsContact = ReadInt32(reader, i++) != 0,
                IsInPlay = ReadInt32(reader, i++) != 0, IsCalledStrike = ReadInt32(reader, i++) != 0,
            });
        }
        return rows;
    }

    private static async Task<List<RunnerEvent>> LoadRunnersAsync(
        SqliteConnection connection, string gameId, CancellationToken cancellationToken)
    {
        var rows = new List<RunnerEvent>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT RunnerEventId, SourceEventId, GameId, RelayGroupId, PlateAppearanceId,
                   SourceRelayNo, SourceSeqNo, SourceOptionIndex, Inning, BattingSide, TeamCode,
                   RunnerPcode, RunnerName, FromBase, ToBase, EventType, Reason, IsOut, IsRun,
                   WasParsed, RawText
            FROM RunnerEvents WHERE GameId=$gameId ORDER BY SourceRelayNo, SourceOptionIndex;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            rows.Add(new RunnerEvent
            {
                RunnerEventId = reader.GetString(i++), SourceEventId = reader.GetString(i++),
                GameId = reader.GetString(i++), RelayGroupId = reader.GetString(i++),
                PlateAppearanceId = NullableString(reader, i++), SourceRelayNo = NullableInt(reader, i++),
                SourceSeqNo = NullableInt(reader, i++), SourceOptionIndex = ReadInt32(reader, i++),
                Inning = NullableInt(reader, i++), BattingSide = (TeamSide)ReadInt32(reader, i++),
                TeamCode = NullableString(reader, i++), RunnerPcode = NullableString(reader, i++),
                RunnerName = NullableString(reader, i++), FromBase = NullableInt(reader, i++),
                ToBase = NullableInt(reader, i++), EventType = (RunnerEventType)ReadInt32(reader, i++),
                Reason = (RunnerAdvanceReason)ReadInt32(reader, i++), IsOut = ReadInt32(reader, i++) != 0,
                IsRun = ReadInt32(reader, i++) != 0, WasParsed = ReadInt32(reader, i++) != 0,
                RawText = NullableString(reader, i++),
            });
        }
        return rows;
    }

    private static async Task<List<PlayerChangeEvent>> LoadChangesAsync(
        SqliteConnection connection, string gameId, CancellationToken cancellationToken)
    {
        var rows = new List<PlayerChangeEvent>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PlayerChangeEventId, SourceEventId, GameId, RelayGroupId, PlateAppearanceId,
                   SourceRelayNo, SourceSeqNo, SourceOptionIndex, Inning, BattingSide, ChangedTeamSide,
                   TeamCode, ChangeType, RawChangeType, RawText, OutPlayerPcode, OutPlayerName,
                   OutPosition, InPlayerPcode, InPlayerName, InPosition, ShiftPlayerPcode,
                   ShiftPlayerName, OldPosition, NewPosition, SourceOutPlayerTurn, BatOrder,
                   IsPitcherChange, IsPinchHitter, IsPinchRunner, WasParsed,
                   BeforeHomeScore, BeforeAwayScore, BeforeOuts,
                   BeforeFirstRunnerPcode, BeforeSecondRunnerPcode, BeforeThirdRunnerPcode
            FROM PlayerChanges WHERE GameId=$gameId ORDER BY SourceRelayNo, SourceOptionIndex;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            rows.Add(new PlayerChangeEvent
            {
                PlayerChangeEventId = reader.GetString(i++), SourceEventId = reader.GetString(i++),
                GameId = reader.GetString(i++), RelayGroupId = reader.GetString(i++),
                PlateAppearanceId = NullableString(reader, i++), SourceRelayNo = NullableInt(reader, i++),
                SourceSeqNo = NullableInt(reader, i++), SourceOptionIndex = ReadInt32(reader, i++),
                Inning = NullableInt(reader, i++), BattingSide = (TeamSide)ReadInt32(reader, i++),
                ChangedTeamSide = (TeamSide)ReadInt32(reader, i++), TeamCode = NullableString(reader, i++),
                ChangeType = (PlayerChangeType)ReadInt32(reader, i++), RawChangeType = NullableString(reader, i++),
                RawText = NullableString(reader, i++), OutPlayerPcode = NullableString(reader, i++),
                OutPlayerName = NullableString(reader, i++), OutPosition = NullableString(reader, i++),
                InPlayerPcode = NullableString(reader, i++), InPlayerName = NullableString(reader, i++),
                InPosition = NullableString(reader, i++), ShiftPlayerPcode = NullableString(reader, i++),
                ShiftPlayerName = NullableString(reader, i++), OldPosition = NullableString(reader, i++),
                NewPosition = NullableString(reader, i++), SourceOutPlayerTurn = NullableInt(reader, i++),
                BatOrder = NullableInt(reader, i++), IsPitcherChange = ReadInt32(reader, i++) != 0,
                IsPinchHitter = ReadInt32(reader, i++) != 0, IsPinchRunner = ReadInt32(reader, i++) != 0,
                WasParsed = ReadInt32(reader, i++) != 0,
                StateBefore = new GameStateSnapshot
                {
                    HomeScore = NullableInt(reader, i++), AwayScore = NullableInt(reader, i++),
                    Outs = NullableInt(reader, i++), FirstBaseRunnerPcode = NullableString(reader, i++),
                    SecondBaseRunnerPcode = NullableString(reader, i++), ThirdBaseRunnerPcode = NullableString(reader, i++),
                },
            });
        }
        return rows;
    }

    private static async Task<List<AdministrativeEvent>> LoadAdministrativeAsync(
        SqliteConnection connection, string gameId, CancellationToken cancellationToken)
    {
        var rows = new List<AdministrativeEvent>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AdministrativeEventId, SourceEventId, GameId, RelayGroupId, PlateAppearanceId,
                   SourceRelayNo, SourceSeqNo, SourceOptionIndex, Inning, BattingSide, EventType,
                   RawText, AutomaticBallDelta, AutomaticStrikeDelta, ReviewOriginalCall,
                   ReviewFinalCall, ReviewOverturned, WasRecognized
            FROM AdministrativeEvents WHERE GameId=$gameId ORDER BY SourceRelayNo, SourceOptionIndex;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            rows.Add(new AdministrativeEvent
            {
                AdministrativeEventId = reader.GetString(i++), SourceEventId = reader.GetString(i++),
                GameId = reader.GetString(i++), RelayGroupId = reader.GetString(i++),
                PlateAppearanceId = NullableString(reader, i++), SourceRelayNo = NullableInt(reader, i++),
                SourceSeqNo = NullableInt(reader, i++), SourceOptionIndex = ReadInt32(reader, i++),
                Inning = NullableInt(reader, i++), BattingSide = (TeamSide)ReadInt32(reader, i++),
                EventType = (AdministrativeEventType)ReadInt32(reader, i++), RawText = NullableString(reader, i++),
                AutomaticBallDelta = ReadInt32(reader, i++), AutomaticStrikeDelta = ReadInt32(reader, i++),
                ReviewOriginalCall = NullableString(reader, i++), ReviewFinalCall = NullableString(reader, i++),
                ReviewOverturned = NullableBoolean(reader, i++), WasRecognized = ReadInt32(reader, i++) != 0,
            });
        }
        return rows;
    }

    private static async Task<List<GamePlayerBattingLine>> LoadBattingLinesAsync(
        SqliteConnection connection, string gameId, CancellationToken cancellationToken)
    {
        var rows = new List<GamePlayerBattingLine>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GameId, Pcode, TeamCode, LineupSequence, TeamSide, Name, BatOrder, Position,
                   BirthDate, Height, Weight, BackNumber, HitType, EnteredAsSubstitute, LeftGame,
                   PlateAppearances, AtBats, Hits, HomeRuns, Walks, HitByPitch, Strikeouts, Runs, RunsBattedIn
            FROM BattingGameLines WHERE GameId=$gameId ORDER BY TeamSide, BatOrder, LineupSequence;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            rows.Add(new GamePlayerBattingLine
            {
                GameId = reader.GetString(i++), Pcode = NullableString(reader, i++),
                TeamCode = NullableString(reader, i++), LineupSequence = NullableInt(reader, i++),
                TeamSide = (TeamSide)ReadInt32(reader, i++), Name = NullableString(reader, i++),
                BatOrder = NullableInt(reader, i++), Position = NullableString(reader, i++),
                BirthDateRaw = NullableString(reader, i++), Height = NullableString(reader, i++),
                Weight = NullableString(reader, i++), BackNumber = NullableString(reader, i++),
                HitType = NullableString(reader, i++), EnteredAsSubstitute = ReadInt32(reader, i++) != 0,
                LeftGame = ReadInt32(reader, i++) != 0, PlateAppearances = NullableInt(reader, i++),
                AtBats = NullableInt(reader, i++), Hits = NullableInt(reader, i++),
                HomeRuns = NullableInt(reader, i++), Walks = NullableInt(reader, i++),
                HitByPitch = NullableInt(reader, i++), Strikeouts = NullableInt(reader, i++),
                Runs = NullableInt(reader, i++), RunsBattedIn = NullableInt(reader, i++),
            });
        }
        return rows;
    }

    private static async Task<List<GamePlayerPitchingLine>> LoadPitchingLinesAsync(
        SqliteConnection connection, string gameId, CancellationToken cancellationToken)
    {
        var rows = new List<GamePlayerPitchingLine>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GameId, Pcode, TeamCode, AppearanceSequence, TeamSide, Name, BirthDate,
                   Height, Weight, BackNumber, HitType, InningsDisplay, PitchCount, HitsAllowed,
                   HomeRunsAllowed, Walks, HitBatters, Strikeouts, RunsAllowed, EarnedRuns, WildPitches
            FROM PitchingGameLines WHERE GameId=$gameId ORDER BY TeamSide, AppearanceSequence;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            rows.Add(new GamePlayerPitchingLine
            {
                GameId = reader.GetString(i++), Pcode = NullableString(reader, i++),
                TeamCode = NullableString(reader, i++), AppearanceSequence = NullableInt(reader, i++),
                TeamSide = (TeamSide)ReadInt32(reader, i++), Name = NullableString(reader, i++),
                BirthDateRaw = NullableString(reader, i++), Height = NullableString(reader, i++),
                Weight = NullableString(reader, i++), BackNumber = NullableString(reader, i++),
                HitType = NullableString(reader, i++), InningsDisplay = NullableString(reader, i++),
                PitchCount = NullableInt(reader, i++), HitsAllowed = NullableInt(reader, i++),
                HomeRunsAllowed = NullableInt(reader, i++), Walks = NullableInt(reader, i++),
                HitBatters = NullableInt(reader, i++), Strikeouts = NullableInt(reader, i++),
                RunsAllowed = NullableInt(reader, i++), EarnedRuns = NullableInt(reader, i++),
                WildPitches = NullableInt(reader, i++),
            });
        }
        return rows;
    }

    private static async Task<List<ParserDiagnostic>> LoadDiagnosticsAsync(
        SqliteConnection connection, string gameId, CancellationToken cancellationToken)
    {
        var rows = new List<ParserDiagnostic>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Severity, Code, Message, GameId, RelayGroupId, EventId, SourceRelayNo, SourceSeqNo
            FROM Diagnostics WHERE GameId=$gameId ORDER BY DiagnosticId;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new ParserDiagnostic
            {
                Severity = (DiagnosticSeverity)ReadInt32(reader, 0), Code = reader.GetString(1),
                Message = reader.GetString(2), GameId = NullableString(reader, 3),
                RelayGroupId = NullableString(reader, 4), EventId = NullableString(reader, 5),
                SourceRelayNo = NullableInt(reader, 6), SourceSeqNo = NullableInt(reader, 7),
            });
        }
        return rows;
    }

    private static void LinkRehydratedObjects(NormalizedGame game)
    {
        var groups = game.RelayGroups.ToDictionary(group => group.RelayGroupId, StringComparer.Ordinal);
        foreach (var item in game.Events)
            if (groups.TryGetValue(item.RelayGroupId, out var group)) group.EventIds.Add(item.EventId);

        var pas = game.PlateAppearances.ToDictionary(pa => pa.PlateAppearanceId, StringComparer.Ordinal);
        foreach (var pitch in game.PitchEvents)
            if (!string.IsNullOrWhiteSpace(pitch.PlateAppearanceId) && pas.TryGetValue(pitch.PlateAppearanceId, out var pa))
                pa.PitchEventIds.Add(pitch.PitchEventId);
        foreach (var runner in game.RunnerEvents)
            if (!string.IsNullOrWhiteSpace(runner.PlateAppearanceId) && pas.TryGetValue(runner.PlateAppearanceId, out var pa))
                pa.RunnerEventIds.Add(runner.RunnerEventId);
        foreach (var change in game.PlayerChanges)
            if (!string.IsNullOrWhiteSpace(change.PlateAppearanceId) && pas.TryGetValue(change.PlateAppearanceId, out var pa))
                pa.PlayerChangeEventIds.Add(change.PlayerChangeEventId);
        foreach (var admin in game.AdministrativeEvents)
            if (!string.IsNullOrWhiteSpace(admin.PlateAppearanceId) && pas.TryGetValue(admin.PlateAppearanceId, out var pa))
                pa.AdministrativeEventIds.Add(admin.AdministrativeEventId);
    }

    private static GameStateSnapshot ReadState(SqliteDataReader reader, int start, bool includeRunnerNames)
    {
        var state = new GameStateSnapshot
        {
            HomeScore = NullableInt(reader, start), AwayScore = NullableInt(reader, start + 1),
            Outs = NullableInt(reader, start + 2), FirstBaseRunnerPcode = NullableString(reader, start + 3),
        };
        if (includeRunnerNames)
        {
            state.FirstBaseRunnerName = NullableString(reader, start + 4);
            state.SecondBaseRunnerPcode = NullableString(reader, start + 5);
            state.SecondBaseRunnerName = NullableString(reader, start + 6);
            state.ThirdBaseRunnerPcode = NullableString(reader, start + 7);
            state.ThirdBaseRunnerName = NullableString(reader, start + 8);
        }
        return state;
    }
}
