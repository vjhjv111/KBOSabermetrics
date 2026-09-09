using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Application.Teams;

namespace NaverRelay.Infrastructure.Sqlite;

/// <summary>
/// 팀 페이지를 관계형 SQLite 테이블만으로 구성합니다.
/// 원본 JSON과 정규화 JSON blob을 다시 읽지 않습니다.
/// </summary>
public sealed partial class DatabaseTeamPageService : ITeamPageService
{
    private const double Wbb = 0.69;
    private const double Whbp = 0.72;
    private const double W1b = 0.88;
    private const double W2b = 1.247;
    private const double W3b = 1.578;
    private const double Whr = 2.031;
    private const double WobaScale = 1.20;

    private readonly DatabaseCacheService _database;
    private readonly DatabaseAnalyticsService _analytics;

    public DatabaseTeamPageService(DatabaseCacheService database)
    {
        _database = database;
        _analytics = new DatabaseAnalyticsService(database);
    }

    public IReadOnlyList<TeamSearchItem> GetTeams() =>
        GetTeamsAsync().GetAwaiter().GetResult();

    public TeamPageData? GetTeamPage(string teamCode) =>
        GetTeamPageAsync(teamCode).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<TeamSearchItem>> GetTeamsAsync(
        CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, TeamCatalogAccumulator>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SeasonYear, GameDate,
                   HomeTeamCode, HomeTeamName,
                   AwayTeamCode, AwayTeamName
            FROM Games
            WHERE LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_r'
            ORDER BY GameDate, GameId;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var year = NullableInt(reader, 0);
            var date = NullableText(reader, 1);
            AddTeam(map, NullableText(reader, 2), NullableText(reader, 3), year, date);
            AddTeam(map, NullableText(reader, 4), NullableText(reader, 5), year, date);
        }

        return map.Values
            .Select(item => new TeamSearchItem
            {
                TeamCode = item.TeamCode,
                LatestName = string.IsNullOrWhiteSpace(item.LatestName) ? item.TeamCode : item.LatestName,
                FirstSeason = item.FirstSeason,
                LastSeason = item.LastSeason,
                Games = item.Games,
            })
            .OrderBy(item => item.LatestName, StringComparer.CurrentCulture)
            .ThenBy(item => item.TeamCode, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<TeamPageData?> GetTeamPageAsync(
        string teamCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedTeamCode = teamCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedTeamCode)) return null;

        var games = await ReadTeamGamesAsync(normalizedTeamCode, cancellationToken).ConfigureAwait(false);
        if (games.Count == 0) return null;

        var league = await _database.GetLeagueReferenceAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var years = games.Where(row => row.Year.HasValue)
            .Select(row => row.Year!.Value)
            .Distinct()
            .OrderBy(year => year)
            .ToList();

