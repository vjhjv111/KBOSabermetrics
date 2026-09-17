using System;
using System.Collections.Generic;
using System.Linq;
using NaverRelay.Models;

namespace NaverRelay.Parsing
{
    /// <summary>
    /// Performs deterministic cross-checks between the raw relay payload and the normalized rows.
    /// These checks are intentionally side-effect free except for appending diagnostics.
    /// </summary>
    internal static class GameConsistencyValidator
    {
        public static void Validate(NormalizedGame normalized, TextRelayData relayData)
        {
            var plays = relayData.TextRelays ?? new List<TextRelayPlay>();
            var rawEvents = plays.SelectMany(play => play.TextOptions ?? new List<TextOption>()).ToList();

            CheckCount(normalized, "RELAY_GROUP_COUNT_MISMATCH",
                plays.Count, normalized.RelayGroups.Count, "relay groups");
            CheckCount(normalized, "EVENT_COUNT_MISMATCH",
                rawEvents.Count, normalized.Events.Count, "events");
            CheckCount(normalized, "PITCH_COUNT_MISMATCH",
                rawEvents.Count(option => option.Type == 1), normalized.PitchEvents.Count, "pitch events");
            CheckCount(normalized, "RUNNER_EVENT_COUNT_MISMATCH",
                rawEvents.Count(option => option.Type == 14 || option.Type == 24),
                normalized.RunnerEvents.Count, "runner events");
            CheckCount(normalized, "PLAYER_CHANGE_COUNT_MISMATCH",
                rawEvents.Count(option => option.Type == 2), normalized.PlayerChanges.Count, "player changes");
            CheckCount(normalized, "ADMIN_EVENT_COUNT_MISMATCH",
                rawEvents.Count(option => option.Type == 7), normalized.AdministrativeEvents.Count,
                "administrative events");
            CheckCount(normalized, "COMPLETED_PA_COUNT_MISMATCH",
                rawEvents.Count(option => option.Type == 13 || option.Type == 23),
                normalized.PlateAppearances.Count(pa => pa.Status == PlateAppearanceStatus.Completed),
                "completed plate appearances");

            CheckUniqueIds(normalized, normalized.RelayGroups.Select(item => item.RelayGroupId),
                "DUPLICATE_RELAY_GROUP_ID", "relayGroupId");
            CheckUniqueIds(normalized, normalized.Events.Select(item => item.EventId),
                "DUPLICATE_EVENT_ID", "eventId");
            CheckUniqueIds(normalized, normalized.PlateAppearances.Select(item => item.PlateAppearanceId),
                "DUPLICATE_PA_ID", "plateAppearanceId");
            CheckUniqueIds(normalized, normalized.PitchEvents.Select(item => item.PitchEventId),
                "DUPLICATE_PITCH_ID", "pitchEventId");
            CheckUniqueIds(normalized, normalized.RunnerEvents.Select(item => item.RunnerEventId),
                "DUPLICATE_RUNNER_ID", "runnerEventId");
            CheckUniqueIds(normalized, normalized.PlayerChanges.Select(item => item.PlayerChangeEventId),
                "DUPLICATE_CHANGE_ID", "playerChangeEventId");
            CheckUniqueIds(normalized, normalized.AdministrativeEvents.Select(item => item.AdministrativeEventId),
                "DUPLICATE_ADMIN_ID", "administrativeEventId");

            CheckReferences(normalized);
            CheckBattingSideFlags(normalized, plays);
            CheckFinalLineBattingTotals(normalized);
        }

        private static void CheckCount(
            NormalizedGame normalized,
            string code,
            int expected,
            int actual,
            string label)
        {
            if (expected == actual)
            {
                return;
            }

            Add(normalized, DiagnosticSeverity.Error, code,
                $"Raw {label} count is {expected}, but normalized count is {actual}.");
        }

        private static void CheckUniqueIds(
            NormalizedGame normalized,
            IEnumerable<string> ids,
            string code,
            string label)
        {
            var duplicate = ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .GroupBy(id => id, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicate == null)
            {
                return;
            }

            Add(normalized, DiagnosticSeverity.Error, code,
                $"The normalized {label} value '{duplicate.Key}' is not unique.");
        }

