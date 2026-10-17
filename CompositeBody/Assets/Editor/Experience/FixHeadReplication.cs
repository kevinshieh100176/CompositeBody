using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.Netcode.Components;
using XRMultiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Activates the player prefab's Head object, so the head pose actually replicates.
    ///
    /// THE BUG. Head was inactive in the prefab. A NetworkBehaviour on an inactive GameObject
    /// never receives OnNetworkSpawn, so NetworkTransform.InternalInitialization never runs, so
    /// CanCommitToTransform stays false and the owner transmits nothing -- for the whole session,
    /// silently.
    ///
    /// WHY IT LOOKED FINE LOCALLY. XRINetworkPlayer.LateUpdate writes head.SetPositionAndRotation
    /// every frame, and writing to a Transform works perfectly well on an inactive object. So the
    /// owner saw its own head tracking correctly while nothing was being sent, and the other
    /// machine showed the avatar standing at the world origin with no error anywhere. Left and
    /// Right were active, so the HANDS replicated and only the head did not -- which is a strange
    /// enough symptom to send you looking in the wrong place.
    ///
    /// Head carries a Transform and a ClientNetworkTransform and nothing else, so activating it
    /// cannot make anything appear. This logs any renderer it would switch on regardless, rather
    /// than asserting that from a reading of the YAML.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.FixHeadReplication.Run
    /// </summary>
    public static class FixHeadReplication
    {
        const string k_Prefab =
            "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";

        [MenuItem("Tools/Verify/Fix Head Replication")]
        public static void Run()
        {
            Debug.Log("[HeadFix] Starting...");

            GameObject root = PrefabUtility.LoadPrefabContents(k_Prefab);
            if (root == null)
            {
                Debug.LogError($"[HeadFix] RESULT: FAIL - no prefab at {k_Prefab}.");
                return;
            }

            try
            {
                var player = root.GetComponentInChildren<XRINetworkPlayer>(true);
                if (player == null)
                {
                    Debug.LogError("[HeadFix] RESULT: FAIL - no XRINetworkPlayer.");
                    return;
                }

                var report = new StringBuilder();
                bool changed = false;

                changed |= Activate(player.head, "head", report);
                changed |= Activate(player.leftHand, "leftHand", report);
                changed |= Activate(player.rightHand, "rightHand", report);

                Debug.Log(report.ToString());

                if (!changed)
                {
                    Debug.Log("[HeadFix] everything was already active.\n[HeadFix] RESULT: PASS");
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, k_Prefab);
                Debug.Log("[HeadFix] prefab saved.\n[HeadFix] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static bool Activate(Transform target, string label, StringBuilder report)
        {
            if (target == null)
            {
                report.AppendLine($"[HeadFix] {label} is not assigned on the prefab.");
                return false;
            }

            var networkTransform = target.GetComponent<NetworkTransform>();
            report.AppendLine($"[HeadFix] {label} '{target.name}' active={target.gameObject.activeSelf} " +
                              $"networkTransform={(networkTransform != null ? networkTransform.GetType().Name : "NONE")}");

            if (networkTransform == null)
                report.AppendLine($"[HeadFix]   WARNING: {label} has no NetworkTransform, so it " +
                                  "cannot replicate however it is configured.");

            if (target.gameObject.activeSelf) return false;

            // Say what switching it on would reveal, rather than assuming it reveals nothing.
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            report.AppendLine($"[HeadFix]   activating. Renderers in this subtree: {renderers.Length}");
            foreach (Renderer r in renderers)
                report.AppendLine($"[HeadFix]     would render: {r.name} ({r.GetType().Name})");

            target.gameObject.SetActive(true);
            return true;
        }
    }
}
