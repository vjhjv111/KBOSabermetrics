using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
using NaverRelay.Parsing;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    private static async Task DeleteExistingGameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string gameId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Games WHERE GameId=$gameId;";
        command.Parameters.AddWithValue("$gameId", gameId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertGameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Games(
                GameId, SeasonYear, GameDate, GameDateTime, SuperCategoryId, UpperCategoryId,
                UpperCategoryName, CategoryId, CategoryName, RoundCode, CompetitionType, Stadium,
                StatusCode, Winner, AwayTeamCode, AwayTeamName, AwayScore, AwayHits, AwayErrors,
                AwayWalks, HomeTeamCode, HomeTeamName, HomeScore, HomeHits, HomeErrors, HomeWalks,
                LastHomeWinRate, LastAwayWinRate, LastWpaByPlate, CompletedPaCount, PitchCount,
                MissingPtsCount, RunnerCount, PlayerChangeCount, AdministrativeCount, WarningCount,
                ErrorCount, UpdatedUtc)
            VALUES(
                $gameId, $seasonYear, $gameDate, $gameDateTime, $superCategoryId, $upperCategoryId,
                $upperCategoryName, $categoryId, $categoryName, $roundCode, $competitionType, $stadium,
                $statusCode, $winner, $awayTeamCode, $awayTeamName, $awayScore, $awayHits, $awayErrors,
                $awayWalks, $homeTeamCode, $homeTeamName, $homeScore, $homeHits, $homeErrors, $homeWalks,
                $lastHomeWinRate, $lastAwayWinRate, $lastWpaByPlate, $completedPaCount, $pitchCount,
                $missingPtsCount, $runnerCount, $playerChangeCount, $administrativeCount, $warningCount,
                $errorCount, $updatedUtc);
            """;
        Add(command, "$gameId", game.GameId);
        Add(command, "$seasonYear", game.SeasonYear);
        Add(command, "$gameDate", NormalizeDate(game.GameDate));
        Add(command, "$gameDateTime", game.GameDateTime);
        Add(command, "$superCategoryId", game.SuperCategoryId);
        Add(command, "$upperCategoryId", game.UpperCategoryId);
        Add(command, "$upperCategoryName", game.UpperCategoryName);
        Add(command, "$categoryId", game.CategoryId);
        Add(command, "$categoryName", game.CategoryName);
        Add(command, "$roundCode", game.RoundCode);
        Add(command, "$competitionType", (int)game.CompetitionType);
        Add(command, "$stadium", game.Stadium);
        Add(command, "$statusCode", game.StatusCode);
        Add(command, "$winner", game.Winner);
        Add(command, "$awayTeamCode", game.AwayTeam.TeamCode);
        Add(command, "$awayTeamName", game.AwayTeam.TeamName);
        Add(command, "$awayScore", game.AwayTeam.FinalScore);
        Add(command, "$awayHits", game.AwayTeam.FinalHits);
        Add(command, "$awayErrors", game.AwayTeam.FinalErrors);
        Add(command, "$awayWalks", game.AwayTeam.FinalWalks);
        Add(command, "$homeTeamCode", game.HomeTeam.TeamCode);
        Add(command, "$homeTeamName", game.HomeTeam.TeamName);
        Add(command, "$homeScore", game.HomeTeam.FinalScore);
        Add(command, "$homeHits", game.HomeTeam.FinalHits);
        Add(command, "$homeErrors", game.HomeTeam.FinalErrors);
        Add(command, "$homeWalks", game.HomeTeam.FinalWalks);
        Add(command, "$lastHomeWinRate", game.LastValidHomeWinRate);
        Add(command, "$lastAwayWinRate", game.LastValidAwayWinRate);
        Add(command, "$lastWpaByPlate", game.LastValidWpaByPlate);
        Add(command, "$completedPaCount", game.Summary.CompletedPlateAppearanceCount);
        Add(command, "$pitchCount", game.Summary.PitchEventCount);
        Add(command, "$missingPtsCount", game.Summary.PtsMissingPitchCount);
        Add(command, "$runnerCount", game.Summary.RunnerEventCount);
        Add(command, "$playerChangeCount", game.Summary.PlayerChangeEventCount);
        Add(command, "$administrativeCount", game.Summary.AdministrativeEventCount);
        Add(command, "$warningCount", game.Summary.WarningCount);
        Add(command, "$errorCount", game.Summary.ErrorCount);
        Add(command, "$updatedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertGameSummaryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        var s = game.Summary;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO GameSummaries VALUES(
                $gameId, $rawRelay, $rawEvent, $rawPitch, $rawPts, $duplicateSeq,
                $completedPa, $interruptedPa, $prePlateSub, $inningMarker, $gameSummary,
                $pitchCount, $ptsMatched, $ptsMissing, $runnerCount, $changeCount, $adminCount,
                $unknownRelay, $unknownEvent, $unknownBatting, $unparsedRunner, $unparsedChange,
                $unknownAdmin, $ptsFailure, $lineMismatch, $warning, $error);
            """;
        Add(command, "$gameId", game.GameId);
        Add(command, "$rawRelay", s.RawRelayGroupCount);
        Add(command, "$rawEvent", s.RawEventCount);
        Add(command, "$rawPitch", s.RawPitchEventCount);
        Add(command, "$rawPts", s.RawPtsCount);
        Add(command, "$duplicateSeq", s.DuplicateSourceSeqNoOccurrenceCount);
        Add(command, "$completedPa", s.CompletedPlateAppearanceCount);
        Add(command, "$interruptedPa", s.InterruptedPlateAppearanceCount);
        Add(command, "$prePlateSub", s.PrePlateSubstitutionGroupCount);
        Add(command, "$inningMarker", s.InningMarkerGroupCount);
        Add(command, "$gameSummary", s.GameSummaryGroupCount);
        Add(command, "$pitchCount", s.PitchEventCount);
        Add(command, "$ptsMatched", s.PtsMatchedPitchCount);
        Add(command, "$ptsMissing", s.PtsMissingPitchCount);
        Add(command, "$runnerCount", s.RunnerEventCount);
        Add(command, "$changeCount", s.PlayerChangeEventCount);
        Add(command, "$adminCount", s.AdministrativeEventCount);
        Add(command, "$unknownRelay", s.UnknownRelayGroupCount);
        Add(command, "$unknownEvent", s.UnknownRawEventTypeCount);
        Add(command, "$unknownBatting", s.UnknownBattingResultCount);
        Add(command, "$unparsedRunner", s.UnparsedRunnerEventCount);
        Add(command, "$unparsedChange", s.UnparsedPlayerChangeCount);
        Add(command, "$unknownAdmin", s.UnknownAdministrativeEventCount);
        Add(command, "$ptsFailure", s.PtsCalculationFailureCount);
        Add(command, "$lineMismatch", s.FinalLineBattingMismatchCount);
        Add(command, "$warning", s.WarningCount);
        Add(command, "$error", s.ErrorCount);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertRelayGroupsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO RelayGroups(
                RelayGroupId, GameId, ChronologicalIndex, SourceRelayNo, Title, TitleStyle,
                Inning, RawHomeOrAway, BattingSide, BattingTeamCode, SourceStatusCode, GroupType,
                PlateAppearanceId, HomeWinRateAfter, AwayWinRateAfter, WpaByPlate,
                BeforeHomeScore, BeforeAwayScore, BeforeOuts,
                BeforeFirstRunnerPcode, BeforeFirstRunnerName,
                BeforeSecondRunnerPcode, BeforeSecondRunnerName,
                BeforeThirdRunnerPcode, BeforeThirdRunnerName,
                AfterHomeScore, AfterAwayScore, AfterOuts)
            VALUES(
                $id, $gameId, $chronological, $relayNo, $title, $titleStyle,
                $inning, $rawSide, $battingSide, $battingTeam, $status, $groupType,
                $paId, $homeWin, $awayWin, $wpa,
                $beforeHome, $beforeAway, $beforeOuts,
                $before1Code, $before1Name, $before2Code, $before2Name, $before3Code, $before3Name,
                $afterHome, $afterAway, $afterOuts);
            """;
        foreach (var group in game.RelayGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            command.Parameters.Clear();
            Add(command, "$id", group.RelayGroupId);
            Add(command, "$gameId", game.GameId);
            Add(command, "$chronological", group.ChronologicalIndex);
            Add(command, "$relayNo", group.SourceRelayNo);
            Add(command, "$title", group.Title);
            Add(command, "$titleStyle", group.TitleStyle);
            Add(command, "$inning", group.Inning);
            Add(command, "$rawSide", group.RawHomeOrAway);
            Add(command, "$battingSide", (int)group.BattingSide);
            Add(command, "$battingTeam", group.BattingTeamCode);
            Add(command, "$status", group.SourceStatusCode);
            Add(command, "$groupType", (int)group.GroupType);
            Add(command, "$paId", group.PlateAppearanceId);
            Add(command, "$homeWin", group.HomeWinRateAfter);
            Add(command, "$awayWin", group.AwayWinRateAfter);
            Add(command, "$wpa", group.WpaByPlate);
            AddStateBefore(command, group.StateBefore);
            Add(command, "$afterHome", group.StateAfter?.HomeScore);
            Add(command, "$afterAway", group.StateAfter?.AwayScore);
            Add(command, "$afterOuts", group.StateAfter?.Outs);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertEventsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NormalizedEvents(
                EventId, GameId, RelayGroupId, PlateAppearanceId, ChronologicalIndex,
                SourceRelayNo, SourceOptionIndex, SourceSeqNo, IsDuplicateSourceSeqNo,
                RawType, EventType, RawText, RawStuff, BattingSide, Inning)
            VALUES(
                $id, $gameId, $relayId, $paId, $chronological,
                $relayNo, $optionIndex, $seqNo, $duplicate,
                $rawType, $eventType, $rawText, $rawStuff, $battingSide, $inning);
            """;
        foreach (var item in game.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            command.Parameters.Clear();
            Add(command, "$id", item.EventId);
            Add(command, "$gameId", item.GameId);
            Add(command, "$relayId", item.RelayGroupId);
            Add(command, "$paId", item.PlateAppearanceId);
            Add(command, "$chronological", item.ChronologicalIndex);
            Add(command, "$relayNo", item.SourceRelayNo);
            Add(command, "$optionIndex", item.SourceOptionIndex);
            Add(command, "$seqNo", item.SourceSeqNo);
            Add(command, "$duplicate", Bool(item.IsDuplicateSourceSeqNo));
            Add(command, "$rawType", item.RawType);
            Add(command, "$eventType", (int)item.EventType);
            Add(command, "$rawText", item.RawText);
            Add(command, "$rawStuff", item.RawStuff);
            Add(command, "$battingSide", (int)item.BattingSide);
            Add(command, "$inning", item.Inning);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertPlateAppearancesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO PlateAppearances(
                PlateAppearanceId, GameId, RelayGroupId, SequenceNumber, OfficialSequenceNumber,
                SourceRelayNo, Inning, RawHomeOrAway, BattingSide, BattingTeamCode, FieldingTeamCode,
                Status, IsOfficial, StartEventId, ResultEventId, BatterPcode, BatterName, BatOrder,
                PitcherPcode, PitcherName, FinalPitcherPcode, FinalPitcherName, ResultRawType,
                ResultText, ResultType, NormalizedResultText, BattedBallType, FieldDirection,
                PrimaryFielder, HomeRunDistanceMeters, CountsAsAtBat, IsHit, IsOut, IsSacrifice,
                IsWalk, IsIntentionalWalk, IsStrikeout, ReachedBase, TotalBases, WasRecognized,
                RunsScored, OutsRecorded, ActualPitchCount, MaximumDisplayPitchNumber,
                HomeWinRateAfter, AwayWinRateAfter, WpaByPlate,
                BeforeHomeScore, BeforeAwayScore, BeforeOuts,
                BeforeFirstRunnerPcode, BeforeFirstRunnerName,
                BeforeSecondRunnerPcode, BeforeSecondRunnerName,
                BeforeThirdRunnerPcode, BeforeThirdRunnerName,
                AfterHomeScore, AfterAwayScore, AfterOuts)
            VALUES(
                $id, $gameId, $relayId, $sequence, $officialSequence,
                $relayNo, $inning, $rawSide, $battingSide, $battingTeam, $fieldingTeam,
                $status, $official, $startEvent, $resultEvent, $batterCode, $batterName, $batOrder,
                $pitcherCode, $pitcherName, $finalPitcherCode, $finalPitcherName, $resultRawType,
                $resultText, $resultType, $normalizedText, $battedBallType, $fieldDirection,
                $primaryFielder, $hrDistance, $countsAsAtBat, $isHit, $isOut, $isSacrifice,
                $isWalk, $isIbb, $isStrikeout, $reachedBase, $totalBases, $recognized,
                $runs, $outs, $pitchCount, $maxDisplayPitch,
                $homeWin, $awayWin, $wpa,
                $beforeHome, $beforeAway, $beforeOuts,
                $before1Code, $before1Name, $before2Code, $before2Name, $before3Code, $before3Name,
                $afterHome, $afterAway, $afterOuts);
            """;

        foreach (var pa in game.PlateAppearances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            command.Parameters.Clear();
            Add(command, "$id", pa.PlateAppearanceId);
            Add(command, "$gameId", pa.GameId);
            Add(command, "$relayId", pa.RelayGroupId);
            Add(command, "$sequence", pa.SequenceNumber);
            Add(command, "$officialSequence", pa.OfficialSequenceNumber);
            Add(command, "$relayNo", pa.SourceRelayNo);
            Add(command, "$inning", pa.Inning);
            Add(command, "$rawSide", pa.RawHomeOrAway);
            Add(command, "$battingSide", (int)pa.BattingSide);
            Add(command, "$battingTeam", pa.BattingTeamCode);
            Add(command, "$fieldingTeam", pa.FieldingTeamCode);
            Add(command, "$status", (int)pa.Status);
            Add(command, "$official", Bool(pa.IsOfficialPlateAppearance));
            Add(command, "$startEvent", pa.StartEventId);
            Add(command, "$resultEvent", pa.ResultEventId);
            Add(command, "$batterCode", pa.BatterPcode);
            Add(command, "$batterName", pa.BatterName);
            Add(command, "$batOrder", pa.BatOrder);
            Add(command, "$pitcherCode", pa.PitcherPcode);
            Add(command, "$pitcherName", pa.PitcherName);
            Add(command, "$finalPitcherCode", pa.FinalPitcherPcode);
            Add(command, "$finalPitcherName", pa.FinalPitcherName);
            Add(command, "$resultRawType", pa.ResultRawType);
            Add(command, "$resultText", pa.ResultText);
            Add(command, "$resultType", (int)pa.Outcome.ResultType);
            Add(command, "$normalizedText", pa.Outcome.NormalizedResultText);
            Add(command, "$battedBallType", (int)pa.Outcome.BattedBallType);
            Add(command, "$fieldDirection", (int)pa.Outcome.FieldDirection);
            Add(command, "$primaryFielder", pa.Outcome.PrimaryFielder);
            Add(command, "$hrDistance", pa.Outcome.HomeRunDistanceMeters);
            Add(command, "$countsAsAtBat", Bool(pa.Outcome.CountsAsAtBat));
            Add(command, "$isHit", Bool(pa.Outcome.IsHit));
            Add(command, "$isOut", Bool(pa.Outcome.IsOut));
            Add(command, "$isSacrifice", Bool(pa.Outcome.IsSacrifice));
            Add(command, "$isWalk", Bool(pa.Outcome.IsWalk));
            Add(command, "$isIbb", Bool(pa.Outcome.IsIntentionalWalk));
            Add(command, "$isStrikeout", Bool(pa.Outcome.IsStrikeout));
            Add(command, "$reachedBase", Bool(pa.Outcome.ReachedBase));
            Add(command, "$totalBases", pa.Outcome.TotalBases);
            Add(command, "$recognized", Bool(pa.Outcome.WasRecognized));
            Add(command, "$runs", pa.RunsScored);
            Add(command, "$outs", pa.OutsRecorded);
            Add(command, "$pitchCount", pa.ActualPitchCount);
            Add(command, "$maxDisplayPitch", pa.MaximumDisplayPitchNumber);
            Add(command, "$homeWin", pa.HomeWinRateAfter);
            Add(command, "$awayWin", pa.AwayWinRateAfter);
            Add(command, "$wpa", pa.WpaByPlate);
            AddStateBefore(command, pa.StateBefore);
            Add(command, "$afterHome", pa.StateAfter?.HomeScore);
            Add(command, "$afterAway", pa.StateAfter?.AwayScore);
            Add(command, "$afterOuts", pa.StateAfter?.Outs);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertPitchesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Pitches(
                PitchEventId, SourceEventId, GameId, RelayGroupId, PlateAppearanceId,
                SourceRelayNo, SourceSeqNo, SourceOptionIndex, ActualPitchIndex, DisplayPitchNumber,
                Inning, BattingSide, PitcherPcode, PitcherName, BatterPcode, BatterName,
                BallsBefore, StrikesBefore, BallsAfter, StrikesAfter, OutsBefore,
                RawPitchResult, PitchResult, PitchType, SpeedKmh, PtsPitchId, HasPtsTracking,
                CrossPlateX, CrossPlateY, CalculatedCrossPlateX, CalculatedCrossPlateZ,
                TimeToPlateSeconds, TopStrikeZone, BottomStrikeZone, IsInNominalStrikeZone,
                X0, Y0, Z0, Vx0, Vy0, Vz0, Ax, Ay, Az, BatterStance,
                IsSwing, IsWhiff, IsContact, IsInPlay, IsCalledStrike)
            VALUES(
                $id, $sourceEvent, $gameId, $relayId, $paId,
                $relayNo, $seqNo, $optionIndex, $actualIndex, $displayNumber,
                $inning, $battingSide, $pitcherCode, $pitcherName, $batterCode, $batterName,
                $ballsBefore, $strikesBefore, $ballsAfter, $strikesAfter, $outsBefore,
                $rawResult, $result, $pitchType, $speed, $ptsId, $hasPts,
                $crossX, $crossY, $calculatedX, $calculatedZ,
                $timeToPlate, $topZone, $bottomZone, $inZone,
                $x0, $y0, $z0, $vx0, $vy0, $vz0, $ax, $ay, $az, $stance,
                $swing, $whiff, $contact, $inPlay, $calledStrike);
            """;
        foreach (var pitch in game.PitchEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            command.Parameters.Clear();
            Add(command, "$id", pitch.PitchEventId);
            Add(command, "$sourceEvent", pitch.SourceEventId);
            Add(command, "$gameId", pitch.GameId);
            Add(command, "$relayId", pitch.RelayGroupId);
            Add(command, "$paId", pitch.PlateAppearanceId);
            Add(command, "$relayNo", pitch.SourceRelayNo);
            Add(command, "$seqNo", pitch.SourceSeqNo);
            Add(command, "$optionIndex", pitch.SourceOptionIndex);
            Add(command, "$actualIndex", pitch.ActualPitchIndex);
            Add(command, "$displayNumber", pitch.DisplayPitchNumber);
            Add(command, "$inning", pitch.Inning);
            Add(command, "$battingSide", (int)pitch.BattingSide);
            Add(command, "$pitcherCode", pitch.PitcherPcode);
            Add(command, "$pitcherName", pitch.PitcherName);
            Add(command, "$batterCode", pitch.BatterPcode);
            Add(command, "$batterName", pitch.BatterName);
            Add(command, "$ballsBefore", pitch.BallsBefore);
            Add(command, "$strikesBefore", pitch.StrikesBefore);
            Add(command, "$ballsAfter", pitch.BallsAfter);
            Add(command, "$strikesAfter", pitch.StrikesAfter);
            Add(command, "$outsBefore", pitch.OutsBefore);
            Add(command, "$rawResult", pitch.RawPitchResult);
            Add(command, "$result", (int)pitch.PitchResult);
            Add(command, "$pitchType", pitch.PitchType);
            Add(command, "$speed", pitch.SpeedKmh);
            Add(command, "$ptsId", pitch.PtsPitchId);
            Add(command, "$hasPts", Bool(pitch.HasPtsTracking));
            Add(command, "$crossX", pitch.CrossPlateX);
            Add(command, "$crossY", pitch.CrossPlateY);
            Add(command, "$calculatedX", pitch.CalculatedCrossPlateX);
            Add(command, "$calculatedZ", pitch.CalculatedCrossPlateZ);
            Add(command, "$timeToPlate", pitch.TimeToPlateSeconds);
            Add(command, "$topZone", pitch.TopStrikeZone);
            Add(command, "$bottomZone", pitch.BottomStrikeZone);
            Add(command, "$inZone", NullableBool(pitch.IsInNominalStrikeZone));
            Add(command, "$x0", pitch.X0);
            Add(command, "$y0", pitch.Y0);
            Add(command, "$z0", pitch.Z0);
            Add(command, "$vx0", pitch.Vx0);
            Add(command, "$vy0", pitch.Vy0);
            Add(command, "$vz0", pitch.Vz0);
            Add(command, "$ax", pitch.Ax);
            Add(command, "$ay", pitch.Ay);
            Add(command, "$az", pitch.Az);
            Add(command, "$stance", pitch.BatterStance);
            Add(command, "$swing", Bool(pitch.IsSwing));
            Add(command, "$whiff", Bool(pitch.IsWhiff));
            Add(command, "$contact", Bool(pitch.IsContact));
            Add(command, "$inPlay", Bool(pitch.IsInPlay));
            Add(command, "$calledStrike", Bool(pitch.IsCalledStrike));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertRunnerEventsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO RunnerEvents(
                RunnerEventId, SourceEventId, GameId, RelayGroupId, PlateAppearanceId,
                SourceRelayNo, SourceSeqNo, SourceOptionIndex, Inning, BattingSide, TeamCode,
                RunnerPcode, RunnerName, FromBase, ToBase, EventType, Reason, IsOut, IsRun,
                WasParsed, RawText)
            VALUES(
                $id, $sourceEvent, $gameId, $relayId, $paId,
                $relayNo, $seqNo, $optionIndex, $inning, $battingSide, $team,
                $runnerCode, $runnerName, $fromBase, $toBase, $eventType, $reason, $isOut, $isRun,
                $parsed, $rawText);
            """;
        foreach (var runner in game.RunnerEvents)
        {
            command.Parameters.Clear();
            Add(command, "$id", runner.RunnerEventId);
            Add(command, "$sourceEvent", runner.SourceEventId);
            Add(command, "$gameId", runner.GameId);
            Add(command, "$relayId", runner.RelayGroupId);
            Add(command, "$paId", runner.PlateAppearanceId);
            Add(command, "$relayNo", runner.SourceRelayNo);
            Add(command, "$seqNo", runner.SourceSeqNo);
            Add(command, "$optionIndex", runner.SourceOptionIndex);
            Add(command, "$inning", runner.Inning);
            Add(command, "$battingSide", (int)runner.BattingSide);
            Add(command, "$team", runner.TeamCode);
            Add(command, "$runnerCode", runner.RunnerPcode);
            Add(command, "$runnerName", runner.RunnerName);
            Add(command, "$fromBase", runner.FromBase);
            Add(command, "$toBase", runner.ToBase);
            Add(command, "$eventType", (int)runner.EventType);
            Add(command, "$reason", (int)runner.Reason);
            Add(command, "$isOut", Bool(runner.IsOut));
            Add(command, "$isRun", Bool(runner.IsRun));
            Add(command, "$parsed", Bool(runner.WasParsed));
            Add(command, "$rawText", runner.RawText);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertPlayerChangesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO PlayerChanges(
                PlayerChangeEventId, SourceEventId, GameId, RelayGroupId, PlateAppearanceId,
                SourceRelayNo, SourceSeqNo, SourceOptionIndex, Inning, BattingSide, ChangedTeamSide,
                TeamCode, ChangeType, RawChangeType, RawText, OutPlayerPcode, OutPlayerName,
                OutPosition, InPlayerPcode, InPlayerName, InPosition, ShiftPlayerPcode,
                ShiftPlayerName, OldPosition, NewPosition, SourceOutPlayerTurn, BatOrder,
                IsPitcherChange, IsPinchHitter, IsPinchRunner, WasParsed,
                BeforeHomeScore, BeforeAwayScore, BeforeOuts,
                BeforeFirstRunnerPcode, BeforeSecondRunnerPcode, BeforeThirdRunnerPcode)
            VALUES(
                $id, $sourceEvent, $gameId, $relayId, $paId,
                $relayNo, $seqNo, $optionIndex, $inning, $battingSide, $changedSide,
                $team, $changeType, $rawChangeType, $rawText, $outCode, $outName,
                $outPosition, $inCode, $inName, $inPosition, $shiftCode,
                $shiftName, $oldPosition, $newPosition, $outTurn, $batOrder,
                $pitcherChange, $pinchHitter, $pinchRunner, $parsed,
                $beforeHome, $beforeAway, $beforeOuts,
                $before1Code, $before2Code, $before3Code);
            """;
        foreach (var change in game.PlayerChanges)
        {
            command.Parameters.Clear();
            Add(command, "$id", change.PlayerChangeEventId);
            Add(command, "$sourceEvent", change.SourceEventId);
            Add(command, "$gameId", change.GameId);
            Add(command, "$relayId", change.RelayGroupId);
            Add(command, "$paId", change.PlateAppearanceId);
            Add(command, "$relayNo", change.SourceRelayNo);
            Add(command, "$seqNo", change.SourceSeqNo);
            Add(command, "$optionIndex", change.SourceOptionIndex);
            Add(command, "$inning", change.Inning);
            Add(command, "$battingSide", (int)change.BattingSide);
            Add(command, "$changedSide", (int)change.ChangedTeamSide);
            Add(command, "$team", change.TeamCode);
            Add(command, "$changeType", (int)change.ChangeType);
            Add(command, "$rawChangeType", change.RawChangeType);
            Add(command, "$rawText", change.RawText);
            Add(command, "$outCode", change.OutPlayerPcode);
            Add(command, "$outName", change.OutPlayerName);
            Add(command, "$outPosition", change.OutPosition);
            Add(command, "$inCode", change.InPlayerPcode);
            Add(command, "$inName", change.InPlayerName);
            Add(command, "$inPosition", change.InPosition);
            Add(command, "$shiftCode", change.ShiftPlayerPcode);
            Add(command, "$shiftName", change.ShiftPlayerName);
            Add(command, "$oldPosition", change.OldPosition);
            Add(command, "$newPosition", change.NewPosition);
            Add(command, "$outTurn", change.SourceOutPlayerTurn);
            Add(command, "$batOrder", change.BatOrder);
            Add(command, "$pitcherChange", Bool(change.IsPitcherChange));
            Add(command, "$pinchHitter", Bool(change.IsPinchHitter));
            Add(command, "$pinchRunner", Bool(change.IsPinchRunner));
            Add(command, "$parsed", Bool(change.WasParsed));
            Add(command, "$beforeHome", change.StateBefore?.HomeScore);
            Add(command, "$beforeAway", change.StateBefore?.AwayScore);
            Add(command, "$beforeOuts", change.StateBefore?.Outs);
            Add(command, "$before1Code", change.StateBefore?.FirstBaseRunnerPcode);
            Add(command, "$before2Code", change.StateBefore?.SecondBaseRunnerPcode);
            Add(command, "$before3Code", change.StateBefore?.ThirdBaseRunnerPcode);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertAdministrativeEventsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AdministrativeEvents(
                AdministrativeEventId, SourceEventId, GameId, RelayGroupId, PlateAppearanceId,
                SourceRelayNo, SourceSeqNo, SourceOptionIndex, Inning, BattingSide, EventType,
                RawText, AutomaticBallDelta, AutomaticStrikeDelta, ReviewOriginalCall,
                ReviewFinalCall, ReviewOverturned, WasRecognized)
            VALUES(
                $id, $sourceEvent, $gameId, $relayId, $paId,
                $relayNo, $seqNo, $optionIndex, $inning, $battingSide, $eventType,
                $rawText, $ballDelta, $strikeDelta, $originalCall,
                $finalCall, $overturned, $recognized);
            """;
        foreach (var item in game.AdministrativeEvents)
        {
            command.Parameters.Clear();
            Add(command, "$id", item.AdministrativeEventId);
            Add(command, "$sourceEvent", item.SourceEventId);
            Add(command, "$gameId", item.GameId);
            Add(command, "$relayId", item.RelayGroupId);
            Add(command, "$paId", item.PlateAppearanceId);
            Add(command, "$relayNo", item.SourceRelayNo);
            Add(command, "$seqNo", item.SourceSeqNo);
            Add(command, "$optionIndex", item.SourceOptionIndex);
            Add(command, "$inning", item.Inning);
            Add(command, "$battingSide", (int)item.BattingSide);
            Add(command, "$eventType", (int)item.EventType);
            Add(command, "$rawText", item.RawText);
            Add(command, "$ballDelta", item.AutomaticBallDelta);
            Add(command, "$strikeDelta", item.AutomaticStrikeDelta);
            Add(command, "$originalCall", item.ReviewOriginalCall);
            Add(command, "$finalCall", item.ReviewFinalCall);
            Add(command, "$overturned", NullableBool(item.ReviewOverturned));
            Add(command, "$recognized", Bool(item.WasRecognized));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertBattingLinesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO BattingGameLines(
                GameId, Pcode, TeamCode, LineupSequence, TeamSide, Name, BatOrder, Position,
                BirthDate, Height, Weight, BackNumber, HitType, EnteredAsSubstitute, LeftGame,
                PlateAppearances, AtBats, Hits, HomeRuns, Walks, HitByPitch, Strikeouts, Runs,
                RunsBattedIn)
            VALUES(
                $gameId, $pcode, $team, $sequence, $side, $name, $batOrder, $position,
                $birth, $height, $weight, $backNumber, $hitType, $substitute, $leftGame,
                $pa, $ab, $hits, $hr, $bb, $hbp, $so, $runs, $rbi);
            """;
        foreach (var line in game.BattingLines)
        {
            var pcode = PlayerCode(line.Pcode, line.Name, line.TeamCode);
            command.Parameters.Clear();
            Add(command, "$gameId", line.GameId);
            Add(command, "$pcode", pcode);
            Add(command, "$team", line.TeamCode ?? string.Empty);
            Add(command, "$sequence", line.LineupSequence ?? 0);
            Add(command, "$side", (int)line.TeamSide);
            Add(command, "$name", line.Name);
            Add(command, "$batOrder", line.BatOrder);
            Add(command, "$position", line.Position);
            Add(command, "$birth", WarehouseProjectionBuilder.NormalizeBirthDate(line.BirthDateRaw));
            Add(command, "$height", line.Height);
            Add(command, "$weight", line.Weight);
            Add(command, "$backNumber", line.BackNumber);
            Add(command, "$hitType", line.HitType);
            Add(command, "$substitute", Bool(line.EnteredAsSubstitute));
            Add(command, "$leftGame", Bool(line.LeftGame));
            Add(command, "$pa", line.PlateAppearances);
            Add(command, "$ab", line.AtBats);
            Add(command, "$hits", line.Hits);
            Add(command, "$hr", line.HomeRuns);
            Add(command, "$bb", line.Walks);
            Add(command, "$hbp", line.HitByPitch);
            Add(command, "$so", line.Strikeouts);
            Add(command, "$runs", line.Runs);
            Add(command, "$rbi", line.RunsBattedIn);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertPitchingLinesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO PitchingGameLines(
                GameId, Pcode, TeamCode, AppearanceSequence, TeamSide, Name, BirthDate,
                Height, Weight, BackNumber, HitType, InningsDisplay, InningsOuts, PitchCount,
                HitsAllowed, HomeRunsAllowed, Walks, HitBatters, Strikeouts, RunsAllowed,
                EarnedRuns, WildPitches)
            VALUES(
                $gameId, $pcode, $team, $sequence, $side, $name, $birth,
                $height, $weight, $backNumber, $hitType, $inningsDisplay, $inningsOuts, $pitchCount,
                $hits, $hr, $bb, $hbp, $so, $runs, $er, $wp);
            """;
        foreach (var line in game.PitchingLines)
        {
            var pcode = PlayerCode(line.Pcode, line.Name, line.TeamCode);
            command.Parameters.Clear();
            Add(command, "$gameId", line.GameId);
            Add(command, "$pcode", pcode);
            Add(command, "$team", line.TeamCode ?? string.Empty);
            Add(command, "$sequence", line.AppearanceSequence ?? 0);
            Add(command, "$side", (int)line.TeamSide);
            Add(command, "$name", line.Name);
            Add(command, "$birth", WarehouseProjectionBuilder.NormalizeBirthDate(line.BirthDateRaw));
            Add(command, "$height", line.Height);
            Add(command, "$weight", line.Weight);
            Add(command, "$backNumber", line.BackNumber);
            Add(command, "$hitType", line.HitType);
            Add(command, "$inningsDisplay", line.InningsDisplay);
            Add(command, "$inningsOuts", WarehouseProjectionBuilder.ParseInningsOuts(line.InningsDisplay));
            Add(command, "$pitchCount", line.PitchCount);
            Add(command, "$hits", line.HitsAllowed);
            Add(command, "$hr", line.HomeRunsAllowed);
            Add(command, "$bb", line.Walks);
            Add(command, "$hbp", line.HitBatters);
            Add(command, "$so", line.Strikeouts);
            Add(command, "$runs", line.RunsAllowed);
            Add(command, "$er", line.EarnedRuns);
            Add(command, "$wp", line.WildPitches);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertDiagnosticsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Diagnostics(
                GameId, Severity, Code, Message, RelayGroupId, EventId, SourceRelayNo, SourceSeqNo)
            VALUES($gameId, $severity, $code, $message, $relayId, $eventId, $relayNo, $seqNo);
            """;
        foreach (var diagnostic in game.Diagnostics)
        {
            command.Parameters.Clear();
            Add(command, "$gameId", game.GameId);
            Add(command, "$severity", (int)diagnostic.Severity);
            Add(command, "$code", diagnostic.Code);
            Add(command, "$message", diagnostic.Message);
            Add(command, "$relayId", diagnostic.RelayGroupId);
            Add(command, "$eventId", diagnostic.EventId);
            Add(command, "$relayNo", diagnostic.SourceRelayNo);
            Add(command, "$seqNo", diagnostic.SourceSeqNo);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertGamePlayersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        IReadOnlyList<WarehousePlayerObservation> players,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO GamePlayers(
                GameId, Pcode, TeamCode, Name, BirthDate, Position, HitType,
                IsBatter, IsPitcher, BatOrder, LineupSequence, SeasonYear, GameDate)
            VALUES(
                $gameId, $pcode, $team, $name, $birth, $position, $hitType,
                $isBatter, $isPitcher, $batOrder, $lineupSequence, $seasonYear, $gameDate);
            """;
        foreach (var player in players)
        {
            command.Parameters.Clear();
            Add(command, "$gameId", game.GameId);
            Add(command, "$pcode", player.Pcode);
            Add(command, "$team", player.TeamCode);
            Add(command, "$name", player.Name);
            Add(command, "$birth", player.BirthDate);
            Add(command, "$position", player.Position);
            Add(command, "$hitType", player.HitType);
            Add(command, "$isBatter", Bool(player.IsBatter));
            Add(command, "$isPitcher", Bool(player.IsPitcher));
            Add(command, "$batOrder", player.BatOrder);
            Add(command, "$lineupSequence", player.LineupSequence);
            Add(command, "$seasonYear", game.SeasonYear);
            Add(command, "$gameDate", NormalizeDate(game.GameDate));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertBatterGameStatsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<BatterGameAggregate> rows,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO BatterGameStats VALUES(
                $gameId, $pcode, $team, $name,
                $pa, $ab, $h, $singles, $doubles, $triples, $hr, $bb, $ibb, $hbp, $so,
                $sf, $sh, $gdp, $tb, $runs, $rbi, $sb, $cs, $outs, $playRuns, $flyBalls, $wpa,
                $pitches, $swings, $contacts, $whiffs, $called, $csw, $inZone, $outZone,
                $zoneSwings, $chaseSwings, $zoneContacts, $outZoneContacts, $firstPitches,
                $firstPitchSwings, $c, $firstBase, $secondBase, $thirdBase, $ss, $lf, $cf, $rf, $dhPa);
            """;
        foreach (var row in rows)
        {
            command.Parameters.Clear();
            Add(command, "$gameId", row.GameId); Add(command, "$pcode", row.Pcode);
            Add(command, "$team", row.TeamCode); Add(command, "$name", row.Name);
            Add(command, "$pa", row.PlateAppearances); Add(command, "$ab", row.AtBats);
            Add(command, "$h", row.Hits); Add(command, "$singles", row.Singles);
            Add(command, "$doubles", row.Doubles); Add(command, "$triples", row.Triples);
            Add(command, "$hr", row.HomeRuns); Add(command, "$bb", row.Walks);
            Add(command, "$ibb", row.IntentionalWalks); Add(command, "$hbp", row.HitByPitch);
            Add(command, "$so", row.Strikeouts); Add(command, "$sf", row.SacrificeFlies);
            Add(command, "$sh", row.SacrificeBunts); Add(command, "$gdp", row.DoublePlays);
            Add(command, "$tb", row.TotalBases); Add(command, "$runs", row.Runs);
            Add(command, "$rbi", row.RunsBattedIn); Add(command, "$sb", row.StolenBases);
            Add(command, "$cs", row.CaughtStealing); Add(command, "$outs", row.OutsRecorded);
            Add(command, "$playRuns", row.RunsScoredOnPlays); Add(command, "$flyBalls", row.FlyBalls);
            Add(command, "$wpa", row.Wpa); Add(command, "$pitches", row.Pitches);
            Add(command, "$swings", row.Swings); Add(command, "$contacts", row.Contacts);
            Add(command, "$whiffs", row.Whiffs); Add(command, "$called", row.CalledStrikes);
            Add(command, "$csw", row.Csw); Add(command, "$inZone", row.InZone);
            Add(command, "$outZone", row.OutZone); Add(command, "$zoneSwings", row.ZoneSwings);
            Add(command, "$chaseSwings", row.ChaseSwings); Add(command, "$zoneContacts", row.ZoneContacts);
            Add(command, "$outZoneContacts", row.OutZoneContacts); Add(command, "$firstPitches", row.FirstPitches);
            Add(command, "$firstPitchSwings", row.FirstPitchSwings); Add(command, "$c", row.CatcherInnings);
            Add(command, "$firstBase", row.FirstBaseInnings); Add(command, "$secondBase", row.SecondBaseInnings);
            Add(command, "$thirdBase", row.ThirdBaseInnings); Add(command, "$ss", row.ShortstopInnings);
            Add(command, "$lf", row.LeftFieldInnings); Add(command, "$cf", row.CenterFieldInnings);
            Add(command, "$rf", row.RightFieldInnings); Add(command, "$dhPa", row.DesignatedHitterPlateAppearances);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertPitcherGameStatsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<PitcherGameAggregate> rows,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO PitcherGameStats VALUES(
                $gameId, $pcode, $team, $name,
                $tbf, $paOuts, $paHits, $paHr, $paBb, $paHbp, $paSo, $sf, $paRuns,
                $flyBalls, $iffb, $pitches, $swings, $contacts, $whiffs, $called, $csw,
                $inZone, $outZone, $zoneSwings, $chaseSwings, $zoneContacts, $outZoneContacts,
                $firstPitches, $firstPitchSwings, $speedSum, $speedCount,
                $hasFinal, $appearanceSequence, $starter, $reliever, $inningsOuts,
                $hitsAllowed, $hrAllowed, $finalBb, $finalHbp, $finalSo, $runsAllowed,
                $earnedRuns, $wildPitches, $finalPitchCount, $entryWpaSum, $entryWpaCount);
            """;
        foreach (var row in rows)
        {
            command.Parameters.Clear();
            Add(command, "$gameId", row.GameId); Add(command, "$pcode", row.Pcode);
            Add(command, "$team", row.TeamCode); Add(command, "$name", row.Name);
            Add(command, "$tbf", row.BattersFaced); Add(command, "$paOuts", row.PlateAppearanceOuts);
            Add(command, "$paHits", row.HitsFromPlateAppearances); Add(command, "$paHr", row.HomeRunsFromPlateAppearances);
            Add(command, "$paBb", row.WalksFromPlateAppearances); Add(command, "$paHbp", row.HitBattersFromPlateAppearances);
            Add(command, "$paSo", row.StrikeoutsFromPlateAppearances); Add(command, "$sf", row.SacrificeFlies);
            Add(command, "$paRuns", row.RunsFromPlateAppearances); Add(command, "$flyBalls", row.FlyBalls);
            Add(command, "$iffb", row.InfieldFlies); Add(command, "$pitches", row.Pitches);
            Add(command, "$swings", row.Swings); Add(command, "$contacts", row.Contacts);
            Add(command, "$whiffs", row.Whiffs); Add(command, "$called", row.CalledStrikes);
            Add(command, "$csw", row.Csw); Add(command, "$inZone", row.InZone);
            Add(command, "$outZone", row.OutZone); Add(command, "$zoneSwings", row.ZoneSwings);
            Add(command, "$chaseSwings", row.ChaseSwings); Add(command, "$zoneContacts", row.ZoneContacts);
            Add(command, "$outZoneContacts", row.OutZoneContacts); Add(command, "$firstPitches", row.FirstPitches);
            Add(command, "$firstPitchSwings", row.FirstPitchSwings); Add(command, "$speedSum", row.SpeedSum);
            Add(command, "$speedCount", row.SpeedCount); Add(command, "$hasFinal", Bool(row.HasFinalLine));
            Add(command, "$appearanceSequence", row.AppearanceSequence); Add(command, "$starter", Bool(row.IsStarter));
            Add(command, "$reliever", Bool(row.IsReliever)); Add(command, "$inningsOuts", row.InningsOuts);
            Add(command, "$hitsAllowed", row.HitsAllowed); Add(command, "$hrAllowed", row.HomeRunsAllowed);
            Add(command, "$finalBb", row.FinalWalks); Add(command, "$finalHbp", row.FinalHitBatters);
            Add(command, "$finalSo", row.FinalStrikeouts); Add(command, "$runsAllowed", row.RunsAllowed);
            Add(command, "$earnedRuns", row.EarnedRuns); Add(command, "$wildPitches", row.WildPitches);
            Add(command, "$finalPitchCount", row.FinalPitchCount); Add(command, "$entryWpaSum", row.EntryAbsoluteWpaSum);
            Add(command, "$entryWpaCount", row.EntryWpaCount);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task UpsertPlayerProfilesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedGame game,
        IReadOnlyList<WarehousePlayerObservation> players,
        CancellationToken cancellationToken)
    {
        var gameDate = NormalizeDate(game.GameDate);
        var utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var isClubGame = string.Equals(game.RoundCode?.Trim(), "kbo_r", StringComparison.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Players(
                Pcode, Name, BirthDate, LatestTeam, PrimaryPosition, Role, BatsThrows,
                FirstSeason, LastSeason, LastGameDate, LastClubGameDate, UpdatedUtc)
            VALUES(
                $pcode, $name, $birth, CASE WHEN $isClubGame=1 THEN $team ELSE NULL END, $position, $role, $batsThrows,
                $year, $year, $gameDate, CASE WHEN $isClubGame=1 THEN $gameDate ELSE NULL END, $utc)
            ON CONFLICT(Pcode) DO UPDATE SET
                Name=CASE WHEN excluded.LastGameDate >= COALESCE(Players.LastGameDate, '') THEN excluded.Name ELSE Players.Name END,
                BirthDate=COALESCE(Players.BirthDate, excluded.BirthDate),
                LatestTeam=CASE
                    WHEN excluded.LastClubGameDate IS NOT NULL
                     AND excluded.LastClubGameDate >= COALESCE(Players.LastClubGameDate, '')
                    THEN excluded.LatestTeam ELSE Players.LatestTeam END,
                PrimaryPosition=CASE WHEN excluded.LastGameDate >= COALESCE(Players.LastGameDate, '') THEN excluded.PrimaryPosition ELSE Players.PrimaryPosition END,
                Role=CASE
                    WHEN Players.Role=excluded.Role THEN Players.Role
                    WHEN Players.Role='타자·투수' OR excluded.Role='타자·투수' THEN '타자·투수'
                    ELSE '타자·투수' END,
                BatsThrows=COALESCE(Players.BatsThrows, excluded.BatsThrows),
                FirstSeason=CASE WHEN Players.FirstSeason IS NULL THEN excluded.FirstSeason ELSE MIN(Players.FirstSeason, excluded.FirstSeason) END,
                LastSeason=CASE WHEN Players.LastSeason IS NULL THEN excluded.LastSeason ELSE MAX(Players.LastSeason, excluded.LastSeason) END,
                LastGameDate=MAX(COALESCE(Players.LastGameDate, ''), COALESCE(excluded.LastGameDate, '')),
                LastClubGameDate=CASE
                    WHEN excluded.LastClubGameDate IS NULL THEN Players.LastClubGameDate
                    ELSE MAX(COALESCE(Players.LastClubGameDate, ''), excluded.LastClubGameDate) END,
                UpdatedUtc=excluded.UpdatedUtc;
            """;
        foreach (var player in players)
        {
            command.Parameters.Clear();
            Add(command, "$pcode", player.Pcode);
            Add(command, "$name", player.Name);
            Add(command, "$birth", player.BirthDate);
            Add(command, "$team", player.TeamCode);
            Add(command, "$isClubGame", Bool(isClubGame));
            Add(command, "$position", NormalizePosition(player.Position, player.IsPitcher));
            Add(command, "$role", player.IsBatter && player.IsPitcher ? "타자·투수" : player.IsPitcher ? "투수" : "타자");
            Add(command, "$batsThrows", player.HitType);
            Add(command, "$year", game.SeasonYear);
            Add(command, "$gameDate", gameDate);
            Add(command, "$utc", utc);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task UpsertParsedSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string gameId,
        InputDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ParsedSources(SourceKey, Fingerprint, GameId, SourceDisplay, ParserVersion, ParsedUtc)
            VALUES($key, $fingerprint, $gameId, $display, $version, $utc)
            ON CONFLICT(SourceKey) DO UPDATE SET
                Fingerprint=excluded.Fingerprint,
                GameId=excluded.GameId,
                SourceDisplay=excluded.SourceDisplay,
                ParserVersion=excluded.ParserVersion,
                ParsedUtc=excluded.ParsedUtc;
            """;
        Add(command, "$key", GetSourceKey(document));
        Add(command, "$fingerprint", GetFingerprint(document));
        Add(command, "$gameId", gameId);
        Add(command, "$display", document.SourceDisplay);
        Add(command, "$version", ParserCacheVersion);
        Add(command, "$utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddStateBefore(SqliteCommand command, GameStateSnapshot? state)
    {
        Add(command, "$beforeHome", state?.HomeScore);
        Add(command, "$beforeAway", state?.AwayScore);
        Add(command, "$beforeOuts", state?.Outs);
        Add(command, "$before1Code", state?.FirstBaseRunnerPcode);
        Add(command, "$before1Name", state?.FirstBaseRunnerName);
        Add(command, "$before2Code", state?.SecondBaseRunnerPcode);
        Add(command, "$before2Name", state?.SecondBaseRunnerName);
        Add(command, "$before3Code", state?.ThirdBaseRunnerPcode);
        Add(command, "$before3Name", state?.ThirdBaseRunnerName);
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, DbValue(value));

    private static string PlayerCode(string? pcode, string? name, string? team)
    {
        if (!string.IsNullOrWhiteSpace(pcode)) return pcode.Trim();
        var cleanTeam = team?.Trim() ?? string.Empty;
        var cleanName = name?.Trim();
        return string.IsNullOrWhiteSpace(cleanName) ? $"UNKNOWN:{cleanTeam}" : $"NAME:{cleanTeam}:{cleanName}";
    }

    private static string NormalizePosition(string? position, bool isPitcher)
    {
        if (isPitcher) return "P";
        if (string.IsNullOrWhiteSpace(position)) return "-";
        return position.Trim().ToUpperInvariant()
            .Replace("포수", "C", StringComparison.Ordinal)
            .Replace("유격수", "SS", StringComparison.Ordinal)
            .Replace("2루수", "2B", StringComparison.Ordinal)
            .Replace("3루수", "3B", StringComparison.Ordinal)
            .Replace("1루수", "1B", StringComparison.Ordinal)
            .Replace("좌익수", "LF", StringComparison.Ordinal)
            .Replace("중견수", "CF", StringComparison.Ordinal)
            .Replace("우익수", "RF", StringComparison.Ordinal)
            .Replace("지명타자", "DH", StringComparison.Ordinal)
            .Replace("대타", "PH", StringComparison.Ordinal)
            .Replace("투수", "P", StringComparison.Ordinal);
    }
}
