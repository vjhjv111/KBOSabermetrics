namespace NaverSabermetrics.Web;

/// <summary>Baseball state transitions, independent of HTTP, persistence and the one-PA engine.</summary>
public static class DiamondSeasonRules
{
    public static string BattingTeam(DiamondSeasonGame game) => game.Half == "top" ? game.AwayTeam : game.HomeTeam;
    public static string FieldingTeam(DiamondSeasonGame game) => game.Half == "top" ? game.HomeTeam : game.AwayTeam;
    public static string Batter(DiamondSeasonGame game) => game.Half == "top" ? game.AwayLineup[game.AwayOrder] : game.HomeLineup[game.HomeOrder];
    public static string Pitcher(DiamondSeasonGame game) => game.Half == "top" ? game.HomePitcher : game.AwayPitcher;
    public static DiamondSeasonPlayerStats Stat(DiamondSeasonSave save, DiamondSeasonGame game, string id)
    {
        if (game.PlayerStats.TryGetValue(id, out var found)) return found;
        var b = save.Teams.SelectMany(x => x.Batters).FirstOrDefault(x => x.Id == id);
        var p = save.Teams.SelectMany(x => x.Pitchers).FirstOrDefault(x => x.Id == id);
        return game.PlayerStats[id] = new() { PlayerId = id, Name = b?.Name ?? p?.Name ?? id, Team = b?.Team ?? p?.Team ?? "" };
    }
    public static void RegisterParticipants(DiamondSeasonSave save, DiamondSeasonGame game)
    {
        void Enter(string id)
        {
            if (game.Participants.Add(id)) Stat(save, game, id);
        }
        Enter(Batter(game)); Enter(Pitcher(game));
        foreach (var runner in game.Bases.OfType<DiamondSeasonRunner>()) Enter(runner.PlayerId);
        var fielders = game.Half == "top" ? game.HomeLineup : game.AwayLineup;
        var team = save.Teams.Single(x => x.Code == FieldingTeam(game));
        foreach (var id in fielders)
            if (team.Batters.FirstOrDefault(x => x.Id == id)?.Profile?.Position != "DH") Enter(id);
    }
    public static void ApplyPlate(DiamondSeasonSave save, DiamondSeasonGame game, DiamondResult result, Func<double> random, long now)
    {
        if (!result.PlateEnded || game.Complete) return;
        var batter = Batter(game); var pitcher = Pitcher(game);
        var b = Stat(save, game, batter); var p = Stat(save, game, pitcher);
        double Speed(string id) => DiamondEngine.Clamp(save.Teams.SelectMany(x => x.Batters).FirstOrDefault(x => x.Id == id)?.Profile?.GameRatings?.Speed ?? 50, 25, 95);
        // Extra-base running is a game model; imported batting totals themselves are never changed.
        if (result.Outcome == "2B" && result.Distance >= 85 && random() < DiamondEngine.Clamp(.025 + (Speed(batter) - 50) * .0015, .01, .12))
        { result.Outcome = "3B"; result.Label = "3루타!"; }
        b.PA++; game.PlateAppearances++;
        var oldOuts = game.Outs; var scored = 0;
        bool WinningRunReached() => game.Half == "bottom" && game.Inning >= 9 && game.HomeRuns > game.AwayRuns;
        void Score(DiamondSeasonRunner? runner)
        {
            if (runner is null || result.Outcome != "HR" && WinningRunReached()) return;
            Stat(save, game, runner.PlayerId).R++;
            Stat(save, game, runner.PitcherId).RunsAllowed++;
            if (game.Half == "top") { game.AwayRuns++; game.AwayLine[game.Inning - 1]++; }
            else { game.HomeRuns++; game.HomeLine[game.Inning - 1]++; }
            scored++;
        }
        var runner = new DiamondSeasonRunner(batter, pitcher);
        if (result.Outcome is "BB" or "HBP")
        {
            if (result.Outcome == "BB") { b.BB++; p.WalksAllowed++; } else { b.HBP++; p.HitBatters++; }
            if (game.Bases[0] != null)
            {
                if (game.Bases[1] != null) { Score(game.Bases[2]); game.Bases[2] = game.Bases[1]; }
                game.Bases[1] = game.Bases[0];
            }
            game.Bases[0] = runner;
        }
        else if (result.Outcome is "1B" or "2B" or "3B" or "HR")
        {
            b.AB++; b.H++; p.HitsAllowed++;
            if (game.Half == "top") game.AwayHits++; else game.HomeHits++;
            var bases = result.Outcome switch { "HR" => 4, "3B" => 3, "2B" => 2, _ => 1 };
            if (bases == 4) { b.HR++; for (var i = 2; i >= 0; i--) Score(game.Bases[i]); Score(runner); game.Bases = new DiamondSeasonRunner?[3]; }
            else if (bases == 3) { b.Triple++; for (var i = 2; i >= 0; i--) Score(game.Bases[i]); game.Bases = [null, null, runner]; }
            else if (bases == 2)
            {
                b.Double++; Score(game.Bases[2]); Score(game.Bases[1]);
                var first = game.Bases[0]; var firstScores = first != null && (oldOuts == 2 || random() < DiamondEngine.Clamp(.5 + (Speed(first.PlayerId) - 50) * .006, .2, .9));
                if (firstScores) Score(first);
                game.Bases = [null, runner, firstScores ? null : first];
            }
            else
            {
                Score(game.Bases[2]); var second = game.Bases[1]; var first = game.Bases[0];
                var secondScores = second != null && (oldOuts == 2 || random() < DiamondEngine.Clamp(.65 + (Speed(second.PlayerId) - 50) * .005, .3, .93));
                if (secondScores) Score(second);
                var third = secondScores ? null : second;
                var firstToThird = first != null && third == null && random() < DiamondEngine.Clamp(.28 + (Speed(first.PlayerId) - 50) * .004, .1, .6);
                if (firstToThird) third = first;
                game.Bases = [runner, firstToThird ? null : first, third];
            }
        }
        else
        {
            var outs = 1;
            if (result.Outcome == "K") { b.AB++; b.K++; p.Strikeouts++; }
            else if (oldOuts < 2 && game.Bases[0] != null && result.Trajectory == "ground" && random() < .55)
            {
                b.AB++; b.GIDP++; outs = 2; game.Bases[0] = null;
                result.Outcome = "GIDP"; result.Label = "병살타";
                // A force third out erases all runs. With zero outs, a runner on third may score, without RBI.
                if (oldOuts == 0) { Score(game.Bases[2]); game.Bases[2] = null; }
            }
            else if (oldOuts < 2 && game.Bases[2] != null && result.Trajectory == "fly" && result.Distance >= 45)
            {
                b.SF++; result.Outcome = "SF"; result.Label = "희생 플라이"; Score(game.Bases[2]); game.Bases[2] = null;
            }
            else b.AB++;
            game.Outs += outs; p.OutsPitched += outs;
        }
        if (result.Outcome != "GIDP") b.RBI += scored;
        result.Points = scored;
        game.Events.Add($"{game.Inning}회 {(game.Half == "top" ? "초" : "말")} · {b.Name} {result.Label}" + (scored > 0 ? $" · {scored}득점" : ""));
        if (game.Events.Count > 80) game.Events.RemoveAt(0);
        if (game.Half == "top") game.AwayOrder = (game.AwayOrder + 1) % 9; else game.HomeOrder = (game.HomeOrder + 1) % 9;
        if (WinningRunReached()) End(game, "walkoff", now);
        else if (game.Outs >= 3)
        {
            if (game.Half == "top" && game.Inning >= 9 && game.HomeRuns > game.AwayRuns) End(game, "home-ahead", now);
            else if (game.Half == "bottom" && game.Inning >= 9 && game.HomeRuns != game.AwayRuns) End(game, "regulation", now);
            else if (game.Half == "bottom" && game.Inning >= 12) End(game, "tie-12", now);
            else
            {
                game.Outs = 0; game.Bases = new DiamondSeasonRunner?[3];
                if (game.Half == "top") { game.Half = "bottom"; game.HomeLine.Add(0); }
                else { game.Half = "top"; game.Inning++; game.AwayLine.Add(0); }
            }
        }
    }
    private static void End(DiamondSeasonGame game, string reason, long now)
    { game.Complete = true; game.EndReason = reason; game.CompletedAt = now; }

