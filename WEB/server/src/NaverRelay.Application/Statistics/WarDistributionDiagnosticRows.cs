using System.ComponentModel;

namespace NaverRelay.Application.Statistics;

public sealed class WarDistributionSummaryRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("Role")] public string Role { get; init; } = string.Empty;
    [DisplayName("선수 수")] public int PlayerCount { get; init; }
    [DisplayName("WAR 합")] public double TotalWar { get; init; }
    [DisplayName("WAR 평균")] public double MeanWar { get; init; }
    [DisplayName("WAR 중앙값")] public double MedianWar { get; init; }
    [DisplayName("WAR P25")] public double P25War { get; init; }
    [DisplayName("WAR P75")] public double P75War { get; init; }
    [DisplayName("WAR P90")] public double P90War { get; init; }
    [DisplayName("WAR>=5")] public int WarAtLeast5 { get; init; }
    [DisplayName("WAR>=4")] public int WarAtLeast4 { get; init; }
    [DisplayName("WAR>=3")] public int WarAtLeast3 { get; init; }
    [DisplayName("WAR>=2")] public int WarAtLeast2 { get; init; }
    [DisplayName("WAR>=1")] public int WarAtLeast1 { get; init; }
    [DisplayName("WAR 0~1")] public int WarZeroToOne { get; init; }
    [DisplayName("WAR<0")] public int WarBelowZero { get; init; }
    [DisplayName("후보 수")] public int CandidateCount { get; init; }
    [DisplayName("후보 WAR 평균")] public double? CandidateMeanWar { get; init; }
    [DisplayName("후보 WAR 중앙값")] public double? CandidateMedianWar { get; init; }
    [DisplayName("후보 WAR P25")] public double? CandidateP25War { get; init; }
    [DisplayName("후보 WAR P75")] public double? CandidateP75War { get; init; }
    [DisplayName("후보 |WAR|<=0.25 %")] public double? CandidateWithin025 { get; init; }
    [DisplayName("후보 |WAR|<=0.50 %")] public double? CandidateWithin050 { get; init; }
    [DisplayName("후보 WAR<0 %")] public double? CandidateBelowZeroPercent { get; init; }
    [DisplayName("Top WAR")] public double? TopWar { get; init; }
    [DisplayName("SP Repl FIP-")] public double StarterReplacementFipMinus { get; init; } = 120.0;
    [DisplayName("RP Repl FIP-")] public double RelieverReplacementFipMinus { get; init; } = 115.0;
    [DisplayName("WARIP")] public double WarIp { get; init; }
}

public sealed class WarDistributionPlayerRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("Role")] public string Role { get; init; } = string.Empty;
    [DisplayName("후보")] public string Candidate { get; init; } = string.Empty;
    [DisplayName("선수코드")] public string PlayerCode { get; init; } = string.Empty;
    [DisplayName("선수명")] public string Name { get; init; } = string.Empty;
    [DisplayName("팀")] public string Teams { get; init; } = string.Empty;
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("IP")] public double Innings { get; init; }
    [DisplayName("PF v2")] public double ParkFactor { get; init; }
    [DisplayName("pFIPR9")] public double ParkAdjustedFipR9 { get; init; }
    [DisplayName("gmLI")] public double GmLi { get; init; }
    [DisplayName("Pre-WAR")] public double PreWar { get; init; }
    [DisplayName("WARIP 보정")] public double WarIpCorrection { get; init; }
    [DisplayName("Final WAR")] public double FinalWar { get; init; }
    [DisplayName("WAR 구간")] public string WarBand { get; init; } = string.Empty;
}

public sealed class WarDistributionDiagnosticBundle
{
    public IReadOnlyList<WarDistributionSummaryRow> Summaries { get; init; } = Array.Empty<WarDistributionSummaryRow>();
    public IReadOnlyList<WarDistributionPlayerRow> Players { get; init; } = Array.Empty<WarDistributionPlayerRow>();
}
