using System.ComponentModel;
using NaverRelay.Application.Queries;

namespace NaverRelay.Application.Statistics;

public sealed class PitcherBasicRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("GS")] public int GamesStarted { get; init; }
    [DisplayName("GR")] public int ReliefGames { get; init; }
    [DisplayName("CG")] public int CompleteGames { get; init; }
    [DisplayName("SHO")] public int Shutouts { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("ER")] public int EarnedRuns { get; init; }
    [DisplayName("R")] public int RunsAllowed { get; init; }
    [DisplayName("TBF")] public int BattersFaced { get; init; }
    [DisplayName("H")] public int HitsAllowed { get; init; }
    [DisplayName("HR")] public int HomeRunsAllowed { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitBatters { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("IFFB")] public int InfieldFlies { get; init; }
    [DisplayName("WP")] public int WildPitches { get; init; }
    [DisplayName("ERA")] public double? ERA { get; init; }
    [DisplayName("RA9")] public double? RA9 { get; init; }
    [DisplayName("FIP")] public double? Fip { get; init; }
    [DisplayName("WHIP")] public double? WHIP { get; init; }
    [DisplayName("피OBP")] public double? OpponentOBP { get; init; }
    [DisplayName("피OPS")] public double? OpponentOPS { get; init; }
    [DisplayName("KBO fWAR")] public double? War { get; init; }
    [DisplayName("KBO RA9-WAR*")] public double? Ra9War { get; init; }
    [DisplayName("Blend WAR*")] public double? BlendWar { get; init; }
    [DisplayName("팬그래프 공식 WAR")] public double? FanGraphsWar { get; init; }
    [DisplayName("대체승률.275 WAR")] public double? LoweredReplacementWar { get; init; }
}

public sealed class PitcherAdvancedRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("CG")] public int CompleteGames { get; init; }
    [DisplayName("SHO")] public int Shutouts { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("K/9")] public double? StrikeoutsPerNine { get; init; }
    [DisplayName("BB/9")] public double? WalksPerNine { get; init; }
    [DisplayName("K/BB")] public double? StrikeoutToWalk { get; init; }
    [DisplayName("HR/9")] public double? HomeRunsPerNine { get; init; }
    [DisplayName("K%")] public double? StrikeoutRate { get; init; }
    [DisplayName("BB%")] public double? WalkRate { get; init; }
    [DisplayName("K-BB%")] public double? StrikeoutMinusWalkRate { get; init; }
    [DisplayName("BABIP")] public double? BABIP { get; init; }
    [DisplayName("LOB%*")] public double? LobRate { get; init; }
    [DisplayName("ERA")] public double? ERA { get; init; }
    [DisplayName("RA9")] public double? RA9 { get; init; }
    [DisplayName("FIP")] public double? Fip { get; init; }
    [DisplayName("xFIP")] public double? Xfip { get; init; }
    [DisplayName("FIP-")] public double? FipMinus { get; init; }
    [DisplayName("xFIP-")] public double? XfipMinus { get; init; }
    [DisplayName("ERA-FIP")] public double? EraMinusFip { get; init; }
    [DisplayName("피AVG")] public double? OpponentAVG { get; init; }
    [DisplayName("피OBP")] public double? OpponentOBP { get; init; }
    [DisplayName("NP")] public int PitchCount { get; init; }
    [DisplayName("P/G")] public double? PitchesPerGame { get; init; }
    [DisplayName("P/IP")] public double? PitchesPerInning { get; init; }
    [DisplayName("P/PA")] public double? PitchesPerPa { get; init; }
}

