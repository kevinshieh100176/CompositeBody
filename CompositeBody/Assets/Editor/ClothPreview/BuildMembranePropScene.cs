using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Avatar.Skin;

namespace CompositeBody.Avatar.Cloth.EditorTools
{
    /// <summary>
    /// Wraps a static prop -- the rustic chair, by default -- in a single vacuum film, and builds
    /// a look-dev scene for it lit the same way as <see cref="BuildMembraneFilmScene"/> so the
    /// chair and the figures can be judged against each other as the same material.
    ///
    /// Two things here that the avatar path does not need:
    ///
    /// - <b>A whole-object wrap.</b> The film comes from <see cref="MembraneWrapBuilder"/>, which
    ///   builds a new enclosing surface, rather than from <see cref="MembraneShellBuilder"/>,
    ///   which offsets the body's own mesh. A prop is rarely one connected surface -- the chair
    ///   is fifteen interpenetrating shells -- and offsetting that gives fifteen separate bags
    ///   with bare geometry between them.
    /// - <b>A static attach path.</b> <see cref="WrapStatic"/>, a MeshFilter/MeshRenderer child,
    ///   where the avatar hangs its film off a second SkinnedMeshRenderer sharing the body's
    ///   bones.
    ///
    /// The span radius is the one setting with a look attached, so the run sweeps it and renders
    /// each value rather than picking for you. Everything else is held at the avatar's values.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; [-clothOut &lt;dir&gt;]
    ///   -executeMethod CompositeBody.Avatar.Cloth.EditorTools.BuildMembranePropScene.Run
    /// </summary>
    public static class BuildMembranePropScene
    {
        const string k_ScenePath = "Assets/_Scenes/MembranePropChair.unity";
        const string k_PropPath = "Assets/_models/rustic-chair/source/chair/chair_low.obj";
        const string k_AlbedoPath = "Assets/_models/rustic-chair/textures/chair_albedo.png";

        const string k_FilmMeshDir = "Assets/_models/PropMembrane";
        const string k_FilmMaterialPath = "Assets/Materials/MembraneProp.mat";
        const string k_DarkBodyPath = "Assets/Materials/MembranePropBody.mat";
        const string k_WoodBodyPath = "Assets/Materials/RusticChairWood.mat";

        /// <summary>
        /// How far gaps can be and still get skinned over: the film bridges anything narrower
        /// than twice this. 10 mm rounds the joints and little else; 45 mm closes the gaps
        /// between the spindles into a sheet. Which of these is the piece's material is a look
        /// decision, so all three get rendered.
        /// </summary>
        static readonly float[] k_SpanSweep = { 0.010f, 0.025f, 0.045f };

        /// <summary>The one left in the scene, and the one the detail shots are taken from.</summary>
        const float k_DefaultSpan = 0.025f;

        // The sheet stands closer off a prop than off a body: the chair's features -- a turned
        // spindle, the lip of the seat -- are smaller than a limb, and at the avatar's 20 mm the
        // film clears them entirely and stops describing the object underneath.
        const float k_Offset = 0.012f;
        const float k_VoxelSize = 0.005f;
        const int k_SmoothIterations = 12;

        /// <summary>A prop that imports outside this range is a scale mistake, not a big chair.</summary>
        const float k_MinPlausibleHeight = 0.15f;
        const float k_MaxPlausibleHeight = 4.0f;

        public static void Run()
        {
            Debug.Log("[PropMembrane] Starting...");

            var shader = Shader.Find("CompositeBody/VacuumMembrane");
            if (shader == null)
            {
                Debug.LogError("[PropMembrane] RESULT: FAIL - shader 'CompositeBody/VacuumMembrane' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[PropMembrane] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[PropMembrane] RESULT: FAIL - the membrane shader has compile errors.");
                return;
            }

            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("[PropMembrane] RESULT: FAIL - URP/Lit not found.");
                return;
            }

