using System.Text.Json;

namespace NaverSabermetrics.Web;

public sealed partial class DiamondSeasonService
{
    private void Apply(DiamondSeasonSave save, JsonElement body, string? op, string actor, DiamondSeasonRosterOverride? custom, CancellationToken token)
    {
        if (op == "next-season")
        {
            if (!save.Complete) throw new DiamondInputError("모든 정규 경기를 마친 뒤 다음 시즌을 시작할 수 있습니다.", 409);
            var table = DiamondSeasonRules.Standings(save); var yours = table.Single(x => x.Team == save.Team);
            save.PreviousSeasons.Add(new(save.SeasonNumber, table[0].Team, yours.Wins, yours.Losses, yours.Ties));
            save.SeasonNumber++; save.Day = 1; save.Complete = false; save.Game = null; save.PlayerStats.Clear();
            save.Schedule = DiamondSeasonRules.Schedule(save.Id, save.SeasonNumber, save.Teams.Select(x => x.Code).ToArray(), save.SeriesPerPair); return;
        }
        if (op == "lineup")
        {
            if (save.Game is { Complete: false }) throw new DiamondInputError("경기 중에는 개별 선수 교체를 사용해 주세요.", 409);
            if (!body.TryGetProperty("lineup", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() != 9)
                throw new DiamondInputError("9명 타순을 지정해 주세요.");
            var lineup = ids.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : "").ToList();
            var team = save.Teams.Single(x => x.Code == save.Team);
            if (lineup.Distinct().Count() != 9 || lineup.Any(id => team.Batters.All(x => x.Id != id))) throw new DiamondInputError("팀 타자 9명을 중복 없이 선택해 주세요.");
            team.Lineup = lineup; return;
        }
        if (save.Complete) throw new DiamondInputError("시즌이 끝났습니다. 다음 시즌을 시작해 주세요.", 409);
        if (op is "start-game" or "sim-half" or "sim-game" or "sim-day" or "sim-to-player")
        {
            if (save.Game is null or { Complete: true })
            {
                ApplyCustom(save, custom);
                var fixture = save.Schedule.Single(x => x.Day == save.Day && (x.HomeTeam == save.Team || x.AwayTeam == save.Team));
                save.Game = StartGame(save, fixture, actor);
            }
            if (op is "sim-game" or "sim-day") Simulate(save, save.Game, actor, false, token);
            else if (op == "sim-half") Simulate(save, save.Game, actor, true, token);
            else if (op == "sim-to-player")
            {
                if (save.CharacterBatterId == null && save.CharacterPitcherId == null)
                    throw new DiamondInputError(custom == null ? "먼저 커리어 선수를 만들어 주세요." : "경기 중에 만든 선수는 다음 경기부터 출전합니다. 현재 경기를 마쳐 주세요.");
                bool MyTurn(DiamondSeasonGame g) => save.CharacterBatterId != null && DiamondSeasonRules.Batter(g) == save.CharacterBatterId
                    || save.CharacterPitcherId != null && DiamondSeasonRules.Pitcher(g) == save.CharacterPitcherId;
                Simulate(save, save.Game, actor, false, token, MyTurn);
                if (!save.Game.Complete && save.Game.Duel.Pitch is not { Resolved: false })
                { save.Game.Duel.Pitch = null; BindMatchup(save, save.Game, actor); }
            }
            CompleteDayIfNeeded(save, actor, token); return;
        }
        var game = save.Game ?? throw new DiamondInputError("먼저 정규 경기를 시작해 주세요.", 409);
        if (game.Complete) throw new DiamondInputError("경기가 끝났습니다. 다음 경기를 시작해 주세요.", 409);
        if (Tick(save, actor, Now())) { CompleteDayIfNeeded(save, actor, token); return; }
        if (op == "tick") return;
        if (op is "substitute" or "change-pitcher") { Substitute(save, game, body, op); return; }
        var batting = DiamondSeasonRules.BattingTeam(game) == save.Team;
        if (op is "ready" or "pitch")
        {
            if (op == "ready" && !batting || op == "pitch" && batting) throw new DiamondInputError("현재 공격·수비에 맞는 조작을 사용해 주세요.", 403);
            var previous = Integer(body, "previousPitch");
            if (previous < game.Duel.PitchCount) return;
            if (previous > game.Duel.PitchCount) throw new DiamondInputError("투구 상태가 바뀌었습니다.", 409);
            if (game.Duel.Pitch is { Resolved: false }) return;
            if (game.Duel.Pitch is { } old && Now() < Math.Max(old.ReleaseAt + old.FlightMs, old.Reaction?.At ?? 0) + 900)
                throw new DiamondInputError("다음 타석을 준비하고 있습니다.", 409);
            if (op == "ready") NewPitch(save, game, actor, null, null, .85, false);
            else
            {
                var quality = Number(body, "quality"); if (quality is < 0 or > 1) throw new DiamondInputError("릴리스 입력을 확인해 주세요.");
                NewPitch(save, game, actor, Text(body, "type") ?? "", Aim(body), quality, false);
            }
            return;
        }
        if (op is "swing" or "take")
        {
            if (!batting) throw new DiamondInputError("우리 팀 공격에서만 타격할 수 있습니다.", 403);
            var pitch = game.Duel.Pitch ?? throw new DiamondInputError("진행 중인 투구가 없습니다.");
            if (pitch.Resolved) return;
            DiamondSwing? swing = null;
            if (op == "swing")
            {
                var id = Integer(body, "pitchId"); if (id < pitch.Id) return;
                if (id != pitch.Id) throw new DiamondInputError("현재 공을 확인해 주세요.", 409);
                var input = body.TryGetProperty("inputAt", out _) ? Number(body, "inputAt") : Number(body, "at") - DiamondEngine.SwingContactMs;
                if (input < Now() - 1500 || input > Now() + 150) throw new DiamondInputError("연결 지연이 큽니다. 최신 투구에서 다시 시도해 주세요.", 409);
                swing = new(input + DiamondEngine.SwingContactMs, Aim(body));
            }
            else if (Now() < pitch.ReleaseAt + pitch.FlightMs) return;
            Resolve(save, game, swing, Now()); CompleteDayIfNeeded(save, actor, token); return;
        }
        throw new DiamondInputError("지원하지 않는 리그 조작입니다.");
    }
    private static void ApplyCustom(DiamondSeasonSave save, DiamondSeasonRosterOverride? custom)
    {
        if (custom == null) return;
        save.CharacterId = custom.CharacterId; var team = save.Teams.Single(x => x.Code == save.Team);
        if (custom.Batter is { } batter)
        {
            var b = Clone(batter); b.Team = team.Code;
            if (b.Pa <= 0 || !double.IsFinite(b.Pa) || !b.Id.StartsWith(save.Season + ":", StringComparison.Ordinal)) throw new DiamondInputError("커리어 타자 기록이 올바르지 않습니다.");
            team.Batters.RemoveAll(x => x.Id == b.Id); team.Batters.Add(b); save.CharacterBatterId = b.Id;
            if (!team.Lineup.Contains(b.Id)) team.Lineup[3] = b.Id;
        }
        if (custom.Pitcher is { } pitcher)
        {
            var p = Clone(pitcher); p.Team = team.Code;
            if (p.Tbf <= 0 || !double.IsFinite(p.Tbf) || p.Arsenal.Count == 0 || !p.Id.StartsWith(save.Season + ":", StringComparison.Ordinal)) throw new DiamondInputError("커리어 투수 기록이 올바르지 않습니다.");
            team.Pitchers.RemoveAll(x => x.Id == p.Id); team.Pitchers.Add(p); save.CharacterPitcherId = p.Id;
        }
        if (custom.Appearance is { } appearance)
            foreach (var id in new[] { custom.Batter?.Id, custom.Pitcher?.Id }.OfType<string>())
                save.Appearances[id] = Clone(appearance);
    }
    private DiamondSeasonGame StartGame(DiamondSeasonSave save, DiamondSeasonFixture fixture, string actor)
    {
        var home = save.Teams.Single(x => x.Code == fixture.HomeTeam); var away = save.Teams.Single(x => x.Code == fixture.AwayTeam);
        string Starter(DiamondSeasonTeam team) => team.Code == save.Team && save.CharacterPitcherId != null ? save.CharacterPitcherId
            : team.Pitchers[(fixture.Day - 1) % Math.Min(5, team.Pitchers.Count)].Id;
        var game = new DiamondSeasonGame { Id = fixture.Id, HomeTeam = home.Code, AwayTeam = away.Code,
            HomeLineup = [.. home.Lineup], AwayLineup = [.. away.Lineup], HomePitcher = Starter(home), AwayPitcher = Starter(away),
            HomeUsedBatters = [.. home.Lineup], AwayUsedBatters = [.. away.Lineup] };
        game.HomeUsedPitchers.Add(game.HomePitcher); game.AwayUsedPitchers.Add(game.AwayPitcher);
        game.Duel = new() { Code = game.Id, Host = actor, Mode = "ai", Pace = save.Pace, CreatedAt = Now(), ExpiresAt = Now() + 365L * 86400000 };
        BindMatchup(save, game, actor); return game;
    }
    private DiamondEngine BindMatchup(DiamondSeasonSave save, DiamondSeasonGame game, string actor)
    {
        var d = game.Duel; d.Host = actor; d.HostRole = DiamondSeasonRules.BattingTeam(game) == save.Team ? "batter" : "pitcher";
        d.Batter = DiamondSeasonRules.Batter(game); d.Pitcher = DiamondSeasonRules.Pitcher(game);
        var b = save.Teams.SelectMany(x => x.Batters).Single(x => x.Id == d.Batter);
        var p = save.Teams.SelectMany(x => x.Pitchers).Single(x => x.Id == d.Pitcher);
        d.Roster = new(save.Season, save.AsOf, save.Revision, b, p);
        if (d.PitchingProfile?.PitcherId != p.Id) d.PitchingProfile = null;
        return new DiamondEngine(_data.ForRoster(d.Roster), _random);
    }
    private DiamondEngine Engine(DiamondSeasonGame game) => new(_data.ForRoster(game.Duel.Roster), _random);
    private void NewPitch(DiamondSeasonSave save, DiamondSeasonGame game, string actor, string? type, DiamondVec? aim, double quality, bool simulated)
    {
        if (game.Duel.Mode == "ai") AutoRelieve(save, game, simulated);
        var engine = BindMatchup(save, game, actor); var d = game.Duel;
        var count = DiamondSeasonRules.Stat(save, game, d.Pitcher).PitchCount;
        var starter = (game.Half == "top" ? game.HomeUsedPitchers : game.AwayUsedPitchers)[0] == d.Pitcher;
        var stamina = DiamondEngine.Clamp(d.Roster?.Pitcher.Profile?.GameRatings?.Stamina ?? 60, 25, 95);
        var comfortable = (starter ? 65 : 22) + (stamina - 60) * .9;
        var fatigue = DiamondEngine.Clamp((count - comfortable) / (starter ? 85d : 45d), 0, 1);
        var pitch = simulated ? SimulatedPitch(game, engine) : type == null ? engine.CreateAiPitch(d, Now()) : engine.CreatePitch(d, type, aim!, Math.Max(0, quality - fatigue * .25), Now());
        // Baseline player stats remain immutable; fatigue modifies this pitch's execution only.
        if (fatigue > 0)
        {
            var before = pitch.Velocity; pitch.Velocity = Math.Round(before * (1 - fatigue * .055), 1);
            pitch.FlightMs *= before / pitch.Velocity;
            if (type == null) pitch.Target = new(DiamondEngine.Clamp(pitch.Target.X + (_random() - .5) * fatigue * .45, -2.5, 2.5),
                DiamondEngine.Clamp(pitch.Target.Y + (_random() - .5) * fatigue * .45, -2, 2));
        }
        if (fatigue > 0 && !simulated) pitch.BodyHit = engine.FindBodyHit(d.Batter, d.Pitcher, pitch);
        if (simulated)
        {
            var oldRelease = pitch.ReleaseAt; pitch.ReleaseAt = Now() - pitch.FlightMs - 1100;
            if (pitch.BodyHit is { } hit) pitch.BodyHit = hit with { At = hit.At + pitch.ReleaseAt - oldRelease };
        }
        d.Pitch = pitch; d.PitchCount = pitch.Id;
        DiamondSeasonRules.RegisterParticipants(save, game);
        DiamondSeasonRules.Stat(save, game, d.Pitcher).PitchCount++;
        if (d.Mode == "ai" && d.HostRole == "pitcher" || simulated) { pitch.AiBatterSwing = FullGameAiSwing(game, engine); pitch.AiBatterSwingPrepared = true; }
    }
    private static DiamondSwing? FullGameAiSwing(DiamondSeasonGame game, DiamondEngine engine)
    {
        var swing = engine.AiSwing(game.Duel); if (swing == null) return null;
        var pitch = game.Duel.Pitch!; var p = game.Duel.Roster!.Pitcher;
        // The original duel AI uses batter discipline. Full games also reflect the opposing
        // pitcher's strikeout ability through the AI's actual timing/aim errors.
        var strikeoutRate = (p.So + .20 * 150) / (p.Tbf + 150);
        var difficulty = DiamondEngine.Clamp(1.14 * Math.Sqrt(strikeoutRate / .20), .85, 1.6);
        var arrival = pitch.ReleaseAt + pitch.FlightMs;
        return new(arrival + (swing.At - arrival) * difficulty,
            new(pitch.Target.X + (swing.Aim.X - pitch.Target.X) * difficulty, pitch.Target.Y + (swing.Aim.Y - pitch.Target.Y) * difficulty));
    }
    private DiamondPitch SimulatedPitch(DiamondSeasonGame game, DiamondEngine engine)
    {
        var d = game.Duel; var roster = d.Roster!; var type = engine.PickAiPitch(d.Pitcher);
        var actual = roster.Pitcher.Arsenal.First(x => x.Type == type);
        var inside = _random() < (roster.Pitcher.Discipline?.ZonePitchRate ?? .48);
        var data = _data.ForRoster(roster); var left = data.BatsLeft(d.Batter, d.Pitcher);
        var target = inside ? new DiamondVec((_random() - .5) * 1.35, (_random() - .5) * 1.25)
            : new DiamondVec((left ? -1 : 1) * (1.15 + _random() * .65), (_random() - .5) * 1.8);
        var factor = d.Pace switch { "practice" => 1.85, "real" => 1.15, _ => 1d };
        var pitch = new DiamondPitch { Id = d.PitchCount + 1, Type = type, Velocity = actual.Velocity, Quality = .85,
            Target = target, ReleaseAt = Now() + 2200, FlightMs = Math.Round(18.44 / (actual.Velocity / 3.6) * 1000 * factor) };
        // The warehouse roster has no per-pitch HBP rate. This explicitly documented default
        // matches the duel AI's prior; automatic play does not evaluate a rendered 3D pose.
        var hbpRate = d.PitchingProfile is { TotalPitchCount: > 0, HitByPitchRate: not null } measured
            ? DiamondEngine.Clamp((measured.HitByPitchRate.Value * measured.TotalPitchCount + .004 * 200) / (measured.TotalPitchCount + 200), 0, .03) : .004;
        if (_random() < hbpRate)
        {
            pitch.Target = new(left ? 1.75 : -1.75, .1);
            pitch.BodyHit = new(pitch.ReleaseAt + pitch.FlightMs, new(pitch.Target.X * .5, 1.105, 0));
        }
        return pitch;
    }
    private void Resolve(DiamondSeasonSave save, DiamondSeasonGame game, DiamondSwing? swing, long now, bool simulated = false)
    {
        if (game.Complete || game.Duel.Pitch is not { Resolved: false }) return;
        var result = Engine(game).EvaluatePitch(game.Duel, swing, now, skipBodyCollision: simulated);
        DiamondSeasonFairBall.Resolve(save, game, result, simulated, _random);
        DiamondEngine.FinishPitch(game.Duel, result);
        DiamondSeasonRules.ApplyPlate(save, game, result, _random, now);
        if (game.Duel.History.Count > 40) game.Duel.History.RemoveRange(0, game.Duel.History.Count - 40);
    }
    private bool Tick(DiamondSeasonSave save, string actor, long now)
    {
        var game = save.Game;
        if (game is null || game.Complete || game.Duel.Pitch is not { Resolved: false } pitch) return false;
        var arrival = pitch.ReleaseAt + pitch.FlightMs; var ai = game.Duel.HostRole == "pitcher";
        var deadline = ai ? Math.Max(arrival + 260, (pitch.AiBatterSwing?.At ?? 0) + 60) : arrival + 750;
        if (now <= deadline) return false;
        Resolve(save, game, ai ? pitch.AiBatterSwing : null, now); return true;
    }
    private void Simulate(DiamondSeasonSave save, DiamondSeasonGame game, string actor, bool halfOnly, CancellationToken token, Func<DiamondSeasonGame, bool>? stopAt = null)
    {
        var inning = game.Inning; var half = game.Half; var pitches = 0;
        while (!game.Complete && (!halfOnly || game.Inning == inning && game.Half == half) && !(stopAt?.Invoke(game) ?? false))
        {
            token.ThrowIfCancellationRequested();
            if (++pitches > 5000) throw new DiamondInputError("자동 경기 투구 상한에 도달했습니다. 저장 상태에서 다시 진행해 주세요.", 503);
            if (game.Duel.Pitch is not { Resolved: false }) NewPitch(save, game, actor, null, null, .85, true);
            var pitch = game.Duel.Pitch!;
            if (pitch.ReleaseAt + pitch.FlightMs > Now() - 1100)
            {
                var delta = Now() - pitch.FlightMs - 1100 - pitch.ReleaseAt; pitch.ReleaseAt += delta;
                if (pitch.BodyHit is {} body) pitch.BodyHit = body with { At = body.At + delta };
                if (pitch.AiBatterSwing is {} ai) pitch.AiBatterSwing = ai with { At = ai.At + delta };
            }
            var swing = pitch.AiBatterSwingPrepared ? pitch.AiBatterSwing : FullGameAiSwing(game, Engine(game));
            Resolve(save, game, swing, Now(), simulated: true);
        }
        // Simulation may finish a live, future-scheduled pitch. It must not block the next manual pitch.
        if (!game.Complete && game.Duel.Pitch is { Resolved: true } last)
        { last.ReleaseAt = Math.Min(last.ReleaseAt, Now() - last.FlightMs - 1100); if (last.Reaction != null) last.Reaction.At = Now() - 1000; }
    }
    private static void Substitute(DiamondSeasonSave save, DiamondSeasonGame game, JsonElement body, string op)
    {
        if (game.Duel.Pitch is { Resolved: false }) throw new DiamondInputError("진행 중인 투구가 끝난 뒤 교체할 수 있습니다.", 409);
        var team = save.Teams.Single(x => x.Code == save.Team); var home = game.HomeTeam == save.Team;
        var id = Text(body, "playerId") ?? "";
        if (op == "change-pitcher")
        {
            var used = home ? game.HomeUsedPitchers : game.AwayUsedPitchers;
            if (team.Pitchers.All(x => x.Id != id) || used.Contains(id)) throw new DiamondInputError("아직 등판하지 않은 우리 팀 투수를 선택해 주세요.");
            used.Add(id); if (home) game.HomePitcher = id; else game.AwayPitcher = id;
            game.Events.Add($"{team.Name} 투수 교체 · {team.Pitchers.Single(x => x.Id == id).Name}");
        }
        else
        {
            var slot = Integer(body, "slot"); var used = home ? game.HomeUsedBatters : game.AwayUsedBatters;
            var lineup = home ? game.HomeLineup : game.AwayLineup;
            if (slot > 8 || team.Batters.All(x => x.Id != id) || used.Contains(id)) throw new DiamondInputError("아직 출전하지 않은 벤치 타자와 타순을 선택해 주세요.");
            var replaced = lineup[slot]; used.Add(id); lineup[slot] = id;
            for (var i = 0; i < 3; i++) if (game.Bases[i]?.PlayerId == replaced) game.Bases[i] = game.Bases[i]! with { PlayerId = id };
            game.Events.Add($"{team.Name} {slot + 1}번 교체 · {team.Batters.Single(x => x.Id == id).Name}");
        }
    }
    private static void AutoRelieve(DiamondSeasonSave save, DiamondSeasonGame game, bool simulated)
    {
        var side = DiamondSeasonRules.FieldingTeam(game);
        if (!simulated && side == save.Team) return;
        var home = side == game.HomeTeam; var current = home ? game.HomePitcher : game.AwayPitcher;
        var used = home ? game.HomeUsedPitchers : game.AwayUsedPitchers;
        var pitches = game.PlayerStats.GetValueOrDefault(current)?.PitchCount ?? 0;
        var team = save.Teams.Single(x => x.Code == side);
        var stamina = DiamondEngine.Clamp(team.Pitchers.Single(x => x.Id == current).Profile?.GameRatings?.Stamina ?? 60, 25, 95);
        if (pitches < (used.Count == 1 ? 105 + (stamina - 60) * .8 : 35 + (stamina - 60) * .45)) return;
        var next = team.Pitchers.Where(x => !used.Contains(x.Id)).OrderBy(x => x.Era ?? 5).ThenByDescending(x => x.Tbf).FirstOrDefault();
        if (next == null) return;
        used.Add(next.Id); if (home) game.HomePitcher = next.Id; else game.AwayPitcher = next.Id;
        game.Events.Add($"{team.Name} 투수 교체 · {next.Name}");
    }
    private void CompleteDayIfNeeded(DiamondSeasonSave save, string actor, CancellationToken token)
    {
        if (save.Game is not { Complete: true } selected) return;
        var fixture = save.Schedule.Single(x => x.Id == selected.Id);
        if (fixture.Complete) return;
        RecordGame(save, selected);
        // Durable outbox is committed atomically with standings and the complete fixture.
        if (_gameCompleted != null) save.PendingRewards.Add(Clone(selected));
        foreach (var other in save.Schedule.Where(x => x.Day == save.Day && !x.Complete).ToArray())
        { var game = StartGame(save, other, actor); Simulate(save, game, actor, false, token); RecordGame(save, game); }
        save.Day++; save.Complete = save.Day > save.TotalDays;
    }
    private static void RecordGame(DiamondSeasonSave save, DiamondSeasonGame game)
    {
        var fixture = save.Schedule.Single(x => x.Id == game.Id); if (fixture.Complete) return;
        fixture.Complete = true; fixture.HomeRuns = game.HomeRuns; fixture.AwayRuns = game.AwayRuns;
        foreach (var (id, stat) in game.PlayerStats)
        {
            if (!save.PlayerStats.TryGetValue(id, out var total)) save.PlayerStats[id] = total = new() { PlayerId = id, Name = stat.Name, Team = stat.Team };
            DiamondSeasonRules.Accumulate(total, stat);
        }
    }
}
