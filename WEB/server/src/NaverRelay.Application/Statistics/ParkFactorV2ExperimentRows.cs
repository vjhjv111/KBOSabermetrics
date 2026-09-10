using System.ComponentModel;

namespace NaverRelay.Application.Statistics;

public sealed class ParkFactorV2StadiumRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("구장")] public string Stadium { get; init; } = string.Empty;
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("IP")] public double Innings { get; init; }
    [DisplayName("Legacy PF")] public double? LegacyParkFactor { get; init; }
    [DisplayName("당해 득점 PF")] public double? AnnualRunParkFactor { get; init; }
    [DisplayName("5Y Raw PF")] public double? RollingRawParkFactor { get; init; }
    [DisplayName("5Y 표본 G")] public double EffectiveGames { get; init; }
    [DisplayName("신뢰도")] public double Reliability { get; init; }
    [DisplayName("100 회귀 PF")] public double? RegressedParkFactor { get; init; }
    [DisplayName("Clamp 전 PF")] public double? PreNormalizedParkFactor { get; init; }
    [DisplayName("KBO PF v2")] public double? KboParkFactorV2 { get; init; }
    [DisplayName("v2-Legacy")] public double? V2MinusLegacy { get; init; }
}

public sealed class ParkFactorV2SeasonRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("구장 수")] public int Stadiums { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("Legacy PF 가중평균")] public double? LegacyWeightedMean { get; init; }
    [DisplayName("Raw PF 가중평균")] public double? RawWeightedMean { get; init; }
    [DisplayName("회귀 PF 가중평균")] public double? RegressedWeightedMean { get; init; }
    [DisplayName("KBO PF v2 가중평균")] public double? FinalWeightedMean { get; init; }
    [DisplayName("v2 최소")] public double? MinV2 { get; init; }
    [DisplayName("v2 중앙값")] public double? MedianV2 { get; init; }
    [DisplayName("v2 최대")] public double? MaxV2 { get; init; }
}

public sealed class PitcherWarParkFactorComparisonRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("리그 IP")] public double LeagueInnings { get; init; }
    [DisplayName("목표 투수 WAR")] public double TargetWar { get; init; }
    [DisplayName("Legacy PF 평균")] public double LegacyWeightedParkFactor { get; init; }
    [DisplayName("KBO PF v2 평균")] public double V2WeightedParkFactor { get; init; }
    [DisplayName("Legacy SP Repl FIP-")] public double LegacyStarterReplacementFipMinus { get; init; }
    [DisplayName("v2 SP Repl FIP-")] public double V2StarterReplacementFipMinus { get; init; }
    [DisplayName("Legacy RP Repl FIP-")] public double LegacyRelieverReplacementFipMinus { get; init; }
    [DisplayName("v2 RP Repl FIP-")] public double V2RelieverReplacementFipMinus { get; init; }
    [DisplayName("Legacy Pre-fWAR")] public double LegacyPreWar { get; init; }
    [DisplayName("v2 Pre-fWAR")] public double V2PreWar { get; init; }
    [DisplayName("Pre-fWAR 차이")] public double PreWarDelta { get; init; }
    [DisplayName("Legacy 달성률")] public double? LegacyTargetRatio { get; init; }
    [DisplayName("v2 달성률")] public double? V2TargetRatio { get; init; }
    [DisplayName("Legacy WARIP")] public double LegacyWarIp { get; init; }
    [DisplayName("v2 WARIP")] public double V2WarIp { get; init; }
    [DisplayName("WARIP 감소율")] public double? WarIpReductionPercent { get; init; }
    [DisplayName("180IP Legacy 보정")] public double LegacyCorrectionAt180Ip { get; init; }
    [DisplayName("180IP v2 보정")] public double V2CorrectionAt180Ip { get; init; }
}


public sealed class ParkFactorSensitivityRow
{
    [DisplayName("Prior G")] public int PriorGames { get; init; }
    [DisplayName("Clamp")] public string Clamp { get; init; } = string.Empty;
    [DisplayName("가중")] public string WeightPolicy { get; init; } = string.Empty;
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PF 평균")] public double WeightedParkFactor { get; init; }
    [DisplayName("PF 최소")] public double MinParkFactor { get; init; }
    [DisplayName("PF 최대")] public double MaxParkFactor { get; init; }
    [DisplayName("SP Repl FIP-")] public double StarterReplacementFipMinus { get; init; }
    [DisplayName("RP Repl FIP-")] public double RelieverReplacementFipMinus { get; init; }
    [DisplayName("목표 WAR")] public double TargetWar { get; init; }
    [DisplayName("Pre-fWAR")] public double PreWar { get; init; }
    [DisplayName("달성률")] public double? TargetRatio { get; init; }
    [DisplayName("WARIP")] public double WarIp { get; init; }
    [DisplayName("180IP 보정")] public double CorrectionAt180Ip { get; init; }
}

public sealed class ParkFactorSensitivitySummaryRow
{
    [DisplayName("Prior G")] public int PriorGames { get; init; }
    [DisplayName("Clamp")] public string Clamp { get; init; } = string.Empty;
    [DisplayName("가중")] public string WeightPolicy { get; init; } = string.Empty;
    [DisplayName("완료시즌 수")] public int CompletedSeasons { get; init; }
    [DisplayName("평균 달성률")] public double? MeanTargetRatio { get; init; }
    [DisplayName("최저 달성률")] public double? MinTargetRatio { get; init; }
    [DisplayName("최고 달성률")] public double? MaxTargetRatio { get; init; }
    [DisplayName("평균 |WARIP|")] public double? MeanAbsoluteWarIp { get; init; }
    [DisplayName("최대 |WARIP|")] public double? MaxAbsoluteWarIp { get; init; }
    [DisplayName("평균 SP Repl FIP-")] public double? MeanStarterReplacementFipMinus { get; init; }
    [DisplayName("평균 RP Repl FIP-")] public double? MeanRelieverReplacementFipMinus { get; init; }
    [DisplayName("PF 최소")] public double? MinParkFactor { get; init; }
    [DisplayName("PF 최대")] public double? MaxParkFactor { get; init; }
}

public sealed class ParkFactorSensitivityBundle
{
    public IReadOnlyList<ParkFactorSensitivitySummaryRow> Summaries { get; init; } = Array.Empty<ParkFactorSensitivitySummaryRow>();
    public IReadOnlyList<ParkFactorSensitivityRow> Details { get; init; } = Array.Empty<ParkFactorSensitivityRow>();
}

public sealed class ParkFactorV2ExperimentBundle
{
    public IReadOnlyList<ParkFactorV2SeasonRow> Seasons { get; init; } = Array.Empty<ParkFactorV2SeasonRow>();
    public IReadOnlyList<ParkFactorV2StadiumRow> Stadiums { get; init; } = Array.Empty<ParkFactorV2StadiumRow>();
    public IReadOnlyList<PitcherWarParkFactorComparisonRow> WarComparisons { get; init; } = Array.Empty<PitcherWarParkFactorComparisonRow>();
}
