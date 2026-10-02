using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Reports whether each Mixamo FBX carries a skinned mesh, its size and height, so one can serve as the shared character.
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.InspectMotionModels.Run
    /// </summary>
    public static class InspectMotionModels
    {
        [MenuItem("Diamond/Inspect motion models")]
        public static void Run()
        {
            var report = new StringBuilder();
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Motions" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var verts = skins.Sum(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0);
                var bounds = skins.Length > 0 ? skins[0].bounds : new Bounds();
                foreach (var s in skins.Skip(1)) bounds.Encapsulate(s.bounds);
                var bones = skins.Length > 0 ? skins[0].bones.Length : 0;
                report.AppendLine($"MODEL {System.IO.Path.GetFileNameWithoutExtension(path),-28} skins={skins.Length} verts={verts} bones={bones} height={bounds.size.y:0.00} meshNames={string.Join(",", skins.Select(s => s.name))}");
            }
            Debug.Log(report.ToString());
        }
    }
}
