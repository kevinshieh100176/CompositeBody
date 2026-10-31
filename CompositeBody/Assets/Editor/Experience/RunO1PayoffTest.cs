using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Diagnostics;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Opens the main scene, drops an <see cref="O1PayoffTestRunner"/> into it and enters play
    /// mode. The runner asserts and exits the editor with its own status code.
    ///
    /// Launch WITHOUT -quit: that flag closes the editor before play mode has run a frame.
    ///
    /// Unity.exe -batchmode -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RunO1PayoffTest.Run
    /// </summary>
    public static class RunO1PayoffTest
    {
        const string k_ScenePath = "Assets/_Scenes/MainScene.unity";

        public static void Run()
        {
            Debug.Log("[PayoffTest] Opening main scene...");

            var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[PayoffTest] RESULT: FAIL - could not open {k_ScenePath}");
                // Only the batch run may exit; a live editor keeps the error in the console.
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var go = new GameObject("O1PayoffTestRunner");
            go.AddComponent<O1PayoffTestRunner>();

            Debug.Log("[PayoffTest] Entering play mode...");
            EditorApplication.EnterPlaymode();
        }
    }
}
