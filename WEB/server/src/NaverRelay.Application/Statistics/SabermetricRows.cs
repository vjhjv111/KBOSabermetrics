using System.ComponentModel;

namespace NaverRelay.Application.Statistics;

public sealed class BatterSabermetricGridRow
{
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("선수")] public string? Name { get; init; }
    [DisplayName("팀")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("BB%")] public double? WalkRate { get; init; }
    [DisplayName("K%")] public double? StrikeoutRate { get; init; }
    [DisplayName("BB/K")] public double? WalkToStrikeout { get; init; }
    [DisplayName("ISO")] public double? Iso { get; init; }
    [DisplayName("BABIP")] public double? Babip { get; init; }
    [DisplayName("wOBA*")] public double? Woba { get; init; }
    [DisplayName("wRAA*")] public double? Wraa { get; init; }
    [DisplayName("wRC*")] public double? Wrc { get; init; }
    [DisplayName("wRC+*")] public double? WrcPlus { get; init; }
    [DisplayName("PF")] public double? ParkFactor { get; init; }
    [DisplayName("wRC+(파크)*")] public double? WrcPlusParkAdjusted { get; init; }
    [DisplayName("OPS+")] public double? OpsPlus { get; init; }
}

public sealed class PitcherSabermetricGridRow
{
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("투수")] public string? Name { get; init; }
    [DisplayName("팀")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("TBF")] public int BattersFaced { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("K%")] public double? StrikeoutRate { get; init; }
    [DisplayName("BB%")] public double? WalkRate { get; init; }
    [DisplayName("K-BB%")] public double? StrikeoutMinusWalkRate { get; init; }
    [DisplayName("BABIP")] public double? Babip { get; init; }
    [DisplayName("LOB%*")] public double? LobRate { get; init; }
    [DisplayName("K/9")] public double? StrikeoutsPerNine { get; init; }
    [DisplayName("BB/9")] public double? WalksPerNine { get; init; }
    [DisplayName("HR/9")] public double? HomeRunsPerNine { get; init; }
    [DisplayName("FIP*")] public double? Fip { get; init; }
    [DisplayName("FIP-*")] public double? FipMinus { get; init; }
    [DisplayName("xFIP*")] public double? Xfip { get; init; }
    [DisplayName("xFIP-*")] public double? XfipMinus { get; init; }
}

public sealed class PlateDisciplineGridRow
{
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("선수")] public string? Name { get; init; }
    [DisplayName("팀")] public string? TeamCode { get; init; }
    [DisplayName("투구 수")] public int Pitches { get; init; }
    [DisplayName("Swing%")] public double? SwingRate { get; init; }
    [DisplayName("Contact%")] public double? ContactRate { get; init; }
    [DisplayName("Whiff%")] public double? WhiffRate { get; init; }
    [DisplayName("CSW%")] public double? CswRate { get; init; }
    [DisplayName("Z-Swing%")] public double? ZoneSwingRate { get; init; }
    [DisplayName("O-Swing%")] public double? ChaseRate { get; init; }
    [DisplayName("Z-Contact%")] public double? ZoneContactRate { get; init; }
    [DisplayName("O-Contact%")] public double? OutZoneContactRate { get; init; }
    [DisplayName("SwStr%")] public double? SwingingStrikeRate { get; init; }
    [DisplayName("1st Swing%")] public double? FirstPitchSwingRate { get; init; }
    [DisplayName("P/PA")] public double? PitchesPerPa { get; init; }
}

public sealed class LeagueConstantGridRow
{
    [DisplayName("항목")] public string Metric { get; init; } = string.Empty;
    [DisplayName("값")] public double? Value { get; init; }
    [DisplayName("설명")] public string Description { get; init; } = string.Empty;
}


public sealed class BatterValueGridRow
{
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("선수")] public string? Name { get; init; }
    [DisplayName("팀")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("타격 Runs*")] public double? BattingRuns { get; init; }
    [DisplayName("주루 Runs*")] public double? RunningRuns { get; init; }
    [DisplayName("수비 Runs*")] public double? FieldingRuns { get; init; }
    [DisplayName("주 포지션*")] public string? PrimaryPosition { get; init; }
    [DisplayName("포지션 보정(FG)*")] public double? PositionRuns { get; init; }
    [DisplayName("포지션 산정 근거*")] public string? PositionBasis { get; init; }
    [DisplayName("추정 수비이닝*")] public double? EstimatedDefensiveInnings { get; init; }
    [DisplayName("대체선수 Runs*")] public double? ReplacementRuns { get; init; }
    [DisplayName("RAR*")] public double? RunsAboveReplacement { get; init; }
    [DisplayName("RPW*")] public double? RunsPerWin { get; init; }
    [DisplayName("Site WAR v1*")] public double? War { get; init; }
}

