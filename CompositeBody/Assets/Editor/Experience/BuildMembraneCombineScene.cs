using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using XRMultiplayer;
using CompositeBody.Avatar.Skin;
using CompositeBody.Avatar.Cloth.EditorTools;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// A two-player sample scene for looking at the membrane avatar and exercising the
    /// two-handed assembly mechanic in a headset.
    ///
    /// It is a full session rather than a static preview, because both of the things being
    /// tested only exist between two people: the membrane has to be seen on somebody else from
    /// across a room, and a <see cref="CompositeHalf"/> pair cannot assemble at all unless both
    /// players are holding their own half.
    ///
    /// Each half carries the template's networked physics stack on purpose. The server decides
    /// the weld by comparing the two halves' positions, so without a client-authoritative
    /// transform replicating what the grabbing players are doing, the server would be measuring
    /// against stale poses and the pair would never come together -- which from inside a headset
    /// looks exactly like the mechanic being broken.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildMembraneCombineScene.Run
    /// </summary>
    public static class BuildMembraneCombineScene
    {
        public const string ScenePath = "Assets/_Scenes/MembraneCombineTest.unity";

        const string k_FilmMeshPath = "Assets/_models/Ch36_CombineTestFilm.asset";
        const string k_Player1FilmMaterial = "Assets/Materials/MembraneFilm_Player1.mat";
        const string k_Player2FilmMaterial = "Assets/Materials/MembraneFilm_Player2.mat";
        const string k_HalfMaterialPath = "Assets/Materials/CombineHalfSolid.mat";
        const string k_GhostMaterialPath = "Assets/Materials/GhostHalf.mat";

        // Same relaxation settings the membrane preview scene was tuned with.
        const float k_BaseOffset = 0.020f;
        const float k_MinOffset = 0.006f;
        const int k_SmoothIterations = 900;
        const float k_SmoothLambda = 0.60f;

        const string k_PairId = "sample_box";
        const float k_HalfWidth = 0.18f;

        public static void Run()
        {
            Debug.Log("[Combine] Starting...");

            if (!CheckShader("CompositeBody/VacuumMembrane", out Shader membraneShader)) return;
            if (!CheckShader("CompositeBody/GhostHalf", out Shader ghostShader)) return;

            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("[Combine] RESULT: FAIL - URP/Lit not found.");
                return;
            }

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeAvatarRig.AvatarFbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[Combine] RESULT: FAIL - avatar FBX not found at {ProbeAvatarRig.AvatarFbxPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var missing = new List<string>();
            Instantiate(BuildBaseScene.NetworkManagerGuid, "Network Manager", missing);
            Instantiate(BuildBaseScene.GameManagerGuid, "XRI Network Game Manager", missing);
            Instantiate(BuildBaseScene.XrOriginGuid, "XR Origin", missing);

            // XRINetworkGameManager calls PlayerHudNotification.Instance with no null check when
            // a client connects, so a scene without this throws on connect.
            Instantiate(BuildBaseScene.NotificationUiGuid, "Player Notification UI", missing);

            if (missing.Count > 0)
            {
                Debug.LogError($"[Combine] RESULT: FAIL - missing template prefabs: {string.Join(", ", missing)}");
                return;
            }

            BuildEnvironment(litShader, out Transform p1Spawn, out Transform p2Spawn);
            BuildSessionManagers();
            BuildStaffAndCalibration(p1Spawn, p2Spawn);

            Mesh filmMesh = BuildFilmMesh(fbx);
            if (filmMesh == null) return;

            BuildMembraneFigure(fbx, filmMesh, membraneShader, PlayerRole.Player1,
                                new Vector3(-0.9f, 0f, 1.6f), 160f, k_Player1FilmMaterial);
            BuildMembraneFigure(fbx, filmMesh, membraneShader, PlayerRole.Player2,
                                new Vector3(0.9f, 0f, 1.6f), 200f, k_Player2FilmMaterial);

            BuildCombinePair(litShader, ghostShader);

            Directory.CreateDirectory("Assets/_Scenes");
            EditorSceneManager.MarkAllScenesDirty();

            // Saved once so every object has a persistent GlobalObjectId, then hashed, then
            // saved again -- an in-scene NetworkObject whose hash stays 0 can never synchronize,
            // so the halves would exist on the host and nowhere else.
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            RegenerateNetworkObjectHashes();
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);

            EnsureInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Combine] Saved scene to {ScenePath}");

            if (!Verify()) return;

            Debug.Log("[Combine] RESULT: PASS");
        }

        static bool CheckShader(string name, out Shader shader)
        {
            shader = Shader.Find(name);
            if (shader == null)
            {
                Debug.LogError($"[Combine] RESULT: FAIL - shader '{name}' not found.");
                return false;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[Combine] {name} {m.severity} line {m.line}: {m.message}");
                Debug.LogError($"[Combine] RESULT: FAIL - shader '{name}' has compile errors.");
                return false;
            }
            Debug.Log($"[Combine] Shader '{name}' compiled clean.");
            return true;
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
            Debug.Log($"[Combine] Added {label}.");
            return instance;
        }

        #region Environment and session

        static void BuildEnvironment(Shader litShader, out Transform p1Spawn, out Transform p2Spawn)
        {
            var root = new GameObject("Environment");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(0.8f, 1f, 0.8f); // 8m x 8m
            floor.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(litShader, "Floor", new Color(0.17f, 0.17f, 0.19f), 0.08f);

            var lightGO = new GameObject("Key Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(0.96f, 0.97f, 1f);
            light.shadows = LightShadows.Soft;
            lightGO.transform.rotation = Quaternion.Euler(42f, 31f, 0f);

            // The membrane is mostly specular and translucent, so it reads as nothing at all in
            // flat ambient light: a second, cooler light from behind gives the film an edge and
            // makes the frost-versus-contact difference visible.
            var rimGO = new GameObject("Rim Light");
            var rim = rimGO.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.75f;
            rim.color = new Color(0.62f, 0.74f, 0.95f);
            rim.shadows = LightShadows.None;
            rimGO.transform.rotation = Quaternion.Euler(14f, 212f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.26f, 0.29f, 0.35f);
            RenderSettings.ambientEquatorColor = new Color(0.19f, 0.20f, 0.23f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.09f, 0.10f);
            RenderSettings.fog = false;

            var spawns = new GameObject("Spawns");
            spawns.transform.SetParent(root.transform, false);

            p1Spawn = new GameObject("Player1_Spawn").transform;
            p1Spawn.SetParent(spawns.transform, false);
            p1Spawn.SetPositionAndRotation(new Vector3(-0.7f, 0f, -1.1f), Quaternion.Euler(0f, 18f, 0f));

            p2Spawn = new GameObject("Player2_Spawn").transform;
            p2Spawn.SetParent(spawns.transform, false);
            p2Spawn.SetPositionAndRotation(new Vector3(0.7f, 0f, -1.1f), Quaternion.Euler(0f, -18f, 0f));
        }

        static Material MakeMaterial(Shader shader, string name, Color color, float smoothness)
        {
            var mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        static void BuildSessionManagers()
        {
            var go = new GameObject("GameSessionManager");
            go.AddComponent<NetworkObject>();
            var session = go.AddComponent<GameSessionManager>();
            go.AddComponent<StoryProgressManager>();

            // This scene is the whole test, so it does not hand off to an experience scene.
            var so = new SerializedObject(session);
            so.FindProperty("m_ExperienceSceneName").stringValue = string.Empty;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildStaffAndCalibration(Transform p1Spawn, Transform p2Spawn)
        {
            var staff = new GameObject("StaffControlPanel");
            staff.AddComponent<StaffControlPanel>();
            staff.AddComponent<DesktopXrFallback>();

            var positioner = staff.AddComponent<RoleSpawnPositioner>();
            var so = new SerializedObject(positioner);
            so.FindProperty("m_Player1Spawn").objectReferenceValue = p1Spawn;
            so.FindProperty("m_Player2Spawn").objectReferenceValue = p2Spawn;
            // Desktop only, so in a headset the rig stays where tracking and calibration put it.
            so.FindProperty("m_DesktopOnly").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            var markerGO = new GameObject("CalibrationMarker");
            markerGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var marker = markerGO.AddComponent<CalibrationPoint>();

            var pinch = markerGO.AddComponent<HandPinchCalibrator>();
            var pinchSo = new SerializedObject(pinch);
            pinchSo.FindProperty("m_Marker").objectReferenceValue = marker;
            pinchSo.ApplyModifiedPropertiesWithoutUndo();

            // Off for now: the experience is hand-tracked throughout, and a pinch is also how a
            // half is picked up, so the gesture that confirms the origin is the gesture that
            // grabs. Height comes from the headset's own floor level either way. Left wired up
            // rather than removed -- re-enabling the component is the whole of putting it back.
            pinch.enabled = false;
        }

        #endregion

        #region Membrane figures

        static Mesh BuildFilmMesh(GameObject fbx)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            try
            {
                var bodySmr = probe.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (bodySmr == null || bodySmr.sharedMesh == null)
                {
                    Debug.LogError("[Combine] RESULT: FAIL - no SkinnedMeshRenderer on the avatar FBX.");
                    return null;
                }

                var mesh = MembraneShellBuilder.Build(bodySmr.sharedMesh,
                                                      k_BaseOffset, k_MinOffset,
                                                      k_SmoothIterations, k_SmoothLambda);
                mesh.name = "Ch36_CombineTestFilm";

                if (AssetDatabase.LoadAssetAtPath<Mesh>(k_FilmMeshPath) != null)
                    AssetDatabase.DeleteAsset(k_FilmMeshPath);
                Directory.CreateDirectory("Assets/_models");
                AssetDatabase.CreateAsset(mesh, k_FilmMeshPath);
                AssetDatabase.SaveAssets();

                Debug.Log($"[Combine] Film mesh: {mesh.vertexCount} verts (shared by both figures).");
                return mesh;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// One membrane-wrapped figure, tinted for a role. These stand in for the players rather
        /// than being the networked avatars: the film is relaxed against this specific body mesh,
        /// so it only fits this character, and the template's own player prefab is a different
        /// one. They are here so both role tints can be judged at real scale, from across a room,
        /// in a headset -- which is the thing a flat preview render cannot answer.
        /// </summary>
        static void BuildMembraneFigure(GameObject fbx, Mesh filmMesh, Shader membraneShader,
                                        PlayerRole role, Vector3 position, float yaw, string materialPath)
        {
            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            avatar.name = $"MembraneFigure_{role}";
            avatar.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            PrefabUtility.UnpackPrefabInstance(avatar, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var bodySmr = avatar.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodySmr == null) return;

            // The body underneath stays, dark and matte: the film's whole read is the difference
            // between where it touches skin and where it spans air, and with nothing beneath it
            // there is no skin for it to be touching.
            var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"Body_{role}" };
            bodyMat.SetColor("_BaseColor", new Color(0.085f, 0.082f, 0.088f));
            bodyMat.SetFloat("_Smoothness", 0.14f);
            bodySmr.sharedMaterial = bodyMat;

            var filmGO = new GameObject("MembraneFilm");
            filmGO.transform.SetParent(avatar.transform, false);

            var filmSmr = filmGO.AddComponent<SkinnedMeshRenderer>();
            filmSmr.sharedMesh = filmMesh;
            filmSmr.bones = bodySmr.bones;
            filmSmr.rootBone = bodySmr.rootBone;
            filmSmr.sharedMaterial = LoadOrCreateFilmMaterial(materialPath, membraneShader, role);

            // A skinned membrane whose bounds are not recomputed vanishes when the bind-pose
            // bounds leave the frustum, which in a headset reads as the body blinking out as you
            // turn your head.
            filmSmr.updateWhenOffscreen = true;

            Debug.Log($"[Combine] Membrane figure for {role} at {position}.");
        }

        static Material LoadOrCreateFilmMaterial(string path, Shader shader, PlayerRole role)
        {
            var mat = LoadOrCreate(path, shader, $"MembraneFilm_{role}");

            Color roleColor = PlayerRoleColors.For(role);

            // Tint where the film is against skin, and keep the air-spanning frost nearly white
            // but pulled slightly toward the role colour. Tinting both equally would flatten the
            // contact read, which is the only thing that makes the sheet legible as a sheet.
            mat.SetColor("_FilmColor", Color.Lerp(new Color(0.72f, 0.78f, 0.86f), roleColor, 0.75f));
            mat.SetColor("_FrostColor", Color.Lerp(new Color(0.88f, 0.91f, 0.96f), roleColor, 0.22f));
            mat.SetFloat("_ClearAlpha", 0.12f);
            mat.SetFloat("_FrostAlpha", 0.66f);
            mat.SetFloat("_ShowContact", 0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        #endregion

        #region Combine pair

        /// <summary>
        /// Two halves of one box, one per role. Each gets the template's networked physics stack
        /// so that grabbing transfers ownership and the holder's pose replicates; the server
        /// needs that to measure the gap between the halves at all.
        /// </summary>
        static void BuildCombinePair(Shader litShader, Shader ghostShader)
        {
            var solid = LoadOrCreate(k_HalfMaterialPath, litShader, "CombineHalfSolid");
            solid.SetColor("_BaseColor", new Color(0.56f, 0.47f, 0.37f));
            solid.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(solid);

            var ghost = LoadOrCreateGhostMaterial(ghostShader);

            // Set apart and at a comfortable height, each in front of its own player's spawn, so
            // neither player has to be told which half is theirs.
            var left = BuildHalf("CombineHalf_P1", new Vector3(-0.55f, 0.95f, 0.35f), solid);
            var right = BuildHalf("CombineHalf_P2", new Vector3(0.55f, 0.95f, 0.35f), solid);

            var socket = new GameObject("AssemblySocket").transform;
            socket.SetParent(left.transform, false);
            socket.localPosition = new Vector3(k_HalfWidth * 2f, 0f, 0f);

            Configure(left, PlayerRole.Player1, true, socket, ghost);
            Configure(right, PlayerRole.Player2, false, null, ghost);

            Debug.Log("[Combine] Built one assembly pair (P1 anchor, P2 follower).");
        }

        static GameObject BuildHalf(string name, Vector3 position, Material solid)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = new Vector3(k_HalfWidth * 2f, 0.22f, 0.22f);
            go.GetComponent<MeshRenderer>().sharedMaterial = solid;
            return go;
        }

        static void Configure(GameObject go, PlayerRole role, bool isAnchor, Transform socket, Material ghostMat)
        {
            // Order matters: every one of these has RequireComponent dependencies on the ones
            // before it, and letting Unity auto-add them produces components with default
            // settings that are then hard to tell apart from ones that were authored.
            var netObj = go.AddComponent<NetworkObject>();

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1.2f;
            rb.useGravity = false;          // a half that falls off the table before anyone grabs it
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var clientTransform = go.AddComponent<ClientNetworkTransform>();

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;   // grabbed where the hand actually is, not snapped to the pivot
            grab.throwOnDetach = false;     // these are meant to be placed, not thrown

            go.AddComponent<NetworkPhysicsInteractable>();

            // RoleGhost before CompositeHalf: it caches the solid materials the first time it is
            // asked to swap and has to see the real ones rather than the ghost.
            var roleGhost = go.AddComponent<RoleGhost>();
            var ghostSo = new SerializedObject(roleGhost);
            ghostSo.FindProperty("m_GhostMaterial").objectReferenceValue = ghostMat;
            var solidFor = ghostSo.FindProperty("m_SolidForRoles");
            solidFor.arraySize = 1;
            solidFor.GetArrayElementAtIndex(0).intValue = (int)role;
            ghostSo.ApplyModifiedPropertiesWithoutUndo();

            var half = go.AddComponent<CompositeHalf>();
            var so = new SerializedObject(half);
            so.FindProperty("m_PairId").stringValue = k_PairId;
            so.FindProperty("m_OwnedBy").intValue = (int)role;
            so.FindProperty("m_IsAnchor").boolValue = isAnchor;
            so.FindProperty("m_SnapRadius").floatValue = 0.22f;
            so.FindProperty("m_Ghost").objectReferenceValue = roleGhost;
            so.FindProperty("m_Interactable").objectReferenceValue = grab;
            so.FindProperty("m_Rigidbody").objectReferenceValue = rb;
            if (socket != null) so.FindProperty("m_AssemblySocket").objectReferenceValue = socket;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[Combine] {go.name}: owned by {role}, anchor={isAnchor}, " +
                      $"netObj={netObj != null}, clientTransform={clientTransform != null}");
        }

        static Material LoadOrCreateGhostMaterial(Shader shader)
        {
            var mat = LoadOrCreate(k_GhostMaterialPath, shader, "GhostHalf");
            mat.SetColor("_GhostColor", new Color(0.42f, 0.72f, 1f));
            mat.SetFloat("_FillAlpha", 0.05f);
            mat.SetFloat("_RimPower", 4f);
            mat.SetFloat("_RimIntensity", 1.5f);
            mat.SetFloat("_EdgeAlpha", 0.9f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        #endregion

        static Material LoadOrCreate(string path, Shader shader, string name)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        static void EnsureInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var entry in scenes)
            {
                if (entry.path != ScenePath) continue;
                Debug.Log("[Combine] Already registered in build settings.");
                return;
            }
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Combine] Added to build settings.");
        }

        static void RegenerateNetworkObjectHashes()
        {
            var onValidate = typeof(NetworkObject).GetMethod(
                "OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);

            if (onValidate == null)
            {
                Debug.LogError("[Combine] Could not find NetworkObject.OnValidate; hashes may stay 0.");
                return;
            }

            foreach (var netObj in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                onValidate.Invoke(netObj, null);
                EditorUtility.SetDirty(netObj);
            }
        }

        static uint ReadGlobalObjectIdHash(NetworkObject netObj) =>
            new SerializedObject(netObj).FindProperty("GlobalObjectIdHash")?.uintValue ?? 0u;

        static bool Verify()
        {
            var required = new (string name, bool present)[]
            {
                ("NetworkManager", Object.FindFirstObjectByType<NetworkManager>() != null),
                ("XRINetworkGameManager", Object.FindFirstObjectByType<XRINetworkGameManager>() != null),
                ("PlayerHudNotification", Object.FindFirstObjectByType<PlayerHudNotification>() != null),
                ("XROrigin", Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>() != null),
                ("GameSessionManager", Object.FindFirstObjectByType<GameSessionManager>() != null),
                ("StoryProgressManager", Object.FindFirstObjectByType<StoryProgressManager>() != null),
                ("StaffControlPanel", Object.FindFirstObjectByType<StaffControlPanel>() != null),
                ("CalibrationPoint", Object.FindFirstObjectByType<CalibrationPoint>() != null),
                ("HandPinchCalibrator", Object.FindFirstObjectByType<HandPinchCalibrator>() != null),
            };

            bool ok = true;
            foreach (var (name, present) in required)
            {
                Debug.Log($"[Combine] {(present ? "OK  " : "MISS")} {name}");
                if (!present) ok = false;
            }
            if (!ok)
            {
                Debug.LogError("[Combine] RESULT: FAIL - a required scene object is missing.");
                return false;
            }

            // Both membrane figures, with a film that actually has geometry.
            var films = new List<SkinnedMeshRenderer>();
            foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (smr.name == "MembraneFilm") films.Add(smr);
            }

            if (films.Count != 2)
            {
                Debug.LogError($"[Combine] RESULT: FAIL - expected 2 membrane films, found {films.Count}.");
                return false;
            }

            foreach (var film in films)
            {
                if (film.sharedMesh == null || film.sharedMesh.vertexCount == 0)
                {
                    Debug.LogError("[Combine] RESULT: FAIL - a membrane film has no mesh.");
                    return false;
                }
                if (film.bones == null || film.bones.Length == 0 || film.rootBone == null)
                {
                    Debug.LogError("[Combine] RESULT: FAIL - a membrane film is not bound to the body's bones, " +
                                   "so it would not follow the figure.");
                    return false;
                }
                if (!film.updateWhenOffscreen)
                {
                    Debug.LogError("[Combine] RESULT: FAIL - a membrane film would cull on its bind-pose bounds.");
                    return false;
                }
            }
            Debug.Log($"[Combine] OK   2 membrane films ({films[0].sharedMesh.vertexCount} verts each)");

            // The assembly pair, with everything the server needs to decide the weld.
            var halves = Object.FindObjectsByType<CompositeHalf>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (halves.Length != 2)
            {
                Debug.LogError($"[Combine] RESULT: FAIL - expected 2 halves, found {halves.Length}.");
                return false;
            }

            bool sawAnchor = false;
            var roles = new HashSet<PlayerRole>();
            foreach (var half in halves)
            {
                if (half.isAnchor) sawAnchor = true;
                roles.Add(half.ownedBy);

                if (half.GetComponent<ClientNetworkTransform>() == null)
                {
                    Debug.LogError($"[Combine] RESULT: FAIL - {half.name} has no ClientNetworkTransform, so the " +
                                   "server would never see it move and the pair could never assemble.");
                    return false;
                }
                if (half.GetComponent<XRGrabInteractable>() == null)
                {
                    Debug.LogError($"[Combine] RESULT: FAIL - {half.name} is not grabbable.");
                    return false;
                }
                if (half.GetComponent<NetworkPhysicsInteractable>() == null)
                {
                    Debug.LogError($"[Combine] RESULT: FAIL - {half.name} has no NetworkPhysicsInteractable, so " +
                                   "grabbing it would not transfer ownership and its pose would not replicate.");
                    return false;
                }
                if (half.GetComponent<RoleGhost>() == null)
                {
                    Debug.LogError($"[Combine] RESULT: FAIL - {half.name} has no RoleGhost, so both players would " +
                                   "see it as theirs.");
                    return false;
                }
            }

            if (!sawAnchor)
            {
                Debug.LogError("[Combine] RESULT: FAIL - neither half is the anchor, so nothing runs the snap check.");
                return false;
            }
            if (roles.Count != 2)
            {
                Debug.LogError("[Combine] RESULT: FAIL - both halves are owned by the same role.");
                return false;
            }
            Debug.Log("[Combine] OK   assembly pair: one anchor, one half per role, fully networked");

            foreach (var netObj in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                uint hash = ReadGlobalObjectIdHash(netObj);
                if (hash == 0)
                {
                    Debug.LogError($"[Combine] RESULT: FAIL - NetworkObject '{netObj.name}' has hash 0 and can " +
                                   "never synchronize to a client.");
                    return false;
                }
            }
            Debug.Log("[Combine] OK   every in-scene NetworkObject has a non-zero hash");

            return true;
        }
    }
}
