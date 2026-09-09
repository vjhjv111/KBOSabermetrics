using NaverRelay.Application.Statistics;
using NaverRelay.Parsing;

namespace NaverRelay.Application.Queries;

[Flags]
public enum GameDataProjection
{
    None = 0,
    Metadata = 1 << 0,
    Summary = 1 << 1,
    RelayGroups = 1 << 2,
    Events = 1 << 3,
    PlateAppearances = 1 << 4,
    PitchEvents = 1 << 5,
    RunnerEvents = 1 << 6,
    PlayerChanges = 1 << 7,
    AdministrativeEvents = 1 << 8,
    BattingLines = 1 << 9,
    PitchingLines = 1 << 10,
    Diagnostics = 1 << 11,

    GameGrid = Metadata | Summary,
    BatterClassic = Metadata | Summary | PlateAppearances,
    PitcherClassic = Metadata | Summary | PlateAppearances | PitchEvents,
    BatterAnalytics = Metadata | Summary | PlateAppearances | RunnerEvents | BattingLines | PlayerChanges,
    PitcherAnalytics = Metadata | Summary | RelayGroups | PlateAppearances | PitchEvents | PlayerChanges | PitchingLines,
    Discipline = Metadata | Summary | PlateAppearances | PitchEvents,
    PlayerPage = Metadata | Summary | RelayGroups | PlateAppearances | PitchEvents | RunnerEvents | PlayerChanges | BattingLines | PitchingLines,
    Full = Metadata | Summary | RelayGroups | Events | PlateAppearances | PitchEvents | RunnerEvents |
           PlayerChanges | AdministrativeEvents | BattingLines | PitchingLines | Diagnostics,
}

public enum AnalyticsGrouping
{
    PlayerByTeam = 0,
    PlayerCareer,
    Team,
}

public sealed record GameQuery
{
    public AnalyticsGrouping Grouping { get; init; } = AnalyticsGrouping.PlayerByTeam;
    public int? SeasonYear { get; init; }
    public string Competition { get; init; } = "정규시즌";
    public string? TeamCode { get; init; }
    public string? OpponentCode { get; init; }
    public string? Venue { get; init; }
    public string? Stadium { get; init; }
    public string? Weekday { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public int? RecentGameCount { get; init; }

    // 타석/투구 단위 상황 필터. 값이 있으면 BatterGameStats/PitcherGameStats의 시즌 합계를
    // 그대로 쓰지 않고 PlateAppearances/Pitches에서 조건에 맞는 이벤트를 다시 집계합니다.
    public string? InningFilter { get; init; }
    public int? OutsBefore { get; init; }
    public string? RunnerState { get; init; }
    public string? ScoreSituation { get; init; }
    public int? BallsBefore { get; init; }
    public int? StrikesBefore { get; init; }
    public int? BatOrder { get; init; }

    public bool HasSituationFilters =>
        !string.IsNullOrWhiteSpace(InningFilter) || OutsBefore.HasValue ||
        !string.IsNullOrWhiteSpace(RunnerState) || !string.IsNullOrWhiteSpace(ScoreSituation) ||
        BallsBefore.HasValue || StrikesBefore.HasValue || BatOrder.HasValue;

    public string CacheKey => string.Join('|',
        Grouping,
        SeasonYear?.ToString() ?? "ALL",
        Competition,
        TeamCode ?? "ALL",
        OpponentCode ?? "ALL",
        Venue ?? "ALL",
        Stadium ?? "ALL",
        Weekday ?? "ALL",
        StartDate?.ToString("yyyy-MM-dd") ?? "MIN",
        EndDate?.ToString("yyyy-MM-dd") ?? "MAX",
        RecentGameCount?.ToString() ?? "ALL",
        InningFilter ?? "ALL",
        OutsBefore?.ToString() ?? "ALL",
        RunnerState ?? "ALL",
        ScoreSituation ?? "ALL",
        BallsBefore?.ToString() ?? "ALL",
        StrikesBefore?.ToString() ?? "ALL",
        BatOrder?.ToString() ?? "ALL");
}

public sealed record DatabaseCatalog
{
    public int GameCount { get; init; }
    public DateTime? MinGameDate { get; init; }
    public DateTime? MaxGameDate { get; init; }
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Teams { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Stadiums { get; init; } = Array.Empty<string>();
}

public sealed record DatabaseFilterOptions
{
    public IReadOnlyList<string> Teams { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Stadiums { get; init; } = Array.Empty<string>();
    public DateTime? MinGameDate { get; init; }
    public DateTime? MaxGameDate { get; init; }
}

public sealed record DatabaseSummary
{
    public int Games { get; init; }
    public long PlateAppearances { get; init; }
    public long Pitches { get; init; }
    public long RunnerEvents { get; init; }
    public long PlayerChanges { get; init; }
    public long AdministrativeEvents { get; init; }
    public long Warnings { get; init; }
    public long Errors { get; init; }
}

public sealed record DatabaseGameHeader
{
    public string GameId { get; init; } = string.Empty;
    public int? SeasonYear { get; init; }
    public string? GameDate { get; init; }
    public string? RoundCode { get; init; }
    public GameCompetitionType CompetitionType { get; init; } = GameCompetitionType.Other;
    public string? Stadium { get; init; }
    public string? HomeTeamCode { get; init; }
    public string? AwayTeamCode { get; init; }
    public string? HomeTeamName { get; init; }
    public string? AwayTeamName { get; init; }
    public int? HomeScore { get; init; }
    public int? AwayScore { get; init; }
    public int PlateAppearances { get; init; }
    public int Pitches { get; init; }
    public int RunnerEvents { get; init; }
    public int PlayerChanges { get; init; }
    public int AdministrativeEvents { get; init; }
    public int Warnings { get; init; }
    public int Errors { get; init; }
    public int Diagnostics => Warnings + Errors;
}


public enum RawDataKind
{
    PlateAppearances,
    Pitches,
    RunnerEvents,
    PlayerChanges,
    AdministrativeEvents,
    Diagnostics,
}

public sealed record RawPageDescriptor(
    RawDataKind Kind,
    long TotalRows,
    int PageIndex,
    int PageSize,
    long LeadingRowsToSkip,
    IReadOnlyList<string> GameIds)
{
    public int PageCount => TotalRows <= 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)Math.Max(1, PageSize));
}

public sealed record DatabaseIndexProgress(int Current, int Total, string Message);
public sealed record DatabaseLoadProgress(int Current, int Total, string Message);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int PageIndex, int PageSize)
{
    public int PageCount => TotalCount <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize));
}

