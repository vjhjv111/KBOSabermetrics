using System.Globalization;
using System.Text.RegularExpressions;
using NaverRelay.Parsing;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

/// <summary>
/// NormalizedGame을 SQLite 관계형 행과 경기 단위 집계 행으로 변환합니다.
/// 이 변환은 원본 JSON을 처음 가져올 때 한 번만 실행됩니다.
/// </summary>
internal static class WarehouseProjectionBuilder
{
    public static WarehouseGameProjection Build(NormalizedGame game)
    {
        var batters = new Dictionary<WarehousePlayerKey, BatterGameAggregate>();
        var pitchers = new Dictionary<WarehousePlayerKey, PitcherGameAggregate>();
        var observations = new Dictionary<WarehousePlayerKey, WarehousePlayerObservation>();

        var officialPas = game.PlateAppearances.Where(pa => pa.IsOfficialPlateAppearance).ToList();
        var paById = officialPas
            .Where(pa => !string.IsNullOrWhiteSpace(pa.PlateAppearanceId))
            .GroupBy(pa => pa.PlateAppearanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var pa in officialPas)
        {
            var batterKey = Key(pa.BatterPcode, pa.BatterName, pa.BattingTeamCode);
            var pitcherKey = Key(pa.PitcherPcode, pa.PitcherName, pa.FieldingTeamCode);
            var batter = GetBatter(batters, game.GameId, batterKey, pa.BatterName);
            var pitcher = GetPitcher(pitchers, game.GameId, pitcherKey, pa.PitcherName);

            batter.PlateAppearances++;
            if (pa.Outcome.CountsAsAtBat) batter.AtBats++;
            if (pa.Outcome.IsHit) batter.Hits++;
            if (pa.Outcome.ResultType is BattingResultType.Single or BattingResultType.InfieldSingle or BattingResultType.BuntSingle) batter.Singles++;
            if (pa.Outcome.ResultType == BattingResultType.Double) batter.Doubles++;
            if (pa.Outcome.ResultType == BattingResultType.Triple) batter.Triples++;
            if (pa.Outcome.ResultType == BattingResultType.HomeRun) batter.HomeRuns++;
            if (pa.Outcome.IsWalk) batter.Walks++;
            if (pa.Outcome.IsIntentionalWalk) batter.IntentionalWalks++;
            if (pa.Outcome.ResultType == BattingResultType.HitByPitch) batter.HitByPitch++;
            if (pa.Outcome.IsStrikeout) batter.Strikeouts++;
            if (pa.Outcome.ResultType == BattingResultType.SacrificeFly) batter.SacrificeFlies++;
            if (pa.Outcome.ResultType == BattingResultType.SacrificeBunt) batter.SacrificeBunts++;
            if (pa.Outcome.ResultType == BattingResultType.GroundedIntoDoublePlay) batter.DoublePlays++;
            batter.TotalBases += pa.Outcome.TotalBases;
            batter.OutsRecorded += Math.Max(0, pa.OutsRecorded);
            batter.RunsScoredOnPlays += Math.Max(0, pa.RunsScored);
            batter.Wpa += pa.WpaByPlate ?? 0.0;
            if (IsFlyBall(pa)) batter.FlyBalls++;

            pitcher.BattersFaced++;
            pitcher.PlateAppearanceOuts += Math.Max(0, pa.OutsRecorded);
            pitcher.RunsFromPlateAppearances += Math.Max(0, pa.RunsScored);
            if (pa.Outcome.IsHit) pitcher.HitsFromPlateAppearances++;
            if (pa.Outcome.ResultType == BattingResultType.HomeRun) pitcher.HomeRunsFromPlateAppearances++;
            if (pa.Outcome.IsWalk) pitcher.WalksFromPlateAppearances++;
            if (pa.Outcome.ResultType == BattingResultType.HitByPitch) pitcher.HitBattersFromPlateAppearances++;
            if (pa.Outcome.IsStrikeout) pitcher.StrikeoutsFromPlateAppearances++;
            if (pa.Outcome.ResultType == BattingResultType.SacrificeFly) pitcher.SacrificeFlies++;
            if (IsFlyBall(pa)) pitcher.FlyBalls++;
            if (IsInfieldFly(pa)) pitcher.InfieldFlies++;

            MergeObservation(observations, batterKey, pa.BatterName, pa.BattingTeamCode, null, null, null, true, false, pa.BatOrder, null);
            MergeObservation(observations, pitcherKey, pa.PitcherName, pa.FieldingTeamCode, null, "투수", null, false, true, null, null);
        }

        foreach (var pitch in game.PitchEvents)
        {
            paById.TryGetValue(pitch.PlateAppearanceId ?? string.Empty, out var pa);
            var battingTeam = pa?.BattingTeamCode ?? TeamForSide(game, pitch.BattingSide);
            var fieldingTeam = pa?.FieldingTeamCode ?? OppositeTeamForSide(game, pitch.BattingSide);
            var batterKey = Key(pitch.BatterPcode, pitch.BatterName, battingTeam);
            var pitcherKey = Key(pitch.PitcherPcode, pitch.PitcherName, fieldingTeam);
            var batter = GetBatter(batters, game.GameId, batterKey, pitch.BatterName);
            var pitcher = GetPitcher(pitchers, game.GameId, pitcherKey, pitch.PitcherName);
            AddDiscipline(batter, pitch);
            AddDiscipline(pitcher, pitch);
            if (pitch.SpeedKmh.HasValue)
            {
                pitcher.SpeedSum += pitch.SpeedKmh.Value;
                pitcher.SpeedCount++;
            }
            MergeObservation(observations, batterKey, pitch.BatterName, battingTeam, null, null, null, true, false, null, null);
            MergeObservation(observations, pitcherKey, pitch.PitcherName, fieldingTeam, null, "투수", null, false, true, null, null);
        }

        foreach (var runner in game.RunnerEvents)
        {
            var key = Key(runner.RunnerPcode, runner.RunnerName, runner.TeamCode);
            var batter = GetBatter(batters, game.GameId, key, runner.RunnerName);
            if (runner.Reason == RunnerAdvanceReason.StolenBase && !runner.IsOut) batter.StolenBases++;
            if (runner.EventType == RunnerEventType.CaughtStealing || runner.Reason == RunnerAdvanceReason.CaughtStealing)
                batter.CaughtStealing++;
            MergeObservation(observations, key, runner.RunnerName, runner.TeamCode, null, null, null, true, false, null, null);
        }

        foreach (var line in game.BattingLines)
        {
            var key = Key(line.Pcode, line.Name, line.TeamCode);
            var batter = GetBatter(batters, game.GameId, key, line.Name);
            batter.Runs += line.Runs ?? 0;
            batter.RunsBattedIn += line.RunsBattedIn ?? 0;
            AddPosition(batter, line.Position, line.EnteredAsSubstitute, line.PlateAppearances ?? 0);
            MergeObservation(
                observations,
                key,
                line.Name,
                line.TeamCode,
                NormalizeBirthDate(line.BirthDateRaw),
                line.Position,
                line.HitType,
                true,
                false,
                line.BatOrder,
                line.LineupSequence);
        }

        foreach (var line in game.PitchingLines)
        {
            var key = Key(line.Pcode, line.Name, line.TeamCode);
            var pitcher = GetPitcher(pitchers, game.GameId, key, line.Name);
            pitcher.HasFinalLine = true;
            pitcher.AppearanceSequence = line.AppearanceSequence ?? pitcher.AppearanceSequence;
            var outs = ParseInningsOuts(line.InningsDisplay);
            pitcher.InningsOuts += outs;
            pitcher.HitsAllowed += line.HitsAllowed ?? 0;
            pitcher.HomeRunsAllowed += line.HomeRunsAllowed ?? 0;
            pitcher.FinalWalks += line.Walks ?? 0;
            pitcher.FinalHitBatters += line.HitBatters ?? 0;
            pitcher.FinalStrikeouts += line.Strikeouts ?? 0;
            pitcher.RunsAllowed += line.RunsAllowed ?? 0;
            pitcher.EarnedRuns += line.EarnedRuns ?? 0;
            pitcher.WildPitches += line.WildPitches ?? 0;
            pitcher.FinalPitchCount += line.PitchCount ?? 0;
            var isStarter = (line.AppearanceSequence ?? int.MaxValue) == 1;
            pitcher.IsStarter |= isStarter;
            pitcher.IsReliever |= !isStarter;
            MergeObservation(
                observations,
                key,
                line.Name,
                line.TeamCode,
                NormalizeBirthDate(line.BirthDateRaw),
                "투수",
                line.HitType,
                false,
                true,
                null,
                line.AppearanceSequence);
        }

        AddReliefLeverage(game, pitchers, observations);

        foreach (var change in game.PlayerChanges)
        {
            if (!string.IsNullOrWhiteSpace(change.InPlayerPcode) || !string.IsNullOrWhiteSpace(change.InPlayerName))
            {
                var key = Key(change.InPlayerPcode, change.InPlayerName, change.TeamCode);
                MergeObservation(observations, key, change.InPlayerName, change.TeamCode, null, change.InPosition,
                    null, !change.IsPitcherChange, change.IsPitcherChange, change.BatOrder, null);
            }
            if (!string.IsNullOrWhiteSpace(change.OutPlayerPcode) || !string.IsNullOrWhiteSpace(change.OutPlayerName))
            {
                var key = Key(change.OutPlayerPcode, change.OutPlayerName, change.TeamCode);
                MergeObservation(observations, key, change.OutPlayerName, change.TeamCode, null, change.OutPosition,
                    null, !change.IsPitcherChange, change.IsPitcherChange, change.BatOrder, null);
            }
        }

        return new WarehouseGameProjection
        {
            Players = observations.Values.OrderBy(item => item.Pcode, StringComparer.Ordinal).ThenBy(item => item.TeamCode, StringComparer.Ordinal).ToList(),
            BatterGames = batters.Values.OrderBy(item => item.Pcode, StringComparer.Ordinal).ThenBy(item => item.TeamCode, StringComparer.Ordinal).ToList(),
            PitcherGames = pitchers.Values.OrderBy(item => item.Pcode, StringComparer.Ordinal).ThenBy(item => item.TeamCode, StringComparer.Ordinal).ToList(),
        };
    }

