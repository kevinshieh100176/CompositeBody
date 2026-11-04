using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Diagnostics;
using CompositeBody.Interaction.EditorTools;
using Object = UnityEngine.Object;

namespace CompositeBody.Interaction.Home.EditorTools
{
    /// <summary>
    /// Tools > Home > Build Home Interaction. Builds /HomeInteraction in the user's art scene
    /// (Assets/_Interaction/Home/S2_v1.unity) from what is authored there:
    ///
    ///   /Home/--- Environment ---/LowPolyWall/{5 pieces}   the assembled mini-house = the answer key
    ///   /Home/--- Environment ---/S3/Inner_Wall/{6 walls}  the big room, each wall with furniture
    ///
    /// and saves the module as Prefabs/HomeInteraction.prefab. Re-runnable: the module root and the
    /// demo bootstrap are rebuilt from scratch every time; the art is only read (and LowPolyWall
    /// switched off, S3's BatchingStatic cleared).
    ///
    /// NEVER SAVES SOMEBODY ELSE'S WORK: if the scene has unsaved changes it fails and says so.
    /// Ends with Verify(): PASS/FAIL lines and a RESULT line.
    /// </summary>
    public static class BuildHomeInteraction
    {
        public const string Tag = "HomeBuild";
        public const string ScenePath = "Assets/_Interaction/Home/S2_v1.unity";
        public const string HomeFolder = "Assets/_Interaction/Home";
        public const string PrefabPath = HomeFolder + "/Prefabs/HomeInteraction.prefab";
        public const string FragmentMaterialPath = HomeFolder + "/Materials/HomeFragment.mat";
        public const string RevealFolder = HomeFolder + "/Materials/Reveal";
        public const string ColliderFolder = HomeFolder + "/Meshes";
        public const string RootName = "HomeInteraction";

        const string k_EnvironmentPath = "Home/--- Environment ---";
        const string k_MiniHousePath = k_EnvironmentPath + "/LowPolyWall";
        const string k_RoomPath = k_EnvironmentPath + "/S3/Inner_Wall";
        const string k_S3Path = k_EnvironmentPath + "/S3";
        const string k_FinalGroupName = "Top";

        const float k_ZoneBMargin = 0.12f;
        static readonly Vector3 k_ZoneASize = new(2.4f, 1.0f, 2.4f);
        const float k_HandHeight = 1.2f;
        const float k_MinColliderThickness = 0.04f; // metres, world: thin paper is hard to touch
        const float k_SpawnDistance = 1.6f;

        // ---- entry ----------------------------------------------------------------------------

        [MenuItem("Tools/Home/Build Home Interaction")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - leave play mode first.");
                return;
            }
            if (!InteractionBuildUtil.RequireCleanActiveScene(ScenePath, Tag, out Scene scene)) return;
            if (!InteractionBuildUtil.RevealShaderOk(out string problem))
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - {InteractionBuildUtil.RevealShaderName} {problem}.");
                return;
            }

