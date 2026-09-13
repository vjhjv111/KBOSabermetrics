namespace NaverSabermetrics.Web;

public sealed class DiamondGameRatings
{
    public int Contact { get; set; } = 58;
    public int Power { get; set; } = 58;
    public int Discipline { get; set; } = 58;
    public int Speed { get; set; } = 58;
    public int Fielding { get; set; } = 58;
    public int Velocity { get; set; } = 58;
    public int Control { get; set; } = 58;
    public int Stamina { get; set; } = 58;
}
public sealed class DiamondCareerAppearance
{
    public string SkinTone { get; set; } = "#c89675";
    public string BodyType { get; set; } = "athletic";
    public int HeightCm { get; set; } = 185;
    public string HairStyle { get; set; } = "short";
    public string HairColor { get; set; } = "#242421";
    public string GloveColor { get; set; } = "#915b31";
    public string CleatColor { get; set; } = "#172027";
    public string BatColor { get; set; } = "#bc8b52";
    public string EquipmentColor { get; set; } = "#27313b";
    public string JerseyNumber { get; set; } = "17";
}
public sealed record DiamondCareerReward(string GameId, int Xp, int Levels, string Summary, long At);
public sealed class DiamondCareerPlayer
{
    public string Id { get; set; } = "";
    public long Version { get; set; }
    public string Name { get; set; } = "";
    public string Team { get; set; } = "";
    public string Position { get; set; } = "CF";
    public string Bats { get; set; } = "R";
    public string Throws { get; set; } = "R";
    public string Delivery { get; set; } = "overhand";
    public string Archetype { get; set; } = "balanced";
    public DiamondCareerAppearance Appearance { get; set; } = new();
    public DiamondGameRatings Ratings { get; set; } = new();
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public int NextLevelXp => 100 + (Level - 1) * 45;
    public int TrainingPoints { get; set; } = 8;
    public int Games { get; set; }
    public DiamondSeasonPlayerStats Stats { get; set; } = new();
    public List<DiamondCareerReward> Rewards { get; set; } = [];
    public List<string> TrainingLog { get; set; } = [];
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}
public sealed record DiamondCareerResponse(DiamondCareerPlayer? Player);
