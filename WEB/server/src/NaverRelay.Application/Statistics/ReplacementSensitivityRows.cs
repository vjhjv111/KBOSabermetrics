using System.ComponentModel;

namespace NaverRelay.Application.Statistics;

public sealed class ReplacementSensitivitySeasonRow
{
    [DisplayName("SP Repl FIP-")] public double StarterReplacementFipMinus { get; init; }
    [DisplayName("RP Repl FIP-")] public double RelieverReplacementFipMinus { get; init; }
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("리그 IP")] public double LeagueInnings { get; init; }
    [DisplayName("SP IP")] public double StarterInnings { get; init; }
    [DisplayName("RP IP")] public double RelieverInnings { get; init; }
    [DisplayName("목표 WAR")] public double TargetWar { get; init; }
    [DisplayName("SP Pre-WAR")] public double StarterPreWar { get; init; }
    [DisplayName("RP Pre-WAR")] public double RelieverPreWar { get; init; }
    [DisplayName("전체 Pre-WAR")] public double TotalPreWar { get; init; }
    [DisplayName("달성률")] public double? TargetRatio { get; init; }
    [DisplayName("WARIP")] public double WarIp { get; init; }
    [DisplayName("SP Final WAR")] public double StarterFinalWar { get; init; }
    [DisplayName("RP Final WAR")] public double RelieverFinalWar { get; init; }
    [DisplayName("SP WAR 비중")] public double? StarterWarShare { get; init; }
    [DisplayName("RP WAR 비중")] public double? RelieverWarShare { get; init; }
    [DisplayName("180IP 보정")] public double CorrectionAt180Ip { get; init; }
    [DisplayName("Top SP WAR")] public double? TopStarterWar { get; init; }
    [DisplayName("Top RP WAR")] public double? TopRelieverWar { get; init; }
    [DisplayName("WAR<=0 SP")] public int NonPositiveStarterCount { get; init; }
    [DisplayName("WAR<=0 RP")] public int NonPositiveRelieverCount { get; init; }
}

public sealed class ReplacementSensitivitySummaryRow
{
    [DisplayName("SP Repl FIP-")] public double StarterReplacementFipMinus { get; init; }
    [DisplayName("RP Repl FIP-")] public double RelieverReplacementFipMinus { get; init; }
    [DisplayName("완료시즌")] public int CompletedSeasons { get; init; }
    [DisplayName("평균 달성률")] public double? MeanTargetRatio { get; init; }
    [DisplayName("평균 |WARIP|")] public double? MeanAbsoluteWarIp { get; init; }
    [DisplayName("최대 |WARIP|")] public double? MaxAbsoluteWarIp { get; init; }
    [DisplayName("평균 SP WAR 비중")] public double? MeanStarterWarShare { get; init; }
    [DisplayName("평균 RP WAR 비중")] public double? MeanRelieverWarShare { get; init; }
    [DisplayName("평균 SP Final WAR")] public double? MeanStarterFinalWar { get; init; }
    [DisplayName("평균 RP Final WAR")] public double? MeanRelieverFinalWar { get; init; }
    [DisplayName("평균 Top SP WAR")] public double? MeanTopStarterWar { get; init; }
    [DisplayName("평균 Top RP WAR")] public double? MeanTopRelieverWar { get; init; }
    [DisplayName("평균 WAR<=0 SP")] public double? MeanNonPositiveStarterCount { get; init; }
    [DisplayName("평균 WAR<=0 RP")] public double? MeanNonPositiveRelieverCount { get; init; }
}

public sealed class ReplacementSensitivityBundle
{
    public IReadOnlyList<ReplacementSensitivitySummaryRow> Summaries { get; init; } = Array.Empty<ReplacementSensitivitySummaryRow>();
    public IReadOnlyList<ReplacementSensitivitySeasonRow> Details { get; init; } = Array.Empty<ReplacementSensitivitySeasonRow>();
}
