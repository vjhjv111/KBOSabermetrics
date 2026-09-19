using System.Text.RegularExpressions;
using NaverRelay.Parsing;

namespace NaverRelay.Application.Defense;

/// <summary>
/// Outcome-independent direction evidence only. FieldDirection/PrimaryFielder from the legacy parser
/// are intentionally NOT accepted: they often describe the successful fielder, not the ball location.
/// P/C receive innings only. These are coarse-zone diagnostics, not tracking-based range measurements.
/// </summary>
public static class DefenseCalculator
{
    public static readonly string[] Positions = ["P", "C", "1B", "2B", "3B", "SS", "LF", "CF", "RF"];

    public static string Position(string? value) => (value ?? "").Trim() switch
    {
        "투수" or "1" or "P" => "P", "포수" or "2" or "C" => "C",
        "1루수" or "3" or "1B" => "1B", "2루수" or "4" or "2B" => "2B",
        "3루수" or "5" or "3B" => "3B", "유격수" or "6" or "SS" => "SS",
        "좌익수" or "7" or "LF" => "LF", "중견수" or "8" or "CF" => "CF",
        "우익수" or "9" or "RF" => "RF", _ => ""
    };

    public sealed record Zone(string Key, bool Core, (string Position, double Weight)[] Owners);

    public static Zone? Locate(string? original)
    {
        var text = (original ?? "").Split(':', 2).Last();
        if (Regex.IsMatch(text, @"3\s*유간|3루[수]?\s*[-~·와과/]*\s*유격수\s*사이"))
            return new("3B-SS", false, [("3B", .5), ("SS", .5)]);
        if (Regex.IsMatch(text, @"1\s*[-~·/]\s*2루간|1루수\s*[-~·와과/]*\s*2루수\s*사이"))
            return new("1B-2B", false, [("1B", .5), ("2B", .5)]);
        if (Regex.IsMatch(text, @"2루수\s*[-~·와과/]*\s*유격수\s*사이|유격수\s*[-~·와과/]*\s*2루수\s*사이"))
            return new("2B-SS", false, [("2B", .5), ("SS", .5)]);
        if (text.Contains("좌중간", StringComparison.Ordinal)) return new("LC", false, [("LF", .5), ("CF", .5)]);
        if (text.Contains("우중간", StringComparison.Ordinal)) return new("RC", false, [("CF", .5), ("RF", .5)]);
        if (Regex.IsMatch(text, @"좌측|좌선상|좌익선상|좌익수\s*(?:방향|방면)")) return new("L", true, [("LF", 1)]);
        if (Regex.IsMatch(text, @"우측|우선상|우익선상|우익수\s*(?:방향|방면)")) return new("R", true, [("RF", 1)]);
        if (Regex.IsMatch(text, @"중앙|중견수\s*(?:방향|방면)")) return new("C", true, [("CF", 1)]);
        foreach (var (word, pos) in new[] { ("1루수", "1B"), ("2루수", "2B"), ("3루수", "3B"), ("유격수", "SS") })
            if (Regex.IsMatch(text, Regex.Escape(word) + @"\s*(?:방향|방면)")) return new(pos, true, [(pos, 1)]);
        return null;
    }

    public static bool IsFairOpportunity(DefensePlay p)
    {
        var result = (BattingResultType)p.ResultType;
        return p.BallType != (int)BattedBallType.Bunt && result is
            BattingResultType.Single or BattingResultType.InfieldSingle or BattingResultType.Double or BattingResultType.Triple or
            BattingResultType.GroundOut or BattingResultType.FlyOut or BattingResultType.LineOut or BattingResultType.InfieldFlyOut or
            BattingResultType.SacrificeFly or BattingResultType.GroundedIntoDoublePlay or BattingResultType.ReachedOnError or BattingResultType.FieldersChoice;
    }