    private static void AddReliefLeverage(
        NormalizedGame game,
        IDictionary<WarehousePlayerKey, PitcherGameAggregate> pitchers,
        IDictionary<WarehousePlayerKey, WarehousePlayerObservation> observations)
    {
        var groups = game.RelayGroups.OrderBy(group => group.ChronologicalIndex).ToList();
        var indexById = groups.Select((group, index) => (group.RelayGroupId, index))
            .GroupBy(item => item.RelayGroupId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);

        foreach (var change in game.PlayerChanges.Where(change => change.IsPitcherChange))
        {
            if (string.IsNullOrWhiteSpace(change.InPlayerPcode) && string.IsNullOrWhiteSpace(change.InPlayerName)) continue;
            var team = change.TeamCode;
            if (string.IsNullOrWhiteSpace(team))
            {
                team = change.ChangedTeamSide switch
                {
                    TeamSide.Home => game.HomeTeam.TeamCode,
                    TeamSide.Away => game.AwayTeam.TeamCode,
                    _ => null,
                };
            }
            var key = Key(change.InPlayerPcode, change.InPlayerName, team);
            var pitcher = GetPitcher(pitchers, game.GameId, key, change.InPlayerName);
            pitcher.IsReliever = true;

            var changeIndex = indexById.GetValueOrDefault(change.RelayGroupId, groups.Count - 1);
            double? wpa = null;
            for (var index = Math.Min(changeIndex, groups.Count - 1); index >= 0; index--)
            {
                var candidate = groups[index].WpaByPlate;
                if (candidate.HasValue && Math.Abs(candidate.Value) > 0.0001)
                {
                    wpa = candidate.Value;
                    break;
                }
            }
            if (wpa.HasValue)
            {
                pitcher.EntryAbsoluteWpaSum += Math.Abs(wpa.Value);
                pitcher.EntryWpaCount++;
            }
            MergeObservation(observations, key, change.InPlayerName, team, null, "투수", null, false, true, null, null);
        }
    }

