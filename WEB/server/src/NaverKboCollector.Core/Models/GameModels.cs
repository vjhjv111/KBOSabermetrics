using System.Collections.Generic;

namespace NaverRelayUI.Models
{
    // Top-level wrapper: { "code": 200, "success": true, "result": {...} }
    public class NaverRelayResponse
    {
        public int Code { get; set; }
        public bool Success { get; set; }
        public RelayResult? Result { get; set; }
    }

    public class RelayResult
    {
        public GameInfo? Game { get; set; }
        public TextRelayData? TextRelayData { get; set; }
        public RelatedGames? RelatedGames { get; set; }
    }

    // "game" block — box score / metadata for the whole game
    public class GameInfo
    {
        public string? GameId { get; set; }
        public string? SuperCategoryId { get; set; }
        public string? UpperCategoryId { get; set; }
        public string? UpperCategoryName { get; set; }
        public string? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string? GameDate { get; set; }
        public string? GameDateTime { get; set; }
        public bool TimeTbd { get; set; }
        public string? Stadium { get; set; }
        public string? HomeTeamCode { get; set; }
        public string? HomeTeamName { get; set; }
        public int? HomeTeamScore { get; set; }
        public string? AwayTeamCode { get; set; }
        public string? AwayTeamName { get; set; }
        public int? AwayTeamScore { get; set; }
        public string? Winner { get; set; } // HOME / AWAY / DRAW
        public string? StatusCode { get; set; } // BEFORE / RESULT / ...
        public int StatusNum { get; set; }
        public string? StatusInfo { get; set; } // e.g. "9회초"
        public bool Cancel { get; set; }
        public bool Suspended { get; set; }
        public int SeasonYear { get; set; }
        public string? RoundCode { get; set; }
        public string? CurrentInning { get; set; }
        public List<string>? HomeTeamScoreByInning { get; set; }
        public List<string>? AwayTeamScoreByInning { get; set; }
        // [Runs, Hits, Errors, BB]
        public List<int>? HomeTeamRheb { get; set; }
        public List<int>? AwayTeamRheb { get; set; }
        public string? HomeStarterName { get; set; }
        public string? AwayStarterName { get; set; }
        public string? WinPitcherName { get; set; }
        public string? LosePitcherName { get; set; }
        public string? HomeCurrentPitcherName { get; set; }
        public string? AwayCurrentPitcherName { get; set; }
        public int Dheader { get; set; } // doubleheader game number (0/1)
        public int SeriesGameNo { get; set; }
        public GameCenterUrl? GameCenterUrl { get; set; }
        public WeatherInfo? WeatherInfo { get; set; }
    }

    public class GameCenterUrl
    {
        public string? BaseUrl { get; set; }
        public string? CheerTabUrl { get; set; }
    }

    public class WeatherInfo
    {
        public string? Weather { get; set; }
        public string? DongCode { get; set; }
        public string? LeisureWeatherCode { get; set; }
        public string? RegionCode { get; set; }
    }

    public class RelatedGames
    {
        public List<GameInfo>? Games { get; set; }
    }
}
