using System;
using NaverRelay.Models;

namespace NaverRelay.Parsing
{
    internal static class PitchResultClassifier
    {
        public static PitchResultType Parse(string? rawCode)
        {
            return rawCode?.Trim().ToUpperInvariant() switch
            {
                "B" => PitchResultType.Ball,
                "F" => PitchResultType.Foul,
                "H" => PitchResultType.InPlay,
                "S" => PitchResultType.SwingingStrike,
                "T" => PitchResultType.CalledStrike,
                "W" => PitchResultType.BuntFoul,
                "V" => PitchResultType.BuntSwingingStrike,
                _ => PitchResultType.Unknown,
            };
        }

        public static void SetDerivedFlags(PitchEvent pitch)
        {
            pitch.IsCalledStrike = pitch.PitchResult == PitchResultType.CalledStrike;
            pitch.IsWhiff = pitch.PitchResult is PitchResultType.SwingingStrike
                or PitchResultType.BuntSwingingStrike;
            pitch.IsInPlay = pitch.PitchResult == PitchResultType.InPlay;
            pitch.IsSwing = pitch.PitchResult is PitchResultType.SwingingStrike
                or PitchResultType.Foul
                or PitchResultType.BuntFoul
                or PitchResultType.BuntSwingingStrike
                or PitchResultType.InPlay;
            pitch.IsContact = pitch.PitchResult is PitchResultType.Foul
                or PitchResultType.BuntFoul
                or PitchResultType.InPlay;
        }
    }

    internal static class PitchTrajectoryCalculator
    {
        /// <summary>
        /// Solves the pitch equations at the raw crossPlateY plane. The raw crossPlateY value is a
        /// front/back plane coordinate, not the vertical crossing height. CalculatedCrossPlateZ is
        /// therefore derived from z0/vz0/az at the solved time.
        /// </summary>
        public static bool TryCalculateAtPlate(
            PtsOption pts,
            out double timeSeconds,
            out double calculatedX,
            out double calculatedZ)
        {
            timeSeconds = default;
            calculatedX = default;
            calculatedZ = default;

            if (!pts.Y0.HasValue || !pts.Vy0.HasValue || !pts.Ay.HasValue || !pts.CrossPlateY.HasValue
                || !pts.X0.HasValue || !pts.Vx0.HasValue || !pts.Ax.HasValue
                || !pts.Z0.HasValue || !pts.Vz0.HasValue || !pts.Az.HasValue)
            {
                return false;
            }

            var a = 0.5 * pts.Ay.Value;
            var b = pts.Vy0.Value;
            var c = pts.Y0.Value - pts.CrossPlateY.Value;

            if (!TrySmallestNonNegativeRoot(a, b, c, out var t))
            {
                return false;
            }

            var x = pts.X0.Value + pts.Vx0.Value * t + 0.5 * pts.Ax.Value * t * t;
            var z = pts.Z0.Value + pts.Vz0.Value * t + 0.5 * pts.Az.Value * t * t;

            if (!double.IsFinite(t) || !double.IsFinite(x) || !double.IsFinite(z))
            {
                return false;
            }

            timeSeconds = t;
            calculatedX = x;
            calculatedZ = z;
            return true;
        }

        public static bool? IsInNominalStrikeZone(PitchEvent pitch)
        {
            if (!pitch.CrossPlateX.HasValue || !pitch.CalculatedCrossPlateZ.HasValue
                || !pitch.TopStrikeZone.HasValue || !pitch.BottomStrikeZone.HasValue)
            {
                return null;
            }

            var horizontal = Math.Abs(pitch.CrossPlateX.Value) <= ParserUtilities.HalfPlateWidthFeet;
            var vertical = pitch.CalculatedCrossPlateZ.Value >= pitch.BottomStrikeZone.Value
                && pitch.CalculatedCrossPlateZ.Value <= pitch.TopStrikeZone.Value;
            return horizontal && vertical;
        }

        private static bool TrySmallestNonNegativeRoot(double a, double b, double c, out double root)
        {
            const double epsilon = 1e-12;
            root = default;

            if (Math.Abs(a) < epsilon)
            {
                if (Math.Abs(b) < epsilon)
                {
                    return false;
                }

                var linearRoot = -c / b;
                if (linearRoot < 0 || !double.IsFinite(linearRoot))
                {
                    return false;
                }

                root = linearRoot;
                return true;
            }

            var discriminant = b * b - 4 * a * c;
            if (discriminant < 0 || !double.IsFinite(discriminant))
            {
                return false;
            }

            var sqrt = Math.Sqrt(discriminant);
            var denominator = 2 * a;
            var r1 = (-b + sqrt) / denominator;
            var r2 = (-b - sqrt) / denominator;

            var r1Valid = r1 >= 0 && double.IsFinite(r1);
            var r2Valid = r2 >= 0 && double.IsFinite(r2);

            if (!r1Valid && !r2Valid)
            {
                return false;
            }

            root = r1Valid && r2Valid ? Math.Min(r1, r2) : (r1Valid ? r1 : r2);
            return true;
        }
    }
}
