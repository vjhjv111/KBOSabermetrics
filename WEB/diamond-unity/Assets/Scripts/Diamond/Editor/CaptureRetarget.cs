using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Diamond.EditorTools
{
    /// <summary>Compares the textured body posed by its own clip (SampleAnimation), the same clip through a playable, and the plain Hit clip through a playable.</summary>
    public static class CaptureRetarget
    {
        static AnimationClip ClipOf(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

        public static void Run()
        {
            var cam = new GameObject("cam", typeof(Camera)).GetComponent<Camera>();
            var light = new GameObject("light", typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures")); Directory.CreateDirectory(dir);
            var bodyPath = "Assets/Motions/Baseball Hit with model.fbx";
            var own = ClipOf(bodyPath); var plain = ClipOf("Assets/Motions/Baseball Hit.fbx");
            for (var variant = 0; variant < 5; variant++)
            {
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath));
                var animator = go.GetComponent<Animator>(); animator.applyRootMotion = false;
                if (variant >= 3) Diamond.Stadium.PlayerKit.Dress(go, Diamond.Stadium.PlayerKit.Role.Batter, Resources.Load<Material>("Diamond/Player"));
                if (variant == 4) animator.Rebind();
                if (variant == 0) own.SampleAnimation(go, 0.3f);
                else
                {
                    var graph = PlayableGraph.Create("t"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(graph, "a", animator);
                    var p = AnimationClipPlayable.Create(graph, variant == 1 ? own : plain);
                    output.SetSourcePlayable(p); graph.Play(); p.SetTime(0.3); graph.Evaluate();
                }
                cam.transform.position = new Vector3(0, 1.3f, -3.2f); cam.transform.LookAt(new Vector3(0, 0.95f, 0)); cam.fieldOfView = 35;
                var rt = new RenderTexture(500, 600, 24); cam.targetTexture = rt; cam.Render(); cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(500, 600, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 500, 600), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"retarget_{variant}.png"), tex.EncodeToPNG());
                cam.targetTexture = null; RenderTexture.active = null;
                var rh = animator.GetBoneTransform(HumanBodyBones.RightHand); var lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Debug.Log($"RETARGET variant {variant}: rightHand {rh.position} leftHand {lh.position} head {animator.GetBoneTransform(HumanBodyBones.Head).position}");
                Object.DestroyImmediate(go);
            }
        }
    }
}