public sealed class PitcherValueGridRow
{
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("투수")] public string? Name { get; init; }
    [DisplayName("팀")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("GS")] public int GamesStarted { get; init; }
    [DisplayName("구원 G")] public int ReliefGames { get; init; }
    [Browsable(false)] public int CompleteGames { get; init; }
    [Browsable(false)] public int Shutouts { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("선발 IP")] public double? StarterInnings { get; init; }
    [DisplayName("구원 IP")] public double? ReliefInnings { get; init; }
    [DisplayName("R")] public int RunsAllowed { get; init; }
    [DisplayName("ER")] public int EarnedRuns { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitBatters { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("IFFB")] public int InfieldFlies { get; init; }
    [DisplayName("WP")] public int WildPitches { get; init; }
    [DisplayName("ifFIP")] public double? IfFip { get; init; }
    [DisplayName("FIPR9")] public double? FipR9 { get; init; }
    [DisplayName("FIP PF")] public double? ParkFactor { get; init; }
    [DisplayName("pFIPR9")] public double? ParkAdjustedFipR9 { get; init; }
    [DisplayName("dRPW")] public double? DynamicRunsPerWin { get; init; }
    [DisplayName("KBO 선발 Repl FIP-")] public double? StarterReplacementFipMinus { get; init; }
    [DisplayName("KBO 구원 Repl FIP-")] public double? RelieverReplacementFipMinus { get; init; }
    [DisplayName("선발 대체승")] public double? StarterReplacementWins { get; init; }
    [DisplayName("구원 대체승")] public double? RelieverReplacementWins { get; init; }
    [DisplayName("gmLI")] public double? GmLi { get; init; }
    [DisplayName("LI 배수")] public double? LeverageMultiplier { get; init; }
    [DisplayName("보정 전 fWAR")] public double? WarBeforeCorrection { get; init; }
    [DisplayName("KBO WARIP")] public double? WarPerInningCorrection { get; init; }
    [DisplayName("WARIP 보정")] public double? LeagueCorrection { get; init; }
    [DisplayName("KBO fWAR v4")] public double? War { get; init; }
    [DisplayName("pRA9*")] public double? ParkAdjustedRa9 { get; init; }
    [DisplayName("RA9 dRPW*")] public double? Ra9RunsPerWin { get; init; }
    [DisplayName("보정 전 RA9-WAR*")] public double? Ra9WarBeforeCorrection { get; init; }
    [DisplayName("RA9 WARIP*")] public double? Ra9WarPerInningCorrection { get; init; }
    [DisplayName("RA9 보정*")] public double? Ra9LeagueCorrection { get; init; }
    [DisplayName("KBO RA9-WAR*")] public double? Ra9War { get; init; }
    [DisplayName("Blend WAR 70/30*")] public double? BlendWar { get; init; }
    [DisplayName("RAR")] public double? RunsAboveReplacement { get; init; }
    [DisplayName("RPW")] public double? RunsPerWin { get; init; }
    [DisplayName("팬그래프 공식 WAR")] public double? FanGraphsWar { get; init; }
    [DisplayName("대체승률.275 WAR")] public double? LoweredReplacementWar { get; init; }

    [Browsable(false)] public double? StarterQualityWins { get; init; }
    [Browsable(false)] public double? RelieverQualityWins { get; init; }
    [Browsable(false)] public double? StarterWarBeforeCorrection { get; init; }
    [Browsable(false)] public double? RelieverWarBeforeCorrection { get; init; }
    [Browsable(false)] public double? FanGraphsWarBeforeCorrection { get; init; }

    // 이전 코드 호환용
    [Browsable(false)] public double? Fip { get; init; }
    [Browsable(false)] public double? ReplacementRa9 { get; init; }
}

public sealed class ParkFactorGridRow
{
    [DisplayName("구장")] public string Stadium { get; init; } = string.Empty;
    [DisplayName("사용 시즌")] public string Seasons { get; init; } = string.Empty;
    [DisplayName("경기 수")] public int Games { get; init; }
    [DisplayName("IP")] public double? Innings { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitBatters { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("IFFB")] public int InfieldFlies { get; init; }
    [DisplayName("원시 FIP PF")] public double? RawFipFactor { get; init; }
    [DisplayName("사용 FIP PF")] public double? UsedFipFactor { get; init; }
    [DisplayName("신뢰도")] public string Confidence { get; init; } = string.Empty;
}
