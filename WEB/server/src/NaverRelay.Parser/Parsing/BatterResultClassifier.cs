using System;
using System.Text.RegularExpressions;

namespace NaverRelay.Parsing
{
    internal static class BatterResultClassifier
    {
        private static readonly Regex HomeRunDistanceRegex = new(
            @"홈런거리\s*:\s*(?<meters>\d+)\s*M",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static BattingOutcome Classify(string? rawText)
        {
            var normalized = ParserUtilities.TextAfterColon(rawText) ?? string.Empty;
            var outcome = new BattingOutcome
            {
                NormalizedResultText = normalized,
                BattedBallType = ClassifyBattedBallType(normalized),
                FieldDirection = ClassifyFieldDirection(normalized),
                PrimaryFielder = DetectPrimaryFielder(normalized),
                HomeRunDistanceMeters = ParseHomeRunDistance(normalized),
            };

            if (Contains(normalized, "자동 고의4구"))
            {
                Set(outcome, BattingResultType.IntentionalWalk, countsAsAtBat: false,
                    isHit: false, isOut: false, isSacrifice: false, isWalk: true,
                    isIntentionalWalk: true, isStrikeout: false, reachedBase: true, totalBases: 0);
            }
            else if (Contains(normalized, "고의4구"))
            {
                Set(outcome, BattingResultType.IntentionalWalk, countsAsAtBat: false,
                    isHit: false, isOut: false, isSacrifice: false, isWalk: true,
                    isIntentionalWalk: true, isStrikeout: false, reachedBase: true, totalBases: 0);
            }
            else if (Contains(normalized, "볼넷"))
            {
                Set(outcome, BattingResultType.Walk, countsAsAtBat: false,
                    isHit: false, isOut: false, isSacrifice: false, isWalk: true,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: true, totalBases: 0);
            }
            else if (Contains(normalized, "몸에 맞는 볼"))
            {
                Set(outcome, BattingResultType.HitByPitch, countsAsAtBat: false,
                    isHit: false, isOut: false, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: true, totalBases: 0);
            }
            else if (Contains(normalized, "홈런"))
            {
                SetHit(outcome, BattingResultType.HomeRun, 4);
            }
            else if (Contains(normalized, "3루타"))
            {
                SetHit(outcome, BattingResultType.Triple, 3);
            }
            else if (Contains(normalized, "2루타"))
            {
                SetHit(outcome, BattingResultType.Double, 2);
            }
            else if (Contains(normalized, "번트안타"))
            {
                SetHit(outcome, BattingResultType.BuntSingle, 1);
            }
            else if (Contains(normalized, "내야안타"))
            {
                SetHit(outcome, BattingResultType.InfieldSingle, 1);
            }
            else if (Contains(normalized, "1루타"))
            {
                SetHit(outcome, BattingResultType.Single, 1);
            }
            else if (Contains(normalized, "스트라이크 낫 아웃") || Contains(normalized, "삼진 아웃"))
            {
                Set(outcome, BattingResultType.Strikeout, countsAsAtBat: true,
                    isHit: false, isOut: true, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: true, reachedBase: false, totalBases: 0);
            }
            else if (Contains(normalized, "희생플라이 아웃"))
            {
                Set(outcome, BattingResultType.SacrificeFly, countsAsAtBat: false,
                    isHit: false, isOut: true, isSacrifice: true, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: false, totalBases: 0);
            }
            else if (Contains(normalized, "희생번트 아웃"))
            {
                Set(outcome, BattingResultType.SacrificeBunt, countsAsAtBat: false,
                    isHit: false, isOut: true, isSacrifice: true, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: false, totalBases: 0);
            }
            else if (Contains(normalized, "병살타 아웃"))
            {
                Set(outcome, BattingResultType.GroundedIntoDoublePlay, countsAsAtBat: true,
                    isHit: false, isOut: true, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: false, totalBases: 0);
            }
            else if (Contains(normalized, "실책으로 출루"))
            {
                Set(outcome, BattingResultType.ReachedOnError, countsAsAtBat: true,
                    isHit: false, isOut: false, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: true, totalBases: 0);
            }
            else if (Contains(normalized, "땅볼로 출루"))
            {
                Set(outcome, BattingResultType.FieldersChoice, countsAsAtBat: true,
                    isHit: false, isOut: false, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: true, totalBases: 0);
            }
            else if (Contains(normalized, "번트") && Contains(normalized, "아웃"))
            {
                Set(outcome, BattingResultType.BuntOut, countsAsAtBat: true,
                    isHit: false, isOut: true, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: false, totalBases: 0);
            }
            else if (Contains(normalized, "인필드플라이 아웃"))
            {
                Set(outcome, BattingResultType.InfieldFlyOut, countsAsAtBat: true,
                    isHit: false, isOut: true, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: false, totalBases: 0);
            }
            else if (Contains(normalized, "파울플라이 아웃"))
            {
                Set(outcome, BattingResultType.FoulFlyOut, countsAsAtBat: true,
                    isHit: false, isOut: true, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: false, totalBases: 0);
            }
            else if (Contains(normalized, "라인드라이브 아웃"))
            {
                Set(outcome, BattingResultType.LineOut, countsAsAtBat: true,
                    isHit: false, isOut: true, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: false, totalBases: 0);
            }
            else if (Contains(normalized, "땅볼 아웃"))
            {
                Set(outcome, BattingResultType.GroundOut, countsAsAtBat: true,
                    isHit: false, isOut: true, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: false, totalBases: 0);
            }
            else if (Contains(normalized, "플라이 아웃"))
            {
                Set(outcome, BattingResultType.FlyOut, countsAsAtBat: true,
                    isHit: false, isOut: true, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: false, totalBases: 0);
            }
            else if (Contains(normalized, "아웃"))
            {
                Set(outcome, BattingResultType.OtherOut, countsAsAtBat: true,
                    isHit: false, isOut: true, isSacrifice: false, isWalk: false,
                    isIntentionalWalk: false, isStrikeout: false, reachedBase: false, totalBases: 0);
            }

            return outcome;
        }

        private static void SetHit(BattingOutcome outcome, BattingResultType resultType, int totalBases)
        {
            Set(outcome, resultType, countsAsAtBat: true, isHit: true, isOut: false,
                isSacrifice: false, isWalk: false, isIntentionalWalk: false,
                isStrikeout: false, reachedBase: true, totalBases: totalBases);
        }

        private static void Set(
            BattingOutcome outcome,
            BattingResultType resultType,
            bool countsAsAtBat,
            bool isHit,
            bool isOut,
            bool isSacrifice,
            bool isWalk,
            bool isIntentionalWalk,
            bool isStrikeout,
            bool reachedBase,
            int totalBases)
        {
            outcome.ResultType = resultType;
            outcome.CountsAsAtBat = countsAsAtBat;
            outcome.IsHit = isHit;
            outcome.IsOut = isOut;
            outcome.IsSacrifice = isSacrifice;
            outcome.IsWalk = isWalk;
            outcome.IsIntentionalWalk = isIntentionalWalk;
            outcome.IsStrikeout = isStrikeout;
            outcome.ReachedBase = reachedBase;
            outcome.TotalBases = totalBases;
            outcome.WasRecognized = true;
        }

        private static BattedBallType ClassifyBattedBallType(string text)
        {
            if (Contains(text, "번트")) return BattedBallType.Bunt;
            if (Contains(text, "라인드라이브")) return BattedBallType.LineDrive;
            if (Contains(text, "인필드플라이")) return BattedBallType.PopUp;
            if (Contains(text, "플라이")) return BattedBallType.FlyBall;
            if (Contains(text, "땅볼")) return BattedBallType.GroundBall;
            return BattedBallType.Unknown;
        }

        private static FieldDirection ClassifyFieldDirection(string text)
        {
            if (Contains(text, "좌중간")) return FieldDirection.LeftCenter;
            if (Contains(text, "우중간")) return FieldDirection.RightCenter;
            if (Contains(text, "좌익수")) return FieldDirection.Left;
            if (Contains(text, "중견수")) return FieldDirection.Center;
            if (Contains(text, "우익수")) return FieldDirection.Right;
            if (Contains(text, "유격수")) return FieldDirection.Shortstop;
            if (Contains(text, "3루수")) return FieldDirection.ThirdBase;
            if (Contains(text, "2루수")) return FieldDirection.SecondBase;
            if (Contains(text, "1루수")) return FieldDirection.FirstBase;
            if (Contains(text, "포수")) return FieldDirection.Catcher;
            if (Contains(text, "투수")) return FieldDirection.Pitcher;
            return FieldDirection.Unknown;
        }

        private static string? DetectPrimaryFielder(string text)
        {
            string[] positions =
            {
                "투수", "포수", "1루수", "2루수", "3루수", "유격수", "좌익수", "중견수", "우익수",
            };

            foreach (var position in positions)
            {
                if (Contains(text, position))
                {
                    return position;
                }
            }

            return null;
        }

        private static int? ParseHomeRunDistance(string text)
        {
            var match = HomeRunDistanceRegex.Match(text);
            return match.Success && int.TryParse(match.Groups["meters"].Value, out var meters)
                ? meters
                : null;
        }

        private static bool Contains(string source, string value)
        {
            return source.Contains(value, StringComparison.Ordinal);
        }
    }
}