public sealed class PitcherWarCalibration
{
    public double ReplacementWinningPercentage { get; set; } = 0.294;
    public double PitcherWarShare { get; set; } = 0.43;
    public double TargetPitcherWar { get; set; }
    public double PreCorrectionFipWar { get; set; }
    public double FipWarPerInning { get; set; }
    public double PreCorrectionRa9War { get; set; }
    public double Ra9WarPerInning { get; set; }

    public double EmpiricalStarterReplacementFipR9 { get; set; }
    public double EmpiricalRelieverReplacementFipR9 { get; set; }
    public double StarterReplacementFipR9 { get; set; }
    public double RelieverReplacementFipR9 { get; set; }
    public double StarterReplacementFipMinus { get; set; }
    public double RelieverReplacementFipMinus { get; set; }

    public double EmpiricalStarterReplacementRa9 { get; set; }
    public double EmpiricalRelieverReplacementRa9 { get; set; }
    public double StarterReplacementRa9 { get; set; }
    public double RelieverReplacementRa9 { get; set; }

    public string ReplacementSampleSeasons { get; set; } = "-";
    public int StarterSamplePitchers { get; set; }
    public int RelieverSamplePitchers { get; set; }
    public double StarterSampleInnings { get; set; }
    public double RelieverSampleInnings { get; set; }
    public double TotalPitchingInnings { get; set; }

    public double BlendFipWeight { get; set; } = 0.70;
    public double BlendRa9Weight { get; set; } = 0.30;
}

/// <summary>
/// 전체 roundCode=kbo_r 데이터에서 한 번만 계산해 SQLite 계산 캐시에 저장하는 기준값입니다.
/// 화면의 연도/팀/기간 필터와 분리되어 wRC+, FIP, 투수 WAR, 파크 팩터가 같은 기준을 사용합니다.
/// </summary>
public sealed class LeagueReference
{
    public int GameCount { get; set; }
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
    public int Outs { get; set; }
    public int Runs { get; set; }
    public int FlyBalls { get; set; }
    public double Woba { get; set; }
    public double Obp { get; set; }
    public double Slg { get; set; }
    public double Ops { get; set; }
    public double RunsPerPa { get; set; }
    public double Ra9 { get; set; }
    public double FipConstant { get; set; }
    public double HrPerFlyBall { get; set; }

    public double PitchingInnings { get; set; }
    public int EarnedRuns { get; set; }
    public int RunsAllowed { get; set; }
    public int PitchingHomeRuns { get; set; }
    public int PitchingWalks { get; set; }
    public int PitchingHitBatters { get; set; }
    public int PitchingStrikeouts { get; set; }
    public int InfieldFlies { get; set; }
    public double LeagueEra { get; set; }
    public double LeagueRa9 { get; set; }
    public double IfFipConstant { get; set; }
    public double Ra9Adjustment { get; set; }
    public double LeagueFipR9 { get; set; }
    public double AverageAbsoluteWpa { get; set; }

    public PitcherWarCalibration PitcherWar { get; set; } = new();
    public List<ParkFactorGridRow> ParkFactors { get; set; } = new();
    public List<LeagueConstantGridRow> Constants { get; set; } = new();
}

public sealed class AnalyticsSnapshot
{
    public List<BatterSummaryGridRow> BatterClassic { get; set; } = new();
    public List<PitcherSummaryGridRow> PitcherClassic { get; set; } = new();
    public List<BatterSabermetricGridRow> BatterSabermetrics { get; set; } = new();
    public List<PitcherSabermetricGridRow> PitcherSabermetrics { get; set; } = new();
    public List<PlateDisciplineGridRow> BatterDiscipline { get; set; } = new();
    public List<PlateDisciplineGridRow> PitcherDiscipline { get; set; } = new();
    public List<BatterValueGridRow> BatterValues { get; set; } = new();
    public List<PitcherValueGridRow> PitcherValues { get; set; } = new();
    public Dictionary<string, int> TeamGames { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> BatterPa { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> PitcherIp { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> PrimaryPositions { get; set; } = new(StringComparer.Ordinal);
}
