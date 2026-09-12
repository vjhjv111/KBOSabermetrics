namespace NaverSabermetrics.Web;

public sealed record DiamondTeam(string Code, string Name);
public sealed record DiamondRoster(int Season, int[] Seasons, string AsOf, string Revision,
    IReadOnlyList<DiamondTeam> Teams, IReadOnlyList<DiamondBatter> Batters, IReadOnlyList<DiamondPitcher> Pitchers);
public sealed record DiamondRosterSelection(int Season, string AsOf, string Revision,
    DiamondBatter Batter, DiamondPitcher Pitcher);