public sealed class PitcherDetailedValueRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("선발 IP")] public double? StarterInnings { get; init; }
    [DisplayName("구원 IP")] public double? ReliefInnings { get; init; }
    [DisplayName("종합 IP")] public double? InningsPitched { get; init; }
    [DisplayName("RPW")] public double? RunsPerWin { get; init; }
    [DisplayName("선발 RAA*")] public double? StarterRunsAboveAverage { get; init; }
    [DisplayName("구원 RAA*")] public double? ReliefRunsAboveAverage { get; init; }
    [DisplayName("종합 RAA*")] public double? RunsAboveAverage { get; init; }
    [DisplayName("KBO 선발 Repl FIP-")] public double? StarterReplacementFipMinus { get; init; }
    [DisplayName("KBO 구원 Repl FIP-")] public double? RelieverReplacementFipMinus { get; init; }
    [DisplayName("선발 대체 Run*")] public double? StarterReplacementRuns { get; init; }
    [DisplayName("구원 대체 Run*")] public double? ReliefReplacementRuns { get; init; }
    [DisplayName("종합 대체 Run*")] public double? ReplacementRuns { get; init; }
    [DisplayName("선발 RAR*")] public double? StarterRAR { get; init; }
    [DisplayName("구원 RAR*")] public double? ReliefRAR { get; init; }
    [DisplayName("종합 RAR*")] public double? RAR { get; init; }
    [DisplayName("선발 WAA*")] public double? StarterWAA { get; init; }
    [DisplayName("구원 WAA*")] public double? ReliefWAA { get; init; }
    [DisplayName("종합 WAA*")] public double? WAA { get; init; }
    [DisplayName("선발 fWAR*")] public double? StarterWar { get; init; }
    [DisplayName("구원 fWAR*")] public double? ReliefWar { get; init; }
    [DisplayName("보정 전 fWAR")] public double? WarBeforeCorrection { get; init; }
    [DisplayName("KBO WARIP")] public double? WarPerInningCorrection { get; init; }
    [DisplayName("WARIP 보정")] public double? LeagueCorrection { get; init; }
    [DisplayName("KBO fWAR v4")] public double? War { get; init; }
    [DisplayName("pRA9*")] public double? ParkAdjustedRa9 { get; init; }
    [DisplayName("KBO RA9-WAR*")] public double? Ra9War { get; init; }
    [DisplayName("Blend WAR 70/30*")] public double? BlendWar { get; init; }
    [DisplayName("팬그래프 공식 WAR")] public double? FanGraphsWar { get; init; }
    [DisplayName("대체승률.275 WAR")] public double? LoweredReplacementWar { get; init; }
}

public sealed class PitcherExtendedRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("TTO%")] public double? ThreeTrueOutcomeRate { get; init; }
    [DisplayName("구종 수")] public int PitchTypeCount { get; init; }
    [DisplayName("Pitch Entropy")] public double? PitchEntropy { get; init; }
    [DisplayName("다양성 지수")] public double? NormalizedPitchEntropy { get; init; }
    [DisplayName("투심%")] public double? TwoSeamUsage { get; init; }
    [DisplayName("포심%")] public double? FourSeamUsage { get; init; }
    [DisplayName("커터%")] public double? CutterUsage { get; init; }
    [DisplayName("커브%")] public double? CurveUsage { get; init; }
    [DisplayName("슬라이더%")] public double? SliderUsage { get; init; }
    [DisplayName("체인지업%")] public double? ChangeupUsage { get; init; }
    [DisplayName("싱커%")] public double? SinkerUsage { get; init; }
    [DisplayName("포크%")] public double? ForkballUsage { get; init; }
    [DisplayName("너클%")] public double? KnuckleballUsage { get; init; }
    [DisplayName("기타%")] public double? OtherUsage { get; init; }
}

public sealed class PitcherWinProbabilityRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("pLI*")] public double? AverageLeverageIndex { get; init; }
    [DisplayName("gmLI*")] public double? GmLi { get; init; }
    [DisplayName("WPA+")] public double? PositiveWpa { get; init; }
    [DisplayName("WPA-")] public double? NegativeWpa { get; init; }
    [DisplayName("WPA")] public double? Wpa { get; init; }
    [DisplayName("WPA/LI*")] public double? WpaPerLi { get; init; }
}

