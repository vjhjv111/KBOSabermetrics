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
                ("d_precontact", contact - 250), ("e_contact", contact), ("f_after", contact + 600),
            };
            foreach (var (name, ms) in times)
            {
                demo.Evaluate(ms);
                // Catcher-side elevated view, and a view from the first-base side of the mound.
                Shot(cam, Path.Combine(dir, $"demo_{name}_catcher.png"), new Vector3(2.5f, 2.2f, -6f), new Vector3(0, 1.3f, 10f), 45);
                var mid = new Vector3(0, 1.4f, 9f);
                Shot(cam, Path.Combine(dir, $"demo_{name}_side.png"), mid + new Vector3(16f, 1.2f, 0), mid, 42);
            }
            Debug.Log($"DEMO ok ball@release={demo.Ball.position}");
        }
    }
}
