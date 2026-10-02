using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Generic clip report for locomotion/fielding clips: length, hips path and speed, chest facing and hand speeds every 0.25 s.
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.AnalyzeClips.Run
    /// </summary>
    public static class AnalyzeClips
    {
        static readonly string[] Targets =
        {
            "Running", "Jog Backward Diagonal", "Running Slide", "Picking Up",
            "Goalkeeper Catch", "Goalkeeper Diving Save", "Goalkeeper Overhand Throw", "Cheering", "Fist Pump",
        };

        static string P(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";

        [MenuItem("Diamond/Analyze clips")]
        public static void Run()
        {
            var sb = new StringBuilder();
            foreach (var name in Targets)
            {
                var path = $"Assets/Motions/{name}.fbx";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"CLIP {name}: missing"); continue; }
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                var go = Object.Instantiate(prefab);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var a = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
                Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
                sb.AppendLine($"CLIP {name}: len={clip.length:0.00}s humanoid={clip.isHumanMotion} loop={clip.isLooping}");
                const float dt = 0.05f;
                Vector3 prevHips = default, prevR = default, prevL = default;
                var step = 0;
                for (var t = 0f; t < clip.length; t += dt, step++)
                {
                    clip.SampleAnimation(go, t);
                    var hips = B(HumanBodyBones.Hips).position;
                    var r = B(HumanBodyBones.RightHand).position; var l = B(HumanBodyBones.LeftHand).position;
                    if (step % 5 == 0)
                    {
                        var sh = B(HumanBodyBones.RightUpperArm).position - B(HumanBodyBones.LeftUpperArm).position; sh.y = 0;
                        var facing = Vector3.Cross(sh.normalized, Vector3.up);
                        var hv = step > 0 ? new Vector2(hips.x - prevHips.x, hips.z - prevHips.z).magnitude / dt : 0;
                        var rv = step > 0 ? (r - prevR).magnitude / dt : 0; var lv = step > 0 ? (l - prevL).magnitude / dt : 0;
                        sb.AppendLine($"  t={t:0.00} hips={P(hips)} v{hv:0.0} face={P(facing)} rH y{r.y:0.00} v{rv:0.0} lH y{l.y:0.00} v{lv:0.0}");
                    }
                    prevHips = hips; prevR = r; prevL = l;
                }
                Object.DestroyImmediate(go);
            }
            Debug.Log(sb.ToString());
        }
    }
}
