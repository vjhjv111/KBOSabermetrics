using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>
    /// Adds the URP package at the version Package Manager recommends for this editor, then exits.
    /// Unity.exe -batchmode -nographics -projectPath . -executeMethod Diamond.EditorTools.InstallUrpPackage.Run
    /// </summary>
    public static class InstallUrpPackage
    {
        static AddRequest _request;

        public static void Run()
        {
            _request = Client.Add("com.unity.render-pipelines.universal");
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (!_request.IsCompleted) return;
            EditorApplication.update -= Poll;
            if (_request.Status == StatusCode.Success)
            {
                Debug.Log($"URPINSTALL ok {_request.Result.name}@{_request.Result.version}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("URPINSTALL failed " + _request.Error?.message);
                EditorApplication.Exit(1);
            }
        }
    }
}
