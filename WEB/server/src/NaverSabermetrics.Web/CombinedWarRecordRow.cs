using System.ComponentModel;

namespace NaverSabermetrics.Web;

public sealed class CombinedWarRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("야수 WAR")] public double? BatterWar { get; init; }
    [DisplayName("투수 WAR")] public double? PitcherWar { get; init; }
    [DisplayName("종합 WAR")] public double? TotalWar { get; init; }
}