        var snapshots = new Dictionary<int, AnalyticsSnapshot>();
        foreach (var year in years)
        {
            snapshots[year] = await _analytics.GetSnapshotAsync(
                new GameQuery
                {
                    SeasonYear = year,
                    Competition = "정규시즌",
                    TeamCode = normalizedTeamCode,
                },
                league,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var battingRaw = await ReadTeamBattingRawAsync(normalizedTeamCode, cancellationToken).ConfigureAwait(false);
        var pitchingRaw = await ReadTeamPitchingRawAsync(normalizedTeamCode, cancellationToken).ConfigureAwait(false);
        var gameSeasons = BuildGameSeasonRecords(games);

        var battingSeasons = BuildBattingSeasons(
            normalizedTeamCode, gameSeasons, battingRaw, snapshots, league);
        var pitchingSeasons = BuildPitchingSeasons(
            normalizedTeamCode, gameSeasons, pitchingRaw, snapshots, league);
        var values = BuildValueSeasons(normalizedTeamCode, gameSeasons, snapshots);
        var playerBatting = BuildPlayerBattingRows(normalizedTeamCode, snapshots);
        var playerPitching = BuildPlayerPitchingRows(normalizedTeamCode, snapshots);

        var opponentRecords = await ReadOpponentRecordsAsync(
            normalizedTeamCode, games, league, cancellationToken).ConfigureAwait(false);
        var situationSplits = await ReadSituationSplitsAsync(
            normalizedTeamCode, cancellationToken).ConfigureAwait(false);
        var battingByPitchType = await ReadBattingPitchTypeSplitsAsync(
            normalizedTeamCode, cancellationToken).ConfigureAwait(false);
        var pitchingByPitchType = await ReadPitchingPitchTypeSplitsAsync(
            normalizedTeamCode, cancellationToken).ConfigureAwait(false);

        var profile = BuildProfile(normalizedTeamCode, games);
        var gameLogs = games
            .OrderByDescending(row => row.Date, StringComparer.Ordinal)
            .ThenByDescending(row => row.GameId, StringComparer.Ordinal)
            .Select(row => new TeamGameLogRow
            {
                Date = row.Date,
                Year = row.Year,
                OpponentCode = row.OpponentCode,
                OpponentName = row.OpponentName,
                Venue = row.IsHome ? "홈" : "원정",
                Stadium = row.Stadium,
                RunsFor = row.RunsFor,
                RunsAgainst = row.RunsAgainst,
                Result = ResultText(row.RunsFor, row.RunsAgainst),
                GameId = row.GameId,
            })
            .ToList();

        return new TeamPageData
        {
            Profile = profile,
            BattingSeasons = battingSeasons,
            PitchingSeasons = pitchingSeasons,
            ValueSeasons = values,
            PlayerBatting = playerBatting,
            PlayerPitching = playerPitching,
            OpponentRecords = opponentRecords,
            SituationSplits = situationSplits,
            BattingByPitchType = battingByPitchType,
            PitchingByPitchType = pitchingByPitchType,
            GameLogs = gameLogs,
            FormulaDocumentation = BuildFormulaDocumentation(profile),
        };
    }

    private static void AddTeam(
        IDictionary<string, TeamCatalogAccumulator> map,
        string? code,
        string? name,
        int? year,
        string? date)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var normalized = code.Trim().ToUpperInvariant();
        if (!map.TryGetValue(normalized, out var item))
        {
            item = new TeamCatalogAccumulator { TeamCode = normalized };
            map[normalized] = item;
        }

        item.Games++;
        if (year.HasValue)
        {
            item.FirstSeason = !item.FirstSeason.HasValue || year.Value < item.FirstSeason.Value
                ? year.Value : item.FirstSeason;
            item.LastSeason = !item.LastSeason.HasValue || year.Value > item.LastSeason.Value
                ? year.Value : item.LastSeason;
        }

        if (!string.IsNullOrWhiteSpace(name) &&
            (string.IsNullOrWhiteSpace(item.LastNameDate) || string.CompareOrdinal(date, item.LastNameDate) >= 0))
        {
            item.LatestName = name.Trim();
            item.LastNameDate = date ?? string.Empty;
        }
    }