    public static int Converted(DefensePlay p) => p.IsHit ? 0 :
        (p.IsBatterOut || ((p.ResultType is (int)BattingResultType.FieldersChoice or (int)BattingResultType.GroundedIntoDoublePlay) && p.OutsRecorded > 0) ? 1 : 0);

    public static DefenseSeasonSnapshot Calculate(IEnumerable<DefenseGameInput> games, DefensePolicy? policy = null)
    {
        var builder = new DefenseSeasonBuilder(policy ?? new());
        foreach (var game in games) builder.AddGame(game);
        return builder.Complete();
    }
}

/// <summary>Streaming game reconstruction; keeps only compact opportunities and RE observations for the season.</summary>
public sealed class DefenseSeasonBuilder
{
    private readonly DefensePolicy _policy;
    private readonly List<DefenseGameResult> _games = new();
    private readonly List<Chance> _chances = new();
    private readonly List<ReState> _states = new();
    private readonly List<ReTransition> _transitions = new();
    private int? _year;
    private bool _completed;

    private sealed record Chance(DefenseGameResult Game, DefenseContribution Player, string Zone, string Hand,
        double Weight, int Outcome, int? State);
    private sealed record ReState(string Team, int State, double Remaining);
    private sealed record ReTransition(string Team, int State, int? NextState, int Runs, int Outcome);
    private sealed record Timeline(int Order, DefenseChange? Change, DefensePlay? Play);

    public DefenseSeasonBuilder(DefensePolicy policy)
    {
        if (policy.MinimumReferencePlays < 1 || policy.MinimumOutcomePlays < 1 || policy.MinimumReStates < 1 || policy.PriorPlays < 0)
            throw new ArgumentOutOfRangeException(nameof(policy));
        _policy = policy;
    }

    public void AddGame(DefenseGameInput input)
    {
        if (_completed) throw new InvalidOperationException("Already completed.");
        if (input.Year <= 0 || _year.HasValue && input.Year != _year) throw new ArgumentException("One real season is required.");
        _year = input.Year;
        var output = new DefenseGameResult { GameId = input.GameId, Year = input.Year, Date = input.Date,
            Stadium = input.Stadium, HomeTeam = input.HomeTeam, AwayTeam = input.AwayTeam };
        _games.Add(output);
        if (!input.Completed) { Audit(output, "", "INCOMPLETE_GAME", 1, "종료가 확인되지 않은 경기: 수비 진단에서 제외"); return; }
        if (string.IsNullOrWhiteSpace(input.HomeTeam) || string.IsNullOrWhiteSpace(input.AwayTeam) || input.HomeTeam == input.AwayTeam)
        { Audit(output, "", "INVALID_TEAMS", 1, "홈/원정 팀 연결 불가"); return; }
        foreach (var warning in input.InputWarnings) Audit(output, "", "INPUT_WARNING", 1, warning);

        var rosters = new Dictionary<string, Dictionary<string, DefensePlayer?>>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var team in new[] { input.HomeTeam, input.AwayTeam })
        {
            var roster = DefenseCalculator.Positions.ToDictionary(p => p, _ => (DefensePlayer?)null);
            rosters[team] = roster;
            var starters = input.Starters.GetValueOrDefault(team) ?? new();
            foreach (var p in starters)
            {
                if (p.Pcode.Length == 0) continue;
                names[p.Pcode] = p.Name;
                // Final lineup can show a later position. The first explicit old-position is better evidence.
                var firstChange = input.Changes.Where(c => c.Team == team && c.OutCode == p.Pcode && c.Parsed)
                    .OrderBy(c => c.Order).FirstOrDefault();
                var pos = DefenseCalculator.Position(firstChange?.OldPosition);
                if (pos.Length == 0) pos = DefenseCalculator.Position(p.Position);
                if (pos.Length == 0) continue; // DH / pinch roles are not defensive positions.
                if (roster[pos] != null && roster[pos]!.Pcode != p.Pcode)
                { roster[pos] = new("", "", pos); Audit(output, team, "AMBIGUOUS_STARTER", 1, "선발 포지션 중복: 추정하지 않음"); }
                else if (roster[pos]?.Pcode != "") roster[pos] = p with { Position = pos };
            }
            foreach (var pos in roster.Keys.ToArray()) if (roster[pos]?.Pcode == "") roster[pos] = null;
            foreach (var duplicate in roster.Where(k => k.Value != null).GroupBy(k => k.Value!.Pcode).Where(g => g.Count() > 1).ToList())
                foreach (var item in duplicate) { roster[item.Key] = null; Audit(output, team, "DUPLICATE_STARTER", 1, "한 선수가 복수 포지션에 중복: 미확정 처리"); }
            output.Teams.Add(new() { TeamCode = team, Outs = Math.Max(0, input.PitchingOuts.GetValueOrDefault(team)) });
        }
        foreach (var c in input.Changes) if (c.InCode.Length > 0 && c.InName.Length > 0) names[c.InCode] = c.InName;