            var propAsset = AssetDatabase.LoadAssetAtPath<GameObject>(k_PropPath);
            if (propAsset == null)
            {
                Debug.LogError($"[PropMembrane] RESULT: FAIL - prop not found at {k_PropPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var prop = (GameObject)PrefabUtility.InstantiatePrefab(propAsset);
            prop.name = "RusticChair_Membrane";
            PrefabUtility.UnpackPrefabInstance(prop, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var renderers = prop.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogError("[PropMembrane] RESULT: FAIL - the prop has no MeshRenderer.");
                return;
            }

            // Everything the prop is made of, in one mesh in the root's space. The wrap does not
            // care how many pieces went in, but it does need them all in one coordinate system
            // and in one grid -- a per-renderer wrap would reintroduce exactly the per-part bags
            // this is here to avoid.
            Mesh source = CombineIntoRootSpace(prop.transform, renderers);
            if (source == null) return;

            if (!CheckScale(source)) return;
            ReportTopology(source);

            // --- the films -----------------------------------------------------------------
            Directory.CreateDirectory(k_FilmMeshDir);
            var films = new List<(float span, Mesh mesh)>();

            foreach (float span in k_SpanSweep)
            {
                Mesh film = BuildWrap(source, span);
                if (film == null) continue;

                string path = $"{k_FilmMeshDir}/RusticChair_Wrap_{Mathf.RoundToInt(span * 1000f)}mm.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(film, path);
                films.Add((span, film));
            }

            if (films.Count == 0)
            {
                Debug.LogError("[PropMembrane] RESULT: FAIL - no film was built at any span radius.");
                return;
            }

            // --- materials and assembly ------------------------------------------------------
            Material filmMat = LoadOrCreateFilmMaterial(shader);
            Material darkBody = LoadOrCreateDarkBody(litShader);
            Material woodBody = LoadOrCreateWoodBody(litShader);

            // Dark by default, matching the avatar: the film's whole read is the difference
            // between where it lies on the surface and where it spans air, and a bright body
            // under it flattens that difference out.
            foreach (var r in renderers)
            {
                SetAllMaterials(r, darkBody);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Mesh defaultFilm = films[0].mesh;
            foreach (var (span, mesh) in films)
                if (Mathf.Approximately(span, k_DefaultSpan)) defaultFilm = mesh;

            MeshFilter filmFilter = WrapStatic(prop.transform, renderers[0], defaultFilm, filmMat);

            // --- placement -------------------------------------------------------------------
            // On the floor and centred, computed rather than hardcoded: this OBJ's pivot is at
            // the mesh centroid and sits over a metre down the -Z axis, so the authored transform
            // puts the chair half underground and well off to one side.
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            prop.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);

            Bounds placed = renderers[0].bounds;
            foreach (var r in renderers) placed.Encapsulate(r.bounds);

            BuildEnvironment(placed, out Camera wide, out Camera detail);

            Directory.CreateDirectory("Assets/_Scenes");
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PropMembrane] Saved {films.Count} film(s) to {k_FilmMeshDir} and the scene to {k_ScenePath}");

            // --- renders ---------------------------------------------------------------------
            string outDir = GetArg("-clothOut");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);

