namespace NaverSabermetrics.Web;

/// <summary>Full-game defense turns physical contact into a hit/out, using the pinned matchup's records.</summary>
public static class DiamondSeasonFairBall
{
    public static void Resolve(DiamondSeasonSave save, DiamondSeasonGame game, DiamondResult result, bool simulated, Func<double> random)
    {
        if (!result.PlateEnded || result.Outcome is not ("1B" or "2B" or "3B" or "HR" or "OUT")) return;
        var b = game.Duel.Roster!.Batter; var p = game.Duel.Roster.Pitcher;
        var ai = simulated || game.Duel.Mode == "ai" && game.Duel.HostRole == "pitcher";
        const double leagueAvg = .265;
        // Small samples are regressed toward a game baseline instead of making a 1-for-1 player unbeatable.
        var batterAverage = (b.H + leagueAvg * 100) / (Math.Max(1, b.Ab) + 100);
        var pitcherHits = p.Whip is {} whip && p.Outs > 0 ? Math.Max(0, whip * p.Outs / 3 - p.Bb) : leagueAvg * Math.Max(1, p.Tbf - p.Bb);
        var pitcherAverage = (pitcherHits + leagueAvg * 150) / (Math.Max(1, p.Tbf - p.Bb) + 150);
        var average = DiamondEngine.Clamp(batterAverage * pitcherAverage / leagueAvg, .145, .405);
        var strikeouts = DiamondEngine.Clamp(((b.So + .20 * 100) / (b.Pa + 100) + (p.So + .20 * 150) / (p.Tbf + 150)) / 2, .07, .38);
        var homeRunRate = DiamondEngine.Clamp((b.Hr + .025 * 100) / (Math.Max(1, b.Ab) + 100), .005, .075);
        var babip = DiamondEngine.Clamp((average - homeRunRate) / Math.Max(.45, 1 - strikeouts - homeRunRate), .18, .42);

        if (ai)
        {
            // The duel's reward-oriented exit speed is calibrated by actual HR frequency for AI batters.
            result.ExitSpeed *= DiamondEngine.Clamp(.91 + .06 * homeRunRate / .025, .90, 1.08);
            if (result.Trajectory != "ground") result.Distance = DiamondEngine.CarryDistance(result.ExitSpeed, result.LaunchAngle, result.Contact?.Position.Y ?? 1.05);
        }
        var fence = 100 + 22 * Math.Cos(Math.Min(Math.PI / 2, Math.Abs(result.Direction) * 2));
        if (result.Distance >= fence && result.LaunchAngle is >= 14 and <= 48)
        { result.Kind = "hit"; result.Outcome = "HR"; result.Label = "홈런!"; result.Points = 4; return; }

        var defenders = game.Half == "top" ? game.HomeLineup : game.AwayLineup;
        var fielderId = defenders[Math.Min(8, (int)(random() * 9))];
        var fielder = save.Teams.SelectMany(x => x.Batters).First(x => x.Id == fielderId);
        var fielding = DiamondEngine.Clamp(fielder.Profile?.GameRatings?.Fielding ?? 50, 25, 95);
        var hitChance = ai ? babip * (.95 + .25 * result.Quality)
            : DiamondEngine.Clamp(.22 + .66 * result.Quality + (average - leagueAvg) * .7, .15, .94);
        hitChance *= 1 - (fielding - 50) * .006;
        if (result.LaunchAngle > 50) hitChance *= .06;
        if (result.Trajectory == "fly" && result.Distance < 45) hitChance *= .22;
        if (result.Quality < .25) hitChance *= .3;
        if (random() >= DiamondEngine.Clamp(hitChance, .015, .96))
        {
            result.Kind = "out"; result.Outcome = "OUT"; result.Points = 0;
            result.Label = result.Trajectory == "ground" ? "땅볼 아웃" : result.Trajectory == "line" ? "직선타 아웃" : "뜬공 아웃";
            return;
        }
        var doubleShare = DiamondEngine.Clamp((b.Slg * b.Ab - b.H - 3 * b.Hr) / Math.Max(1, b.H - b.Hr), .08, .40);
        var extra = result.Distance >= 55 ? doubleShare + (!ai && result.Quality > .85 ? .2 : 0) : .025;
        var two = random() < extra;
        result.Kind = "hit"; result.Outcome = two ? "2B" : "1B"; result.Label = two ? "2루타!" : "안타!"; result.Points = two ? 2 : 1;
    }
}
