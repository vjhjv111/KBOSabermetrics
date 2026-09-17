using System.ComponentModel;
using NaverRelay.Parsing;

namespace NaverRelay.Application.Statistics;

public sealed class GameGridRow
{
    [DisplayName("경기 ID")] public string GameId { get; init; } = string.Empty;
    [DisplayName("시즌")] public int? SeasonYear { get; init; }
    [DisplayName("경기 구분")] public string CompetitionType { get; init; } = string.Empty;
    [DisplayName("카테고리 ID")] public string? CategoryId { get; init; }
    [DisplayName("카테고리명")] public string? CategoryName { get; init; }
    [DisplayName("라운드 코드")] public string? RoundCode { get; init; }
    [DisplayName("정규시즌")] public string IsRegularSeason { get; init; } = string.Empty;
    [DisplayName("일자")] public string? GameDate { get; init; }
    [DisplayName("원정팀")] public string? AwayTeam { get; init; }
    [DisplayName("원정 점수")] public int? AwayScore { get; init; }
    [DisplayName("홈 점수")] public int? HomeScore { get; init; }
    [DisplayName("홈팀")] public string? HomeTeam { get; init; }
    [DisplayName("구장")] public string? Stadium { get; init; }
    [DisplayName("완료 타석")] public int PlateAppearances { get; init; }
    [DisplayName("투구")] public int Pitches { get; init; }
    [DisplayName("PTS 누락")] public int MissingPts { get; init; }
    [DisplayName("주루 이벤트")] public int RunnerEvents { get; init; }
    [DisplayName("선수 교체")] public int PlayerChanges { get; init; }
    [DisplayName("경고")] public int Warnings { get; init; }
    [DisplayName("오류")] public int Errors { get; init; }
}

public sealed class PlateAppearanceGridRow
{
    [DisplayName("경기 ID")] public string GameId { get; init; } = string.Empty;
    [DisplayName("순번")] public int Sequence { get; init; }
    [DisplayName("공식 순번")] public int? OfficialSequence { get; init; }
    [DisplayName("이닝")] public string Inning { get; init; } = string.Empty;
    [DisplayName("공격팀")] public string? TeamCode { get; init; }
    [DisplayName("타자 코드")] public string? BatterPcode { get; init; }
    [DisplayName("타자")] public string? Batter { get; init; }
    [DisplayName("투수 코드")] public string? PitcherPcode { get; init; }
    [DisplayName("투수")] public string? Pitcher { get; init; }
    [DisplayName("결과")] public string Outcome { get; init; } = string.Empty;
    [DisplayName("원문")] public string? ResultText { get; init; }
    [DisplayName("공식 PA")] public string IsOfficial { get; init; } = string.Empty;
    [DisplayName("타수")] public string CountsAsAtBat { get; init; } = string.Empty;
    [DisplayName("득점")] public int Runs { get; init; }
    [DisplayName("아웃")] public int Outs { get; init; }
    [DisplayName("실투구 수")] public int Pitches { get; init; }
    [DisplayName("WPA")] public double? Wpa { get; init; }
}

public sealed class PitchGridRow
{
    [DisplayName("경기 ID")] public string GameId { get; init; } = string.Empty;
    [DisplayName("이닝")] public string Inning { get; init; } = string.Empty;
    [DisplayName("실제 순번")] public int ActualPitchIndex { get; init; }
    [DisplayName("표시 순번")] public int? DisplayPitchNumber { get; init; }
    [DisplayName("투수 코드")] public string? PitcherPcode { get; init; }
    [DisplayName("투수")] public string? Pitcher { get; init; }
    [DisplayName("타자 코드")] public string? BatterPcode { get; init; }
    [DisplayName("타자")] public string? Batter { get; init; }
    [DisplayName("카운트 전")] public string CountBefore { get; init; } = string.Empty;
    [DisplayName("결과")] public string Result { get; init; } = string.Empty;
    [DisplayName("구종")] public string? PitchType { get; init; }
    [DisplayName("구속(km/h)")] public double? SpeedKmh { get; init; }
    [DisplayName("Plate X")] public double? PlateX { get; init; }
    [DisplayName("Plate Z")] public double? PlateZ { get; init; }
    [DisplayName("존 내부")] public string InZone { get; init; } = string.Empty;
    [DisplayName("PTS")] public string HasPts { get; init; } = string.Empty;
    [DisplayName("스윙")] public string IsSwing { get; init; } = string.Empty;
    [DisplayName("헛스윙")] public string IsWhiff { get; init; } = string.Empty;
    [DisplayName("인플레이")] public string IsInPlay { get; init; } = string.Empty;
}

