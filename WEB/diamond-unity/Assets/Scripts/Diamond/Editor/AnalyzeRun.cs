using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Prints the hips' ground speed and distance travelled over time for the batting clips, to find when the swing ends
    /// and the batter starts running. Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.AnalyzeRun.Run
    /// </summary>
    public static class AnalyzeRun
    {
        static readonly string[] Targets = { "Baseball Hit", "Baseball Hit_almostmiss", "Baseball Hit_homerun", "Baseball Strike", "Baseball Bunt" };

        [MenuItem("Diamond/Analyze run timing")]
        public static void Run()
        {
            var sb = new StringBuilder();
            foreach (var name in Targets)
            {
                var path = $"Assets/Motions/{name}.fbx";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                var go = Object.Instantiate(prefab);
                var a = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
                var hips = a.GetBoneTransform(HumanBodyBones.Hips);
                clip.SampleAnimation(go, 0);
                var origin = hips.position;
                var prev = origin;
                const float dt = 0.1f;
                var line = new StringBuilder();
                for (var t = dt; t < clip.length; t += dt)
                {
                    clip.SampleAnimation(go, t);
                    var p = hips.position;
                    var speed = new Vector2(p.x - prev.x, p.z - prev.z).magnitude / dt;
                    var away = new Vector2(p.x - origin.x, p.z - origin.z).magnitude;
                    line.Append($" {t:0.0}s:v{speed:0.0}/d{away:0.0}");
                    prev = p;
                }
                sb.AppendLine($"RUN {name} len={clip.length:0.00}s hipsSpeed(m/s)/distFromStart(m):{line}");
                Object.DestroyImmediate(go);
            }
            Debug.Log(sb.ToString());
        }
    }
}
