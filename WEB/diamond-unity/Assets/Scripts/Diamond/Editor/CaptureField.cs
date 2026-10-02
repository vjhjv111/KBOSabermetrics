using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Diamond.Stadium;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Renders the prototype scene from a few viewpoints to PNG files (needs a GPU: do not pass -nographics).
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod Diamond.EditorTools.CaptureField.Run
    /// </summary>
    public static class CaptureField
    {
        static void Shot(Camera cam, string file, Vector3 position, Vector3 lookAt, float fov = 50)
        {
            cam.transform.position = position;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BattingPrototype.unity");
            var field = Object.FindFirstObjectByType<StadiumBuilder>();
            field.Build();
            var cam = Camera.main;
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
            Directory.CreateDirectory(dir);
            Shot(cam, Path.Combine(dir, "behind-home.png"), new Vector3(0, 1.8f, -4.5f), new Vector3(0, 1.3f, 18.44f));
            Shot(cam, Path.Combine(dir, "overview.png"), new Vector3(0, 55, -45), new Vector3(0, 0, 40), 60);
            Shot(cam, Path.Combine(dir, "behind-high.png"), new Vector3(0, 4f, -8f), new Vector3(0, 0f, 14f), 55);
            Shot(cam, Path.Combine(dir, "behind-mid.png"), new Vector3(0, 2.6f, -6f), new Vector3(0, 0.5f, 16f), 55);
            Shot(cam, Path.Combine(dir, "outfield.png"), new Vector3(0, 6, 60), new Vector3(0, 2, -5), 60);
            Debug.Log("CAPTURE ok " + dir);
        }
    }
}
