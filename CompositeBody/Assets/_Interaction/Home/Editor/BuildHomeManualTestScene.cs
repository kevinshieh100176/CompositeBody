using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompositeBody.Interaction.EditorTools;

namespace CompositeBody.Interaction.Home.EditorTools
{
    /// <summary>
    /// Tools > Home > Build Manual Test Scene.
    ///
    /// Makes Assets/_Interaction/Home/Home_ManualTest.unity: a COPY of the Home demo scene
    /// (S2_v1.unity -- art, the built HomeInteraction, the two-player bootstrap) plus the
    /// manual-test kit: XRI's XR Interaction Simulator for mouse-and-keyboard XR, the
    /// DesktopTestHud for host / join / start and the interaction buttons, and Home's own HUD
    /// section (<see cref="HomeManualTestActions"/>).
    ///
    /// A COPY, regenerated on every run, so the art scene stays exactly as the artists and the Home
    /// builder leave it, and the test scene never drifts from it: re-run this after S2_v1 changes.
    /// Do not edit Home_ManualTest.unity by hand -- the next run overwrites it. The copy keeps its
    /// own GUID across runs (the file is overwritten, its .meta is not), and gets its own
    /// NetworkObject hashes, so it can sit in Build Settings next to S2_v1.
    /// </summary>
    public static class BuildHomeManualTestScene
    {
        const string Tag = "HomeManualTest";
        public const string SourcePath = "Assets/_Interaction/Home/S2_v1.unity";
        public const string TestPath = "Assets/_Interaction/Home/Home_ManualTest.unity";
        public const float ManualTestTimeout = 1800f;

        [MenuItem("Tools/Home/Build Manual Test Scene")]
        public static void Run()
        {
            if (InteractionBuildUtil.AnySceneDirty(Tag))
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - save or discard the open scene's changes first; this tool never saves somebody else's work.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourcePath) == null)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - {SourcePath} not found.");
                return;
            }

            string previous = SceneManager.GetActiveScene().path;

            // Overwrite the file, keep the .meta (and so the GUID Build Settings point at).
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            File.Copy(Path.Combine(projectRoot, SourcePath), Path.Combine(projectRoot, TestPath), true);
            AssetDatabase.ImportAsset(TestPath, ImportAssetOptions.ForceUpdate);

            Scene scene = EditorSceneManager.OpenScene(TestPath, OpenSceneMode.Single);

            var failures = new List<string>();
            if (Object.FindAnyObjectByType<HomeAssembly>() == null)
                failures.Add("the copy has no HomeAssembly -- run Tools/Home/Build Home Interaction on S2_v1 first");

            GameObject kit = InteractionBuildUtil.AddManualTestKit(scene, Tag);
            if (kit == null) failures.Add("could not add the manual-test kit");
            else kit.AddComponent<HomeManualTestActions>();

            // The show's 180 s timeout finishes the beat while a person is still finding their
            // way around the simulator. In this copy only, give them half an hour; the HUD's
            // "Expire timeout" button tests the timeout path on demand.
            HomeAssembly asmForTimeout = Object.FindAnyObjectByType<HomeAssembly>();
            if (asmForTimeout != null)
            {
                var so = new SerializedObject(asmForTimeout);
                SerializedProperty timeout = so.FindProperty("m_Timings.timeout");
                if (timeout != null) timeout.floatValue = ManualTestTimeout;
                else failures.Add("HomeAssembly has no m_Timings.timeout to lengthen");
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // A copied scene carries the source's hashes; recompute them from this scene's ids.
            InteractionBuildUtil.RegenerateNetworkObjectHashes(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (InteractionBuildUtil.EnsureInBuildSettings(TestPath))
                Debug.Log($"[{Tag}] added {TestPath} to Build Settings (Netcode needs it for a client to sync).");

            Verify(scene, failures);

            if (!string.IsNullOrEmpty(previous) && previous != TestPath)
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        }

        static void Verify(Scene scene, List<string> failures)
        {
            InteractionBuildUtil.VerifyDemoBootstrap(Tag, scene, failures);
            InteractionBuildUtil.VerifyManualTestKit(Tag, scene, failures);
            InteractionBuildUtil.VerifyHashes(Tag, scene, failures);
            InteractionBuildUtil.Check(Tag, Object.FindAnyObjectByType<HomeManualTestActions>() != null,
                                       "Home's HUD section is present", failures);
            InteractionBuildUtil.Check(Tag, System.Array.Exists(EditorBuildSettings.scenes, s => s.path == TestPath && s.enabled),
                                       "test scene is in Build Settings", failures);

            HomeAssembly asm = Object.FindAnyObjectByType<HomeAssembly>();
            InteractionBuildUtil.Check(Tag, asm != null && asm.built, "HomeAssembly is built (pairs and zones)", failures);
            InteractionBuildUtil.Check(Tag, asm != null && asm.beatRoot == null,
                                       "HomeAssembly arms on session Start (no beat root)", failures);
            InteractionBuildUtil.Check(Tag, asm != null && Mathf.Approximately(asm.timings.timeout, ManualTestTimeout),
                                       $"test copy's timeout is {ManualTestTimeout:0}s (S2_v1 keeps its own)", failures);

            InteractionBuildUtil.Verdict(Tag, failures);
        }
    }
}