public sealed class RunnerGridRow
{
    [DisplayName("경기 ID")] public string GameId { get; init; } = string.Empty;
    [DisplayName("이닝")] public string Inning { get; init; } = string.Empty;
    [DisplayName("팀")] public string? TeamCode { get; init; }
    [DisplayName("주자 코드")] public string? RunnerPcode { get; init; }
    [DisplayName("주자")] public string? Runner { get; init; }
    [DisplayName("출발")] public string FromBase { get; init; } = string.Empty;
    [DisplayName("도착")] public string ToBase { get; init; } = string.Empty;
    [DisplayName("이벤트")] public string EventType { get; init; } = string.Empty;
    [DisplayName("사유")] public string Reason { get; init; } = string.Empty;
    [DisplayName("아웃")] public string IsOut { get; init; } = string.Empty;
    [DisplayName("득점")] public string IsRun { get; init; } = string.Empty;
    [DisplayName("해석 성공")] public string WasParsed { get; init; } = string.Empty;
    [DisplayName("원문")] public string? RawText { get; init; }
}

public sealed class PlayerChangeGridRow
{
    [DisplayName("경기 ID")] public string GameId { get; init; } = string.Empty;
    [DisplayName("이닝")] public string Inning { get; init; } = string.Empty;
    [DisplayName("팀")] public string? TeamCode { get; init; }
    [DisplayName("변경 유형")] public string ChangeType { get; init; } = string.Empty;
    [DisplayName("교체 아웃")] public string? OutPlayer { get; init; }
    [DisplayName("기존 위치")] public string? OutPosition { get; init; }
    [DisplayName("교체 인")] public string? InPlayer { get; init; }
    [DisplayName("새 위치")] public string? InPosition { get; init; }
    [DisplayName("타순")] public int? BatOrder { get; init; }
    [DisplayName("투수 교체")] public string IsPitcherChange { get; init; } = string.Empty;
    [DisplayName("대타")] public string IsPinchHitter { get; init; } = string.Empty;
    [DisplayName("대주자")] public string IsPinchRunner { get; init; } = string.Empty;
    [DisplayName("해석 성공")] public string WasParsed { get; init; } = string.Empty;
    [DisplayName("원문")] public string? RawText { get; init; }
}

public sealed class AdministrativeGridRow
{
    [DisplayName("경기 ID")] public string GameId { get; init; } = string.Empty;
    [DisplayName("이닝")] public string Inning { get; init; } = string.Empty;
    [DisplayName("공격 구분")] public string BattingSide { get; init; } = string.Empty;
    [DisplayName("유형")] public string EventType { get; init; } = string.Empty;
    [DisplayName("자동 볼")] public int AutomaticBallDelta { get; init; }
    [DisplayName("자동 스트라이크")] public int AutomaticStrikeDelta { get; init; }
    [DisplayName("판독 번복")] public string ReviewOverturned { get; init; } = string.Empty;
    [DisplayName("원문")] public string? RawText { get; init; }
}

public sealed class DiagnosticGridRow
{
    [DisplayName("등급")] public string Severity { get; init; } = string.Empty;
    [DisplayName("경기 ID")] public string? GameId { get; init; }
    [DisplayName("코드")] public string Code { get; init; } = string.Empty;
    [DisplayName("내용")] public string Message { get; init; } = string.Empty;
    [DisplayName("릴레이 번호")] public int? RelayNo { get; init; }
    [DisplayName("SeqNo")] public int? SeqNo { get; init; }
    [DisplayName("이벤트 ID")] public string? EventId { get; init; }
}