    public static List<DiamondSeasonFixture> Schedule(string saveId, int seasonNumber, IReadOnlyList<string> teams, int repeats)
    {
        if (teams.Count != 10 || teams.Distinct().Count() != 10) throw new DiamondInputError("정규 리그는 선수 9명 이상과 투수를 보유한 10개 팀이 필요합니다.");
        var result = new List<DiamondSeasonFixture>(); var day = 0;
        for (var cycle = 0; cycle < repeats; cycle++)
        for (var reverse = 0; reverse < 2; reverse++)
        {
            var ring = teams.ToList();
            for (var round = 0; round < 9; round++)
            {
                day++;
                for (var pair = 0; pair < 5; pair++)
                {
                    var a = ring[pair]; var b = ring[9 - pair];
                    var flip = (round + pair + reverse) % 2 == 1;
                    result.Add(new() { Id = $"{saveId}:{seasonNumber}:{day}:{pair}", Day = day, HomeTeam = flip ? b : a, AwayTeam = flip ? a : b });
                }
                var last = ring[^1]; ring.RemoveAt(ring.Count - 1); ring.Insert(1, last);
            }
        }
        return result;
    }
    public static IReadOnlyList<DiamondSeasonStanding> Standings(DiamondSeasonSave save)
    {
        var rows = save.Teams.Select(team =>
        {
            var games = save.Schedule.Where(x => x.Complete && (x.HomeTeam == team.Code || x.AwayTeam == team.Code)).ToArray();
            var scored = games.Sum(x => x.HomeTeam == team.Code ? x.HomeRuns : x.AwayRuns);
            var allowed = games.Sum(x => x.HomeTeam == team.Code ? x.AwayRuns : x.HomeRuns);
            var ties = games.Count(x => x.HomeRuns == x.AwayRuns);
            var wins = games.Count(x => x.HomeTeam == team.Code ? x.HomeRuns > x.AwayRuns : x.AwayRuns > x.HomeRuns);
            var losses = games.Length - wins - ties;
            return new DiamondSeasonStanding(team.Code, team.Name, games.Length, wins, losses, ties, scored, allowed,
                wins + losses == 0 ? 0 : (double)wins / (wins + losses), 0);
        }).OrderByDescending(x => x.Pct).ThenByDescending(x => x.Wins).ThenByDescending(x => x.RunsFor - x.RunsAgainst).ThenBy(x => x.Team, StringComparer.Ordinal).ToList();
        if (rows.Count > 0) { var leader = rows[0]; rows = rows.Select(x => x with { GamesBehind = ((leader.Wins - x.Wins) + (x.Losses - leader.Losses)) / 2d }).ToList(); }
        return rows;
    }
    public static void Accumulate(DiamondSeasonPlayerStats target, DiamondSeasonPlayerStats game)
    {
        foreach (var property in typeof(DiamondSeasonPlayerStats).GetProperties().Where(x => x.PropertyType == typeof(int)))
            property.SetValue(target, (int)property.GetValue(target)! + (int)property.GetValue(game)!);
    }
}
