using System.ComponentModel;

namespace NaverRelay.Application.Statistics;

public sealed class PitcherWarDiagnosticSeasonRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("리그 IP")] public double LeagueInnings { get; init; }
    [DisplayName("lgERA")] public double LeagueEra { get; init; }
    [DisplayName("lgRA9")] public double LeagueRa9 { get; init; }
    [DisplayName("lgFIPR9")] public double LeagueFipR9 { get; init; }

    [DisplayName("SP 전체 역할표본")] public int StarterRoleLines { get; init; }
    [DisplayName("SP 후보")] public int StarterCandidates { get; init; }
    [DisplayName("SP 후보 최대 IP")] public double? StarterPoolMaxInnings { get; init; }
    [DisplayName("SP 후보 IP")] public double StarterCandidateInnings { get; init; }
    [DisplayName("SP 관측 FIP- 평균")] public double? StarterObservedMeanFipMinus { get; init; }
    [DisplayName("SP 회귀 FIP- 평균")] public double? StarterRegressedMeanFipMinus { get; init; }
    [DisplayName("SP P25")] public double? StarterP25FipMinus { get; init; }
    [DisplayName("SP P50")] public double? StarterP50FipMinus { get; init; }
    [DisplayName("SP P75")] public double? StarterP75FipMinus { get; init; }
    [DisplayName("SP P90")] public double? StarterP90FipMinus { get; init; }
    [DisplayName("SP 3Y P75")] public double? StarterRolling3P75FipMinus { get; init; }
    [DisplayName("SP 5Y P75")] public double? StarterRolling5P75FipMinus { get; init; }
    [DisplayName("SP FG 하한 FIP-")] public double StarterFgFloorFipMinus { get; init; }
    [DisplayName("SP 진단 적용 FIP-")] public double StarterDiagnosticReplacementFipMinus { get; init; }

    [DisplayName("RP 전체 역할표본")] public int RelieverRoleLines { get; init; }
    [DisplayName("RP 후보")] public int RelieverCandidates { get; init; }
    [DisplayName("RP 후보 최대 IP")] public double? RelieverPoolMaxInnings { get; init; }
    [DisplayName("RP 후보 IP")] public double RelieverCandidateInnings { get; init; }
    [DisplayName("RP 관측 FIP- 평균")] public double? RelieverObservedMeanFipMinus { get; init; }
    [DisplayName("RP 회귀 FIP- 평균")] public double? RelieverRegressedMeanFipMinus { get; init; }
    [DisplayName("RP P25")] public double? RelieverP25FipMinus { get; init; }
    [DisplayName("RP P50")] public double? RelieverP50FipMinus { get; init; }
    [DisplayName("RP P75")] public double? RelieverP75FipMinus { get; init; }
    [DisplayName("RP P90")] public double? RelieverP90FipMinus { get; init; }
    [DisplayName("RP 3Y P75")] public double? RelieverRolling3P75FipMinus { get; init; }
    [DisplayName("RP 5Y P75")] public double? RelieverRolling5P75FipMinus { get; init; }
    [DisplayName("RP FG 하한 FIP-")] public double RelieverFgFloorFipMinus { get; init; }
    [DisplayName("RP 진단 적용 FIP-")] public double RelieverDiagnosticReplacementFipMinus { get; init; }

    [DisplayName("SP P75 RA9")] public double? StarterP75Ra9 { get; init; }
    [DisplayName("RP P75 RA9")] public double? RelieverP75Ra9 { get; init; }
    [DisplayName("SP 진단 Repl RA9")] public double StarterDiagnosticReplacementRa9 { get; init; }
    [DisplayName("RP 진단 Repl RA9")] public double RelieverDiagnosticReplacementRa9 { get; init; }

    [DisplayName("목표 투수 WAR")] public double TargetPitcherWar { get; init; }
    [DisplayName("보정 전 fWAR")] public double PreCorrectionFipWar { get; init; }
    [DisplayName("fWAR 달성률")] public double? FipWarTargetRatio { get; init; }
    [DisplayName("WARIP")] public double FipWarPerInning { get; init; }
    [DisplayName("보정 후 fWAR")] public double FinalFipWar { get; init; }
    [DisplayName("보정 전 RA9-WAR")] public double PreCorrectionRa9War { get; init; }
    [DisplayName("RA9 달성률")] public double? Ra9WarTargetRatio { get; init; }
    [DisplayName("RA9 WARIP")] public double Ra9WarPerInning { get; init; }
    [DisplayName("보정 후 RA9-WAR")] public double FinalRa9War { get; init; }

    [DisplayName("현재 v3 SP Repl FIP-")] public double CurrentV3StarterReplacementFipMinus { get; init; }
    [DisplayName("현재 v3 RP Repl FIP-")] public double CurrentV3RelieverReplacementFipMinus { get; init; }
}

public sealed class PitcherReplacementCandidateRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("역할")] public string Role { get; init; } = string.Empty;
    [DisplayName("선수 코드")] public string Pcode { get; init; } = string.Empty;
    [DisplayName("선수")] public string Name { get; init; } = string.Empty;
    [DisplayName("팀")] public string TeamCodes { get; init; } = string.Empty;
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("IP")] public double Innings { get; init; }
    [DisplayName("PF")] public double ParkFactor { get; init; }
    [DisplayName("pFIPR9")] public double ParkAdjustedFipR9 { get; init; }
    [DisplayName("관측 FIP-")] public double ObservedFipMinus { get; init; }
    [DisplayName("회귀 FIP-")] public double RegressedFipMinus { get; init; }
    [DisplayName("pRA9")] public double ParkAdjustedRa9 { get; init; }
    [DisplayName("회귀 RA9")] public double RegressedRa9 { get; init; }
    [DisplayName("gmLI")] public double GmLi { get; init; }
    [DisplayName("회귀 기준 IP")] public double RegressionInnings { get; init; }
}

public sealed class PitcherWarDiagnosticBundle
{
    public IReadOnlyList<PitcherWarDiagnosticSeasonRow> Seasons { get; init; } = Array.Empty<PitcherWarDiagnosticSeasonRow>();
    public IReadOnlyList<PitcherReplacementCandidateRow> Candidates { get; init; } = Array.Empty<PitcherReplacementCandidateRow>();
}