    private static BatterGameAggregate GetBatter(
        IDictionary<WarehousePlayerKey, BatterGameAggregate> values,
        string gameId,
        WarehousePlayerKey key,
        string? name)
    {
        if (!values.TryGetValue(key, out var value))
        {
            value = new BatterGameAggregate
            {
                GameId = gameId,
                Pcode = key.Pcode,
                TeamCode = key.TeamCode,
                Name = CleanName(name, key.Pcode),
            };
            values[key] = value;
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            value.Name = name.Trim();
        }
        return value;
    }

    private static PitcherGameAggregate GetPitcher(
        IDictionary<WarehousePlayerKey, PitcherGameAggregate> values,
        string gameId,
        WarehousePlayerKey key,
        string? name)
    {
        if (!values.TryGetValue(key, out var value))
        {
            value = new PitcherGameAggregate
            {
                GameId = gameId,
                Pcode = key.Pcode,
                TeamCode = key.TeamCode,
                Name = CleanName(name, key.Pcode),
            };
            values[key] = value;
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            value.Name = name.Trim();
        }
        return value;
    }

    private static void AddDiscipline(BatterGameAggregate value, PitchEvent pitch)
    {
        value.Pitches++;
        if (pitch.IsSwing) value.Swings++;
        if (pitch.IsContact) value.Contacts++;
        if (pitch.IsWhiff) value.Whiffs++;
        if (pitch.IsCalledStrike) value.CalledStrikes++;
        if (pitch.IsWhiff || pitch.IsCalledStrike) value.Csw++;
        if (pitch.IsInNominalStrikeZone == true)
        {
            value.InZone++;
            if (pitch.IsSwing) value.ZoneSwings++;
            if (pitch.IsContact) value.ZoneContacts++;
        }
        else if (pitch.IsInNominalStrikeZone == false)
        {
            value.OutZone++;
            if (pitch.IsSwing) value.ChaseSwings++;
            if (pitch.IsContact) value.OutZoneContacts++;
        }
        if (pitch.DisplayPitchNumber == 1 || (pitch.BallsBefore == 0 && pitch.StrikesBefore == 0))
        {
            value.FirstPitches++;
            if (pitch.IsSwing) value.FirstPitchSwings++;
        }
    }

