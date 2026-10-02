using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Diamond.Model;
using Diamond.Sim;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Prints BallFlight values for fixed inputs so they can be diffed against the TypeScript originals
    /// (lib/action-engine.ts ballPosition, lib/batted-ball.ts):
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.PortCheck.Run
    /// </summary>
    public static class PortCheck
    {
        static PitchResult Hit(string trajectory, double exit, double angle, double direction) => new PitchResult
        {
            trajectory = trajectory, exitSpeed = exit, launchAngle = angle, direction = direction,
            contact = new Contact { at = 2700, position = new Position3 { x = 0.2, y = 1.2, z = 0 } },
        };

        [MenuItem("Diamond/Print port check")]
        public static void Run()
        {
            var pitch = new Pitch
            {
                id = 1, type = "slider", velocity = 140, releaseAt = 1000, flightMs = 1700,
                releaseX = -0.33, releaseY = 1.84, releaseZ = -18.44 + 0.12,
                target = new Vec2 { x = 0.4, y = -0.3 }, breakX = 0.42, breakY = 0.18,
            };
            var hits = new[] { Hit("fly", 150, 28, 0.3), Hit("ground", 120, 3, -0.5), Hit("line", 135, 15, 0.1) };
            var result = new
            {
                pitch = new double[] { 0, 1000, 1500, 2000, 2700, 3000 }.Select(t => BallFlight.Pitched(pitch, t)),
                batted = hits.Select(h => new double[] { 2600, 2700, 3000, 4000, 6000, 9000 }.Select(t => BallFlight.Batted(h, t))),
                carry = new[] { BallFlight.CarryDistance(150, 28, 1.2), BallFlight.CarryDistance(120, 40), BallFlight.CarryDistance(100, 10, 0.5) },
            };
            Debug.Log("PORTCHECK " + JsonConvert.SerializeObject(result));
        }
    }
}
