using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Diamond.Sim;
using Diamond.Stadium;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Places a pitcher on the mound and a right-handed batter at the plate, samples chosen frames of the Mixamo
    /// clips and renders them to Captures/ (needs a GPU: no -nographics).
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod Diamond.EditorTools.CapturePoses.Run
    /// </summary>
    public static class CapturePoses
    {
        static GameObject Spawn(string fbx, Vector3 position, float yaw, out AnimationClip clip)
        {
            var path = $"Assets/Motions/{fbx}.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var go = Object.Instantiate(prefab);
            go.name = fbx;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            var surface = Resources.Load<Material>("Diamond/Player");
            var joints = Resources.Load<Material>("Diamond/PlayerJoints");
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                r.sharedMaterial = r.name.Contains("Joints") ? joints : surface;
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.applyRootMotion = false;
            return go;
        }

        static void Shot(Camera cam, string file, Vector3 position, Vector3 lookAt, float fov)
        {
            cam.transform.position = position; cam.transform.LookAt(lookAt); cam.fieldOfView = fov;
            var rt = new RenderTexture(960, 600, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 600, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BattingPrototype.unity");
            Object.FindFirstObjectByType<StadiumBuilder>().Build();
            var cam = Camera.main;
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
            Directory.CreateDirectory(dir);

            // Pitcher on the mound facing home (Unity +z is towards the outfield, so facing home means yaw 180).
            var mound = Field.ToUnity(0, Field.MoundHeight, -Field.MoundDistance);
            var pitcher = Spawn("Baseball Pitching_1", mound, 180, out var pitchClip);
            foreach (var t in new[] { 0.0f, 1.2f, 1.4f, 1.5f, 1.63f })
            {
                pitchClip.SampleAnimation(pitcher, t);
                // Side view from the first-base side, and the catcher's view.
                Shot(cam, Path.Combine(dir, $"pitch_side_{t:0.00}.png"), mound + new Vector3(7, 1.5f, 0), mound + new Vector3(0, 1.1f, 0), 40);
                if (t == 1.5f) Shot(cam, Path.Combine(dir, "pitch_catcher.png"), new Vector3(0, 1.5f, -3), mound + new Vector3(0, 1.4f, 0), 28);
            }

            // Right-handed batter on the left of the plate from the catcher's view (web x<0 -> Unity x<0), chest towards the plate (yaw 90).
            Object.DestroyImmediate(pitcher);
            var batterPos = Field.ToUnity(-0.95, 0, 0);
            var batter = Spawn("Baseball Hit", batterPos, 0, out var hitClip);
            foreach (var t in new[] { 0.0f, 1.2f, 1.35f, 1.47f, 1.6f })
            {
                hitClip.SampleAnimation(batter, t);
                Shot(cam, Path.Combine(dir, $"hit_catcher_{t:0.00}.png"), new Vector3(0, 1.6f, -4f), batterPos + new Vector3(0.4f, 1.1f, 0), 30);
                Shot(cam, Path.Combine(dir, $"hit_top_{t:0.00}.png"), batterPos + new Vector3(0.9f, 5.5f, 0.3f), batterPos + new Vector3(0.5f, 0.8f, 0.3f), 40);
            }
            Debug.Log("POSES ok " + dir);
        }
    }
}
