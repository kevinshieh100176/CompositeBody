using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.Netcode.Components;
using XRMultiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Prints how the player prefab's networked limbs are wired, and in particular whether
    /// anything that gets switched off for the local player ENCLOSES one of them.
    ///
    /// WHY. SetupLocalPlayer does `foreach (var hand in m_handsObjects) hand.SetActive(false)`.
    /// If one of those objects is an ancestor of the networked Left or Right transform, then
    /// switching it off takes the NetworkTransform down with it -- which is the same failure the
    /// head had, arriving by a different route: the owner keeps writing the pose, nothing is
    /// transmitted, and the other machine sees hands that never move.
    ///
    /// Read-only. It changes nothing; it answers a question that YAML cannot answer reliably,
    /// because "is B inside A" needs the object graph.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ReportPlayerRigWiring.Run
    /// </summary>
    public static class ReportPlayerRigWiring
    {
        const string k_Prefab =
            "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";

        [MenuItem("Tools/Verify/Report Player Rig Wiring")]
        public static void Run()
        {
            Debug.Log("[RigWiring] Starting...");

            GameObject root = PrefabUtility.LoadPrefabContents(k_Prefab);
            if (root == null)
            {
                Debug.LogError($"[RigWiring] RESULT: FAIL - no prefab at {k_Prefab}.");
                return;
            }

            try
            {
                var player = root.GetComponentInChildren<XRINetworkPlayer>(true);
                if (player == null)
                {
                    Debug.LogError("[RigWiring] RESULT: FAIL - no XRINetworkPlayer.");
                    return;
                }

                var report = new StringBuilder();

                var limbs = new (string name, Transform t)[]
                {
                    ("head", player.head),
                    ("leftHand", player.leftHand),
                    ("rightHand", player.rightHand),
                };

                report.AppendLine("[RigWiring] networked limbs:");
                foreach (var (name, t) in limbs)
                {
                    if (t == null) { report.AppendLine($"[RigWiring]   {name}: NOT ASSIGNED"); continue; }
                    var nt = t.GetComponent<NetworkTransform>();
                    report.AppendLine($"[RigWiring]   {name,-10} '{t.name}' " +
                                      $"path={Path(root.transform, t)} " +
                                      $"activeSelf={t.gameObject.activeSelf} " +
                                      $"activeInHierarchy={t.gameObject.activeInHierarchy} " +
                                      $"nt={(nt != null ? nt.GetType().Name : "NONE")}");
                }

                // The list SetupLocalPlayer switches off.
                FieldInfo field = typeof(XRINetworkPlayer).GetField(
                    "m_handsObjects", BindingFlags.Instance | BindingFlags.NonPublic);

                if (field == null)
                {
                    report.AppendLine("[RigWiring] no m_handsObjects field; the template changed.");
                }
                else if (field.GetValue(player) is GameObject[] hands)
                {
                    report.AppendLine($"[RigWiring] m_handsObjects ({hands.Length}) -- these are " +
                                      "SetActive(false) on the LOCAL player:");

                    foreach (GameObject hand in hands)
                    {
                        if (hand == null) { report.AppendLine("[RigWiring]   <null>"); continue; }

                        report.AppendLine($"[RigWiring]   '{hand.name}' " +
                                          $"path={Path(root.transform, hand.transform)}");

                        foreach (var (name, t) in limbs)
                        {
                            if (t == null) continue;
                            if (!t.IsChildOf(hand.transform)) continue;

                            report.AppendLine($"[RigWiring]     *** ENCLOSES {name} '{t.name}' -- " +
                                              "switching this off stops that limb replicating ***");
                        }
                    }
                }

                Debug.Log(report.ToString());
                Debug.Log("[RigWiring] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static string Path(Transform root, Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (Transform c = t; c != null && c != root; c = c.parent) parts.Insert(0, c.name);
            return parts.Count == 0 ? "<root>" : string.Join("/", parts);
        }
    }
}