            try
            {
                BuildInto(scene);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[{Tag}] RESULT: FAIL - exception: {e.Message}");
            }
        }

        [MenuItem("Tools/Home/Verify Home Interaction")]
        public static void VerifyMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - open '{ScenePath}' first.");
                return;
            }
            Verify(scene);
        }

        // ---- the art --------------------------------------------------------------------------

        sealed class Piece
        {
            public string label;          // Floor, Back, ...
            public Transform art;         // the LowPolyWall child
            public Mesh mesh;
            public Material material;
            public Bounds world;          // mesh bounds in world space
        }

        sealed class Wall
        {
            public string name;           // Floor, Back, ... Top
            public Transform transform;
            public Bounds world;          // the wall's own mesh, no furniture
            public string axis;           // +X, -Z, ...
            public Vector3 direction;     // outward unit
        }

        static Transform FindPath(Scene scene, string path)
        {
            string[] parts = path.Split('/');
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;
                Transform t = root.transform;
                for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
                if (t != null) return t;
            }
            return null;
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            for (Transform c = t.parent; c != null; c = c.parent) p = c.name + "/" + p;
            return "/" + p;
        }

        /// <summary>World bounds of a mesh under a transform -- works on inactive objects too.</summary>
        static Bounds WorldBounds(Transform t, Mesh mesh)
        {
            Bounds b = mesh.bounds;
            Matrix4x4 m = t.localToWorldMatrix;
            var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                result.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return result;
        }

        static string LabelOfArt(string artName)
        {
            int dot = artName.LastIndexOf('.');
            return dot >= 0 ? artName.Substring(dot + 1) : artName;
        }

        static string NameOfWall(string wallName) =>
            wallName.EndsWith("_Wall", StringComparison.Ordinal) ? wallName.Substring(0, wallName.Length - 5) : wallName;

        /// <summary>The dominant axis of <paramref name="offset"/> normalised by <paramref name="half"/>.</summary>
        static string DominantAxis(Vector3 offset, Vector3 half, out Vector3 direction)
        {
            var n = new Vector3(
                offset.x / Mathf.Max(half.x, 1e-3f),
                offset.y / Mathf.Max(half.y, 1e-3f),
                offset.z / Mathf.Max(half.z, 1e-3f));
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) { direction = new Vector3(Mathf.Sign(n.x), 0, 0); return n.x >= 0 ? "+X" : "-X"; }
            if (ay >= az) { direction = new Vector3(0, Mathf.Sign(n.y), 0); return n.y >= 0 ? "+Y" : "-Y"; }
            direction = new Vector3(0, 0, Mathf.Sign(n.z));
            return n.z >= 0 ? "+Z" : "-Z";
        }

        static List<Piece> ReadPieces(Transform miniHouse)
        {
            var pieces = new List<Piece>();
            foreach (Transform child in miniHouse)
            {
                var mf = child.GetComponent<MeshFilter>();
                var mr = child.GetComponent<MeshRenderer>();
                if (mf == null || mf.sharedMesh == null || mr == null) continue;
                pieces.Add(new Piece
                {
                    label = LabelOfArt(child.name),
                    art = child,
                    mesh = mf.sharedMesh,
                    material = mr.sharedMaterial,
                    world = WorldBounds(child, mf.sharedMesh),
                });
            }
            return pieces;
        }

        static List<Wall> ReadWalls(Transform room, out Bounds roomBounds)
        {
            var walls = new List<Wall>();
            roomBounds = default;
            bool first = true;
            foreach (Transform child in room)
            {
                var mf = child.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var w = new Wall { name = NameOfWall(child.name), transform = child, world = WorldBounds(child, mf.sharedMesh) };
                walls.Add(w);
                if (first) { roomBounds = w.world; first = false; }
                else roomBounds.Encapsulate(w.world);
            }
            foreach (Wall w in walls)
                w.axis = DominantAxis(w.world.center - roomBounds.center, roomBounds.extents, out w.direction);
            return walls;
        }

        // ---- build ----------------------------------------------------------------------------

        static void BuildInto(Scene scene)
        {
            Transform miniHouse = FindPath(scene, k_MiniHousePath);
            Transform room = FindPath(scene, k_RoomPath);
            Transform s3 = FindPath(scene, k_S3Path);
            if (miniHouse == null || room == null || s3 == null)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - art missing: '{k_MiniHousePath}' " +
                               $"{(miniHouse != null ? "ok" : "MISSING")}, '{k_RoomPath}' {(room != null ? "ok" : "MISSING")}.");
                return;
            }

            List<Piece> pieces = ReadPieces(miniHouse);
            List<Wall> walls = ReadWalls(room, out Bounds roomBounds);
            if (pieces.Count != 5 || walls.Count != 6)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - expected 5 mini-house pieces and 6 walls, found " +
                               $"{pieces.Count} and {walls.Count}.");
                return;
            }

            // ---- materials ----------------------------------------------------------------
            Directory.CreateDirectory(RevealFolder);
            Directory.CreateDirectory(ColliderFolder);
            Directory.CreateDirectory(HomeFolder + "/Prefabs");

            Material fragmentMat = InteractionBuildUtil.MakeFragmentMaterial(pieces[0].material, FragmentMaterialPath);
            var twins = new Dictionary<Material, Material>();
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || twins.ContainsKey(m) || HomeRevealGroup.IsMelting(m)) continue;
                    twins[m] = InteractionBuildUtil.MakeRevealTwin(m, RevealFolder);
                }
            }
            // Only what this builder made. A global AssetDatabase.SaveAssets() in a shared editor
            // writes every other dirty asset too -- somebody else's unsaved prefab edit included.
            SaveOwn(fragmentMat);
            foreach (Material twin in twins.Values) SaveOwn(twin);
            s_MadeMeshes.Clear();
            Debug.Log($"[{Tag}] materials: {Path.GetFileName(FragmentMaterialPath)} + {twins.Count} reveal twins in {RevealFolder}.");

            // ---- mapping --------------------------------------------------------------------
            Bounds house = pieces[0].world;
            foreach (Piece p in pieces) house.Encapsulate(p.world);

            var pieceAxis = new Dictionary<Piece, string>();
            foreach (Piece p in pieces) pieceAxis[p] = DominantAxis(p.world.center - house.center, house.extents, out _);

            var mapping = new List<(Piece piece, Wall wall)>();
            foreach (Piece p in pieces)
            {
                Wall w = walls.FirstOrDefault(x => x.axis == pieceAxis[p]);
                mapping.Add((p, w));
            }
            Wall finalWall = walls.FirstOrDefault(w => mapping.All(m => m.wall != w));

            Debug.Log($"[{Tag}] mapping (fragment -> wall, by dominant axis; name check):");
            foreach ((Piece p, Wall w) in mapping)
            {
                bool nameOk = w != null && string.Equals(p.label, w.name, StringComparison.OrdinalIgnoreCase);
                string line = $"[{Tag}]   {p.label,-6} {pieceAxis[p]} -> {(w != null ? w.transform.name : "(none)"),-11} " +
                              $"{(w != null ? w.axis : "")} {(nameOk ? "name ok" : "NAME MISMATCH")}";
                if (nameOk) Debug.Log(line); else Debug.LogWarning(line);
            }
            Debug.Log($"[{Tag}]   (none)      -> {(finalWall != null ? finalWall.transform.name : "(none)")} " +
                      $"{(finalWall != null ? finalWall.axis : "")} last, after the other five");

            // ---- the module root ----------------------------------------------------------
            foreach (GameObject old in scene.GetRootGameObjects())
                if (old.name == RootName) Object.DestroyImmediate(old);

            var rootGO = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(rootGO, scene);
            var assembly = rootGO.AddComponent<HomeAssembly>();

            // Zones.
            Transform zones = Child(rootGO.transform, "Zones");
            var zoneB = Child(zones, "ZoneB").gameObject.AddComponent<HomeZone>();
            zoneB.transform.position = house.center;
            zoneB.size = house.size + Vector3.one * (2f * k_ZoneBMargin);
            var zoneA = Child(zones, "ZoneA").gameObject.AddComponent<HomeZone>();
            zoneA.transform.position = new Vector3(house.center.x, k_HandHeight, house.center.z);
            zoneA.size = k_ZoneASize;
            SetZoneColour(zoneA, new Color(0.3f, 0.8f, 1f, 0.8f));
            SetZoneColour(zoneB, new Color(1f, 0.75f, 0.2f, 0.9f));

            // Slots + fragments.
            Transform slots = Child(rootGO.transform, "Slots");
            Transform fragmentsParent = Child(rootGO.transform, "Fragments");
            Transform groupsParent = Child(rootGO.transform, "Groups");

            var pairs = new List<(HomeFragment fragment, HomeRevealGroup group)>();
            foreach ((Piece p, Wall w) in mapping)
            {
                Transform slot = Child(slots, p.label + "_Slot");
                p.art.GetPositionAndRotation(out Vector3 artPos, out Quaternion artRot);
                slot.SetPositionAndRotation(artPos, artRot);

                HomeFragment fragment = MakeFragment(fragmentsParent, p, slot, fragmentMat, assembly);
                HomeRevealGroup group = w != null ? MakeGroup(groupsParent, w, twins) : null;
                pairs.Add((fragment, group));
            }
            HomeRevealGroup final = finalWall != null ? MakeGroup(groupsParent, finalWall, twins) : null;

            var aso = new SerializedObject(assembly);
            SerializedProperty pairsProp = aso.FindProperty("m_Pairs");
            pairsProp.arraySize = pairs.Count;
            for (int i = 0; i < pairs.Count; i++)
            {
                SerializedProperty e = pairsProp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("fragment").objectReferenceValue = pairs[i].fragment;
                e.FindPropertyRelative("group").objectReferenceValue = pairs[i].group;
            }
            aso.FindProperty("m_FinalGroup").objectReferenceValue = final;
            aso.FindProperty("m_ZoneA").objectReferenceValue = zoneA;
            aso.FindProperty("m_ZoneB").objectReferenceValue = zoneB;
            aso.ApplyModifiedPropertiesWithoutUndo();

            // ---- the art: room movable, mini-house is the answer key ------------------------
            int cleared = InteractionBuildUtil.ClearBatchingStatic(s3.gameObject);
            Debug.Log($"[{Tag}] cleared BatchingStatic on {cleared} object(s) under S3 (ContributeGI kept).");
            miniHouse.gameObject.SetActive(false);

            // ---- demo bootstrap -----------------------------------------------------------
            Vector3 c = new(house.center.x, 0f, house.center.z);
            var p1 = new Pose(c + Vector3.back * k_SpawnDistance, Quaternion.LookRotation(Vector3.forward));
            var p2 = new Pose(c + Vector3.forward * k_SpawnDistance, Quaternion.LookRotation(Vector3.back));
            if (InteractionBuildUtil.BuildDemoBootstrap(scene, p1, p2, Tag) == null)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - demo bootstrap could not be built.");
                return;
            }

            // ---- save order: scene, prefab, scene overrides, save, hashes, save --------------
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - could not save the scene.");
                return;
            }

            GameObject instance = PrefabUtility.SaveAsPrefabAssetAndConnect(
                rootGO, PrefabPath, InteractionMode.AutomatedAction, out bool saved);
            if (!saved || instance == null)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - could not save {PrefabPath}.");
                return;
            }

            // Scene references a prefab asset cannot hold: the group targets, as overrides.
            foreach (HomeRevealGroup g in instance.GetComponentsInChildren<HomeRevealGroup>(true))
            {
                Transform target = FindPath(scene, g.targetPath.TrimStart('/'));
                var gso = new SerializedObject(g);
                gso.FindProperty("m_Target").objectReferenceValue = target;
                gso.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(g);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Hashes come from persistent ids, which exist only after a save. Scene-side only --
            // never applied to the prefab.
            InteractionBuildUtil.RegenerateNetworkObjectHashes(scene);
            // A value written straight into a prefab INSTANCE is only kept as an override if it is
            // recorded; otherwise the next load reverts it to the prefab's value.
            foreach (NetworkObject netObj in instance.GetComponentsInChildren<NetworkObject>(true))
                PrefabUtility.RecordPrefabInstancePropertyModifications(netObj);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            foreach (Mesh m in s_MadeMeshes) SaveOwn(m);

            Debug.Log($"[{Tag}] built /{RootName} ({pairs.Count} fragments, {pairs.Count + (final != null ? 1 : 0)} " +
                      $"groups), saved {PrefabPath} and {ScenePath}.");

            Verify(scene);
        }

        static readonly List<Mesh> s_MadeMeshes = new();

        static void SaveOwn(Object asset)
        {
            if (asset != null && AssetDatabase.Contains(asset)) AssetDatabase.SaveAssetIfDirty(asset);
        }

        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void SetZoneColour(HomeZone zone, Color c)
        {
            var so = new SerializedObject(zone);
            so.FindProperty("m_GizmoColor").colorValue = c;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static HomeFragment MakeFragment(Transform parent, Piece p, Transform slot, Material material, HomeAssembly assembly)
        {
            var root = new GameObject(p.label);
            root.transform.SetParent(parent, false);
            p.art.GetPositionAndRotation(out Vector3 pos, out Quaternion rot);
            root.transform.SetPositionAndRotation(pos, rot);
            root.transform.localScale = p.art.lossyScale;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = p.mesh;
            var mr = visual.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.lightProbeUsage = LightProbeUsage.BlendProbes;
            GameObjectUtility.SetStaticEditorFlags(visual, 0);
            var view = visual.AddComponent<HomeFragmentView>();

            Mesh colliderMesh = MakeColliderMesh(p, root.transform.lossyScale);
            XRGrabInteractable grab = InteractionBuildUtil.ConfigureGrabbable(root, colliderMesh);
            var fragment = root.AddComponent<HomeFragment>();

            var so = new SerializedObject(fragment);
            so.FindProperty("m_Interactable").objectReferenceValue = grab;
            so.FindProperty("m_NetworkTransform").objectReferenceValue = root.GetComponent<XRMultiplayer.ClientNetworkTransform>();
            so.FindProperty("m_Assembly").objectReferenceValue = assembly;
            so.FindProperty("m_Slot").objectReferenceValue = slot;
            so.FindProperty("m_View").objectReferenceValue = view;
            so.FindProperty("m_Collider").objectReferenceValue = root.GetComponent<MeshCollider>();
            so.FindProperty("m_Label").stringValue = p.label;
            so.FindProperty("m_LocalCenter").vector3Value = p.mesh.bounds.center;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fragment;
        }

        /// <summary>
        /// A box hull around the paper, thickened to <see cref="k_MinColliderThickness"/> in world
        /// space. The paper meshes are flat quads: PhysX cannot cook a convex hull from coplanar
        /// points, and a 0 mm collider is hard to touch anyway.
        /// </summary>
        static Mesh MakeColliderMesh(Piece p, Vector3 lossyScale)
        {
            string path = $"{ColliderFolder}/{p.label}_Collider.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = $"{p.label}_Collider";

            Bounds b = p.mesh.bounds;
            Vector3 half = b.extents;
            float s = Mathf.Max(1e-4f, Mathf.Min(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z)));
            float minHalf = k_MinColliderThickness * 0.5f / s;
            half = Vector3.Max(half, Vector3.one * minHalf);

            var v = new Vector3[8];
            for (int i = 0; i < 8; i++)
                v[i] = b.center + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
            int[] tris =
            {
                0, 2, 1, 1, 2, 3,  4, 5, 6, 5, 7, 6,  // -z, +z
                0, 1, 4, 1, 5, 4,  2, 6, 3, 3, 6, 7,  // -y, +y
                0, 4, 2, 2, 4, 6,  1, 3, 5, 3, 7, 5,  // -x, +x
            };
            mesh.vertices = v;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            s_MadeMeshes.Add(mesh);
            return mesh;
        }

        static HomeRevealGroup MakeGroup(Transform parent, Wall w, Dictionary<Material, Material> twins)
        {
            var go = new GameObject(w.name);
            go.transform.SetParent(parent, false);
            var group = go.AddComponent<HomeRevealGroup>();

            var sources = new List<Material>();
            var made = new List<Material>();
            foreach (Renderer r in w.transform.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && !sources.Contains(m) && twins.TryGetValue(m, out Material twin) && twin != null)
                    {
                        sources.Add(m);
                        made.Add(twin);
                    }

            group.Configure(w.name, w.transform, PathOf(w.transform), w.direction, sources.ToArray(), made.ToArray());
            EditorUtility.SetDirty(group);
            return group;
        }

        // ---- verify ---------------------------------------------------------------------------

        public static bool Verify(Scene scene)
        {
            var failures = new List<string>();
            bool Check(bool ok, string what) => InteractionBuildUtil.Check(Tag, ok, what, failures);

            Check(InteractionBuildUtil.RevealShaderOk(out string shaderProblem),
                  $"{InteractionBuildUtil.RevealShaderName} compiles{(shaderProblem != null ? $" ({shaderProblem})" : "")}");

            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (!Check(root != null, $"/{RootName} exists"))
            {
                InteractionBuildUtil.Verdict(Tag, failures);
                return false;
            }

            var assembly = root.GetComponent<HomeAssembly>();
            Check(assembly != null && assembly.built, "HomeAssembly on the root, built");
            Check(root.GetComponent<NetworkObject>() == null, "module root has no NetworkObject");
            Check(PrefabUtility.IsPartOfPrefabInstance(root) &&
                  PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == PrefabPath,
                  $"/{RootName} is an instance of {PrefabPath}");
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Check(prefabAsset != null && prefabAsset.GetComponent<NetworkObject>() == null,
                  "prefab asset exists, root has no NetworkObject");
            if (assembly == null)
            {
                InteractionBuildUtil.Verdict(Tag, failures);
                return false;
            }

            // Zones.
            HomeZone a = assembly.zoneA, b = assembly.zoneB;
            Check(a != null && b != null, "zones A and B wired");
            if (a != null && b != null)
            {
                Check(a.Encloses(b), $"zone A ({Fmt(a.size)}) encloses zone B ({Fmt(b.size)})");
                Transform room = FindPath(scene, k_RoomPath);
                if (room != null)
                {
                    ReadWalls(room, out Bounds roomBounds);
                    Check(BoundsHoldZone(roomBounds, a), $"zone A inside the room bounds {Fmt(roomBounds.size)}");
                }
                ProbeVolume apv = Object.FindObjectsByType<ProbeVolume>(FindObjectsInactive.Include).FirstOrDefault(v => v.gameObject.scene == scene);
                if (apv == null) Check(false, "an Adaptive Probe Volume lights zone A");
                else if (apv.mode != ProbeVolume.Mode.Local)
                    Check(true, $"APV '{apv.name}' is {apv.mode} (covers the scene, so zone A)");
                else
                {
                    var apvBounds = new Bounds(apv.transform.position, apv.size);
                    Check(BoundsHoldZone(apvBounds, a), $"zone A inside APV '{apv.name}' {Fmt(apv.size)}");
                }
            }

            // Fragments.
            var fragmentMat = AssetDatabase.LoadAssetAtPath<Material>(FragmentMaterialPath);
            Check(InteractionBuildUtil.IsRevealMaterialOk(fragmentMat) && fragmentMat.IsKeywordEnabled("_EMISSION"),
                  "HomeFragment.mat: reveal shader, _ALPHATEST_ON, _EMISSION");

            var seenFragments = new HashSet<HomeFragment>();
            var seenGroups = new HashSet<HomeRevealGroup>();
            int pairsOk = 0;
            foreach (HomeAssembly.Pair pair in assembly.pairs)
            {
                HomeFragment f = pair?.fragment;
                HomeRevealGroup g = pair?.group;
                if (!Check(f != null && g != null, $"pair {(f != null ? f.label : "?")} has a fragment and a group")) continue;
                Check(seenFragments.Add(f) && seenGroups.Add(g), $"{f.label}: mapping one-to-one");
                pairsOk++;

                InteractionBuildUtil.VerifyGrabbable(Tag, f.gameObject, failures);
                Transform visual = f.transform.Find("Visual");
                Check(visual != null && visual.localPosition == Vector3.zero && visual.localRotation == Quaternion.identity &&
                      visual.localScale == Vector3.one, $"{f.label}: Visual child is identity");
                Check(f.view != null && f.view.transform == visual && f.GetComponent<BreathingView>() == null,
                      $"{f.label}: HomeFragmentView on Visual, not the root");
                Check(f.grabCollider != null && f.grabCollider.transform == f.transform, $"{f.label}: collider on the root");
                var mr = visual != null ? visual.GetComponent<MeshRenderer>() : null;
                Check(mr != null && mr.sharedMaterial == fragmentMat, $"{f.label}: renders with HomeFragment.mat");

                Transform art = FindPath(scene, k_MiniHousePath + "/" + ArtNameFor(scene, f.label));
                if (f.slot != null && art != null)
                {
                    float err = Vector3.Distance(f.slot.position, art.position);
                    float ang = Quaternion.Angle(f.slot.rotation, art.rotation);
                    Check(err < 0.0005f && ang < 0.05f, $"{f.label}: slot on the art pose ({err * 1000f:0.000} mm, {ang:0.000} deg)");
                    float rootErr = Vector3.Distance(f.transform.position, f.slot.position);
                    Check(rootErr < 0.0005f, $"{f.label}: fragment authored at its slot ({rootErr * 1000f:0.000} mm)");
                    if (b != null) Check(b.Contains(f.worldCenter), $"{f.label}: slot centre inside zone B");
                }
                else Check(false, $"{f.label}: slot and art found");

                bool nameOk = string.Equals(f.label, g.groupName, StringComparison.OrdinalIgnoreCase);
                if (!nameOk) Debug.LogWarning($"[{Tag}] {f.label} maps to '{g.groupName}' (names differ).");
                Debug.Log($"[{Tag}] MAP  {f.label,-6} -> {g.groupName,-6} target '{(g.target != null ? g.target.name : "NULL")}' " +
                          $"float {Fmt(g.floatDirection)}");
            }
            Check(pairsOk == 5, $"5 fragment/group pairs (found {pairsOk})");

            // Groups.
            var groups = assembly.pairs.Where(p => p?.group != null).Select(p => p.group).ToList();
            if (assembly.finalGroup != null) groups.Add(assembly.finalGroup);
            Check(assembly.finalGroup != null && assembly.finalGroup.groupName == k_FinalGroupName,
                  "final group is Top (no fragment)");
            Check(groups.Count == 6, $"6 reveal groups (found {groups.Count})");
            Check(groups.Select(g => g.target).Distinct().Count() == groups.Count, "each group has its own wall");

            foreach (HomeRevealGroup g in groups)
            {
                Check(g.target != null, $"group {g.groupName}: target override set (scene object)");
                Check(g.target == null || g.targetPath == PathOf(g.target), $"group {g.groupName}: path fallback '{g.targetPath}'");
                if (g.target == null) continue;

                int slotsTotal = 0, slotsBad = 0, twinsBad = 0;
                foreach (Renderer r in g.target.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] originals = r.sharedMaterials;
                    var reveal = new Material[originals.Length];
                    for (int s = 0; s < originals.Length; s++)
                    {
                        slotsTotal++;
                        Material m = originals[s];
                        if (m == null) { slotsBad++; continue; }
                        if (HomeRevealGroup.IsMelting(m)) { reveal[s] = m; continue; }
                        Material twin = g.TwinOf(m);
                        if (twin == null) { slotsBad++; continue; }
                        reveal[s] = twin;
                        if (!InteractionBuildUtil.IsRevealMaterialOk(twin) || !AssetDatabase.Contains(twin)) twinsBad++;
                    }
                    if (reveal.Length != originals.Length) slotsBad++;
                }
                Check(slotsBad == 0, $"group {g.groupName}: every one of {slotsTotal} material slots has a melting or twin reveal");
                Check(twinsBad == 0, $"group {g.groupName}: twins are assets on {InteractionBuildUtil.RevealShaderName} with _ALPHATEST_ON");
            }

            Transform s3 = FindPath(scene, k_S3Path);
            Check(s3 != null && !InteractionBuildUtil.AnyBatchingStatic(s3.gameObject), "no BatchingStatic left under S3");
            Transform mini = FindPath(scene, k_MiniHousePath);
            Check(mini != null && !mini.gameObject.activeSelf, "LowPolyWall art switched off (kept as the answer key)");

            // Network + bootstrap.
            InteractionBuildUtil.VerifyHashes(Tag, scene, failures);
            InteractionBuildUtil.VerifyDemoBootstrap(Tag, scene, failures);

            // Debug id unique among this scene's interactions.
            var ids = new List<string>();
            foreach (GameObject go in scene.GetRootGameObjects())
                foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb is IDebugInteraction di) ids.Add(di.debugId);
            Check(ids.Count(i => i == assembly.debugId) == 1, $"debug id '{assembly.debugId}' is unique in the scene");

            InteractionBuildUtil.Verdict(Tag, failures);
            return failures.Count == 0;
        }

        static string ArtNameFor(Scene scene, string label)
        {
            Transform mini = FindPath(scene, k_MiniHousePath);
            if (mini == null) return label;
            foreach (Transform c in mini)
                if (LabelOfArt(c.name) == label) return c.name;
            return label;
        }

        static bool BoundsHoldZone(Bounds bounds, HomeZone zone)
        {
            Vector3 h = zone.size * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                if (!bounds.Contains(zone.center + zone.rotation * corner)) return false;
            }
            return true;
        }

        static string Fmt(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";
    }
}
