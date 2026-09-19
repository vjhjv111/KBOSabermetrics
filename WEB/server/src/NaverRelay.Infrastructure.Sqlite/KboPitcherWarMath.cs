namespace NaverRelay.Infrastructure.Sqlite;

internal static class KboPitcherWarMath
{
    public const double DefaultReplacementWinningPercentage = 0.294;
    public const double DefaultPitcherWarShare = 0.43;
    public const double DefaultBlendFipWeight = 0.70;
    public const double DefaultBlendRa9Weight = 0.30;

    // FanGraphs 원 공식의 고정 대체수준(경기당 승수): 구원 0.03승, 선발 0.12승.
    // (지금 KBO 사이트 공식은 대신 최근 시즌 표본에서 경험적으로 추정한 선발/구원별
    // 대체수준을 쓰지만, "팬그래프 공식 그대로" 비교용 WAR은 이 고정값을 씁니다.)
    public const double FanGraphsReplacementRelieverWinsPerGame = 0.03;
    public const double FanGraphsReplacementStarterWinsPerGame = 0.12;

    // 지금 공식은 그대로 두고 대체선수 승률 기준만 낮춘 비교용 WAR에 쓰는 값.
    public const double LoweredReplacementWinningPercentage = 0.275;

    public static double DynamicRunsPerWin(
        double leagueRunRate,
        double pitcherRunRate,
        double innings,
        int games)
    {
        if (innings <= 0 || games <= 0) return Math.Max(1.0, (leagueRunRate + 2.0) * 1.5);
        var inningsPerGame = innings / games;
        return ((((18.0 - inningsPerGame) * leagueRunRate + inningsPerGame * pitcherRunRate) / 18.0) + 2.0) * 1.5;
    }

    public static double LeverageMultiplier(double gmLi, bool isReliever) =>
        isReliever ? (1.0 + Math.Clamp(gmLi, 0.1, 5.0)) / 2.0 : 1.0;

    public static double WinsAboveAverage(
        double leagueRunRate,
        double pitcherRunRate,
        double innings,
        double runsPerWin,
        double leverageMultiplier = 1.0)
    {
        if (innings <= 0 || runsPerWin <= 0) return 0.0;
        return (leagueRunRate - pitcherRunRate) / runsPerWin * (innings / 9.0) * leverageMultiplier;
    }

    public static double ReplacementWins(
        double replacementRunRate,
        double leagueRunRate,
        double innings,
        double runsPerWin,
        double leverageMultiplier = 1.0)
    {
        if (innings <= 0 || runsPerWin <= 0) return 0.0;
        return (replacementRunRate - leagueRunRate) / runsPerWin * (innings / 9.0) * leverageMultiplier;
    }

    public static double ParkAdjust(double runRate, double parkFactor)
    {
        var divisor = Math.Max(0.50, parkFactor / 100.0);
        return runRate / divisor;
    }

    public static double ComputeTotalReplacementWar(
        int leagueGameCount,
        double replacementWinningPercentage = DefaultReplacementWinningPercentage) =>
        Math.Max(0, leagueGameCount) * 2.0 * (0.500 - replacementWinningPercentage);

    public static double ComputeTargetPitcherWar(
        int leagueGameCount,
        double replacementWinningPercentage = DefaultReplacementWinningPercentage,
        double pitcherShare = DefaultPitcherWarShare) =>
        ComputeTotalReplacementWar(leagueGameCount, replacementWinningPercentage) * pitcherShare;

    // FanGraphs 공식의 대체수준: 그 투수 시즌 전체의 선발 비중(GS/G)으로
    // 구원 0.03승/경기와 선발 0.12승/경기를 가중평균합니다.
    public static double FanGraphsReplacementLevelWinsPerGame(double gamesStarted, double games)
    {
        if (games <= 0) return FanGraphsReplacementRelieverWinsPerGame;
        var startShare = Math.Clamp(gamesStarted / games, 0.0, 1.0);
        return FanGraphsReplacementRelieverWinsPerGame * (1.0 - startShare) +
               FanGraphsReplacementStarterWinsPerGame * startShare;
    }
}