public sealed class PitcherRunnerRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("SB")] public int StolenBases { get; init; }
    [DisplayName("CS")] public int CaughtStealing { get; init; }
    [DisplayName("SB%")] public double? StolenBaseSuccessRate { get; init; }
    [DisplayName("SBA")] public int StolenBaseAttempts { get; init; }
    [DisplayName("SB2")] public int StolenSecond { get; init; }
    [DisplayName("SB3")] public int StolenThird { get; init; }
    [DisplayName("SB4")] public int StolenHome { get; init; }
    [DisplayName("CS2")] public int CaughtAtSecond { get; init; }
    [DisplayName("CS3")] public int CaughtAtThird { get; init; }
    [DisplayName("CS4")] public int CaughtAtHome { get; init; }
    [DisplayName("WP")] public int WildPitches { get; init; }
}

public sealed class PitcherStarterRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("GS")] public int GamesStarted { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("ERA")] public double? ERA { get; init; }
    [DisplayName("QS")] public int QualityStarts { get; init; }
    [DisplayName("QS%")] public double? QualityStartRate { get; init; }
    [DisplayName("QS+")] public int QualityStartsPlus { get; init; }
    [DisplayName("QS+%")] public double? QualityStartPlusRate { get; init; }
    [DisplayName("RS*")] public int RunSupport { get; init; }
    [DisplayName("RS9*")] public double? RunSupportPerNine { get; init; }
    [DisplayName("팀 W")] public int TeamWins { get; init; }
    [DisplayName("팀 L")] public int TeamLosses { get; init; }
    [DisplayName("팀 W%")] public double? TeamWinRate { get; init; }
    [DisplayName("IP/GS")] public double? InningsPerStart { get; init; }
    [DisplayName("P/GS")] public double? PitchesPerStart { get; init; }
    [DisplayName("KBO fWAR v4")] public double? StarterWar { get; set; }
}

public sealed class PitcherRelieverRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("GR")] public int ReliefGames { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("ERA")] public double? ERA { get; init; }
    [DisplayName("2연투")] public int BackToBackAppearances { get; init; }
    [DisplayName("3연투")] public int ThreeDayStreaks { get; init; }
    [DisplayName("4연투")] public int FourDayStreaks { get; init; }
    [DisplayName("1+이닝")] public int OnePlusInningGames { get; init; }
    [DisplayName("IP/GR")] public double? InningsPerReliefGame { get; init; }
    [DisplayName("P/GR")] public double? PitchesPerReliefGame { get; init; }
    [DisplayName("gmLI*")] public double? GmLi { get; init; }
    [DisplayName("KBO fWAR v4")] public double? ReliefWar { get; set; }
}

public sealed class PitcherBattedBallRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("BIP")] public int BallsInPlay { get; init; }
    [DisplayName("BABIP")] public double? BABIP { get; init; }
    [DisplayName("GB%")] public double? GroundBallRate { get; init; }
    [DisplayName("ifFB%")] public double? InfieldFlyRate { get; init; }
    [DisplayName("ofFB%")] public double? OutfieldFlyRate { get; init; }
    [DisplayName("FB%")] public double? FlyBallRate { get; init; }
    [DisplayName("LD%")] public double? LineDriveRate { get; init; }
    [DisplayName("GB/FB")] public double? GroundBallToFlyBall { get; init; }
    [DisplayName("HR/FB%")] public double? HomeRunPerFlyBall { get; init; }
    [DisplayName("내야안타%")] public double? InfieldHitRate { get; init; }
}

