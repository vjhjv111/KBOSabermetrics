using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaverRelay.Models
{
    /// <summary>
    /// One raw textRelays[] group. It is a display/relay group, not guaranteed to be a PA.
    /// </summary>
    public sealed class TextRelayPlay
    {
        public string? Title { get; set; }
        public string? TitleStyle { get; set; }
        public int? No { get; set; }
        public int? Inn { get; set; }
        public string? HomeOrAway { get; set; }
        public int? StatusCode { get; set; }
        public List<TextOption>? TextOptions { get; set; }
        public List<PtsOption>? PtsOptions { get; set; }
        public MetricOption? MetricOption { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    /// <summary>One raw event inside a relay group.</summary>
    public sealed class TextOption
    {
        public CurrentGameState? CurrentGameState { get; set; }

        /// <summary>
        /// Source sequence number. It is not a safe primary key: different event kinds can share it.
        /// </summary>
        public int? Seqno { get; set; }

        public string? Text { get; set; }
        public int? Type { get; set; }
        public BatterRecord? BatterRecord { get; set; }
        public CurrentPlayersInfo? CurrentPlayersInfo { get; set; }
        public PlayerChange? PlayerChange { get; set; }
        public int? PitchNum { get; set; }
        public string? PitchResult { get; set; }
        public string? PtsPitchId { get; set; }
        public string? Speed { get; set; }
        public string? Stuff { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    /// <summary>
    /// Snapshot attached to a PA-start event. Every value is nullable because at least one verified
    /// response contains a batterRecord object whose fields are all null.
    /// </summary>
    public sealed class BatterRecord
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
        public string? Cin { get; set; }
        public string? Cout { get; set; }
        public int? Pa { get; set; }
        public string? Pcode { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class CurrentPlayersInfo
    {
        public PlayerSideInfo? Away { get; set; }
        public PlayerSideInfo? Home { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class PlayerSideInfo
    {
        public string? PlayerType { get; set; }
        public PlayerStatsSnapshot? MonthlyStats { get; set; }
        public PlayerStatsSnapshot? CurrentSeasonStats { get; set; }
        public PlayerStatsSnapshot? TotalSeasonStats { get; set; }
        public PlayerStatsSnapshot? CurrentSeasonStatsOnOpponents { get; set; }
        public CurrentGamePlayerStats? CurrentGamePlayerStats { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class PlayerStatsSnapshot
    {
        public int? Bb { get; set; }
        public int? Kk { get; set; }
        public int? GameCount { get; set; }
        public int? S { get; set; }
        public double? Era { get; set; }
        public int? W { get; set; }
        public string? Inn { get; set; }
        public string? Inn2 { get; set; }
        public int? L { get; set; }
        public int? Er { get; set; }
        public int? Ab { get; set; }
        public int? Hit { get; set; }
        public double? Hra { get; set; }
        public int? Rbi { get; set; }
        public int? Hr { get; set; }
        public double? Whip { get; set; }
        public double? Obp { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class CurrentGamePlayerStats
    {
        public int? Kk { get; set; }
        public int? Hit { get; set; }
        public int? Bhome { get; set; }
        public int? BallCount { get; set; }
        public double? Era { get; set; }
        public double? SeasonEra { get; set; }
        public string? Inn { get; set; }
        public int? Run { get; set; }
        public int? StrikeCount { get; set; }
        public int? Bb { get; set; }
        public int? Ab { get; set; }
        public string? BatResult { get; set; }
        public int? Rbi { get; set; }
        public int? BatOrder { get; set; }
        public int? Hr { get; set; }
        public int? So { get; set; }
        public int? Pa { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    /// <summary>Raw pitch-tracking values joined through PitchId == TextOption.PtsPitchId.</summary>
    public sealed class PtsOption
    {
        public string? PitchId { get; set; }
        public int? Inn { get; set; }
        public int? Ballcount { get; set; }
        public double? CrossPlateX { get; set; }
        public double? CrossPlateY { get; set; }
        public double? TopSz { get; set; }
        public double? BottomSz { get; set; }
        public double? Vy0 { get; set; }
        public double? Vz0 { get; set; }
        public double? Vx0 { get; set; }
        public double? Z0 { get; set; }
        public double? Y0 { get; set; }
        public double? X0 { get; set; }
        public double? Ax { get; set; }
        public double? Ay { get; set; }
        public double? Az { get; set; }
        public string? Stance { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class MetricOption
    {
        public double? HomeTeamWinRate { get; set; }
        public double? AwayTeamWinRate { get; set; }
        public double? WpaByPlate { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class PlayerChange
    {
        public string? LiveText { get; set; }
        public string? Type { get; set; }
        public PlayerChangePerson? InPlayer { get; set; }
        public PlayerChangePerson? OutPlayer { get; set; }
        public PlayerChangePerson? ShiftPlayer { get; set; }
        public string? ShiftMessage { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class PlayerChangePerson
    {
        public string? PlayerName { get; set; }
        public string? PlayerPos { get; set; }
        public string? PlayerId { get; set; }
        public int? OutPlayerTurn { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }
}
