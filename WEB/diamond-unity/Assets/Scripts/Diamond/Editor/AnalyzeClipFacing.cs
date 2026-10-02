using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>Logs the yaw (degrees, 0 = model +z) each fielder clip faces, from shoulders and toes, in the model's own frame.</summary>
    public static class AnalyzeClipFacing
    {
        [MenuItem("Diamond/Analyze clip facing")]
        public static void Run()
        {
            foreach (var n in new[] { "Baseball Idle", "Running", "Running Slide", "Picking Up", "Goalkeeper Catch", "Goalkeeper Diving Save", "Goalkeeper Overhand Throw", "Cheering" })
            {
                var path = $"Assets/Motions/{n}.fbx";
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var a = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
                a.applyRootMotion = false;
                var line = "";
                foreach (var f in new[] { 0f, 0.25f, 0.5f, 0.75f })
                {
                    clip.SampleAnimation(go, clip.length * f);
                    var r = a.GetBoneTransform(HumanBodyBones.RightUpperArm).position - a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position; r.y = 0;
                    var fwd = Vector3.Cross(r.normalized, Vector3.up);
                    var toe = (a.GetBoneTransform(HumanBodyBones.LeftToes).position - a.GetBoneTransform(HumanBodyBones.LeftFoot).position
                             + a.GetBoneTransform(HumanBodyBones.RightToes).position - a.GetBoneTransform(HumanBodyBones.RightFoot).position); toe.y = 0;
                    line += $" [{f:0.00}] chest {Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg:0} toes {Mathf.Atan2(toe.x, toe.z) * Mathf.Rad2Deg:0};";
                }
                Debug.Log($"CFACE {n} len={clip.length:0.0}:{line}");
                Object.DestroyImmediate(go);
            }
        }
    }
}
