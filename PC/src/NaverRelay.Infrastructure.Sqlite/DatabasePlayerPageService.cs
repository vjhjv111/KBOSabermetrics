using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Players;
using NaverRelay.Parsing;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

/// <summary>
/// 선수 검색과 개인 페이지를 관계형 SQLite만으로 구성합니다.
/// 원본 JSON이나 DB 내부 JSON blob은 읽지 않습니다.
/// </summary>
public sealed partial class DatabasePlayerPageService : IPlayerPageService
{
    private readonly DatabaseCacheService _database;
    private readonly DatabaseAnalyticsService _analytics;

    public DatabasePlayerPageService(DatabaseCacheService database)
    {
        _database = database;
        _analytics = new DatabaseAnalyticsService(database);
    }

    public IReadOnlyList<PlayerSearchItem> SearchPlayers(string? query, int maxResults = 100) =>
        SearchPlayersAsync(query, maxResults).GetAwaiter().GetResult();

    public PlayerPageData? GetPlayerPage(string pcode) =>
        GetPlayerPageAsync(pcode).GetAwaiter().GetResult();

    public Task<IReadOnlyList<PlayerSearchItem>> SearchPlayersAsync(
        string? query,
        int maxResults = 100,
        CancellationToken cancellationToken = default) =>
        _database.SearchPlayersAsync(query, maxResults, cancellationToken);

