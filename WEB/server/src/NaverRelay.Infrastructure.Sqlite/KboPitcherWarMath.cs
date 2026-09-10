namespace NaverRelay.Infrastructure.Sqlite;

internal static class KboPitcherWarMath
{
    public const double DefaultReplacementWinningPercentage = 0.294;
    public const double DefaultPitcherWarShare = 0.43;
    public const double DefaultBlendFipWeight = 0.70;
    public const double DefaultBlendRa9Weight = 0.30;

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
}
