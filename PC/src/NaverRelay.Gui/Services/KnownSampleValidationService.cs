using NaverRelay.Parsing;

namespace NaverRelay.Gui.Services;

internal static class KnownSampleValidationService
{
    private static readonly HashSet<string> ExpectedGameIds = new(StringComparer.Ordinal)
    {
        "20260626HTOB02026",
        "20260626KTSS02026",
        "20260626LGLT02026",
        "20260626WONC02026",
        "20260627HHSK02026",
        "20260627HTOB02026",
        "20260627KTSS02026",
    };

    public static bool IsKnownSample(IReadOnlyCollection<NormalizedGame> games)
    {
        return games.Count == ExpectedGameIds.Count
               && games.Select(game => game.GameId).ToHashSet(StringComparer.Ordinal).SetEquals(ExpectedGameIds);
    }

    public static IReadOnlyList<string> Validate(IReadOnlyCollection<NormalizedGame> games)
    {
        var failures = new List<string>();
        Check(failures, "경기 수", 7, games.Count);
        Check(failures, "릴레이 그룹", 666, games.Sum(game => game.Summary.RawRelayGroupCount));
        Check(failures, "원본 이벤트", 3646, games.Sum(game => game.Summary.RawEventCount));
        Check(failures, "완료 타석", 522, games.Sum(game => game.Summary.CompletedPlateAppearanceCount));
        Check(failures, "중단 타석", 1, games.Sum(game => game.Summary.InterruptedPlateAppearanceCount));
        Check(failures, "실제 투구", 1997, games.Sum(game => game.Summary.PitchEventCount));
        Check(failures, "PTS 연결", 1996, games.Sum(game => game.Summary.PtsMatchedPitchCount));
        Check(failures, "PTS 누락", 1, games.Sum(game => game.Summary.PtsMissingPitchCount));
        Check(failures, "주자 이벤트", 193, games.Sum(game => game.Summary.RunnerEventCount));
        Check(failures, "선수 교체", 120, games.Sum(game => game.Summary.PlayerChangeEventCount));
        Check(failures, "관리 이벤트", 141, games.Sum(game => game.Summary.AdministrativeEventCount));
        Check(failures, "미분류 릴레이", 0, games.Sum(game => game.Summary.UnknownRelayGroupCount));
        Check(failures, "미분류 타격 결과", 0, games.Sum(game => game.Summary.UnknownBattingResultCount));
        Check(failures, "미해석 주자 이벤트", 0, games.Sum(game => game.Summary.UnparsedRunnerEventCount));
        Check(failures, "미해석 선수 교체", 0, games.Sum(game => game.Summary.UnparsedPlayerChangeCount));
        Check(failures, "PTS 계산 실패", 0, games.Sum(game => game.Summary.PtsCalculationFailureCount));
        Check(failures, "최종 타격 라인 불일치", 0, games.Sum(game => game.Summary.FinalLineBattingMismatchCount));
        Check(failures, "파서 오류", 0, games.Sum(game => game.Summary.ErrorCount));
        Check(failures, "파서 경고", 1, games.Sum(game => game.Summary.WarningCount));
        return failures;
    }

    private static void Check(ICollection<string> failures, string label, int expected, int actual)
    {
        if (expected != actual)
        {
            failures.Add($"{label}: 예상 {expected}, 실제 {actual}");
        }
    }
}
