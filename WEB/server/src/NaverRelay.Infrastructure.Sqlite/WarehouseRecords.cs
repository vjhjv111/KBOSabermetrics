using NaverRelay.Parsing;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

internal readonly record struct WarehousePlayerKey(string Pcode, string TeamCode);

internal sealed class WarehousePlayerObservation
{
    public string Pcode { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TeamCode { get; set; } = string.Empty;
    public string? BirthDate { get; set; }
    public string? Position { get; set; }
    public string? HitType { get; set; }
    public bool IsBatter { get; set; }
    public bool IsPitcher { get; set; }
    public int? BatOrder { get; set; }
    public int? LineupSequence { get; set; }
}

internal sealed class BatterGameAggregate
{
    public string GameId { get; init; } = string.Empty;
    public string Pcode { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TeamCode { get; init; } = string.Empty;

    public int PlateAppearances { get; set; }
    public int AtBats { get; set; }
    public int Hits { get; set; }
    public int Singles { get; set; }
    public int Doubles { get; set; }
    public int Triples { get; set; }
    public int HomeRuns { get; set; }
    public int Walks { get; set; }
    public int IntentionalWalks { get; set; }
    public int HitByPitch { get; set; }
    public int Strikeouts { get; set; }
    public int SacrificeFlies { get; set; }
    public int SacrificeBunts { get; set; }
    public int DoublePlays { get; set; }
    public int TotalBases { get; set; }
    public int Runs { get; set; }
    public int RunsBattedIn { get; set; }
    public int StolenBases { get; set; }
    public int CaughtStealing { get; set; }
    public int OutsRecorded { get; set; }
    public int RunsScoredOnPlays { get; set; }
    public int FlyBalls { get; set; }
    public double Wpa { get; set; }

    public int Pitches { get; set; }
    public int Swings { get; set; }
    public int Contacts { get; set; }
    public int Whiffs { get; set; }
    public int CalledStrikes { get; set; }
    public int Csw { get; set; }
    public int InZone { get; set; }
    public int OutZone { get; set; }
    public int ZoneSwings { get; set; }
    public int ChaseSwings { get; set; }
    public int ZoneContacts { get; set; }
    public int OutZoneContacts { get; set; }
    public int FirstPitches { get; set; }
    public int FirstPitchSwings { get; set; }

    public double CatcherInnings { get; set; }
    public double FirstBaseInnings { get; set; }
    public double SecondBaseInnings { get; set; }
    public double ThirdBaseInnings { get; set; }
    public double ShortstopInnings { get; set; }
    public double LeftFieldInnings { get; set; }
    public double CenterFieldInnings { get; set; }
    public double RightFieldInnings { get; set; }
    public int DesignatedHitterPlateAppearances { get; set; }

    public string PrimaryPosition => PositionSummary.PrimaryPosition;
    public double EstimatedDefensiveInnings => PositionSummary.EstimatedDefensiveInnings;
    public double PositionRuns => PositionSummary.Runs;
    public string PositionBasis => PositionSummary.Basis;

    private PositionAdjustmentSummary PositionSummary => WarehousePositionAdjustment.Calculate(
        CatcherInnings,
        FirstBaseInnings,
        SecondBaseInnings,
        ThirdBaseInnings,
        ShortstopInnings,
        LeftFieldInnings,
        CenterFieldInnings,
        RightFieldInnings,
        DesignatedHitterPlateAppearances);
}

internal sealed class PitcherGameAggregate
{
    public string GameId { get; init; } = string.Empty;
    public string Pcode { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TeamCode { get; init; } = string.Empty;

    // 타석/투구 이벤트 기반 집계
    public int BattersFaced { get; set; }
    public int PlateAppearanceOuts { get; set; }
    public int HitsFromPlateAppearances { get; set; }
    public int HomeRunsFromPlateAppearances { get; set; }
    public int WalksFromPlateAppearances { get; set; }
    public int HitBattersFromPlateAppearances { get; set; }
    public int StrikeoutsFromPlateAppearances { get; set; }
    public int SacrificeFlies { get; set; }
    public int RunsFromPlateAppearances { get; set; }
    public int FlyBalls { get; set; }
    public int InfieldFlies { get; set; }

    public int Pitches { get; set; }
    public int Swings { get; set; }
    public int Contacts { get; set; }
    public int Whiffs { get; set; }
    public int CalledStrikes { get; set; }
    public int Csw { get; set; }
    public int InZone { get; set; }
    public int OutZone { get; set; }
    public int ZoneSwings { get; set; }
    public int ChaseSwings { get; set; }
    public int ZoneContacts { get; set; }
    public int OutZoneContacts { get; set; }
    public int FirstPitches { get; set; }
    public int FirstPitchSwings { get; set; }
    public double SpeedSum { get; set; }
    public int SpeedCount { get; set; }

    // 경기별 투수 최종 기록 기반 집계
    public bool HasFinalLine { get; set; }
    public int AppearanceSequence { get; set; }
    public bool IsStarter { get; set; }
    public bool IsReliever { get; set; }
    public int InningsOuts { get; set; }
    public int HitsAllowed { get; set; }
    public int HomeRunsAllowed { get; set; }
    public int FinalWalks { get; set; }
    public int FinalHitBatters { get; set; }
    public int FinalStrikeouts { get; set; }
    public int RunsAllowed { get; set; }
    public int EarnedRuns { get; set; }
    public int WildPitches { get; set; }
    public int FinalPitchCount { get; set; }

    // 구원 등판 당시 원본 WPA 기반 gmLI 재료
    public double EntryAbsoluteWpaSum { get; set; }
    public int EntryWpaCount { get; set; }
}

internal sealed class WarehouseGameProjection
{
    public IReadOnlyList<WarehousePlayerObservation> Players { get; init; } = Array.Empty<WarehousePlayerObservation>();
    public IReadOnlyList<BatterGameAggregate> BatterGames { get; init; } = Array.Empty<BatterGameAggregate>();
    public IReadOnlyList<PitcherGameAggregate> PitcherGames { get; init; } = Array.Empty<PitcherGameAggregate>();
}

internal sealed class BatterAggregateRecord
{
    // Team execution context, derived from plate appearances for the team-batting view.
    public int DoublePlayOpportunities { get; set; }
    public int SacrificeBuntFailures { get; set; }
    public int? LeftOnBase { get; set; }
    public string Pcode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TeamCode { get; init; } = string.Empty;
    public int Games { get; init; }
    public int PlateAppearances { get; init; }
    public int AtBats { get; init; }
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
    public int RunsBattedIn { get; init; }
    public int StolenBases { get; init; }
    public int CaughtStealing { get; init; }
    public int OutsRecorded { get; init; }
    public int RunsScoredOnPlays { get; init; }
    public int FlyBalls { get; init; }
    public double Wpa { get; init; }
    public int Pitches { get; init; }
    public int Swings { get; init; }
    public int Contacts { get; init; }
    public int Whiffs { get; init; }
    public int CalledStrikes { get; init; }
    public int Csw { get; init; }
    public int InZone { get; init; }
    public int OutZone { get; init; }
    public int ZoneSwings { get; init; }
    public int ChaseSwings { get; init; }
    public int ZoneContacts { get; init; }
    public int OutZoneContacts { get; init; }
    public int FirstPitches { get; init; }
    public int FirstPitchSwings { get; init; }
    public double CatcherInnings { get; init; }
    public double FirstBaseInnings { get; init; }
    public double SecondBaseInnings { get; init; }
    public double ThirdBaseInnings { get; init; }
    public double ShortstopInnings { get; init; }
    public double LeftFieldInnings { get; init; }
    public double CenterFieldInnings { get; init; }
    public double RightFieldInnings { get; init; }
    public int DesignatedHitterPlateAppearances { get; init; }
    public Dictionary<string, int> StadiumPA { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PositionAdjustmentSummary Position => WarehousePositionAdjustment.Calculate(
        CatcherInnings,
        FirstBaseInnings,
        SecondBaseInnings,
        ThirdBaseInnings,
        ShortstopInnings,
        LeftFieldInnings,
        CenterFieldInnings,
        RightFieldInnings,
        DesignatedHitterPlateAppearances);
}

internal sealed record TeamBattingContext(
    int DoublePlayOpportunities,
    int SacrificeBuntFailures,
    int? LeftOnBase);

internal sealed class PitcherAggregateRecord
{
    // Display-only season team totals; individual inputs and WAR calibration remain unchanged.
    public int? OfficialTeamEarnedRuns { get; set; }
    public int? DisplayTeamGames { get; set; }
    public string Pcode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TeamCode { get; init; } = string.Empty;
    public int Games { get; init; }
    public int BattersFaced { get; init; }
    public int PlateAppearanceOuts { get; init; }
    public int HitsFromPlateAppearances { get; init; }
    public int HomeRunsFromPlateAppearances { get; init; }
    public int WalksFromPlateAppearances { get; init; }
    public int HitBattersFromPlateAppearances { get; init; }
    public int StrikeoutsFromPlateAppearances { get; init; }
    public int SacrificeFlies { get; init; }
    public int RunsFromPlateAppearances { get; init; }
    public int OpponentAtBats { get; init; }
    public int TotalBasesAllowed { get; init; }
    public int FlyBalls { get; init; }
    public int InfieldFlies { get; init; }
    public int Pitches { get; init; }
    public int Swings { get; init; }
    public int Contacts { get; init; }
    public int Whiffs { get; init; }
    public int CalledStrikes { get; init; }
    public int Csw { get; init; }
    public int InZone { get; init; }
    public int OutZone { get; init; }
    public int ZoneSwings { get; init; }
    public int ChaseSwings { get; init; }
    public int ZoneContacts { get; init; }
    public int OutZoneContacts { get; init; }
    public int FirstPitches { get; init; }
    public int FirstPitchSwings { get; init; }
    public double SpeedSum { get; init; }
    public int SpeedCount { get; init; }

