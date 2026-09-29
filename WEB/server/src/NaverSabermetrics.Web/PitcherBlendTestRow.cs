using System.ComponentModel;
using NaverRelay.Application.Statistics;

namespace NaverSabermetrics.Web;

// Local comparison uses the existing KBO fWAR and RA9-WAR components, not a
// blend of FIP/RA9 rates. Missing components must never become zero WAR.
public sealed class PitcherBlendTestRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("IP")] public double? InningsPitched { get; init; }
    [DisplayName("대체선수 승률")] public double ReplacementWinningPercentage => 0.294;
    [DisplayName("KBO FIP WAR")] public double? FipWar { get; init; }
    [DisplayName("KBO RA9 WAR")] public double? Ra9War { get; init; }
    [DisplayName("혼합 WAR")] public double? BlendWar { get; init; }

    public static PitcherBlendTestRow From(PitcherValueGridRow row, double fipWeight) => new()
    {
        Pcode = row.Pcode, Name = row.Name, TeamCode = row.TeamCode,
        Games = row.Games, InningsPitched = row.InningsPitched,
        FipWar = row.War, Ra9War = row.Ra9War,
        BlendWar = row.War.HasValue && row.Ra9War.HasValue
            ? row.War.Value * fipWeight + row.Ra9War.Value * (1.0 - fipWeight)
            : null,
    };
}