    private static void AddDiscipline(PitcherGameAggregate value, PitchEvent pitch)
    {
        value.Pitches++;
        if (pitch.IsSwing) value.Swings++;
        if (pitch.IsContact) value.Contacts++;
        if (pitch.IsWhiff) value.Whiffs++;
        if (pitch.IsCalledStrike) value.CalledStrikes++;
        if (pitch.IsWhiff || pitch.IsCalledStrike) value.Csw++;
        if (pitch.IsInNominalStrikeZone == true)
        {
            value.InZone++;
            if (pitch.IsSwing) value.ZoneSwings++;
            if (pitch.IsContact) value.ZoneContacts++;
        }
        else if (pitch.IsInNominalStrikeZone == false)
        {
            value.OutZone++;
            if (pitch.IsSwing) value.ChaseSwings++;
            if (pitch.IsContact) value.OutZoneContacts++;
        }
        if (pitch.DisplayPitchNumber == 1 || (pitch.BallsBefore == 0 && pitch.StrikesBefore == 0))
        {
            value.FirstPitches++;
            if (pitch.IsSwing) value.FirstPitchSwings++;
        }
    }

    private static void AddPosition(BatterGameAggregate aggregate, string? rawPosition, bool substitute, int plateAppearances)
    {
        var positions = ParsePositions(rawPosition).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var defense = positions.Where(position => position != "DH" && position != "PH").ToList();
        if (defense.Count > 0)
        {
            var innings = substitute ? 4.5 : 9.0;
            foreach (var position in defense)
            {
                var share = innings / defense.Count;
                switch (position)
                {
                    case "C": aggregate.CatcherInnings += share; break;
                    case "1B": aggregate.FirstBaseInnings += share; break;
                    case "2B": aggregate.SecondBaseInnings += share; break;
                    case "3B": aggregate.ThirdBaseInnings += share; break;
                    case "SS": aggregate.ShortstopInnings += share; break;
                    case "LF": aggregate.LeftFieldInnings += share; break;
                    case "CF": aggregate.CenterFieldInnings += share; break;
                    case "RF": aggregate.RightFieldInnings += share; break;
                }
            }
        }
        if (positions.Contains("DH", StringComparer.OrdinalIgnoreCase) || positions.Contains("PH", StringComparer.OrdinalIgnoreCase))
            aggregate.DesignatedHitterPlateAppearances += Math.Max(0, plateAppearances);
    }

