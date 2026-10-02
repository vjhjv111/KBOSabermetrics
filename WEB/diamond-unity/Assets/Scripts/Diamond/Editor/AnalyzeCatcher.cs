using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Describes the Mixamo catcher clip: hips height, hand heights/speeds and facing over time, to find the crouch, the
    /// glove-receiving movement and any throw/standing phases.
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.AnalyzeCatcher.Run
    /// </summary>
    public static class AnalyzeCatcher
    {
        static string P(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";

        [MenuItem("Diamond/Analyze catcher")]
        public static void Run()
        {
            const string path = "Assets/Motions/Baseball Catcher.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var a = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            var sb = new StringBuilder();
            const float dt = 0.1f;
            Vector3 pl = default, pr = default;
            for (var t = 0f; t < clip.length; t += dt)
            {
                clip.SampleAnimation(go, t);
                var hips = B(HumanBodyBones.Hips).position;
                var l = B(HumanBodyBones.LeftHand).position; var r = B(HumanBodyBones.RightHand).position;
                var lv = t > 0 ? (l - pl).magnitude / dt : 0; var rv = t > 0 ? (r - pr).magnitude / dt : 0;
                var shoulder = B(HumanBodyBones.RightUpperArm).position - B(HumanBodyBones.LeftUpperArm).position; shoulder.y = 0;
                var facing = Vector3.Cross(shoulder.normalized, Vector3.up);
                sb.AppendLine($"CATCH t={t:0.0} hipsY={hips.y:0.00} hips={P(hips)} lHand={P(l - hips)} v{lv:0.0} rHand={P(r - hips)} v{rv:0.0} facing={P(facing)}");
                pl = l; pr = r;
            }
            Object.DestroyImmediate(go);
            Debug.Log(sb.ToString());
        }
    }
}