public sealed class BatterSummaryGridRow
{
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("선수")] public string? Name { get; init; }
    [DisplayName("팀")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("PA")] public int PA { get; init; }
    [DisplayName("AB")] public int AB { get; init; }
    [DisplayName("R")] public int Runs { get; init; }
    [DisplayName("H")] public int H { get; init; }
    [DisplayName("1B")] public int Singles { get; init; }
    [DisplayName("2B")] public int Doubles { get; init; }
    [DisplayName("3B")] public int Triples { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("RBI")] public int RBI { get; init; }
    [DisplayName("SB")] public int StolenBases { get; init; }
    [DisplayName("CS")] public int CaughtStealing { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("IBB")] public int IntentionalWalks { get; init; }
    [DisplayName("HBP")] public int HitByPitch { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("SF")] public int SacrificeFlies { get; init; }
    [DisplayName("SH")] public int SacrificeBunts { get; init; }
    [DisplayName("GDP")] public int DoublePlays { get; init; }
    [DisplayName("병살 상황")] public int DoublePlayOpportunities { get; set; }
    [DisplayName("희생번트 실패")] public int SacrificeBuntFailures { get; set; }
    [Browsable(false)] public int? LeftOnBase { get; set; }
    [DisplayName("TB")] public int TotalBases { get; init; }
    [DisplayName("AVG")] public double? AVG { get; init; }
    [DisplayName("OBP")] public double? OBP { get; init; }
    [DisplayName("SLG")] public double? SLG { get; init; }
    [DisplayName("OPS")] public double? OPS { get; init; }
    [DisplayName("BABIP")] public double? BABIP { get; init; }
    [DisplayName("BB%")] public double? WalkRate { get; init; }
    [DisplayName("K%")] public double? StrikeoutRate { get; init; }
    [DisplayName("FB")] public int FlyBalls { get; init; }
    [DisplayName("WPA")] public double Wpa { get; init; }
    [DisplayName("투구 수")] public int Pitches { get; init; }
    [DisplayName("Swing")] public int Swings { get; init; }
    [DisplayName("Contact")] public int Contacts { get; init; }
    [DisplayName("Whiff")] public int Whiffs { get; init; }
    [DisplayName("Called Strike")] public int CalledStrikes { get; init; }
    [DisplayName("CSW")] public int Csw { get; init; }
    [DisplayName("Zone")] public int InZone { get; init; }
    [DisplayName("Out Zone")] public int OutZone { get; init; }
    [DisplayName("Z-Swing")] public int ZoneSwings { get; init; }
    [DisplayName("O-Swing")] public int ChaseSwings { get; init; }
    [DisplayName("Z-Contact")] public int ZoneContacts { get; init; }
    [DisplayName("O-Contact")] public int OutZoneContacts { get; init; }
    [DisplayName("초구")] public int FirstPitches { get; init; }
    [DisplayName("초구 Swing")] public int FirstPitchSwings { get; init; }
}

public sealed class PitcherSummaryGridRow
{
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("투수")] public string? Name { get; init; }
    [DisplayName("팀")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("TBF")] public int BattersFaced { get; init; }
    [DisplayName("투구 수")] public int PitchCount { get; init; }
    [DisplayName("H")] public int Hits { get; init; }
    [DisplayName("HR")] public int HomeRuns { get; init; }
    [DisplayName("BB")] public int Walks { get; init; }
    [DisplayName("HBP")] public int HitBatters { get; init; }
    [Browsable(false)] public int OpponentAtBats { get; init; }
    [Browsable(false)] public int TotalBasesAllowed { get; init; }
    [Browsable(false)] public int SacrificeFlies { get; init; }
    [DisplayName("SO")] public int Strikeouts { get; init; }
    [DisplayName("Swing")] public int Swings { get; init; }
    [DisplayName("Whiff")] public int Whiffs { get; init; }
    [DisplayName("CSW")] public int CswCount { get; init; }
    [DisplayName("평균 구속")] public double? AverageSpeed { get; init; }
    [DisplayName("K%")] public double? StrikeoutRate { get; init; }
    [DisplayName("BB%")] public double? WalkRate { get; init; }
    [DisplayName("Whiff%")] public double? WhiffRate { get; init; }
    [DisplayName("CSW%")] public double? CswRate { get; init; }
}

