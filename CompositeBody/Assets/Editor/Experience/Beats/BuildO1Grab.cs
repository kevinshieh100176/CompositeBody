using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// O-1: each player has one object, and only they can see it. Player A a wool coat, Player B
    /// a soviet mug.
    ///
    /// NOT NETWORKED, and that is the whole architecture. Neither player ever sees the other's
    /// object, so there is nothing to replicate: no NetworkObject, no ClientNetworkTransform, no
    /// authority to hand around when a grab starts. Both objects exist on both machines and each
    /// client hides the one that is not theirs, exactly as the 真人 are handled in O-0. A later
    /// beat that needs the other player to see what you are holding would need all of that; this
    /// one does not, and building it anyway would mean maintaining a synchronisation nobody can
    /// observe.
    ///
    /// Visibility is RoleVisibility, which the project already has -- renderers AND colliders, so
    /// the other player cannot grab a coat they cannot see by sweeping a hand through it.
    ///
    /// The meshes arrive as zipped source. They are extracted beside the chair's, imported at
    /// whatever scale they were authored in, and then scaled HERE to a real size: a coat is about
    /// 1.1 m on the hanger and a mug about 11 cm, and a prop that is secretly four metres tall is
    /// the sort of thing that costs an afternoon in a headset.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildO1Grab.Run
    /// </summary>
    public static class BuildO1Grab
    {
        const string k_PrefabPath = "Assets/_Scenes/O_1.prefab";

        const string k_CoatModel = "Assets/_models/wool-coat/source/coat/Lavard_Coat.obj";
        const string k_CoatTexture = "Assets/_models/wool-coat/textures/Lavard_Coat.jpeg";
        const string k_CoatMaterial = "Assets/Materials/O1Coat.mat";

        const string k_MugModel = "Assets/_models/soviet-mug/source/mug/cap.fbx";
        const string k_MugBase = "Assets/_models/soviet-mug/textures/cap_DefaultMaterial_BaseColor.png";
        const string k_MugNormal = "Assets/_models/soviet-mug/textures/cap_DefaultMaterial_Normal.png";
        const string k_MugMaterial = "Assets/Materials/O1Mug.mat";

        /// <summary>Real sizes, in metres, along the model's longest axis.</summary>
        const float k_CoatHeight = 1.10f;
        const float k_MugHeight = 0.11f;

        [MenuItem("Tools/O-1/Build Grab Interaction")]
        public static void Run()
        {
            Debug.Log("[O-1] Starting...");

            var root = new GameObject("O_1");
            var content = new GameObject("Content");
            content.transform.SetParent(root.transform, false);

            if (!BuildInto(content.transform))
            {
                Object.DestroyImmediate(root);
                Debug.LogError("[O-1] RESULT: FAIL - a source mesh or shader is missing. The " +
                               "models ship zipped; both archives have to be extracted into " +
                               "their source folders before Unity can import them.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(k_PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath, out bool saved);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            if (!saved)
            {
                Debug.LogError($"[O-1] RESULT: FAIL - could not write {k_PrefabPath}.");
                return;
            }

            Debug.Log($"[O-1] {k_PrefabPath}\n" +
                      "  Coat visible to Player A, mug to Player B; renderers and colliders both.\n" +
                      "  Local only -- neither object is networked, because neither is ever seen " +
                      "by the other player.\n" +
                      "  F12 in the debug panel switches which one you see.\n" +
                      "[O-1] RESULT: PASS");
        }

        /// <summary>
        /// Put the two objects under an existing parent, for the merged Onboarding prefab. The
        /// beat is two props and a rule about who sees them; it has no room of its own, which is
        /// the whole reason the beats were merged.
        ///
        /// Returns false if a mesh or shader is missing, so a caller building something larger
        /// can stop rather than save a prefab with a hole in it.
        /// </summary>
        public static bool BuildInto(Transform parent)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Mesh coatMesh = LoadMesh(k_CoatModel);
            Mesh mugMesh = LoadMesh(k_MugModel);
            if (lit == null || coatMesh == null || mugMesh == null) return false;

            Material coatMat = BuildMaterial(lit, k_CoatMaterial, "O1Coat", k_CoatTexture, null,
                                             0f, 0.25f);
            Material mugMat = BuildMaterial(lit, k_MugMaterial, "O1Mug", k_MugBase, k_MugNormal,
                                            0.6f, 0.55f);

            // Within reach of a player standing at the near end, one to each side, at the height
            // a thing would be if someone were holding it out to you.
            BuildStation(parent, "Coat (Player A)", coatMesh, coatMat,
                         PlayerRole.Player1, new Vector3(-0.38f, 1.05f, -1.35f), k_CoatHeight);

            BuildStation(parent, "Mug (Player B)", mugMesh, mugMat,
                         PlayerRole.Player2, new Vector3(0.38f, 1.05f, -1.35f), k_MugHeight);

            return true;
        }

        /// <summary>
        /// One object: the mesh, something to grab it by, and the rule about who can see it.
        /// </summary>
        static void BuildStation(Transform parent, string name, Mesh mesh, Material material,
                                 PlayerRole owner, Vector3 position, float targetSize)
        {
            // Built once, then it is yours. A re-run leaves an existing station exactly where
            // it was put -- position, rotation and scale -- because staging is a hand decision
            // and a builder that resets it every time is a builder nobody can run.
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                Debug.Log($"[O-1] {name}: already present, left where it is.");
                return;
            }

            var station = new GameObject(name);
            station.transform.SetParent(parent, false);
            station.transform.position = position;

            var go = new GameObject(name.Split(' ')[0]);
            go.transform.SetParent(station.transform, false);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            // Scaled to a real size from the mesh's own bounds, so it does not matter what units
            // the model was exported in -- and the factor is logged, so a model that turns out to
            // be in millimetres says so rather than merely looking wrong.
            Vector3 size = mesh.bounds.size;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float scale = longest > 1e-5f ? targetSize / longest : 1f;
            go.transform.localScale = Vector3.one * scale;

            // Convex: a non-kinematic Rigidbody cannot carry a concave mesh collider. The hull is
            // fatter than a coat really is, which for something meant to be picked up is the
            // forgiving direction to be wrong in.
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.useGravity = false;      // an object that falls to the floor before anyone reaches it
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;   // held where the hand closed, not snapped to the pivot
            grab.throwOnDetach = false;     // these are meant to be put down, not thrown

            // On the station rather than on the object, so the rule survives the object being
            // rebuilt, and so the whole station goes together if anything is added beside it.
            var visibility = station.AddComponent<RoleVisibility>();
            var so = new SerializedObject(visibility);
            SerializedProperty roles = so.FindProperty("m_VisibleToRoles");
            roles.arraySize = 1;
            roles.GetArrayElementAtIndex(0).intValue = (int)owner;
            so.FindProperty("m_AlsoDisableColliders").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[O-1] {name}: mesh {size.x:F2}x{size.y:F2}x{size.z:F2} -> " +
                      $"scale {scale:F4} for {targetSize:F2} m, visible to {owner} only.");
        }

        static Mesh LoadMesh(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Mesh mesh) return mesh;
            }

            Debug.LogError($"[O-1] no mesh inside {path}.");
            return null;
        }

        static Material BuildMaterial(Shader lit, string path, string name, string basePath,
                                      string normalPath, float metallic, float smoothness)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);
            if (baseMap != null) material.SetTexture("_BaseMap", baseMap);
            else Debug.LogWarning($"[O-1] no base texture at {basePath}; {name} will be untextured.");

            if (!string.IsNullOrEmpty(normalPath))
            {
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                if (normal != null)
                {
                    // The importer has to be told it is a normal map; as a colour texture it
                    // would light the mug as if it were printed with blue-violet noise.
                    var importer = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                    if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                    {
                        importer.textureType = TextureImporterType.NormalMap;
                        importer.SaveAndReimport();
                    }
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
            }

            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);

            // Transparent, so ScanHandover can fade the object up as the 真人 dissolves away.
            //
            // Alpha rather than the distance dither the body fades with: that one hides whatever
            // is NEARER than its threshold, which is right for a body you stand inside and wrong
            // for something you pick up -- a mug raised to the face would disappear. The cost is
            // transparent sorting, which in a room whose whole palette is additive beams and
            // point clouds is a cost already being paid.
            material.SetFloat("_Surface", 1f);                  // 0 opaque, 1 transparent
            material.SetFloat("_Blend", 0f);                    // alpha blend
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