    public async Task<PlayerPageData?> GetPlayerPageAsync(
        string pcode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pcode)) return null;
        var profileItem = await _database.GetPlayerAsync(pcode, cancellationToken).ConfigureAwait(false);
        if (profileItem is null) return null;
        var league = await _database.GetLeagueReferenceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var seasonKeys = await ReadSeasonKeysAsync(pcode, cancellationToken).ConfigureAwait(false);
        var snapshots = new Dictionary<int, AnalyticsSnapshot>();
        foreach (var year in seasonKeys.Select(key => key.Year).Distinct().OrderBy(year => year))
        {
            snapshots[year] = await _analytics.GetSnapshotAsync(
                new GameQuery { SeasonYear = year, Competition = "정규시즌" },
                league,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var battingRaw = await ReadBattingSeasonRawAsync(pcode, cancellationToken).ConfigureAwait(false);
        var pitchingRaw = await ReadPitchingSeasonRawAsync(pcode, cancellationToken).ConfigureAwait(false);
        var batting = BuildBattingSeasons(profileItem, seasonKeys, snapshots, battingRaw);
        var pitching = BuildPitchingSeasons(profileItem, seasonKeys, snapshots, pitchingRaw);
        var values = BuildValueSeasons(batting, pitching);
        var gameLogs = await ReadGameLogsAsync(pcode, cancellationToken).ConfigureAwait(false);
        var plateAppearances = await ReadPlateAppearanceLogsAsync(pcode, cancellationToken).ConfigureAwait(false);
        var pitches = await ReadPitchLogsAsync(pcode, cancellationToken).ConfigureAwait(false);
        var opponentSplits = await ReadOpponentSplitsAsync(pcode, cancellationToken).ConfigureAwait(false);
        var situationSplits = await ReadSituationSplitsAsync(pcode, cancellationToken).ConfigureAwait(false);
        var battingByPitchType = await ReadBattingPitchTypeSplitsAsync(pcode, cancellationToken).ConfigureAwait(false);
        var pitchingByPitchType = await ReadPitchingPitchTypeSplitsAsync(pcode, cancellationToken).ConfigureAwait(false);
        var rollingGames = await ReadRollingBattingGamesAsync(pcode, cancellationToken).ConfigureAwait(false);
        var rolling = new Dictionary<int, IReadOnlyList<RollingMetricPoint>>
        {
            [7] = BuildRollingWrcPlus(rollingGames, 7, league),
            [15] = BuildRollingWrcPlus(rollingGames, 15, league),
            [30] = BuildRollingWrcPlus(rollingGames, 30, league),
        };
        var teamHistory = await ReadTeamHistoryAsync(pcode, cancellationToken).ConfigureAwait(false);
        var careerWar = values.Count == 0 ? (double?)null : values.Sum(row => row.TotalWar ?? 0.0);
        var profile = new PlayerProfile
        {
            Pcode = profileItem.Pcode,
            Name = profileItem.Name,
            BirthDate = profileItem.BirthDate,
            LatestTeam = teamHistory.LastOrDefault() ?? "-",
            TeamHistory = teamHistory.Count == 0 ? "-" : string.Join(" → ", teamHistory),
            PrimaryPosition = profileItem.PrimaryPosition,
            Role = profileItem.Role,
            BatsThrows = profileItem.BatsThrows,
            FirstSeason = ParseActiveYear(profileItem.ActiveYears, true),
            LastSeason = ParseActiveYear(profileItem.ActiveYears, false),
            CareerGames = gameLogs.Count,
            CareerPlateAppearances = batting.Sum(row => row.PA),
            CareerInnings = pitching.Sum(row => row.InningsPitched),
            CareerWar = careerWar,
        };

        return new PlayerPageData
        {
            Profile = profile,
            BattingSeasons = batting,
            PitchingSeasons = pitching,
            ValueSeasons = values,
            GameLogs = gameLogs,
            PlateAppearances = plateAppearances,
            Pitches = pitches,
            OpponentSplits = opponentSplits,
            SituationSplits = situationSplits,
            BattingByPitchType = battingByPitchType,
            PitchingByPitchType = pitchingByPitchType,
            HasBatting = batting.Count > 0 || plateAppearances.Any(row => row.Perspective == "타자"),
            HasPitching = pitching.Count > 0 || plateAppearances.Any(row => row.Perspective == "투수"),
            RollingWrcPlus = rolling,
            FormulaDocumentation = BuildFormulaDocumentation(profile, batting, pitching, league),
        };
    }

    private static IReadOnlyList<PlayerBattingSeasonRow> BuildBattingSeasons(
        PlayerSearchItem profile,
        IReadOnlyList<SeasonKey> keys,
        IReadOnlyDictionary<int, AnalyticsSnapshot> snapshots,
        IReadOnlyDictionary<(int Year, string Team), BattingSeasonRaw> rawRows)
    {
        var result = new List<PlayerBattingSeasonRow>();
        foreach (var key in keys.Where(key => key.IsBatter).OrderBy(key => key.Year).ThenBy(key => key.Team, StringComparer.Ordinal))
        {
            if (!snapshots.TryGetValue(key.Year, out var snapshot)) continue;
            var classic = snapshot.BatterClassic.FirstOrDefault(row => row.Pcode == profile.Pcode && row.TeamCode == key.Team);
            var saber = snapshot.BatterSabermetrics.FirstOrDefault(row => row.Pcode == profile.Pcode && row.TeamCode == key.Team);
            var value = snapshot.BatterValues.FirstOrDefault(row => row.Pcode == profile.Pcode && row.TeamCode == key.Team);
            rawRows.TryGetValue((key.Year, key.Team), out var raw);
            if (classic is null && raw is null) continue;
            result.Add(new PlayerBattingSeasonRow
            {
                Year = key.Year, Team = key.Team, Age = CalculateAge(profile.BirthDate, key.Year),
                Position = value?.PrimaryPosition ?? profile.PrimaryPosition,
                Games = classic?.Games ?? raw?.Games ?? 0,
                PA = classic?.PA ?? raw?.PA ?? 0, AB = classic?.AB ?? raw?.AB ?? 0,
                Runs = raw?.Runs ?? 0, Hits = classic?.H ?? raw?.Hits ?? 0,
                Singles = classic?.Singles ?? raw?.Singles ?? 0,
                Doubles = classic?.Doubles ?? raw?.Doubles ?? 0,
                Triples = classic?.Triples ?? raw?.Triples ?? 0,
                HomeRuns = classic?.HomeRuns ?? raw?.HomeRuns ?? 0,
                TotalBases = classic?.TotalBases ?? raw?.TotalBases ?? 0,
                RBI = raw?.Rbi ?? 0, StolenBases = raw?.StolenBases ?? 0,
                CaughtStealing = raw?.CaughtStealing ?? 0,
                Walks = classic?.Walks ?? raw?.Walks ?? 0,
                IntentionalWalks = classic?.IntentionalWalks ?? raw?.IntentionalWalks ?? 0,
                HitByPitch = classic?.HitByPitch ?? raw?.HitByPitch ?? 0,
                Strikeouts = classic?.Strikeouts ?? raw?.Strikeouts ?? 0,
                DoublePlays = classic?.DoublePlays ?? raw?.DoublePlays ?? 0,
                SacrificeBunts = classic?.SacrificeBunts ?? raw?.SacrificeBunts ?? 0,
                SacrificeFlies = classic?.SacrificeFlies ?? raw?.SacrificeFlies ?? 0,
                AVG = classic?.AVG, OBP = classic?.OBP, SLG = classic?.SLG, OPS = classic?.OPS,
                ISO = saber?.Iso, BABIP = classic?.BABIP ?? saber?.Babip,
                WalkRate = saber?.WalkRate ?? classic?.WalkRate,
                StrikeoutRate = saber?.StrikeoutRate ?? classic?.StrikeoutRate,
                Woba = saber?.Woba, Wraa = saber?.Wraa, Wrc = saber?.Wrc,
                WrcPlus = saber?.WrcPlus, OpsPlus = saber?.OpsPlus,
                BattingRuns = value?.BattingRuns, RunningRuns = value?.RunningRuns,
                PositionRuns = value?.PositionRuns, ReplacementRuns = value?.ReplacementRuns,
                RunsAboveReplacement = value?.RunsAboveReplacement, War = value?.War,
            });
        }
        return result;
    }

    private static IReadOnlyList<PlayerPitchingSeasonRow> BuildPitchingSeasons(
        PlayerSearchItem profile,
        IReadOnlyList<SeasonKey> keys,
        IReadOnlyDictionary<int, AnalyticsSnapshot> snapshots,
        IReadOnlyDictionary<(int Year, string Team), PitchingSeasonRaw> rawRows)
    {
        var result = new List<PlayerPitchingSeasonRow>();
        foreach (var key in keys.Where(key => key.IsPitcher).OrderBy(key => key.Year).ThenBy(key => key.Team, StringComparer.Ordinal))
        {
            if (!rawRows.TryGetValue((key.Year, key.Team), out var raw)) continue;
            snapshots.TryGetValue(key.Year, out var snapshot);
            var saber = snapshot?.PitcherSabermetrics.FirstOrDefault(row => row.Pcode == profile.Pcode && row.TeamCode == key.Team);
            var value = snapshot?.PitcherValues.FirstOrDefault(row => row.Pcode == profile.Pcode && row.TeamCode == key.Team);
            var innings = raw.InningsOuts / 3.0;
            result.Add(new PlayerPitchingSeasonRow
            {
                Year = key.Year, Team = key.Team, Age = CalculateAge(profile.BirthDate, key.Year),
                Games = raw.Games, GamesStarted = raw.GamesStarted, ReliefGames = raw.ReliefGames,
                InningsPitched = innings, RunsAllowed = raw.RunsAllowed, EarnedRuns = raw.EarnedRuns,
                HitsAllowed = raw.HitsAllowed, HomeRunsAllowed = raw.HomeRuns,
                Walks = raw.Walks, HitBatters = raw.HitBatters,
                Strikeouts = raw.Strikeouts, InfieldFlies = raw.InfieldFlies,
                ERA = innings > 0 ? raw.EarnedRuns * 9.0 / innings : null,
                WHIP = innings > 0 ? (raw.HitsAllowed + raw.Walks) / innings : null,
                StrikeoutsPerNine = RatePerNine(raw.Strikeouts, innings),
                WalksPerNine = RatePerNine(raw.Walks, innings),
                HomeRunsPerNine = RatePerNine(raw.HomeRuns, innings),
                Fip = saber?.Fip, Xfip = saber?.Xfip,
                IfFip = value?.IfFip, FipR9 = value?.FipR9,
                ParkFactor = value?.ParkFactor, ParkAdjustedFipR9 = value?.ParkAdjustedFipR9,
                DynamicRunsPerWin = value?.DynamicRunsPerWin,
                GmLi = value?.GmLi, LeverageMultiplier = value?.LeverageMultiplier,
                StarterReplacementFipMinus = value?.StarterReplacementFipMinus,
                RelieverReplacementFipMinus = value?.RelieverReplacementFipMinus,
                WarPerInningCorrection = value?.WarPerInningCorrection,
                LeagueCorrection = value?.LeagueCorrection,
                War = value?.War,
                ParkAdjustedRa9 = value?.ParkAdjustedRa9,
                Ra9War = value?.Ra9War,
                BlendWar = value?.BlendWar,
            });
        }
        return result;
    }

    private static IReadOnlyList<PlayerValueSeasonRow> BuildValueSeasons(
        IReadOnlyList<PlayerBattingSeasonRow> batting,
        IReadOnlyList<PlayerPitchingSeasonRow> pitching)
    {
        return batting.Select(row => (row.Year, row.Team))
            .Concat(pitching.Select(row => (row.Year, row.Team)))
            .Distinct()
            .OrderBy(key => key.Year)
            .ThenBy(key => key.Team, StringComparer.Ordinal)
            .Select(key =>
            {
                var batter = batting.FirstOrDefault(row => row.Year == key.Year && row.Team == key.Team);
                var pitcher = pitching.FirstOrDefault(row => row.Year == key.Year && row.Team == key.Team);
                var pitcherRar = pitcher?.War is double pitcherWar && pitcher.DynamicRunsPerWin is double runsPerWin
                    ? pitcherWar * runsPerWin
                    : (double?)null;
                return new PlayerValueSeasonRow
                {
                    Year = key.Year, Team = key.Team,
                    BattingRuns = batter?.BattingRuns, RunningRuns = batter?.RunningRuns,
                    FieldingRuns = batter is null ? null : 0.0, PositionRuns = batter?.PositionRuns,
                    ReplacementRuns = batter?.ReplacementRuns, BatterRAR = batter?.RunsAboveReplacement,
                    BatterWar = batter?.War, PitcherRAR = pitcherRar, PitcherWar = pitcher?.War,
                    PitcherRa9War = pitcher?.Ra9War, PitcherBlendWar = pitcher?.BlendWar,
                    TotalWar = (batter?.War ?? 0.0) + (pitcher?.War ?? 0.0),
                };
            }).ToList();
    }

    private async Task<IReadOnlyList<SeasonKey>> ReadSeasonKeysAsync(string pcode, CancellationToken cancellationToken)
    {
        var rows = new Dictionary<(int, string), SeasonKey>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.SeasonYear, x.TeamCode, MAX(x.IsBatter), MAX(x.IsPitcher)
            FROM (
                SELECT GameId, TeamCode, 1 AS IsBatter, 0 AS IsPitcher FROM BatterGameStats WHERE Pcode=$pcode
                UNION ALL
                SELECT GameId, TeamCode, 0, 1 FROM PitcherGameStats WHERE Pcode=$pcode AND HasFinalLine=1
            ) x
            INNER JOIN Games g ON g.GameId=x.GameId
            WHERE g.SeasonYear IS NOT NULL AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            GROUP BY g.SeasonYear, x.TeamCode
            ORDER BY g.SeasonYear, x.TeamCode;
            """;
        command.Parameters.AddWithValue("$pcode", pcode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new SeasonKey(ReadInt(reader, 0), reader.GetString(1), ReadInt(reader, 2) != 0, ReadInt(reader, 3) != 0);
            rows[(row.Year, row.Team)] = row;
        }
        return rows.Values.OrderBy(row => row.Year).ThenBy(row => row.Team, StringComparer.Ordinal).ToList();
    }

    private async Task<IReadOnlyDictionary<(int Year, string Team), BattingSeasonRaw>> ReadBattingSeasonRawAsync(
        string pcode, CancellationToken cancellationToken)
    {
        var result = new Dictionary<(int, string), BattingSeasonRaw>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.SeasonYear, b.TeamCode, COUNT(*), SUM(b.PA), SUM(b.AB), SUM(b.H),
                   SUM(b.Singles), SUM(b.Doubles), SUM(b.Triples), SUM(b.HR), SUM(b.BB), SUM(b.IBB),
                   SUM(b.HBP), SUM(b.SO), SUM(b.SF), SUM(b.SH), SUM(b.GDP), SUM(b.TB),
                   SUM(b.Runs), SUM(b.RBI), SUM(b.SB), SUM(b.CS)
            FROM BatterGameStats b INNER JOIN Games g ON g.GameId=b.GameId
            WHERE b.Pcode=$pcode AND g.SeasonYear IS NOT NULL
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            GROUP BY g.SeasonYear, b.TeamCode;
            """;
        command.Parameters.AddWithValue("$pcode", pcode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            var row = new BattingSeasonRaw
            {
                Year = ReadInt(reader, i++), Team = reader.GetString(i++), Games = ReadInt(reader, i++),
                PA = ReadInt(reader, i++), AB = ReadInt(reader, i++), Hits = ReadInt(reader, i++),
                Singles = ReadInt(reader, i++), Doubles = ReadInt(reader, i++), Triples = ReadInt(reader, i++),
                HomeRuns = ReadInt(reader, i++), Walks = ReadInt(reader, i++), IntentionalWalks = ReadInt(reader, i++),
                HitByPitch = ReadInt(reader, i++), Strikeouts = ReadInt(reader, i++), SacrificeFlies = ReadInt(reader, i++),
                SacrificeBunts = ReadInt(reader, i++), DoublePlays = ReadInt(reader, i++), TotalBases = ReadInt(reader, i++),
                Runs = ReadInt(reader, i++), Rbi = ReadInt(reader, i++), StolenBases = ReadInt(reader, i++),
                CaughtStealing = ReadInt(reader, i++),
            };
            result[(row.Year, row.Team)] = row;
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<(int Year, string Team), PitchingSeasonRaw>> ReadPitchingSeasonRawAsync(
        string pcode, CancellationToken cancellationToken)
    {
        var result = new Dictionary<(int, string), PitchingSeasonRaw>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.SeasonYear, p.TeamCode, COUNT(*), SUM(p.IsStarter), SUM(p.IsReliever),
                   SUM(p.InningsOuts), SUM(p.RunsAllowed), SUM(p.EarnedRuns), SUM(p.HitsAllowed),
                   SUM(p.HomeRunsAllowed), SUM(p.FinalBB), SUM(p.FinalHBP), SUM(p.FinalSO), SUM(p.IFFB)
            FROM PitcherGameStats p INNER JOIN Games g ON g.GameId=p.GameId
            WHERE p.Pcode=$pcode AND p.HasFinalLine=1 AND g.SeasonYear IS NOT NULL
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            GROUP BY g.SeasonYear, p.TeamCode;
            """;
        command.Parameters.AddWithValue("$pcode", pcode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var i = 0;
            var row = new PitchingSeasonRaw
            {
                Year = ReadInt(reader, i++), Team = reader.GetString(i++), Games = ReadInt(reader, i++),
                GamesStarted = ReadInt(reader, i++), ReliefGames = ReadInt(reader, i++),
                InningsOuts = ReadInt(reader, i++), RunsAllowed = ReadInt(reader, i++),
                EarnedRuns = ReadInt(reader, i++), HitsAllowed = ReadInt(reader, i++),
                HomeRuns = ReadInt(reader, i++), Walks = ReadInt(reader, i++),
                HitBatters = ReadInt(reader, i++), Strikeouts = ReadInt(reader, i++),
                InfieldFlies = ReadInt(reader, i++),
            };
            result[(row.Year, row.Team)] = row;
        }
        return result;
    }

    private async Task<IReadOnlyList<PlayerGameLogRow>> ReadGameLogsAsync(string pcode, CancellationToken cancellationToken)
    {
        var rows = new List<PlayerGameLogRow>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.GameDate, g.SeasonYear, COALESCE(b.TeamCode,p.TeamCode,''),
                   g.HomeTeamCode, g.AwayTeamCode, g.Stadium, g.HomeScore, g.AwayScore,
                   b.PA, b.AB, b.H, b.Doubles, b.Triples, b.HR, b.BB, b.HBP, b.SO,
                   b.Runs, b.RBI, b.SB, b.CS,
                   p.InningsOuts, p.EarnedRuns, p.FinalSO, g.GameId
            FROM Games g
            LEFT JOIN BatterGameStats b ON b.GameId=g.GameId AND b.Pcode=$pcode
            LEFT JOIN PitcherGameStats p ON p.GameId=g.GameId AND p.Pcode=$pcode AND p.HasFinalLine=1
            WHERE (b.Pcode IS NOT NULL OR p.Pcode IS NOT NULL)
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            ORDER BY g.GameDate DESC, g.GameId DESC;
            """;
        command.Parameters.AddWithValue("$pcode", pcode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var team = NullableText(reader, 2) ?? string.Empty;
            var home = NullableText(reader, 3) ?? string.Empty;
            var away = NullableText(reader, 4) ?? string.Empty;
            var isHome = string.Equals(team, home, StringComparison.Ordinal);
            var teamScore = isHome ? NullableInt(reader, 6) : NullableInt(reader, 7);
            var opponentScore = isHome ? NullableInt(reader, 7) : NullableInt(reader, 6);
            var resultText = teamScore.HasValue && opponentScore.HasValue
                ? $"{(teamScore > opponentScore ? "W" : teamScore < opponentScore ? "L" : "D")} {teamScore}-{opponentScore}"
                : "-";
            rows.Add(new PlayerGameLogRow
            {
                Date = NullableText(reader, 0) ?? string.Empty, Year = NullableInt(reader, 1),
                Team = team, Opponent = isHome ? away : home, Venue = isHome ? "홈" : "원정",
                Stadium = NullableText(reader, 5) ?? string.Empty, Result = resultText,
                PA = NullableInt(reader, 8), AB = NullableInt(reader, 9), Hits = NullableInt(reader, 10),
                Doubles = NullableInt(reader, 11), Triples = NullableInt(reader, 12), HomeRuns = NullableInt(reader, 13),
                Walks = NullableInt(reader, 14), HitByPitch = NullableInt(reader, 15), Strikeouts = NullableInt(reader, 16),
                Runs = NullableInt(reader, 17), RBI = NullableInt(reader, 18), StolenBases = NullableInt(reader, 19),
                CaughtStealing = NullableInt(reader, 20),
                InningsPitched = reader.IsDBNull(21) ? null : ReadInt(reader, 21) / 3.0,
                EarnedRuns = NullableInt(reader, 22), PitchingStrikeouts = NullableInt(reader, 23),
                GameId = reader.GetString(24),
            });
        }
        return rows;
    }

    private async Task<IReadOnlyList<PlayerPlateAppearanceRow>> ReadPlateAppearanceLogsAsync(
        string pcode, CancellationToken cancellationToken)
    {
        var rows = new List<PlayerPlateAppearanceRow>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.GameDate, pa.BatterPcode, pa.PitcherPcode, pa.BattingTeamCode, pa.FieldingTeamCode,
                   pa.Inning, pa.BattingSide, pa.PitcherName, pa.BatterName, pa.ResultText,
                   pa.ResultType, pa.ActualPitchCount, pa.BeforeOuts,
                   pa.BeforeFirstRunnerName, pa.BeforeSecondRunnerName, pa.BeforeThirdRunnerName,
                   pa.RunsScored, pa.WpaByPlate, pa.GameId
            FROM PlateAppearances pa INNER JOIN Games g ON g.GameId=pa.GameId
            WHERE (pa.BatterPcode=$pcode OR pa.PitcherPcode=$pcode)
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            ORDER BY g.GameDate DESC, pa.GameId DESC, pa.SequenceNumber DESC;
            """;
        command.Parameters.AddWithValue("$pcode", pcode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var battingPerspective = string.Equals(NullableText(reader, 1), pcode, StringComparison.Ordinal);
            rows.Add(new PlayerPlateAppearanceRow
            {
                Date = NullableText(reader, 0) ?? string.Empty,
                Perspective = battingPerspective ? "타격" : "투구",
                Opponent = battingPerspective ? NullableText(reader, 4) ?? string.Empty : NullableText(reader, 3) ?? string.Empty,
                Inning = InningText(NullableInt(reader, 5), (TeamSide)ReadInt(reader, 6)),
                Pitcher = NullableText(reader, 7) ?? string.Empty,
                Batter = NullableText(reader, 8) ?? string.Empty,
                Result = NullableText(reader, 9) ?? ((BattingResultType)ReadInt(reader, 10)).ToString(),
                Pitches = ReadInt(reader, 11), OutsBefore = NullableInt(reader, 12),
                RunnersBefore = RunnersText(NullableText(reader, 13), NullableText(reader, 14), NullableText(reader, 15)),
                RunsScored = ReadInt(reader, 16), Wpa = NullableReal(reader, 17), GameId = reader.GetString(18),
            });
        }
        return rows;
    }

    private async Task<IReadOnlyList<PlayerPitchLogRow>> ReadPitchLogsAsync(
        string pcode, CancellationToken cancellationToken)
    {
        var rows = new List<PlayerPitchLogRow>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.GameDate, p.PitcherPcode, p.BatterPcode,
                   pa.BattingTeamCode, pa.FieldingTeamCode, p.Inning, p.BattingSide,
                   p.BatterName, p.PitcherName, p.DisplayPitchNumber, p.BallsBefore, p.StrikesBefore,
                   p.PitchType, p.SpeedKmh, p.PitchResult,
                   COALESCE(p.CalculatedCrossPlateX,p.CrossPlateX), p.CalculatedCrossPlateZ,
                   p.IsInNominalStrikeZone, p.GameId
            FROM Pitches p
            INNER JOIN Games g ON g.GameId=p.GameId
            LEFT JOIN PlateAppearances pa ON pa.PlateAppearanceId=p.PlateAppearanceId
            WHERE (p.PitcherPcode=$pcode OR p.BatterPcode=$pcode)
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            ORDER BY g.GameDate DESC, p.GameId DESC, p.ActualPitchIndex DESC;
            """;
        command.Parameters.AddWithValue("$pcode", pcode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pitchingPerspective = string.Equals(NullableText(reader, 1), pcode, StringComparison.Ordinal);
            rows.Add(new PlayerPitchLogRow
            {
                Date = NullableText(reader, 0) ?? string.Empty,
                Perspective = pitchingPerspective ? "투구" : "타격",
                Opponent = pitchingPerspective ? NullableText(reader, 3) ?? string.Empty : NullableText(reader, 4) ?? string.Empty,
                Inning = InningText(NullableInt(reader, 5), (TeamSide)ReadInt(reader, 6)),
                Batter = NullableText(reader, 7) ?? string.Empty, Pitcher = NullableText(reader, 8) ?? string.Empty,
                PitchNumber = NullableInt(reader, 9),
                Count = $"{NullableInt(reader, 10)?.ToString(CultureInfo.InvariantCulture) ?? "-"}-{NullableInt(reader, 11)?.ToString(CultureInfo.InvariantCulture) ?? "-"}",
                PitchType = NullableText(reader, 12) ?? string.Empty, SpeedKmh = NullableReal(reader, 13),
                Result = ((PitchResultType)ReadInt(reader, 14)).ToString(),
                PlateX = NullableReal(reader, 15), PlateZ = NullableReal(reader, 16),
                Zone = reader.IsDBNull(17) ? "-" : ReadInt(reader, 17) != 0 ? "존 안" : "존 밖",
                GameId = reader.GetString(18),
            });
        }
        return rows;
    }

    private async Task<IReadOnlyList<RollingBattingGame>> ReadRollingBattingGamesAsync(
        string pcode, CancellationToken cancellationToken)
    {
        var rows = new List<RollingBattingGame>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.GameDate, g.GameId, g.SeasonYear, b.PA, b.AB, b.H, b.Singles, b.Doubles, b.Triples,
                   b.HR, b.BB, b.IBB, b.HBP, b.SO, b.SF, b.TB
            FROM BatterGameStats b INNER JOIN Games g ON g.GameId=b.GameId
            WHERE b.Pcode=$pcode AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            ORDER BY g.GameDate, g.GameId;
            """;
        command.Parameters.AddWithValue("$pcode", pcode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new RollingBattingGame
            {
                Date = NullableText(reader, 0) ?? string.Empty, GameId = reader.GetString(1),
                Year = NullableInt(reader, 2),
                PA = ReadInt(reader, 3), AB = ReadInt(reader, 4), Hits = ReadInt(reader, 5),
                Singles = ReadInt(reader, 6), Doubles = ReadInt(reader, 7), Triples = ReadInt(reader, 8),
                HomeRuns = ReadInt(reader, 9), Walks = ReadInt(reader, 10), IntentionalWalks = ReadInt(reader, 11),
                HitByPitch = ReadInt(reader, 12), Strikeouts = ReadInt(reader, 13),
                SacrificeFlies = ReadInt(reader, 14), TotalBases = ReadInt(reader, 15),
            });
        }
        return rows;
    }

    private async Task<IReadOnlyList<string>> ReadTeamHistoryAsync(string pcode, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT gp.TeamCode, MIN(g.GameDate) AS FirstDate
            FROM GamePlayers gp INNER JOIN Games g ON g.GameId=gp.GameId
            WHERE gp.Pcode=$pcode
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
              AND TRIM(COALESCE(gp.TeamCode,''))<>''
            GROUP BY gp.TeamCode ORDER BY FirstDate, gp.TeamCode;
            """;
        command.Parameters.AddWithValue("$pcode", pcode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(reader.GetString(0));
        return result;
    }

    private static IReadOnlyList<RollingMetricPoint> BuildRollingWrcPlus(
        IReadOnlyList<RollingBattingGame> games,
        int windowGames,
        LeagueReference league)
    {
        var result = new List<RollingMetricPoint>();
        foreach (var seasonGames in games.GroupBy(game => game.Year))
        {
          var season = seasonGames.ToList();
          for (var index = windowGames - 1; index < season.Count; index++)
          {
            var window = season.Skip(index - windowGames + 1).Take(windowGames).ToList();
            var total = new RollingBattingGame
            {
                Year = window[^1].Year,
                PA = window.Sum(row => row.PA), AB = window.Sum(row => row.AB), Hits = window.Sum(row => row.Hits),
                Singles = window.Sum(row => row.Singles), Doubles = window.Sum(row => row.Doubles),
                Triples = window.Sum(row => row.Triples), HomeRuns = window.Sum(row => row.HomeRuns),
                Walks = window.Sum(row => row.Walks), IntentionalWalks = window.Sum(row => row.IntentionalWalks),
                HitByPitch = window.Sum(row => row.HitByPitch), Strikeouts = window.Sum(row => row.Strikeouts),
                SacrificeFlies = window.Sum(row => row.SacrificeFlies), TotalBases = window.Sum(row => row.TotalBases),
            };
            result.Add(new RollingMetricPoint
            {
                Date = window[^1].Date, WindowGames = windowGames, PA = total.PA,
                WrcPlus = ComputeWrcPlus(total, league.GetWobaConstants(total.Year)),
            });
          }
        }
        return result;
    }

    private static double? ComputeWrcPlus(RollingBattingGame row, WobaConstants constants)
    {
        if (row.PA <= 0 || constants.RunsPerPa <= 0) return null;
        var woba = constants.Calculate(
            row.AB, row.Walks, row.IntentionalWalks, row.HitByPitch,
            row.SacrificeFlies, row.Singles, row.Doubles, row.Triples, row.HomeRuns);
        if (!woba.HasValue) return null;
        var wraa = (woba.Value - constants.LeagueWoba) / constants.Scale * row.PA;
        var wrc = wraa + constants.RunsPerPa * row.PA;
        return 100.0 * (wrc / row.PA) / constants.RunsPerPa;
    }

    private static string BuildFormulaDocumentation(
        PlayerProfile profile,
        IReadOnlyList<PlayerBattingSeasonRow> batting,
        IReadOnlyList<PlayerPitchingSeasonRow> pitching,
        LeagueReference league)
    {
        var latestBat = batting.OrderByDescending(row => row.Year).FirstOrDefault();
        var latestPit = pitching.OrderByDescending(row => row.Year).FirstOrDefault();
        var constants = league.GetWobaConstants(latestBat?.Year);
        var lines = new List<string>
        {
            $"선수: {profile.Name} ({profile.Pcode})", $"생년월일: {profile.BirthDate}", string.Empty,
            "[타격 지표]",
            $"wOBA = ({constants.UnintentionalWalk:0.000}×uBB + {constants.HitByPitch:0.000}×HBP + {constants.Single:0.000}×1B + {constants.Double:0.000}×2B + {constants.Triple:0.000}×3B + {constants.HomeRun:0.000}×HR) / (AB + BB - IBB + SF + HBP)",
            $"wRAA = ((wOBA - 리그 wOBA) / {constants.Scale:0.000}) × PA ({constants.Source})",
            "wRC = wRAA + 리그 R/PA × PA",
            "wRC+ = 100 × (wRC / PA) / 리그 R/PA",
            "Site WAR v1 = (타격 Runs + 주루 Runs + 포지션 Runs + 대체선수 Runs) / 10",
            string.Empty, "[투수 지표]",
            "ifFIP = [13×HR + 3×(BB+HBP) - 2×(SO+IFFB)] / IP + ifFIP 상수",
            "IFFB: 포수/1루수 파울플라이, 2루수/3루수/유격수/투수 뜬공",
            "FIPR9 = ifFIP + (리그 RA9 - 리그 ERA)",
            "pFIPR9 = FIPR9 / (FIP 파크팩터 / 100)",
            "dRPW = ((((18-IP/G)×리그 FIPR9 + (IP/G)×pFIPR9) / 18) + 2) × 1.5",
            "KBO 역할별 대체수준 = 최근 3개 정규시즌 저사용 선발/구원 표본의 회귀 pFIPR9",
            "보정 전 fWAR = 평균 대비 승 + 역할별 대체선수 승 (구원은 LI 배수 적용)",
            "구원 LI 배수 = (1 + gmLI) / 2",
            "KBO WARIP = (목표 투수 WAR - 리그 보정 전 투수 WAR 합) / 리그 전체 IP",
            "KBO fWAR v4 = 보정 전 fWAR + KBO WARIP × IP",
            "KBO RA9-WAR = pRA9 기반 보정 전 WAR + RA9 WARIP × IP",
            "Blend WAR = 0.70 × KBO fWAR + 0.30 × KBO RA9-WAR", string.Empty,
        };
        if (latestBat is not null)
            lines.Add($"최근 타격 시즌 {latestBat.Year} {latestBat.Team}: PA {latestBat.PA}, wOBA {Format(latestBat.Woba, "0.000")}, wRC+ {Format(latestBat.WrcPlus, "0.0")}, WAR {Format(latestBat.War, "0.00")}");
        if (latestPit is not null)
            lines.Add($"최근 투구 시즌 {latestPit.Year} {latestPit.Team}: IP {latestPit.InningsPitched:0.0}, ifFIP {Format(latestPit.IfFip, "0.00")}, gmLI {Format(latestPit.GmLi, "0.00")}, KBO fWAR {Format(latestPit.War, "0.00")}, RA9-WAR {Format(latestPit.Ra9War, "0.00")}, Blend {Format(latestPit.BlendWar, "0.00")}");
        lines.Add(string.Empty);
        lines.Add("* 개인 페이지는 관계형 SQLite 테이블만 조회합니다. 원본 JSON을 다시 읽거나 역직렬화하지 않습니다.");
        return string.Join(Environment.NewLine, lines);
    }

    private static int? CalculateAge(string birthDate, int year)
    {
        if (!DateTime.TryParse(birthDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth)) return null;
        var reference = new DateTime(year, 7, 1);
        var age = reference.Year - birth.Year;
        if (reference < birth.AddYears(age)) age--;
        return Math.Max(0, age);
    }

    private static int? ParseActiveYear(string activeYears, bool first)
    {
        var parts = activeYears.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        return int.TryParse(first ? parts[0] : parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ? year : null;
    }

    private static string InningText(int? inning, TeamSide side) => inning.HasValue
        ? $"{inning.Value}회{(side == TeamSide.Away ? "초" : side == TeamSide.Home ? "말" : string.Empty)}"
        : "-";

    private static string RunnersText(string? first, string? second, string? third)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(first)) values.Add($"1루 {first}");
        if (!string.IsNullOrWhiteSpace(second)) values.Add($"2루 {second}");
        if (!string.IsNullOrWhiteSpace(third)) values.Add($"3루 {third}");
        return values.Count == 0 ? "주자 없음" : string.Join(", ", values);
    }

    private static string Format(double? value, string format) => value.HasValue
        ? value.Value.ToString(format, CultureInfo.InvariantCulture)
        : "-";
    private static double? RatePerNine(double value, double innings) => innings > 0 ? value * 9.0 / innings : null;
    private static int ReadInt(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    private static int? NullableInt(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    private static double? NullableReal(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    private static string? NullableText(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null : reader.GetString(ordinal);

    private sealed record SeasonKey(int Year, string Team, bool IsBatter, bool IsPitcher);
    private sealed class BattingSeasonRaw
    {
        public int Year { get; init; }
        public string Team { get; init; } = string.Empty;
        public int Games { get; init; }
        public int PA { get; init; }
        public int AB { get; init; }
        public int Hits { get; init; }
        public int Singles { get; init; }
        public int Doubles { get; init; }
        public int Triples { get; init; }
        public int HomeRuns { get; init; }
        public int Walks { get; init; }
        public int IntentionalWalks { get; init; }
        public int HitByPitch { get; init; }
        public int Strikeouts { get; init; }
        public int SacrificeFlies { get; init; }
        public int SacrificeBunts { get; init; }
        public int DoublePlays { get; init; }
        public int TotalBases { get; init; }
        public int Runs { get; init; }
        public int Rbi { get; init; }
        public int StolenBases { get; init; }
        public int CaughtStealing { get; init; }
    }

    private sealed class PitchingSeasonRaw
    {
        public int Year { get; init; }
        public string Team { get; init; } = string.Empty;
        public int Games { get; init; }
        public int GamesStarted { get; init; }
        public int ReliefGames { get; init; }
        public int InningsOuts { get; init; }
        public int RunsAllowed { get; init; }
        public int EarnedRuns { get; init; }
        public int HitsAllowed { get; init; }
        public int HomeRuns { get; init; }
        public int Walks { get; init; }
        public int HitBatters { get; init; }
        public int Strikeouts { get; init; }
        public int InfieldFlies { get; init; }
    }
    private sealed class RollingBattingGame
    {
        public int? Year { get; init; }
        public string Date { get; init; } = string.Empty;
        public string GameId { get; init; } = string.Empty;
        public int PA { get; init; }
        public int AB { get; init; }
        public int Hits { get; init; }
        public int Singles { get; init; }
        public int Doubles { get; init; }
        public int Triples { get; init; }
        public int HomeRuns { get; init; }
        public int Walks { get; init; }
        public int IntentionalWalks { get; init; }
        public int HitByPitch { get; init; }
        public int Strikeouts { get; init; }
        public int SacrificeFlies { get; init; }
        public int TotalBases { get; init; }
    }
}
