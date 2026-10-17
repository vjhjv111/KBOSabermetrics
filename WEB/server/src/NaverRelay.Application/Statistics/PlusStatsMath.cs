namespace NaverRelay.Application.Statistics;

public static class PlusStatsMath
{
    public static double? Rate(double numerator, double denominator) =>
        denominator > 0 && double.IsFinite(numerator) && double.IsFinite(denominator)
            ? numerator / denominator : null;

    public static double? Index(double? value, double? leagueRate) =>
        value.HasValue && double.IsFinite(value.Value) && leagueRate.HasValue &&
        double.IsFinite(leagueRate.Value) && leagueRate.Value > 0
            ? 100 * value.Value / leagueRate.Value : null;

    // PF is the full park effect, weighted over actual innings (including road parks).
    // Do not halve it again as one would when using only the pitcher's home park.
    public static double? EraMinus(double? era, double? leagueEra, double? exposureFactor) =>
        exposureFactor is > 0 and < 200
            ? Index(era * (2 - exposureFactor.Value / 100), leagueEra) : null;
}

public sealed class PlusStatsReference
{
    public double? AVG { get; set; }
    public double? OBP { get; set; }
    public double? SLG { get; set; }
    public double? ISO { get; set; }
    public double? BABIP { get; set; }
    public double? BB { get; set; }
    public double? K { get; set; }
    public double? ERA { get; set; }
}
