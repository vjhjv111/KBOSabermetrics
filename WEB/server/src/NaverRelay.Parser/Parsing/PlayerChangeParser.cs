using System;
using System.Text.RegularExpressions;
using NaverRelay.Models;

namespace NaverRelay.Parsing
{
    internal static class PlayerChangeParser
    {
        private static readonly Regex BatOrderRegex = new(
            @"(?<order>[1-9])번타자",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static PlayerChangeEvent Parse(
            TextOption option,
            NormalizedEvent sourceEvent,
            ParsingContext context)
        {
            var raw = option.PlayerChange;
            var result = new PlayerChangeEvent
            {
                PlayerChangeEventId = sourceEvent.EventId + ":change",
                SourceEventId = sourceEvent.EventId,
                GameId = sourceEvent.GameId,
                RelayGroupId = sourceEvent.RelayGroupId,
                PlateAppearanceId = sourceEvent.PlateAppearanceId,
                SourceRelayNo = sourceEvent.SourceRelayNo,
                SourceSeqNo = sourceEvent.SourceSeqNo,
                SourceOptionIndex = sourceEvent.SourceOptionIndex,
                Inning = sourceEvent.Inning,
                BattingSide = sourceEvent.BattingSide,
                RawChangeType = raw?.Type,
                RawText = option.Text ?? raw?.LiveText,
                StateBefore = sourceEvent.StateBefore,
            };

            var rawType = raw?.Type?.Trim().ToLowerInvariant();
            if (rawType == "substitution" && raw != null)
            {
                ParseStructuredSubstitution(raw, result, context);
            }
            else if (rawType == "shift" && raw != null)
            {
                ParseStructuredShift(raw, result, context);
            }
            else
            {
                ParseFromText(result, context);
            }

            FinalizeDerivedValues(result, context);
            return result;
        }

        private static void ParseStructuredSubstitution(
            PlayerChange raw,
            PlayerChangeEvent result,
            ParsingContext context)
        {
            result.ChangeType = PlayerChangeType.Substitution;
            result.OutPlayerPcode = raw.OutPlayer?.PlayerId;
            result.OutPlayerName = raw.OutPlayer?.PlayerName;
            result.OutPosition = raw.OutPlayer?.PlayerPos;
            result.InPlayerPcode = raw.InPlayer?.PlayerId;
            result.InPlayerName = raw.InPlayer?.PlayerName;
            result.InPosition = raw.InPlayer?.PlayerPos;
            result.SourceOutPlayerTurn = raw.InPlayer?.OutPlayerTurn;
            result.BatOrder = ParserUtilities.BatOrderFromOutPlayerTurn(result.SourceOutPlayerTurn);

            FillPlayerCodeFromRoster(result, context);
            result.WasParsed = !string.IsNullOrWhiteSpace(result.InPlayerName)
                && !string.IsNullOrWhiteSpace(result.OutPlayerName);
        }

        private static void ParseStructuredShift(
            PlayerChange raw,
            PlayerChangeEvent result,
            ParsingContext context)
        {
            result.ChangeType = PlayerChangeType.PositionShift;
            result.ShiftPlayerPcode = raw.ShiftPlayer?.PlayerId;
            result.ShiftPlayerName = raw.ShiftPlayer?.PlayerName;
            result.OldPosition = raw.ShiftPlayer?.PlayerPos;
            result.NewPosition = ParseNewPosition(raw.ShiftMessage, result.RawText);
            result.SourceOutPlayerTurn = raw.ShiftPlayer?.OutPlayerTurn;
            result.BatOrder = ParserUtilities.BatOrderFromOutPlayerTurn(result.SourceOutPlayerTurn);

            if (string.IsNullOrWhiteSpace(result.ShiftPlayerPcode))
            {
                result.ShiftPlayerPcode = context.FindByName(result.ShiftPlayerName)?.Pcode;
            }

            result.WasParsed = !string.IsNullOrWhiteSpace(result.ShiftPlayerName)
                && !string.IsNullOrWhiteSpace(result.NewPosition);
        }

        private static void ParseFromText(PlayerChangeEvent result, ParsingContext context)
        {
            var text = result.RawText?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                result.ChangeType = PlayerChangeType.TextOnly;
                result.WasParsed = false;
                return;
            }

            var separator = text.IndexOf(" : ", StringComparison.Ordinal);
            if (separator < 0)
            {
                result.ChangeType = PlayerChangeType.TextOnly;
                result.WasParsed = false;
                return;
            }

            var left = text[..separator].Trim();
            var right = text[(separator + 3)..].Trim();
            var leftPerson = SplitPersonDescriptor(left);

            if (text.Contains("수비위치 변경", StringComparison.Ordinal))
            {
                result.ChangeType = PlayerChangeType.PositionShift;
                result.OldPosition = leftPerson.position;
                result.ShiftPlayerName = leftPerson.name;
                result.NewPosition = ParseNewPosition(right, text);
                result.ShiftPlayerPcode = context.FindByName(result.ShiftPlayerName)?.Pcode;
                result.WasParsed = !string.IsNullOrWhiteSpace(result.ShiftPlayerName)
                    && !string.IsNullOrWhiteSpace(result.NewPosition);
                return;
            }

            if (text.Contains("교체", StringComparison.Ordinal))
            {
                result.ChangeType = PlayerChangeType.Substitution;
                var cleanedRight = Regex.Replace(right, @"\s*\(으\)로\s*교체\s*$", string.Empty).Trim();
                var rightPerson = SplitPersonDescriptor(cleanedRight);
                result.OutPosition = leftPerson.position;
                result.OutPlayerName = leftPerson.name;
                result.InPosition = rightPerson.position;
                result.InPlayerName = rightPerson.name;
                FillPlayerCodeFromRoster(result, context);

                var orderMatch = BatOrderRegex.Match(left);
                if (orderMatch.Success && int.TryParse(orderMatch.Groups["order"].Value, out var order))
                {
                    result.BatOrder = order;
                }

                result.WasParsed = !string.IsNullOrWhiteSpace(result.OutPlayerName)
                    && !string.IsNullOrWhiteSpace(result.InPlayerName);
                return;
            }

            result.ChangeType = PlayerChangeType.TextOnly;
            result.WasParsed = false;
        }

