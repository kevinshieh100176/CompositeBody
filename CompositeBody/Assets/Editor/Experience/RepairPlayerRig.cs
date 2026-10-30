using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using CompositeBody.Avatar.Skin;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Silences the two NullReferenceExceptions the player rig throws every frame, by fixing what
    /// causes them rather than by switching anything off.
    ///
    /// BOTH ARE THE SAME KIND OF FAULT: a reference the rig expects to be there and is not, in a
    /// place where the package code dereferences it without checking. Neither is a Unity bug and
    /// neither was going to stop on its own.
    ///
    /// 1. No LocomotionMediator. The rig is still wired the XRI 2 way, with a LocomotionSystem,
    ///    and XRI 3's providers want a mediator instead. The legacy providers survive this by
    ///    falling back to the LocomotionSystem path, which is why snap turn worked -- but
    ///    DynamicMoveProvider derives from the new ContinuousMoveProvider, whose Update reads
    ///    mediator.xrOrigin unguarded on the very first line. Adding the mediator stops the
    ///    exception AND gives the stick back its move provider, which is the same fix.
    ///
    /// 2. The pinch point's material is missing. Each Pinch Visual renderer has two material
    ///    slots -- a depth-only pass and a rim-lit dot -- and the second one's asset is gone from
    ///    the project, so sharedMaterials[1] is null. The affordance receivers on that object
    ///    read a colour off it every frame to capture their starting value, and reading a
    ///    property off a null material is the exception. The material is rebuilt from
    ///    RimLight.shadergraph, which is the shader whose properties those receivers name:
    ///    _MainColor and _Power, which match nothing else in the project.
    ///
    /// Idempotent. Run it again and it reports that there was nothing to do.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RepairPlayerRig.Run
    /// </summary>
    public static class RepairPlayerRig
    {
        const string k_RigPath = "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRMPT_XR_Origin_Setup.prefab";
        const string k_RimShader = "Assets/Samples/XR Interaction Toolkit/3.3.0/Hands Interaction Demo/Shaders/RimLight.shadergraph";
        const string k_PinchMaterial = "Assets/VRMPAssets/Materials/PinchPointRimLight.mat";

        [MenuItem("Tools/O-0/Repair Player Rig")]
        public static void Run()
        {
            Debug.Log("[RigRepair] Starting...");

            GameObject root = PrefabUtility.LoadPrefabContents(k_RigPath);
            if (root == null)
            {
                Debug.LogError($"[RigRepair] RESULT: FAIL - could not open {k_RigPath}.");
                return;
            }

            try
            {
                bool mediator = AddLocomotionMediator(root);
                int slots = RepairPinchMaterials(root);
                int films = JoinHandFilmsToFormation(root);

                if (!mediator && slots == 0 && films == 0)
                {
                    Debug.Log("[RigRepair] nothing to do; the rig is already repaired." +
                              "\n[RigRepair] RESULT: PASS");
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, k_RigPath);
                Debug.Log($"[RigRepair] mediator {(mediator ? "added" : "already present")}, " +
                          $"{slots} pinch material slot(s) filled, " +
                          $"{films} hand film(s) joined to the formation." +
                          "\n[RigRepair] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Put the mediator on whatever object already carries the locomotion stack, so the
        /// providers find it with GetComponentInParent rather than by a project-wide search.
        /// XRBodyTransformer comes with it through RequireComponent, and locates the XROrigin
        /// itself.
        /// </summary>
        static bool AddLocomotionMediator(GameObject root)
        {
            if (root.GetComponentInChildren<LocomotionMediator>(true) != null) return false;

            // The providers live under the same object as the old LocomotionSystem, so that is
            // where a mediator has to be for GetComponentInParent to reach it.
            Transform host = null;
#pragma warning disable CS0618 // LocomotionSystem is the deprecated XRI 2 class; finding it is the point.
            var legacy = root.GetComponentInChildren<
                UnityEngine.XR.Interaction.Toolkit.LocomotionSystem>(true);
#pragma warning restore CS0618
            if (legacy != null) host = legacy.transform;

            if (host == null)
            {
                var provider = root.GetComponentInChildren<LocomotionProvider>(true);
                host = provider != null ? provider.transform.parent : null;
            }
            if (host == null) host = root.transform;

            host.gameObject.AddComponent<LocomotionMediator>();
            Debug.Log($"[RigRepair] added LocomotionMediator (and XRBodyTransformer) to " +
                      $"'{host.name}'. The stick's move provider can reach a mediator now.");
            return true;
        }

        /// <summary>
        /// Fill the empty material slots that affordance receivers read, and return how many
        /// were filled. Only those, and only when null: an empty slot nothing reads is somebody's
        /// decision, not a fault.
        /// </summary>
        static int RepairPinchMaterials(GameObject root)
        {
            Material pinch = null;
            int filled = 0;

            // ONLY the slot an affordance helper actually reads. Sweeping every renderer for
            // empty slots looked tidier and was wrong: the rig has six more -- the poke points,
            // the controller models -- whose empty slot is nobody's bug, and filling those with
            // a visible rim-light material would have put glowing markers into the piece to fix
            // an exception they were not throwing.
#pragma warning disable CS0618 // The affordance system is deprecated in XRI 3; it is still what throws.
            var helpers = root.GetComponentsInChildren<
                UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Rendering.MaterialPropertyBlockHelper>(true);

            foreach (var helper in helpers)
            {
                Renderer renderer = helper.rendererTarget;
                if (renderer == null) continue;

                int index = helper.materialIndex;
#pragma warning restore CS0618
                Material[] materials = renderer.sharedMaterials;
                if (index < 0 || index >= materials.Length || materials[index] != null) continue;

                pinch ??= LoadOrCreatePinchMaterial();
                if (pinch == null) return filled;

                materials[index] = pinch;
                renderer.sharedMaterials = materials;
                filled++;
                Debug.Log($"[RigRepair] '{renderer.name}' material slot {index} was empty and is " +
                          $"read every frame by an affordance receiver; filled with {pinch.name}.");
            }

            return filled;
        }

        /// <summary>
        /// Give the rig's hand films a MembraneReveal and the component that hands them to
        /// AvatarFormation, and return how many were newly joined.
        ///
        /// These are the hands the PLAYER sees -- the XR hand visuals on the rig. The avatar's
        /// own hand films are what the other player sees and are handled with the rest of the
        /// avatar; these were left fully formed, so a player who had touched nothing still had
        /// wrapped hands in front of their face.
        ///
        /// Found by having a MembraneFilmLink, not by name. The films are named after whatever
        /// the rig they were built from was called, and a repair that stops working because
        /// somebody renamed a hand is not a repair.
        /// </summary>
        static int JoinHandFilmsToFormation(GameObject root)
        {
            int joined = 0;

            foreach (var link in root.GetComponentsInChildren<MembraneFilmLink>(true))
            {
                if (link.GetComponent<RigHandFilm>() != null) continue;

                if (link.GetComponent<MembraneReveal>() == null)
                    link.gameObject.AddComponent<MembraneReveal>();

                link.gameObject.AddComponent<RigHandFilm>();
                joined++;
                Debug.Log($"[RigRepair] '{link.name}' now grows with the body on the touch.");
            }

            return joined;
        }

        static Material LoadOrCreatePinchMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_PinchMaterial);
            if (existing != null) return existing;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(k_RimShader);
            if (shader == null)
            {
                Debug.LogError($"[RigRepair] no shader at {k_RimShader}, so the empty pinch " +
                               "material slot cannot be filled and the affordance receivers " +
                               "will keep throwing.");
                return null;
            }

            var material = new Material(shader) { name = "PinchPointRimLight" };

            // Starting values only. The affordance receivers capture whatever is here on their
            // first frame and then tween from it, so these are a baseline rather than the look.
            if (material.HasProperty("_MainColor"))
                material.SetColor("_MainColor", new Color(0.72f, 0.78f, 0.95f, 1f));
            if (material.HasProperty("_EdgeColor"))
                material.SetColor("_EdgeColor", new Color(0.85f, 0.90f, 1f, 1f));
            if (material.HasProperty("_Power"))
                material.SetFloat("_Power", 1.5f);

            AssetDatabase.CreateAsset(material, k_PinchMaterial);
            Debug.Log($"[RigRepair] created {k_PinchMaterial} on RimLight.");
            return material;
        }
    }
}
