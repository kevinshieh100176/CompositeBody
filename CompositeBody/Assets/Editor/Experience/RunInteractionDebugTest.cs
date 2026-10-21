using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Diagnostics;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Opens the main scene, drops an <see cref="InteractionDebugTestRunner"/> into it and enters
    /// play mode. The runner asserts and exits the editor with its own status code.
    ///
    /// Launch WITHOUT -quit: that flag closes the editor before play mode has run a frame.
    ///
    /// Unity.exe -batchmode -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RunInteractionDebugTest.Run
    /// </summary>
    public static class RunInteractionDebugTest
    {
        const string k_ScenePath = "Assets/_Scenes/MainScene.unity";

        public static void Run()
        {
            Debug.Log("[DebugPanelTest] Opening main scene...");

            var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[DebugPanelTest] RESULT: FAIL - could not open {k_ScenePath}");
                EditorApplication.Exit(1);
                return;
            }

            var go = new GameObject("InteractionDebugTestRunner");
            go.AddComponent<InteractionDebugTestRunner>();

            Debug.Log("[DebugPanelTest] Entering play mode...");
            EditorApplication.EnterPlaymode();
        }
    }
}
