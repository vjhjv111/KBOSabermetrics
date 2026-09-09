using System.ComponentModel;

namespace NaverRelay.Application.Teams;

/// <summary>
/// WinForms와 향후 ASP.NET Core/React가 동일하게 사용할 팀 페이지 계약입니다.
/// 구현체는 관계형 SQLite 또는 PostgreSQL로 교체할 수 있습니다.
/// </summary>
public interface ITeamPageService
{
    IReadOnlyList<TeamSearchItem> GetTeams();
    TeamPageData? GetTeamPage(string teamCode);
}

public sealed class TeamSearchItem
{
    [DisplayName("팀 코드")] public string TeamCode { get; init; } = string.Empty;
    [DisplayName("최근 팀명")] public string LatestName { get; init; } = string.Empty;
    [DisplayName("첫 시즌")] public int? FirstSeason { get; init; }
    [DisplayName("최근 시즌")] public int? LastSeason { get; init; }
    [DisplayName("경기")] public int Games { get; init; }
}

public sealed class TeamProfile
{
    public string TeamCode { get; init; } = string.Empty;
    public string LatestName { get; init; } = string.Empty;
    public string NameHistory { get; init; } = string.Empty;
    public string StadiumHistory { get; init; } = string.Empty;
    public int? FirstSeason { get; init; }
    public int? LastSeason { get; init; }
    public int Games { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Ties { get; init; }
    public int RunsFor { get; init; }
    public int RunsAgainst { get; init; }
    public int RunDifferential => RunsFor - RunsAgainst;
    public double? WinningPercentage { get; init; }
}

public sealed class TeamBattingSeasonRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("Team")] public string TeamName { get; init; } = string.Empty;
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("W")] public int Wins { get; init; }
    [DisplayName("L")] public int Losses { get; init; }
    [DisplayName("D")] public int Ties { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("AB")] public int AB { get; init; }
    [DisplayName("R")] public int Runs { get; init; }
    [DisplayName("H")] public int Hits { get; init; }
    [DisplayName("1B")] public int Singles { get; init; }
    [DisplayName("2B")] public int Doubles { get; init; }
    [DisplayName("3B")] public int Triples { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("TB")] public int TotalBases { get; init; }
    [DisplayName("RBI")] public int RBI { get; init; }
    [DisplayName("SB")] public int StolenBases { get; init; }
    [DisplayName("CS")] public int CaughtStealing { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("IBB")] public int IntentionalWalks { get; init; }
    [DisplayName("HBP")] public int HitByPitch { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("GDP")] public int DoublePlays { get; init; }
    [DisplayName("SH")] public int SacrificeBunts { get; init; }
    [DisplayName("SF")] public int SacrificeFlies { get; init; }
    [DisplayName("AVG")] public double? AVG { get; init; }
    [DisplayName("OBP")] public double? OBP { get; init; }
    [DisplayName("SLG")] public double? SLG { get; init; }
    [DisplayName("OPS")] public double? OPS { get; init; }
    [DisplayName("ISO")] public double? ISO { get; init; }
    [DisplayName("BABIP")] public double? BABIP { get; init; }
    [DisplayName("BB%")] public double? WalkRate { get; init; }
    [DisplayName("K%")] public double? StrikeoutRate { get; init; }
    [DisplayName("wOBA*")] public double? Woba { get; init; }
    [DisplayName("wRAA*")] public double? Wraa { get; init; }
    [DisplayName("wRC*")] public double? Wrc { get; init; }
    [DisplayName("wRC+*")] public double? WrcPlus { get; init; }
    [DisplayName("OPS+")] public double? OpsPlus { get; init; }
    [DisplayName("타자 WAR 합")] public double? BatterWar { get; init; }
}

public sealed class TeamPitchingSeasonRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("Team")] public string TeamName { get; init; } = string.Empty;
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("W")] public int Wins { get; init; }
    [DisplayName("L")] public int Losses { get; init; }
    [DisplayName("D")] public int Ties { get; init; }
    [DisplayName("IP")] public double InningsPitched { get; init; }
    [DisplayName("R")] public int RunsAllowed { get; init; }
    [DisplayName("ER")] public int EarnedRuns { get; init; }
    [DisplayName("H")] public int HitsAllowed { get; init; }
    [DisplayName("HR")] public int HomeRunsAllowed { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitBatters { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("IFFB")] public int InfieldFlies { get; init; }
    [DisplayName("ERA")] public double? ERA { get; init; }
    [DisplayName("WHIP")] public double? WHIP { get; init; }
    [DisplayName("K/9")] public double? StrikeoutsPerNine { get; init; }
    [DisplayName("BB/9")] public double? WalksPerNine { get; init; }
    [DisplayName("HR/9")] public double? HomeRunsPerNine { get; init; }
    [DisplayName("FIP*")] public double? Fip { get; init; }
    [DisplayName("xFIP*")] public double? Xfip { get; init; }
    [DisplayName("ifFIP")] public double? IfFip { get; init; }
    [DisplayName("FIPR9")] public double? FipR9 { get; init; }
    [DisplayName("FIP PF")] public double? ParkFactor { get; init; }
    [DisplayName("pFIPR9")] public double? ParkAdjustedFipR9 { get; init; }
    [DisplayName("dRPW")] public double? DynamicRunsPerWin { get; init; }
    [DisplayName("불펜 gmLI")] public double? GmLi { get; init; }
    [DisplayName("KBO fWAR 합")] public double? PitcherWar { get; init; }
    [DisplayName("KBO RA9-WAR 합*")] public double? PitcherRa9War { get; init; }
    [DisplayName("Blend WAR 합*")] public double? PitcherBlendWar { get; init; }
}