public sealed class PitcherDirectionRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("L%")] public double? LeftRate { get; init; }
    [DisplayName("LC%")] public double? LeftCenterRate { get; init; }
    [DisplayName("C%")] public double? CenterRate { get; init; }
    [DisplayName("RC%")] public double? RightCenterRate { get; init; }
    [DisplayName("R%")] public double? RightRate { get; init; }
    [DisplayName("PU%")] public double? PullRate { get; init; }
    [DisplayName("OP%")] public double? OppositeRate { get; init; }
    [DisplayName("L 개수")] public int Left { get; init; }
    [DisplayName("LC 개수")] public int LeftCenter { get; init; }
    [DisplayName("C 개수")] public int Center { get; init; }
    [DisplayName("RC 개수")] public int RightCenter { get; init; }
    [DisplayName("R 개수")] public int Right { get; init; }
    [DisplayName("PU 개수")] public int Pull { get; init; }
    [DisplayName("OP 개수")] public int Opposite { get; init; }
    [DisplayName("L H")] public int LeftHits { get; init; }
    [DisplayName("LC H")] public int LeftCenterHits { get; init; }
    [DisplayName("C H")] public int CenterHits { get; init; }
    [DisplayName("RC H")] public int RightCenterHits { get; init; }
    [DisplayName("R H")] public int RightHits { get; init; }
    [DisplayName("PU H")] public int PullHits { get; init; }
    [DisplayName("OP H")] public int OppositeHits { get; init; }
    [DisplayName("L 피AVG")] public double? LeftAverage { get; init; }
    [DisplayName("LC 피AVG")] public double? LeftCenterAverage { get; init; }
    [DisplayName("C 피AVG")] public double? CenterAverage { get; init; }
    [DisplayName("RC 피AVG")] public double? RightCenterAverage { get; init; }
    [DisplayName("R 피AVG")] public double? RightAverage { get; init; }
    [DisplayName("PU 피AVG")] public double? PullAverage { get; init; }
    [DisplayName("OP 피AVG")] public double? OppositeAverage { get; init; }
    [DisplayName("BABIP")] public double? BABIP { get; init; }
}

public sealed class PitcherPitchProfileRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("투구 수")] public int Pitches { get; init; }
    [DisplayName("S%")] public double? StrikeRate { get; init; }
    [DisplayName("루킹%")] public double? CalledStrikeRate { get; init; }
    [DisplayName("헛스윙/투구%")] public double? WhiffPerPitch { get; init; }
    [DisplayName("CSW%")] public double? CswRate { get; init; }
    [DisplayName("Swing%")] public double? SwingRate { get; init; }
    [DisplayName("Contact%")] public double? ContactRate { get; init; }
    [DisplayName("Whiff/Swing%")] public double? WhiffRate { get; init; }
    [DisplayName("초구 S%")] public double? FirstPitchStrikeRate { get; init; }
    [DisplayName("초구 헛스윙%")] public double? FirstPitchWhiffRate { get; init; }
    [DisplayName("Putaway%")] public double? PutAwayRate { get; init; }
    [DisplayName("Z-Pitch%")] public double? ZonePitchRate { get; init; }
    [DisplayName("Z-Swing%")] public double? ZoneSwingRate { get; init; }
    [DisplayName("Z-Contact%")] public double? ZoneContactRate { get; init; }
    [DisplayName("O-Pitch%")] public double? OutZonePitchRate { get; init; }
    [DisplayName("O-Swing%")] public double? ChaseRate { get; init; }
    [DisplayName("O-Contact%")] public double? OutZoneContactRate { get; init; }
    [DisplayName("Heart%")] public double? HeartPitchRate { get; init; }
    [DisplayName("Heart Swing%")] public double? HeartSwingRate { get; init; }
    [DisplayName("L/SO")] public int CalledStrikeouts { get; init; }
    [DisplayName("S/SO")] public int SwingingStrikeouts { get; init; }
    [DisplayName("L/SO%")] public double? CalledStrikeoutRate { get; init; }
}

