using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// One-shot batch-mode setup: adds the new LAN multiplayer scaffold objects into
    /// SampleScene (the only fully-wired scene -- has NetworkManager/XRINetworkGameManager/XR
    /// Origin already working). Run via:
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildMultiplayerScaffold.Run
    /// Safe to re-run: skips creating objects that already exist by name.
    /// </summary>
    public static class BuildMultiplayerScaffold
    {
        const string k_ScenePath = "Assets/Scenes/SampleScene.unity";

        public static void Run()
        {
            Debug.Log("[BuildMultiplayerScaffold] Starting...");

            var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[BuildMultiplayerScaffold] Could not open scene at {k_ScenePath}");
                return;
            }

            bool changed = false;
            changed |= EnsureGameSessionManager();
            changed |= EnsureStaffControlPanel();
            changed |= EnsureCalibrationMarker();

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[BuildMultiplayerScaffold] Scene saved.");
            }
            else
            {
                Debug.Log("[BuildMultiplayerScaffold] Nothing to do, scaffold already present.");
            }

            Debug.Log("[BuildMultiplayerScaffold] Done.");
        }

        static bool EnsureGameSessionManager()
        {
            if (GameObject.Find("GameSessionManager") != null) return false;

            var go = new GameObject("GameSessionManager");
            go.AddComponent<NetworkObject>();
            go.AddComponent<GameSessionManager>();
            go.AddComponent<StoryProgressManager>();

            Debug.Log("[BuildMultiplayerScaffold] Created GameSessionManager.");
            return true;
        }

        static bool EnsureStaffControlPanel()
        {
            if (GameObject.Find("StaffControlPanel") != null) return false;

            var go = new GameObject("StaffControlPanel");
            go.AddComponent<StaffControlPanel>();

            Debug.Log("[BuildMultiplayerScaffold] Created StaffControlPanel.");
            return true;
        }

        static bool EnsureCalibrationMarker()
        {
            if (GameObject.Find("CalibrationMarker") != null) return false;

            var go = new GameObject("CalibrationMarker");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.AddComponent<CalibrationPoint>();

            Debug.Log("[BuildMultiplayerScaffold] Created CalibrationMarker at scene origin -- move it to your physical floor marker's position/orientation.");
            return true;
        }
    }
}
