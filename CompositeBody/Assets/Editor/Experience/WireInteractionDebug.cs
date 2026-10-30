using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts the interaction debug surface into the main scene: the relay beside the session
    /// managers, and the panel on an object of its own.
    ///
    /// THE PANEL CANNOT SHARE THE STAFF PANEL'S OBJECT. Both build their own Screen Space
    /// Overlay canvas at runtime and a GameObject may hold only one Canvas, so the second
    /// component to run would fail. They are anchored to opposite top corners instead.
    ///
    /// THE RELAY GOES ON THE SESSION MANAGER OBJECT because that is where the NetworkObject is.
    /// It is also the right lifetime: the states it mirrors belong to the session, and the
    /// experience scene is loaded and unloaded underneath it.
    ///
    /// Re-running is safe; existing components are left alone.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.WireInteractionDebug.Run
    /// </summary>
    public static class WireInteractionDebug
    {
        const string k_ScenePath = "Assets/_Scenes/MainScene.unity";
        const string k_PanelObject = "InteractionDebugPanel";

        [MenuItem("Tools/Diagnostics/Wire Interaction Debug Panel")]
        public static void Run()
        {
            Debug.Log("[WireDebug] Starting...");

            var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[WireDebug] RESULT: FAIL - could not open {k_ScenePath}.");
                return;
            }

            bool changed = false;

            if (!AddRelay(ref changed)) return;
            AddPanel(ref changed);

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[WireDebug] saved the scene.");
            }
            else
            {
                Debug.Log("[WireDebug] nothing to change.");
            }

            Debug.Log("[WireDebug] RESULT: PASS");
        }

        static bool AddRelay(ref bool changed)
        {
            var session = Object.FindFirstObjectByType<GameSessionManager>(FindObjectsInactive.Include);
            if (session == null)
            {
                Debug.LogError("[WireDebug] RESULT: FAIL - no GameSessionManager in the scene, so " +
                               "there is no NetworkObject to hang the relay on.");
                return false;
            }

            if (session.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError($"[WireDebug] RESULT: FAIL - '{session.name}' has no NetworkObject.");
                return false;
            }

            if (session.GetComponent<InteractionDebugRelay>() != null)
            {
                Debug.Log($"[WireDebug] '{session.name}' already has the relay.");
                return true;
            }

            session.gameObject.AddComponent<InteractionDebugRelay>();
            changed = true;

            Debug.Log($"[WireDebug] added InteractionDebugRelay to '{session.name}'. It is a " +
                      "NetworkBehaviour on a scene NetworkObject, so both machines must be on " +
                      "this build.");
            return true;
        }

        static void AddPanel(ref bool changed)
        {
            var existing = Object.FindFirstObjectByType<InteractionDebugPanel>(FindObjectsInactive.Include);
            if (existing != null)
            {
                if (existing.GetComponent<InteractionDebugRoster>() == null)
                {
                    existing.gameObject.AddComponent<InteractionDebugRoster>();
                    changed = true;
                    Debug.Log($"[WireDebug] added the roster to '{existing.name}' so " +
                              "switched-off interactions still get rows.");
                }
                else
                {
                    Debug.Log($"[WireDebug] '{existing.name}' already carries the panel and roster.");
                }
                return;
            }

            var go = new GameObject(k_PanelObject);
            go.AddComponent<InteractionDebugPanel>();

            // The roster beside it: interactions on switched-off objects never run their own
            // Awake, so without this the orbits, the footprints and the calibrator have no rows
            // until something turns them on -- which is the moment they stop being the question.
            go.AddComponent<InteractionDebugRoster>();
            changed = true;

            Debug.Log($"[WireDebug] added '{k_PanelObject}' with the panel on it. Press F1 on the " +
                      "desktop mirror to open it.");
        }
    }
}