public sealed class PitcherPitchTypeRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("가치* 투심")] public double? TwoSeamValue { get; init; }
    [DisplayName("가치* 포심")] public double? FourSeamValue { get; init; }
    [DisplayName("가치* 커터")] public double? CutterValue { get; init; }
    [DisplayName("가치* 커브")] public double? CurveValue { get; init; }
    [DisplayName("가치* 슬라이더")] public double? SliderValue { get; init; }
    [DisplayName("가치* 체인지업")] public double? ChangeupValue { get; init; }
    [DisplayName("가치* 싱커")] public double? SinkerValue { get; init; }
    [DisplayName("가치* 포크")] public double? ForkballValue { get; init; }
    [DisplayName("가치* 너클")] public double? KnuckleballValue { get; init; }
    [DisplayName("가치* 기타")] public double? OtherValue { get; init; }
    [DisplayName("가치/100* 투심")] public double? TwoSeamValuePer100 { get; init; }
    [DisplayName("가치/100* 포심")] public double? FourSeamValuePer100 { get; init; }
    [DisplayName("가치/100* 커터")] public double? CutterValuePer100 { get; init; }
    [DisplayName("가치/100* 커브")] public double? CurveValuePer100 { get; init; }
    [DisplayName("가치/100* 슬라이더")] public double? SliderValuePer100 { get; init; }
    [DisplayName("가치/100* 체인지업")] public double? ChangeupValuePer100 { get; init; }
    [DisplayName("가치/100* 싱커")] public double? SinkerValuePer100 { get; init; }
    [DisplayName("가치/100* 포크")] public double? ForkballValuePer100 { get; init; }
    [DisplayName("가치/100* 너클")] public double? KnuckleballValuePer100 { get; init; }
    [DisplayName("가치/100* 기타")] public double? OtherValuePer100 { get; init; }
    [DisplayName("구속 투심")] public double? TwoSeamSpeed { get; init; }
    [DisplayName("구속 포심")] public double? FourSeamSpeed { get; init; }
    [DisplayName("구속 커터")] public double? CutterSpeed { get; init; }
    [DisplayName("구속 커브")] public double? CurveSpeed { get; init; }
    [DisplayName("구속 슬라이더")] public double? SliderSpeed { get; init; }
    [DisplayName("구속 체인지업")] public double? ChangeupSpeed { get; init; }
    [DisplayName("구속 싱커")] public double? SinkerSpeed { get; init; }
    [DisplayName("구속 포크")] public double? ForkballSpeed { get; init; }
    [DisplayName("구속 너클")] public double? KnuckleballSpeed { get; init; }
    [DisplayName("구속 기타")] public double? OtherSpeed { get; init; }
    [DisplayName("구사율 투심")] public double? TwoSeamUsage { get; init; }
    [DisplayName("구사율 포심")] public double? FourSeamUsage { get; init; }
    [DisplayName("구사율 커터")] public double? CutterUsage { get; init; }
    [DisplayName("구사율 커브")] public double? CurveUsage { get; init; }
    [DisplayName("구사율 슬라이더")] public double? SliderUsage { get; init; }
    [DisplayName("구사율 체인지업")] public double? ChangeupUsage { get; init; }
    [DisplayName("구사율 싱커")] public double? SinkerUsage { get; init; }
    [DisplayName("구사율 포크")] public double? ForkballUsage { get; init; }
    [DisplayName("구사율 너클")] public double? KnuckleballUsage { get; init; }
    [DisplayName("구사율 기타")] public double? OtherUsage { get; init; }
    [DisplayName("투구수 투심")] public int TwoSeamCount { get; init; }
    [DisplayName("투구수 포심")] public int FourSeamCount { get; init; }
    [DisplayName("투구수 커터")] public int CutterCount { get; init; }
    [DisplayName("투구수 커브")] public int CurveCount { get; init; }
    [DisplayName("투구수 슬라이더")] public int SliderCount { get; init; }
    [DisplayName("투구수 체인지업")] public int ChangeupCount { get; init; }
    [DisplayName("투구수 싱커")] public int SinkerCount { get; init; }
    [DisplayName("투구수 포크")] public int ForkballCount { get; init; }
    [DisplayName("투구수 너클")] public int KnuckleballCount { get; init; }
    [DisplayName("투구수 기타")] public int OtherCount { get; init; }
    [DisplayName("피AVG 투심")] public double? TwoSeamOpponentAverage { get; init; }
    [DisplayName("피AVG 포심")] public double? FourSeamOpponentAverage { get; init; }
    [DisplayName("피AVG 커터")] public double? CutterOpponentAverage { get; init; }
    [DisplayName("피AVG 커브")] public double? CurveOpponentAverage { get; init; }
    [DisplayName("피AVG 슬라이더")] public double? SliderOpponentAverage { get; init; }
    [DisplayName("피AVG 체인지업")] public double? ChangeupOpponentAverage { get; init; }
    [DisplayName("피AVG 싱커")] public double? SinkerOpponentAverage { get; init; }
    [DisplayName("피AVG 포크")] public double? ForkballOpponentAverage { get; init; }
    [DisplayName("피AVG 너클")] public double? KnuckleballOpponentAverage { get; init; }
    [DisplayName("피AVG 기타")] public double? OtherOpponentAverage { get; init; }
    [DisplayName("피SLG 투심")] public double? TwoSeamOpponentSlugging { get; init; }
    [DisplayName("피SLG 포심")] public double? FourSeamOpponentSlugging { get; init; }
    [DisplayName("피SLG 커터")] public double? CutterOpponentSlugging { get; init; }
    [DisplayName("피SLG 커브")] public double? CurveOpponentSlugging { get; init; }
    [DisplayName("피SLG 슬라이더")] public double? SliderOpponentSlugging { get; init; }
    [DisplayName("피SLG 체인지업")] public double? ChangeupOpponentSlugging { get; init; }
    [DisplayName("피SLG 싱커")] public double? SinkerOpponentSlugging { get; init; }
    [DisplayName("피SLG 포크")] public double? ForkballOpponentSlugging { get; init; }
    [DisplayName("피SLG 너클")] public double? KnuckleballOpponentSlugging { get; init; }
    [DisplayName("피SLG 기타")] public double? OtherOpponentSlugging { get; init; }
}

