using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NaverSabermetrics.Web;

/// <summary>Server-authoritative port of action-engine.ts and its collision/flight helpers.</summary>
public sealed class DiamondEngine(DiamondData data, Func<double>? random = null)
{
    public const int SwingContactMs = 95;
    private readonly Func<double> _random = random ?? CryptoRandom;
    private static readonly Dictionary<string, (double X, double Y)> Breaks = new(StringComparer.Ordinal)
    {
        ["fastball"] = (.02, .03), ["slider"] = (.42, .18), ["curve"] = (.12, .58),
        ["changeup"] = (-.25, .35), ["splitter"] = (-.08, .5), ["sinker"] = (-.3, .26), ["cutter"] = (.2, .1)
    };
    public static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
    // Math.Round uses bankers' rounding; the original JavaScript rounds towards +infinity at a tie.
    public static double Round(double value) => Math.Floor(value + .5);
    public static double CryptoRandom()
    {
        Span<byte> bytes = stackalloc byte[4]; RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes) / 4294967296d;
    }
    public double Rand() => _random();
    private double Normal() => Math.Sqrt(-2 * Math.Log(Math.Max(1e-8, Rand()))) * Math.Cos(2 * Math.PI * Rand());
    public (double Contact, double Power, double Control, double Strikeout) Attributes(string batter, string pitcher)
    {
        var b = data.Batter(batter); var p = data.Pitcher(pitcher);
        return (Round(Clamp(100 - b.So / b.Pa * 170, 25, 95)), Round(Clamp((b.Slg - b.Avg) * 200 + 28, 20, 95)),
            Round(Clamp(100 - p.Bb / p.Tbf * 400, 30, 95)), Round(Clamp(p.So / p.Tbf * 240, 20, 95)));
    }
    public string PickAiPitch(string pitcher)
    {
        var arsenal = data.Arsenal(pitcher); var value = Rand() * arsenal.Sum(x => x.Usage);
        foreach (var pitch in arsenal) { value -= pitch.Usage; if (value <= 0) return pitch.Type; }
        return arsenal[0].Type;
    }
    public DiamondPitch CreatePitch(DiamondGame game, string type, DiamondVec aim, double quality, long now)
    {
        var actual = data.Arsenal(game.Pitcher).FirstOrDefault(p => p.Type == type);
        if (actual is null || !Breaks.TryGetValue(type, out var bend)) throw new DiamondInputError("선수가 사용하는 구종을 선택해 주세요.");
        var p = data.Pitcher(game.Pitcher);
        var control = Clamp(1 - p.Bb / p.Tbf * 4, .4, .92); var scatter = (1 - quality) * .65 + (1 - control) * .3;
        var target = new DiamondVec(Clamp(aim.X + Normal() * scatter, -2, 2), Clamp(aim.Y + Normal() * scatter, -2, 2));
        var velocity = Round((actual.Velocity + (quality - .5) * 3 + (Rand() - .5) * 2) * 10) / 10;
        var hand = data.ThrowsLeft(game.Pitcher) ? -1 : 1;
        var under = data.Underhand(game.Pitcher); var scale = (data.Profile(game.Pitcher, "pitcher")?.HeightCm ?? 185) / 185;
        var factor = game.Pace switch { "practice" => 1.85, "real" => 1.15, _ => 1d };
        var pitch = new DiamondPitch
        {
            Id = game.PitchCount + 1, Type = type, Velocity = velocity, ReleaseAt = now + (game.Mode == "pvp" ? 1900 : 1200),
            FlightMs = Round(18.44 / (velocity / 3.6) * 1000 * factor), ReleaseX = -(under ? .58 : .33) * hand * scale,
            ReleaseY = (under ? 1.08 : 1.84) * scale, ReleaseZ = -18.44 + (under ? .22 : .12) * scale,
            Target = target, BreakX = bend.X * hand, BreakY = bend.Y, Quality = quality, Resolved = false
        };
        pitch.BodyHit = FindBodyHit(game.Batter, game.Pitcher, pitch); return pitch;
    }
    public static DiamondPosition BallPosition(DiamondPitch p, double time)
    {
        var u = Clamp((time - p.ReleaseAt) / p.FlightMs, 0, 1.35); var bend = Math.Sin(Math.PI * Math.Min(1, u));
        return new((p.ReleaseX ?? -.33) * (1 - u) + p.Target.X * .5 * u - p.BreakX * bend,
            (p.ReleaseY ?? 1.84) * (1 - u) + (1.05 + p.Target.Y * .55) * u + p.BreakY * bend + .12 * bend,
            (p.ReleaseZ ?? -18.44) * (1 - u));
    }
    public DiamondBodyHit? FindBodyHit(string batter, string pitcher, DiamondPitch pitch) =>
        SweepBody(at => BallPosition(pitch, at), pitch.ReleaseAt + pitch.FlightMs, pitch.FlightMs, data.Colliders(data.BatsLeft(batter, pitcher)));

    public DiamondResult EvaluatePitch(DiamondGame game, DiamondSwing? swing, long now)
    {
        var pitch = game.Pitch ?? throw new DiamondInputError("진행 중인 투구가 없습니다.");
        var att = Attributes(game.Batter, game.Pitcher); var arrival = pitch.ReleaseAt + pitch.FlightMs;
        var inZone = Math.Abs(pitch.Target.X) <= 1 && Math.Abs(pitch.Target.Y) <= 1;
        var result = new DiamondResult { Id = pitch.Id, At = now, SwingAt = swing?.At, SwingAim = swing?.Aim, PlateLocation = pitch.Target };
        var bodyHit = pitch.BodyHit ?? FindBodyHit(game.Batter, game.Pitcher, pitch);
        if (bodyHit != null && (swing == null || swing.At - SwingContactMs > bodyHit.At))
        {
            result.BodyHit = bodyHit; result.SwingAt = null; result.SwingAim = null;
            if (inZone) { result.Kind = "strike"; result.Outcome = "STRIKE"; result.Label = "데드볼 스트라이크"; }
            else { result.Kind = "hbp"; result.Outcome = "HBP"; result.Label = "사구 · 몸에 맞는 공"; result.Points = 1; result.PlateEnded = true; }
            return result;
        }
        if (swing == null)
        {
            result.Kind = inZone ? "strike" : "ball"; result.Label = inZone ? "스트라이크" : "볼";
            result.Outcome = inZone ? "STRIKE" : "BALL"; return result;
        }
        var timing = swing.At - arrival;
        var dx = swing.Aim.X - pitch.Target.X; var dy = swing.Aim.Y - pitch.Target.Y; var error = Math.Sqrt(dx * dx + dy * dy);
        var tolerance = (game.Pace == "practice" ? 145 : 100) * (.75 + att.Contact / 200); var radius = .34 + att.Contact * .0042;
        result.Timing = Round(timing); result.AimError = Round(error * 1000) / 1000;
        var missed = Math.Abs(timing) > tolerance * 1.35 || error > radius * 1.6 || swing.At - SwingContactMs > arrival;
        if (bodyHit != null && (missed || bodyHit.At <= swing.At))
        { result.BodyHit = bodyHit; result.Kind = "strike"; result.Label = "데드볼 스트라이크"; result.Outcome = "MISS"; return result; }
        if (missed) { result.Kind = "strike"; result.Label = "헛스윙"; result.Outcome = "MISS"; return result; }
        result.Contact = new(swing.At, new(pitch.Target.X * .5, Math.Max(.065, 1.05 + pitch.Target.Y * .55), 0));
        if (Math.Abs(timing) > tolerance || error > radius)
        {
            result.Kind = "foul"; result.Label = "파울"; result.Outcome = "FOUL"; result.Trajectory = "foul";
            result.Quality = .15; result.ExitSpeed = Round(65 + att.Power * .35); result.LaunchAngle = 20;
            result.Direction = (timing < 0 ? -1 : 1) * (data.BatsLeft(game.Batter, game.Pitcher) ? -1 : 1) * (Math.PI / 2 + .3);
            result.Distance = CarryDistance(result.ExitSpeed, result.LaunchAngle, result.Contact.Position.Y); return result;
        }
        var q = Clamp(1 - Math.Abs(timing) / tolerance * .55 - error / radius * .55, 0, 1);
        var angle = Clamp(24 + (pitch.Target.Y - swing.Aim.Y) * 44, -15, 65); var exit = 90 + q * 58 + att.Power * .43;
        var trajectory = angle <= 10 ? "ground" : angle <= 25 ? "line" : "fly";
        var distance = trajectory == "ground" ? 0 : CarryDistance(exit, angle, result.Contact.Position.Y);
        result.Quality = q; result.LaunchAngle = angle; result.ExitSpeed = exit; result.Distance = distance;
        result.Direction = Clamp(timing / tolerance * .95 * (data.BatsLeft(game.Batter, game.Pitcher) ? -1 : 1) + (pitch.Target.X - swing.Aim.X) * .2, -1.3, 1.3);
        result.PlateEnded = true; result.Trajectory = trajectory;
        var outLabel = trajectory == "ground" ? "땅볼 아웃" : trajectory == "line" ? "직선타 아웃" : "뜬공 아웃";
        if (distance >= 105 && angle >= 14 && angle <= 48) { result.Kind = "hit"; result.Outcome = "HR"; result.Label = "홈런!"; result.Points = 4; }
        else if (q < .28 || angle > 50 || (angle > 28 && distance < 70)) { result.Kind = "out"; result.Outcome = "OUT"; result.Label = outLabel; }
        else if (distance >= 80) { result.Kind = "hit"; result.Outcome = "2B"; result.Label = "2루타!"; result.Points = 2; }
        else if (q >= .42) { result.Kind = "hit"; result.Outcome = "1B"; result.Label = "안타!"; result.Points = 1; }
        else { result.Kind = "out"; result.Outcome = "OUT"; result.Label = outLabel; }
        return result;
    }
    public DiamondSwing? AiSwing(DiamondGame game)
    {
        var p = game.Pitch!; var b = data.Batter(game.Batter);
        var inside = Math.Abs(p.Target.X) <= 1 && Math.Abs(p.Target.Y) <= 1;
        var discipline = data.Discipline(game.Batter, "batter");
        if (Rand() > (inside ? discipline?.ZoneSwingRate ?? .65 : discipline?.ChaseRate ?? .3)) return null;
        var timingStd = (game.Pace == "practice" ? 80 : 60) * (.7 + b.So / b.Pa * 2);
        var contact = inside ? discipline?.ZoneContactRate ?? .85 : discipline?.OutZoneContactRate ?? .65;
        var aimStd = .14 + (1 - contact) * 1.6;
        return new(p.ReleaseAt + p.FlightMs + Normal() * timingStd,
            new(p.Target.X + Normal() * aimStd, p.Target.Y + Normal() * aimStd));
    }
    public static void FinishPitch(DiamondGame game, DiamondResult result)
    {
        if (game.Pitch == null || game.Pitch.Resolved) return;
        game.Pitch.Resolved = true;
        if (result.Kind == "strike" && ++game.Strikes >= 3)
        { result.PlateEnded = true; result.Outcome = "K"; result.Label = "삼진 아웃"; result.Kind = "out"; }
        if (result.Kind == "foul" && game.Strikes < 2) game.Strikes++;
        if (result.Kind == "ball" && ++game.Balls >= 4)
        { result.PlateEnded = true; result.Outcome = "BB"; result.Label = "볼넷 출루"; result.Kind = "walk"; result.Points = 1; }
        if (result.PlateEnded) { game.Round++; game.Balls = 0; game.Strikes = 0; }
        game.Score += result.Points; game.Pitch.Reaction = result; game.History.Add(result);
    }
    public static string Side(DiamondGame game, string actor) => game.Host == actor ? game.HostRole : game.HostRole == "batter" ? "pitcher" : "batter";
    public static DiamondView View(DiamondGame game, string actor, long now) => new(game.Format, game.Code, game.Mode, Side(game, actor),
        game.Batter, game.Pitcher, game.Pace, game.Round, game.Balls, game.Strikes, game.Score, game.PitchCount,
        game.Pitch, game.History, game.Mode == "pvp" && game.Guest == null, game.Round >= 6,
        game.Round >= 6 ? game.Score >= 4 ? "batter" : "pitcher" : null, now, game.ExpiresAt, game.Roster);

    public static double CarryDistance(double speedKph, double angle, double height = 1.05)
    {
        var speed = speedKph / 3.6 * Math.Sqrt(.63); var radians = angle * Math.PI / 180; var up = speed * Math.Sin(radians);
        return Round(speed * Math.Cos(radians) * (up + Math.Sqrt(up * up + 2 * 9.81 * Math.Max(0, height))) / 9.81);
    }
    private static DiamondPosition Sub(DiamondPosition a, DiamondPosition b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static DiamondPosition Mix(DiamondPosition a, DiamondPosition b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    private static double Dot(DiamondPosition a, DiamondPosition b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static bool SegmentTouchesBody(DiamondPosition from, DiamondPosition to, IReadOnlyList<DiamondCapsule> capsules)
    {
        foreach (var capsule in capsules)
        {
            var d1 = Sub(to, from); var d2 = Sub(capsule.B, capsule.A); var r = Sub(from, capsule.A);
            var aa = Dot(d1, d1); var ee = Dot(d2, d2); var f = Dot(d2, r); double s = 0, t = 0;
            if (aa <= 1e-14) t = ee > 1e-14 ? Clamp(f / ee, 0, 1) : 0;
            else
            {
                var c = Dot(d1, r);
                if (ee <= 1e-14) s = Clamp(-c / aa, 0, 1);
                else
                {
                    var bb = Dot(d1, d2); var denom = aa * ee - bb * bb;
                    s = denom > 1e-14 ? Clamp((bb * f - c * ee) / denom, 0, 1) : 0; t = (bb * s + f) / ee;
                    if (t < 0) { t = 0; s = Clamp(-c / aa, 0, 1); }
                    else if (t > 1) { t = 1; s = Clamp((bb - c) / aa, 0, 1); }
                }
            }
            var d = Sub(Mix(from, to, s), Mix(capsule.A, capsule.B, t));
            if (Dot(d, d) <= (capsule.Radius + .065) * (capsule.Radius + .065)) return true;
        }
        return false;
    }
    public static DiamondBodyHit? SweepBody(Func<double, DiamondPosition> positionAt, double arrival, double flightMs, IReadOnlyList<DiamondCapsule> capsules)
    {
        var start = arrival - flightMs * .065; var end = arrival + flightMs * .065; const int steps = 260; var previous = start;
        for (var i = 0; i <= steps; i++)
        {
            var at = start + (end - start) * i / steps;
            if (SegmentTouchesBody(positionAt(previous), positionAt(at), capsules))
            {
                var lo = previous; var hi = at;
                for (var j = 0; j < 14; j++)
                {
                    var mid = (lo + hi) / 2;
                    if (SegmentTouchesBody(positionAt(previous), positionAt(mid), capsules)) hi = mid; else lo = mid;
                }
                return new(hi, positionAt(hi));
            }
            previous = at;
        }
        return null;
    }
}
