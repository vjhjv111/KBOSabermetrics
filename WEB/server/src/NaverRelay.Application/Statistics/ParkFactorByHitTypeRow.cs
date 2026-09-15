using System.ComponentModel;

namespace NaverRelay.Application.Statistics;

/// <summary>
/// Single-season (non-regressed) component park factors by hit type. Unlike KboParkFactorsV2
/// (5-year regressed, used in WAR/wRC+), this is a diagnostic/display-only "이 해 이 구장" view:
/// for each stat, PF = 100 * (stat per game at this park) / (stat per game in the park's primary
/// home club's road games that same season). Never consumed by WAR or wRC+ calculations.
/// </summary>
public sealed class ParkFactorByHitTypeRow
{
    [DisplayName("Year")] public int Year { get; init; }
    [DisplayName("구장")] public string Stadium { get; init; } = string.Empty;
    [DisplayName("경기")] public int Games { get; init; }
    [DisplayName("득점 PF")] public double? RunParkFactor { get; init; }
    [DisplayName("단타 PF")] public double? SingleParkFactor { get; init; }
    [DisplayName("2루타 PF")] public double? DoubleParkFactor { get; init; }
    [DisplayName("3루타 PF")] public double? TripleParkFactor { get; init; }
    [DisplayName("홈런 PF")] public double? HomeRunParkFactor { get; init; }
    [DisplayName("장타 PF")] public double? ExtraBaseHitParkFactor { get; init; }
}