                foreach (var (span, mesh) in films)
                {
                    filmFilter.sharedMesh = mesh;
                    string tag = $"span{Mathf.RoundToInt(span * 1000f)}mm";
                    RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, $"wrap_{tag}.png"));
                    RenderClothPreview.RenderCameraToFile(detail, Path.Combine(outDir, $"wrap_{tag}_detail.png"));
                }

                filmFilter.sharedMesh = defaultFilm;

                filmMat.SetFloat("_ShowContact", 1f);
                RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, "wrap_contact.png"));
                filmMat.SetFloat("_ShowContact", 0f);

                foreach (var r in renderers) SetAllMaterials(r, woodBody);
                RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, "wrap_wood.png"));
                foreach (var r in renderers) SetAllMaterials(r, darkBody);

                EditorUtility.SetDirty(filmMat);
                AssetDatabase.SaveAssets();
                Debug.Log($"[PropMembrane] Wrote renders to {outDir}");
            }

            Debug.Log("[PropMembrane] RESULT: PASS");
        }

        static Mesh BuildWrap(Mesh source, float span)
        {
            var settings = MembraneWrapBuilder.Settings.Default;
            settings.voxelSize = k_VoxelSize;
            settings.offset = k_Offset;
            settings.spanRadius = span;
            settings.smoothIterations = k_SmoothIterations;

            Mesh film;
            try
            {
                film = MembraneWrapBuilder.Build(source, settings, out string report);
                Debug.Log($"[PropMembrane] span {span * 1000f:F0}mm:\n{report}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PropMembrane] span {span * 1000f:F0}mm failed: {e}");
                return null;
            }

            if (film == null)
            {
                Debug.LogWarning($"[PropMembrane] span {span * 1000f:F0}mm produced no film.");
                return null;
            }

            film.name = $"RusticChair_Wrap_{Mathf.RoundToInt(span * 1000f)}mm";
            return film;
        }

        /// <summary>
        /// Every mesh under the prop, baked into the root's local space. Built by hand rather
        /// than with Mesh.CombineMeshes so it works regardless of the source models' Read/Write
        /// setting, which for an imported prop is normally off.
        /// </summary>
        public static Mesh CombineIntoRootSpace(Transform root, MeshRenderer[] renderers)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                Mesh m = mf.sharedMesh;
                Matrix4x4 toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                int baseIndex = verts.Count;

                foreach (var v in m.vertices) verts.Add(toRoot.MultiplyPoint3x4(v));

                for (int s = 0; s < m.subMeshCount; s++)
                    foreach (int i in m.GetTriangles(s))
                        tris.Add(baseIndex + i);
            }

            if (verts.Count == 0)
            {
                Debug.LogError("[PropMembrane] RESULT: FAIL - the prop's renderers have no mesh data.");
                return null;
            }

            var combined = new Mesh
            {
                name = "PropSource",
                indexFormat = verts.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            combined.SetVertices(verts);
            combined.SetTriangles(tris, 0);
            combined.RecalculateBounds();

            Debug.Log($"[PropMembrane] Combined {renderers.Length} renderer(s) into " +
                      $"{verts.Count:N0} verts / {tris.Count / 3:N0} triangles, bounds {combined.bounds.size}.");
            return combined;
        }

        /// <summary>
        /// Hangs a film off a static prop. The avatar's film is a second SkinnedMeshRenderer
        /// sharing the body's bones; a prop has none, so the film is just a mesh. Parented to the
        /// prop root with <c>worldPositionStays: false</c>, because that is the space the wrap
        /// was built in.
        /// </summary>
        public static MeshFilter WrapStatic(Transform root, Renderer visibilitySource, Mesh film, Material filmMat)
        {
            var filmGO = new GameObject("MembraneWrap");
            filmGO.transform.SetParent(root, false);

            var mf = filmGO.AddComponent<MeshFilter>();
            mf.sharedMesh = film;

            var mr = filmGO.AddComponent<MeshRenderer>();
            mr.sharedMaterial = filmMat;
            // The prop is inside the film, so its shadow would be cast on the film's own inside.
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var link = filmGO.AddComponent<MembraneFilmLink>();
            var so = new SerializedObject(link);
            so.FindProperty("m_Source").objectReferenceValue = visibilitySource;
            so.FindProperty("m_Film").objectReferenceValue = mr;
            so.ApplyModifiedPropertiesWithoutUndo();

            return mf;
        }

        /// <summary>
        /// Every membrane setting is in absolute metres, so a mis-scaled prop does not produce a
        /// wrong-looking film -- it produces an invisible one, with every vertex reporting a gap
        /// near zero, reading as fully in contact, and drawing at the 0.06 clear alpha. This OBJ
        /// is authored in millimetres behind a header comment claiming centimetres, so it is
        /// worth saying out loud rather than discovering in a render.
        /// </summary>
        static bool CheckScale(Mesh source)
        {
            float height = source.bounds.size.y;
            if (height >= k_MinPlausibleHeight && height <= k_MaxPlausibleHeight) return true;

            var importer = AssetImporter.GetAtPath(k_PropPath) as ModelImporter;
            Debug.LogError(
                $"[PropMembrane] RESULT: FAIL - the prop imports {height:F2} m tall " +
                $"(bounds {source.bounds.size}), outside the plausible " +
                $"{k_MinPlausibleHeight}-{k_MaxPlausibleHeight} m. The membrane offsets are " +
                $"absolute metres, so at this scale the film would sit optically on the surface " +
                $"and render invisible. Set the model importer's scale factor " +
                $"(currently {(importer != null ? importer.globalScale.ToString("G") : "unknown")}).");
            return false;
        }

        /// <summary>
        /// What the prop is made of. Informational now rather than a warning: the wrap is built
        /// in a voxel grid, so disconnected and interpenetrating shells are no longer a problem
        /// to be fixed in the asset. It is still worth printing, because the shell count is the
        /// thing that decides whether the film needed a span radius at all.
        /// </summary>
        static void ReportTopology(Mesh mesh)
        {
            var verts = mesh.vertices;
            var repOf = new Dictionary<Vector3Int, int>(verts.Length);
            var weld = new int[verts.Length];
            int repCount = 0;

            for (int i = 0; i < verts.Length; i++)
            {
                var key = new Vector3Int(
                    Mathf.RoundToInt(verts[i].x * 10000f),
                    Mathf.RoundToInt(verts[i].y * 10000f),
                    Mathf.RoundToInt(verts[i].z * 10000f));

                if (!repOf.TryGetValue(key, out int rep))
                {
                    rep = repCount++;
                    repOf.Add(key, rep);
                }
                weld[i] = rep;
            }

            var parent = new int[repCount];
            for (int i = 0; i < repCount; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            void Union(int a, int bb)
            {
                int ra = Find(a), rb = Find(bb);
                if (ra != rb) parent[ra] = rb;
            }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Union(weld[tris[t]], weld[tris[t + 1]]);
                    Union(weld[tris[t + 1]], weld[tris[t + 2]]);
                }
            }

            var roots = new HashSet<int>();
            for (int i = 0; i < repCount; i++) roots.Add(Find(i));

            Debug.Log($"[PropMembrane] Source topology: {repCount:N0} welded verts in {roots.Count} shell(s).");
        }

        static void SetAllMaterials(Renderer r, Material mat)
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }

        /// <summary>
        /// The prop's own film material, separate from the avatar's. Same values today, but the
        /// contact settings are the one thing that genuinely may have to differ: a wrapped prop's
        /// gap range comes out of a different construction than Ch36's, and sharing one asset
        /// would mean tuning the chair silently retunes the player.
        /// </summary>
        static Material LoadOrCreateFilmMaterial(Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_FilmMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(shader) { name = "MembraneProp" };
                AssetDatabase.CreateAsset(mat, k_FilmMaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.SetColor("_FilmColor", new Color(0.70f, 0.76f, 0.85f));
            mat.SetColor("_FrostColor", new Color(0.88f, 0.91f, 0.96f));
            mat.SetFloat("_ClearAlpha", 0.06f);
            mat.SetFloat("_FrostAlpha", 0.60f);
            mat.SetFloat("_ContactFloor", 0.007f);
            mat.SetFloat("_ContactRange", 0.020f);
            mat.SetFloat("_ContactSharpness", 1.0f);
            mat.SetFloat("_TautClarity", 0.6f);
            mat.SetFloat("_CreaseScale", 30f);
            mat.SetFloat("_CreaseStretch", 7f);
            mat.SetFloat("_CreaseStrength", 0.70f);
            mat.SetFloat("_CreaseSharpness", 3.5f);
            mat.SetFloat("_Smoothness", 0.93f);
            mat.SetColor("_SpecColor2", Color.white);
            mat.SetFloat("_SpecIntensity", 4.0f);
            mat.SetFloat("_FresnelPower", 2.6f);
            mat.SetFloat("_FresnelIntensity", 1.0f);
            mat.SetFloat("_EdgeOpacity", 0.42f);
            mat.SetFloat("_Translucency", 0.55f);
            mat.SetFloat("_TranslucencyPower", 4.0f);
            mat.SetFloat("_AmbientIntensity", 0.35f);
            mat.SetFloat("_ShowContact", 0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreateDarkBody(Shader litShader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_DarkBodyPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(litShader) { name = "MembranePropBody" };
                AssetDatabase.CreateAsset(mat, k_DarkBodyPath);
            }
            else if (mat.shader != litShader)
            {
                mat.shader = litShader;
            }

            // The same values the avatar's body sits at, so the two read as one material system.
            mat.SetColor("_BaseColor", new Color(0.085f, 0.082f, 0.088f));
            mat.SetFloat("_Smoothness", 0.14f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// The chair's own albedo, for the comparison render. Albedo only: where the film is in
        /// contact it draws at 0.06 alpha and you read the body almost straight through, so the
        /// base colour is the whole of what this variant is asking about, and the normal map
        /// would need its import type changed to be worth wiring.
        /// </summary>
        static Material LoadOrCreateWoodBody(Shader litShader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_WoodBodyPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(litShader) { name = "RusticChairWood" };
                AssetDatabase.CreateAsset(mat, k_WoodBodyPath);
            }
            else if (mat.shader != litShader)
            {
                mat.shader = litShader;
            }

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(k_AlbedoPath);
            if (albedo == null)
                Debug.LogWarning($"[PropMembrane] No albedo at {k_AlbedoPath}; the wood variant will be flat.");

            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.20f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// The film scene's lighting, framed off the prop's bounds: hard key from one side, cool
        /// rim from the other, no ground and almost no ambient. Any fill at all washes out the
        /// streak highlights along the creases, which are the whole of what the film is for.
        /// </summary>
        static void BuildEnvironment(Bounds b, out Camera wide, out Camera detail)
        {
            var keyGO = new GameObject("Key");
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.9f;
            key.color = new Color(1f, 0.99f, 0.96f);
            key.shadows = LightShadows.Soft;
            keyGO.transform.rotation = Quaternion.Euler(18f, 232f, 0f);

            var rimGO = new GameObject("Rim");
            var rim = rimGO.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 1.15f;
            rim.color = new Color(0.86f, 0.92f, 1f);
            rimGO.transform.rotation = Quaternion.Euler(10f, 42f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.07f, 0.08f, 0.10f);
            RenderSettings.ambientEquatorColor = new Color(0.05f, 0.05f, 0.06f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.02f, 0.02f);
            RenderSettings.fog = false;

            float reach = Mathf.Max(b.size.x, b.size.y, b.size.z);

            var wideGO = new GameObject("PreviewCamera");
            wide = wideGO.AddComponent<Camera>();
            wideGO.AddComponent<UniversalAdditionalCameraData>();
            wide.clearFlags = CameraClearFlags.SolidColor;
            wide.backgroundColor = Color.black;
            wide.fieldOfView = 32f;
            wide.nearClipPlane = 0.02f;
            wideGO.transform.position = b.center + new Vector3(0.75f, 0.45f, 1.0f).normalized * (reach * 2.4f);
            wideGO.transform.LookAt(b.center);

            // Close on the top of the back, where the sheet has the most to do: a convex rail it
            // pulls taut over, and the gaps between the spindles to bridge or fall into.
            var detailGO = new GameObject("DetailCamera");
            detail = detailGO.AddComponent<Camera>();
            detailGO.AddComponent<UniversalAdditionalCameraData>();
            detail.clearFlags = CameraClearFlags.SolidColor;
            detail.backgroundColor = Color.black;
            detail.fieldOfView = 30f;
            detail.nearClipPlane = 0.01f;
            Vector3 focus = new Vector3(b.center.x, b.max.y - b.size.y * 0.18f, b.center.z);
            detailGO.transform.position = focus + new Vector3(0.6f, 0.25f, 1.0f).normalized * (reach * 0.95f);
            detailGO.transform.LookAt(focus);
        }

        static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, System.StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
