using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaverRelay.Models
{
    /// <summary>
    /// Naver relay API top-level wrapper: { "code": 200, "success": true, "result": { ... } }
    /// </summary>
    public sealed class NaverRelayResponse
    {
        public int? Code { get; set; }
        public bool? Success { get; set; }
        public RelayResult? Result { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class RelayResult
    {
        public GameInfo? Game { get; set; }
        public TextRelayData? TextRelayData { get; set; }
        public RelatedGames? RelatedGames { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    /// <summary>
    /// Raw game metadata and final scoreboard information.
    /// Nullable value types are intentional: historical responses occasionally contain nulls.
    /// </summary>
    public sealed class GameInfo
    {
        public string? GameId { get; set; }
        public string? SuperCategoryId { get; set; }
        public string? UpperCategoryId { get; set; }
        public string? UpperCategoryName { get; set; }
        public string? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string? GameDate { get; set; }
        public string? GameDateTime { get; set; }
        public bool? TimeTbd { get; set; }
        public string? Stadium { get; set; }
        public string? HomeTeamCode { get; set; }
        public string? HomeTeamName { get; set; }
        public int? HomeTeamScore { get; set; }
        public string? AwayTeamCode { get; set; }
        public string? AwayTeamName { get; set; }
        public int? AwayTeamScore { get; set; }
        public string? Winner { get; set; }
        public string? StatusCode { get; set; }
        public int? StatusNum { get; set; }
        public string? StatusInfo { get; set; }
        public bool? Cancel { get; set; }
        public bool? Suspended { get; set; }
        public int? SeasonYear { get; set; }
        public string? RoundCode { get; set; }
        public string? CurrentInning { get; set; }
        public List<string>? HomeTeamScoreByInning { get; set; }
        public List<string>? AwayTeamScoreByInning { get; set; }

        /// <summary>[Runs, Hits, Errors, Bases on balls]</summary>
        public List<int>? HomeTeamRheb { get; set; }

        /// <summary>[Runs, Hits, Errors, Bases on balls]</summary>
        public List<int>? AwayTeamRheb { get; set; }

        public string? HomeStarterName { get; set; }
        public string? AwayStarterName { get; set; }
        public string? WinPitcherName { get; set; }
        public string? LosePitcherName { get; set; }
        public string? HomeCurrentPitcherName { get; set; }
        public string? AwayCurrentPitcherName { get; set; }
        public int? Dheader { get; set; }
        public int? SeriesGameNo { get; set; }
        public GameCenterUrl? GameCenterUrl { get; set; }
        public WeatherInfo? WeatherInfo { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class GameCenterUrl
    {
        public string? BaseUrl { get; set; }
        public string? CheerTabUrl { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class WeatherInfo
    {
        public string? Weather { get; set; }
        public string? DongCode { get; set; }
        public string? LeisureWeatherCode { get; set; }
        public string? RegionCode { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }

    public sealed class RelatedGames
    {
        public List<GameInfo>? Games { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }
}
