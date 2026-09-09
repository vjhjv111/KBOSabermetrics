using NaverRelay.Parsing;

namespace NaverRelay.Application.Statistics;

public static class DisplayText
{
    public static string TeamSide(TeamSide side) => side switch
    {
        NaverRelay.Parsing.TeamSide.Away => "원정",
        NaverRelay.Parsing.TeamSide.Home => "홈",
        _ => "미상",
    };

    public static string Inning(int? inning, TeamSide side)
    {
        if (!inning.HasValue)
        {
            return "-";
        }

        var half = side switch
        {
            NaverRelay.Parsing.TeamSide.Away => "초",
            NaverRelay.Parsing.TeamSide.Home => "말",
            _ => string.Empty,
        };
        return $"{inning.Value}회{half}";
    }

    public static string BattingResult(BattingResultType value) => value switch
    {
        BattingResultType.Single => "안타",
        BattingResultType.InfieldSingle => "내야안타",
        BattingResultType.BuntSingle => "번트안타",
        BattingResultType.Double => "2루타",
        BattingResultType.Triple => "3루타",
        BattingResultType.HomeRun => "홈런",
        BattingResultType.Walk => "볼넷",
        BattingResultType.IntentionalWalk => "고의4구",
        BattingResultType.HitByPitch => "몸에 맞는 공",
        BattingResultType.Strikeout => "삼진",
        BattingResultType.GroundOut => "땅볼 아웃",
        BattingResultType.FlyOut => "뜬공 아웃",
        BattingResultType.LineOut => "직선타 아웃",
        BattingResultType.InfieldFlyOut => "내야 뜬공",
        BattingResultType.FoulFlyOut => "파울 뜬공",
        BattingResultType.BuntOut => "번트 아웃",
        BattingResultType.SacrificeFly => "희생플라이",
        BattingResultType.SacrificeBunt => "희생번트",
        BattingResultType.GroundedIntoDoublePlay => "병살타",
        BattingResultType.ReachedOnError => "실책 출루",
        BattingResultType.FieldersChoice => "야수선택",
        BattingResultType.OtherOut => "기타 아웃",
        _ => "미분류",
    };

    public static string PitchResult(PitchResultType value) => value switch
    {
        PitchResultType.Ball => "볼",
        PitchResultType.Foul => "파울",
        PitchResultType.InPlay => "인플레이",
        PitchResultType.SwingingStrike => "헛스윙",
        PitchResultType.CalledStrike => "루킹 스트라이크",
        PitchResultType.BuntFoul => "번트 파울",
        _ => "미분류",
    };

    public static string RunnerEvent(RunnerEventType value) => value switch
    {
        RunnerEventType.Advance => "진루",
        RunnerEventType.Scored => "득점",
        RunnerEventType.ForceOut => "포스 아웃",
        RunnerEventType.TagOut => "태그 아웃",
        RunnerEventType.CaughtStealing => "도루 실패",
        RunnerEventType.Pickoff => "견제사",
        _ => "미분류",
    };

    public static string RunnerReason(RunnerAdvanceReason value) => value switch
    {
        RunnerAdvanceReason.BatterPlay => "타구",
        RunnerAdvanceReason.StolenBase => "도루",
        RunnerAdvanceReason.CaughtStealing => "도루 실패",
        RunnerAdvanceReason.WildPitch => "폭투",
        RunnerAdvanceReason.Error => "실책",
        RunnerAdvanceReason.OtherRunnerPlay => "다른 주자 플레이",
        RunnerAdvanceReason.Pickoff => "견제",
        RunnerAdvanceReason.Obstruction => "주루 방해",
        RunnerAdvanceReason.BaserunningPlay => "주루 플레이",
        RunnerAdvanceReason.ForceOut => "포스 아웃",
        RunnerAdvanceReason.TagOut => "태그 아웃",
        _ => "미분류",
    };

    public static string Administrative(AdministrativeEventType value) => value switch
    {
        AdministrativeEventType.MoundVisitByCoach => "코치 마운드 방문",
        AdministrativeEventType.MoundVisitByCatcher => "포수 마운드 방문",
        AdministrativeEventType.PitcherDisengagement => "투수 이탈 제한",
        AdministrativeEventType.PitchClockViolation => "피치클락 위반",
        AdministrativeEventType.VideoReview => "비디오 판독",
        AdministrativeEventType.Ejection => "퇴장",
        _ => "미분류",
    };

    public static string Severity(DiagnosticSeverity value) => value switch
    {
        DiagnosticSeverity.Info => "정보",
        DiagnosticSeverity.Warning => "경고",
        DiagnosticSeverity.Error => "오류",
        _ => value.ToString(),
    };

    public static string Base(int? value) => value switch
    {
        0 => "홈",
        1 => "1루",
        2 => "2루",
        3 => "3루",
        4 => "홈",
        _ => "-",
    };

    public static string YesNo(bool value) => value ? "예" : "아니오";

    public static string NullableYesNo(bool? value) => value switch
    {
        true => "예",
        false => "아니오",
        _ => "-",
    };

    public static string Rate(double? value) => value.HasValue ? value.Value.ToString("0.000") : "-";
}
