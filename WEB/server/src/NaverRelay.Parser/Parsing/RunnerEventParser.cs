using System;
using System.Text.RegularExpressions;
using NaverRelay.Models;

namespace NaverRelay.Parsing
{
    internal static class RunnerEventParser
    {
        private static readonly Regex RunnerRegex = new(
            @"^(?<from>[123])루주자\s+(?<name>.+?)\s*:\s*(?<action>.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex AdvanceRegex = new(
            @"(?<to>[123])루까지\s*진루",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ForceBaseRegex = new(
            @"(?<to>[123])루\s*터치아웃",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static RunnerEvent Parse(
            TextOption option,
            NormalizedEvent sourceEvent,
            ParsingContext context)
        {
            var result = new RunnerEvent
            {
                RunnerEventId = sourceEvent.EventId + ":runner",
                SourceEventId = sourceEvent.EventId,
                GameId = sourceEvent.GameId,
                RelayGroupId = sourceEvent.RelayGroupId,
                PlateAppearanceId = sourceEvent.PlateAppearanceId,
                SourceRelayNo = sourceEvent.SourceRelayNo,
                SourceSeqNo = sourceEvent.SourceSeqNo,
                SourceOptionIndex = sourceEvent.SourceOptionIndex,
                Inning = sourceEvent.Inning,
                BattingSide = sourceEvent.BattingSide,
                TeamCode = context.GetTeamCode(sourceEvent.BattingSide),
                RawText = option.Text,
                StateBefore = sourceEvent.StateBefore,
            };

            var text = option.Text?.Trim() ?? string.Empty;
            var match = RunnerRegex.Match(text);
            if (!match.Success)
            {
                return result;
            }

            if (!int.TryParse(match.Groups["from"].Value, out var fromBase))
            {
                return result;
            }

            var runnerName = match.Groups["name"].Value.Trim();
            var action = match.Groups["action"].Value.Trim();
            var rosterPlayer = context.FindByName(runnerName, sourceEvent.BattingSide);
            var stateRunner = ResolveRunnerFromState(sourceEvent.StateBefore, fromBase);

            result.FromBase = fromBase;
            result.RunnerName = runnerName;
            result.RunnerPcode = rosterPlayer?.Pcode
                ?? (stateRunner.HasValue ? stateRunner.Value.pcode : null);
            result.IsOut = action.Contains("아웃", StringComparison.Ordinal);
            result.ToBase = ParseDestination(action, fromBase, result.IsOut);
            result.IsRun = !result.IsOut && result.ToBase == 4;
            result.EventType = ClassifyEventType(action, result.IsOut, result.IsRun);
            result.Reason = ClassifyReason(action, result.EventType);
            result.WasParsed = true;
            return result;
        }

        private static int? ParseDestination(string action, int fromBase, bool isOut)
        {
            if (action.Contains("홈인", StringComparison.Ordinal))
            {
                return 4;
            }

            var advance = AdvanceRegex.Match(action);
            if (advance.Success && int.TryParse(advance.Groups["to"].Value, out var advanceBase))
            {
                return advanceBase;
            }

            var forced = ForceBaseRegex.Match(action);
            if (forced.Success && int.TryParse(forced.Groups["to"].Value, out var forceBase))
            {
                return forceBase;
            }

            if (action.Contains("견제사", StringComparison.Ordinal))
            {
                return fromBase;
            }

            if (action.Contains("도루실패", StringComparison.Ordinal)
                || action.Contains("태그아웃", StringComparison.Ordinal)
                || (isOut && action.Contains("포스아웃", StringComparison.Ordinal)))
            {
                return Math.Min(4, fromBase + 1);
            }

            return null;
        }

        private static RunnerEventType ClassifyEventType(string action, bool isOut, bool isRun)
        {
            if (action.Contains("견제사", StringComparison.Ordinal)) return RunnerEventType.Pickoff;
            if (action.Contains("도루실패", StringComparison.Ordinal)) return RunnerEventType.CaughtStealing;
            if (action.Contains("포스아웃", StringComparison.Ordinal)) return RunnerEventType.ForceOut;
            if (action.Contains("태그아웃", StringComparison.Ordinal)) return RunnerEventType.TagOut;
            if (isRun) return RunnerEventType.Scored;
            if (!isOut) return RunnerEventType.Advance;
            return RunnerEventType.Unknown;
        }

        private static RunnerAdvanceReason ClassifyReason(string action, RunnerEventType eventType)
        {
            if (action.Contains("폭투", StringComparison.Ordinal)) return RunnerAdvanceReason.WildPitch;
            if (action.Contains("도루실패", StringComparison.Ordinal)) return RunnerAdvanceReason.CaughtStealing;
            if (action.Contains("도루로", StringComparison.Ordinal)) return RunnerAdvanceReason.StolenBase;
            if (action.Contains("견제사", StringComparison.Ordinal)) return RunnerAdvanceReason.Pickoff;
            if (action.Contains("실책", StringComparison.Ordinal)) return RunnerAdvanceReason.Error;
            if (action.Contains("다른주자수비", StringComparison.Ordinal)) return RunnerAdvanceReason.OtherRunnerPlay;
            if (action.Contains("주루방해", StringComparison.Ordinal)) return RunnerAdvanceReason.Obstruction;
            if (action.Contains("주자의 재치", StringComparison.Ordinal)) return RunnerAdvanceReason.BaserunningPlay;
            if (eventType == RunnerEventType.ForceOut) return RunnerAdvanceReason.ForceOut;
            if (eventType == RunnerEventType.TagOut) return RunnerAdvanceReason.TagOut;
            return RunnerAdvanceReason.BatterPlay;
        }

        private static (string? pcode, string? name)? ResolveRunnerFromState(GameStateSnapshot? state, int fromBase)
        {
            if (state == null)
            {
                return null;
            }

            return fromBase switch
            {
                1 => (state.FirstBaseRunnerPcode, state.FirstBaseRunnerName),
                2 => (state.SecondBaseRunnerPcode, state.SecondBaseRunnerName),
                3 => (state.ThirdBaseRunnerPcode, state.ThirdBaseRunnerName),
                _ => null,
            };
        }
    }
}
