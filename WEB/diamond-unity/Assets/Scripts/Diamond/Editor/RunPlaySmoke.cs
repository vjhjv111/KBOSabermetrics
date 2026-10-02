using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Diamond.Net;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Enters play mode in batch mode so ServerPlay talks to the local server for a few pitches, then exits.
    /// Unity.exe -batchmode -projectPath . -executeMethod Diamond.EditorTools.RunPlaySmoke.Run -logFile smoke.log
    /// (needs a GPU and the site running at http://127.0.0.1:5080; no -quit and no -nographics.)
    /// </summary>
    public static class RunPlaySmoke
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BattingPrototype.unity");
            var play = Object.FindFirstObjectByType<ServerPlay>();
            var so = new SerializedObject(play);
            so.FindProperty("quitAfterPitches").intValue = 10;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorApplication.EnterPlaymode();
        }
    }
}
