using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaverSabermetrics.Web;

public sealed record DiamondVec(double X, double Y);
public sealed record DiamondPosition(double X, double Y, double Z);
public sealed record DiamondBodyHit(double At, DiamondPosition Position);
public sealed record DiamondCapsule(DiamondPosition A, DiamondPosition B, double Radius);
public sealed record DiamondSwing(double At, DiamondVec Aim);
public sealed record DiamondContact(double At, DiamondPosition Position);
public sealed record DiamondArsenal(string Type, double Velocity, double Usage);

public sealed class DiamondPitch
{
    public int Id { get; set; }
    public string Type { get; set; } = "fastball";
    public double Velocity { get; set; }
    public double ReleaseAt { get; set; }
    public double FlightMs { get; set; }
    public double? ReleaseX { get; set; }
    public double? ReleaseY { get; set; }
    public double? ReleaseZ { get; set; }
    public DiamondVec Target { get; set; } = new(0, 0);
    public double BreakX { get; set; }
    public double BreakY { get; set; }
    public double Quality { get; set; }
    public bool Resolved { get; set; }
    public DiamondResult? Reaction { get; set; }
    public DiamondSwing? AiBatterSwing { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AiBatterSwingPrepared { get; set; }
    public DiamondBodyHit? BodyHit { get; set; }
}

public sealed class DiamondResult
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "ball";
    public string Outcome { get; set; } = "BALL";
    public double? Timing { get; set; }
    public double? AimError { get; set; }
    public double Quality { get; set; }
    public double Distance { get; set; }
    public double ExitSpeed { get; set; }
    public double LaunchAngle { get; set; }
    public double Direction { get; set; }
    public int Points { get; set; }
    public bool PlateEnded { get; set; }
    public double At { get; set; }
    public double? SwingAt { get; set; }
    public DiamondVec? SwingAim { get; set; }
    public DiamondVec? PlateLocation { get; set; }
    public DiamondBodyHit? BodyHit { get; set; }
    public DiamondContact? Contact { get; set; }
    public string? Trajectory { get; set; }
}

public sealed class DiamondGame
{
    public string Format { get; set; } = "action-v2";
    public string Code { get; set; } = "";
    public string Mode { get; set; } = "ai";
    public string Host { get; set; } = "";
    public string? Guest { get; set; }
    public string HostRole { get; set; } = "batter";
    public string Batter { get; set; } = "";
    public string Pitcher { get; set; } = "";
    public DiamondRosterSelection? Roster { get; set; }
    public string Pace { get; set; } = "practice";
    public int Round { get; set; }
    public int Balls { get; set; }
    public int Strikes { get; set; }
    public int Score { get; set; }
    public int PitchCount { get; set; }
    public DiamondPitch? Pitch { get; set; }
    public List<DiamondResult> History { get; set; } = [];
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
}

public sealed record DiamondView(string Format, string Code, string Mode, string Role, string Batter,
    string Pitcher, string Pace, int Round, int Balls, int Strikes, int Score, int PitchCount,
    DiamondPitch? Pitch, IReadOnlyList<DiamondResult> History, bool Waiting, bool Done,
    string? Winner, long ServerNow, long ExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DiamondRosterSelection? Roster = null);

public sealed class DiamondInputError(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

public static class DiamondJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
