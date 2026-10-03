using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Diamond.Model;
using Diamond.Sim;
using Diamond.Stadium;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Plans and renders four plays (ground-ball out, fly-ball out, single with a runner on first, home run with two on) with the
    /// FieldPlayDirector and saves overview frames to Captures/play_*.png. Needs a GPU (no -nographics).
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod Diamond.EditorTools.CaptureFieldPlay.Run
    /// </summary>
    public static class CaptureFieldPlay
    {
        static void Snap(Camera cam, string file)
        {
            var rt = new RenderTexture(1000, 560, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1000, 560, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1000, 560), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BattingPrototype.unity");
            Object.FindFirstObjectByType<StadiumBuilder>().Build();
            var demo = Object.FindFirstObjectByType<PitchReplayDemo>();
            demo.Setup();
            var director = Object.FindFirstObjectByType<FieldPlayDirector>();
            director.Build();
            var cam = Camera.main;
            Object.DestroyImmediate(cam.GetComponent<GameCamera>());
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
            Directory.CreateDirectory(dir);

            var pitch = new Pitch
            {
                id = 1, type = "fastball", velocity = 145, releaseAt = 2200, flightMs = 850,
                releaseX = -0.33, releaseY = 1.84, releaseZ = -18.32, target = new Vec2 { x = 0, y = 0 }, breakX = 0.02, breakY = 0.03,
            };
            var t0 = pitch.releaseAt + pitch.flightMs;

            PitchResult Make(string outcome, string kind, string trajectory, double exit, double angle, double direction) => new PitchResult
            {
                id = 1, label = outcome, kind = kind, outcome = outcome, trajectory = trajectory, exitSpeed = exit, launchAngle = angle, direction = direction,
                contact = new Contact { at = t0, position = new Position3 { x = 0.2, y = 1.2, z = 0 } }, plateEnded = true, at = t0,
            };

            var plays = new (string name, PitchResult r, string[] before, string[] after, int runs)[]
            {
                ("groundout", Make("OUT", "out", "ground", 125, 4, -0.5), null, null, 0),
                ("flyout", Make("OUT", "out", "fly", 140, 38, 0.1), null, null, 0),
                ("single", Make("1B", "hit", "line", 150, 13, -0.45), new[] { "r1", null, null }, new[] { "b", "r1", null }, 0),
                ("groundsingle", Make("1B", "hit", "ground", 118, 3, -0.05), null, new[] { "b", null, null }, 0),
                ("homerun", Make("HR", "hit", "fly", 172, 28, 0.2), new[] { "r1", "r2", null }, new string[] { null, null, null }, 3),
            };

            foreach (var (name, r, before, after, runs) in plays)
            {
                demo.Play(pitch, t0);
                director.ResetField();
                director.SetRunnersOnBase(before);
                demo.Evaluate(t0 - 600);
                demo.SetResult(r);
                var end = director.Plan(r, "b", before, after, runs);
                Debug.Log($"FIELDPLAY {name} planned end={(end - t0) / 1000.0:0.0}s after contact, verdict at {(director.VerdictMs - t0) / 1000.0:0.0}s");
                foreach (var dt in new[] { 400, 1200, 2200, 3400, 5200, 7600 })
                {
                    var ms = t0 + dt;
                    if (ms > end + 400) break;
                    demo.Evaluate(ms);
                    director.Evaluate(ms);
                    cam.transform.position = new Vector3(0f, 34f, -26f);
                    cam.transform.LookAt(new Vector3(0f, 0f, 36f));
                    cam.fieldOfView = 60;
                    Snap(cam, Path.Combine(dir, $"play_{name}_{dt:0000}.png"));
                    if (name == "groundout" || name == "single" || name == "groundsingle")
                    {
                        // Close-ups of the fielder nearest the ball and of the batter-runner.
                        var ballPos = demo.Ball.position;
                        var near = director.GetComponentsInChildren<Animator>().Where(a => a.gameObject.activeInHierarchy && a.name.StartsWith("Fielder")).OrderBy(a => (a.transform.position - ballPos).sqrMagnitude).First();
                        var runner = director.GetComponentsInChildren<Animator>().FirstOrDefault(a => a.gameObject.activeInHierarchy && a.name == "Runner 0");
                        foreach (var (tag, target) in new[] { ("fielder", near.transform), ("runner", runner != null ? runner.transform : near.transform) })
                        {
                            var c = target.position;
                            cam.transform.position = c + new Vector3(3.5f, 1.6f, -4f); cam.transform.LookAt(c + Vector3.up * 0.9f); cam.fieldOfView = 35;
                            Snap(cam, Path.Combine(dir, $"close_{name}_{tag}_{dt:0000}.png"));
                        }
                    }
                }
            }
            Debug.Log("FIELDPLAY ok");
        }
    }
}