    private static IEnumerable<string> ParsePositions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        var value = raw.ToUpperInvariant()
            .Replace("포수", "C", StringComparison.Ordinal)
            .Replace("유격수", "SS", StringComparison.Ordinal)
            .Replace("2루수", "2B", StringComparison.Ordinal)
            .Replace("3루수", "3B", StringComparison.Ordinal)
            .Replace("중견수", "CF", StringComparison.Ordinal)
            .Replace("좌익수", "LF", StringComparison.Ordinal)
            .Replace("우익수", "RF", StringComparison.Ordinal)
            .Replace("1루수", "1B", StringComparison.Ordinal)
            .Replace("지명타자", "DH", StringComparison.Ordinal)
            .Replace("대타", "PH", StringComparison.Ordinal)
            .Replace("대주자", "PR", StringComparison.Ordinal)
            .Replace("투수", "P", StringComparison.Ordinal);
        foreach (var token in Regex.Split(value, @"[^A-Z0-9]+"))
        {
            if (token is "C" or "1B" or "2B" or "3B" or "SS" or "LF" or "CF" or "RF" or "DH" or "PH")
                yield return token;
        }
    }

    private static void MergeObservation(
        IDictionary<WarehousePlayerKey, WarehousePlayerObservation> observations,
        WarehousePlayerKey key,
        string? name,
        string? team,
        string? birthDate,
        string? position,
        string? hitType,
        bool isBatter,
        bool isPitcher,
        int? batOrder,
        int? lineupSequence)
    {
        if (!observations.TryGetValue(key, out var observation))
        {
            observation = new WarehousePlayerObservation
            {
                Pcode = key.Pcode,
                TeamCode = key.TeamCode,
                Name = CleanName(name, key.Pcode),
            };
            observations[key] = observation;
        }
        if (!string.IsNullOrWhiteSpace(name)) observation.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(team)) observation.TeamCode = team.Trim();
        if (!string.IsNullOrWhiteSpace(birthDate)) observation.BirthDate ??= birthDate;
        if (!string.IsNullOrWhiteSpace(position)) observation.Position ??= position.Trim();
        if (!string.IsNullOrWhiteSpace(hitType)) observation.HitType ??= hitType.Trim();
        observation.IsBatter |= isBatter;
        observation.IsPitcher |= isPitcher;
        observation.BatOrder ??= batOrder;
        observation.LineupSequence ??= lineupSequence;
    }

    private static WarehousePlayerKey Key(string? pcode, string? name, string? team)
    {
        var cleanTeam = team?.Trim() ?? string.Empty;
        var cleanPcode = pcode?.Trim();
        if (string.IsNullOrWhiteSpace(cleanPcode))
        {
            var cleanName = name?.Trim();
            cleanPcode = string.IsNullOrWhiteSpace(cleanName)
                ? $"UNKNOWN:{cleanTeam}"
                : $"NAME:{cleanTeam}:{cleanName}";
        }
        return new WarehousePlayerKey(cleanPcode, cleanTeam);
    }

    private static string CleanName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    private static string? TeamForSide(NormalizedGame game, TeamSide side) => side switch
    {
        TeamSide.Home => game.HomeTeam.TeamCode,
        TeamSide.Away => game.AwayTeam.TeamCode,
        _ => null,
    };

    private static string? OppositeTeamForSide(NormalizedGame game, TeamSide battingSide) => battingSide switch
    {
        TeamSide.Home => game.AwayTeam.TeamCode,
        TeamSide.Away => game.HomeTeam.TeamCode,
        _ => null,
    };

    internal static int ParseInningsOuts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var text = value.Trim();
        if (text.Contains(' '))
        {
            var pieces = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length >= 2 && int.TryParse(pieces[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
            {
                var fraction = pieces[1].Split('/');
                if (fraction.Length == 2 && int.TryParse(fraction[0], out var numerator) && int.TryParse(fraction[1], out var denominator) && denominator == 3)
                    return whole * 3 + Math.Clamp(numerator, 0, 2);
            }
        }
        var parts = text.Split('.');
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var innings)) return 0;
        var outs = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var remainder)
            ? Math.Clamp(remainder, 0, 2)
            : 0;
        return innings * 3 + outs;
    }

    internal static string FormatInnings(int outs)
    {
        var safe = Math.Max(0, outs);
        return $"{safe / 3}.{safe % 3}";
    }

    internal static string? NormalizeBirthDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length >= 8 && DateTime.TryParseExact(digits[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value.Trim();
    }

    internal static bool IsInfieldFly(PlateAppearance pa)
    {
        var text = pa.ResultText ?? string.Empty;
        return text.Contains("포수 파울플라이", StringComparison.Ordinal)
            || text.Contains("1루수 파울플라이", StringComparison.Ordinal)
            || text.Contains("2루수 뜬공", StringComparison.Ordinal)
            || text.Contains("3루수 뜬공", StringComparison.Ordinal)
            || text.Contains("유격수 뜬공", StringComparison.Ordinal)
            || text.Contains("투수 뜬공", StringComparison.Ordinal);
    }

    private static bool IsFlyBall(PlateAppearance pa) =>
        pa.Outcome.BattedBallType is BattedBallType.FlyBall or BattedBallType.PopUp
        || pa.Outcome.ResultType == BattingResultType.HomeRun;
}
