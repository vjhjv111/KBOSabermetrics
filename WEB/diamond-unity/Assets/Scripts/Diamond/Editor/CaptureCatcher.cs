using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Diamond.Model;
using Diamond.Stadium;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Renders (a) a taken pitch arriving in the catcher's mitt and (b) a swing-and-miss with the return to the stance,
    /// and logs how close the glove gets to the ball. Needs a GPU (no -nographics).
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod Diamond.EditorTools.CaptureCatcher.Run
    /// </summary>
    public static class CaptureCatcher
    {
        static void Shot(Camera cam, string file, Vector3 position, Vector3 lookAt, float fov)
        {
            cam.transform.position = position; cam.transform.LookAt(lookAt); cam.fieldOfView = fov;
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
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
            var cam = Camera.main;
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
            Directory.CreateDirectory(dir);

            var pitch = demo.Pitch;
            var arrival = pitch.releaseAt + pitch.flightMs;

            // (a) Take: the pitch reaches the catcher.
            demo.Play(pitch, null);
            demo.Evaluate(arrival - 600);
            demo.SetResult(new PitchResult { kind = "ball", outcome = "BALL", label = "볼" });
            foreach (var (name, dt) in new[] { ("a0", -400), ("a1", -150), ("a2", 100), ("a3", 500), ("a4", 1500) })
            {
                demo.Evaluate(arrival + dt);
                Debug.Log($"CATCH {name} dt={dt}ms gloveToBall={demo.GloveToBall():0.00}m ball={demo.Ball.position:0.00}");
                Shot(cam, Path.Combine(dir, $"catch_{name}.png"), new Vector3(1.6f, 1.3f, -3.2f), new Vector3(0f, 0.9f, -1.0f), 35);
            }

            // (b) Swing and miss: server says "no contact" right after the swing starts, then hold and return to the stance.
            var contact = arrival;
            demo.Play(pitch, contact);
            demo.Evaluate(contact - 60);
            demo.SetResult(new PitchResult { kind = "strike", outcome = "MISS", label = "헛스윙", swingAt = contact });
            foreach (var (name, dt) in new[] { ("b0", 0), ("b1", 200), ("b2", 450), ("b3", 900), ("b4", 1600), ("b5", 2600) })
            {
                demo.Evaluate(contact + dt);
                Shot(cam, Path.Combine(dir, $"miss_{name}.png"), new Vector3(2.8f, 1.5f, -3.2f), new Vector3(-0.4f, 0.9f, 0f), 38);
            }
            // (c) Wide, high and low pitches: the catcher should slide, turn and rise to receive them.
            foreach (var (tag, tx, ty) in new[] { ("wideR", 1.8, 0.0), ("wideL", -1.8, 0.0), ("high", 0.0, 1.8), ("low", 0.0, -1.8) })
            {
                var extra = new Pitch
                {
                    id = 3, type = "fastball", velocity = 145, releaseAt = pitch.releaseAt, flightMs = pitch.flightMs,
                    releaseX = pitch.releaseX, releaseY = pitch.releaseY, releaseZ = pitch.releaseZ,
                    target = new Vec2 { x = tx, y = ty }, breakX = 0.02, breakY = 0.03,
                };
                demo.Play(extra, null);
                demo.Evaluate(arrival - 800);
                demo.SetResult(new PitchResult { kind = "ball", outcome = "BALL", label = "볼" });
                demo.Evaluate(arrival + 350);
                Debug.Log($"CATCH {tag} gloveToBall={demo.GloveToBall():0.00}m ball={demo.Ball.position:0.00}");
                Shot(cam, Path.Combine(dir, $"catch_{tag}.png"), new Vector3(2.6f, 1.6f, -4.6f), new Vector3(0f, 0.9f, -1.4f), 42);
            }
            Debug.Log("CATCHER ok");
        }
    }
}
