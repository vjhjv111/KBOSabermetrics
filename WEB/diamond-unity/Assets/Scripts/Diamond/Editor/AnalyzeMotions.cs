using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Samples pitching/batting clips and reports when each hand moves fastest, which brackets the
    /// ball-release and bat-contact instants used to time-warp clips to the game's rules.
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.AnalyzeMotions.Run
    /// </summary>
    public static class AnalyzeMotions
    {
        static readonly string[] Targets =
        {
            "Baseball Pitching_1", "Baseball Pitching_2", "Baseball Hit", "Baseball Hit_almostmiss", "Baseball Hit_homerun", "Baseball Bunt", "Baseball Strike",
        };

        [MenuItem("Diamond/Analyze motions")]
        public static void Run()
        {
            var report = new StringBuilder();
            foreach (var name in Targets)
            {
                var path = $"Assets/Motions/{name}.fbx";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                var go = Object.Instantiate(prefab);
                var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
                var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (rightHand == null || leftHand == null) { report.AppendLine($"ANALYZE {name}: no humanoid hands"); Object.DestroyImmediate(go); continue; }

                const float dt = 1f / 30f;
                var n = Mathf.CeilToInt(clip.length / dt);
                var rv = new float[n]; var lv = new float[n];
                Vector3 pr = default, pl = default;
                for (var i = 0; i < n; i++)
                {
                    clip.SampleAnimation(go, i * dt);
                    var r = rightHand.position - hips.position * 0f; var l = leftHand.position;
                    if (i > 0) { rv[i] = (r - pr).magnitude / dt; lv[i] = (l - pl).magnitude / dt; }
                    pr = r; pl = l;
                }
                int Peak(float[] v) => System.Array.IndexOf(v, v.Max());
                var rp = Peak(rv); var lp = Peak(lv);
                report.AppendLine($"ANALYZE {name,-26} len={clip.length:0.00}s  rightHand peak {rv[rp]:0.0} m/s at {rp * dt:0.00}s | leftHand peak {lv[lp]:0.0} m/s at {lp * dt:0.00}s");
                // Coarse speed profile of the right hand (every 0.2 s) to see the shape of the motion.
                var profile = string.Join(" ", Enumerable.Range(0, n / 6).Select(k => $"{k * 6 * dt:0.0}:{rv[k * 6]:0}"));
                report.AppendLine($"        right-hand speed {profile}");
                Object.DestroyImmediate(go);
            }
            Debug.Log(report.ToString());
        }
    }
}
