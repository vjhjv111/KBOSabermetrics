using System.IO;
using UnityEditor;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Creates URP Lit materials under Resources/Diamond for StadiumBuilder, plus a generated mown-turf texture.
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.CreateFieldMaterials.Run
    /// </summary>
    public static class CreateFieldMaterials
    {
        const string Folder = "Assets/Resources/Diamond";

        static Material Lit(string name, Color color, float smoothness, Texture2D map = null, Vector2? tiling = null)
        {
            var path = $"{Folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            if (map != null) { material.SetTexture("_BaseMap", map); material.SetTextureScale("_BaseMap", tiling ?? Vector2.one); }
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Mown-turf stripes: alternating light and dark bands (12 m period in world metres) with fine blade noise.</summary>
        static Texture2D TurfTexture()
        {
            const string path = "Assets/Textures/Stadium/turf.png";
            Directory.CreateDirectory("Assets/Textures/Stadium");
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(9031);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var stripe = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Sin(y / (float)size * Mathf.PI * 2f) * 1.8f * 0.5f + 0.5f));
                var noise = (float)rng.NextDouble() - 0.5f;
                var light = Mathf.Lerp(0.88f, 1.06f, stripe) + noise * 0.16f;
                tex.SetPixel(x, y, new Color(0.25f * light, 0.40f * light, 0.125f * light, 1f));
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [MenuItem("Diamond/Create field materials")]
        public static void Run()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var turf = TurfTexture();
            // UVs are world metres / 6, so a 12 m stripe period maps to a tiling of 1/2 across a single texture repeat.
            Lit("Turf", Color.white, 0.12f, turf, new Vector2(0.5f, 0.5f));
            Lit("Clay", new Color(0.62f, 0.40f, 0.26f), 0.08f);
            Lit("Chalk", new Color(0.93f, 0.91f, 0.86f), 0.25f);
            Lit("WarningTrack", new Color(0.60f, 0.40f, 0.32f), 0.05f);
            Lit("WallPadding", new Color(0.086f, 0.263f, 0.20f), 0.35f);
            Lit("Gold", new Color(0.91f, 0.76f, 0.32f), 0.45f);
            Lit("Ball", new Color(0.98f, 0.98f, 0.96f), 0.5f);
            Lit("Player", new Color(0.92f, 0.93f, 0.95f), 0.35f);
            Lit("PlayerJoints", new Color(0.12f, 0.16f, 0.22f), 0.3f);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("FIELDMAT ok");
        }
    }
}