public static class GridRowFactory
{
    public static IReadOnlyList<GameGridRow> Games(IEnumerable<NormalizedGame> games) => games
        .Select(game => new GameGridRow
        {
            GameId = game.GameId,
            SeasonYear = game.SeasonYear,
            CompetitionType = CompetitionTypeText(game.CompetitionType),
            CategoryId = game.CategoryId,
            CategoryName = game.CategoryName,
            RoundCode = game.RoundCode,
            IsRegularSeason = DisplayText.YesNo(game.IsRegularSeason),
            GameDate = game.GameDate,
            AwayTeam = game.AwayTeam.TeamName ?? game.AwayTeam.TeamCode,
            AwayScore = game.AwayTeam.FinalScore,
            HomeScore = game.HomeTeam.FinalScore,
            HomeTeam = game.HomeTeam.TeamName ?? game.HomeTeam.TeamCode,
            Stadium = game.Stadium,
            PlateAppearances = game.Summary.CompletedPlateAppearanceCount,
            Pitches = game.Summary.PitchEventCount,
            MissingPts = game.Summary.PtsMissingPitchCount,
            RunnerEvents = game.Summary.RunnerEventCount,
            PlayerChanges = game.Summary.PlayerChangeEventCount,
            Warnings = game.Summary.WarningCount,
            Errors = game.Summary.ErrorCount,
        })
        .ToList();

    private static string CompetitionTypeText(GameCompetitionType type) => type switch
    {
        GameCompetitionType.RegularSeason => "정규시즌",
        GameCompetitionType.Preseason => "시범경기",
        GameCompetitionType.Postseason => "포스트시즌",
        GameCompetitionType.AllStar => "올스타전",
        GameCompetitionType.Futures => "퓨처스리그",
        _ => "기타",
    };

    public static IReadOnlyList<PlateAppearanceGridRow> PlateAppearances(IEnumerable<NormalizedGame> games) => games
        .SelectMany(game => game.PlateAppearances)
        .Select(pa => new PlateAppearanceGridRow
        {
            GameId = pa.GameId,
            Sequence = pa.SequenceNumber,
            OfficialSequence = pa.OfficialSequenceNumber,
            Inning = DisplayText.Inning(pa.Inning, pa.BattingSide),
            TeamCode = pa.BattingTeamCode,
            BatterPcode = pa.BatterPcode,
            Batter = pa.BatterName,
            PitcherPcode = pa.PitcherPcode,
            Pitcher = pa.PitcherName,
            Outcome = DisplayText.BattingResult(pa.Outcome.ResultType),
            ResultText = pa.ResultText,
            IsOfficial = DisplayText.YesNo(pa.IsOfficialPlateAppearance),
            CountsAsAtBat = DisplayText.YesNo(pa.Outcome.CountsAsAtBat),
            Runs = pa.RunsScored,
            Outs = pa.OutsRecorded,
            Pitches = pa.ActualPitchCount,
            Wpa = pa.WpaByPlate,
        })
        .ToList();

    public static IReadOnlyList<PitchGridRow> Pitches(IEnumerable<NormalizedGame> games) => games
        .SelectMany(game => game.PitchEvents)
        .Select(pitch => new PitchGridRow
        {
            GameId = pitch.GameId,
            Inning = DisplayText.Inning(pitch.Inning, pitch.BattingSide),
            ActualPitchIndex = pitch.ActualPitchIndex,
            DisplayPitchNumber = pitch.DisplayPitchNumber,
            PitcherPcode = pitch.PitcherPcode,
            Pitcher = pitch.PitcherName,
            BatterPcode = pitch.BatterPcode,
            Batter = pitch.BatterName,
            CountBefore = $"{pitch.BallsBefore?.ToString() ?? "-"}-{pitch.StrikesBefore?.ToString() ?? "-"}",
            Result = DisplayText.PitchResult(pitch.PitchResult),
            PitchType = pitch.PitchType,
            SpeedKmh = pitch.SpeedKmh,
            PlateX = pitch.CalculatedCrossPlateX ?? pitch.CrossPlateX,
            PlateZ = pitch.CalculatedCrossPlateZ,
            InZone = DisplayText.NullableYesNo(pitch.IsInNominalStrikeZone),
            HasPts = DisplayText.YesNo(pitch.HasPtsTracking),
            IsSwing = DisplayText.YesNo(pitch.IsSwing),
            IsWhiff = DisplayText.YesNo(pitch.IsWhiff),
            IsInPlay = DisplayText.YesNo(pitch.IsInPlay),
        })
        .ToList();

