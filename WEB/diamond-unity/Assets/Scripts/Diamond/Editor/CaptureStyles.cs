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
    /// Renders overhand / sidearm / underhand deliveries for right- and left-handed pitchers around the release (and the
    /// left-handed batter) and logs how far the hand is from the release point. Needs a GPU (no -nographics).
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod Diamond.EditorTools.CaptureStyles.Run
    /// </summary>
    public static class CaptureStyles
    {
        static void Shot(Camera cam, string file, Vector3 position, Vector3 lookAt, float fov)
        {
            cam.transform.position = position; cam.transform.LookAt(lookAt); cam.fieldOfView = fov;
            var rt = new RenderTexture(800, 600, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(800, 600, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 800, 600), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }

        // Release offsets of the web game's PITCH_RELEASE (local, right-handed): x, y (above the mound), z.
        static (double x, double y, double z) Local(string style) => style switch
        {
            "sidearm" => (-0.72, 1.43, 0.20),
            "underhand" => (-0.58, 1.08, 0.22),
            _ => (-0.33, 1.84, 0.12),
        };

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BattingPrototype.unity");
            Object.FindFirstObjectByType<StadiumBuilder>().Build();
            var demo = Object.FindFirstObjectByType<PitchReplayDemo>();
            demo.Setup();
            var cam = Camera.main;
            Object.DestroyImmediate(cam.GetComponent<GameCamera>());
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
            Directory.CreateDirectory(dir);
            var mound = Field.ToUnity(0, Field.MoundHeight, -Field.MoundDistance);

            foreach (var style in new[] { "overhand", "sidearm", "underhand" })
            foreach (var left in new[] { false, true })
            {
                demo.SetPitcher(left, style);
                var hand = left ? -1 : 1;
                var (lx, ly, lz) = Local(style);
                var pitch = new Pitch
                {
                    id = 9, type = "fastball", velocity = 140, releaseAt = 2200, flightMs = 850,
                    releaseX = lx * hand, releaseY = Field.MoundHeight + ly, releaseZ = -Field.MoundDistance + lz,
                    target = new Vec2 { x = 0, y = 0 }, breakX = 0.02, breakY = 0.03,
                };
                demo.Play(pitch, null);
                var tag = $"{style}_{(left ? "L" : "R")}";
                var target = Field.ToUnity(pitch.releaseX.Value, pitch.releaseY.Value, pitch.releaseZ.Value);
                foreach (var dt in new[] { -250, 0, 200 })
                {
                    demo.Evaluate(pitch.releaseAt + dt);
                    var d = Vector3.Distance(demo.ThrowingHandPosition, target);
                    Debug.Log($"STYLE {tag} dt={dt} handToRelease={d:0.00}m shoulderY={demo.ThrowingShoulderHeight():0.00}");
                    // Side view from the first-base side, and the batter's view looking at the mound.
                    Shot(cam, Path.Combine(dir, $"style_{tag}_{dt}_side.png"), mound + new Vector3(5f, 1.4f, 0f), mound + new Vector3(0f, 1.2f, 0f), 38);
                    if (dt == 0) Shot(cam, Path.Combine(dir, $"style_{tag}_front.png"), mound + new Vector3(0f, 1.6f, -7f), mound + new Vector3(0f, 1.2f, 0f), 30);
                }
            }

            // Left-handed batter in the box: stance and swing contact.
            demo.SetPitcher(false, "overhand");
            demo.SetBatter(true);
            var sample = new Pitch
            {
                id = 10, type = "fastball", velocity = 140, releaseAt = 2200, flightMs = 850,
                releaseX = -0.33, releaseY = 1.84, releaseZ = -18.32, target = new Vec2 { x = 0, y = 0 }, breakX = 0.02, breakY = 0.03,
            };
            var contact = sample.releaseAt + sample.flightMs;
            demo.Play(sample, contact);
            foreach (var (name, ms) in new[] { ("stance", sample.releaseAt - 500), ("contact", contact) })
            {
                demo.Evaluate(ms);
                Shot(cam, Path.Combine(dir, $"lefty_{name}.png"), new Vector3(0f, 1.9f, -6.6f), new Vector3(0.6f, 1.0f, 0f), 30);
            }
            Debug.Log("STYLES ok");
        }
    }
}
