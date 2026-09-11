using System.ComponentModel;

namespace NaverRelay.Application.Statistics;

public sealed class BatterReplacementCandidateRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("선수코드")] public string Pcode { get; init; } = string.Empty;
    [DisplayName("선수명")] public string Name { get; init; } = string.Empty;
    [DisplayName("팀")] public string TeamCode { get; init; } = string.Empty;
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PlateAppearances { get; init; }
    [DisplayName("wRC+")] public double? WrcPlus { get; init; }
    [DisplayName("회귀 wRC+")] public double? RegressedWrcPlus { get; init; }
    [DisplayName("wRAA/600PA")] public double? WraaPer600Pa { get; init; }
    [DisplayName("회귀 타격R/600")] public double? RegressedBattingRunsPer600 { get; init; }
    [DisplayName("주루R/600")] public double? RunningRunsPer600 { get; init; }
    [DisplayName("20-79")] public string In20To79 { get; init; } = string.Empty;
    [DisplayName("20-99")] public string In20To99 { get; init; } = string.Empty;
    [DisplayName("20-149")] public string In20To149 { get; init; } = string.Empty;
    [DisplayName("하위35%")] public string InBottom35 { get; init; } = string.Empty;
}

public sealed class ReplacementTeamDiagnosticRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("후보정의")] public string CandidatePolicy { get; init; } = string.Empty;
    [DisplayName("후보수")] public int CandidateCount { get; init; }
    [DisplayName("후보 PA")] public int CandidatePa { get; init; }
    [DisplayName("PA 상한")] public int PaUpperBound { get; init; }
    [DisplayName("후보 raw wRC+")] public double? RawWrcPlus { get; init; }
    [DisplayName("후보 회귀 wRC+")] public double? RegressedWrcPlus { get; init; }
    [DisplayName("회귀 타격R/600")] public double? RegressedBattingRunsPer600 { get; init; }
    [DisplayName("주루R/600")] public double? RunningRunsPer600 { get; init; }
    [DisplayName("리그 R/G")] public double LeagueRunsPerGame { get; init; }
    [DisplayName("대체팀 RS/G")] public double ReplacementRunsScoredPerGame { get; init; }
    [DisplayName("SP IP%")] public double StarterInningsShare { get; init; }
    [DisplayName("RP IP%")] public double RelieverInningsShare { get; init; }
    [DisplayName("대체투수 FIP-")] public double ReplacementPitchingFipMinus { get; init; }
    [DisplayName("대체팀 RA/G")] public double ReplacementRunsAllowedPerGame { get; init; }
    [DisplayName("PythagenPat exp")] public double PythagenPatExponent { get; init; }
    [DisplayName("추정 대체팀 승률")] public double EstimatedReplacementWinningPercentage { get; init; }
    [DisplayName("144G 예상승")] public double ExpectedWinsPer144 { get; init; }
    [DisplayName("144G 예상패")] public double ExpectedLossesPer144 { get; init; }
    [DisplayName("리그 총 WAR")] public double ImpliedLeagueWar { get; init; }

    // Runs-based cross-check: derives replacement WPct from runs below average,
    // rather than converting FIP- directly into RA/G.
    [DisplayName("타격 RBA/G")] public double BatterRunsBelowAveragePerGame { get; init; }
    [DisplayName("투구 RBA/G")] public double PitcherRunsBelowAveragePerGame { get; init; }
    [DisplayName("총 RBA/G")] public double TotalRunsBelowAveragePerGame { get; init; }
    [DisplayName("동적 RPW")] public double DynamicRunsPerWin { get; init; }
    [DisplayName("Runs기반 대체승률")] public double RunsBasedReplacementWinningPercentage { get; init; }
    [DisplayName("Runs기반 144G 승")] public double RunsBasedExpectedWinsPer144 { get; init; }
    [DisplayName("Runs기반 리그WAR")] public double RunsBasedImpliedLeagueWar { get; init; }
}

public sealed class BatterReplacementDiagnosticBundle
{
    public IReadOnlyList<ReplacementTeamDiagnosticRow> Summaries { get; init; } = Array.Empty<ReplacementTeamDiagnosticRow>();
    public IReadOnlyList<BatterReplacementCandidateRow> Candidates { get; init; } = Array.Empty<BatterReplacementCandidateRow>();
}
