using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Diagnostics;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Opens the main scene, drops a <see cref="BeatSpineTestRunner"/> into it and enters play
    /// mode. The runner does the asserting and exits the editor with its own status code.
    ///
    /// Launch WITHOUT -quit: that flag closes the editor as soon as this method returns, which
    /// is before play mode has run a single frame, and the run would look like a clean pass
    /// having tested nothing.
    ///
    /// Unity.exe -batchmode -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RunBeatSpineTest.Run
    /// </summary>
    public static class RunBeatSpineTest
    {
        public static void Run()
        {
            Debug.Log("[SpineTest] Opening main scene...");

            var scene = EditorSceneManager.OpenScene(BuildBaseScene.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[SpineTest] RESULT: FAIL - could not open {BuildBaseScene.ScenePath}");
                EditorApplication.Exit(1);
                return;
            }

            var go = new GameObject("BeatSpineTestRunner");
            go.AddComponent<BeatSpineTestRunner>();

            Debug.Log("[SpineTest] Entering play mode...");
            EditorApplication.EnterPlaymode();
        }
    }
}
