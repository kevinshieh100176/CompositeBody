using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Interaction.Home.Diagnostics;

namespace CompositeBody.Interaction.Home.EditorTools
{
    /// <summary>
    /// Tools > Home > Run Home Interaction Test: enters play mode in the open S2_v1 and adds a
    /// <see cref="HomeInteractionTestRunner"/> once play mode has started.
    ///
    /// SAFE IN A LIVE EDITOR. The runner is created AFTER entering play mode, so the scene is never
    /// dirtied by it, and the runner leaves play mode when it is done -- it exits the editor only
    /// in batch mode (<see cref="RunBatch"/>). The active scene must be S2_v1 (unsaved changes are fine:
    /// play mode never writes it); nothing is opened over it.
    ///
    /// Stills go to <see cref="CaptureDirKey"/> (SessionState) if set, else %TEMP%/HomeInteractionTest.
    /// </summary>
    [InitializeOnLoad]
    public static class RunHomeInteractionTest
    {
        public const string PendingKey = "CompositeBody.Home.RunHomeInteractionTest.Pending";
        public const string CaptureDirKey = "CompositeBody.Home.RunHomeInteractionTest.CaptureDir";

        static RunHomeInteractionTest()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tools/Home/Run Home Interaction Test")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError($"[{HomeInteractionTestRunner.Tag}] RESULT: FAIL - already in play mode.");
                return;
            }
            // Active scene only, not clean: play mode never writes the scene, and the template rig's
            // UI marks itself dirty on every domain reload, which would otherwise block every run.
            UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.path != BuildHomeInteraction.ScenePath)
            {
                Debug.LogError($"[{HomeInteractionTestRunner.Tag}] RESULT: FAIL - open '{BuildHomeInteraction.ScenePath}' " +
                               $"first (active scene is '{active.path}').");
                return;
            }

            SessionState.SetBool(PendingKey, true);
            Debug.Log($"[{HomeInteractionTestRunner.Tag}] entering play mode...");
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Headless: -batchmode (no -quit) -executeMethod ...RunHomeInteractionTest.RunBatch</summary>
        public static void RunBatch()
        {
            var scene = EditorSceneManager.OpenScene(BuildHomeInteraction.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[{HomeInteractionTestRunner.Tag}] RESULT: FAIL - could not open {BuildHomeInteraction.ScenePath}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);

            string dir = SessionState.GetString(CaptureDirKey, "");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Path.GetTempPath(), "HomeInteractionTest");

            var go = new GameObject("HomeInteractionTestRunner");
            var runner = go.AddComponent<HomeInteractionTestRunner>();
            runner.captureDirectory = dir;
        }
    }
}
