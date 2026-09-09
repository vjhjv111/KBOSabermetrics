using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaverRelay.Models
{
    /// <summary>Raw textRelayData block.</summary>
    public sealed class TextRelayData
    {
        public string? Category { get; set; }
        public string? GameId { get; set; }
        public int? No { get; set; }
        public int? Inn { get; set; }

        /// <summary>
        /// Raw Naver batting-side flag. In the verified KBO relay samples:
        /// "0" = away team batting (top), "1" = home team batting (bottom).
        /// </summary>
        public string? HomeOrAway { get; set; }

        public string? PitcherVsBatterCareerStats { get; set; }
        public InningScore? InningScore { get; set; }
        public EntryTeam? HomeEntry { get; set; }
        public EntryTeam? AwayEntry { get; set; }
        public LineupTeam? HomeLineup { get; set; }
        public LineupTeam? AwayLineup { get; set; }
        public CurrentGameState? CurrentGameState { get; set; }
        public MetricOption? LastValidMetricOption { get; set; }

        /// <summary>
        /// Raw relay groups, newest first. A group is not necessarily one plate appearance;
        /// it can be an inning marker, substitution shell, interrupted PA, or game summary.
        /// </summary>
        public List<TextRelayPlay>? TextRelays { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class InningScore
    {
        public Dictionary<string, string>? Home { get; set; }
        public Dictionary<string, string>? Away { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class EntryTeam
    {
        public List<EntryPlayer>? Batter { get; set; }
        public List<EntryPlayer>? Pitcher { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class EntryPlayer
    {
        public string? Hittype { get; set; }
        public string? Pos { get; set; }
        public string? PitchingStyle { get; set; }
        public string? Name { get; set; }
        public string? Pcode { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class LineupTeam
    {
        public List<LineupBatter>? Batter { get; set; }
        public List<LineupPitcher>? Pitcher { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    /// <summary>Final per-game batting line for one player in a lineup slot.</summary>
    public sealed class LineupBatter
    {
        public int? Bb { get; set; }
        public int? Ab { get; set; }
        public string? PosName { get; set; }
        public int? Seqno { get; set; }
        public int? BatOrder { get; set; }
        public double? SeasonHra { get; set; }
        public string? Birth { get; set; }
        public string? Weight { get; set; }
        public int? Run { get; set; }
        public int? Hr { get; set; }
        public double? PsHra { get; set; }
        public string? VsHra { get; set; }
        public string? Backnum { get; set; }
        public int? Hit { get; set; }
        public string? HitType { get; set; }
        public int? Pos { get; set; }
        public int? Hbp { get; set; }
        public string? Name { get; set; }
        public int? Rbi { get; set; }
        public double? TodayHra { get; set; }
        public int? So { get; set; }
        public string? Height { get; set; }

        /// <summary>Naver normally sends the string "true" or null, not a JSON boolean.</summary>
        public string? Cin { get; set; }

        /// <summary>Naver normally sends the string "true" or null, not a JSON boolean.</summary>
        public string? Cout { get; set; }

        public int? Pa { get; set; }
        public string? Pcode { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    /// <summary>Final per-game pitching line.</summary>
    public sealed class LineupPitcher
    {
        public int? Bb { get; set; }
        public int? Kk { get; set; }
        public string? VsEra { get; set; }
        public int? Seqno { get; set; }
        public int? BallCount { get; set; }
        public string? Inn { get; set; }
        public int? Run { get; set; }
        public int? Hr { get; set; }
        public string? SeasonEra { get; set; }
        public int? Er { get; set; }
        public string? PsEra { get; set; }
        public string? Backnum { get; set; }
        public int? Hit { get; set; }
        public string? HitType { get; set; }
        public int? Hbp { get; set; }
        public string? Name { get; set; }
        public int? Wp { get; set; }
        public double? TodayEra { get; set; }
        public string? Pcode { get; set; }
        public string? Birth { get; set; }
        public string? Height { get; set; }
        public string? Weight { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    /// <summary>
    /// Raw currentGameState. Naver ships these values as strings.
    /// base1/base2/base3 are batting-order slots, not player IDs and not booleans.
    /// </summary>
    public sealed class CurrentGameState
    {
        public string? HomeScore { get; set; }
        public string? AwayScore { get; set; }
        public string? HomeHit { get; set; }
        public string? AwayHit { get; set; }
        public string? HomeBallFour { get; set; }
        public string? AwayBallFour { get; set; }
        public string? HomeError { get; set; }
        public string? AwayError { get; set; }
        public string? Pitcher { get; set; }
        public string? Batter { get; set; }
        public string? Strike { get; set; }
        public string? Ball { get; set; }
        public string? Out { get; set; }
        public string? Base1 { get; set; }
        public string? Base2 { get; set; }
        public string? Base3 { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }
}
