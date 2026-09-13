using NaverRelay.Application.Statistics;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

/// <summary>
/// JSON 또는 NormalizedGame 역직렬화 없이 BatterGameStats/PitcherGameStats를 SQL로 합산해
/// 화면용 통계를 만듭니다. 계산 결과는 DataVersion 기반 ComputedCache에 저장됩니다.
/// </summary>
public sealed partial class DatabaseAnalyticsService : IAnalyticsQueryService
{
    private const string AnalyticsCacheVersion = "relational-analytics-official-per-nine-v4";
    private const double Wbb = 0.69;
    private const double Whbp = 0.72;
    private const double W1b = 0.88;
    private const double W2b = 1.247;
    private const double W3b = 1.578;
    private const double Whr = 2.031;
    private const double WobaScale = 1.20;

    private readonly DatabaseCacheService _database;

    public DatabaseAnalyticsService(DatabaseCacheService database) => _database = database;

    public async Task<AnalyticsSnapshot> GetSnapshotAsync(
        GameQuery query,
        LeagueReference league,
        IProgress<DatabaseLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{AnalyticsCacheVersion}:{query.CacheKey}";
        var cached = await _database.TryLoadComputedAsync<AnalyticsSnapshot>(cacheKey, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null) return cached;

        var data = await _database.GetAggregateDataAsync(query, progress, cancellationToken).ConfigureAwait(false);
        var allocation = await GetWarAllocationCalibrationAsync(query, league, cancellationToken).ConfigureAwait(false);
        var result = Build(data, league, query.SeasonYear, allocation);
        await _database.SaveComputedAsync(cacheKey, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static AnalyticsSnapshot Build(WarehouseAnalyticsData data, LeagueReference league, int? seasonYear, WarAllocationCalibration allocation)
    {
        var batterClassic = data.Batters.Select(BuildBatterClassic)
            .OrderByDescending(row => row.PA)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        var batterSaber = data.Batters.Select(row => BuildBatterSaber(row, league))
            .OrderByDescending(row => row.PA)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        var pitcherClassic = data.Pitchers.Select(BuildPitcherClassic)
            .OrderByDescending(row => row.BattersFaced)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        var pitcherSaber = data.Pitchers.Select(row => BuildPitcherSaber(row, league))
            .OrderByDescending(row => row.BattersFaced)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        var batterDiscipline = data.Batters.Select(BuildBatterDiscipline)
            .OrderByDescending(row => row.Pitches)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        var pitcherDiscipline = data.Pitchers.Select(BuildPitcherDiscipline)
            .OrderByDescending(row => row.Pitches)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();

        var saberByKey = batterSaber.ToDictionary(
            row => PlayerKey(row.Pcode, row.TeamCode),
            StringComparer.Ordinal);
        var batterValues = data.Batters
            .Select(row => BuildBatterValue(row, saberByKey.GetValueOrDefault(PlayerKey(row.Pcode, row.TeamCode)), allocation.BatterReplacementRunsPerPa))
            .OrderByDescending(row => row.War)
            .ThenByDescending(row => row.PA)
            .ToList();
        var pitcherValues = data.Pitchers
            .Where(row => row.FinalGames > 0)
            .Select(row => BuildPitcherValue(row, league, seasonYear, allocation.PitcherWarPerInning))
            .OrderByDescending(row => row.War)
            .ThenByDescending(row => row.InningsPitched)
            .ToList();

        return new AnalyticsSnapshot
        {
            BatterClassic = batterClassic,
            PitcherClassic = pitcherClassic,
            BatterSabermetrics = batterSaber,
            PitcherSabermetrics = pitcherSaber,
            BatterDiscipline = batterDiscipline,
            PitcherDiscipline = pitcherDiscipline,
            BatterValues = batterValues,
            PitcherValues = pitcherValues,
            TeamGames = new Dictionary<string, int>(data.TeamGames, StringComparer.Ordinal),
            BatterPa = batterSaber.GroupBy(row => PlayerKey(row.Pcode, row.TeamCode), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(row => row.PA), StringComparer.Ordinal),
            PitcherIp = pitcherValues.Count > 0
                ? pitcherValues.GroupBy(row => PlayerKey(row.Pcode, row.TeamCode), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Sum(row => row.InningsPitched ?? 0.0), StringComparer.Ordinal)
                : pitcherSaber.GroupBy(row => PlayerKey(row.Pcode, row.TeamCode), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Sum(row => row.InningsPitched ?? 0.0), StringComparer.Ordinal),
            PrimaryPositions = batterValues.GroupBy(row => PlayerKey(row.Pcode, row.TeamCode), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(row => row.EstimatedDefensiveInnings).First().PrimaryPosition ?? "-",
                    StringComparer.Ordinal),
        };
    }

    private static BatterSummaryGridRow BuildBatterClassic(BatterAggregateRecord row)
    {
        var avg = Divide(row.Hits, row.AtBats);
        var obp = Divide(row.Hits + row.Walks + row.HitByPitch,
            row.AtBats + row.Walks + row.HitByPitch + row.SacrificeFlies);
        var slg = Divide(row.TotalBases, row.AtBats);
        return new BatterSummaryGridRow
        {
            Pcode = Empty(row.Pcode), Name = Empty(row.Name), TeamCode = Empty(row.TeamCode), Games = row.Games,
            PA = row.PlateAppearances, AB = row.AtBats, Runs = row.Runs, H = row.Hits, Singles = row.Singles,
            Doubles = row.Doubles, Triples = row.Triples, HomeRuns = row.HomeRuns,
            RBI = row.RunsBattedIn, StolenBases = row.StolenBases, CaughtStealing = row.CaughtStealing,
            Walks = row.Walks,
            IntentionalWalks = row.IntentionalWalks, HitByPitch = row.HitByPitch, Strikeouts = row.Strikeouts,
            SacrificeFlies = row.SacrificeFlies, SacrificeBunts = row.SacrificeBunts,
            DoublePlays = row.DoublePlays, TotalBases = row.TotalBases,
            AVG = avg, OBP = obp, SLG = slg, OPS = obp.HasValue && slg.HasValue ? obp.Value + slg.Value : null,
            BABIP = Divide(row.Hits - row.HomeRuns,
                row.AtBats - row.Strikeouts - row.HomeRuns + row.SacrificeFlies),
            WalkRate = Divide(row.Walks, row.PlateAppearances),
            StrikeoutRate = Divide(row.Strikeouts, row.PlateAppearances),
            FlyBalls = row.FlyBalls, Wpa = row.Wpa, Pitches = row.Pitches,
            Swings = row.Swings, Contacts = row.Contacts, Whiffs = row.Whiffs,
            CalledStrikes = row.CalledStrikes, Csw = row.Csw,
            InZone = row.InZone, OutZone = row.OutZone,
            ZoneSwings = row.ZoneSwings, ChaseSwings = row.ChaseSwings,
            ZoneContacts = row.ZoneContacts, OutZoneContacts = row.OutZoneContacts,
            FirstPitches = row.FirstPitches, FirstPitchSwings = row.FirstPitchSwings,
        };
    }

    private static BatterSabermetricGridRow BuildBatterSaber(BatterAggregateRecord row, LeagueReference league)
    {
        var avg = Divide(row.Hits, row.AtBats);
        var slg = Divide(row.TotalBases, row.AtBats);
        var obp = Divide(row.Hits + row.Walks + row.HitByPitch,
            row.AtBats + row.Walks + row.HitByPitch + row.SacrificeFlies);
        var denominator = row.AtBats + row.Walks - row.IntentionalWalks + row.SacrificeFlies + row.HitByPitch;
        var woba = Divide(
            Wbb * (row.Walks - row.IntentionalWalks) +
            Whbp * row.HitByPitch +
            W1b * row.Singles +
            W2b * row.Doubles +
            W3b * row.Triples +
            Whr * row.HomeRuns,
            denominator);
        double? wraa = woba.HasValue
            ? (woba.Value - league.Woba) / WobaScale * row.PlateAppearances
            : null;
        double? wrc = wraa.HasValue
            ? wraa.Value + league.RunsPerPa * row.PlateAppearances
            : null;
        double? wrcPlus = wrc.HasValue && row.PlateAppearances > 0 && league.RunsPerPa > 0
            ? 100.0 * (wrc.Value / row.PlateAppearances) / league.RunsPerPa
            : null;
        double? opsPlus = obp.HasValue && slg.HasValue && league.Obp > 0 && league.Slg > 0
            ? 100.0 * (obp.Value / league.Obp + slg.Value / league.Slg - 1.0)
            : null;

        return new BatterSabermetricGridRow
        {
            Pcode = Empty(row.Pcode), Name = Empty(row.Name), TeamCode = Empty(row.TeamCode), Games = row.Games,
            PA = row.PlateAppearances,
            WalkRate = Divide(row.Walks, row.PlateAppearances),
            StrikeoutRate = Divide(row.Strikeouts, row.PlateAppearances),
            WalkToStrikeout = Divide(row.Walks, row.Strikeouts),
            Iso = avg.HasValue && slg.HasValue ? slg.Value - avg.Value : null,
            Babip = Divide(row.Hits - row.HomeRuns,
                row.AtBats - row.Strikeouts - row.HomeRuns + row.SacrificeFlies),
            Woba = woba, Wraa = wraa, Wrc = wrc, WrcPlus = wrcPlus, OpsPlus = opsPlus,
        };
    }

    private static PitcherSummaryGridRow BuildPitcherClassic(PitcherAggregateRecord row) => new()
    {
        Pcode = Empty(row.Pcode), Name = Empty(row.Name), TeamCode = Empty(row.TeamCode), Games = row.Games,
        BattersFaced = row.BattersFaced, PitchCount = row.Pitches,
        Hits = row.HitsFromPlateAppearances, HomeRuns = row.HomeRunsFromPlateAppearances,
        Walks = row.WalksFromPlateAppearances, HitBatters = row.HitBattersFromPlateAppearances,
        Strikeouts = row.StrikeoutsFromPlateAppearances, Swings = row.Swings, Whiffs = row.Whiffs,
        CswCount = row.Csw, AverageSpeed = row.SpeedCount > 0 ? row.SpeedSum / row.SpeedCount : null,
        StrikeoutRate = Divide(row.StrikeoutsFromPlateAppearances, row.BattersFaced),
        WalkRate = Divide(row.WalksFromPlateAppearances, row.BattersFaced),
        WhiffRate = Divide(row.Whiffs, row.Swings), CswRate = Divide(row.Csw, row.Pitches),
    };

    private static PitcherSabermetricGridRow BuildPitcherSaber(PitcherAggregateRecord row, LeagueReference league)
    {
        var innings = row.PlateAppearanceOuts / 3.0;
        // Official outs include non-PA outs; all per-nine rates use the matching official counts.
        var hasFinalLine = row.FinalGames > 0;
        var perNineInnings = hasFinalLine ? row.InningsOuts / 3.0 : innings;
        var fip = innings > 0
            ? (13.0 * row.HomeRunsFromPlateAppearances +
               3.0 * (row.WalksFromPlateAppearances + row.HitBattersFromPlateAppearances) -
               2.0 * row.StrikeoutsFromPlateAppearances) / innings + league.FipConstant
            : (double?)null;
        var expectedHomeRuns = row.FlyBalls * league.HrPerFlyBall;
        var xfip = innings > 0
            ? (13.0 * expectedHomeRuns +
               3.0 * (row.WalksFromPlateAppearances + row.HitBattersFromPlateAppearances) -
               2.0 * row.StrikeoutsFromPlateAppearances) / innings + league.FipConstant
            : (double?)null;
        var lobDenominator = row.HitsFromPlateAppearances + row.WalksFromPlateAppearances +
                             row.HitBattersFromPlateAppearances - 1.4 * row.HomeRunsFromPlateAppearances;
        var lob = lobDenominator > 0
            ? (row.HitsFromPlateAppearances + row.WalksFromPlateAppearances +
               row.HitBattersFromPlateAppearances - row.RunsFromPlateAppearances) / lobDenominator
            : (double?)null;

        return new PitcherSabermetricGridRow
        {
            Pcode = Empty(row.Pcode), Name = Empty(row.Name), TeamCode = Empty(row.TeamCode), Games = row.Games,
            BattersFaced = row.BattersFaced, InningsPitched = innings,
            StrikeoutRate = Divide(row.StrikeoutsFromPlateAppearances, row.BattersFaced),
            WalkRate = Divide(row.WalksFromPlateAppearances, row.BattersFaced),
            StrikeoutMinusWalkRate = Divide(
                row.StrikeoutsFromPlateAppearances - row.WalksFromPlateAppearances,
                row.BattersFaced),
            Babip = Divide(
                row.HitsFromPlateAppearances - row.HomeRunsFromPlateAppearances,
                row.BattersFaced - row.WalksFromPlateAppearances - row.HitBattersFromPlateAppearances -
                row.StrikeoutsFromPlateAppearances - row.HomeRunsFromPlateAppearances + row.SacrificeFlies),
            LobRate = lob,
            StrikeoutsPerNine = RatePerNine(hasFinalLine ? row.FinalStrikeouts : row.StrikeoutsFromPlateAppearances, perNineInnings),
            WalksPerNine = RatePerNine(hasFinalLine ? row.FinalWalks : row.WalksFromPlateAppearances, perNineInnings),
            HomeRunsPerNine = RatePerNine(hasFinalLine ? row.HomeRunsAllowed : row.HomeRunsFromPlateAppearances, perNineInnings),
            Fip = fip,
            FipMinus = fip.HasValue && league.Ra9 > 0 ? 100.0 * fip.Value / league.Ra9 : null,
            Xfip = xfip,
            XfipMinus = xfip.HasValue && league.Ra9 > 0 ? 100.0 * xfip.Value / league.Ra9 : null,
        };
    }

    private static PlateDisciplineGridRow BuildBatterDiscipline(BatterAggregateRecord row) =>
        BuildDiscipline(row.Pcode, row.Name, row.TeamCode, row.Pitches, row.Swings, row.Contacts,
            row.Whiffs, row.Csw, row.InZone, row.OutZone, row.ZoneSwings, row.ChaseSwings,
            row.ZoneContacts, row.OutZoneContacts, row.FirstPitches, row.FirstPitchSwings,
            row.PlateAppearances);

    private static PlateDisciplineGridRow BuildPitcherDiscipline(PitcherAggregateRecord row) =>
        BuildDiscipline(row.Pcode, row.Name, row.TeamCode, row.Pitches, row.Swings, row.Contacts,
            row.Whiffs, row.Csw, row.InZone, row.OutZone, row.ZoneSwings, row.ChaseSwings,
            row.ZoneContacts, row.OutZoneContacts, row.FirstPitches, row.FirstPitchSwings,
            row.BattersFaced);

    private static PlateDisciplineGridRow BuildDiscipline(
        string pcode, string name, string teamCode,
        int pitches, int swings, int contacts, int whiffs, int csw,
        int inZone, int outZone, int zoneSwings, int chaseSwings,
        int zoneContacts, int outZoneContacts, int firstPitches, int firstPitchSwings,
        int plateAppearances) => new()
    {
        Pcode = Empty(pcode), Name = Empty(name), TeamCode = Empty(teamCode), Pitches = pitches,
        SwingRate = Divide(swings, pitches), ContactRate = Divide(contacts, swings),
        WhiffRate = Divide(whiffs, swings), CswRate = Divide(csw, pitches),
        ZoneSwingRate = Divide(zoneSwings, inZone), ChaseRate = Divide(chaseSwings, outZone),
        ZoneContactRate = Divide(zoneContacts, zoneSwings),
        OutZoneContactRate = Divide(outZoneContacts, chaseSwings),
        SwingingStrikeRate = Divide(whiffs, pitches),
        FirstPitchSwingRate = Divide(firstPitchSwings, firstPitches),
        PitchesPerPa = plateAppearances > 0 ? (double)pitches / plateAppearances : null,
    };

    private static BatterValueGridRow BuildBatterValue(
        BatterAggregateRecord row,
        BatterSabermetricGridRow? saber,
        double replacementRunsPerPa)
    {
        const double runsPerWin = 10.0;
        var runningRuns = row.StolenBases * 0.20 - row.CaughtStealing * 0.40;
        var replacementRuns = row.PlateAppearances * replacementRunsPerPa;
        var battingRuns = saber?.Wraa ?? 0.0;
        var position = row.Position;
        var rar = battingRuns + runningRuns + position.Runs + replacementRuns;
        return new BatterValueGridRow
        {
            Pcode = Empty(row.Pcode), Name = Empty(row.Name), TeamCode = Empty(row.TeamCode), Games = row.Games,
            PA = row.PlateAppearances, BattingRuns = battingRuns, RunningRuns = runningRuns,
            FieldingRuns = 0.0, PrimaryPosition = position.PrimaryPosition,
            PositionRuns = position.Runs, PositionBasis = position.Basis,
            EstimatedDefensiveInnings = position.EstimatedDefensiveInnings,
            ReplacementRuns = replacementRuns, RunsAboveReplacement = rar,
            RunsPerWin = runsPerWin, War = rar / runsPerWin,
        };
    }

    private static PitcherValueGridRow BuildPitcherValue(PitcherAggregateRecord row, LeagueReference league, int? seasonYear, double pitcherWarPerInning)
    {
        var innings = row.InningsOuts / 3.0;
        var starterInnings = row.StarterInningsOuts / 3.0;
        var reliefInnings = row.ReliefInningsOuts / 3.0;
        var calibration = league.PitcherWar ?? new PitcherWarCalibration();

        double? ifFip = innings > 0
            ? (13.0 * row.HomeRunsAllowed +
               3.0 * (row.FinalWalks + row.FinalHitBatters) -
               2.0 * (row.FinalStrikeouts + row.InfieldFlies)) / innings + league.IfFipConstant
            : null;
        double? fipR9 = ifFip.HasValue
            ? ifFip.Value + league.Ra9Adjustment
            : null;

        // KBO fWAR v4 uses season-aware KBO PF v2. Career/multi-season views fall back
        // to the latest available v2 factor per stadium; season views use that exact year.
        var v2Rows = league.KboParkFactorsV2
            .Where(x => !string.IsNullOrWhiteSpace(x.Stadium))
            .ToList();
        var parkByStadium = v2Rows
            .GroupBy(x => x.Stadium, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    if (seasonYear.HasValue)
                    {
                        var exact = g.FirstOrDefault(x => x.Year == seasonYear.Value);
                        if (exact is not null) return exact.Factor;
                    }
                    return g.OrderByDescending(x => x.Year).First().Factor;
                },
                StringComparer.OrdinalIgnoreCase);
        var weightedParkNumerator = 0.0;
        foreach (var item in row.StadiumOuts)
        {
            var stadiumInnings = item.Value / 3.0;
            var factor = parkByStadium.TryGetValue(item.Key, out var storedFactor)
                ? storedFactor
                : 100.0;
            weightedParkNumerator += stadiumInnings * factor;
        }
        var parkFactor = innings > 0 && row.StadiumOuts.Count > 0
            ? weightedParkNumerator / innings
            : 100.0;
        double? adjustedFipR9 = fipR9.HasValue
            ? KboPitcherWarMath.ParkAdjust(fipR9.Value, parkFactor)
            : null;

        var games = Math.Max(1, row.FinalGames);
        var fipRunsPerWin = adjustedFipR9.HasValue
            ? KboPitcherWarMath.DynamicRunsPerWin(league.LeagueFipR9, adjustedFipR9.Value, innings, games)
            : Math.Max(1.0, (league.LeagueFipR9 + 2.0) * 1.5);
        var gmLi = row.EntryWpaCount > 0 && league.AverageAbsoluteWpa > 0
            ? Math.Clamp((row.EntryAbsoluteWpaSum / row.EntryWpaCount) / league.AverageAbsoluteWpa, 0.1, 5.0)
            : 1.0;
        var leverageMultiplier = KboPitcherWarMath.LeverageMultiplier(gmLi, true);

        var starterReplacementFipR9 = calibration.StarterReplacementFipR9 > 0
            ? calibration.StarterReplacementFipR9 : league.LeagueFipR9 * 1.20;
        var relieverReplacementFipR9 = calibration.RelieverReplacementFipR9 > 0
            ? calibration.RelieverReplacementFipR9 : league.LeagueFipR9 * 1.15;

        var starterQualityWins = adjustedFipR9.HasValue
            ? KboPitcherWarMath.WinsAboveAverage(
                league.LeagueFipR9, adjustedFipR9.Value, starterInnings, fipRunsPerWin)
            : 0.0;
        var relieverQualityWins = adjustedFipR9.HasValue
            ? KboPitcherWarMath.WinsAboveAverage(
                league.LeagueFipR9, adjustedFipR9.Value, reliefInnings, fipRunsPerWin, leverageMultiplier)
            : 0.0;
        var starterReplacementWins = KboPitcherWarMath.ReplacementWins(
            starterReplacementFipR9, league.LeagueFipR9, starterInnings, fipRunsPerWin);
        var relieverReplacementWins = KboPitcherWarMath.ReplacementWins(
            relieverReplacementFipR9, league.LeagueFipR9, reliefInnings, fipRunsPerWin, leverageMultiplier);
        var starterWarBeforeCorrection = starterQualityWins + starterReplacementWins;
        var relieverWarBeforeCorrection = relieverQualityWins + relieverReplacementWins;
        var fipWarBeforeCorrection = starterWarBeforeCorrection + relieverWarBeforeCorrection;
        var fipLeagueCorrection = pitcherWarPerInning * innings;
        var fipWar = fipWarBeforeCorrection + fipLeagueCorrection;

        double? rawRa9 = innings > 0 ? row.RunsAllowed * 9.0 / innings : null;
        double? parkAdjustedRa9 = rawRa9.HasValue
            ? KboPitcherWarMath.ParkAdjust(rawRa9.Value, parkFactor)
            : null;
        var ra9RunsPerWin = parkAdjustedRa9.HasValue
            ? KboPitcherWarMath.DynamicRunsPerWin(league.LeagueRa9, parkAdjustedRa9.Value, innings, games)
            : Math.Max(1.0, (league.LeagueRa9 + 2.0) * 1.5);
        var starterReplacementRa9 = calibration.StarterReplacementRa9 > league.LeagueRa9
            ? calibration.StarterReplacementRa9
            : league.LeagueRa9 + 0.12 * ra9RunsPerWin;
        var relieverReplacementRa9 = calibration.RelieverReplacementRa9 > league.LeagueRa9
            ? calibration.RelieverReplacementRa9
            : league.LeagueRa9 + 0.03 * ra9RunsPerWin;

        var starterRa9QualityWins = parkAdjustedRa9.HasValue
            ? KboPitcherWarMath.WinsAboveAverage(
                league.LeagueRa9, parkAdjustedRa9.Value, starterInnings, ra9RunsPerWin)
            : 0.0;
        var relieverRa9QualityWins = parkAdjustedRa9.HasValue
            ? KboPitcherWarMath.WinsAboveAverage(
                league.LeagueRa9, parkAdjustedRa9.Value, reliefInnings, ra9RunsPerWin, leverageMultiplier)
            : 0.0;
        var starterRa9ReplacementWins = KboPitcherWarMath.ReplacementWins(
            starterReplacementRa9, league.LeagueRa9, starterInnings, ra9RunsPerWin);
        var relieverRa9ReplacementWins = KboPitcherWarMath.ReplacementWins(
            relieverReplacementRa9, league.LeagueRa9, reliefInnings, ra9RunsPerWin, leverageMultiplier);
        var ra9WarBeforeCorrection = starterRa9QualityWins + relieverRa9QualityWins +
                                     starterRa9ReplacementWins + relieverRa9ReplacementWins;
        var ra9LeagueCorrection = calibration.Ra9WarPerInning * innings;
        var ra9War = ra9WarBeforeCorrection + ra9LeagueCorrection;

        var blendTotalWeight = calibration.BlendFipWeight + calibration.BlendRa9Weight;
        var blendWar = blendTotalWeight > 0
            ? (fipWar * calibration.BlendFipWeight + ra9War * calibration.BlendRa9Weight) / blendTotalWeight
            : fipWar;
        var weightedReplacementRa9 = innings > 0
            ? (starterReplacementRa9 * starterInnings + relieverReplacementRa9 * reliefInnings) / innings
            : league.LeagueRa9;

        return new PitcherValueGridRow
        {
            Pcode = Empty(row.Pcode), Name = Empty(row.Name), TeamCode = Empty(row.TeamCode),
            Games = row.DisplayTeamGames ?? row.FinalGames, GamesStarted = row.GamesStarted, ReliefGames = row.ReliefGames,
            InningsPitched = innings, StarterInnings = starterInnings, ReliefInnings = reliefInnings,
            RunsAllowed = row.RunsAllowed, EarnedRuns = row.OfficialTeamEarnedRuns ?? row.EarnedRuns,
            HomeRuns = row.HomeRunsAllowed, Walks = row.FinalWalks,
            HitBatters = row.FinalHitBatters, Strikeouts = row.FinalStrikeouts,
            InfieldFlies = row.InfieldFlies, WildPitches = row.WildPitches,
            IfFip = ifFip, FipR9 = fipR9, ParkFactor = parkFactor, ParkAdjustedFipR9 = adjustedFipR9,
            DynamicRunsPerWin = fipRunsPerWin,
            StarterReplacementFipMinus = calibration.StarterReplacementFipMinus,
            RelieverReplacementFipMinus = calibration.RelieverReplacementFipMinus,
            StarterReplacementWins = starterReplacementWins,
            RelieverReplacementWins = relieverReplacementWins,
            GmLi = gmLi, LeverageMultiplier = leverageMultiplier,
            StarterQualityWins = starterQualityWins, RelieverQualityWins = relieverQualityWins,
            StarterWarBeforeCorrection = starterWarBeforeCorrection,
            RelieverWarBeforeCorrection = relieverWarBeforeCorrection,
            WarBeforeCorrection = fipWarBeforeCorrection,
            WarPerInningCorrection = pitcherWarPerInning,
            LeagueCorrection = fipLeagueCorrection,
            War = fipWar,
            ParkAdjustedRa9 = parkAdjustedRa9, Ra9RunsPerWin = ra9RunsPerWin,
            Ra9WarBeforeCorrection = ra9WarBeforeCorrection,
            Ra9WarPerInningCorrection = calibration.Ra9WarPerInning,
            Ra9LeagueCorrection = ra9LeagueCorrection,
            Ra9War = ra9War, BlendWar = blendWar,
            RunsAboveReplacement = fipWar * fipRunsPerWin, RunsPerWin = fipRunsPerWin,
            Fip = ifFip, ReplacementRa9 = weightedReplacementRa9,
        };
    }

    private static string PlayerKey(string? pcode, string? team) =>
        $"{pcode ?? string.Empty}|{team ?? string.Empty}";

    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static double? Divide(double numerator, double denominator) => denominator > 0 ? numerator / denominator : null;
    private static double? RatePerNine(double numerator, double innings) => innings > 0 ? numerator * 9.0 / innings : null;
}