    private async Task<IReadOnlyList<TeamGameRecord>> ReadTeamGamesAsync(
        string teamCode,
        CancellationToken cancellationToken)
    {
        var rows = new List<TeamGameRecord>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GameId, SeasonYear, GameDate, Stadium,
                   HomeTeamCode, HomeTeamName, HomeScore,
                   AwayTeamCode, AwayTeamName, AwayScore
            FROM Games
            WHERE LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_r'
              AND (HomeTeamCode=$team OR AwayTeamCode=$team)
            ORDER BY GameDate, GameId;
            """;
        command.Parameters.AddWithValue("$team", teamCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var homeCode = NullableText(reader, 4) ?? string.Empty;
            var isHome = string.Equals(homeCode, teamCode, StringComparison.OrdinalIgnoreCase);
            rows.Add(new TeamGameRecord
            {
                GameId = reader.GetString(0),
                Year = NullableInt(reader, 1),
                Date = NullableText(reader, 2) ?? string.Empty,
                Stadium = NullableText(reader, 3) ?? "-",
                IsHome = isHome,
                TeamName = isHome ? NullableText(reader, 5) ?? teamCode : NullableText(reader, 8) ?? teamCode,
                OpponentCode = isHome ? NullableText(reader, 7) ?? string.Empty : homeCode,
                OpponentName = isHome ? NullableText(reader, 8) ?? string.Empty : NullableText(reader, 5) ?? string.Empty,
                RunsFor = isHome ? NullableInt(reader, 6) : NullableInt(reader, 9),
                RunsAgainst = isHome ? NullableInt(reader, 9) : NullableInt(reader, 6),
            });
        }
        return rows;
    }

    private async Task<IReadOnlyDictionary<int, TeamBattingRaw>> ReadTeamBattingRawAsync(
        string teamCode,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, TeamBattingRaw>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.SeasonYear,
                   COUNT(DISTINCT b.GameId),
                   SUM(b.PA), SUM(b.AB), SUM(b.H), SUM(b.Singles), SUM(b.Doubles), SUM(b.Triples),
                   SUM(b.HR), SUM(b.BB), SUM(b.IBB), SUM(b.HBP), SUM(b.SO), SUM(b.SF), SUM(b.SH),
                   SUM(b.GDP), SUM(b.TB), SUM(b.Runs), SUM(b.RBI), SUM(b.SB), SUM(b.CS)
            FROM BatterGameStats b INNER JOIN Games g ON g.GameId=b.GameId
            WHERE b.TeamCode=$team
              AND g.SeasonYear IS NOT NULL
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            GROUP BY g.SeasonYear
            ORDER BY g.SeasonYear;
            """;
        command.Parameters.AddWithValue("$team", teamCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new TeamBattingRaw
            {
                Year = reader.GetInt32(0), Games = ReadInt(reader, 1), PA = ReadInt(reader, 2),
                AB = ReadInt(reader, 3), Hits = ReadInt(reader, 4), Singles = ReadInt(reader, 5),
                Doubles = ReadInt(reader, 6), Triples = ReadInt(reader, 7), HomeRuns = ReadInt(reader, 8),
                Walks = ReadInt(reader, 9), IntentionalWalks = ReadInt(reader, 10), HitByPitch = ReadInt(reader, 11),
                Strikeouts = ReadInt(reader, 12), SacrificeFlies = ReadInt(reader, 13),
                SacrificeBunts = ReadInt(reader, 14), DoublePlays = ReadInt(reader, 15),
                TotalBases = ReadInt(reader, 16), Runs = ReadInt(reader, 17), RBI = ReadInt(reader, 18),
                StolenBases = ReadInt(reader, 19), CaughtStealing = ReadInt(reader, 20),
            };
            result[row.Year] = row;
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<int, TeamPitchingRaw>> ReadTeamPitchingRawAsync(
        string teamCode,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, TeamPitchingRaw>();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.SeasonYear, COALESCE(NULLIF(TRIM(g.Stadium),''),'-'),
                   COUNT(DISTINCT p.GameId), SUM(p.TBF), SUM(p.FlyBalls), SUM(p.IFFB), SUM(p.Pitches),
                   SUM(p.IsStarter), SUM(p.IsReliever), SUM(p.InningsOuts), SUM(p.HitsAllowed),
                   SUM(p.HomeRunsAllowed), SUM(p.FinalBB), SUM(p.FinalHBP), SUM(p.FinalSO),
                   SUM(p.RunsAllowed), SUM(p.EarnedRuns), SUM(p.EntryAbsoluteWpaSum), SUM(p.EntryWpaCount)
            FROM PitcherGameStats p INNER JOIN Games g ON g.GameId=p.GameId
            WHERE p.TeamCode=$team
              AND g.SeasonYear IS NOT NULL
              AND LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'
            GROUP BY g.SeasonYear, COALESCE(NULLIF(TRIM(g.Stadium),''),'-')
            ORDER BY g.SeasonYear;
            """;
        command.Parameters.AddWithValue("$team", teamCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var year = reader.GetInt32(0);
            if (!result.TryGetValue(year, out var row))
            {
                row = new TeamPitchingRaw { Year = year };
                result[year] = row;
            }
            var stadium = reader.GetString(1);
            row.Games += ReadInt(reader, 2);
            row.BattersFaced += ReadInt(reader, 3);
            row.FlyBalls += ReadInt(reader, 4);
            row.InfieldFlies += ReadInt(reader, 5);
            row.Pitches += ReadInt(reader, 6);
            row.GamesStarted += ReadInt(reader, 7);
            row.ReliefGames += ReadInt(reader, 8);
            var outs = ReadInt(reader, 9);
            row.InningsOuts += outs;
            row.HitsAllowed += ReadInt(reader, 10);
            row.HomeRuns += ReadInt(reader, 11);
            row.Walks += ReadInt(reader, 12);
            row.HitBatters += ReadInt(reader, 13);
            row.Strikeouts += ReadInt(reader, 14);
            row.RunsAllowed += ReadInt(reader, 15);
            row.EarnedRuns += ReadInt(reader, 16);
            row.EntryAbsoluteWpaSum += NullableReal(reader, 17) ?? 0.0;
            row.EntryWpaCount += ReadInt(reader, 18);
            row.StadiumOuts[stadium] = row.StadiumOuts.GetValueOrDefault(stadium) + outs;
        }
        return result;
    }

    private static IReadOnlyDictionary<int, TeamGameSeasonRecord> BuildGameSeasonRecords(
        IReadOnlyList<TeamGameRecord> games)
    {
        var result = new Dictionary<int, TeamGameSeasonRecord>();
        foreach (var game in games.Where(row => row.Year.HasValue))
        {
            var year = game.Year!.Value;
            if (!result.TryGetValue(year, out var row))
            {
                row = new TeamGameSeasonRecord { Year = year };
                result[year] = row;
            }
            row.Games++;
            row.TeamName = game.TeamName;
            if (game.RunsFor.HasValue) row.RunsFor += game.RunsFor.Value;
            if (game.RunsAgainst.HasValue) row.RunsAgainst += game.RunsAgainst.Value;
            AddDecision(row, game.RunsFor, game.RunsAgainst);
        }
        return result;
    }

    private static IReadOnlyList<TeamBattingSeasonRow> BuildBattingSeasons(
        string teamCode,
        IReadOnlyDictionary<int, TeamGameSeasonRecord> gameSeasons,
        IReadOnlyDictionary<int, TeamBattingRaw> battingRaw,
        IReadOnlyDictionary<int, AnalyticsSnapshot> snapshots,
        LeagueReference league)
    {
        var result = new List<TeamBattingSeasonRow>();
        foreach (var year in gameSeasons.Keys.Union(battingRaw.Keys).Distinct().OrderBy(year => year))
        {
            if (!battingRaw.TryGetValue(year, out var raw)) continue;
            gameSeasons.TryGetValue(year, out var game);
            snapshots.TryGetValue(year, out var snapshot);

            var avg = Divide(raw.Hits, raw.AB);
            var obp = Divide(raw.Hits + raw.Walks + raw.HitByPitch,
                raw.AB + raw.Walks + raw.HitByPitch + raw.SacrificeFlies);
            var slg = Divide(raw.TotalBases, raw.AB);
            var ops = obp.HasValue && slg.HasValue ? obp.Value + slg.Value : (double?)null;
            var wobaDenominator = raw.AB + raw.Walks - raw.IntentionalWalks + raw.SacrificeFlies + raw.HitByPitch;
            double? woba = wobaDenominator > 0
                ? (Wbb * (raw.Walks - raw.IntentionalWalks) + Whbp * raw.HitByPitch +
                   W1b * raw.Singles + W2b * raw.Doubles + W3b * raw.Triples + Whr * raw.HomeRuns) /
                  wobaDenominator
                : null;
            double? wraa = woba.HasValue
                ? (woba.Value - league.Woba) / WobaScale * raw.PA
                : null;
            double? wrc = wraa.HasValue ? wraa.Value + league.RunsPerPa * raw.PA : null;
            double? wrcPlus = wrc.HasValue && raw.PA > 0 && league.RunsPerPa > 0
                ? 100.0 * (wrc.Value / raw.PA) / league.RunsPerPa
                : null;
            double? opsPlus = obp.HasValue && slg.HasValue && league.Obp > 0 && league.Slg > 0
                ? 100.0 * (obp.Value / league.Obp + slg.Value / league.Slg - 1.0)
                : null;
            var batterWar = snapshot?.BatterValues
                .Where(row => string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase))
                .Sum(row => row.War ?? 0.0);

            result.Add(new TeamBattingSeasonRow
            {
                Year = year, TeamName = game?.TeamName ?? teamCode,
                Games = game?.Games ?? raw.Games, Wins = game?.Wins ?? 0,
                Losses = game?.Losses ?? 0, Ties = game?.Ties ?? 0,
                PA = raw.PA, AB = raw.AB, Runs = raw.Runs, Hits = raw.Hits,
                Singles = raw.Singles, Doubles = raw.Doubles, Triples = raw.Triples,
                HomeRuns = raw.HomeRuns, TotalBases = raw.TotalBases, RBI = raw.RBI,
                StolenBases = raw.StolenBases, CaughtStealing = raw.CaughtStealing,
                Walks = raw.Walks, IntentionalWalks = raw.IntentionalWalks,
                HitByPitch = raw.HitByPitch, Strikeouts = raw.Strikeouts,
                DoublePlays = raw.DoublePlays, SacrificeBunts = raw.SacrificeBunts,
                SacrificeFlies = raw.SacrificeFlies, AVG = avg, OBP = obp, SLG = slg,
                OPS = ops, ISO = avg.HasValue && slg.HasValue ? slg.Value - avg.Value : null,
                BABIP = Divide(raw.Hits - raw.HomeRuns,
                    raw.AB - raw.Strikeouts - raw.HomeRuns + raw.SacrificeFlies),
                WalkRate = Divide(raw.Walks, raw.PA), StrikeoutRate = Divide(raw.Strikeouts, raw.PA),
                Woba = woba, Wraa = wraa, Wrc = wrc, WrcPlus = wrcPlus, OpsPlus = opsPlus,
                BatterWar = snapshot is null ? null : batterWar,
            });
        }
        return result;
    }

    private static IReadOnlyList<TeamPitchingSeasonRow> BuildPitchingSeasons(
        string teamCode,
        IReadOnlyDictionary<int, TeamGameSeasonRecord> gameSeasons,
        IReadOnlyDictionary<int, TeamPitchingRaw> pitchingRaw,
        IReadOnlyDictionary<int, AnalyticsSnapshot> snapshots,
        LeagueReference league)
    {
        var result = new List<TeamPitchingSeasonRow>();
        var parkByStadium = league.ParkFactors
            .Where(row => !string.IsNullOrWhiteSpace(row.Stadium))
            .GroupBy(row => row.Stadium, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().UsedFipFactor ?? 100.0,
                StringComparer.OrdinalIgnoreCase);

        foreach (var year in gameSeasons.Keys.Union(pitchingRaw.Keys).Distinct().OrderBy(year => year))
        {
            if (!pitchingRaw.TryGetValue(year, out var raw)) continue;
            gameSeasons.TryGetValue(year, out var game);
            snapshots.TryGetValue(year, out var snapshot);
            var innings = raw.InningsOuts / 3.0;
            double? fip = innings > 0
                ? (13.0 * raw.HomeRuns + 3.0 * (raw.Walks + raw.HitBatters) - 2.0 * raw.Strikeouts) /
                  innings + league.FipConstant
                : null;
            var expectedHomeRuns = raw.FlyBalls * league.HrPerFlyBall;
            double? xfip = innings > 0
                ? (13.0 * expectedHomeRuns + 3.0 * (raw.Walks + raw.HitBatters) - 2.0 * raw.Strikeouts) /
                  innings + league.FipConstant
                : null;
            double? ifFip = innings > 0
                ? (13.0 * raw.HomeRuns + 3.0 * (raw.Walks + raw.HitBatters) -
                   2.0 * (raw.Strikeouts + raw.InfieldFlies)) / innings + league.IfFipConstant
                : null;
            double? fipR9 = ifFip.HasValue ? ifFip.Value + league.Ra9Adjustment : null;
            var weightedPark = 0.0;
            foreach (var item in raw.StadiumOuts)
            {
                var factor = parkByStadium.TryGetValue(item.Key, out var stored) ? stored : 100.0;
                weightedPark += item.Value * factor;
            }
            var parkFactor = raw.InningsOuts > 0 ? weightedPark / raw.InningsOuts : 100.0;
            double? adjusted = fipR9.HasValue
                ? fipR9.Value / Math.Max(0.5, parkFactor / 100.0)
                : null;
            var teamGames = Math.Max(1, game?.Games ?? raw.Games);
            var inningsPerGame = innings / teamGames;
            double? dynamicRunsPerWin = adjusted.HasValue
                ? ((((18.0 - inningsPerGame) * league.LeagueFipR9 + inningsPerGame * adjusted.Value) / 18.0) + 2.0) * 1.5
                : null;
            double? gmLi = raw.EntryWpaCount > 0 && league.AverageAbsoluteWpa > 0
                ? Math.Clamp((raw.EntryAbsoluteWpaSum / raw.EntryWpaCount) / league.AverageAbsoluteWpa, 0.1, 5.0)
                : 1.0;
            var teamPitchers = snapshot?.PitcherValues
                .Where(row => string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var pitcherWar = teamPitchers?.Sum(row => row.War ?? 0.0);
            var pitcherRa9War = teamPitchers?.Sum(row => row.Ra9War ?? 0.0);
            var pitcherBlendWar = teamPitchers?.Sum(row => row.BlendWar ?? 0.0);

            result.Add(new TeamPitchingSeasonRow
            {
                Year = year, TeamName = game?.TeamName ?? teamCode,
                Games = game?.Games ?? raw.Games, Wins = game?.Wins ?? 0,
                Losses = game?.Losses ?? 0, Ties = game?.Ties ?? 0,
                InningsPitched = innings, RunsAllowed = raw.RunsAllowed,
                EarnedRuns = raw.EarnedRuns, HitsAllowed = raw.HitsAllowed,
                HomeRunsAllowed = raw.HomeRuns, Walks = raw.Walks,
                HitBatters = raw.HitBatters, Strikeouts = raw.Strikeouts,
                InfieldFlies = raw.InfieldFlies,
                ERA = RatePerNine(raw.EarnedRuns, innings),
                WHIP = innings > 0 ? (raw.HitsAllowed + raw.Walks) / innings : null,
                StrikeoutsPerNine = RatePerNine(raw.Strikeouts, innings),
                WalksPerNine = RatePerNine(raw.Walks, innings),
                HomeRunsPerNine = RatePerNine(raw.HomeRuns, innings),
                Fip = fip, Xfip = xfip, IfFip = ifFip, FipR9 = fipR9,
                ParkFactor = parkFactor, ParkAdjustedFipR9 = adjusted,
                DynamicRunsPerWin = dynamicRunsPerWin, GmLi = gmLi,
                PitcherWar = snapshot is null ? null : pitcherWar,
                PitcherRa9War = snapshot is null ? null : pitcherRa9War,
                PitcherBlendWar = snapshot is null ? null : pitcherBlendWar,
            });
        }
        return result;
    }

    private static IReadOnlyList<TeamValueSeasonRow> BuildValueSeasons(
        string teamCode,
        IReadOnlyDictionary<int, TeamGameSeasonRecord> gameSeasons,
        IReadOnlyDictionary<int, AnalyticsSnapshot> snapshots)
    {
        var result = new List<TeamValueSeasonRow>();
        foreach (var year in snapshots.Keys.OrderBy(year => year))
        {
            var snapshot = snapshots[year];
            var batters = snapshot.BatterValues
                .Where(row => string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var pitchers = snapshot.PitcherValues
                .Where(row => string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            gameSeasons.TryGetValue(year, out var game);
            var batterWar = batters.Sum(row => row.War ?? 0.0);
            var pitcherWar = pitchers.Sum(row => row.War ?? 0.0);
            var pitcherRa9War = pitchers.Sum(row => row.Ra9War ?? 0.0);
            var pitcherBlendWar = pitchers.Sum(row => row.BlendWar ?? 0.0);
            result.Add(new TeamValueSeasonRow
            {
                Year = year, TeamName = game?.TeamName ?? teamCode,
                BattingRuns = batters.Sum(row => row.BattingRuns ?? 0.0),
                RunningRuns = batters.Sum(row => row.RunningRuns ?? 0.0),
                FieldingRuns = batters.Sum(row => row.FieldingRuns ?? 0.0),
                PositionRuns = batters.Sum(row => row.PositionRuns ?? 0.0),
                ReplacementRuns = batters.Sum(row => row.ReplacementRuns ?? 0.0),
                BatterRAR = batters.Sum(row => row.RunsAboveReplacement ?? 0.0),
                BatterWar = batterWar,
                PitcherRAR = pitchers.Sum(row => row.RunsAboveReplacement ?? 0.0),
                PitcherWar = pitcherWar,
                PitcherRa9War = pitcherRa9War,
                PitcherBlendWar = pitcherBlendWar,
                TotalWar = batterWar + pitcherWar,
            });
        }
        return result;
    }

    private static IReadOnlyList<TeamPlayerBattingRow> BuildPlayerBattingRows(
        string teamCode,
        IReadOnlyDictionary<int, AnalyticsSnapshot> snapshots)
    {
        var rows = new List<TeamPlayerBattingRow>();
        foreach (var item in snapshots.OrderBy(pair => pair.Key))
        {
            var year = item.Key;
            var snapshot = item.Value;
            foreach (var classic in snapshot.BatterClassic
                         .Where(row => string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase)))
            {
                var saber = snapshot.BatterSabermetrics.FirstOrDefault(row =>
                    string.Equals(row.Pcode, classic.Pcode, StringComparison.Ordinal) &&
                    string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase));
                var value = snapshot.BatterValues.FirstOrDefault(row =>
                    string.Equals(row.Pcode, classic.Pcode, StringComparison.Ordinal) &&
                    string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase));
                rows.Add(new TeamPlayerBattingRow
                {
                    Year = year, Pcode = classic.Pcode ?? string.Empty, Name = classic.Name ?? string.Empty,
                    Position = value?.PrimaryPosition ?? "-", Games = classic.Games, PA = classic.PA,
                    AB = classic.AB, Hits = classic.H, Doubles = classic.Doubles, Triples = classic.Triples,
                    HomeRuns = classic.HomeRuns, Walks = classic.Walks, Strikeouts = classic.Strikeouts,
                    AVG = classic.AVG, OBP = classic.OBP, SLG = classic.SLG, OPS = classic.OPS,
                    Woba = saber?.Woba, WrcPlus = saber?.WrcPlus, War = value?.War,
                });
            }
        }
        return rows.OrderByDescending(row => row.Year)
            .ThenByDescending(row => row.War ?? double.MinValue)
            .ThenByDescending(row => row.PA)
            .ToList();
    }

    private static IReadOnlyList<TeamPlayerPitchingRow> BuildPlayerPitchingRows(
        string teamCode,
        IReadOnlyDictionary<int, AnalyticsSnapshot> snapshots)
    {
        var rows = new List<TeamPlayerPitchingRow>();
        foreach (var item in snapshots.OrderBy(pair => pair.Key))
        {
            var year = item.Key;
            var snapshot = item.Value;
            foreach (var classic in snapshot.PitcherClassic
                         .Where(row => string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase)))
            {
                var saber = snapshot.PitcherSabermetrics.FirstOrDefault(row =>
                    string.Equals(row.Pcode, classic.Pcode, StringComparison.Ordinal) &&
                    string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase));
                var value = snapshot.PitcherValues.FirstOrDefault(row =>
                    string.Equals(row.Pcode, classic.Pcode, StringComparison.Ordinal) &&
                    string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase));
                var innings = value?.InningsPitched ?? saber?.InningsPitched;
                rows.Add(new TeamPlayerPitchingRow
                {
                    Year = year, Pcode = classic.Pcode ?? string.Empty, Name = classic.Name ?? string.Empty,
                    Games = classic.Games, GamesStarted = value?.GamesStarted ?? 0,
                    InningsPitched = innings,
                    ERA = innings.HasValue && innings.Value > 0 && value is not null
                        ? value.EarnedRuns * 9.0 / innings.Value : null,
                    WHIP = innings.HasValue && innings.Value > 0
                        ? (classic.Hits + classic.Walks) / innings.Value : null,
                    StrikeoutsPerNine = saber?.StrikeoutsPerNine,
                    WalksPerNine = saber?.WalksPerNine,
                    Fip = saber?.Fip, IfFip = value?.IfFip,
                    GmLi = value?.GmLi, War = value?.War,
                    Ra9War = value?.Ra9War, BlendWar = value?.BlendWar,
                });
            }
        }
        return rows.OrderByDescending(row => row.Year)
            .ThenByDescending(row => row.War ?? double.MinValue)
            .ThenByDescending(row => row.InningsPitched ?? 0.0)
            .ToList();
    }

    private static TeamProfile BuildProfile(string teamCode, IReadOnlyList<TeamGameRecord> games)
    {
        var wins = 0;
        var losses = 0;
        var ties = 0;
        var runsFor = 0;
        var runsAgainst = 0;
        foreach (var game in games)
        {
            if (game.RunsFor.HasValue) runsFor += game.RunsFor.Value;
            if (game.RunsAgainst.HasValue) runsAgainst += game.RunsAgainst.Value;
            if (!game.RunsFor.HasValue || !game.RunsAgainst.HasValue) continue;
            if (game.RunsFor.Value > game.RunsAgainst.Value) wins++;
            else if (game.RunsFor.Value < game.RunsAgainst.Value) losses++;
            else ties++;
        }

        var latest = games.OrderByDescending(row => row.Date, StringComparer.Ordinal)
            .ThenByDescending(row => row.GameId, StringComparer.Ordinal)
            .First();
        var names = DistinctPreservingOrder(games.Select(row => row.TeamName));
        var stadiums = DistinctPreservingOrder(games.Select(row => row.Stadium));
        return new TeamProfile
        {
            TeamCode = teamCode,
            LatestName = latest.TeamName,
            NameHistory = names.Count == 0 ? latest.TeamName : string.Join(" → ", names),
            StadiumHistory = stadiums.Count == 0 ? "-" : string.Join(", ", stadiums),
            FirstSeason = games.Where(row => row.Year.HasValue).Select(row => row.Year!.Value).DefaultIfEmpty().Min(),
            LastSeason = games.Where(row => row.Year.HasValue).Select(row => row.Year!.Value).DefaultIfEmpty().Max(),
            Games = games.Count,
            Wins = wins,
            Losses = losses,
            Ties = ties,
            RunsFor = runsFor,
            RunsAgainst = runsAgainst,
            WinningPercentage = wins + losses > 0 ? (double)wins / (wins + losses) : null,
        };
    }

    private static string BuildFormulaDocumentation(TeamProfile profile) => string.Join(Environment.NewLine,
    [
        $"팀: {profile.LatestName} ({profile.TeamCode})",
        $"팀명 이력: {profile.NameHistory}",
        string.Empty,
        "[팀 타격]",
        "팀 AVG/OBP/SLG/OPS는 선택 시즌의 모든 타자 경기 기록을 합산한 뒤 다시 계산합니다.",
        "wOBA = (0.69×uBB + 0.72×HBP + 0.88×1B + 1.247×2B + 1.578×3B + 2.031×HR) / (AB + BB - IBB + SF + HBP)",
        "wRC+ = 100 × (팀 wRC / 팀 PA) / 리그 R/PA",
        string.Empty,
        "[팀 투수 가치]",
        "KBO fWAR 합 = 소속 투수별 KBO fWAR v3의 합",
        "KBO RA9-WAR 합 = 소속 투수별 pRA9 기반 WAR의 합",
        "Blend WAR 합 = 0.70×KBO fWAR 합 + 0.30×KBO RA9-WAR 합",
        "KBO WARIP는 리그 전체 목표 투수 WAR와 보정 전 합의 차이를 이닝당 균일 배분합니다.",
        string.Empty,
        "[상대전적]",
        "승률 = W / (W + L), 무승부는 분모에서 제외합니다.",
        "상대별 타격·투구 기록은 roundCode=kbo_r 경기만 집계합니다.",
        string.Empty,
        "[구종별]",
        "구종별 타격 성적은 각 타석의 마지막 실제 투구 구종에 타석 결과를 귀속합니다.",
        "구종별 투구 성과는 해당 팀 투수의 모든 실제 투구를 구종별로 집계합니다.",
        string.Empty,
        "[상황별]",
        "클러치: 7회 이후, 팀 기준 절대 점수차 3점 이내",
        "득점권: 타석 시작 시 2루 또는 3루에 주자가 있는 상황",
        string.Empty,
        "* 팀 페이지는 관계형 SQLite 테이블만 조회하며 원본 JSON을 다시 읽지 않습니다.",
    ]);

    private static void AddDecision(TeamGameSeasonRecord row, int? runsFor, int? runsAgainst)
    {
        if (!runsFor.HasValue || !runsAgainst.HasValue) return;
        if (runsFor.Value > runsAgainst.Value) row.Wins++;
        else if (runsFor.Value < runsAgainst.Value) row.Losses++;
        else row.Ties++;
    }

    private static string ResultText(int? runsFor, int? runsAgainst)
    {
        if (!runsFor.HasValue || !runsAgainst.HasValue) return "-";
        return runsFor.Value > runsAgainst.Value ? "승" : runsFor.Value < runsAgainst.Value ? "패" : "무";
    }

    private static List<string> DistinctPreservingOrder(IEnumerable<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value)) continue;
            result.Add(value);
        }
        return result;
    }

    private static double? Divide(double numerator, double denominator) =>
        denominator > 0 ? numerator / denominator : null;

    private static double? RatePerNine(double numerator, double innings) =>
        innings > 0 ? numerator * 9.0 / innings : null;

    private static int ReadInt(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static int? NullableInt(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static double? NullableReal(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static string? NullableText(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null : reader.GetString(ordinal);

    private sealed class TeamCatalogAccumulator
    {
        public string TeamCode { get; init; } = string.Empty;
        public string LatestName { get; set; } = string.Empty;
        public string LastNameDate { get; set; } = string.Empty;
        public int? FirstSeason { get; set; }
        public int? LastSeason { get; set; }
        public int Games { get; set; }
    }

    private sealed class TeamGameRecord
    {
        public string GameId { get; init; } = string.Empty;
        public int? Year { get; init; }
        public string Date { get; init; } = string.Empty;
        public string Stadium { get; init; } = string.Empty;
        public bool IsHome { get; init; }
        public string TeamName { get; init; } = string.Empty;
        public string OpponentCode { get; init; } = string.Empty;
        public string OpponentName { get; init; } = string.Empty;
        public int? RunsFor { get; init; }
        public int? RunsAgainst { get; init; }
    }

    private sealed class TeamGameSeasonRecord
    {
        public int Year { get; init; }
        public string TeamName { get; set; } = string.Empty;
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Ties { get; set; }
        public int RunsFor { get; set; }
        public int RunsAgainst { get; set; }
    }

    private sealed class TeamBattingRaw
    {
        public int Year { get; init; }
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
        public int RBI { get; init; }
        public int StolenBases { get; init; }
        public int CaughtStealing { get; init; }
    }

    private sealed class TeamPitchingRaw
    {
        public int Year { get; init; }
        public int Games { get; set; }
        public int BattersFaced { get; set; }
        public int FlyBalls { get; set; }
        public int InfieldFlies { get; set; }
        public int Pitches { get; set; }
        public int GamesStarted { get; set; }
        public int ReliefGames { get; set; }
        public int InningsOuts { get; set; }
        public int HitsAllowed { get; set; }
        public int HomeRuns { get; set; }
        public int Walks { get; set; }
        public int HitBatters { get; set; }
        public int Strikeouts { get; set; }
        public int RunsAllowed { get; set; }
        public int EarnedRuns { get; set; }
        public double EntryAbsoluteWpaSum { get; set; }
        public int EntryWpaCount { get; set; }
        public Dictionary<string, int> StadiumOuts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
