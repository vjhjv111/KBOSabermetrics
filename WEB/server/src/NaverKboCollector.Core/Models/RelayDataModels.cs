using System.Collections.Generic;

namespace NaverRelayUI.Models
{
    // "textRelayData" block: current state + lineups + play-by-play list
    public class TextRelayData
    {
        public string? Category { get; set; }
        public string? GameId { get; set; }
        public int No { get; set; } // seqno of the most recent play
        public int Inn { get; set; }
        public string? HomeOrAway { get; set; } // "0" = home batting, "1" = away batting
        public string? PitcherVsBatterCareerStats { get; set; }
        public InningScore? InningScore { get; set; }
        public EntryTeam? HomeEntry { get; set; } // roster entered so far
        public EntryTeam? AwayEntry { get; set; }
        public LineupTeam? HomeLineup { get; set; } // in-game stat lines
        public LineupTeam? AwayLineup { get; set; }
        public CurrentGameState? CurrentGameState { get; set; }
        public List<TextRelayPlay>? TextRelays { get; set; } // one entry per plate appearance, newest first
    }

    // inningScore.home / inningScore.away : { "1": "0", "2": "1", ... }
    public class InningScore
    {
        public Dictionary<string, string>? Home { get; set; }
        public Dictionary<string, string>? Away { get; set; }
    }

    public class EntryTeam
    {
        public List<EntryPlayer>? Batter { get; set; }
        public List<EntryPlayer>? Pitcher { get; set; }
    }

    public class EntryPlayer
    {
        public string? Hittype { get; set; }
        public string? Pos { get; set; }
        public string? PitchingStyle { get; set; }
        public string? Name { get; set; }
        public string? Pcode { get; set; } // player id — join key for stats
    }

    public class LineupTeam
    {
        public List<LineupBatter>? Batter { get; set; }
        public List<LineupPitcher>? Pitcher { get; set; }
    }

    // Per-game batting line for one lineup slot (seqno = substitution order within batOrder)
    public class LineupBatter
    {
        public int Bb { get; set; }
        public int Ab { get; set; }
        public string? PosName { get; set; }
        public int Seqno { get; set; }
        public int BatOrder { get; set; }
        public double SeasonHra { get; set; }
        public int Run { get; set; }
        public int Hr { get; set; }
        public double PsHra { get; set; }
        public string? VsHra { get; set; }
        public string? Backnum { get; set; }
        public int Hit { get; set; }
        public string? HitType { get; set; }
        public int Pos { get; set; }
        public int Hbp { get; set; }
        public string? Name { get; set; }
        public int Rbi { get; set; }
        public double TodayHra { get; set; }
        public int So { get; set; }
        // Naver sends these as the STRING "true" (not a JSON boolean) or null —
        // compare with == "true" rather than treating as bool.
        public string? Cin { get; set; } // non-null/"true" = came in as a substitute
        public string? Cout { get; set; } // non-null/"true" = left the game
        public int Pa { get; set; }
        public string? Pcode { get; set; }
    }

    public class LineupPitcher
    {
        public int Bb { get; set; }
        public int Kk { get; set; }
        public string? VsEra { get; set; }
        public int Seqno { get; set; }
        public int BallCount { get; set; }
        public string? Inn { get; set; } // e.g. "6.0"
        public int Run { get; set; }
        public int Hr { get; set; }
        public string? SeasonEra { get; set; }
        public int Er { get; set; }
        public string? PsEra { get; set; }
        public string? Backnum { get; set; }
        public int Hit { get; set; }
        public string? HitType { get; set; }
        public int Hbp { get; set; }
        public string? Name { get; set; }
        public int Wp { get; set; }
        public double TodayEra { get; set; }
        public string? Pcode { get; set; }
    }

    // currentGameState — Naver ships these as strings, not booleans/ints
    public class CurrentGameState
    {
        public string? HomeScore { get; set; }
        public string? AwayScore { get; set; }
        public string? HomeHit { get; set; }
        public string? AwayHit { get; set; }
        public string? HomeBallFour { get; set; }
        public string? AwayBallFour { get; set; }
        public string? HomeError { get; set; }
        public string? AwayError { get; set; }
        public string? Pitcher { get; set; } // pcode
        public string? Batter { get; set; } // pcode
        public string? Strike { get; set; }
        public string? Ball { get; set; }
        public string? Out { get; set; }
        // NOTE: base1/2/3 are NOT booleans — non-"0" values appear to be the
        // batOrder of the runner occupying that base. Verify against a few
        // more games before relying on this for baserunner state.
        public string? Base1 { get; set; }
        public string? Base2 { get; set; }
        public string? Base3 { get; set; }
    }
}
