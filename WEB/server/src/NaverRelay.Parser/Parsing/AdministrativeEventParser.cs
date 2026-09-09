using System;
using System.Text.RegularExpressions;
using NaverRelay.Models;

namespace NaverRelay.Parsing
{
    internal static class AdministrativeEventParser
    {
        private static readonly Regex ReviewCallRegex = new(
            @"관련\s+(?<before>[^→]+)→(?<after>.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static AdministrativeEvent Parse(TextOption option, NormalizedEvent sourceEvent)
        {
            var text = option.Text?.Trim() ?? string.Empty;
            var result = new AdministrativeEvent
            {
                AdministrativeEventId = sourceEvent.EventId + ":admin",
                SourceEventId = sourceEvent.EventId,
                GameId = sourceEvent.GameId,
                RelayGroupId = sourceEvent.RelayGroupId,
                PlateAppearanceId = sourceEvent.PlateAppearanceId,
                SourceRelayNo = sourceEvent.SourceRelayNo,
                SourceSeqNo = sourceEvent.SourceSeqNo,
                SourceOptionIndex = sourceEvent.SourceOptionIndex,
                Inning = sourceEvent.Inning,
                BattingSide = sourceEvent.BattingSide,
                RawText = option.Text,
                StateBefore = sourceEvent.StateBefore,
            };

            if (text.Contains("퇴장", StringComparison.Ordinal))
            {
                result.EventType = AdministrativeEventType.Ejection;
                result.WasRecognized = true;
            }
            else if (text.Contains("피치클락", StringComparison.Ordinal))
            {
                result.EventType = AdministrativeEventType.PitchClockViolation;
                result.AutomaticBallDelta = text.Contains("볼", StringComparison.Ordinal) ? 1 : 0;
                result.AutomaticStrikeDelta = text.Contains("스트라이크", StringComparison.Ordinal) ? 1 : 0;
                result.WasRecognized = true;
            }
            else if (text.Contains("비디오 판독", StringComparison.Ordinal)
                     || text.Contains("비디오판독", StringComparison.Ordinal))
            {
                result.EventType = AdministrativeEventType.VideoReview;
                ParseReviewCalls(text, result);
                result.WasRecognized = true;
            }
            else if (text.Contains("코칭스태프 마운드 방문", StringComparison.Ordinal))
            {
                result.EventType = AdministrativeEventType.MoundVisitByCoach;
                result.WasRecognized = true;
            }
            else if (text.Contains("포수 마운드 방문", StringComparison.Ordinal))
            {
                result.EventType = AdministrativeEventType.MoundVisitByCatcher;
                result.WasRecognized = true;
            }
            else if (text.Contains("투수판 이탈", StringComparison.Ordinal))
            {
                result.EventType = AdministrativeEventType.PitcherDisengagement;
                result.WasRecognized = true;
            }

            return result;
        }

        private static void ParseReviewCalls(string text, AdministrativeEvent result)
        {
            var match = ReviewCallRegex.Match(text);
            if (!match.Success)
            {
                return;
            }

            result.ReviewOriginalCall = match.Groups["before"].Value.Trim();
            result.ReviewFinalCall = match.Groups["after"].Value.Trim();
            result.ReviewOverturned = !string.Equals(
                result.ReviewOriginalCall,
                result.ReviewFinalCall,
                StringComparison.Ordinal);
        }
    }
}
