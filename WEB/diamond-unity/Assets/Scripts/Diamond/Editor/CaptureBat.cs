using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Diamond.Stadium;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Renders the dressed right-handed batter at stance / load / contact for several bat axes in the hand, to pick PlayerKit.BatEuler.
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod Diamond.EditorTools.CaptureBat.Run
    /// </summary>
    public static class CaptureBat
    {
        static AnimationClip ClipOf(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

        public static void Run()
        {
            var cam = new GameObject("cam", typeof(Camera)).GetComponent<Camera>();
            var light = new GameObject("light", typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures")); Directory.CreateDirectory(dir);
            var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Motions/Baseball Hit with model.fbx");
            var clip = ClipOf("Assets/Motions/Baseball Hit.fbx");
            var material = Resources.Load<Material>("Diamond/Player");
            var variants = new (string n, Vector3 e)[]
            {
                ("Yp", new Vector3(0, 0, 0)), ("Yn", new Vector3(180, 0, 0)), ("Zp", new Vector3(90, 0, 0)), ("Zn", new Vector3(-90, 0, 0)),
                ("Xp", new Vector3(0, 0, -90)), ("Xn", new Vector3(0, 0, 90)),
            };
            foreach (var v in variants)
            {
                PlayerKit.BatEuler = v.e;
                var go = Object.Instantiate(body);
                var animator = go.GetComponent<Animator>(); animator.applyRootMotion = false;
                PlayerKit.Dress(go, PlayerKit.Role.Batter, material).SetLook(TeamLooks.For("LG", false));
                var graph = PlayableGraph.Create("t"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "a", animator);
                var p = AnimationClipPlayable.Create(graph, clip);
                output.SetSourcePlayable(p); graph.Play();
                foreach (var (name, t) in new[] { ("stance", 0.3), ("load", 1.1), ("contact", 1.47), ("follow", 1.9) })
                {
                    p.SetTime(t); graph.Evaluate();
                    cam.transform.position = new Vector3(0, 1.3f, -3.4f); cam.transform.LookAt(new Vector3(0, 1.0f, 0)); cam.fieldOfView = 30;
                    var rt = new RenderTexture(480, 480, 24); cam.targetTexture = rt; cam.Render(); cam.Render(); RenderTexture.active = rt;
                    var tex = new Texture2D(480, 480, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 480, 480), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(dir, $"bat_{v.n}_{name}.png"), tex.EncodeToPNG());
                    cam.targetTexture = null; RenderTexture.active = null;
                }
                graph.Destroy();
                Object.DestroyImmediate(go);
            }
            Debug.Log("BAT ok");
        }
    }
}
