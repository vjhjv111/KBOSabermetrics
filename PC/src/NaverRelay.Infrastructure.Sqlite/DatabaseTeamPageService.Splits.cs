using NaverRelay.Application.Queries;
using NaverRelay.Application.Teams;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseTeamPageService
{
    private sealed class TeamSplitAccumulator
    {
        public int PA { get; private set; }
        public int AB { get; private set; }
        public int Hits { get; private set; }
        public int Singles { get; private set; }
        public int Doubles { get; private set; }
        public int Triples { get; private set; }
        public int HomeRuns { get; private set; }
        public int Walks { get; private set; }
        public int IntentionalWalks { get; private set; }
        public int HitByPitch { get; private set; }
        public int Strikeouts { get; private set; }
        public int SacrificeFlies { get; private set; }
        public int TotalBases { get; private set; }

        public void Add(
            int resultType,
            bool countsAsAtBat,
            bool isHit,
            int totalBases,
            bool isWalk,
            bool isIntentionalWalk,
            bool isStrikeout)
        {
            PA++;
            if (countsAsAtBat) AB++;
            if (isHit)
            {
                Hits++;
                if (totalBases == 1) Singles++;
                else if (totalBases == 2) Doubles++;
                else if (totalBases == 3) Triples++;
                else if (totalBases == 4) HomeRuns++;
            }
            TotalBases += Math.Max(0, totalBases);
            if (isWalk || isIntentionalWalk) Walks++;
            if (isIntentionalWalk) IntentionalWalks++;
            if (resultType == (int)BattingResultType.HitByPitch) HitByPitch++;
            if (resultType == (int)BattingResultType.SacrificeFly) SacrificeFlies++;
            if (isStrikeout) Strikeouts++;
        }
    }

    private sealed class TeamOpponentAccumulator
    {
        public int Year { get; init; }
        public string OpponentCode { get; init; } = string.Empty;
        public string OpponentName { get; set; } = string.Empty;
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Ties { get; set; }
        public int RunsFor { get; set; }
        public int RunsAgainst { get; set; }
        public TeamSplitAccumulator Batting { get; } = new();
        public int InningsOuts { get; set; }
        public int EarnedRuns { get; set; }
        public int PitchingHomeRuns { get; set; }
        public int PitchingWalks { get; set; }
        public int PitchingHitBatters { get; set; }
        public int PitchingStrikeouts { get; set; }
    }

    private sealed class PitchTypePitchingAccumulator
    {
        public int Year { get; init; }
        public string PitchType { get; init; } = string.Empty;
        public int Pitches { get; set; }
        public int SpeedCount { get; set; }
        public double SpeedSum { get; set; }
        public int Strikes { get; set; }
        public int Swings { get; set; }
        public int Whiffs { get; set; }
        public int Contacts { get; set; }
        public int Csw { get; set; }
        public TeamSplitAccumulator Outcomes { get; } = new();
    }

    private async Task<IReadOnlyList<TeamOpponentRecordRow>> ReadOpponentRecordsAsync(
        string teamCode,
        IReadOnlyList<TeamGameRecord> games,
        LeagueReference league,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<(int Year, string Opponent), TeamOpponentAccumulator>();
        foreach (var game in games.Where(row => row.Year.HasValue && !string.IsNullOrWhiteSpace(row.OpponentCode)))
        {
            var key = (game.Year!.Value, game.OpponentCode);
            if (!map.TryGetValue(key, out var row))
            {
                row = new TeamOpponentAccumulator
                {
                    Year = key.Item1,
                    OpponentCode = key.Item2,
                    OpponentName = game.OpponentName,
                };
                map[key] = row;
            }
            row.Games++;
            if (!string.IsNullOrWhiteSpace(game.OpponentName)) row.OpponentName = game.OpponentName;
            if (game.RunsFor.HasValue) row.RunsFor += game.RunsFor.Value;
            if (game.RunsAgainst.HasValue) row.RunsAgainst += game.RunsAgainst.Value;
            if (!game.RunsFor.HasValue || !game.RunsAgainst.HasValue) continue;
            if (game.RunsFor.Value > game.RunsAgainst.Value) row.Wins++;
            else if (game.RunsFor.Value < game.RunsAgainst.Value) row.Losses++;
            else row.Ties++;
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var battingCommand = connection.CreateCommand())
        {
            battingCommand.CommandText = """
                SELECT g.SeasonYear,
                       CASE WHEN g.HomeTeamCode=$team THEN g.AwayTeamCode ELSE g.HomeTeamCode END AS OpponentCode,
                       CASE WHEN g.HomeTeamCode=$team THEN g.AwayTeamName ELSE g.HomeTeamName END,
                       pa.ResultType, pa.CountsAsAtBat, pa.IsHit, pa.TotalBases,
                       pa.IsWalk, pa.IsIntentionalWalk, pa.IsStrikeout
                FROM PlateAppearances pa INNER JOIN Games g ON g.GameId=pa.GameId
                WHERE pa.IsOfficial=1
                  AND pa.BattingTeamCode=$team
                  AND g.SeasonYear IS NOT NULL
                  AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
                ORDER BY g.SeasonYear, pa.GameId, pa.SequenceNumber;
                """;
            battingCommand.Parameters.AddWithValue("$team", teamCode);
            await using var reader = await battingCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var opponent = NullableText(reader, 1) ?? "-";
                var key = (reader.GetInt32(0), opponent);
                if (!map.TryGetValue(key, out var row))
                {
                    row = new TeamOpponentAccumulator
                    {
                        Year = key.Item1,
                        OpponentCode = opponent,
                        OpponentName = NullableText(reader, 2) ?? opponent,
                    };
                    map[key] = row;
                }
                if (!reader.IsDBNull(2)) row.OpponentName = reader.GetString(2);
                row.Batting.Add(
                    ReadInt(reader, 3), ReadInt(reader, 4) != 0, ReadInt(reader, 5) != 0,
                    ReadInt(reader, 6), ReadInt(reader, 7) != 0, ReadInt(reader, 8) != 0,
                    ReadInt(reader, 9) != 0);
            }
        }

        await using (var pitchingCommand = connection.CreateCommand())
        {
            pitchingCommand.CommandText = """
                SELECT g.SeasonYear,
                       CASE WHEN g.HomeTeamCode=$team THEN g.AwayTeamCode ELSE g.HomeTeamCode END AS OpponentCode,
                       MAX(CASE WHEN g.HomeTeamCode=$team THEN g.AwayTeamName ELSE g.HomeTeamName END),
                       SUM(p.InningsOuts), SUM(p.EarnedRuns), SUM(p.HomeRunsAllowed),
                       SUM(p.FinalBB), SUM(p.FinalHBP), SUM(p.FinalSO)
                FROM PitcherGameStats p INNER JOIN Games g ON g.GameId=p.GameId
                WHERE p.TeamCode=$team
                  AND g.SeasonYear IS NOT NULL
                  AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
                GROUP BY g.SeasonYear, OpponentCode
                ORDER BY g.SeasonYear;
                """;
            pitchingCommand.Parameters.AddWithValue("$team", teamCode);
            await using var reader = await pitchingCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var opponent = NullableText(reader, 1) ?? "-";
                var key = (reader.GetInt32(0), opponent);
                if (!map.TryGetValue(key, out var row))
                {
                    row = new TeamOpponentAccumulator
                    {
                        Year = key.Item1,
                        OpponentCode = opponent,
                        OpponentName = NullableText(reader, 2) ?? opponent,
                    };
                    map[key] = row;
                }
                if (!reader.IsDBNull(2)) row.OpponentName = reader.GetString(2);
                row.InningsOuts += ReadInt(reader, 3);
                row.EarnedRuns += ReadInt(reader, 4);
                row.PitchingHomeRuns += ReadInt(reader, 5);
                row.PitchingWalks += ReadInt(reader, 6);
                row.PitchingHitBatters += ReadInt(reader, 7);
                row.PitchingStrikeouts += ReadInt(reader, 8);
            }
        }

        return map.Values.Select(row =>
        {
            var batting = row.Batting;
            var avg = Divide(batting.Hits, batting.AB);
            var obp = Divide(batting.Hits + batting.Walks + batting.HitByPitch,
                batting.AB + batting.Walks + batting.HitByPitch + batting.SacrificeFlies);
            var slg = Divide(batting.TotalBases, batting.AB);
            var innings = row.InningsOuts / 3.0;
            double? fip = innings > 0
                ? (13.0 * row.PitchingHomeRuns +
                   3.0 * (row.PitchingWalks + row.PitchingHitBatters) -
                   2.0 * row.PitchingStrikeouts) / innings + league.FipConstant
                : null;
            return new TeamOpponentRecordRow
            {
                Year = row.Year,
                OpponentCode = row.OpponentCode,
                OpponentName = string.IsNullOrWhiteSpace(row.OpponentName) ? row.OpponentCode : row.OpponentName,
                Games = row.Games, Wins = row.Wins, Losses = row.Losses, Ties = row.Ties,
                WinningPercentage = row.Wins + row.Losses > 0
                    ? (double)row.Wins / (row.Wins + row.Losses) : null,
                RunsFor = row.RunsFor, RunsAgainst = row.RunsAgainst,
                RunDifferential = row.RunsFor - row.RunsAgainst,
                PA = batting.PA, AB = batting.AB, Hits = batting.Hits,
                HomeRuns = batting.HomeRuns, Walks = batting.Walks,
                Strikeouts = batting.Strikeouts, AVG = avg, OBP = obp, SLG = slg,
                OPS = obp.HasValue && slg.HasValue ? obp.Value + slg.Value : null,
                InningsPitched = innings, EarnedRuns = row.EarnedRuns,
                ERA = RatePerNine(row.EarnedRuns, innings), Fip = fip,
            };
        })
        .OrderByDescending(row => row.Year)
        .ThenByDescending(row => row.Games)
        .ThenBy(row => row.OpponentCode, StringComparer.Ordinal)
        .ToList();
    }

    private async Task<IReadOnlyList<TeamSituationSplitRow>> ReadSituationSplitsAsync(
        string teamCode,
        LeagueReference league,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<(int Year, string Perspective, string Category, string Bucket), TeamSplitAccumulator>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.SeasonYear, g.HomeTeamCode, g.AwayTeamCode,
                   pa.BattingTeamCode, pa.FieldingTeamCode, pa.Inning, pa.BeforeOuts,
                   pa.BeforeFirstRunnerPcode, pa.BeforeSecondRunnerPcode, pa.BeforeThirdRunnerPcode,
                   pa.BeforeHomeScore, pa.BeforeAwayScore,
                   pa.ResultType, pa.CountsAsAtBat, pa.IsHit, pa.TotalBases,
                   pa.IsWalk, pa.IsIntentionalWalk, pa.IsStrikeout
            FROM PlateAppearances pa INNER JOIN Games g ON g.GameId=pa.GameId
            WHERE pa.IsOfficial=1
              AND g.SeasonYear IS NOT NULL
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
              AND (pa.BattingTeamCode=$team OR pa.FieldingTeamCode=$team);
            """;
        command.Parameters.AddWithValue("$team", teamCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var year = reader.GetInt32(0);
            var homeTeam = NullableText(reader, 1) ?? string.Empty;
            var battingTeam = NullableText(reader, 3) ?? string.Empty;
            var asBatting = string.Equals(battingTeam, teamCode, StringComparison.OrdinalIgnoreCase);
            var perspective = asBatting ? "타격" : "투구";
            var inning = ReadInt(reader, 5);
            var outs = ReadInt(reader, 6);
            var onFirst = !reader.IsDBNull(7);
            var onSecond = !reader.IsDBNull(8);
            var onThird = !reader.IsDBNull(9);
            var homeScore = ReadInt(reader, 10);
            var awayScore = ReadInt(reader, 11);
            var isHome = string.Equals(homeTeam, teamCode, StringComparison.OrdinalIgnoreCase);
            var teamScore = isHome ? homeScore : awayScore;
            var opponentScore = isHome ? awayScore : homeScore;
            var scoreDifference = teamScore - opponentScore;

            void Add(string category, string bucket)
            {
                var key = (year, perspective, category, bucket);
                if (!map.TryGetValue(key, out var accumulator))
                {
                    accumulator = new TeamSplitAccumulator();
                    map[key] = accumulator;
                }
                accumulator.Add(
                    ReadInt(reader, 12), ReadInt(reader, 13) != 0, ReadInt(reader, 14) != 0,
                    ReadInt(reader, 15), ReadInt(reader, 16) != 0, ReadInt(reader, 17) != 0,
                    ReadInt(reader, 18) != 0);
            }

            var runnerBucket = onFirst && onSecond && onThird ? "만루"
                : onFirst && onSecond ? "1·2루"
                : onFirst && onThird ? "1·3루"
                : onSecond && onThird ? "2·3루"
                : onFirst ? "1루"
                : onSecond ? "2루"
                : onThird ? "3루"
                : "주자 없음";
            Add("주자", runnerBucket);
            Add("아웃", $"{outs}아웃");
            Add("이닝", inning <= 3 ? "1~3회" : inning <= 6 ? "4~6회" : inning <= 9 ? "7~9회" : "연장");
            Add("점수차", scoreDifference == 0 ? "동점"
                : scoreDifference > 0 ? scoreDifference <= 2 ? "1~2점 리드" : "3점+ 리드"
                : scoreDifference >= -2 ? "1~2점 열세" : "3점+ 열세");
            Add("득점권", onSecond || onThird ? "득점권" : "비득점권");
            Add("클러치", inning >= 7 && Math.Abs(scoreDifference) <= 3 ? "클러치" : "일반");
            Add("장소", isHome ? "홈" : "원정");
            Add("선두타자", outs == 0 && !onFirst && !onSecond && !onThird ? "선두타자" : "비선두");
            Add("2아웃", outs == 2 ? "2아웃" : "0~1아웃");
        }

        return map.Select(pair => ToSituationRow(pair.Key, pair.Value, league))
            .OrderByDescending(row => row.Year)
            .ThenBy(row => row.Perspective, StringComparer.CurrentCulture)
            .ThenBy(row => row.Category, StringComparer.CurrentCulture)
            .ThenByDescending(row => row.PA)
            .ToList();
    }

    private static TeamSituationSplitRow ToSituationRow(
        (int Year, string Perspective, string Category, string Bucket) key,
        TeamSplitAccumulator value,
        LeagueReference league)
    {
        var avg = Divide(value.Hits, value.AB);
        var obp = Divide(value.Hits + value.Walks + value.HitByPitch,
            value.AB + value.Walks + value.HitByPitch + value.SacrificeFlies);
        var slg = Divide(value.TotalBases, value.AB);
        return new TeamSituationSplitRow
        {
            Year = key.Year, Perspective = key.Perspective, Category = key.Category, Bucket = key.Bucket,
            PA = value.PA, AB = value.AB, Hits = value.Hits, Doubles = value.Doubles,
            Triples = value.Triples, HomeRuns = value.HomeRuns, Walks = value.Walks,
            HitByPitch = value.HitByPitch, Strikeouts = value.Strikeouts,
            AVG = avg, OBP = obp, SLG = slg,
            OPS = obp.HasValue && slg.HasValue ? obp.Value + slg.Value : null,
            Woba = CalculateWoba(value, league.GetWobaConstants(key.Year)),
        };
    }

    private async Task<IReadOnlyList<TeamPitchTypeBattingRow>> ReadBattingPitchTypeSplitsAsync(
        string teamCode,
        LeagueReference league,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<(int Year, string PitchType), TeamSplitAccumulator>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.SeasonYear, COALESCE(NULLIF(TRIM(p.PitchType),''),'미상'),
                   pa.ResultType, pa.CountsAsAtBat, pa.IsHit, pa.TotalBases,
                   pa.IsWalk, pa.IsIntentionalWalk, pa.IsStrikeout
            FROM PlateAppearances pa INNER JOIN Games g ON g.GameId=pa.GameId
            LEFT JOIN Pitches p ON p.PitchEventId=(
                SELECT p2.PitchEventId FROM Pitches p2
                WHERE p2.PlateAppearanceId=pa.PlateAppearanceId
                ORDER BY p2.ActualPitchIndex DESC LIMIT 1)
            WHERE pa.IsOfficial=1
              AND pa.BattingTeamCode=$team
              AND g.SeasonYear IS NOT NULL
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r';
            """;
        command.Parameters.AddWithValue("$team", teamCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = (reader.GetInt32(0), reader.GetString(1));
            if (!map.TryGetValue(key, out var row))
            {
                row = new TeamSplitAccumulator();
                map[key] = row;
            }
            row.Add(
                ReadInt(reader, 2), ReadInt(reader, 3) != 0, ReadInt(reader, 4) != 0,
                ReadInt(reader, 5), ReadInt(reader, 6) != 0, ReadInt(reader, 7) != 0,
                ReadInt(reader, 8) != 0);
        }

        return map.Select(pair =>
        {
            var value = pair.Value;
            var avg = Divide(value.Hits, value.AB);
            var obp = Divide(value.Hits + value.Walks + value.HitByPitch,
                value.AB + value.Walks + value.HitByPitch + value.SacrificeFlies);
            var slg = Divide(value.TotalBases, value.AB);
            return new TeamPitchTypeBattingRow
            {
                Year = pair.Key.Year, PitchType = pair.Key.PitchType,
                PA = value.PA, AB = value.AB, Hits = value.Hits, Singles = value.Singles,
                Doubles = value.Doubles, Triples = value.Triples, HomeRuns = value.HomeRuns,
                Walks = value.Walks, HitByPitch = value.HitByPitch, Strikeouts = value.Strikeouts,
                AVG = avg, OBP = obp, SLG = slg,
                OPS = obp.HasValue && slg.HasValue ? obp.Value + slg.Value : null,
                Woba = CalculateWoba(value, league.GetWobaConstants(pair.Key.Year)),
            };
        })
        .OrderByDescending(row => row.Year)
        .ThenByDescending(row => row.PA)
        .ThenBy(row => row.PitchType, StringComparer.CurrentCulture)
        .ToList();
    }

    private async Task<IReadOnlyList<TeamPitchTypePitchingRow>> ReadPitchingPitchTypeSplitsAsync(
        string teamCode,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<(int Year, string PitchType), PitchTypePitchingAccumulator>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var pitchCommand = connection.CreateCommand())
        {
            pitchCommand.CommandText = """
                SELECT g.SeasonYear, COALESCE(NULLIF(TRIM(p.PitchType),''),'미상'),
                       COUNT(*), COUNT(p.SpeedKmh), SUM(COALESCE(p.SpeedKmh,0)),
                       SUM(CASE WHEN p.PitchResult IN (2,3,4,5,6) THEN 1 ELSE 0 END),
                       SUM(p.IsSwing), SUM(p.IsWhiff), SUM(p.IsContact),
                       SUM(CASE WHEN p.IsCalledStrike=1 OR p.IsWhiff=1 THEN 1 ELSE 0 END)
                FROM Pitches p
                INNER JOIN PlateAppearances pa ON pa.PlateAppearanceId=p.PlateAppearanceId
                INNER JOIN Games g ON g.GameId=p.GameId
                WHERE pa.FieldingTeamCode=$team
                  AND g.SeasonYear IS NOT NULL
                  AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
                GROUP BY g.SeasonYear, COALESCE(NULLIF(TRIM(p.PitchType),''),'미상');
                """;
            pitchCommand.Parameters.AddWithValue("$team", teamCode);
            await using var reader = await pitchCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = (reader.GetInt32(0), reader.GetString(1));
                map[key] = new PitchTypePitchingAccumulator
                {
                    Year = key.Item1, PitchType = key.Item2,
                    Pitches = ReadInt(reader, 2), SpeedCount = ReadInt(reader, 3),
                    SpeedSum = NullableReal(reader, 4) ?? 0.0,
                    Strikes = ReadInt(reader, 5), Swings = ReadInt(reader, 6),
                    Whiffs = ReadInt(reader, 7), Contacts = ReadInt(reader, 8), Csw = ReadInt(reader, 9),
                };
            }
        }

        await using (var outcomeCommand = connection.CreateCommand())
        {
            outcomeCommand.CommandText = """
                SELECT g.SeasonYear, COALESCE(NULLIF(TRIM(p.PitchType),''),'미상'),
                       pa.ResultType, pa.CountsAsAtBat, pa.IsHit, pa.TotalBases,
                       pa.IsWalk, pa.IsIntentionalWalk, pa.IsStrikeout
                FROM PlateAppearances pa INNER JOIN Games g ON g.GameId=pa.GameId
                LEFT JOIN Pitches p ON p.PitchEventId=(
                    SELECT p2.PitchEventId FROM Pitches p2
                    WHERE p2.PlateAppearanceId=pa.PlateAppearanceId
                    ORDER BY p2.ActualPitchIndex DESC LIMIT 1)
                WHERE pa.IsOfficial=1
                  AND pa.FieldingTeamCode=$team
                  AND g.SeasonYear IS NOT NULL
                  AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r';
                """;
            outcomeCommand.Parameters.AddWithValue("$team", teamCode);
            await using var reader = await outcomeCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = (reader.GetInt32(0), reader.GetString(1));
                if (!map.TryGetValue(key, out var row))
                {
                    row = new PitchTypePitchingAccumulator { Year = key.Item1, PitchType = key.Item2 };
                    map[key] = row;
                }
                row.Outcomes.Add(
                    ReadInt(reader, 2), ReadInt(reader, 3) != 0, ReadInt(reader, 4) != 0,
                    ReadInt(reader, 5), ReadInt(reader, 6) != 0, ReadInt(reader, 7) != 0,
                    ReadInt(reader, 8) != 0);
            }
        }

        var totals = map.Values.GroupBy(row => row.Year)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Pitches));
        return map.Values.Select(row =>
        {
            var avg = Divide(row.Outcomes.Hits, row.Outcomes.AB);
            var obp = Divide(row.Outcomes.Hits + row.Outcomes.Walks + row.Outcomes.HitByPitch,
                row.Outcomes.AB + row.Outcomes.Walks + row.Outcomes.HitByPitch + row.Outcomes.SacrificeFlies);
            var slg = Divide(row.Outcomes.TotalBases, row.Outcomes.AB);
            return new TeamPitchTypePitchingRow
            {
                Year = row.Year, PitchType = row.PitchType, Pitches = row.Pitches,
                UsageRate = totals.TryGetValue(row.Year, out var total) ? Divide(row.Pitches, total) : null,
                AverageSpeed = Divide(row.SpeedSum, row.SpeedCount),
                StrikeRate = Divide(row.Strikes, row.Pitches), SwingRate = Divide(row.Swings, row.Pitches),
                WhiffRate = Divide(row.Whiffs, row.Swings), ContactRate = Divide(row.Contacts, row.Swings),
                CswRate = Divide(row.Csw, row.Pitches),
                PlateAppearances = row.Outcomes.PA, AtBats = row.Outcomes.AB,
                HitsAllowed = row.Outcomes.Hits, HomeRunsAllowed = row.Outcomes.HomeRuns,
                OpponentAVG = avg,
                OpponentOPS = obp.HasValue && slg.HasValue ? obp.Value + slg.Value : null,
            };
        })
        .OrderByDescending(row => row.Year)
        .ThenByDescending(row => row.Pitches)
        .ThenBy(row => row.PitchType, StringComparer.CurrentCulture)
        .ToList();
    }

    private static double? CalculateWoba(TeamSplitAccumulator value, WobaConstants constants)
    {
        return constants.Calculate(
            value.AB, value.Walks, value.IntentionalWalks, value.HitByPitch,
            value.SacrificeFlies, value.Singles, value.Doubles, value.Triples, value.HomeRuns);
    }
}
