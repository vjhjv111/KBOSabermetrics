namespace NaverRelay.Parsing;

/// <summary>Only explicitly supplied official fields are populated. Plate-appearance events remain independent.</summary>
public sealed record OfficialBattingStats
{
    public DateTimeOffset? SourceTime { get; init; }
    public string Source { get; init; } = "KBO_BOX_SCORE";
    public int? PlateAppearances { get; init; }
    public int? AtBats { get; init; }
    public int? Runs { get; init; }
    public int? Hits { get; init; }
    public int? Doubles { get; init; }
    public int? Triples { get; init; }
    public int? HomeRuns { get; init; }
    public int? RunsBattedIn { get; init; }
    public int? Walks { get; init; }
    public int? IntentionalWalks { get; init; }
    public int? HitByPitch { get; init; }
    public int? Strikeouts { get; init; }
    public int? StolenBases { get; init; }
    public int? CaughtStealing { get; init; }
    public int? DoublePlays { get; init; }
    public int? SacrificeBunts { get; init; }
    public int? SacrificeFlies { get; init; }
    // KBO's ambiguous "희타" must not be assumed to mean SH or SF individually.
    public int? Sacrifices { get; init; }
}

public sealed record OfficialPitchingStats
{
    public DateTimeOffset? SourceTime { get; init; }
    public string Source { get; init; } = "KBO_BOX_SCORE";
    public int? InningsOuts { get; init; }
    public int? BattersFaced { get; init; }
    public int? AtBats { get; init; }
    public int? PitchCount { get; init; }
    public int? HitsAllowed { get; init; }
    public int? HomeRunsAllowed { get; init; }
    public int? Walks { get; init; }
    public int? HitBatters { get; init; }
    public int? Strikeouts { get; init; }
    public int? RunsAllowed { get; init; }
    public int? EarnedRuns { get; init; }
    public int? WildPitches { get; init; }
}