    public static IReadOnlyList<RunnerGridRow> Runners(IEnumerable<NormalizedGame> games) => games
        .SelectMany(game => game.RunnerEvents)
        .Select(runner => new RunnerGridRow
        {
            GameId = runner.GameId,
            Inning = DisplayText.Inning(runner.Inning, runner.BattingSide),
            TeamCode = runner.TeamCode,
            RunnerPcode = runner.RunnerPcode,
            Runner = runner.RunnerName,
            FromBase = DisplayText.Base(runner.FromBase),
            ToBase = DisplayText.Base(runner.ToBase),
            EventType = DisplayText.RunnerEvent(runner.EventType),
            Reason = DisplayText.RunnerReason(runner.Reason),
            IsOut = DisplayText.YesNo(runner.IsOut),
            IsRun = DisplayText.YesNo(runner.IsRun),
            WasParsed = DisplayText.YesNo(runner.WasParsed),
            RawText = runner.RawText,
        })
        .ToList();

    public static IReadOnlyList<PlayerChangeGridRow> PlayerChanges(IEnumerable<NormalizedGame> games) => games
        .SelectMany(game => game.PlayerChanges)
        .Select(change => new PlayerChangeGridRow
        {
            GameId = change.GameId,
            Inning = DisplayText.Inning(change.Inning, change.BattingSide),
            TeamCode = change.TeamCode,
            ChangeType = change.ChangeType.ToString(),
            OutPlayer = change.OutPlayerName,
            OutPosition = change.OutPosition,
            InPlayer = change.InPlayerName,
            InPosition = change.InPosition,
            BatOrder = change.BatOrder,
            IsPitcherChange = DisplayText.YesNo(change.IsPitcherChange),
            IsPinchHitter = DisplayText.YesNo(change.IsPinchHitter),
            IsPinchRunner = DisplayText.YesNo(change.IsPinchRunner),
            WasParsed = DisplayText.YesNo(change.WasParsed),
            RawText = change.RawText,
        })
        .ToList();

    public static IReadOnlyList<AdministrativeGridRow> AdministrativeEvents(IEnumerable<NormalizedGame> games) => games
        .SelectMany(game => game.AdministrativeEvents)
        .Select(item => new AdministrativeGridRow
        {
            GameId = item.GameId,
            Inning = DisplayText.Inning(item.Inning, item.BattingSide),
            BattingSide = DisplayText.TeamSide(item.BattingSide),
            EventType = DisplayText.Administrative(item.EventType),
            AutomaticBallDelta = item.AutomaticBallDelta,
            AutomaticStrikeDelta = item.AutomaticStrikeDelta,
            ReviewOverturned = DisplayText.NullableYesNo(item.ReviewOverturned),
            RawText = item.RawText,
        })
        .ToList();

    public static IReadOnlyList<DiagnosticGridRow> Diagnostics(IEnumerable<NormalizedGame> games) => games
        .SelectMany(game => game.Diagnostics)
        .Select(item => new DiagnosticGridRow
        {
            Severity = DisplayText.Severity(item.Severity),
            GameId = item.GameId,
            Code = item.Code,
            Message = item.Message,
            RelayNo = item.SourceRelayNo,
            SeqNo = item.SourceSeqNo,
            EventId = item.EventId,
        })
        .ToList();
}
