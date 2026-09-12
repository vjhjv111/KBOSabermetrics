namespace NaverSabermetrics.Web;

public sealed partial class DiamondEngine
{
    /// <summary>Joint observed pitch/location/velocity sampling, with a game-space body-contact calibration.</summary>
    public DiamondPitch CreateAiPitch(DiamondGame game, long now)
    {
        var profile = game.PitchingProfile;
        var hand = data.BatsLeft(game.Batter, game.Pitcher) ? "L" : "R";
        var supported = data.Arsenal(game.Pitcher).Select(p => p.Type).ToHashSet(StringComparer.Ordinal);
        var all = profile?.Source == "observed" ? profile.Bins.Where(b => supported.Contains(b.Type) && b.Count > 0
            && double.IsFinite(b.X) && double.IsFinite(b.Y) && double.IsFinite(b.Velocity) && b.Velocity > 0).ToArray() : [];
        var sameHand = all.Where(b => b.BatterHand == hand).ToArray();
        var exact = sameHand.Where(b => b.Balls == game.Balls && b.Strikes == game.Strikes).ToArray();
        var pool = exact.Sum(NormalCount) >= 24 ? exact : sameHand.Sum(NormalCount) >= 40 ? sameHand : all;
        // A few innings must not make one HBP a huge probability. The prior is a game tuning value,
        // close to the observed league 0.385% per pitch, not HBP/TBF (a different denominator).
        var hbpRate = profile is { TotalPitchCount: > 0, HitByPitchRate: not null }
            ? Clamp((profile.HitByPitchRate.Value * profile.TotalPitchCount + .004 * 200) / (profile.TotalPitchCount + 200), 0, .03)
            : .004;
        if (Rand() < hbpRate)
        {
            var hbpPool = sameHand.Any(b => b.HitByPitchCount > 0) ? sameHand : all;
            var observed = WeightedBin(hbpPool, true);
            var type = observed?.Type ?? PickAiPitch(game.Pitcher);
            var speed = observed?.Velocity ?? data.Arsenal(game.Pitcher).First(p => p.Type == type).Velocity;
            if (observed is not null)
            {
                var original = PitchAt(game, type, new(observed.X, observed.Y), speed, .85, now);
                if (original.BodyHit is not null && !InZone(original.Target)) return original;
            }
            // The enlarged game strike zone scales real HBP endpoints past the character.
            // Put this rare event on the visible body and verify actual contact; the result is
            // still decided by EvaluatePitch (a swing/zone pitch cannot be awarded an HBP).
            var bodySide = hand == "L" ? 1 : -1;
            for (var i = 0; i < 8; i++)
            {
                var pitch = PitchAt(game, type, new(bodySide * (1.65 + Rand() * .15), -.1 + Rand() * .8), speed, .85, now);
                if (pitch.BodyHit is not null && !InZone(pitch.Target)) return pitch;
            }
        }

        var first = WeightedBin(pool, false);
        var selectedType = first?.Type ?? PickAiPitch(game.Pitcher);
        var selectedSpeed = first?.Velocity ?? data.Arsenal(game.Pitcher).First(p => p.Type == selectedType).Velocity;
        var zone = first is not null ? InZone(new(first.X, first.Y))
            : Rand() < (data.Discipline(game.Pitcher, "pitcher")?.ZonePitchRate ?? .48);
        if (first is not null)
        {
            var pitch = PitchAt(game, first.Type, new(first.X, first.Y), first.Velocity, .85, now);
            if (pitch.BodyHit is null) return pitch;
            // Retain the chosen type and strike/ball class when the static ready pose overlaps
            // an ordinary measured pitch. Re-sampling avoids turning normal inside balls into
            // frequent HBP merely because the game's plate and character have different scales.
            bool Matches(DiamondPitchLocationBin b) => b.Type == selectedType && NormalCount(b) > 0 && InZone(new(b.X, b.Y)) == zone;
            var candidates = pool.Where(Matches).ToArray();
            var wider = all.Where(Matches).ToArray();
            for (var i = 0; i < 12; i++)
            {
                var alternative = WeightedBin(i < 6 ? candidates : wider, false);
                if (alternative is null) break;
                pitch = PitchAt(game, alternative.Type, new(alternative.X, alternative.Y), alternative.Velocity, .85, now);
                if (pitch.BodyHit is null) return pitch;
            }
        }
        // Missing coordinates / all candidates overlap: bounded safe game locations, still using
        // the selected pitch type, speed and strike/ball class. Manual/PvP physics is unchanged.
        for (var i = 0; i < 8; i++)
        {
            var target = zone ? new DiamondVec((Rand() - .5) * 1.35, (Rand() - .5) * 1.25)
                : new DiamondVec((hand == "L" ? -1 : 1) * (1.15 + Rand() * .65), (Rand() - .5) * 1.8);
            var pitch = PitchAt(game, selectedType, target, selectedSpeed, .85, now);
            if (pitch.BodyHit is null) return pitch;
        }
        return PitchAt(game, selectedType, zone ? new(0, -.4) : new(hand == "L" ? -2.5 : 2.5, -.4), selectedSpeed, .85, now);
    }

    private static bool InZone(DiamondVec v) => Math.Abs(v.X) <= 1 && Math.Abs(v.Y) <= 1;
    private static int NormalCount(DiamondPitchLocationBin b) => Math.Max(0, b.Count - b.HitByPitchCount);
    private DiamondPitchLocationBin? WeightedBin(IReadOnlyList<DiamondPitchLocationBin> bins, bool hbp)
    {
        var total = bins.Sum(b => hbp ? b.HitByPitchCount : NormalCount(b));
        if (total <= 0) return null;
        var value = Rand() * total;
        foreach (var bin in bins)
        {
            var weight = hbp ? bin.HitByPitchCount : NormalCount(bin);
            if (weight <= 0) continue;
            value -= weight; if (value < 0) return bin;
        }
        return bins.Last(b => (hbp ? b.HitByPitchCount : NormalCount(b)) > 0);
    }
}
