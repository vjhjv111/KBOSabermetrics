using System.ComponentModel;

namespace NaverRelay.Application.Players;

/// <summary>
/// 데스크톱과 향후 ASP.NET Core API가 함께 사용할 선수 페이지 계약입니다.
/// WinForms, SQLite, HTTP 형식에 의존하지 않습니다.
/// </summary>
public interface IPlayerPageService
{
    IReadOnlyList<PlayerSearchItem> SearchPlayers(string? query, int maxResults = 100);
    PlayerPageData? GetPlayerPage(string pcode);
}

public sealed class PlayerSearchItem
{
    [DisplayName("선수 코드")] public string Pcode { get; init; } = string.Empty;
    [DisplayName("선수")] public string Name { get; init; } = string.Empty;
    [DisplayName("생년월일")] public string BirthDate { get; init; } = "미상";
    [DisplayName("최근 팀")] public string LatestTeam { get; init; } = "-";
    [DisplayName("주 포지션")] public string PrimaryPosition { get; init; } = "-";
    [DisplayName("구분")] public string Role { get; init; } = "-";
    [DisplayName("투타")] public string BatsThrows { get; init; } = "-";
    [DisplayName("활동 연도")] public string ActiveYears { get; init; } = "-";
}

public sealed class PlayerProfile
{
    public string Pcode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string BirthDate { get; init; } = "미상";
    public string LatestTeam { get; init; } = "-";
    public string TeamHistory { get; init; } = "-";
    public string PrimaryPosition { get; init; } = "-";
    public string Role { get; init; } = "-";
    public string BatsThrows { get; init; } = "-";
    public int? FirstSeason { get; init; }
    public int? LastSeason { get; init; }
    public int CareerGames { get; init; }
    public int CareerPlateAppearances { get; init; }
    public double CareerInnings { get; init; }
    public double? CareerWar { get; init; }
}

public sealed class PlayerBattingSeasonRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("Team")] public string Team { get; init; } = string.Empty;
    [DisplayName("Age")] public int? Age { get; init; }
    [DisplayName("Pos.")] public string Position { get; init; } = "-";
    [DisplayName("G")] public int Games { get; init; }
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
    [DisplayName("타격 Runs*")] public double? BattingRuns { get; init; }
    [DisplayName("주루 Runs*")] public double? RunningRuns { get; init; }
    [DisplayName("포지션 Runs*")] public double? PositionRuns { get; init; }
    [DisplayName("대체 Runs*")] public double? ReplacementRuns { get; init; }
    [DisplayName("RAR*")] public double? RunsAboveReplacement { get; init; }
    [DisplayName("WAR*")] public double? War { get; init; }
}

public sealed class PlayerPitchingSeasonRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("Team")] public string Team { get; init; } = string.Empty;
    [DisplayName("Age")] public int? Age { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("GS")] public int GamesStarted { get; init; }
    [DisplayName("구원 G")] public int ReliefGames { get; init; }
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
    [DisplayName("gmLI")] public double? GmLi { get; init; }
    [DisplayName("LI 배수")] public double? LeverageMultiplier { get; init; }
    [DisplayName("KBO 선발 Repl FIP-")] public double? StarterReplacementFipMinus { get; init; }
    [DisplayName("KBO 구원 Repl FIP-")] public double? RelieverReplacementFipMinus { get; init; }
    [DisplayName("KBO WARIP")] public double? WarPerInningCorrection { get; init; }
    [DisplayName("WARIP 보정")] public double? LeagueCorrection { get; init; }
    [DisplayName("KBO fWAR v4")] public double? War { get; init; }
    [DisplayName("pRA9*")] public double? ParkAdjustedRa9 { get; init; }
    [DisplayName("KBO RA9-WAR*")] public double? Ra9War { get; init; }
    [DisplayName("Blend WAR 70/30*")] public double? BlendWar { get; init; }
}

public sealed class PlayerValueSeasonRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("Team")] public string Team { get; init; } = string.Empty;
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
    [DisplayName("통합 WAR")] public double? TotalWar { get; init; }
}

public sealed class PlayerGameLogRow
{
    [DisplayName("Date")] public string Date { get; init; } = string.Empty;
    [DisplayName("Year")] public int? Year { get; init; }
    [DisplayName("Team")] public string Team { get; init; } = string.Empty;
    [DisplayName("상대")] public string Opponent { get; init; } = string.Empty;
    [DisplayName("장소")] public string Venue { get; init; } = string.Empty;
    [DisplayName("구장")] public string Stadium { get; init; } = string.Empty;
    [DisplayName("결과")] public string Result { get; init; } = string.Empty;
    [DisplayName("PA")] public int? PA { get; init; }
    [DisplayName("AB")] public int? AB { get; init; }
    [DisplayName("H")] public int? Hits { get; init; }
    [DisplayName("2B")] public int? Doubles { get; init; }
    [DisplayName("3B")] public int? Triples { get; init; }
    [DisplayName("HR")] public int? HomeRuns { get; init; }
    [DisplayName("BB")] public int? Walks { get; init; }
    [DisplayName("HBP")] public int? HitByPitch { get; init; }
    [DisplayName("SO")] public int? Strikeouts { get; init; }
    [DisplayName("R")] public int? Runs { get; init; }
    [DisplayName("RBI")] public int? RBI { get; init; }
    [DisplayName("SB")] public int? StolenBases { get; init; }
    [DisplayName("CS")] public int? CaughtStealing { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("ER")] public int? EarnedRuns { get; init; }
    [DisplayName("투수 SO")] public int? PitchingStrikeouts { get; init; }
    [DisplayName("GameId")] public string GameId { get; init; } = string.Empty;
}

