using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Diamond.Model;
using Diamond.Sim;
using Diamond.Stadium;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Renders the dressed players: batter stance and swing contact (right and left), pitcher, catcher and a fielder's ready stance.
    /// Needs a GPU (no -nographics). Run "Rebuild prototype" first so the scene uses the textured body.
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod Diamond.EditorTools.CaptureStance.Run
    /// </summary>
    public static class CaptureStance
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

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BattingPrototype.unity");
            Object.FindFirstObjectByType<StadiumBuilder>().Build();
            var demo = Object.FindFirstObjectByType<PitchReplayDemo>();
            demo.Setup();
            var director = Object.FindFirstObjectByType<FieldPlayDirector>();
            var cam = Camera.main;
            Object.DestroyImmediate(cam.GetComponent<GameCamera>());
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
            Directory.CreateDirectory(dir);
            Shot(cam, Path.Combine(dir, "warmup.png"), new Vector3(0f, 1.6f, -4.5f), new Vector3(0f, 0.9f, 0f), 30);

            demo.SetLooks(TeamLooks.For("LG", false), TeamLooks.For("SSG", true));
            if (director != null) director.SetLooks(TeamLooks.For("LG", false), TeamLooks.For("SSG", true));

            var sample = new Pitch
            {
                id = 10, type = "fastball", velocity = 140, releaseAt = 2200, flightMs = 850,
                releaseX = -0.33, releaseY = 1.84, releaseZ = -18.32, target = new Vec2 { x = 0, y = 0 }, breakX = 0.02, breakY = 0.03,
            };
            var contact = sample.releaseAt + sample.flightMs;
            foreach (var left in new[] { false, true })
            {
                demo.SetBatter(left);
                var tag = left ? "L" : "R";
                var bx = left ? 1.15f : -1.15f;
                demo.ShowStance(0);
                for (var k = 0; k < 3; k++) demo.Evaluate(k * 16);
                Shot(cam, Path.Combine(dir, $"batter_{tag}_stance_catcher.png"), new Vector3(0f, 1.6f, -4.5f), new Vector3(bx * 0.5f, 0.9f, 0f), 30);
                Shot(cam, Path.Combine(dir, $"batter_{tag}_stance_close.png"), new Vector3(0f, 1.3f, -2.6f), new Vector3(bx, 1.0f, 0f), 30);
                demo.Play(sample, contact);
                foreach (var ms in new[] { contact - 300, contact })
                {
                    demo.Evaluate(ms);
                    Shot(cam, Path.Combine(dir, $"batter_{tag}_{(ms == contact ? "contact" : "load")}.png"), new Vector3(0f, 1.3f, -2.6f), new Vector3(bx, 1.0f, 0f), 30);
                }
            }

            // Pitcher and catcher from the side/front.
            demo.SetBatter(false);
            demo.Play(sample, null);
            demo.Evaluate(sample.releaseAt);
            var mound = Field.ToUnity(0, Field.MoundHeight, -Field.MoundDistance);
            Shot(cam, Path.Combine(dir, "pitcher_release.png"), mound + new Vector3(5f, 1.4f, 0f), mound + new Vector3(0f, 1.1f, 0f), 35);
            demo.Evaluate(sample.releaseAt + 900);
            Shot(cam, Path.Combine(dir, "catcher.png"), new Vector3(0f, 1.4f, -3.4f), new Vector3(0f, 0.8f, 1f), 35);

            demo.Evaluate(sample.releaseAt - 500);
            Shot(cam, Path.Combine(dir, "catcher_front.png"), new Vector3(0f, 1.2f, 2.2f), new Vector3(0f, 0.85f, -1.5f), 30);
            Shot(cam, Path.Combine(dir, "pitcher_front.png"), mound + new Vector3(0f, 1.4f, -4.5f), mound + new Vector3(0f, 1.2f, 0f), 30);
            demo.SetBatter(true);
            demo.ShowStance(0); for (var k = 0; k < 3; k++) demo.Evaluate(k * 16);
            Shot(cam, Path.Combine(dir, "batter_front.png"), new Vector3(1.15f, 1.3f, 3.2f), new Vector3(1.15f, 1.15f, 0f), 30);
            if (director != null)
            {
                director.ResetField();
                var ss = director.GetComponentsInChildren<Animator>().FirstOrDefault(x => x.name.StartsWith("Fielder SS"));
                if (ss != null)
                {
                    var p = ss.transform.position;
                    var f = (Vector3.zero - p); f.y = 0; f.Normalize();
                    Shot(cam, Path.Combine(dir, "fielder_front.png"), p + f * 4.5f + Vector3.up * 1.1f, p + Vector3.up * 0.9f, 30);
                    Shot(cam, Path.Combine(dir, "fielder_side.png"), p + Vector3.Cross(Vector3.up, f) * 4.5f + Vector3.up * 1.1f, p + Vector3.up * 0.9f, 30);
                }
            }
            Debug.Log("STANCE ok");
        }
    }
}
