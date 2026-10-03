using System;
using Diamond.Model;

namespace Diamond.Sim
{
    /// <summary>
    /// Visual strike zone. The server's aim space treats |aim| = 1 as the zone edge and maps it to +/-0.5 m wide and
    /// 1.05 +/- 0.55 m high (twice a real zone). The 3D view draws a regulation-sized zone instead: the 17-inch plate plus the
    /// ball radius wide, and from just under the knee to between the belt and the shoulders for a ~1.85 m batter.
    /// Everything shown (pitch paths, contact points, mouse aim) goes through this mapping; server results are unchanged.
    /// </summary>
    public static class StrikeZone
    {
        public const float HalfWidth = 0.25f;
        public const float CenterY = 0.83f;
        public const float HalfHeight = 0.33f;

        public const double WebHalfWidth = 0.5;
        public const double WebCenterY = 1.05;
        public const double WebHalfHeight = 0.55;

        /// <summary>
        /// Beyond the zone edge the aim axis is stretched less than the server's (0.4 m per aim unit instead of 0.25 inside): the server's batter
        /// body starts at aim 1.34 (0.67 m in its mapping) and its torso is centred at aim 1.8, so a pitch the server scores as hitting the
        /// batter has to look like it hits him. The batter stands at BatterX to match.
        /// </summary>
        public const double OutsideScale = 0.4;
        public const double BatterX = 0.65;

        /// <summary>World x of an aim-space x (web axes, metres).</summary>
        public static double X(double aimX)
        {
            var m = Math.Abs(aimX);
            return Math.Sign(aimX) * (m <= 1 ? m * HalfWidth : HalfWidth + (m - 1) * OutsideScale);
        }

        /// <summary>Inverse of <see cref="X"/>: the aim-space x of a world x.</summary>
        public static double AimX(double worldX)
        {
            var m = Math.Abs(worldX);
            return Math.Sign(worldX) * (m <= HalfWidth ? m / HalfWidth : 1 + (m - HalfWidth) / OutsideScale);
        }
        /// <summary>World y of an aim-space y (metres).</summary>
        public static double Y(double aimY) => CenterY + aimY * HalfHeight;

        /// <summary>Re-maps a point given in the web game's plate mapping (e.g. a server contact position) to the regulation zone.</summary>
        public static Position3 FromWeb(Position3 p) => new Position3
        {
            x = X(p.x / WebHalfWidth),
            y = CenterY + (p.y - WebCenterY) / WebHalfHeight * HalfHeight,
            z = p.z,
        };
    }
}
