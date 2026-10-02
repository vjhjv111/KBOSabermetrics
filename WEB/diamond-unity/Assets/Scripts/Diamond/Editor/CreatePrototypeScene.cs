using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Diamond.Stadium;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Creates Assets/Scenes/BattingPrototype.unity: field + light + a camera behind home plate looking at the mound.
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Diamond.EditorTools.CreatePrototypeScene.Run
    /// </summary>
    public static class CreatePrototypeScene
    {
        static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

        [MenuItem("Diamond/Create prototype scene")]
        public static void Run()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cam.AddComponent<Camera>();
            camera.fieldOfView = 50;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 600;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.72f, 0.88f);
            cam.AddComponent<AudioListener>();
            // Behind home plate (web z>0 is behind home; Unity z is negated) at eye height, looking at the mound.
            cam.transform.position = new Vector3(0, 1.8f, -4.5f);
            cam.transform.LookAt(new Vector3(0, 1.3f, 18.44f));
            var gameCamera = cam.AddComponent<GameCamera>();

            var light = new GameObject("Directional Light");
            var l = light.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.2f;
            l.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(52, -35, 0);

            var field = new GameObject("Field");
            field.AddComponent<StadiumBuilder>();
            var demo = field.AddComponent<PitchReplayDemo>();
            field.AddComponent<Diamond.Net.ServerPlay>();
            var so = new SerializedObject(demo);
            so.FindProperty("pitcherPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Motions/Baseball Pitching_1.fbx");
            so.FindProperty("pitchClip").objectReferenceValue = Clip("Assets/Motions/Baseball Pitching_1.fbx");
            so.FindProperty("batterPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Motions/Baseball Hit.fbx");
            so.FindProperty("hitClip").objectReferenceValue = Clip("Assets/Motions/Baseball Hit.fbx");
            so.FindProperty("missClip").objectReferenceValue = Clip("Assets/Motions/Baseball Hit_almostmiss.fbx");
            so.FindProperty("catcherPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Motions/Baseball Catcher.fbx");
            so.FindProperty("catcherClip").objectReferenceValue = Clip("Assets/Motions/Baseball Catcher.fbx");
            so.ApplyModifiedPropertiesWithoutUndo();
            var cameraSo = new SerializedObject(gameCamera);
            cameraSo.FindProperty("demo").objectReferenceValue = demo;
            cameraSo.ApplyModifiedPropertiesWithoutUndo();

            const string path = "Assets/Scenes/BattingPrototype.unity";
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log("SCENE ok " + path);
        }
    }
}
