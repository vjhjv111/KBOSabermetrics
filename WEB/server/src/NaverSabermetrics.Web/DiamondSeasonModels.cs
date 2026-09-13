using System.Text.Json.Serialization;

namespace NaverSabermetrics.Web;

public sealed record DiamondSeasonRosterOverride(DiamondBatter? Batter, DiamondPitcher? Pitcher, string CharacterId, DiamondCareerAppearance? Appearance = null);
public sealed class DiamondSeasonPlayerStats
{
    public string PlayerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Team { get; set; } = "";
    public int PA { get; set; } public int AB { get; set; } public int H { get; set; }
    public int Double { get; set; } public int Triple { get; set; } public int HR { get; set; }
    public int BB { get; set; } public int HBP { get; set; } public int K { get; set; }
    public int R { get; set; } public int RBI { get; set; } public int SF { get; set; } public int GIDP { get; set; }
    public int PitchCount { get; set; } public int OutsPitched { get; set; } public int HitsAllowed { get; set; }
    public int RunsAllowed { get; set; } public int WalksAllowed { get; set; } public int HitBatters { get; set; }
    public int Strikeouts { get; set; }
}
public sealed class DiamondSeasonTeam
{
    public string Code { get; set; } = ""; public string Name { get; set; } = "";
    public List<string> Lineup { get; set; } = [];
    public List<DiamondBatter> Batters { get; set; } = [];
    public List<DiamondPitcher> Pitchers { get; set; } = [];
}
public sealed class DiamondSeasonFixture
{
    public string Id { get; set; } = ""; public int Day { get; set; }
    public string HomeTeam { get; set; } = ""; public string AwayTeam { get; set; } = "";
    public bool Complete { get; set; } public int HomeRuns { get; set; } public int AwayRuns { get; set; }
}
public sealed record DiamondSeasonRunner(string PlayerId, string PitcherId);
public sealed class DiamondSeasonGame
{
    public string Id { get; set; } = "";
    public string HomeTeam { get; set; } = ""; public string AwayTeam { get; set; } = "";
    public int Inning { get; set; } = 1; public string Half { get; set; } = "top"; public int Outs { get; set; }
    public int HomeRuns { get; set; } public int AwayRuns { get; set; }
    public int HomeHits { get; set; } public int AwayHits { get; set; }
    public List<int> HomeLine { get; set; } = []; public List<int> AwayLine { get; set; } = [0];
    public List<string> HomeLineup { get; set; } = []; public List<string> AwayLineup { get; set; } = [];
    public int HomeOrder { get; set; } public int AwayOrder { get; set; }
    public string HomePitcher { get; set; } = ""; public string AwayPitcher { get; set; } = "";
    public List<string> HomeUsedPitchers { get; set; } = []; public List<string> AwayUsedPitchers { get; set; } = [];
    public List<string> HomeUsedBatters { get; set; } = []; public List<string> AwayUsedBatters { get; set; } = [];
    public DiamondSeasonRunner?[] Bases { get; set; } = new DiamondSeasonRunner?[3];
    public bool Complete { get; set; } public string EndReason { get; set; } = "";
    public Dictionary<string, DiamondSeasonPlayerStats> PlayerStats { get; set; } = [];
    // Actual field/plate/base participation, independent of whether a PA was completed.
    public HashSet<string> Participants { get; set; } = [];
    public List<string> Events { get; set; } = [];
    public int PlateAppearances { get; set; } public long? CompletedAt { get; set; }
    // Persisted with the game; excluded from the frontend game object by the view projection.
    public DiamondGame Duel { get; set; } = new();
}
public sealed record DiamondSeasonStanding(string Team, string Name, int Played, int Wins, int Losses, int Ties,
    int RunsFor, int RunsAgainst, double Pct, double GamesBehind);
public sealed record DiamondSeasonSummary(int SeasonNumber, string Champion, int Wins, int Losses, int Ties);
public sealed class DiamondSeasonSave
{
    public string Id { get; set; } = ""; public long Version { get; set; }
    public string Team { get; set; } = ""; public int Season { get; set; } public int SeasonNumber { get; set; } = 1;
    public string AsOf { get; set; } = ""; public string Revision { get; set; } = "";
    public string Pace { get; set; } = "practice"; public int Day { get; set; } = 1;
    public int TotalDays { get; set; } public int SeriesPerPair { get; set; } = 8; public bool Complete { get; set; }
    public List<DiamondSeasonTeam> Teams { get; set; } = [];
    public List<DiamondSeasonFixture> Schedule { get; set; } = [];
    public Dictionary<string, DiamondSeasonPlayerStats> PlayerStats { get; set; } = [];
    public DiamondSeasonGame? Game { get; set; }
    public List<DiamondSeasonSummary> PreviousSeasons { get; set; } = [];
    public long CreatedAt { get; set; } public long UpdatedAt { get; set; }
    public string? CharacterId { get; set; }
    public string? CharacterBatterId { get; set; }
    public string? CharacterPitcherId { get; set; }
    public Dictionary<string, DiamondCareerAppearance> Appearances { get; set; } = [];
    public List<DiamondSeasonGame> PendingRewards { get; set; } = [];
}
public sealed record DiamondSeasonView(string Id, long Version, string Team, int Season, int SeasonNumber,
    string AsOf, string Revision, string Pace, int Day, int TotalDays, int SeriesPerPair, bool Complete,
    IReadOnlyList<DiamondSeasonTeam> Teams, IReadOnlyList<DiamondSeasonFixture> Schedule,
    IReadOnlyList<DiamondSeasonStanding> Standings, IReadOnlyDictionary<string, DiamondSeasonPlayerStats> PlayerStats,
    DiamondSeasonGame? Game, IReadOnlyList<DiamondSeasonSummary> PreviousSeasons, long CreatedAt, long UpdatedAt,
    IReadOnlyDictionary<string, DiamondCareerAppearance>? Appearances = null);
public sealed record DiamondSeasonResponse(DiamondSeasonView? Save, DiamondView? Action, long ServerNow);
