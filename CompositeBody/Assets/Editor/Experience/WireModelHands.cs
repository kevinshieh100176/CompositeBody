using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using CompositeBody.Avatar.Body;
using XRMultiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Switches the avatar from the separate tracked hands to Ch36's own, by un-collapsing its
    /// hand bones and adding the retarget that makes its fingers follow the tracked skeleton.
    ///
    /// WHAT IT CHANGES, AND WHAT IT DOES NOT. MembraneBodyRig shrinks Ch36's hand bones to a
    /// hundredth of their size so the separate mesh can stand in for them; that is switched off.
    /// The tracked skeleton itself is left running and still replicates -- only its mesh is
    /// hidden, by the retarget, at runtime. Nothing about the networking changes, which is the
    /// point: the fingers already arrive on both machines.
    ///
    /// Re-runnable. The driver and target roots are only filled when empty, so a hand pointed
    /// somewhere by hand stays pointed there.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.WireModelHands.Run
    /// </summary>
    public static class WireModelHands
    {
        const string k_Prefab =
            "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";

        [MenuItem("Tools/Avatar/Use The Model's Hands")]
        public static void Run()
        {
            Debug.Log("[ModelHands] Starting...");

            GameObject root = PrefabUtility.LoadPrefabContents(k_Prefab);
            if (root == null)
            {
                Debug.LogError($"[ModelHands] RESULT: FAIL - no prefab at {k_Prefab}.");
                return;
            }

            try
            {
                var report = new StringBuilder();

                var body = root.GetComponentInChildren<MembraneBodyRig>(true);
                if (body == null)
                {
                    Debug.LogError("[ModelHands] RESULT: FAIL - no MembraneBodyRig in the prefab.");
                    return;
                }

                // Stop hiding Ch36's hands.
                var so = new SerializedObject(body);
                SerializedProperty collapse = so.FindProperty("m_CollapseHands");
                if (collapse == null)
                {
                    Debug.LogError("[ModelHands] RESULT: FAIL - no m_CollapseHands field.");
                    return;
                }

                report.AppendLine($"[ModelHands] m_CollapseHands {collapse.boolValue} -> false");
                collapse.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();

                var retarget = body.GetComponent<MembraneHandRetarget>();
                if (retarget == null)
                {
                    retarget = body.gameObject.AddComponent<MembraneHandRetarget>();
                    report.AppendLine("[ModelHands] added MembraneHandRetarget beside the body rig.");
                }

                var player = root.GetComponentInChildren<XRINetworkPlayer>(true);
                if (player == null)
                {
                    Debug.LogError("[ModelHands] RESULT: FAIL - no XRINetworkPlayer.");
                    return;
                }

                // The networked hand transforms are the drivers' roots: the tracked skeleton
                // lives under them, and they are what MembraneBodyRig already solves the wrist
                // onto, so driver and target start from the same place.
                Wire(retarget, "m_Left", player.leftHand, FindBone(body, "LeftHand"), report);
                Wire(retarget, "m_Right", player.rightHand, FindBone(body, "RightHand"), report);

                Debug.Log(report.ToString());

                PrefabUtility.SaveAsPrefabAsset(root, k_Prefab);
                Debug.Log("[ModelHands] prefab saved.\n[ModelHands] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Ch36's wrist. Matched on the exact bone name rather than a suffix, because
        /// "LeftHand" is also the start of LeftHandIndex1 and every other finger bone.
        /// </summary>
        static Transform FindBone(Component body, string exactSuffix)
        {
            Transform best = null;

            foreach (Transform t in body.transform.root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.EndsWith(exactSuffix, System.StringComparison.Ordinal)) continue;
                if (!t.name.StartsWith("mixamorig", System.StringComparison.Ordinal)) continue;

                // Shortest match wins: mixamorig1:LeftHand, not mixamorig1:LeftHandThumb1.
                if (best == null || t.name.Length < best.name.Length) best = t;
            }

            return best;
        }

        static void Wire(MembraneHandRetarget retarget, string fieldName,
                         Transform driver, Transform target, StringBuilder report)
        {
            FieldInfo field = typeof(MembraneHandRetarget).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (field?.GetValue(retarget) is not MembraneHandRetarget.Hand hand)
            {
                report.AppendLine($"[ModelHands] {fieldName}: no such field.");
                return;
            }

            if (hand.driverRoot == null) hand.driverRoot = driver;
            if (hand.targetRoot == null) hand.targetRoot = target;

            report.AppendLine($"[ModelHands] {fieldName}: driver=" +
                              $"'{(hand.driverRoot != null ? hand.driverRoot.name : "NONE")}' " +
                              $"target='{(hand.targetRoot != null ? hand.targetRoot.name : "NONE")}'");

            if (hand.driverRoot == null || hand.targetRoot == null)
                report.AppendLine($"[ModelHands]   *** {fieldName} incomplete -- those fingers " +
                                  "will not follow ***");

            EditorUtility.SetDirty(retarget);
        }
    }
}
