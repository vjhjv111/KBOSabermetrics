using System.Collections.Generic;

namespace NaverRelayUI.Models
{
    // One entry in textRelays[] = one plate appearance ("타석").
    // "no" is the seqno of the LAST event in that PA and is unique per game.
    public class TextRelayPlay
    {
        public string? Title { get; set; } // e.g. "1번타자 김대한"
        public string? TitleStyle { get; set; }
        public int No { get; set; }
        public int Inn { get; set; }
        public string? HomeOrAway { get; set; }
        public int StatusCode { get; set; }
        public List<TextOption>? TextOptions { get; set; } // every event within the PA, in order
        public List<PtsOption>? PtsOptions { get; set; } // pitch-tracking data, join on pitchId == ptsPitchId
        public MetricOption? MetricOption { get; set; }
    }

    // One event within a plate appearance: a pitch, a steal, a mound visit,
    // the PA-start marker (type 8), or the outcome (type 23/24 etc).
    public class TextOption
    {
        public CurrentGameState? CurrentGameState { get; set; } // state AFTER this event
        public int Seqno { get; set; } // global sequence number for the game — use as primary key
        public string? Text { get; set; } // human-readable description, e.g. "1구 볼"
        public int Type { get; set; } // 8=PA start, 1=pitch, 7=mound visit/no-pitch event, 23/24=play result
        public BatterRecord? BatterRecord { get; set; } // only present on the type=8 (PA start) event
        public CurrentPlayersInfo? CurrentPlayersInfo { get; set; }
        public int? PitchNum { get; set; } // pitch number within this PA (only on pitch events)
        public string? PitchResult { get; set; } // B=ball, T/S=strike variants, F=foul, H=in play, W=bunt foul
        public string? PtsPitchId { get; set; } // join key into PtsOptions[].PitchId
        public string? Speed { get; set; } // km/h, as string
        public string? Stuff { get; set; } // pitch type, e.g. "직구", "슬라이더"
    }

    public class BatterRecord
    {
        public int Bb { get; set; }
        public int Ab { get; set; }
        public string? PosName { get; set; }
        public int BatOrder { get; set; }
        public double SeasonHra { get; set; }
        public int Run { get; set; }
        public int Hr { get; set; }
        public string? VsHra { get; set; }
        public string? Backnum { get; set; }
        public int Hit { get; set; }
        public string? HitType { get; set; }
        public string? Name { get; set; }
        public int Rbi { get; set; }
        public double TodayHra { get; set; }
        public int So { get; set; }
        public int Pa { get; set; }
        public string? Pcode { get; set; }
    }

    public class CurrentPlayersInfo
    {
        public PlayerSideInfo? Away { get; set; }
        public PlayerSideInfo? Home { get; set; }
    }

    public class PlayerSideInfo
    {
        public string? PlayerType { get; set; } // "pitcher" or "batter"
        public PlayerStatsSnapshot? MonthlyStats { get; set; }
        public PlayerStatsSnapshot? CurrentSeasonStats { get; set; }
        public PlayerStatsSnapshot? TotalSeasonStats { get; set; }
        public PlayerStatsSnapshot? CurrentSeasonStatsOnOpponents { get; set; }
        public CurrentGamePlayerStats? CurrentGamePlayerStats { get; set; }
    }

    // Shared shape for monthly/season/opponent-split stat blocks.
    // Batting and pitching fields share one object; irrelevant fields are 0/null.
    public class PlayerStatsSnapshot
    {
        public int Bb { get; set; }
        public int Kk { get; set; }
        public int GameCount { get; set; }
        public int S { get; set; } // saves
        public double Era { get; set; }
        public int W { get; set; }
        public string? Inn { get; set; } // "20.2" style — parse with the standard KBO 1/3-inning convention
        public string? Inn2 { get; set; } // "20 2/3" display form
        public int L { get; set; }
        public int Er { get; set; }
        public int Ab { get; set; }
        public int Hit { get; set; }
        public double Hra { get; set; }
        public int Rbi { get; set; }
        public int Hr { get; set; }
        public double Whip { get; set; }
        public double Obp { get; set; }
    }

    public class CurrentGamePlayerStats
    {
        public int Kk { get; set; }
        public int Hit { get; set; }
        public int Bhome { get; set; }
        public int BallCount { get; set; }
        public double Era { get; set; }
        public double SeasonEra { get; set; }
        public string? Inn { get; set; }
        public int Run { get; set; }
        public int StrikeCount { get; set; }
        public int Bb { get; set; }
        public int Ab { get; set; }
        public string? BatResult { get; set; }
        public int Rbi { get; set; }
        public int BatOrder { get; set; }
        public int Hr { get; set; }
        public int So { get; set; }
        public int Pa { get; set; }
    }

    // Pitch-tracking physics data (Statcast-style), joined via PitchId == TextOption.PtsPitchId
    public class PtsOption
    {
        public string? PitchId { get; set; }
        public int Inn { get; set; }
        public int Ballcount { get; set; }
        public double CrossPlateX { get; set; }
        public double CrossPlateY { get; set; }
        public double TopSz { get; set; }
        public double BottomSz { get; set; }
        public double Vy0 { get; set; }
        public double Vz0 { get; set; }
        public double Vx0 { get; set; }
        public double Z0 { get; set; }
        public double Y0 { get; set; }
        public double X0 { get; set; }
        public double Ax { get; set; }
        public double Ay { get; set; }
        public double Az { get; set; }
        public string? Stance { get; set; } // batter stance, L/R
    }

    public class MetricOption
    {
        public double HomeTeamWinRate { get; set; }
        public double AwayTeamWinRate { get; set; }
        public double WpaByPlate { get; set; } // win probability added by this PA
    }
}
