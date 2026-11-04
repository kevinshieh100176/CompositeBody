using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using XRMultiplayer;
using CompositeBody.Diagnostics;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;
using CompositeBody.Multiplayer.EditorSetup;
using Object = UnityEngine.Object;

namespace CompositeBody.Interaction.EditorTools
{
    /// <summary>
    /// What every interaction-module builder needs, in one place so Home and ObjectAssembly build
    /// their fragments, materials and demo scenes the same way. Builders call these and then
    /// <see cref="Check"/> their own result; nothing here opens or saves a scene by itself except
    /// where the method says so.
    /// </summary>
    public static class InteractionBuildUtil
    {
        public const string RevealShaderName = "CompositeBody/InteractionReveal";
        public const string BootstrapRootName = "--- Demo Bootstrap ---";

        // ---- preconditions -------------------------------------------------------------------

        /// <summary>
        /// The active scene must be <paramref name="scenePath"/> and have no unsaved changes.
        /// NEVER saves for the user and never replaces their open scene: a builder that did would
        /// silently throw away somebody's work in a shared editor.
        /// </summary>
        public static bool RequireCleanActiveScene(string scenePath, string tag, out Scene scene)
        {
            scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath)
            {
                Debug.LogError($"[{tag}] RESULT: FAIL - open '{scenePath}' first (active scene is " +
                               $"'{(string.IsNullOrEmpty(scene.path) ? "(unsaved)" : scene.path)}').");
                return false;
            }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isDirty)
                {
                    Debug.LogError($"[{tag}] RESULT: FAIL - '{s.path}' has unsaved changes. Save it first; " +
                                   "this builder never saves somebody else's work for them.");
                    return false;
                }
            }
            return true;
        }

        /// <summary>True if any loaded scene has unsaved changes (logs which).</summary>
        public static bool AnySceneDirty(string tag)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isDirty) continue;
                Debug.LogError($"[{tag}] '{s.path}' has unsaved changes. Save it first.");
                return true;
            }
            return false;
        }

        // ---- checks --------------------------------------------------------------------------

        /// <summary>One PASS/FAIL line; returns ok so callers can count.</summary>
        public static bool Check(string tag, bool ok, string what, List<string> failures)
        {
            if (ok) Debug.Log($"[{tag}] OK   {what}");
            else
            {
                Debug.LogError($"[{tag}] FAIL {what}");
                failures?.Add(what);
            }
            return ok;
        }

        public static void Verdict(string tag, List<string> failures)
        {
            if (failures.Count == 0) Debug.Log($"[{tag}] RESULT: PASS");
            else Debug.LogError($"[{tag}] RESULT: FAIL ({failures.Count}): {string.Join("; ", failures)}");
        }

        // ---- grabbable fragments -------------------------------------------------------------

        /// <summary>
        /// Puts the shared grab stack on <paramref name="root"/>. The order matters because of
        /// RequireComponent: collider, NetworkObject, Rigidbody, ClientNetworkTransform,
        /// XRGrabInteractable -- then the caller adds its NetGrabbable subclass.
        ///
        /// Every flag below is there because the default breaks something; see
        /// <see cref="NetGrabbable"/> and the module plan for the reasons.
        /// </summary>
        public static XRGrabInteractable ConfigureGrabbable(GameObject root, Mesh colliderMesh)
        {
            var collider = GetOrAdd<MeshCollider>(root);
            collider.sharedMesh = colliderMesh;
            collider.convex = true;

            var netObj = GetOrAdd<NetworkObject>(root);
            SetNetworkObjectFlags(netObj);

            var body = GetOrAdd<Rigidbody>(root);
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;

            var cnt = GetOrAdd<ClientNetworkTransform>(root);
            cnt.SyncScaleX = cnt.SyncScaleY = cnt.SyncScaleZ = false;
            cnt.InLocalSpace = false;

            var grab = GetOrAdd<XRGrabInteractable>(root);
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.selectMode = InteractableSelectMode.Single;
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true;
            grab.matchAttachRotation = true;
            grab.snapToColliderVolume = false;
            grab.attachEaseInTime = 0f;
            grab.throwOnDetach = false;
            grab.forceGravityOnDetach = false;
            grab.retainTransformParent = true;
            // XRI 3.5 un-parents on grab by default; NGO then logs or really re-parents a
            // NetworkObject under a non-network parent.
            grab.unparentTransformOnGrab = false;
            // XRGrabInteractable.Reset() adopts the single renderer child as "predicted visuals"
            // and then overwrites its local pose while held -- which would cancel BreathingView.
            grab.predictedVisualsTransform = null;

            EditorUtility.SetDirty(root);
            return grab;
        }

        // Not `GetComponent<T>() ?? AddComponent<T>()`: in the editor a missing component comes
        // back as a "fake null" object that == null but is not a null reference, so ?? keeps it.
        public static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        public static void SetNetworkObjectFlags(NetworkObject netObj)
        {
            var so = new SerializedObject(netObj);
            SetBool(so, "DontDestroyWithOwner", true);
            SetBool(so, "AutoObjectParentSync", false);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetBool(SerializedObject so, string prop, bool value)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning($"[InteractionBuild] NetworkObject has no serialized '{prop}'.");
        }

        /// <summary>Reads a NetworkObject flag the builder set, for Verify.</summary>
        public static bool ReadNetworkObjectBool(NetworkObject netObj, string prop)
        {
            SerializedProperty p = new SerializedObject(netObj).FindProperty(prop);
            return p != null && p.boolValue;
        }

        /// <summary>The checks every builder runs on every fragment it made.</summary>
        public static void VerifyGrabbable(string tag, GameObject root, List<string> failures)
        {
            string n = root.name;
            var netObj = root.GetComponent<NetworkObject>();
            var grab = root.GetComponent<XRGrabInteractable>();
            var body = root.GetComponent<Rigidbody>();
            var cnt = root.GetComponent<ClientNetworkTransform>();
            var col = root.GetComponent<MeshCollider>();

            Check(tag, netObj != null && ReadNetworkObjectBool(netObj, "DontDestroyWithOwner"),
                  $"{n}: NetworkObject DontDestroyWithOwner", failures);
            Check(tag, netObj != null && !ReadNetworkObjectBool(netObj, "AutoObjectParentSync"),
                  $"{n}: NetworkObject AutoObjectParentSync off", failures);
            Check(tag, body != null && body.isKinematic && !body.useGravity, $"{n}: kinematic, no gravity", failures);
            Check(tag, cnt != null && !cnt.SyncScaleX && !cnt.InLocalSpace, $"{n}: ClientNetworkTransform pos/rot world", failures);
            Check(tag, col != null && col.convex && col.sharedMesh != null, $"{n}: convex MeshCollider", failures);
            Check(tag, grab != null && grab.movementType == XRBaseInteractable.MovementType.Instantaneous &&
                       grab.selectMode == InteractableSelectMode.Single && !grab.throwOnDetach &&
                       !grab.forceGravityOnDetach && !grab.unparentTransformOnGrab &&
                       grab.predictedVisualsTransform == null,
                  $"{n}: XRGrabInteractable flags", failures);
            Check(tag, root.GetComponent<NetGrabbable>() != null, $"{n}: has a NetGrabbable", failures);
        }

        // ---- network hashes ------------------------------------------------------------------

        /// <summary>
        /// Invokes NetworkObject's internal OnValidate, which assigns GlobalObjectIdHash from the
        /// object's persistent id. Unity only does this when a person edits the object in the
        /// inspector; script-built objects keep 0 and never synchronize. Call AFTER the scene has
        /// been saved once (new objects have no persistent id before that).
        /// </summary>
        public static void RegenerateNetworkObjectHashes(Scene scene)
        {
            MethodInfo onValidate = typeof(NetworkObject).GetMethod(
                "OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);
            if (onValidate == null)
            {
                Debug.LogError("[InteractionBuild] Could not find NetworkObject.OnValidate; hashes may stay 0.");
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (NetworkObject netObj in root.GetComponentsInChildren<NetworkObject>(true))
                {
                    onValidate.Invoke(netObj, null);
                    EditorUtility.SetDirty(netObj);
                }
            }
        }

        public static uint HashOf(NetworkObject netObj) =>
            new SerializedObject(netObj).FindProperty("GlobalObjectIdHash")?.uintValue ?? 0u;

        /// <summary>Every NetworkObject in <paramref name="scene"/> has a non-zero, unique hash.</summary>
        public static void VerifyHashes(string tag, Scene scene, List<string> failures)
        {
            var seen = new Dictionary<uint, string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (NetworkObject netObj in root.GetComponentsInChildren<NetworkObject>(true))
                {
                    uint h = HashOf(netObj);
                    Check(tag, h != 0, $"{netObj.name}: NetworkObject hash non-zero", failures);
                    if (h == 0) continue;
                    if (seen.TryGetValue(h, out string other))
                        Check(tag, false, $"{netObj.name}: hash {h} duplicates '{other}'", failures);
                    else seen[h] = netObj.name;
                }
            }
        }

        // ---- materials -----------------------------------------------------------------------

        public static Shader RevealShader()
        {
            Shader s = Shader.Find(RevealShaderName);
            if (s == null) Debug.LogError($"[InteractionBuild] Shader '{RevealShaderName}' not found.");
            return s;
        }

        public static bool RevealShaderOk(out string problem)
        {
            Shader s = Shader.Find(RevealShaderName);
            if (s == null) { problem = "not found"; return false; }
            if (ShaderUtil.ShaderHasError(s)) { problem = "has compile errors"; return false; }
            problem = null;
            return true;
        }

        /// <summary>
        /// The "Lit + reveal" twin of <paramref name="source"/>, saved as an asset in
        /// <paramref name="folder"/> (re-used and refreshed if it exists). An ASSET, never a runtime
        /// material: nothing would reference a runtime one, and a player build strips the shader
        /// variants it needs.
        ///
        /// Copies every property the two shaders share (they share all of URP Lit's), keeps the
        /// source's keywords (normal map, emission, ...), and then forces what the reveal needs:
        /// _ALPHATEST_ON + _AlphaClip (so shadows and depth clip with it), alpha-test queue, and
        /// _SourceAlphaClip set from whether the source was itself a cutout.
        /// </summary>
        public static Material MakeRevealTwin(Material source, string folder, float revealDefault = 1f)
        {
            Shader reveal = RevealShader();
            if (source == null || reveal == null) return null;

            Directory.CreateDirectory(folder);
            string path = $"{folder}/{Sanitize(source.name)}_Reveal.mat";
            var twin = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = twin == null;
            if (created) twin = new Material(reveal);

            bool sourceClipped = source.IsKeywordEnabled("_ALPHATEST_ON") ||
                                 (source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0.5f);

            twin.shader = reveal;
            twin.CopyMatchingPropertiesFromMaterial(source);
            twin.shaderKeywords = source.shaderKeywords;
            twin.name = Path.GetFileNameWithoutExtension(path);

            twin.EnableKeyword("_ALPHATEST_ON");
            twin.SetFloat("_AlphaClip", 1f);
            twin.SetFloat("_SourceAlphaClip", sourceClipped ? 1f : 0f);
            if (!sourceClipped) twin.SetFloat("_Cutoff", 0.5f);
            twin.SetFloat("_Reveal", revealDefault);
            twin.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            twin.enableInstancing = source.enableInstancing;
            twin.globalIlluminationFlags = source.globalIlluminationFlags;

            if (created) AssetDatabase.CreateAsset(twin, path);
            else EditorUtility.SetDirty(twin);
            return twin;
        }

        /// <summary>
        /// A grabbable fragment's own material: a reveal twin of <paramref name="source"/> with
        /// emission switched on (black, so it only glows when BreathingView pulses it), saved at
        /// <paramref name="path"/>.
        /// </summary>
        public static Material MakeFragmentMaterial(Material source, string path)
        {
            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            Shader reveal = RevealShader();
            if (source == null || reveal == null || string.IsNullOrEmpty(folder)) return null;
            Directory.CreateDirectory(folder);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created) mat = new Material(reveal);

            mat.shader = reveal;
            mat.CopyMatchingPropertiesFromMaterial(source);
            mat.shaderKeywords = source.shaderKeywords;
            mat.name = Path.GetFileNameWithoutExtension(path);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_SourceAlphaClip", 0f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_Reveal", 1f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

            if (created) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        public static bool IsRevealMaterialOk(Material m) =>
            m != null && m.shader != null && m.shader.name == RevealShaderName && m.IsKeywordEnabled("_ALPHATEST_ON");

        static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        // ---- static flags --------------------------------------------------------------------

        /// <summary>
        /// Clears BatchingStatic (a statically batched renderer cannot visibly move) and keeps
        /// everything else, ContributeGI included, so the bake stays valid. Returns how many changed.
        /// </summary>
        public static int ClearBatchingStatic(GameObject root)
        {
            int changed = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                if ((flags & StaticEditorFlags.BatchingStatic) == 0) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                changed++;
            }
            return changed;
        }

        public static bool AnyBatchingStatic(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if ((GameObjectUtility.GetStaticEditorFlags(t.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                    return true;
            return false;
        }

        // ---- demo bootstrap ------------------------------------------------------------------

        /// <summary>
        /// Everything a standalone two-player demo scene needs to host, join and grab, mirroring
        /// Assets/Editor/MultiplayerSetup/BuildBaseScene.cs (the project's own base scene). The
        /// Network Manager VR Multiplayer and XRI Network Game Manager prefabs go at the scene
        /// root (NGO refuses a nested NetworkManager); everything else goes under one root so a
        /// rebuild replaces it cleanly:
        ///   (no Player_Notification_UI -- the XRMPT rig already carries the PlayerHudNotification
        ///   XRINetworkGameManager needs on connect), GameSessionManager
        ///   (NetworkObject + StoryProgressManager + InteractionDebugRelay -- it installs the
        ///   connection-approval callback, without which a client hangs), StaffControlPanel (+ LAN
        ///   discovery, DesktopXrFallback, RoleSpawnPositioner), InteractionDebugPanel/Roster,
        ///   a Playspace carrying the XRMPT rig, a CalibrationPoint, and a DesktopGrabProbe.
        /// The scene's own Main Camera is deactivated so exactly one AudioListener is live.
        ///
        /// Returns null (and logs) if a template prefab is missing.
        /// </summary>
        public static GameObject BuildDemoBootstrap(Scene scene, Pose player1Spawn, Pose player2Spawn, string tag)
        {
            // The managers live at the SCENE ROOT, not under the bootstrap root: NGO refuses to
            // start a NetworkManager that is nested ("NetworkManager cannot be nested"), and the
            // template's game manager is a singleton written the same way. So a rebuild clears
            // both the bootstrap root and any root-level instances of those two prefabs.
            string[] rootPrefabGuids = { BuildBaseScene.NetworkManagerGuid, BuildBaseScene.GameManagerGuid };
            var rootPrefabs = new List<GameObject>();
            foreach (string guid in rootPrefabGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var asset = string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (asset != null) rootPrefabs.Add(asset);
            }

            foreach (GameObject old in scene.GetRootGameObjects())
            {
                if (old.name == BootstrapRootName) { Object.DestroyImmediate(old); continue; }
                GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(old);
                if (source != null && rootPrefabs.Contains(source)) Object.DestroyImmediate(old);
            }

            var root = new GameObject(BootstrapRootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            var missing = new List<string>();
            GameObject networkManager = InstantiatePrefab(BuildBaseScene.NetworkManagerGuid, "Network Manager", null, missing);
            GameObject gameManager = InstantiatePrefab(BuildBaseScene.GameManagerGuid, "XRI Network Game Manager", null, missing);
            if (networkManager != null) SceneManager.MoveGameObjectToScene(networkManager, scene);
            if (gameManager != null) SceneManager.MoveGameObjectToScene(gameManager, scene);

            // No separate Player_Notification_UI: the XRMPT rig already carries a
            // PlayerHudNotification, and a second one logs "Instance is not null" on play.

            var playspace = new GameObject("Playspace");
            playspace.transform.SetParent(root.transform, false);
            playspace.AddComponent<Playspace>();
            GameObject rig = InstantiatePrefab(BuildBaseScene.XrOriginGuid, "XR Origin", playspace.transform, missing);

            if (missing.Count > 0)
            {
                Debug.LogError($"[{tag}] demo bootstrap: missing template prefabs: {string.Join(", ", missing)}");
                Object.DestroyImmediate(root);
                if (networkManager != null) Object.DestroyImmediate(networkManager);
                if (gameManager != null) Object.DestroyImmediate(gameManager);
                return null;
            }

            // Session: approval, roles, story gates, debug relay.
            var sessionGO = new GameObject("GameSessionManager");
            sessionGO.transform.SetParent(root.transform, false);
            NetworkObject sessionNet = sessionGO.AddComponent<NetworkObject>();
            var session = sessionGO.AddComponent<GameSessionManager>();
            sessionGO.AddComponent<StoryProgressManager>();
            sessionGO.AddComponent<InteractionDebugRelay>();
            var sso = new SerializedObject(session);
            SetString(sso, "m_ExperienceSceneName", ""); // Start flips the state; loads nothing
            SetBool(sso, "m_AllowSolo", true);            // one person can dial a module in
            sso.ApplyModifiedPropertiesWithoutUndo();

            // Spawns + staff panel.
            var spawns = new GameObject("Spawns");
            spawns.transform.SetParent(root.transform, false);
            Transform p1 = new GameObject("Player1_Spawn").transform;
            p1.SetParent(spawns.transform, false);
            p1.SetPositionAndRotation(player1Spawn.position, player1Spawn.rotation);
            Transform p2 = new GameObject("Player2_Spawn").transform;
            p2.SetParent(spawns.transform, false);
            p2.SetPositionAndRotation(player2Spawn.position, player2Spawn.rotation);

            var staff = new GameObject("StaffControlPanel");
            staff.transform.SetParent(root.transform, false);
            staff.AddComponent<StaffControlPanel>(); // pulls in LAN broadcaster + discovery
            staff.AddComponent<DesktopXrFallback>();
            var positioner = staff.AddComponent<RoleSpawnPositioner>();
            var pso = new SerializedObject(positioner);
            pso.FindProperty("m_Player1Spawn").objectReferenceValue = p1;
            pso.FindProperty("m_Player2Spawn").objectReferenceValue = p2;
            pso.ApplyModifiedPropertiesWithoutUndo();

            // Debug panel on its own object: it builds its own overlay canvas, as does the staff
            // panel, and one GameObject may hold only one Canvas.
            var debugPanel = new GameObject("InteractionDebugPanel");
            debugPanel.transform.SetParent(root.transform, false);
            debugPanel.AddComponent<InteractionDebugPanel>();
            debugPanel.AddComponent<InteractionDebugRoster>();

            var calibration = new GameObject("CalibrationMarker");
            calibration.transform.SetParent(root.transform, false);
            calibration.AddComponent<CalibrationPoint>();

            var probe = new GameObject("DesktopGrabProbe");
            probe.transform.SetParent(root.transform, false);
            probe.AddComponent<CompositeBody.Interaction.Diagnostics.DesktopGrabProbe>();

            // One AudioListener, one MainCamera: the rig's.
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go == root) continue;
                foreach (Camera cam in go.GetComponentsInChildren<Camera>(true))
                {
                    if (!cam.gameObject.activeSelf) continue;
                    if (cam.GetComponent<AudioListener>() == null && !cam.CompareTag("MainCamera")) continue;
                    cam.gameObject.SetActive(false);
                    Debug.Log($"[{tag}] deactivated scene camera '{cam.name}' (the rig's camera is the view).");
                }
            }

            _ = rig; _ = sessionNet;
            EditorSceneManager.MarkSceneDirty(scene);
            return root;
        }

        static void SetString(SerializedObject so, string prop, string value)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p != null) p.stringValue = value;
            else Debug.LogWarning($"[InteractionBuild] '{so.targetObject.GetType().Name}' has no '{prop}'.");
        }

        static GameObject InstantiatePrefab(string guid, string label, Transform parent, List<string> missing)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { missing.Add(label); return null; }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            return instance;
        }

        /// <summary>The bootstrap's must-haves are present, and exactly one AudioListener is live.</summary>
        public static void VerifyDemoBootstrap(string tag, Scene scene, List<string> failures)
        {
            bool Has<T>() where T : Component
            {
                foreach (GameObject go in scene.GetRootGameObjects())
                    if (go.GetComponentInChildren<T>(true) != null) return true;
                return false;
            }

            int RootLevel<T>() where T : Component
            {
                int n = 0;
                foreach (GameObject go in scene.GetRootGameObjects())
                    if (go.GetComponent<T>() != null) n++;
                return n;
            }

            int CountAll<T>() where T : Component
            {
                int n = 0;
                foreach (GameObject go in scene.GetRootGameObjects())
                    n += go.GetComponentsInChildren<T>(true).Length;
                return n;
            }

            Check(tag, Has<NetworkManager>(), "bootstrap: NetworkManager", failures);
            Check(tag, CountAll<NetworkManager>() == 1 && RootLevel<NetworkManager>() == 1,
                  "bootstrap: exactly one NetworkManager, at the scene root (NGO refuses a nested one)", failures);
            Check(tag, Has<XRINetworkGameManager>(), "bootstrap: XRINetworkGameManager", failures);
            Check(tag, CountAll<PlayerHudNotification>() == 1,
                  $"bootstrap: exactly one PlayerHudNotification (found {CountAll<PlayerHudNotification>()})", failures);
            Check(tag, Has<GameSessionManager>(), "bootstrap: GameSessionManager", failures);
            Check(tag, Has<StaffControlPanel>(), "bootstrap: StaffControlPanel", failures);
            Check(tag, Has<Playspace>(), "bootstrap: Playspace", failures);

            int listeners = 0;
            foreach (GameObject go in scene.GetRootGameObjects())
                foreach (AudioListener l in go.GetComponentsInChildren<AudioListener>(false))
                    if (l.enabled && l.gameObject.activeInHierarchy) listeners++;
            Check(tag, listeners == 1, $"bootstrap: exactly one live AudioListener (found {listeners})", failures);
        }
    }
}
