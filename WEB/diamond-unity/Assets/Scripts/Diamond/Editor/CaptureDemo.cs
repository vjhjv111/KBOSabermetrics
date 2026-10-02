using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Diamond.Stadium;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Evaluates the replay demo at chosen game times (ms) and renders Captures/demo_*.png (needs a GPU: no -nographics).
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod Diamond.EditorTools.CaptureDemo.Run
    /// </summary>
    public static class CaptureDemo
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
            var release = demo.Pitch.releaseAt;
            var contact = demo.Result.contact.at;
            var times = new (string name, double ms)[]
            {
                ("a_windup", release - 900), ("b_release", release), ("c_flight", release + 700),
                ("d_precontact", contact - 250), ("e_contact", contact), ("f_after", contact + 600), ("g_late", contact + 2500),
            };
            foreach (var (name, ms) in times)
            {
                demo.Evaluate(ms);
                // Catcher-side elevated view, and a view from the first-base side of the mound.
                Shot(cam, Path.Combine(dir, $"demo_{name}_catcher.png"), new Vector3(2.5f, 2.2f, -6f), new Vector3(0, 1.3f, 10f), 45);
                var mid = new Vector3(0, 1.4f, 9f);
                Shot(cam, Path.Combine(dir, $"demo_{name}_side.png"), mid + new Vector3(16f, 1.2f, 0), mid, 42);
            }
            // Start-of-motion top-down views of each actor (world +x is to the right, +z up in the image).
            foreach (var (name, ms) in new (string, double)[] { ("start", 0), ("pre", release - 1800), ("rel", release) })
            {
                demo.Evaluate(ms);
                var moundPos = new Vector3(0, 0, 18.44f);
                Shot(cam, Path.Combine(dir, $"face_pitcher_{name}.png"), moundPos + new Vector3(0, 7f, 0.01f), moundPos, 35);
                var plate = new Vector3(-0.95f, 0, 0);
                Shot(cam, Path.Combine(dir, $"face_batter_{name}.png"), plate + new Vector3(0.6f, 7f, 0.01f), plate + new Vector3(0.6f, 0, 0), 35);
            }
            foreach (var ms in new double[] { 0, release - 1800, release - 900, release, contact - 300, contact })
            {
                demo.Evaluate(ms);
                Debug.Log($"FACE ms={ms:0} chest(pitcher)={demo.Facing(true):0.00} chest(batter)={demo.Facing(false):0.00} toes(pitcher)={demo.ToeForward(true):0.00} toes(batter)={demo.ToeForward(false):0.00}");
            }
            // Which clip time puts the throwing hand closest to the server's release point?
            var target = Diamond.Sim.Field.ToUnity(Diamond.Sim.BallFlight.Pitched(demo.Pitch, demo.Pitch.releaseAt));
            var bestT = 0.0; var bestD = float.MaxValue; var bestPos = Vector3.zero;
            for (var t = 1.0; t <= 2.2; t += 0.01)
            {
                var p = demo.PitcherHandAt(t);
                var d = Vector3.Distance(p, target);
                if (d < bestD) { bestD = d; bestT = t; bestPos = p; }
            }
            Debug.Log($"RELEASE target={target:0.00} bestClipTime={bestT:0.00}s dist={bestD:0.00}m handAt={bestPos:0.00}");
            foreach (var t in new[] { 1.3, 1.4, 1.5, 1.6, 1.7 })
                Debug.Log($"RELEASE hand t={t:0.0}s pos={demo.PitcherHandAt(t):0.00} dist={Vector3.Distance(demo.PitcherHandAt(t), target):0.00}");
            Debug.Log($"DEMO ok ball@release={demo.Ball.position}");
        }
    }
}