        DefenseContribution Row(string team, string pos, DefensePlayer player)
        {
            var found = output.Players.FirstOrDefault(r => r.TeamCode == team && r.Pcode == player.Pcode && r.Position == pos);
            if (found != null) return found;
            var row = new DefenseContribution { Pcode = player.Pcode, Name = player.Name, TeamCode = team, Position = pos };
            output.Players.Add(row); return row;
        }
        void ClearTeam(string team)
        {
            if (rosters.TryGetValue(team, out var roster)) foreach (var pos in roster.Keys.ToArray()) roster[pos] = null;
        }
        void Change(DefenseChange change)
        {
            if (!rosters.TryGetValue(change.Team, out var roster))
            { ClearTeam(input.HomeTeam); ClearTeam(input.AwayTeam); Audit(output, "", "UNRESOLVED_CHANGE_TEAM", 1, "교체 팀 미확정: 이후 선수 매핑 중지"); return; }
            if (!change.Parsed || change.OutCode.Length == 0 || change.InCode.Length == 0)
            { ClearTeam(change.Team); Audit(output, change.Team, "UNRESOLVED_CHANGE_PLAYER", 1, "교체 선수 ID 미확정: 이후 선수 매핑 중지"); return; }
            foreach (var old in roster.Where(k => k.Value?.Pcode == change.OutCode || k.Value?.Pcode == change.InCode).Select(k => k.Key).ToArray()) roster[old] = null;
            var newPos = DefenseCalculator.Position(change.NewPosition);
            if (newPos.Length > 0)
                roster[newPos] = new(change.InCode, names.GetValueOrDefault(change.InCode, change.InCode), newPos);
            // PH/PR/DH deliberately leave the vacated defensive position unknown until an explicit defensive change.
        }

        // ChronologicalIndex must be present; no ordering from batting result or successful fielder is guessed.
        var missingChanges = input.Changes.Where(c => c.Order < 0 || c.Inning <= 0 || !rosters.ContainsKey(c.BattingTeam)).ToList();
        foreach (var c in missingChanges)
        {
            if (rosters.ContainsKey(c.Team)) ClearTeam(c.Team); else { ClearTeam(input.HomeTeam); ClearTeam(input.AwayTeam); }
            Audit(output, c.Team, "CHANGE_TIME_MISSING", 1, "교체 순서/이닝 미확정: 해당 팀 선수 매핑 제외");
        }
        var invalidChangeTeams = missingChanges.Select(c => c.Team).ToHashSet();
        if (missingChanges.Any(c => !rosters.ContainsKey(c.Team))) invalidChangeTeams.UnionWith(rosters.Keys);
        var halfKeys = input.Plays.Where(p => p.Inning > 0 && rosters.ContainsKey(p.BattingTeam)).Select(p => (p.Inning, p.BattingTeam))
            .Concat(input.Changes.Where(c => c.Order >= 0 && c.Inning > 0 && rosters.ContainsKey(c.BattingTeam)).Select(c => (c.Inning, c.BattingTeam)))
            .Distinct().OrderBy(k => k.Inning).ThenBy(k => k.BattingTeam == input.AwayTeam ? 0 : 1).ToList();
        foreach (var team in rosters.Keys)
        {
            var innings = halfKeys.Where(k => k.BattingTeam != team).Select(k => k.Inning).ToArray();
            if (innings.Length == 0 || innings[0] != 1 || innings.Distinct().Count() != innings.Max())
            { invalidChangeTeams.Add(team); ClearTeam(team); Audit(output, team, "MISSING_HALF", 1, "수비 이닝 중계 구간 누락: 선수 매핑 제외"); }
        }