        private static void FinalizeDerivedValues(PlayerChangeEvent result, ParsingContext context)
        {
            TeamSide teamSide;
            if (result.ChangeType == PlayerChangeType.PositionShift)
            {
                teamSide = context.ResolveTeam(result.ShiftPlayerPcode, result.ShiftPlayerName);
                result.BatOrder ??= context.FindActiveBatOrder(teamSide,
                    result.ShiftPlayerPcode, result.ShiftPlayerName);
            }
            else
            {
                teamSide = context.ResolveTeam(result.InPlayerPcode, result.InPlayerName,
                    context.ResolveTeam(result.OutPlayerPcode, result.OutPlayerName));
                result.BatOrder ??= context.FindActiveBatOrder(teamSide,
                    result.OutPlayerPcode, result.OutPlayerName);
            }

            result.ChangedTeamSide = teamSide;
            result.TeamCode = context.GetTeamCode(teamSide);
            result.IsPitcherChange = ContainsPosition(result.OutPosition, "투수")
                || ContainsPosition(result.InPosition, "투수")
                || ContainsPosition(result.OldPosition, "투수")
                || ContainsPosition(result.NewPosition, "투수");
            result.IsPinchHitter = ContainsPosition(result.InPosition, "대타");
            result.IsPinchRunner = ContainsPosition(result.InPosition, "대주자");
        }

        private static void FillPlayerCodeFromRoster(PlayerChangeEvent result, ParsingContext context)
        {
            if (string.IsNullOrWhiteSpace(result.OutPlayerPcode))
            {
                result.OutPlayerPcode = context.FindByName(result.OutPlayerName)?.Pcode;
            }

            if (string.IsNullOrWhiteSpace(result.InPlayerPcode))
            {
                result.InPlayerPcode = context.FindByName(result.InPlayerName)?.Pcode;
            }
        }

        private static (string? position, string? name) SplitPersonDescriptor(string text)
        {
            var trimmed = text.Trim();
            var separator = trimmed.LastIndexOf(' ');
            if (separator <= 0 || separator >= trimmed.Length - 1)
            {
                return (null, trimmed.Length == 0 ? null : trimmed);
            }

            return (trimmed[..separator].Trim(), trimmed[(separator + 1)..].Trim());
        }

        private static string? ParseNewPosition(string? shiftMessage, string? fallbackText)
        {
            var source = shiftMessage;
            if (string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(fallbackText))
            {
                var separator = fallbackText.IndexOf(" : ", StringComparison.Ordinal);
                source = separator >= 0 ? fallbackText[(separator + 3)..] : fallbackText;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            return Regex.Replace(source.Trim(), @"\s*\(으\)로\s*수비위치\s*변경\s*$", string.Empty).Trim();
        }

        private static bool ContainsPosition(string? position, string value)
        {
            return !string.IsNullOrWhiteSpace(position)
                && position.Contains(value, StringComparison.Ordinal);
        }
    }
}
