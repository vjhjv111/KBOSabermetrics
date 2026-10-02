using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Diamond.Net;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Enters play mode in batch mode so ServerPlay talks to the local server, then exits after N pitches.
    /// Unity.exe -batchmode -projectPath . -executeMethod Diamond.EditorTools.RunPlaySmoke.Run (practice game)
    ///                                        -executeMethod Diamond.EditorTools.RunPlaySmoke.RunMatch (full match)
    /// (needs a GPU and the site running at http://127.0.0.1:5080; no -quit and no -nographics.)
    /// </summary>
    public static class RunPlaySmoke
    {
        static void Start(bool fullMatch, int pitches)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BattingPrototype.unity");
            var play = Object.FindFirstObjectByType<ServerPlay>();
            var so = new SerializedObject(play);
            so.FindProperty("quitAfterPitches").intValue = pitches;
            so.FindProperty("humanBatter").boolValue = true;
            so.FindProperty("simulateHumanClicks").boolValue = true;
            so.FindProperty("fullMatch").boolValue = fullMatch;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorApplication.EnterPlaymode();
        }

        public static void Run() => Start(false, 10);
        public static void RunMatch() => Start(true, 30);
    }
}