public sealed class TeamValueSeasonRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("Team")] public string TeamName { get; init; } = string.Empty;
    [DisplayName("타격 Runs")] public double? BattingRuns { get; init; }
    [DisplayName("주루 Runs")] public double? RunningRuns { get; init; }
    [DisplayName("수비 Runs")] public double? FieldingRuns { get; init; }
    [DisplayName("포지션 Runs")] public double? PositionRuns { get; init; }
    [DisplayName("대체 Runs")] public double? ReplacementRuns { get; init; }
    [DisplayName("타자 RAR")] public double? BatterRAR { get; init; }
    [DisplayName("타자 WAR")] public double? BatterWar { get; init; }
    [DisplayName("투수 RAR")] public double? PitcherRAR { get; init; }
    [DisplayName("투수 fWAR")] public double? PitcherWar { get; init; }
    [DisplayName("투수 RA9-WAR*")] public double? PitcherRa9War { get; init; }
    [DisplayName("투수 Blend WAR*")] public double? PitcherBlendWar { get; init; }
    [DisplayName("팀 WAR 합")] public double? TotalWar { get; init; }
}

public sealed class TeamPlayerBattingRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("선수 코드")] public string Pcode { get; init; } = string.Empty;
    [DisplayName("선수")] public string Name { get; init; } = string.Empty;
    [DisplayName("Pos.")] public string Position { get; init; } = string.Empty;
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("AB")] public int AB { get; init; }
    [DisplayName("H")] public int Hits { get; init; }
    [DisplayName("2B")] public int Doubles { get; init; }
    [DisplayName("3B")] public int Triples { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("AVG")] public double? AVG { get; init; }
    [DisplayName("OBP")] public double? OBP { get; init; }
    [DisplayName("SLG")] public double? SLG { get; init; }
    [DisplayName("OPS")] public double? OPS { get; init; }
    [DisplayName("wOBA*")] public double? Woba { get; init; }
    [DisplayName("wRC+*")] public double? WrcPlus { get; init; }
    [DisplayName("WAR*")] public double? War { get; init; }
}

public sealed class TeamPlayerPitchingRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("선수 코드")] public string Pcode { get; init; } = string.Empty;
    [DisplayName("투수")] public string Name { get; init; } = string.Empty;
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("GS")] public int GamesStarted { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("ERA")] public double? ERA { get; init; }
    [DisplayName("WHIP")] public double? WHIP { get; init; }
    [DisplayName("K/9")] public double? StrikeoutsPerNine { get; init; }
    [DisplayName("BB/9")] public double? WalksPerNine { get; init; }
    [DisplayName("FIP*")] public double? Fip { get; init; }
    [DisplayName("ifFIP")] public double? IfFip { get; init; }
    [DisplayName("gmLI")] public double? GmLi { get; init; }
    [DisplayName("KBO fWAR")] public double? War { get; init; }
    [DisplayName("KBO RA9-WAR*")] public double? Ra9War { get; init; }
    [DisplayName("Blend WAR*")] public double? BlendWar { get; init; }
}

public sealed class TeamOpponentRecordRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("상대 코드")] public string OpponentCode { get; init; } = string.Empty;
    [DisplayName("상대")] public string OpponentName { get; init; } = string.Empty;
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("W")] public int Wins { get; init; }
    [DisplayName("L")] public int Losses { get; init; }
    [DisplayName("D")] public int Ties { get; init; }
    [DisplayName("승률")] public double? WinningPercentage { get; init; }
    [DisplayName("득점")] public int RunsFor { get; init; }
    [DisplayName("실점")] public int RunsAgainst { get; init; }
    [DisplayName("득실차")] public int RunDifferential { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("AB")] public int AB { get; init; }
    [DisplayName("H")] public int Hits { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("AVG")] public double? AVG { get; init; }
    [DisplayName("OBP")] public double? OBP { get; init; }
    [DisplayName("SLG")] public double? SLG { get; init; }
    [DisplayName("OPS")] public double? OPS { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("ER")] public int EarnedRuns { get; init; }
    [DisplayName("ERA")] public double? ERA { get; init; }
    [DisplayName("FIP*")] public double? Fip { get; init; }
}

