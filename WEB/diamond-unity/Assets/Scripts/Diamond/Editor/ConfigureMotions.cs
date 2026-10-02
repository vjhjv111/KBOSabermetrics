using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Sets every FBX in Assets/Motions to a Humanoid rig (avatar created from the file itself) and prints
    /// each clip's length so release/contact timing can be checked.
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.ConfigureMotions.Run
    /// </summary>
    public static class ConfigureMotions
    {
        static readonly string[] Loops = { "Idle", "Milling", "Walk", "On Deck" };

        [MenuItem("Diamond/Configure motions (Humanoid)")]
        public static void Run()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Motions" });
            var report = new StringBuilder();
            foreach (var path in guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                var loop = Loops.Any(path.Contains);
                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips) { clip.loopTime = loop; clip.loopPose = loop; }
                importer.clipAnimations = clips;
                importer.SaveAndReimport();

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
                    report.AppendLine($"MOTION {System.IO.Path.GetFileNameWithoutExtension(path),-28} clip='{asset.name}' len={asset.length:0.000}s fps={asset.frameRate} humanoid={asset.isHumanMotion} loop={asset.isLooping}");
            }
            Debug.Log(report.ToString());
            AssetDatabase.SaveAssets();
        }
    }
}
