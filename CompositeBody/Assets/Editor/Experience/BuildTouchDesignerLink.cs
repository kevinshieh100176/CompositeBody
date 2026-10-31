using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts the TouchDesigner stream on MainScene's ShowTimeline object.
    ///
    /// WHY THERE AND NOT ON ITS OWN OBJECT. The sender's whole payload is the show's clock and
    /// the players' poses, and ShowTimeline already owns the first of those -- the sender reads
    /// <see cref="ShowTimelineDriver"/> off it. Hanging it on the same object means there is one
    /// place to look when the stream and the show disagree about what time it is, rather than two
    /// objects that have to be kept in step.
    ///
    /// WHY MAINSCENE AND NOT ONBOARDING. Onboarding is one beat of a 38-minute piece, loaded as a
    /// scene prefab inside MainScene. A stream that stopped when the onboarding content was
    /// disabled would go quiet for the back half of the show, which is 56% of it.
    ///
    /// Seed-once: if a sender is already there, its settings are left exactly as they are. Host,
    /// port and rate are things that get tuned against the venue's network and against TD, and a
    /// re-run should not reset them -- this project has already lost hand-tuned values to a
    /// builder that overwrote rather than seeded.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildTouchDesignerLink.Run
    /// </summary>
    public static class BuildTouchDesignerLink
    {
        const string k_Scene = "Assets/_Scenes/MainScene.unity";
        const string k_Host = "ShowTimeline";

        [MenuItem("Tools/Onboarding/Add TouchDesigner Link")]
        public static void Run()
        {
            Debug.Log("[TDLink] Starting...");

            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.OpenScene(k_Scene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[TDLink] RESULT: FAIL - could not open {k_Scene}.");
                return;
            }

            GameObject host = Find(scene, k_Host);
            if (host == null)
            {
                Debug.LogError($"[TDLink] RESULT: FAIL - no '{k_Host}' object in {k_Scene}. The " +
                               "show timeline is supposed to live there; if it has been renamed, " +
                               "point k_Host at the new name.");
                return;
            }

            if (host.GetComponent<ShowTimelineDriver>() == null)
            {
                // Not fatal -- the sender falls back to zero show time -- but it means the stream
                // would carry poses and a clock that is not the show's, which is worth saying.
                Debug.LogWarning($"[TDLink] '{k_Host}' has no ShowTimelineDriver, so /show/time " +
                                 "will stream 0. Is this the right object?");
            }

            var existing = host.GetComponent<TouchDesignerSender>();
            if (existing != null)
            {
                Debug.Log($"[TDLink] a sender is already on '{k_Host}'; settings left alone.\n" +
                          "[TDLink] RESULT: PASS");
                return;
            }

            host.AddComponent<TouchDesignerSender>();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, k_Scene))
            {
                Debug.LogError($"[TDLink] RESULT: FAIL - could not save {k_Scene}.");
                return;
            }

            Debug.Log($"[TDLink] added TouchDesignerSender to '{k_Host}' in {k_Scene}.\n" +
                      "[TDLink] defaults: 127.0.0.1:10000, 60 Hz, addresses under '/cb', " +
                      "right-handed conversion ON.\n" +
                      "[TDLink] the host can also be set at launch with '-tdhost <ip>', which " +
                      "overrides the field -- the way to point a build at the venue's TD " +
                      "machine without rebuilding.\n" +
                      "[TDLink] RESULT: PASS");
        }

        /// <summary>
        /// By name, across the whole scene including inactive objects -- the show's objects are
        /// not all enabled at edit time, and GameObject.Find would skip exactly those.
        /// </summary>
        static GameObject Find(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;

                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.name == name) return t.gameObject;
            }
            return null;
        }
    }
}
