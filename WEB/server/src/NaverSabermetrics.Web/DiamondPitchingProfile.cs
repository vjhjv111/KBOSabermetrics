namespace NaverSabermetrics.Web;

/// <summary>A bounded, private match snapshot. It is not part of the public season roster or poll view.</summary>
public sealed class DiamondPitchingProfile
{
    public int Season { get; init; }
    public string PitcherId { get; init; } = "";
    public string Revision { get; init; } = "";
    public string Source { get; init; } = "default";
    public int TotalPitchCount { get; init; }
    public int MeasuredPitchCount { get; init; }
    public int UsablePitchCount { get; init; }
    public int HitByPitchCount { get; init; }
    public double? HitByPitchRate { get; init; }
    public double GridSize { get; init; }
    public IReadOnlyList<DiamondPitchLocationBin> Bins { get; init; } = [];
    public string? SampleNote { get; init; }
}

/// <param name="X">Plate X divided by 8.5/12 feet. Positive is the left-handed batter's side.</param>
/// <param name="Y">2*(plate Z - zone bottom)/(zone top - zone bottom)-1. The zone is -1 through +1.</param>
/// <param name="Balls">Pre-pitch balls, or -1 when unavailable.</param>
/// <param name="Strikes">Pre-pitch strikes, or -1 when unavailable.</param>
public sealed record DiamondPitchLocationBin(
    string Type, double X, double Y, double Velocity, string? BatterHand,
    int Balls, int Strikes, int Count, int HitByPitchCount);