public sealed class TeamSituationSplitRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("관점")] public string Perspective { get; init; } = string.Empty;
    [DisplayName("구분")] public string Category { get; init; } = string.Empty;
    [DisplayName("상황")] public string Bucket { get; init; } = string.Empty;
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("AB")] public int AB { get; init; }
    [DisplayName("H")] public int Hits { get; init; }
    [DisplayName("2B")] public int Doubles { get; init; }
    [DisplayName("3B")] public int Triples { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitByPitch { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("AVG")] public double? AVG { get; init; }
    [DisplayName("OBP")] public double? OBP { get; init; }
    [DisplayName("SLG")] public double? SLG { get; init; }
    [DisplayName("OPS")] public double? OPS { get; init; }
    [DisplayName("wOBA*")] public double? Woba { get; init; }
}

public sealed class TeamPitchTypeBattingRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("구종")] public string PitchType { get; init; } = string.Empty;
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("AB")] public int AB { get; init; }
    [DisplayName("H")] public int Hits { get; init; }
    [DisplayName("1B")] public int Singles { get; init; }
    [DisplayName("2B")] public int Doubles { get; init; }
    [DisplayName("3B")] public int Triples { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitByPitch { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("AVG")] public double? AVG { get; init; }
    [DisplayName("OBP")] public double? OBP { get; init; }
    [DisplayName("SLG")] public double? SLG { get; init; }
    [DisplayName("OPS")] public double? OPS { get; init; }
    [DisplayName("wOBA*")] public double? Woba { get; init; }
}

public sealed class TeamPitchTypePitchingRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("구종")] public string PitchType { get; init; } = string.Empty;
    [DisplayName("투구")] public int Pitches { get; init; }
    [DisplayName("사용률")] public double? UsageRate { get; init; }
    [DisplayName("평균 구속")] public double? AverageSpeed { get; init; }
    [DisplayName("스트라이크%")] public double? StrikeRate { get; init; }
    [DisplayName("Swing%")] public double? SwingRate { get; init; }
    [DisplayName("Whiff%")] public double? WhiffRate { get; init; }
    [DisplayName("Contact%")] public double? ContactRate { get; init; }
    [DisplayName("CSW%")] public double? CswRate { get; init; }
    [DisplayName("상대 PA")] public int PlateAppearances { get; init; }
    [DisplayName("상대 AB")] public int AtBats { get; init; }
    [DisplayName("피안타")] public int HitsAllowed { get; init; }
    [DisplayName("피홈런")] public int HomeRunsAllowed { get; init; }
    [DisplayName("피AVG")] public double? OpponentAVG { get; init; }
    [DisplayName("피OPS")] public double? OpponentOPS { get; init; }
}

public sealed class TeamGameLogRow
{
    [DisplayName("Date")] public string Date { get; init; } = string.Empty;
    [DisplayName("Year")] public int? Year { get; init; }
    [DisplayName("상대 코드")] public string OpponentCode { get; init; } = string.Empty;
    [DisplayName("상대")] public string OpponentName { get; init; } = string.Empty;
    [DisplayName("장소")] public string Venue { get; init; } = string.Empty;
    [DisplayName("구장")] public string Stadium { get; init; } = string.Empty;
    [DisplayName("득점")] public int? RunsFor { get; init; }
    [DisplayName("실점")] public int? RunsAgainst { get; init; }
    [DisplayName("결과")] public string Result { get; init; } = string.Empty;
    [DisplayName("GameId")] public string GameId { get; init; } = string.Empty;
}

public sealed class TeamPageData
{
    public TeamProfile Profile { get; init; } = new();
    public IReadOnlyList<TeamBattingSeasonRow> BattingSeasons { get; init; } = Array.Empty<TeamBattingSeasonRow>();
    public IReadOnlyList<TeamPitchingSeasonRow> PitchingSeasons { get; init; } = Array.Empty<TeamPitchingSeasonRow>();
    public IReadOnlyList<TeamValueSeasonRow> ValueSeasons { get; init; } = Array.Empty<TeamValueSeasonRow>();
    public IReadOnlyList<TeamPlayerBattingRow> PlayerBatting { get; init; } = Array.Empty<TeamPlayerBattingRow>();
    public IReadOnlyList<TeamPlayerPitchingRow> PlayerPitching { get; init; } = Array.Empty<TeamPlayerPitchingRow>();
    public IReadOnlyList<TeamOpponentRecordRow> OpponentRecords { get; init; } = Array.Empty<TeamOpponentRecordRow>();
    public IReadOnlyList<TeamSituationSplitRow> SituationSplits { get; init; } = Array.Empty<TeamSituationSplitRow>();
    public IReadOnlyList<TeamPitchTypeBattingRow> BattingByPitchType { get; init; } = Array.Empty<TeamPitchTypeBattingRow>();
    public IReadOnlyList<TeamPitchTypePitchingRow> PitchingByPitchType { get; init; } = Array.Empty<TeamPitchTypePitchingRow>();
    public IReadOnlyList<TeamGameLogRow> GameLogs { get; init; } = Array.Empty<TeamGameLogRow>();
    public string FormulaDocumentation { get; init; } = string.Empty;
}
