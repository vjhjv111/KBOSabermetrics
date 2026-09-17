using System.Collections.Generic;

namespace NaverRelayUI.Models
{
    // GET /schedule/games?upperCategoryId=kbaseball&fromDate=...&toDate=...&page=N
    // Page size is fixed at 10 server-side regardless of any pageSize param.
    public class ScheduleGamesResponse
    {
        public int Code { get; set; }
        public bool Success { get; set; }
        public ScheduleGamesResult? Result { get; set; }
    }

    public class ScheduleGamesResult
    {
        public List<ScheduleGame>? Games { get; set; }
        public int GameTotalCount { get; set; } // total across the whole fromDate..toDate range, not just this page
    }

    // Lightweight listing entry — NOT the same shape as GameInfo from the relay
    // endpoint (fewer fields). Use this only to enumerate gameIds.
    public class ScheduleGame
    {
        public string? GameId { get; set; }
        // "kbo" = real KBO league game. Anything else (kbaseballetc, etc.) is
        // scrimmages/youth/other — filter these out.
        public string? CategoryId { get; set; }
        public string? GameDate { get; set; }
        public string? GameDateTime { get; set; }
        public string? HomeTeamCode { get; set; }
        public string? HomeTeamName { get; set; }
        public int HomeTeamScore { get; set; }
        public string? AwayTeamCode { get; set; }
        public string? AwayTeamName { get; set; }
        public int AwayTeamScore { get; set; }
        public string? Winner { get; set; }
        public string? StatusCode { get; set; }
        public string? StatusInfo { get; set; }
        public bool Cancel { get; set; }
        public bool Suspended { get; set; }
    }
}
