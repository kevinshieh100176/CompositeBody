using System.Collections.Generic;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;
using CompositeBody.PointClouds;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Opens MainScene and checks everything a test run needs, before anyone puts a headset on.
    ///
    /// Written because every failure here looks the same from inside a headset -- black, or a
    /// room with nothing in it -- and the causes are completely different: no rig, no network
    /// manager, a beat whose content root came unwired, a prefab instance that lost its link, a
    /// NetworkObject hashing to zero so the beat index never leaves the host. Ten minutes of
    /// putting the headset on and taking it off again, or one batch run.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.VerifyMainScene.Run
    /// </summary>
    public static class VerifyMainScene
    {
        const string k_Scene = "Assets/_Scenes/MainScene.unity";

        static int s_Fail;

        [MenuItem("Tools/O-0/Verify MainScene")]
        static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run();
        }

        public static void Run()
        {
            Debug.Log("[MainScene] Starting...");
            s_Fail = 0;

            Scene scene = EditorSceneManager.OpenScene(k_Scene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[MainScene] RESULT: FAIL - could not open {k_Scene}.");
                return;
            }

            CheckRig();
            CheckNetwork();
            CheckShow();
            CheckBeat();
            CheckRoom();
            CheckForMissingScripts(scene);
            CheckBuildSettings();

            Debug.Log(s_Fail == 0
                ? "[MainScene] RESULT: PASS"
                : $"[MainScene] {s_Fail} problem(s).\n[MainScene] RESULT: FAIL");
        }

        static void CheckRig()
        {
            var origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (!Require(origin != null, "XR rig present")) return;

            // The camera lives inside the rig prefab, which is why looking for a Camera component
            // in the scene YAML finds nothing and means nothing.
            Require(origin.Camera != null, "XR rig has a camera");
            Require(Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include) != null,
                    "a camera exists to render from");
        }

        static void CheckNetwork()
        {
            var manager = Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
            if (!Require(manager != null, "NetworkManager present")) return;

            // Without a player prefab, hosting succeeds and no one ever appears.
            Require(manager.NetworkConfig != null && manager.NetworkConfig.PlayerPrefab != null,
                    "NetworkManager has a player prefab");

            Require(HasComponentNamed("XRINetworkGameManager"),
                    "XRINetworkGameManager present (the Host button calls it)");
            Require(HasComponentNamed("GameSessionManager"), "GameSessionManager present");
            Require(HasComponentNamed("StaffControlPanel"), "StaffControlPanel present (Host Session)");
        }

        static void CheckShow()
        {
            var director = Object.FindFirstObjectByType<ExperienceDirector>(FindObjectsInactive.Include);
            if (!Require(director != null, "ExperienceDirector present")) return;

            var netObj = director.GetComponent<NetworkObject>();
            if (Require(netObj != null, "ExperienceDirector has a NetworkObject"))
            {
                // Zero means NGO cannot match it between host and client, so the beat index
                // never reaches the second headset and the show runs on the host alone.
                Require(netObj.PrefabIdHash != 0, "ExperienceDirector's NetworkObject hash is set");
            }

            Require(Object.FindFirstObjectByType<ScreenFade>(FindObjectsInactive.Include) != null,
                    "ScreenFade present");
        }

        static void CheckBeat()
        {
            var beat = Object.FindFirstObjectByType<O0ArrivalBeat>(FindObjectsInactive.Include);
            if (!Require(beat != null, "O-0 beat present")) return;

            Require(PrefabUtility.GetPrefabInstanceStatus(beat.gameObject) == PrefabInstanceStatus.Connected,
                    "O-0 is a connected prefab instance");

            var so = new SerializedObject(beat);
            Require(so.FindProperty("m_ContentRoot").objectReferenceValue != null,
                    "O-0 has a content root to switch on");
            Require(so.FindProperty("m_Director").objectReferenceValue != null,
                    "O-0's cue has its Timeline director");

            Transform root = beat.transform;
            var playable = root.GetComponentInChildren<PlayableDirector>(true);
            if (Require(playable != null, "O-0 has a PlayableDirector"))
            {
                Require(playable.playableAsset != null, "the director has a timeline assigned");
                // Driven from ExperienceClock by the beat. On Game Time the two headsets would
                // run the same cross-fade at different moments.
                Require(playable.timeUpdateMode == DirectorUpdateMode.Manual,
                        "the director is on Manual (driven from ExperienceClock)");
            }

            int figures = root.GetComponentsInChildren<PointCloudFigure>(true).Length;
            Require(figures == 2, $"two 真人 point clouds (found {figures})");

            foreach (var figure in root.GetComponentsInChildren<PointCloudFigure>(true))
            {
                var filter = figure.GetComponent<MeshFilter>();
                Require(filter != null && filter.sharedMesh != null && filter.sharedMesh.vertexCount > 1000,
                        $"{figure.transform.parent?.name} has a point cloud mesh");
            }

            int spots = root.GetComponentsInChildren<VolumetricSpot>(true).Length;
            Require(spots == 3, $"three fixtures: one white, two 光圈 (found {spots})");

            Require(root.GetComponentInChildren<O0TouchAwakening>(true) != null,
                    "the touch interaction is present");
            int plates = root.GetComponentsInChildren<MusicBoxFloor>(true).Length;
            Require(plates == 3, $"three music-box plates (found {plates})");
            int hands = root.GetComponentsInChildren<HandCloud>(true).Length;
            Require(hands == 2, $"two hand clouds (found {hands})");

            Require(root.GetComponentInChildren<UnityEngine.VFX.VisualEffect>(true) != null,
                    "the dust VFX is present");
        }

        static void CheckRoom()
        {
            var bounds = Object.FindFirstObjectByType<PlaySpaceBounds>(FindObjectsInactive.Include);
            if (!Require(bounds != null, "play space outline present")) return;
            Debug.Log($"[MainScene] note  play space is {bounds.size.x} x {bounds.size.y} m");

            int spawns = 0;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                spawns += CountNamed(root.transform, "_Spawn");
            Require(spawns >= 2, $"two spawn points (found {spawns})");
        }

        /// <summary>
        /// A null component is a script that failed to compile, moved namespace or was deleted.
        /// It shows up in the inspector as "Missing (Mono Script)" and nowhere else -- and the
        /// object it was on silently does nothing.
        /// </summary>
        static void CheckForMissingScripts(Scene scene)
        {
            int missing = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    Component[] components = t.GetComponents<Component>();
                    for (int i = 0; i < components.Length; i++)
                    {
                        if (components[i] != null) continue;
                        missing++;
                        Debug.LogError($"[MainScene] missing script on '{Path(t)}' (slot {i})");
                    }
                }
            }
            Require(missing == 0, $"no missing scripts (found {missing})");
        }

        static void CheckBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int index = -1;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == k_Scene && scenes[i].enabled) { index = i; break; }
            }
            Require(index >= 0, "MainScene is in build settings and enabled");
            if (index > 0)
                Debug.Log($"[MainScene] note  MainScene is entry {index}; a build boots into " +
                          $"'{scenes[0].path}' instead.");
        }

        static int CountNamed(Transform parent, string suffix)
        {
            int n = parent.name.EndsWith(suffix) ? 1 : 0;
            foreach (Transform child in parent) n += CountNamed(child, suffix);
            return n;
        }

        static bool HasComponentNamed(string typeName)
        {
            foreach (var c in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c != null && c.GetType().Name == typeName) return true;
            }
            return false;
        }

        static string Path(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts);
        }

        static bool Require(bool condition, string what)
        {
            if (condition) { Debug.Log($"[MainScene] ok   {what}"); return true; }
            Debug.LogError($"[MainScene] FAILED: {what}");
            s_Fail++;
            return false;
        }
    }
}
