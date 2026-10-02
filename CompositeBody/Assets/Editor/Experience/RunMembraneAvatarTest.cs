using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Diagnostics;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Opens the membrane/combine sample scene, drops a <see cref="MembraneAvatarTestRunner"/>
    /// into it and enters play mode. The runner asserts and exits with its own status code.
    ///
    /// Launch WITHOUT -quit: that closes the editor as soon as this returns, which is before
    /// play mode has run a frame, and the run would look like a clean pass having tested nothing.
    ///
    /// Unity.exe -batchmode -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RunMembraneAvatarTest.Run
    /// </summary>
    public static class RunMembraneAvatarTest
    {
        public static void Run()
        {
            Debug.Log("[MembraneTest] Opening the sample scene...");

            var scene = EditorSceneManager.OpenScene(BuildMembraneCombineScene.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[MembraneTest] RESULT: FAIL - could not open {BuildMembraneCombineScene.ScenePath}");
                EditorApplication.Exit(1);
                return;
            }

            var go = new GameObject("MembraneAvatarTestRunner");
            go.AddComponent<MembraneAvatarTestRunner>();

            Debug.Log("[MembraneTest] Entering play mode...");
            EditorApplication.EnterPlaymode();
        }
    }
}
