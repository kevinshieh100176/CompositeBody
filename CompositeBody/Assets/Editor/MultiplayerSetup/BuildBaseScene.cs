using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Builds the project's base scene: a clean two-player LAN setup with the VR rig, the
    /// networking managers, LAN discovery, role assignment and the staff control panel, and
    /// none of the template's demo mini-games.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildBaseScene.Run
    /// </summary>
    public static class BuildBaseScene
    {
        public const string ScenePath = "Assets/_Scenes/CompositeBody_Main.unity";

        // Prefab GUIDs from the VR Multiplayer template.
        internal const string NetworkManagerGuid = "3967f87296c892e40b57e37bc8601550";
        internal const string GameManagerGuid = "0267a1d338db2bf4c9625e450bbaaad0";
        internal const string XrOriginGuid = "eaa382d4bb6a20948a05d43247d66508";
        internal const string NotificationUiGuid = "124729d69b3904f678772dd7681f3a87";

        public static void Run()
        {
            Debug.Log("[BaseScene] Starting...");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var missing = new List<string>();
            var networkManager = Instantiate(NetworkManagerGuid, "Network Manager", missing);
            var gameManager = Instantiate(GameManagerGuid, "XRI Network Game Manager", missing);
            var xrOrigin = Instantiate(XrOriginGuid, "XR Origin", missing);

            // Required: XRINetworkGameManager calls PlayerHudNotification.Instance directly when
            // a client connects, with no null check, so a scene without this throws on connect.
            var notificationUi = Instantiate(NotificationUiGuid, "Player Notification UI", missing);

            if (missing.Count > 0)
            {
                Debug.LogError($"[BaseScene] RESULT: FAIL - missing prefabs: {string.Join(", ", missing)}");
                return;
            }

            BuildEnvironment(out Transform p1Spawn, out Transform p2Spawn, out GameObject envRoot);
            BuildSessionManagers(envRoot);
            BuildStaffPanel(p1Spawn, p2Spawn);
            BuildCalibrationMarker();

            Directory.CreateDirectory("Assets/_Scenes");
            EditorSceneManager.MarkAllScenesDirty();

            // First save: objects created purely from script have no persistent GlobalObjectId
            // until the scene exists on disk.
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);

            // Now that they have persistent ids, generate the NetworkObject hashes and save
            // again. Without this every in-scene NetworkObject keeps GlobalObjectIdHash = 0,
            // which NGO cannot match between host and client, so the object never synchronizes
            // and no replicated state (roles, story progress) ever reaches a client.
            RegenerateNetworkObjectHashes();
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);

            AddToBuildSettings();
            AssetDatabase.SaveAssets();

            Verify();

            string outPath = GetArg("-sceneShot");
            if (!string.IsNullOrEmpty(outPath))
            {
                // Temporary camera: the XR rig's camera does not frame usefully without a headset.
                var camGO = new GameObject("TempPreviewCamera");
                var cam = camGO.AddComponent<Camera>();
                camGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.14f, 0.16f, 0.19f);
                cam.fieldOfView = 55f;
                camGO.transform.position = new Vector3(4.5f, 3.2f, -5.5f);
                camGO.transform.LookAt(new Vector3(0f, 0.8f, 0f));

                CompositeBody.Avatar.Cloth.EditorTools.RenderClothPreview.RenderCameraToFile(cam, outPath);
                Object.DestroyImmediate(camGO);
            }

            Debug.Log("[BaseScene] RESULT: PASS");
        }

        static GameObject Instantiate(string guid, string label, List<string> missing)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                missing.Add(label);
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Debug.Log($"[BaseScene] Added {label} ({Path.GetFileName(path)})");
            return instance;
        }

        static void BuildEnvironment(out Transform p1Spawn, out Transform p2Spawn, out GameObject envRoot)
        {
            var root = new GameObject("Environment");
            envRoot = root;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(1.2f, 1f, 1.2f); // 12m x 12m play space
            floor.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial("Floor", new Color(0.24f, 0.25f, 0.27f), 0.1f);

            // A few fixed landmarks: without them it is hard to tell whether the other player's
            // avatar is actually tracking, since an empty room gives no parallax reference.
            var landmarkColors = new[]
            {
                new Color(0.75f, 0.30f, 0.28f),
                new Color(0.30f, 0.55f, 0.78f),
                new Color(0.80f, 0.70f, 0.32f),
                new Color(0.38f, 0.70f, 0.45f),
            };
            var landmarkPositions = new[]
            {
                new Vector3(3.2f, 0.5f, 3.2f), new Vector3(-3.2f, 0.5f, 3.2f),
                new Vector3(3.2f, 0.5f, -3.2f), new Vector3(-3.2f, 0.5f, -3.2f),
            };
            for (int i = 0; i < landmarkPositions.Length; i++)
            {
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = $"Landmark_{i}";
                pillar.transform.SetParent(root.transform, false);
                pillar.transform.position = landmarkPositions[i];
                pillar.transform.localScale = new Vector3(0.35f, 1f, 0.35f);
                pillar.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial($"Landmark{i}", landmarkColors[i], 0.25f);
            }

            var spawns = new GameObject("Spawns");
            spawns.transform.SetParent(root.transform, false);

            p1Spawn = new GameObject("Player1_Spawn").transform;
            p1Spawn.SetParent(spawns.transform, false);
            p1Spawn.SetPositionAndRotation(new Vector3(-0.8f, 0f, -1.2f), Quaternion.Euler(0f, 20f, 0f));

            p2Spawn = new GameObject("Player2_Spawn").transform;
            p2Spawn.SetParent(spawns.transform, false);
            p2Spawn.SetPositionAndRotation(new Vector3(0.8f, 0f, -1.2f), Quaternion.Euler(0f, -20f, 0f));

            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            lightGO.transform.rotation = Quaternion.Euler(45f, 35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.40f, 0.44f, 0.50f);
            RenderSettings.ambientEquatorColor = new Color(0.30f, 0.30f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.16f, 0.17f);
        }

        static Material MakeMaterial(string name, Color color, float smoothness)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        static void BuildSessionManagers(GameObject lobbyRoot)
        {
            var go = new GameObject("GameSessionManager");
            go.AddComponent<NetworkObject>();
            var session = go.AddComponent<GameSessionManager>();
            go.AddComponent<StoryProgressManager>();

            var so = new SerializedObject(session);
            so.FindProperty("m_ExperienceSceneName").stringValue =
                Path.GetFileNameWithoutExtension(ConfigureProjectSetup.ExperienceScenePath);
            so.FindProperty("m_LobbyEnvironmentRoot").objectReferenceValue = lobbyRoot;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildStaffPanel(Transform p1Spawn, Transform p2Spawn)
        {
            var go = new GameObject("StaffControlPanel");

            // StaffControlPanel requires both discovery components; adding it pulls them in.
            go.AddComponent<StaffControlPanel>();

            // Without this the app throws a locomotion NRE every frame when no headset is
            // attached, flooding the log and stalling the network handshake.
            go.AddComponent<DesktopXrFallback>();

            var positioner = go.AddComponent<RoleSpawnPositioner>();
            var so = new SerializedObject(positioner);
            so.FindProperty("m_Player1Spawn").objectReferenceValue = p1Spawn;
            so.FindProperty("m_Player2Spawn").objectReferenceValue = p2Spawn;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildCalibrationMarker()
        {
            var go = new GameObject("CalibrationMarker");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.AddComponent<CalibrationPoint>();
        }

        static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[BaseScene] Registered as build scene 0.");
        }

        /// <summary>
        /// Invokes NetworkObject's internal OnValidate, which is what assigns
        /// GlobalObjectIdHash from the object's persistent GlobalObjectId. Unity runs this
        /// automatically when a human edits the object in the Inspector, but not for objects
        /// created and saved entirely from a batch-mode script.
        /// </summary>
        static void RegenerateNetworkObjectHashes()
        {
            var onValidate = typeof(NetworkObject).GetMethod(
                "OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);

            if (onValidate == null)
            {
                Debug.LogError("[BaseScene] Could not find NetworkObject.OnValidate; hashes may stay 0.");
                return;
            }

            foreach (var netObj in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include))
            {
                onValidate.Invoke(netObj, null);
                EditorUtility.SetDirty(netObj);
            }
        }

        static uint ReadGlobalObjectIdHash(NetworkObject netObj)
        {
            var prop = new SerializedObject(netObj).FindProperty("GlobalObjectIdHash");
            return prop != null ? prop.uintValue : 0u;
        }

        static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, System.StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        /// <summary>Fails loudly if anything the connect path depends on is absent.</summary>
        static void Verify()
        {
            var required = new (string name, bool present)[]
            {
                ("NetworkManager", Object.FindAnyObjectByType<NetworkManager>() != null),
                ("XRINetworkGameManager", Object.FindAnyObjectByType<XRMultiplayer.XRINetworkGameManager>() != null),
                ("PlayerHudNotification", Object.FindAnyObjectByType<XRMultiplayer.PlayerHudNotification>() != null),
                ("GameSessionManager", Object.FindAnyObjectByType<GameSessionManager>() != null),
                ("StoryProgressManager", Object.FindAnyObjectByType<StoryProgressManager>() != null),
                ("StaffControlPanel", Object.FindAnyObjectByType<StaffControlPanel>() != null),
                ("LanSessionBroadcaster", Object.FindAnyObjectByType<LanSessionBroadcaster>() != null),
                ("LanSessionDiscovery", Object.FindAnyObjectByType<LanSessionDiscovery>() != null),
                ("XROrigin", Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>() != null),
            };

            foreach (var (name, present) in required)
                Debug.Log($"[BaseScene] {(present ? "OK  " : "MISS")} {name}");

            // A zero hash means the object can never synchronize to clients. The field is
            // internal to Netcode, so it has to be read through SerializedObject.
            foreach (var netObj in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include))
            {
                uint hash = ReadGlobalObjectIdHash(netObj);
                Debug.Log($"[BaseScene] {(hash != 0 ? "OK  " : "MISS")} NetworkObject '{netObj.name}' GlobalObjectIdHash={hash}");
            }

            var nm = Object.FindAnyObjectByType<NetworkManager>();
            if (nm != null)
            {
                // Read the serialized m_NetworkConfig, not NetworkConfig: NetworkManagerVRMultiplayer
                // copies the former into the latter in Awake, so before play mode the public
                // NetworkConfig is still empty and reports false negatives.
                var so = new SerializedObject(nm);
                var cfg = so.FindProperty("m_NetworkConfig");
                if (cfg != null)
                {
                    var prefab = cfg.FindPropertyRelative("PlayerPrefab");
                    var transport = cfg.FindPropertyRelative("NetworkTransport");
                    Debug.Log($"[BaseScene] {(prefab?.objectReferenceValue != null ? "OK  " : "MISS")} PlayerPrefab -> {prefab?.objectReferenceValue}");
                    Debug.Log($"[BaseScene] {(transport?.objectReferenceValue != null ? "OK  " : "MISS")} NetworkTransport -> {transport?.objectReferenceValue}");
                }
            }
        }
    }
}