        private static void CheckReferences(NormalizedGame normalized)
        {
            var groupIds = normalized.RelayGroups
                .Select(group => group.RelayGroupId)
                .ToHashSet(StringComparer.Ordinal);
            var eventIds = normalized.Events
                .Select(item => item.EventId)
                .ToHashSet(StringComparer.Ordinal);
            var plateAppearanceIds = normalized.PlateAppearances
                .Select(item => item.PlateAppearanceId)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var item in normalized.Events)
            {
                if (!groupIds.Contains(item.RelayGroupId))
                {
                    Add(normalized, DiagnosticSeverity.Error, "EVENT_GROUP_REFERENCE_MISSING",
                        $"Event {item.EventId} references missing relay group {item.RelayGroupId}.",
                        item.RelayGroupId, item.EventId, item.SourceRelayNo, item.SourceSeqNo);
                }

                if (!string.IsNullOrWhiteSpace(item.PlateAppearanceId)
                    && !plateAppearanceIds.Contains(item.PlateAppearanceId))
                {
                    Add(normalized, DiagnosticSeverity.Error, "EVENT_PA_REFERENCE_MISSING",
                        $"Event {item.EventId} references missing plate appearance {item.PlateAppearanceId}.",
                        item.RelayGroupId, item.EventId, item.SourceRelayNo, item.SourceSeqNo);
                }
            }

            foreach (var pitch in normalized.PitchEvents)
            {
                if (!eventIds.Contains(pitch.SourceEventId))
                {
                    Add(normalized, DiagnosticSeverity.Error, "PITCH_EVENT_REFERENCE_MISSING",
                        $"Pitch {pitch.PitchEventId} references missing source event {pitch.SourceEventId}.",
                        pitch.RelayGroupId, pitch.SourceEventId, pitch.SourceRelayNo, pitch.SourceSeqNo);
                }
            }

            foreach (var runner in normalized.RunnerEvents)
            {
                if (!eventIds.Contains(runner.SourceEventId))
                {
                    Add(normalized, DiagnosticSeverity.Error, "RUNNER_EVENT_REFERENCE_MISSING",
                        $"Runner event {runner.RunnerEventId} references missing source event {runner.SourceEventId}.",
                        runner.RelayGroupId, runner.SourceEventId, runner.SourceRelayNo, runner.SourceSeqNo);
                }
            }

            foreach (var change in normalized.PlayerChanges)
            {
                if (!eventIds.Contains(change.SourceEventId))
                {
                    Add(normalized, DiagnosticSeverity.Error, "CHANGE_EVENT_REFERENCE_MISSING",
                        $"Player change {change.PlayerChangeEventId} references missing source event {change.SourceEventId}.",
                        change.RelayGroupId, change.SourceEventId, change.SourceRelayNo, change.SourceSeqNo);
                }
            }

            foreach (var administrative in normalized.AdministrativeEvents)
            {
                if (!eventIds.Contains(administrative.SourceEventId))
                {
                    Add(normalized, DiagnosticSeverity.Error, "ADMIN_EVENT_REFERENCE_MISSING",
                        $"Administrative event {administrative.AdministrativeEventId} references missing source event {administrative.SourceEventId}.",
                        administrative.RelayGroupId, administrative.SourceEventId,
                        administrative.SourceRelayNo, administrative.SourceSeqNo);
                }
            }
        }

        private static void CheckBattingSideFlags(
            NormalizedGame normalized,
            IEnumerable<TextRelayPlay> plays)
        {
            foreach (var play in plays)
            {
                var title = play.Title ?? string.Empty;
                if (title.Contains("회초", StringComparison.Ordinal) && play.HomeOrAway != "0")
                {
                    Add(normalized, DiagnosticSeverity.Warning, "TOP_INNING_SIDE_MISMATCH",
                        $"Relay title '{title}' denotes the top half, but homeOrAway is '{play.HomeOrAway}'.",
                        sourceRelayNo: play.No);
                }
                else if (title.Contains("회말", StringComparison.Ordinal) && play.HomeOrAway != "1")
                {
                    Add(normalized, DiagnosticSeverity.Warning, "BOTTOM_INNING_SIDE_MISMATCH",
                        $"Relay title '{title}' denotes the bottom half, but homeOrAway is '{play.HomeOrAway}'.",
                        sourceRelayNo: play.No);
                }
            }
        }