        foreach (var half in halfKeys)
        {
            var fielding = half.BattingTeam == input.HomeTeam ? input.AwayTeam : input.HomeTeam;
            var roster = rosters[fielding];
            var teamRow = output.Teams.Single(t => t.TeamCode == fielding);
            var cap = Math.Clamp(teamRow.Outs - (half.Inning - 1) * 3, 0, 3);
            var plays = input.Plays.Where(p => p.Inning == half.Inning && p.BattingTeam == half.BattingTeam).ToList();
            var changes = input.Changes.Where(c => c.Inning == half.Inning && c.BattingTeam == half.BattingTeam && c.Order >= 0).ToList();
            var timeValid = teamRow.Outs > 0 && !invalidChangeTeams.Contains(fielding);
            if (plays.Any(p => p.BeforeOuts > cap)) timeValid = false;
            var ledger = new Dictionary<DefenseContribution, int>();
            var unknownOuts = 0;
            var currentOuts = 0;
            void Credit(int end)
            {
                if (end < currentOuts || end > cap) { timeValid = false; return; }
                var delta = end - currentOuts;
                foreach (var pos in DefenseCalculator.Positions)
                {
                    if (roster[pos] is { } player && !invalidChangeTeams.Contains(fielding))
                    { var r = Row(fielding, pos, player); ledger[r] = ledger.GetValueOrDefault(r) + delta; }
                    else unknownOuts += delta;
                }
                currentOuts = end;
            }
            var timeline = plays.Where(p => p.ResultOrder.HasValue && !p.SyntheticOrder)
                .Select(p => new Timeline(p.ResultOrder!.Value, null, p))
                .Concat(changes.Select(c => new Timeline(c.Order, c, null))).OrderBy(e => e.Order).ThenBy(e => e.Change != null ? 0 : 1).ToList();
            // Do not assign a play when two different source events claim the same clock value.
            var collisions = timeline.GroupBy(e => e.Order).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            foreach (var play in plays)
            {
                if (DefenseCalculator.IsFairOpportunity(play))
                { teamRow.FairBalls++; teamRow.ConvertedBalls += DefenseCalculator.Converted(play); }
                if (!play.ResultOrder.HasValue || play.SyntheticOrder)
                    Audit(output, fielding, "PLAY_TIME_MISSING", 1, "타석 순서를 확정할 수 없음: 선수 수비기회에서 제외");
            }
            foreach (var entry in timeline)
            {
                if (entry.Change is { } change)
                {
                    if (change.BeforeOuts is { } b && b is >= 0 and <= 3) Credit(b); else timeValid = false;
                    Change(change);
                    if (invalidChangeTeams.Contains(change.Team)) ClearTeam(change.Team);
                    continue;
                }
                var play = entry.Play!;
                if (!DefenseCalculator.IsFairOpportunity(play)) continue;
                if (play.FieldingTeam != fielding || collisions.Contains(entry.Order))
                { Audit(output, fielding, "PLAY_ORDER_OR_TEAM_CONFLICT", 1, "타석 순서/수비팀 충돌"); continue; }
                if (play.ResultType is (int)BattingResultType.GroundedIntoDoublePlay or (int)BattingResultType.FieldersChoice)
                { Audit(output, fielding, "MULTI_FIELDER_PLAY", 1, "병살/야수선택은 팀 DER에만 반영; 개인 Range Runs 제외"); continue; }
                var zone = DefenseCalculator.Locate(play.Text);
                if (zone is null)
                { Audit(output, fielding, "NO_INDEPENDENT_ZONE", 1, "독립 방향 정보 없음: 처리 야수명을 방향으로 대체하지 않음"); continue; }
                teamRow.LocatedBalls++;
                if (zone.Owners.Any(o => roster[o.Position] is null) || invalidChangeTeams.Contains(fielding))
                { teamRow.UnmappedBalls++; Audit(output, fielding, "UNMAPPED_ZONE_OWNER", 1, "담당 영역 수비수 미확정: 공유구역도 전체 제외"); continue; }
                var outcome = DefenseCalculator.Converted(play);
                foreach (var owner in zone.Owners)
                {
                    var row = Row(fielding, owner.Position, roster[owner.Position]!);
                    row.Opportunities += owner.Weight; row.Conversions += owner.Weight * outcome;
                    if (zone.Core) { row.ZoneOpportunities += owner.Weight; row.ZoneConversions += owner.Weight * outcome; }
                    _chances.Add(new(output, row, zone.Key, play.BatterHand, owner.Weight, outcome, State(play)));
                }
            }
            Credit(cap);
            if (timeValid)
            {
                foreach (var item in ledger) item.Key.Outs += item.Value;
                if (unknownOuts > 0) Audit(output, fielding, "UNKNOWN_POSITION_OUTS", unknownOuts, "미확정 포지션의 아웃카운트(9개 포지션 합 기준)");
            }
            else Audit(output, fielding, "UNRESOLVED_INNINGS", cap * 9, "교체 아웃시점/팀 투구이닝 불일치: 해당 반이닝 수비이닝은 배분하지 않음");
        }
        BuildReObservations(input, output);
    }

    private static int? State(DefensePlay p) => p.BeforeOuts is >= 0 and <= 2 && p.BeforeBases is >= 0 and <= 7
        ? p.BeforeOuts.Value * 8 + p.BeforeBases.Value : null;
    private static int? BeforeScore(DefenseGameInput game, DefensePlay play, string batting) => batting == game.HomeTeam ? play.BeforeHomeScore : play.BeforeAwayScore;
    private static int? AfterScore(DefenseGameInput game, DefensePlay play, string batting) => batting == game.HomeTeam ? play.AfterHomeScore : play.AfterAwayScore;

    private void BuildReObservations(DefenseGameInput game, DefenseGameResult output)
    {
        var halves = game.Plays.Where(p => p.Inning > 0 && (p.BattingTeam == game.HomeTeam || p.BattingTeam == game.AwayTeam))
            .GroupBy(p => (p.Inning, p.BattingTeam)).OrderBy(g => g.Key.Inning).ThenBy(g => g.Key.BattingTeam == game.AwayTeam ? 0 : 1).ToList();
        for (int h = 0; h < halves.Count; h++)
        {
            var half = halves[h]; var batting = half.Key.BattingTeam;
            var fielding = batting == game.HomeTeam ? game.AwayTeam : game.HomeTeam;
            var cap = Math.Clamp(game.PitchingOuts.GetValueOrDefault(fielding) - 3 * (half.Key.Inning - 1), 0, 3);
            var ps = half.OrderBy(p => p.ResultOrder ?? int.MaxValue).ToList();
            if (cap != 3 || ps.Any(p => !p.ResultOrder.HasValue || !p.StartOrder.HasValue || p.SyntheticOrder || !State(p).HasValue))
            { Audit(output, fielding, "RE_HALF_EXCLUDED", 1, "RE24: 끝나지 않은 반이닝 또는 상태/순서 누락"); continue; }
            int? endScore = h + 1 < halves.Count
                ? BeforeScore(game, halves[h + 1].OrderBy(p => p.StartOrder ?? int.MaxValue).First(), batting)
                : batting == game.HomeTeam ? game.HomeScore : game.AwayScore;
            if (!endScore.HasValue || endScore < 0 || ps.Any(p => BeforeScore(game, p, batting) is not { } s || s > endScore || s < 0))
            { Audit(output, fielding, "RE_SCORE_MISSING", 1, "RE24: 종료 점수 연결 불가"); continue; }
            bool invalid = false;
            for (int i = 1; i < ps.Count; i++)
                if (ps[i].BeforeOuts < ps[i - 1].BeforeOuts || ps[i].StartOrder <= ps[i - 1].ResultOrder || BeforeScore(game, ps[i], batting) < BeforeScore(game, ps[i - 1], batting)) invalid = true;
            if (invalid) { Audit(output, fielding, "RE_STATE_CONFLICT", 1, "RE24: 시간/아웃/점수 비단조 상태"); continue; }
            foreach (var p in ps) _states.Add(new(fielding, State(p)!.Value, endScore.Value - BeforeScore(game, p, batting)!.Value));
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                if (!DefenseCalculator.IsFairOpportunity(p) || p.ResultType is (int)BattingResultType.GroundedIntoDoublePlay or (int)BattingResultType.FieldersChoice) continue;
                var next = i + 1 < ps.Count ? ps[i + 1] : null;
                var finishOrder = next?.StartOrder ?? int.MaxValue;
                // Independent steals, pickoffs, WP etc. must not be credited to the batted-ball fielder.
                if (game.LooseRunnerEvents.Any(r => r.Inning == p.Inning && r.BattingTeam == batting && r.Order >= p.StartOrder && r.Order < finishOrder && r.Reason != (int)RunnerAdvanceReason.BatterPlay)) continue;
                var outcome = DefenseCalculator.Converted(p);
                var nextOuts = next?.BeforeOuts ?? 3;
                if (nextOuts - p.BeforeOuts != outcome) continue; // one conversion, not DP / extra baserunning outs
                var nextScore = next is null ? endScore : BeforeScore(game, next, batting);
                if (!nextScore.HasValue) continue;
                var runs = nextScore.Value - BeforeScore(game, p, batting)!.Value;
                if (runs is < 0 or > 4) continue;
                _transitions.Add(new(fielding, State(p)!.Value, next is null ? null : State(next), runs, outcome));
            }
        }
    }

    public DefenseSeasonSnapshot Complete(CancellationToken cancellationToken = default)
    {
        if (_completed) throw new InvalidOperationException("Already completed.");
        _completed = true;
        // Full-season, other-team reference. Filtering is applied AFTER this calibration.
        foreach (var team in _chances.Select(c => c.Player.TeamCode).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var train = _chances.Where(c => c.Player.TeamCode != team).ToList();
            var re = _states.Where(s => s.Team != team).GroupBy(s => s.State)
                .Where(g => g.Count() >= _policy.MinimumReStates).ToDictionary(g => g.Key, g => g.Average(s => s.Remaining));
            var transitions = _transitions.Where(t => t.Team != team && re.ContainsKey(t.State) && (!t.NextState.HasValue || re.ContainsKey(t.NextState.Value)))
                .Select(t => (t.State, t.Outcome, Value: t.Runs + (t.NextState.HasValue ? re[t.NextState.Value] : 0) - re[t.State])).ToList();
            double? ValueFor(int? state)
            {
                var specific = state.HasValue ? transitions.Where(t => t.State == state).ToList() : new();
                double? Estimate(List<(int State, int Outcome, double Value)> samples)
                {
                    var outs = samples.Where(s => s.Outcome == 1).Select(s => s.Value).ToList();
                    var nonouts = samples.Where(s => s.Outcome == 0).Select(s => s.Value).ToList();
                    if (samples.Count < _policy.MinimumReferencePlays || Math.Min(outs.Count, nonouts.Count) < _policy.MinimumOutcomePlays) return null;
                    var delta = nonouts.Average() - outs.Average();
                    return double.IsFinite(delta) && delta > 0 && delta <= 5 ? delta : null;
                }
                return Estimate(specific) ?? Estimate(transitions);
            }
            // Pre-group once: O(plays) aggregation rather than scanning the entire league per chance.
            var baseBuckets = train.GroupBy(c => (c.Player.Position, c.Zone)).ToDictionary(g => g.Key, g => Summary(g));
            var fineBuckets = train.GroupBy(c => (c.Player.Position, c.Zone, c.Hand, c.Game.Stadium)).ToDictionary(g => g.Key, g => Summary(g));
            var rvCache = new Dictionary<int, double?>();
            foreach (var c in _chances.Where(c => c.Player.TeamCode == team))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!baseBuckets.TryGetValue((c.Player.Position, c.Zone), out var baseRows) || !Supported(baseRows))
                { Audit(c.Game, team, "MODEL_REFERENCE_TOO_SMALL", 1, "타 팀 동일 구역의 성공/실패 표본 부족: OOA/RR 미산출"); continue; }
                var count = baseRows.Count;
                var p = baseRows.Success / count;
                if (fineBuckets.TryGetValue((c.Player.Position, c.Zone, c.Hand, c.Game.Stadium), out var finer) && Supported(finer))
                    p = (finer.Success + _policy.PriorPlays * p) / (finer.Count + _policy.PriorPlays);
                var residual = c.Weight * (c.Outcome - p);
                c.Player.ModelOpportunities += c.Weight; c.Player.ExpectedOuts += c.Weight * p; c.Player.OoaLite += residual;
                var sk = c.State ?? -1;
                if (!rvCache.TryGetValue(sk, out var rv)) { rv = ValueFor(c.State); rvCache[sk] = rv; }
                if (!rv.HasValue) { Audit(c.Game, team, "RE_RUN_VALUE_UNAVAILABLE", 1, "타 팀 RE24/아웃 대비 비아웃 run value 표본 부족: RR 미산출"); continue; }
                c.Player.RunValueOpportunities += c.Weight; c.Player.RangeRuns += residual * rv.Value;
            }
        }
        // One additive centering per full season × position. Subsets/career never get recentered again.
        foreach (var group in _games.SelectMany(g => g.Players).Where(p => p.RunValueOpportunities > 0).GroupBy(p => p.Position))
        {
            var rate = group.Sum(p => p.RangeRuns) / group.Sum(p => p.RunValueOpportunities);
            foreach (var p in group) p.CenteringRuns = -rate * p.RunValueOpportunities;
        }
        foreach (var game in _games)
            foreach (var team in game.Teams)
            {
                Audit(game, team.TeamCode, "TOTAL_FAIR_BIP", team.FairBalls, "홈런·삼진·볼넷·사구·번트 제외 팀 타구 표본");
                Audit(game, team.TeamCode, "LOCATED_BIP", team.LocatedBalls, "독립 방향 문구가 있는 타구(좌표 데이터 아님)");
                Audit(game, team.TeamCode, "FINAL_TEAM_OUTS", team.Outs, "경기별 최종 투구 아웃카운트");
            }
        return new() { Year = _year ?? 0, Games = _games };
    }

    private readonly record struct Bucket(double Count, double Success);
    private static Bucket Summary(IEnumerable<Chance> rows) => new(rows.Sum(c => c.Weight), rows.Sum(c => c.Weight * c.Outcome));
    private bool Supported(Bucket rows) => rows.Count >= _policy.MinimumReferencePlays &&
        rows.Success >= _policy.MinimumOutcomePlays && rows.Count - rows.Success >= _policy.MinimumOutcomePlays;

    private static void Audit(DefenseGameResult game, string team, string code, int count, string text)
    {
        if (count <= 0) return;
        var found = game.Audit.FirstOrDefault(a => a.TeamCode == team && a.Code == code && a.Description == text);
        if (found != null) found.Count += count;
        else game.Audit.Add(new() { Year = game.Year, GameId = game.GameId, TeamCode = team, Code = code, Count = count, Description = text });
    }
}