public static class PitcherRecordRoomRowFactory
{
    public static IReadOnlyList<PitcherBasicRecordRow> BuildBasic(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var saber = snapshot.PitcherSabermetrics.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var values = snapshot.PitcherValues.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var rows = snapshot.PitcherClassic
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row =>
            {
                saber.TryGetValue(Key(row.Pcode, row.TeamCode), out var saberRow);
                values.TryGetValue(Key(row.Pcode, row.TeamCode), out var valueRow);
                var innings = valueRow?.InningsPitched;
                double? era = innings.HasValue && innings.Value > 0 ? valueRow!.EarnedRuns * 9.0 / innings.Value : null;
                double? ra9 = innings.HasValue && innings.Value > 0 ? valueRow!.RunsAllowed * 9.0 / innings.Value : null;
                double? whip = innings.HasValue && innings.Value > 0
                    ? (row.Hits + row.Walks) / innings.Value
                    : null;
                var obpDenominator = row.OpponentAtBats + row.Walks + row.HitBatters + row.SacrificeFlies;
                double? opponentObp = obpDenominator > 0
                    ? (row.Hits + row.Walks + row.HitBatters) / (double)obpDenominator
                    : null;
                double? opponentSlg = row.OpponentAtBats > 0
                    ? row.TotalBasesAllowed / (double)row.OpponentAtBats
                    : null;
                double? opponentOps = opponentObp.HasValue && opponentSlg.HasValue
                    ? opponentObp.Value + opponentSlg.Value
                    : null;
                return new PitcherBasicRecordRow
                {
                    Pcode = row.Pcode,
                    Name = row.Name,
                    TeamCode = row.TeamCode,
                    Games = valueRow?.Games ?? row.Games,
                    GamesStarted = valueRow?.GamesStarted ?? 0,
                    ReliefGames = valueRow?.ReliefGames ?? 0,
                    CompleteGames = valueRow?.CompleteGames ?? 0,
                    Shutouts = valueRow?.Shutouts ?? 0,
                    InningsPitched = innings,
                    EarnedRuns = valueRow?.EarnedRuns ?? 0,
                    RunsAllowed = valueRow?.RunsAllowed ?? 0,
                    BattersFaced = row.BattersFaced,
                    HitsAllowed = row.Hits,
                    HomeRunsAllowed = valueRow?.HomeRuns ?? row.HomeRuns,
                    Walks = valueRow?.Walks ?? row.Walks,
                    HitBatters = valueRow?.HitBatters ?? row.HitBatters,
                    Strikeouts = valueRow?.Strikeouts ?? row.Strikeouts,
                    InfieldFlies = valueRow?.InfieldFlies ?? 0,
                    WildPitches = valueRow?.WildPitches ?? 0,
                    ERA = era,
                    RA9 = ra9,
                    Fip = saberRow?.Fip,
                    WHIP = whip,
                    OpponentOBP = opponentObp,
                    OpponentOPS = opponentOps,
                    War = valueRow?.War,
                    Ra9War = valueRow?.Ra9War,
                    BlendWar = valueRow?.BlendWar,
                    FanGraphsWar = valueRow?.FanGraphsWar,
                    LoweredReplacementWar = valueRow?.LoweredReplacementWar,
                };
            })
            .OrderByDescending(row => row.War ?? double.MinValue)
            .ThenByDescending(row => row.InningsPitched ?? 0.0)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static IReadOnlyList<PitcherAdvancedRecordRow> BuildAdvanced(
        AnalyticsSnapshot snapshot,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var classic = snapshot.PitcherClassic.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var values = snapshot.PitcherValues.ToDictionary(row => Key(row.Pcode, row.TeamCode), StringComparer.Ordinal);
        var rows = snapshot.PitcherSabermetrics
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row =>
            {
                classic.TryGetValue(Key(row.Pcode, row.TeamCode), out var classicRow);
                values.TryGetValue(Key(row.Pcode, row.TeamCode), out var valueRow);
                var innings = valueRow?.InningsPitched ?? row.InningsPitched;
                double? era = valueRow is not null && innings.HasValue && innings.Value > 0
                    ? valueRow.EarnedRuns * 9.0 / innings.Value
                    : null;
                double? ra9 = valueRow is not null && innings.HasValue && innings.Value > 0
                    ? valueRow.RunsAllowed * 9.0 / innings.Value
                    : null;
                var opponentAb = Math.Max(0, row.BattersFaced - (classicRow?.Walks ?? 0) - (classicRow?.HitBatters ?? 0));
                double? opponentAvg = opponentAb > 0 ? (classicRow?.Hits ?? 0) / (double)opponentAb : null;
                double? opponentObp = row.BattersFaced > 0
                    ? ((classicRow?.Hits ?? 0) + (classicRow?.Walks ?? 0) + (classicRow?.HitBatters ?? 0)) / (double)row.BattersFaced
                    : null;
                return new PitcherAdvancedRecordRow
                {
                    Pcode = row.Pcode,
                    Name = row.Name,
                    TeamCode = row.TeamCode,
                    Games = valueRow?.Games ?? row.Games,
                    CompleteGames = valueRow?.CompleteGames ?? 0,
                    Shutouts = valueRow?.Shutouts ?? 0,
                    InningsPitched = innings,
                    StrikeoutsPerNine = row.StrikeoutsPerNine,
                    WalksPerNine = row.WalksPerNine,
                    StrikeoutToWalk = Divide(valueRow?.Strikeouts ?? classicRow?.Strikeouts ?? 0, valueRow?.Walks ?? classicRow?.Walks ?? 0),
                    HomeRunsPerNine = row.HomeRunsPerNine,
                    StrikeoutRate = row.StrikeoutRate,
                    WalkRate = row.WalkRate,
                    StrikeoutMinusWalkRate = row.StrikeoutMinusWalkRate,
                    BABIP = row.Babip,
                    LobRate = row.LobRate,
                    ERA = era,
                    RA9 = ra9,
                    Fip = row.Fip,
                    Xfip = row.Xfip,
                    FipMinus = row.FipMinus,
                    XfipMinus = row.XfipMinus,
                    EraMinusFip = era.HasValue && row.Fip.HasValue ? era.Value - row.Fip.Value : null,
                    OpponentAVG = opponentAvg,
                    OpponentOBP = opponentObp,
                    PitchCount = classicRow?.PitchCount ?? 0,
                    PitchesPerGame = Divide(classicRow?.PitchCount ?? 0, valueRow?.Games ?? row.Games),
                    PitchesPerInning = Divide(classicRow?.PitchCount ?? 0, innings ?? 0.0),
                    PitchesPerPa = Divide(classicRow?.PitchCount ?? 0, row.BattersFaced),
                };
            })
            .OrderBy(row => row.Fip ?? double.MaxValue)
            .ThenByDescending(row => row.InningsPitched ?? 0.0)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static IReadOnlyList<PitcherDetailedValueRecordRow> BuildValue(
        AnalyticsSnapshot snapshot,
        LeagueReference league,
        IReadOnlySet<string>? eligibleKeys = null)
    {
        var rows = snapshot.PitcherValues
            .Where(row => IsEligible(row.Pcode, row.TeamCode, eligibleKeys))
            .Select(row =>
            {
                var innings = row.InningsPitched ?? 0.0;
                var starterInnings = row.StarterInnings ?? 0.0;
                var reliefInnings = row.ReliefInnings ?? 0.0;
                var rpw = row.DynamicRunsPerWin ?? row.RunsPerWin ?? 10.0;
                var starterQualityWins = row.StarterQualityWins ?? 0.0;
                var reliefQualityWins = row.RelieverQualityWins ?? 0.0;
                var starterReplacementWins = row.StarterReplacementWins ?? 0.0;
                var reliefReplacementWins = row.RelieverReplacementWins ?? 0.0;
                var starterShare = innings > 0 ? starterInnings / innings : 0.0;
                var reliefShare = innings > 0 ? reliefInnings / innings : 0.0;
                var correction = row.LeagueCorrection ?? 0.0;
                var starterCorrection = correction * starterShare;
                var reliefCorrection = correction * reliefShare;
                var starterWar = starterQualityWins + starterReplacementWins + starterCorrection;
                var reliefWar = reliefQualityWins + reliefReplacementWins + reliefCorrection;
                return new PitcherDetailedValueRecordRow
                {
                    Pcode = row.Pcode,
                    Name = row.Name,
                    TeamCode = row.TeamCode,
                    Games = row.Games,
                    StarterInnings = starterInnings,
                    ReliefInnings = reliefInnings,
                    InningsPitched = innings,
                    RunsPerWin = rpw,
                    StarterRunsAboveAverage = starterQualityWins * rpw,
                    ReliefRunsAboveAverage = reliefQualityWins * rpw,
                    RunsAboveAverage = (starterQualityWins + reliefQualityWins) * rpw,
                    StarterReplacementFipMinus = row.StarterReplacementFipMinus,
                    RelieverReplacementFipMinus = row.RelieverReplacementFipMinus,
                    StarterReplacementRuns = starterReplacementWins * rpw,
                    ReliefReplacementRuns = reliefReplacementWins * rpw,
                    ReplacementRuns = (starterReplacementWins + reliefReplacementWins) * rpw,
                    StarterRAR = starterWar * rpw,
                    ReliefRAR = reliefWar * rpw,
                    RAR = (row.War ?? starterWar + reliefWar) * rpw,
                    StarterWAA = starterQualityWins,
                    ReliefWAA = reliefQualityWins,
                    WAA = starterQualityWins + reliefQualityWins,
                    StarterWar = starterWar,
                    ReliefWar = reliefWar,
                    WarBeforeCorrection = row.WarBeforeCorrection,
                    WarPerInningCorrection = row.WarPerInningCorrection,
                    LeagueCorrection = row.LeagueCorrection,
                    War = row.War,
                    ParkAdjustedRa9 = row.ParkAdjustedRa9,
                    Ra9War = row.Ra9War,
                    BlendWar = row.BlendWar,
                    FanGraphsWar = row.FanGraphsWar,
                    LoweredReplacementWar = row.LoweredReplacementWar,
                };
            })
            .OrderByDescending(row => row.War ?? double.MinValue)
            .ThenByDescending(row => row.InningsPitched ?? 0.0)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ApplyRanks(rows);
        return rows;
    }

    public static string Key(string? pcode, string? teamCode) => $"{pcode ?? string.Empty}|{teamCode ?? string.Empty}";

    private static bool IsEligible(string? pcode, string? teamCode, IReadOnlySet<string>? eligibleKeys) =>
        eligibleKeys is null || eligibleKeys.Contains(Key(pcode, teamCode));

    private static double? Divide(double numerator, double denominator) =>
        Math.Abs(denominator) < 0.0000001 ? null : numerator / denominator;

    private static void ApplyRanks<T>(IReadOnlyList<T> rows)
    {
        var property = typeof(T).GetProperty("Rank");
        if (property is null || !property.CanWrite) return;
        for (var index = 0; index < rows.Count; index++) property.SetValue(rows[index], index + 1);
    }
}