        private static void CheckFinalLineBattingTotals(NormalizedGame normalized)
        {
            if (!string.Equals(normalized.StatusCode, "RESULT", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized.StatusCode, "ENDED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var side in new[] { TeamSide.Away, TeamSide.Home })
            {
                var lines = normalized.BattingLines
                    .Where(line => line.TeamSide == side)
                    .ToList();
                if (lines.Count == 0)
                {
                    continue;
                }

                var plateAppearances = normalized.PlateAppearances
                    .Where(pa => pa.Status == PlateAppearanceStatus.Completed && pa.BattingSide == side)
                    .ToList();
                var sideLabel = side == TeamSide.Away ? "away" : "home";

                CompareFinalBattingTotal(normalized, sideLabel, "PA", "FINAL_LINE_PA_MISMATCH",
                    SumKnown(lines.Select(line => line.PlateAppearances)), plateAppearances.Count);
                CompareFinalBattingTotal(normalized, sideLabel, "AB", "FINAL_LINE_AB_MISMATCH",
                    SumKnown(lines.Select(line => line.AtBats)), plateAppearances.Count(pa => pa.Outcome.CountsAsAtBat));
                CompareFinalBattingTotal(normalized, sideLabel, "H", "FINAL_LINE_H_MISMATCH",
                    SumKnown(lines.Select(line => line.Hits)), plateAppearances.Count(pa => pa.Outcome.IsHit));
                CompareFinalBattingTotal(normalized, sideLabel, "HR", "FINAL_LINE_HR_MISMATCH",
                    SumKnown(lines.Select(line => line.HomeRuns)),
                    plateAppearances.Count(pa => pa.Outcome.ResultType == BattingResultType.HomeRun));
                CompareFinalBattingTotal(normalized, sideLabel, "BB", "FINAL_LINE_BB_MISMATCH",
                    SumKnown(lines.Select(line => line.Walks)), plateAppearances.Count(pa => pa.Outcome.IsWalk));
                CompareFinalBattingTotal(normalized, sideLabel, "HBP", "FINAL_LINE_HBP_MISMATCH",
                    SumKnown(lines.Select(line => line.HitByPitch)),
                    plateAppearances.Count(pa => pa.Outcome.ResultType == BattingResultType.HitByPitch));
                CompareFinalBattingTotal(normalized, sideLabel, "SO", "FINAL_LINE_SO_MISMATCH",
                    SumKnown(lines.Select(line => line.Strikeouts)), plateAppearances.Count(pa => pa.Outcome.IsStrikeout));
            }
        }

        private static int? SumKnown(IEnumerable<int?> values)
        {
            var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
            return known.Count == 0 ? null : known.Sum();
        }

        private static void CompareFinalBattingTotal(
            NormalizedGame normalized,
            string sideLabel,
            string statistic,
            string code,
            int? finalLineValue,
            int parsedValue)
        {
            if (!finalLineValue.HasValue || finalLineValue.Value == parsedValue)
            {
                return;
            }

            Add(normalized, DiagnosticSeverity.Warning, code,
                $"The {sideLabel} final batting lines contain {finalLineValue.Value} {statistic}, " +
                $"but normalized completed plate appearances produce {parsedValue}.");
        }

        private static void Add(
            NormalizedGame normalized,
            DiagnosticSeverity severity,
            string code,
            string message,
            string? relayGroupId = null,
            string? eventId = null,
            int? sourceRelayNo = null,
            int? sourceSeqNo = null)
        {
            normalized.Diagnostics.Add(new ParserDiagnostic
            {
                Severity = severity,
                Code = code,
                Message = message,
                GameId = normalized.GameId,
                RelayGroupId = relayGroupId,
                EventId = eventId,
                SourceRelayNo = sourceRelayNo,
                SourceSeqNo = sourceSeqNo,
            });
        }
    }
}
