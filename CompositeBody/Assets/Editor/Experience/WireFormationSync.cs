using UnityEditor;
using UnityEngine;
using CompositeBody.Experience;
using XRMultiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Wires up "you are a light until you touch": the replication component onto the player
    /// prefab, and a volumetric presence light onto the onboarding's blob presenter.
    ///
    /// DELIBERATELY MINIMAL ON THE PLAYER PREFAB. It adds one component if it is missing and
    /// touches nothing else. That prefab is the spine of the session -- this project has already
    /// lost an afternoon to a repair script that filled in eight slots when two were wanted --
    /// and a NetworkBehaviour added to it changes what every client expects at spawn.
    ///
    /// THE LIGHT GOES BESIDE THE PRESENTER, NOT UNDER THE AVATAR. RoleBlobPresenter drives the
    /// stand-in's world position itself, so parenting it to the remote player would fight that
    /// every frame -- and the presenter hides every renderer under the remote avatar, which would
    /// include the light.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.WireFormationSync.Run
    /// </summary>
    public static class WireFormationSync
    {
        const string k_PlayerPrefab =
            "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";
        const string k_Onboarding = "Assets/_Scenes/Onboarding.prefab";
        const string k_GlowMaterial = "Assets/Materials/OrbitGlow.mat";
        const string k_LightName = "Remote Presence Light";

        [MenuItem("Tools/Onboarding/Wire Formation Sync")]
        public static void Run()
        {
            Debug.Log("[WireSync] Starting...");

            if (!AddSyncToPlayer()) return;
            if (!AddPresenceLight()) return;

            AssetDatabase.SaveAssets();
            Debug.Log("[WireSync] RESULT: PASS");
        }

        static bool AddSyncToPlayer()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(k_PlayerPrefab);
            if (root == null)
            {
                Debug.LogError($"[WireSync] RESULT: FAIL - no prefab at {k_PlayerPrefab}.");
                return false;
            }

            try
            {
                // On the same GameObject as XRINetworkPlayer, because AvatarFormationSync.For
                // looks it up with GetComponent on exactly that object from both sides of the
                // connection.
                var player = root.GetComponentInChildren<XRINetworkPlayer>(true);
                if (player == null)
                {
                    Debug.LogError("[WireSync] RESULT: FAIL - no XRINetworkPlayer in the player " +
                                   "prefab; nothing to hang the sync on.");
                    return false;
                }

                if (player.GetComponent<AvatarFormationSync>() != null)
                {
                    Debug.Log("[WireSync] the player prefab already has AvatarFormationSync.");
                    return true;
                }

                player.gameObject.AddComponent<AvatarFormationSync>();
                PrefabUtility.SaveAsPrefabAsset(root, k_PlayerPrefab);

                Debug.Log($"[WireSync] added AvatarFormationSync to '{player.name}' in the " +
                          "player prefab. Every client now expects it at spawn, so both machines " +
                          "must be on this build.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static bool AddPresenceLight()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(k_Onboarding);
            if (root == null)
            {
                Debug.LogError($"[WireSync] RESULT: FAIL - no prefab at {k_Onboarding}.");
                return false;
            }

            try
            {
                var presenter = root.GetComponentInChildren<RoleBlobPresenter>(true);
                if (presenter == null)
                {
                    Debug.LogError("[WireSync] RESULT: FAIL - no RoleBlobPresenter in the " +
                                   "onboarding prefab.");
                    return false;
                }

                var so = new SerializedObject(presenter);
                SerializedProperty slot = so.FindProperty("m_PresenceLight");
                if (slot == null)
                {
                    Debug.LogError("[WireSync] RESULT: FAIL - RoleBlobPresenter has no " +
                                   "m_PresenceLight field; is the script out of date?");
                    return false;
                }

                if (slot.objectReferenceValue != null)
                {
                    Debug.Log("[WireSync] a presence light is already assigned; left as it is.");
                    return true;
                }

                Transform parent = presenter.transform;
                Transform existing = parent.Find(k_LightName);

                GameObject light = existing != null ? existing.gameObject : BuildLight(parent);
                if (light == null) return false;

                slot.objectReferenceValue = light.GetComponent<VolumetricPoint>();
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, k_Onboarding);
                Debug.Log($"[WireSync] added '{k_LightName}' and assigned it to the presenter.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static GameObject BuildLight(Transform parent)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_GlowMaterial);
            if (material == null)
            {
                Debug.LogError($"[WireSync] RESULT: FAIL - no glow material at {k_GlowMaterial}.");
                return null;
            }

            // The same unit sphere the orbit glows use: VolumetricPoint reads its radius off the
            // transform scale, so the mesh only has to be a sphere of radius 0.5.
            GameObject light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            light.name = k_LightName;
            light.transform.SetParent(parent, false);

            // Air does not collide with anything.
            Object.DestroyImmediate(light.GetComponent<Collider>());

            var renderer = light.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            light.AddComponent<VolumetricPoint>();
            return light;
        }
    }
}
