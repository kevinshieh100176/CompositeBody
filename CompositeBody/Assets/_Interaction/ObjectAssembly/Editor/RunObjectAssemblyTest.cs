using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompositeBody.Interaction.EditorTools;
using CompositeBody.Interaction.ObjectAssembly.Diagnostics;

namespace CompositeBody.Interaction.ObjectAssembly.EditorTools
{
    /// <summary>
    /// Opens ObjectAssembly_Demo.unity, drops an <see cref="OATestRunner"/> into it (unsaved) and
    /// enters play mode. The runner logs "[OATest] RESULT: PASS/FAIL".
    ///
    /// SAFE IN A LIVE EDITOR: the runner leaves play mode instead of exiting unless the editor is
    /// in batch mode, and when play mode ends this reopens whatever scenes were open before
    /// (the demo scene is discarded, never saved -- the runner object only lived in memory).
    /// Refuses while any loaded scene has unsaved changes.
    ///
    /// Batch (editor closed; launch WITHOUT -quit, or the editor closes before play mode runs):
    ///   Unity.exe -batchmode -projectPath &lt;path&gt;
    ///     -executeMethod CompositeBody.Interaction.ObjectAssembly.EditorTools.RunObjectAssemblyTest.Run
    /// </summary>
    [InitializeOnLoad]
    public static class RunObjectAssemblyTest
    {
        const string Tag = "OATest";
        const string RestoreKey = "OATest.RestoreScenes";

        static RunObjectAssemblyTest()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;

            // Leaving play mode reloads the domain, and EnteredEditMode can fire before this
            // constructor has re-subscribed -- one run in three came back to the demo scene. So a
            // restore still pending once we are plainly back in edit mode is done from here too.
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !string.IsNullOrEmpty(SessionState.GetString(RestoreKey, "")))
                EditorApplication.delayCall += () =>
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) Restore();
                };
        }

        [MenuItem("Tools/ObjectAssembly/Run Test")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - already in play mode.");
                return;
            }
            if (InteractionBuildUtil.AnySceneDirty(Tag))
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - a loaded scene has unsaved changes; save it first.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var restore = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && !string.IsNullOrEmpty(s.path)) restore.Add(s.path);
            }
            if (!Application.isBatchMode) SessionState.SetString(RestoreKey, string.Join("|", restore));

            Scene scene = EditorSceneManager.OpenScene(BuildObjectAssembly.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - could not open {BuildObjectAssembly.ScenePath}; run Tools/ObjectAssembly/Build Demo.");
                SessionState.EraseString(RestoreKey);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            new GameObject("OATestRunner").AddComponent<OATestRunner>();
            Debug.Log($"[{Tag}] entering play mode...");
            EditorApplication.EnterPlaymode();
        }

        static void HandlePlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.delayCall += Restore;
        }

        /// <summary>Reopen what was open before the run. Idempotent: the key is taken before anything opens.</summary>
        static void Restore()
        {
            string saved = SessionState.GetString(RestoreKey, "");
            if (string.IsNullOrEmpty(saved)) return;
            SessionState.EraseString(RestoreKey);

            // The demo scene now holds the unsaved runner object; it is discarded, not saved.
            string[] paths = saved.Split('|');
            EditorSceneManager.OpenScene(paths[0], OpenSceneMode.Single);
            for (int i = 1; i < paths.Length; i++) EditorSceneManager.OpenScene(paths[i], OpenSceneMode.Additive);
            Debug.Log($"[{Tag}] reopened {saved.Replace("|", ", ")}.");
        }
    }
}
