using UnityEngine;
using Diamond.Model;

namespace Diamond.Sim
{
    /// <summary>Field geometry shared with the web game (lib/stadium-world.ts, lib/field-play.ts).</summary>
    public static class Field
    {
        public const double BasePath = 27.432;
        public const double MoundDistance = 18.44;
        public const double MoundHeight = 0.254;
        public const double MoundRadius = 2.7432;
        public const double CentreField = 122;
        public const double FoulLine = 99;

        // Web axes -> Unity axes. The web game is right-handed with the outfield at -z; Unity is
        // left-handed with +z forward, so only z is negated and x stays to the batter's right.
        public static Vector3 ToUnity(double x, double y, double z) => new Vector3((float)x, (float)y, (float)-z);
        public static Vector3 ToUnity(Position3 p) => ToUnity(p.x, p.y, p.z);

        static readonly double BaseCorner = BasePath / System.Math.Sqrt(2);

        /// <summary>Home, first, second, third, home (web axes).</summary>
        public static readonly Position3[] Bases =
        {
            new Position3 { x = 0, y = 0, z = 0 },
            new Position3 { x = BaseCorner, y = 0, z = -BaseCorner },
            new Position3 { x = 0, y = 0, z = -BaseCorner * 2 },
            new Position3 { x = -BaseCorner, y = 0, z = -BaseCorner },
            new Position3 { x = 0, y = 0, z = 0 },
        };

        /// <summary>1B, 2B, SS, 3B, LF, CF, RF default positions (web axes).</summary>
        public static readonly Position3[] DefensiveSpots =
        {
            new Position3 { x = 17.5, y = 0, z = -23 }, new Position3 { x = 11, y = 0, z = -32 },
            new Position3 { x = -10, y = 0, z = -32 }, new Position3 { x = -18, y = 0, z = -22 },
            new Position3 { x = -43, y = 0, z = -76 }, new Position3 { x = 0, y = 0, z = -91 },
            new Position3 { x = 43, y = 0, z = -76 },
        };
        public static readonly string[] DefensiveSlots = { "1B", "2B", "SS", "3B", "LF", "CF", "RF" };
    }
}
