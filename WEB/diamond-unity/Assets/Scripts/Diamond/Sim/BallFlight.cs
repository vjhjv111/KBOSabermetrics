using System;
using Diamond.Model;

namespace Diamond.Sim
{
    /// <summary>Pure ports of lib/action-engine.ts ballPosition and lib/batted-ball.ts. Web axes, metres, ms.</summary>
    public static class BallFlight
    {
        const double Gravity = 9.81;
        static readonly double SpeedScale = Math.Sqrt(0.63);
        static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));

        /// <summary>Ball position of a pitch at server-clock time <paramref name="time"/> (ms).</summary>
        public static Position3 Pitched(Pitch p, double time)
        {
            var u = Clamp((time - p.releaseAt) / p.flightMs, 0, 1.35);
            var bend = Math.Sin(Math.PI * Math.Min(1, u));
            return new Position3
            {
                x = (p.releaseX ?? -0.33) * (1 - u) + p.target.x * 0.5 * u - p.breakX * bend,
                y = (p.releaseY ?? 1.84) * (1 - u) + (1.05 + p.target.y * 0.55) * u + p.breakY * bend + 0.12 * bend,
                z = (p.releaseZ ?? -18.44) * (1 - u),
            };
        }

        public static long CarryDistance(double exitSpeed, double angle, double height = 1.05)
        {
            var speed = exitSpeed / 3.6 * SpeedScale;
            var radians = angle * Math.PI / 180;
            var up = speed * Math.Sin(radians);
            return (long)Math.Round(speed * Math.Cos(radians) * (up + Math.Sqrt(up * up + 2 * Gravity * Math.Max(0, height))) / Gravity, MidpointRounding.AwayFromZero);
        }

        /// <summary>Batted ball position, or null before contact. Mirrors battedBallPosition (flight, bounce and roll).</summary>
        public static Position3 Batted(PitchResult result, double now)
        {
            var contact = result.contact;
            if (contact == null || now < contact.at) return null;
            var t = (now - contact.at) / 1000;
            var speed = result.exitSpeed / 3.6 * SpeedScale;
            var angle = result.launchAngle * Math.PI / 180;
            var horizontal = speed * Math.Cos(angle);
            var vertical = speed * Math.Sin(angle);
            var height = contact.position.y;
            var landing = (vertical + Math.Sqrt(vertical * vertical + 2 * Gravity * Math.Max(0, height))) / Gravity;
            double distance, y;
            if (t <= landing)
            {
                distance = horizontal * t;
                y = height + vertical * t - Gravity * t * t / 2;
            }
            else
            {
                var roll = t - landing;
                var rollSpeed = horizontal * (result.trajectory == "ground" ? 0.72 : 0.35);
                const double friction = 7;
                var rolling = Math.Min(roll, rollSpeed / friction);
                distance = horizontal * landing + rollSpeed * rolling - friction * rolling * rolling / 2;
                y = 0.065 + Math.Abs(Math.Sin(roll * 13)) * 0.16 * Math.Exp(-roll * 2.8);
            }
            return new Position3
            {
                x = contact.position.x + Math.Sin(result.direction) * distance,
                y = Math.Max(0.065, y),
                z = contact.position.z - Math.Cos(result.direction) * distance,
            };
        }
    }
}