    public int FinalGames { get; init; }
    public int GamesStarted { get; init; }
    public int ReliefGames { get; init; }
    public int CompleteGames { get; init; }
    public int Shutouts { get; init; }
    public int InningsOuts { get; init; }
    public int StarterInningsOuts { get; init; }
    public int ReliefInningsOuts { get; init; }
    public int HitsAllowed { get; init; }
    public int HomeRunsAllowed { get; init; }
    public int FinalWalks { get; init; }
    public int FinalHitBatters { get; init; }
    public int FinalStrikeouts { get; init; }
    public int RunsAllowed { get; init; }
    public int EarnedRuns { get; init; }
    public int WildPitches { get; init; }
    public int FinalPitchCount { get; init; }
    public double EntryAbsoluteWpaSum { get; init; }
    public int EntryWpaCount { get; init; }
    public Dictionary<string, int> StadiumOuts { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class WarehouseAnalyticsData
{
    public IReadOnlyList<BatterAggregateRecord> Batters { get; init; } = Array.Empty<BatterAggregateRecord>();
    public IReadOnlyList<PitcherAggregateRecord> Pitchers { get; init; } = Array.Empty<PitcherAggregateRecord>();
    public IReadOnlyDictionary<string, int> TeamGames { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
}

internal readonly record struct PositionAdjustmentSummary(
    double Runs,
    string Basis,
    double EstimatedDefensiveInnings,
    string PrimaryPosition);

internal static class WarehousePositionAdjustment
{
    // FanGraphs 방식 포지션당 보정치(풀타임 1458이닝=162경기×9이닝 기준, DH는 600PA 기준).
    // 리그 상수(연도별 상수) 화면에도 그대로 노출하므로 여기 값을 바꾸면 그 화면 표시도 함께 바뀝니다.
    internal const double FullSeasonInnings = 1458.0;
    internal const double DesignatedHitterFullSeasonPlateAppearances = 600.0;
    internal static readonly IReadOnlyDictionary<string, double> Rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["C"] = 12.5,
        ["SS"] = 7.5,
        ["2B"] = 2.5,
        ["3B"] = 2.5,
        ["CF"] = 2.5,
        ["LF"] = -7.5,
        ["RF"] = -7.5,
        ["1B"] = -12.5,
        ["DH"] = -17.5,
    };

    public static PositionAdjustmentSummary Calculate(
        double catcher,
        double firstBase,
        double secondBase,
        double thirdBase,
        double shortstop,
        double leftField,
        double centerField,
        double rightField,
        int designatedHitterPlateAppearances)
    {
        var innings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["C"] = catcher,
            ["1B"] = firstBase,
            ["2B"] = secondBase,
            ["3B"] = thirdBase,
            ["SS"] = shortstop,
            ["LF"] = leftField,
            ["CF"] = centerField,
            ["RF"] = rightField,
        };

        var runs = innings.Sum(item => item.Value / FullSeasonInnings * Rates[item.Key]);
        if (designatedHitterPlateAppearances > 0)
            runs += designatedHitterPlateAppearances / DesignatedHitterFullSeasonPlateAppearances * Rates["DH"];

        var positive = innings.Where(item => item.Value > 0.0001)
            .OrderByDescending(item => item.Value)
            .ToList();
        var basis = positive.Select(item => $"{item.Key} {item.Value:0.0}이닝").ToList();
        if (designatedHitterPlateAppearances > 0)
            basis.Add($"DH/PH {designatedHitterPlateAppearances}PA");

        var primary = positive.Count > 0
            ? positive[0].Key
            : designatedHitterPlateAppearances > 0 ? "DH" : "-";
        return new PositionAdjustmentSummary(
            runs,
            basis.Count > 0 ? string.Join(", ", basis) : "포지션 기록 없음",
            innings.Values.Sum(),
            primary);
    }
}
