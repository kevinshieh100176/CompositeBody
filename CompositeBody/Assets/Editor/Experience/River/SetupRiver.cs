using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Gets a river from nothing to something you can look at: import settings on the generated
    /// textures, a water material, and a spline in the scene with a plausible shape.
    ///
    /// THE IMPORT SETTINGS ARE NOT COSMETIC. The two normal maps have to be imported AS normal
    /// maps -- UnpackNormalScale in the shader assumes that encoding, and a normal map imported as
    /// a colour texture comes back with sRGB applied and decodes to garbage that looks like
    /// plausible-but-wrong lighting. The foam mask is the opposite case: it is data, not colour,
    /// so sRGB goes off.
    ///
    /// Re-runnable. The material is created once and then left alone, because it is the thing
    /// that gets tuned by hand.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.SetupRiver.Run
    /// </summary>
    public static class SetupRiver
    {
        const string k_TextureDir = "Assets/Textures/Water";
        const string k_NormalA = k_TextureDir + "/Water_Normal_A.png";
        const string k_NormalB = k_TextureDir + "/Water_Normal_B.png";
        const string k_Foam = k_TextureDir + "/Water_Foam.png";
        const string k_Material = "Assets/Materials/River.mat";
        const string k_BedMaterial = "Assets/Materials/RiverBed.mat";
        const string k_Shader = "CompositeBody/River";

        [MenuItem("Tools/River/1 - Import Textures And Material")]
        public static void Run()
        {
            Debug.Log("[RiverSetup] Starting...");

            ImportNormal(k_NormalA);
            ImportNormal(k_NormalB);
            ImportMask(k_Foam);

            Material material = EnsureMaterial();
            if (material == null) return;

            Material bed = EnsureBedMaterial();
            if (bed == null) return;

            AssetDatabase.SaveAssets();
            Debug.Log($"[RiverSetup] textures imported and {k_Material} ready.\n" +
                      "[RiverSetup] RESULT: PASS");
        }

        [MenuItem("Tools/River/2 - Create River In Scene")]
        public static void CreateInScene()
        {
            Material material = EnsureMaterial();
            if (material == null) return;

            var go = new GameObject("River_S1_1");
            var container = go.AddComponent<SplineContainer>();
            Spline spline = container.Spline;

            // An asymmetric course: it crosses the play area off-centre and descends downstream,
            // so neither player is standing on the middle of it and the flow has a direction.
            // Deliberately not a smooth arc -- a river that reads as a drawn curve reads as a path.
            float3[] knots =
            {
                new(-2.6f,  0.05f, -12.0f),
                new(-0.8f,  0.02f,  -6.5f),
                new( 0.7f,  0.00f,  -1.5f),
                new(-0.4f, -0.03f,   3.5f),
                new( 1.9f, -0.07f,   9.0f),
                new( 1.1f, -0.10f,  14.0f),
            };

            foreach (float3 knot in knots) spline.Add(new BezierKnot(knot));
            for (int i = 0; i < spline.Count; i++)
                spline.SetTangentMode(i, TangentMode.AutoSmooth);

            var river = go.AddComponent<RiverSpline>();
            var so = new SerializedObject(river);
            SerializedProperty materialProperty = so.FindProperty("m_Material");
            if (materialProperty != null) materialProperty.objectReferenceValue = material;
            so.ApplyModifiedPropertiesWithoutUndo();

            AssignBedMaterial(river);

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);

            Debug.Log($"[RiverSetup] created '{go.name}' with {knots.Length} knots. " +
                      "Shape it, then Tools > River > Build Selected River.\n" +
                      "[RiverSetup] RESULT: PASS", go);
        }

        /// <summary>
        /// Fills a river's empty bed slot. Separate from creating the river because the bed
        /// arrived after the first rivers did, and an existing one would otherwise refuse to
        /// rebuild with "has no bed material" and no way to fix it but by hand.
        /// </summary>
        public static void AssignBedMaterial(RiverSpline river)
        {
            if (river == null) return;

            var so = new SerializedObject(river);
            SerializedProperty slot = so.FindProperty("m_BedMaterial");
            if (slot == null || slot.objectReferenceValue != null) return;

            Material bed = EnsureBedMaterial();
            if (bed == null) return;

            slot.objectReferenceValue = bed;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(river);
            Debug.Log($"[RiverSetup] gave '{river.name}' the bed material.", river);
        }

        /// <summary>
        /// The riverbed. Plain URP/Lit and deliberately dark: it exists to be the thing the
        /// water's depth is measured against and to catch a little of the orbit light through
        /// the shallows, not to be looked at. Created once, then left alone to be tuned.
        /// </summary>
        public static Material EnsureBedMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_BedMaterial);
            if (existing != null) return existing;

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[RiverSetup] RESULT: FAIL - URP/Lit not found.");
                return null;
            }

            var bed = new Material(lit) { name = "RiverBed" };
            bed.SetColor("_BaseColor", new Color(0.16f, 0.15f, 0.14f));
            bed.SetFloat("_Smoothness", 0.12f);

            AssetDatabase.CreateAsset(bed, k_BedMaterial);
            Debug.Log($"[RiverSetup] created {k_BedMaterial}.");
            return bed;
        }

        static Material EnsureMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_Material);
            if (existing != null) return existing;

            Shader shader = Shader.Find(k_Shader);
            if (shader == null)
            {
                Debug.LogError($"[RiverSetup] RESULT: FAIL - shader '{k_Shader}' not found. " +
                               "Has River.shader compiled?");
                return null;
            }

            var material = new Material(shader) { name = "River" };

            material.SetTexture("_NormalA", AssetDatabase.LoadAssetAtPath<Texture>(k_NormalA));
            material.SetTexture("_NormalB", AssetDatabase.LoadAssetAtPath<Texture>(k_NormalB));
            material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture>(k_Foam));

            // The Y of these is TILES PER METRE, because UV.y is distance along the spline. So
            // layer A repeats every 2 m and B every 0.6 m, and scrolling at different speeds the
            // two never line up into a visible pattern.
            material.SetTextureScale("_NormalA", new Vector2(2f, 0.5f));
            material.SetTextureScale("_NormalB", new Vector2(5f, 1.7f));

            AssetDatabase.CreateAsset(material, k_Material);
            Debug.Log($"[RiverSetup] created {k_Material}.");
            return material;
        }

        static void ImportNormal(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[RiverSetup] no texture at {path}.");
                return;
            }

            if (importer.textureType == TextureImporterType.NormalMap &&
                importer.wrapMode == TextureWrapMode.Repeat) return;

            importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            Debug.Log($"[RiverSetup] {path} imported as a normal map.");
        }

        static void ImportMask(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[RiverSetup] no texture at {path}.");
                return;
            }

            if (!importer.sRGBTexture && importer.wrapMode == TextureWrapMode.Repeat) return;

            // A mask is data. Left as sRGB the threshold in the shader would land in the wrong
            // place and the foam would come out either everywhere or nowhere.
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            Debug.Log($"[RiverSetup] {path} imported as a linear mask.");
        }
    }
}