public sealed class PlayerPlateAppearanceRow
{
    [DisplayName("Date")] public string Date { get; init; } = string.Empty;
    [DisplayName("관점")] public string Perspective { get; init; } = string.Empty;
    [DisplayName("상대")] public string Opponent { get; init; } = string.Empty;
    [DisplayName("이닝")] public string Inning { get; init; } = string.Empty;
    [DisplayName("투수")] public string Pitcher { get; init; } = string.Empty;
    [DisplayName("타자")] public string Batter { get; init; } = string.Empty;
    [DisplayName("결과")] public string Result { get; init; } = string.Empty;
    [DisplayName("투구 수")] public int Pitches { get; init; }
    [DisplayName("아웃 전")] public int? OutsBefore { get; init; }
    [DisplayName("주자 전")] public string RunnersBefore { get; init; } = string.Empty;
    [DisplayName("득점")] public int RunsScored { get; init; }
    [DisplayName("WPA")] public double? Wpa { get; init; }
    [DisplayName("GameId")] public string GameId { get; init; } = string.Empty;
}

public sealed class PlayerPitchLogRow
{
    [DisplayName("Date")] public string Date { get; init; } = string.Empty;
    [DisplayName("관점")] public string Perspective { get; init; } = string.Empty;
    [DisplayName("상대")] public string Opponent { get; init; } = string.Empty;
    [DisplayName("이닝")] public string Inning { get; init; } = string.Empty;
    [DisplayName("타자")] public string Batter { get; init; } = string.Empty;
    [DisplayName("투수")] public string Pitcher { get; init; } = string.Empty;
    [DisplayName("구수")] public int? PitchNumber { get; init; }
    [DisplayName("카운트")] public string Count { get; init; } = string.Empty;
    [DisplayName("구종")] public string PitchType { get; init; } = string.Empty;
    [DisplayName("구속")] public double? SpeedKmh { get; init; }
    [DisplayName("결과")] public string Result { get; init; } = string.Empty;
    [DisplayName("Plate X")] public double? PlateX { get; init; }
    [DisplayName("Plate Z")] public double? PlateZ { get; init; }
    [DisplayName("존")] public string Zone { get; init; } = string.Empty;
    [DisplayName("GameId")] public string GameId { get; init; } = string.Empty;
}

public sealed class RollingMetricPoint
{
    [DisplayName("Date")] public string Date { get; init; } = string.Empty;
    [DisplayName("Window")] public int WindowGames { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("wRC+")] public double? WrcPlus { get; init; }
}


public sealed class PlayerOpponentSplitRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("관점")] public string Perspective { get; init; } = string.Empty;
    [DisplayName("상대")] public string Opponent { get; init; } = string.Empty;
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
}

public sealed class PlayerSituationSplitRow
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
}

public sealed class PlayerPitchTypeBattingRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("구종")] public string PitchType { get; init; } = string.Empty;
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
}

public sealed class PlayerPitchTypePitchingRow
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
}

public sealed class PlayerPageData
{
    public PlayerProfile Profile { get; init; } = new();
    public IReadOnlyList<PlayerBattingSeasonRow> BattingSeasons { get; init; } = Array.Empty<PlayerBattingSeasonRow>();
    public IReadOnlyList<PlayerPitchingSeasonRow> PitchingSeasons { get; init; } = Array.Empty<PlayerPitchingSeasonRow>();
    public IReadOnlyList<PlayerValueSeasonRow> ValueSeasons { get; init; } = Array.Empty<PlayerValueSeasonRow>();
    public IReadOnlyList<PlayerGameLogRow> GameLogs { get; init; } = Array.Empty<PlayerGameLogRow>();
    public IReadOnlyList<PlayerPlateAppearanceRow> PlateAppearances { get; init; } = Array.Empty<PlayerPlateAppearanceRow>();
    public IReadOnlyList<PlayerPitchLogRow> Pitches { get; init; } = Array.Empty<PlayerPitchLogRow>();
    public IReadOnlyList<PlayerOpponentSplitRow> OpponentSplits { get; init; } = Array.Empty<PlayerOpponentSplitRow>();
    public IReadOnlyList<PlayerSituationSplitRow> SituationSplits { get; init; } = Array.Empty<PlayerSituationSplitRow>();
    public IReadOnlyList<PlayerPitchTypeBattingRow> BattingByPitchType { get; init; } = Array.Empty<PlayerPitchTypeBattingRow>();
    public IReadOnlyList<PlayerPitchTypePitchingRow> PitchingByPitchType { get; init; } = Array.Empty<PlayerPitchTypePitchingRow>();
    public bool HasBatting { get; init; }
    public bool HasPitching { get; init; }
    public IReadOnlyDictionary<int, IReadOnlyList<RollingMetricPoint>> RollingWrcPlus { get; init; }
        = new Dictionary<int, IReadOnlyList<RollingMetricPoint>>();
    public string FormulaDocumentation { get; init; } = string.Empty;
}
