using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Creates URP assets (renderer + pipeline) and makes them the default for every quality level.
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.SetupUrp.Run
    /// </summary>
    public static class SetupUrp
    {
        const string Folder = "Assets/Settings";

        [MenuItem("Diamond/Setup URP")]
        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Settings");

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Folder + "/Diamond_Renderer.asset");
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, Folder + "/Diamond_Renderer.asset");
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Folder + "/Diamond_URP.asset");
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, Folder + "/Diamond_URP.asset");
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            var levels = QualitySettings.names.Length;
            var current = QualitySettings.GetQualityLevel();
            for (var i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            AssetDatabase.SaveAssets();
            Debug.Log($"URPSETUP ok pipeline={AssetDatabase.GetAssetPath(pipeline)} levels={levels} default={(GraphicsSettings.defaultRenderPipeline != null)}");
        }
    }
}
