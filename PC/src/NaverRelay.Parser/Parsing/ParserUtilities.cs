using System;
using System.Globalization;

namespace NaverRelay.Parsing
{
    internal static class ParserUtilities
    {
        public const double HalfPlateWidthFeet = 8.5 / 12.0;

        public static int? ParseInt(string? value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        public static double? ParseDouble(string? value)
        {
            return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        public static TeamSide ParseBattingSide(string? raw)
        {
            return raw switch
            {
                "0" => TeamSide.Away,
                "1" => TeamSide.Home,
                _ => TeamSide.Unknown,
            };
        }

        public static TeamSide Opposite(TeamSide side)
        {
            return side switch
            {
                TeamSide.Away => TeamSide.Home,
                TeamSide.Home => TeamSide.Away,
                _ => TeamSide.Unknown,
            };
        }

        public static NormalizedEventType MapEventType(int? rawType)
        {
            return rawType switch
            {
                0 => NormalizedEventType.InningMarker,
                1 => NormalizedEventType.Pitch,
                2 => NormalizedEventType.PlayerChange,
                7 => NormalizedEventType.Administrative,
                8 => NormalizedEventType.PlateAppearanceStart,
                13 or 23 => NormalizedEventType.BatterResult,
                14 or 24 => NormalizedEventType.RunnerResult,
                99 => NormalizedEventType.GameSummary,
                _ => NormalizedEventType.Unknown,
            };
        }

        public static string RelayGroupId(string gameId, int? relayNo, int chronologicalIndex)
        {
            var sourcePart = relayNo?.ToString(CultureInfo.InvariantCulture) ?? "none";
            var indexPart = chronologicalIndex.ToString(CultureInfo.InvariantCulture);
            return $"{gameId}:relay:{sourcePart}:idx{indexPart}";
        }

        public static string EventId(string gameId, int? relayNo, int relayIndex, int optionIndex)
        {
            var sourcePart = relayNo?.ToString(CultureInfo.InvariantCulture) ?? "none";
            var relayIndexPart = relayIndex.ToString(CultureInfo.InvariantCulture);
            var optionIndexPart = optionIndex.ToString(CultureInfo.InvariantCulture);
            return $"{gameId}:event:{sourcePart}:idx{relayIndexPart}:{optionIndexPart}";
        }

        public static string PlateAppearanceId(string gameId, int? relayNo, int relayIndex)
        {
            var sourcePart = relayNo?.ToString(CultureInfo.InvariantCulture) ?? "none";
            var indexPart = relayIndex.ToString(CultureInfo.InvariantCulture);
            return $"{gameId}:pa:{sourcePart}:idx{indexPart}";
        }

        public static string? TextAfterColon(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var separator = text.IndexOf(" : ", StringComparison.Ordinal);
            return separator >= 0 ? text[(separator + 3)..].Trim() : text.Trim();
        }

        public static int PositiveDifference(int? after, int? before)
        {
            if (!after.HasValue || !before.HasValue)
            {
                return 0;
            }

            return Math.Max(0, after.Value - before.Value);
        }

        public static int? BatOrderFromOutPlayerTurn(int? outPlayerTurn)
        {
            if (!outPlayerTurn.HasValue)
            {
                return null;
            }

            var order = Math.Abs(outPlayerTurn.Value) % 10;
            return order >= 1 && order <= 9 ? order : null;
        }

        public static bool IsTrueString(string? value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
