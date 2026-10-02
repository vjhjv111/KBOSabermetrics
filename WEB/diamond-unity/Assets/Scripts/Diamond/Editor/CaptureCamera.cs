using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Diamond.Model;
using Diamond.Stadium;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Steps the demo and GameCamera through a hit ball in 33 ms ticks and renders frames to Captures/cam_*.png
    /// (needs a GPU: no -nographics). Unity.exe -batchmode -quit -projectPath . -executeMethod Diamond.EditorTools.CaptureCamera.Run
    /// </summary>
    public static class CaptureCamera
    {
        static void Snap(Camera cam, string file)
        {
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }

        static void Sweep(PitchReplayDemo demo, GameCamera gc, Camera cam, string dir, double from, double to, (string name, double ms)[] shots)
        {
            var next = 0;
            for (var ms = from; ms <= to; ms += 33)
            {
                demo.Evaluate(ms);
                gc.Tick(0.033f);
                while (next < shots.Length && ms >= shots[next].ms)
                {
                    Snap(cam, Path.Combine(dir, $"cam_{shots[next].name}.png"));
                    Debug.Log($"CAM {shots[next].name} ms={ms:0} fov={cam.fieldOfView:0.0} pos={cam.transform.position:0.0} ball={demo.Ball.position:0.0}");
                    next++;
                }
            }
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BattingPrototype.unity");
            Object.FindFirstObjectByType<StadiumBuilder>().Build();
            var demo = Object.FindFirstObjectByType<PitchReplayDemo>();
            demo.Setup();
            var cam = Camera.main;
            var gc = cam.GetComponent<GameCamera>();
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
            Directory.CreateDirectory(dir);
            var contact = demo.Pitch.releaseAt + demo.Pitch.flightMs;

            Sweep(demo, gc, cam, dir, contact - 1200, contact + 5200, new[]
            {
                ("a_pitch", contact - 300), ("b_hit", contact + 250), ("c_follow", contact + 900),
                ("d_far", contact + 2000), ("e_land", contact + 3200), ("f_late", contact + 5000),
            });

            // The next pitch begins: the camera should return to the pitch view.
            var next = new Pitch
            {
                id = 2, type = "fastball", velocity = 145, releaseAt = contact + 6000, flightMs = 800,
                releaseX = -0.33, releaseY = 1.84, releaseZ = -18.32, target = new Vec2 { x = 0, y = 0 }, breakX = 0.02, breakY = 0.03,
            };
            demo.Play(next, null);
            Sweep(demo, gc, cam, dir, contact + 5300, contact + 7000, new[] { ("g_return", contact + 6500) });
            Debug.Log("CAMERA ok");
        }
    }
}
