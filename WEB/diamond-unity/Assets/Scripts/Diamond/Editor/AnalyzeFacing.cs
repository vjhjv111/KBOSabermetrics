using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Prints key bone positions in the model's own space (identity root, +z = model forward) at chosen times so the
    /// pitching/hitting direction and the batter's handedness can be derived.
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.AnalyzeFacing.Run
    /// </summary>
    public static class AnalyzeFacing
    {
        static string P(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";

        static void Dump(StringBuilder sb, string fbx, float[] times)
        {
            var path = $"Assets/Motions/{fbx}.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var a = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            foreach (var t in times)
            {
                clip.SampleAnimation(go, t);
                var hips = B(HumanBodyBones.Hips).position;
                sb.AppendLine($"FACING {fbx} t={t:0.00}  hips={P(hips)}  chest={P(B(HumanBodyBones.UpperChest)?.position ?? B(HumanBodyBones.Chest).position - hips)}" +
                              $"  rShoulder-lShoulder={P(B(HumanBodyBones.RightUpperArm).position - B(HumanBodyBones.LeftUpperArm).position)}" +
                              $"  rFoot={P(B(HumanBodyBones.RightFoot).position - hips)} lFoot={P(B(HumanBodyBones.LeftFoot).position - hips)}" +
                              $"  rHand={P(B(HumanBodyBones.RightHand).position - hips)} lHand={P(B(HumanBodyBones.LeftHand).position - hips)}");
            }
            // Throwing/swinging direction: velocity of the right hand around the peak time.
            const float dt = 1f / 60f;
            foreach (var t in times)
            {
                clip.SampleAnimation(go, t - dt); var p0 = B(HumanBodyBones.RightHand).position;
                clip.SampleAnimation(go, t + dt); var p1 = B(HumanBodyBones.RightHand).position;
                var v = (p1 - p0) / (2 * dt);
                sb.AppendLine($"FACING {fbx} t={t:0.00}  rightHand velocity={P(v)} |v|={v.magnitude:0.0}");
            }
            Object.DestroyImmediate(go);
        }

        [MenuItem("Diamond/Analyze facing")]
        public static void Run()
        {
            var sb = new StringBuilder();
            Dump(sb, "Baseball Pitching_1", new[] { 0.0f, 1.2f, 1.5f, 1.63f, 1.8f });
            Dump(sb, "Baseball Hit", new[] { 0.0f, 1.2f, 1.4f, 1.47f, 1.6f });
            Debug.Log(sb.ToString());
        }
    }
}
