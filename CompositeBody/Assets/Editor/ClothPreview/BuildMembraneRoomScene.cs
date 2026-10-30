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
    /// A small room -- floor, four walls, ceiling, a table, two chairs and a crate -- sealed
    /// inside one continuous vacuum film.
    ///
    /// The thing that makes a room different from a prop is which side the film is on. A prop is
    /// looked at from outside, so <see cref="MembraneWrapBuilder"/> starts its fill at the grid
    /// boundary and the sheet closes around the object. A room is looked at from inside, so the
    /// fill is seeded in the middle of the room instead and the sheet forms on every surface
    /// facing that seed. Run the prop way, a room would be wrapped on its outer shell and you
    /// would be standing in an unfilmed box.
    ///
    /// Seeding it that way also gets the thing worth having: the walls, the floor and everything
    /// standing on it come out as <b>one</b> sheet, not as separately wrapped objects, because
    /// from a seed in the middle of the room they are all one surface. The film runs up a chair
    /// leg, across the floor and into the corner without a seam.
    ///
    /// Resolution is the real constraint here and it is worth knowing why. The film's creases are
    /// shader noise sampled in object space, so they do not need mesh density -- but the sheet's
    /// shape and its gap channel do, and a room is fifty-odd square metres of surface to cover.
    /// At the chair's own 5 mm voxels this room would come out well over a million triangles.
    /// 20 mm is the compromise: flat walls lose nothing, and the chair spindles come out blobbier
    /// than they do in <see cref="BuildMembranePropScene"/>, which bakes that chair alone.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; [-clothOut &lt;dir&gt;] [-roomVoxel 0.02]
    ///   -executeMethod CompositeBody.Avatar.Cloth.EditorTools.BuildMembraneRoomScene.Run
    /// </summary>
    public static class BuildMembraneRoomScene
    {
        const string k_ScenePath = "Assets/_Scenes/MembraneRoom.unity";
        const string k_AnimScenePath = "Assets/_Scenes/MembraneRoomAnimated.unity";
        const string k_ChairPath = "Assets/_models/rustic-chair/source/chair/chair_low.obj";

        const string k_FilmMeshDir = "Assets/_models/PropMembrane";
        const string k_FilmMeshPath = k_FilmMeshDir + "/Room_MembraneWrap.asset";
        const string k_FilmMaterialPath = "Assets/Materials/MembraneProp.mat";
        const string k_AnimMaterialPath = "Assets/Materials/MembraneRoomAnimated.mat";
        const string k_BodyMaterialPath = "Assets/Materials/MembranePropBody.mat";

        /// <summary>Frames of the preview sweep, and the span of seconds they cover.</summary>
        const int k_Frames = 10;
        const float k_CycleSeconds = 6f;

        /// <summary>Points along the growth to capture. Not evenly spaced: the front covers the
        /// near half of the room in the first third of the ramp, so even steps would spend most
        /// of the sweep on a room that is already finished.</summary>
        static readonly float[] k_RevealSweep = { 0.08f, 0.20f, 0.35f, 0.55f, 0.80f, 1.00f };

        // Interior clear dimensions. Deliberately small: every extra square metre of wall is
        // several thousand more triangles in the film, and nothing about the look needs a hall.
        const float k_RoomWidth = 3.0f;
        const float k_RoomDepth = 2.4f;
        const float k_RoomHeight = 2.3f;
        const float k_Thickness = 0.12f;

        const float k_DefaultVoxel = 0.020f;
        const float k_Offset = 0.012f;
        const float k_SpanRadius = 0.025f;
        const int k_SmoothIterations = 12;

        /// <summary>
        /// Where the fill starts, and so which surfaces get filmed. Eye height in the middle of
        /// the floor: the film forms on everything someone standing there can see.
        /// </summary>
        static Vector3 SeedPoint => new Vector3(0f, 1.3f, 0f);

        public static void Run()
        {
            Debug.Log("[MembraneRoom] Starting...");

            var shader = Shader.Find("CompositeBody/VacuumMembrane");
            if (shader == null)
            {
                Debug.LogError("[MembraneRoom] RESULT: FAIL - shader 'CompositeBody/VacuumMembrane' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[MembraneRoom] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[MembraneRoom] RESULT: FAIL - the membrane shader has compile errors.");
                return;
            }

            var animShader = Shader.Find("CompositeBody/VacuumMembraneAnimated");
            if (animShader == null)
            {
                Debug.LogError("[MembraneRoom] RESULT: FAIL - shader 'CompositeBody/VacuumMembraneAnimated' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(animShader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(animShader))
                    Debug.LogError($"[MembraneRoom] Animated shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[MembraneRoom] RESULT: FAIL - the animated membrane shader has compile errors.");
                return;
            }

            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("[MembraneRoom] RESULT: FAIL - URP/Lit not found.");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material bodyMat = LoadBodyMaterial(litShader);
            Material filmMat = LoadFilmMaterial(shader);

            var root = new GameObject("MembraneRoom");

            BuildShell(root.transform, bodyMat);
            BuildFurniture(root.transform, bodyMat);

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                // Everything is inside the film, so its own shadow would land on the film's
                // inside face rather than on the room.
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Mesh source = BuildMembranePropScene.CombineIntoRootSpace(root.transform, renderers);
            if (source == null)
            {
                Debug.LogError("[MembraneRoom] RESULT: FAIL - nothing to wrap.");
                return;
            }

            Debug.Log($"[MembraneRoom] Room built: {renderers.Length} renderers, " +
                      $"{source.vertexCount:N0} verts, bounds {source.bounds.size}.");

            // --- the film ------------------------------------------------------------------
            float voxel = ParseFloatArg("-roomVoxel", k_DefaultVoxel);

            var settings = MembraneWrapBuilder.Settings.Default;
            settings.voxelSize = voxel;
            settings.offset = k_Offset;
            settings.spanRadius = k_SpanRadius;
            settings.smoothIterations = k_SmoothIterations;
            settings.interiorSeed = SeedPoint;

            Mesh film;
            try
            {
                film = MembraneWrapBuilder.Build(source, settings, out string report);
                Debug.Log($"[MembraneRoom] wrap:\n{report}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MembraneRoom] RESULT: FAIL - the wrap threw: {e}");
                return;
            }

            if (film == null)
            {
                Debug.LogError("[MembraneRoom] RESULT: FAIL - the wrap produced no film.");
                return;
            }

            film.name = "Room_MembraneWrap";
            Directory.CreateDirectory(k_FilmMeshDir);
            if (AssetDatabase.LoadAssetAtPath<Mesh>(k_FilmMeshPath) != null) AssetDatabase.DeleteAsset(k_FilmMeshPath);
            AssetDatabase.CreateAsset(film, k_FilmMeshPath);

            MeshFilter filmFilter = BuildMembranePropScene.WrapStatic(root.transform, renderers[0], film, filmMat);
            var filmRenderer = filmFilter.GetComponent<MeshRenderer>();

            Debug.Log($"[MembraneRoom] Film: {film.vertexCount:N0} verts, " +
                      $"{film.triangles.Length / 3:N0} triangles at {voxel * 1000f:F0}mm voxels.");

            // --- the still scene ---------------------------------------------------------------
            BuildEnvironment(out Camera wide, out Camera detail);

            Directory.CreateDirectory("Assets/_Scenes");
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MembraneRoom] Saved the film to {k_FilmMeshPath} and the still scene to {k_ScenePath}");

            string outDir = GetArg("-clothOut");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, "room_wide.png"));
                RenderClothPreview.RenderCameraToFile(detail, Path.Combine(outDir, "room_detail.png"));

                filmMat.SetFloat("_ShowContact", 1f);
                RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, "room_contact.png"));
                filmMat.SetFloat("_ShowContact", 0f);

                EditorUtility.SetDirty(filmMat);
                AssetDatabase.SaveAssets();
            }

            // --- the animated scene ------------------------------------------------------------
            // The same room and the same film mesh, with the moving material on it instead, so
            // the two scenes share one baked asset rather than carrying a copy of 226k triangles
            // each. Saved as a second scene rather than replacing the first: the still version is
            // what the look was signed off against and stays exactly as it was.
            Material animMat = LoadAnimatedMaterial(animShader, filmMat);
            filmRenderer.sharedMaterial = animMat;

            // The sheet grows outward from the middle of the floor -- from where someone
            // standing in the room would be, rather than from a corner. An empty transform
            // rather than a baked-in coordinate, so the origin can be moved to a door or a prop
            // later without rebuilding the film.
            var growFrom = new GameObject("GrowOrigin");
            growFrom.transform.SetParent(root.transform, false);
            growFrom.transform.localPosition = new Vector3(0f, 0.05f, 0f);

            var revealer = filmRenderer.gameObject.AddComponent<MembraneReveal>();
            var rso = new SerializedObject(revealer);
            rso.FindProperty("m_Film").objectReferenceValue = filmRenderer;
            rso.FindProperty("m_GrowFrom").objectReferenceValue = growFrom.transform;
            rso.FindProperty("m_AutoRadius").boolValue = true;
            rso.FindProperty("m_GrowOnEnable").boolValue = true;
            rso.FindProperty("m_FadeInSeconds").floatValue = 8f;
            rso.FindProperty("m_FadeOutSeconds").floatValue = 5f;
            rso.FindProperty("m_Reveal").floatValue = 1f;
            rso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_AnimScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MembraneRoom] Saved the animated scene to {k_AnimScenePath}");

            if (!string.IsNullOrEmpty(outDir))
            {
                for (int f = 0; f < k_Frames; f++)
                {
                    float t = f * k_CycleSeconds / k_Frames;
                    animMat.SetFloat("_ManualTime", t);
                    RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, $"anim_wide_{f:D2}.png"));
                    RenderClothPreview.RenderCameraToFile(detail, Path.Combine(outDir, $"anim_detail_{f:D2}.png"));
                }
                // The growth, driven through the component rather than by setting the material:
                // the component writes a property block, which overrides the material, so a
                // sweep written onto the material would silently render ten identical frames.
                // It also means these renders exercise the thing the scene actually ships with.
                animMat.SetFloat("_ManualTime", 0f);
                foreach (float r in k_RevealSweep)
                {
                    revealer.SetRevealImmediate(r);
                    string tag = Mathf.RoundToInt(r * 100f).ToString("D3");
                    RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, $"grow_{tag}.png"));
                }
                revealer.SetRevealImmediate(1f);

                Debug.Log($"[MembraneRoom] Wrote renders, {k_Frames} animation frames and " +
                          $"{k_RevealSweep.Length} growth frames to {outDir}");
            }

            // Back to real time. Left at a preview frame, the shipped material would be frozen on
            // whatever instant the last render happened to capture.
            animMat.SetFloat("_ManualTime", -1f);
            EditorUtility.SetDirty(animMat);
            AssetDatabase.SaveAssets();

            Debug.Log("[MembraneRoom] RESULT: PASS");
        }

        // ---- the room -----------------------------------------------------------------------

        /// <summary>
        /// Floor, four walls and a ceiling, as slabs with real thickness. Sealed rather than
        /// open-topped, because the fill is what decides where film goes: a gap in the shell
        /// would let it escape to the outside and wrap the building instead of the room.
        /// </summary>
        static void BuildShell(Transform parent, Material body)
        {
            float w = k_RoomWidth, d = k_RoomDepth, h = k_RoomHeight, t = k_Thickness;

            Slab(parent, body, "Floor", new Vector3(0f, -t * 0.5f, 0f), new Vector3(w + t * 2f, t, d + t * 2f));
            Slab(parent, body, "Ceiling", new Vector3(0f, h + t * 0.5f, 0f), new Vector3(w + t * 2f, t, d + t * 2f));

            Slab(parent, body, "Wall_North", new Vector3(0f, h * 0.5f, d * 0.5f + t * 0.5f), new Vector3(w + t * 2f, h, t));
            Slab(parent, body, "Wall_South", new Vector3(0f, h * 0.5f, -d * 0.5f - t * 0.5f), new Vector3(w + t * 2f, h, t));
            Slab(parent, body, "Wall_East", new Vector3(w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d));
            Slab(parent, body, "Wall_West", new Vector3(-w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d));
        }

        static void BuildFurniture(Transform parent, Material body)
        {
            // A table against the far third of the room, with a chair either side of it.
            var table = new GameObject("Table");
            table.transform.SetParent(parent, false);
            table.transform.localPosition = new Vector3(0.1f, 0f, 0.35f);

            const float topW = 1.10f, topD = 0.66f, topT = 0.045f, legT = 0.065f, deskH = 0.73f;

            Slab(table.transform, body, "Top", new Vector3(0f, deskH - topT * 0.5f, 0f), new Vector3(topW, topT, topD));

            float lx = topW * 0.5f - legT * 0.8f, lz = topD * 0.5f - legT * 0.8f;
            float legH = deskH - topT;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -lx : lx;
                float sz = (i & 2) == 0 ? -lz : lz;
                Slab(table.transform, body, $"Leg{i}", new Vector3(sx, legH * 0.5f, sz), new Vector3(legT, legH, legT));
            }

            var chairAsset = AssetDatabase.LoadAssetAtPath<GameObject>(k_ChairPath);
            if (chairAsset == null)
            {
                Debug.LogWarning($"[MembraneRoom] No chair at {k_ChairPath}; the room gets a table and a crate only.");
            }
            else
            {
                PlaceChair(parent, chairAsset, body, "Chair_A", new Vector3(-0.25f, 0f, -0.30f), 0f);
                PlaceChair(parent, chairAsset, body, "Chair_B", new Vector3(0.45f, 0f, 1.00f), 180f);
            }

            // The crate: a flat-sided object against a corner, which is where the film's bridging
            // shows most plainly -- it runs from the crate into the wall rather than down behind it.
            Slab(parent, body, "Crate", new Vector3(-1.20f, 0.225f, -0.85f), new Vector3(0.45f, 0.45f, 0.45f));
        }

        /// <summary>
        /// Drops a chair at a spot on the floor. The OBJ's pivot is at its own centroid and sits
        /// over a metre down the -Z axis, so placing one by its transform alone puts it half
        /// underground and well away from where it was asked for; the offset is measured off the
        /// renderer's bounds instead.
        /// </summary>
        static void PlaceChair(Transform parent, GameObject asset, Material body, string name, Vector3 spot, float yaw)
        {
            var chair = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            chair.name = name;
            PrefabUtility.UnpackPrefabInstance(chair, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            chair.transform.SetParent(parent, false);
            chair.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var renderers = chair.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) return;

            Bounds b = renderers[0].bounds;
            foreach (var r in renderers)
            {
                b.Encapsulate(r.bounds);
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = body;
                r.sharedMaterials = mats;
            }

            chair.transform.position += spot - new Vector3(b.center.x, b.min.y, b.center.z);
        }

        static GameObject Slab(Transform parent, Material body, string name, Vector3 localPos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = body;
            return go;
        }

        // ---- materials and lighting ------------------------------------------------------------

        static Material LoadBodyMaterial(Shader litShader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_BodyMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(litShader) { name = "MembranePropBody" };
                AssetDatabase.CreateAsset(mat, k_BodyMaterialPath);
            }
            mat.SetColor("_BaseColor", new Color(0.085f, 0.082f, 0.088f));
            mat.SetFloat("_Smoothness", 0.14f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadFilmMaterial(Shader shader)
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

            // The contact floor is the one value that has to differ from the avatar's. The wrap
            // never places the sheet closer than its standoff, so leaving the floor at the
            // avatar's 7 mm means film lying flat on a wall still reports a gap and never reads
            // as fully in contact -- the whole room comes out uniformly milky.
            mat.SetFloat("_ContactFloor", k_Offset);
            mat.SetFloat("_ContactRange", 0.020f);
            mat.SetFloat("_ContactSharpness", 1.0f);

            mat.SetColor("_FilmColor", new Color(0.70f, 0.76f, 0.85f));
            mat.SetColor("_FrostColor", new Color(0.88f, 0.91f, 0.96f));
            mat.SetFloat("_ClearAlpha", 0.06f);
            mat.SetFloat("_FrostAlpha", 0.60f);
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

        /// <summary>
        /// The moving material, started as an exact copy of the still one.
        ///
        /// Copied property by property off the still material rather than written out again here,
        /// so the two cannot drift: whatever the still film is tuned to, the animated film is the
        /// same sheet, and the only difference between them is the motion block the still shader
        /// does not have.
        /// </summary>
        static Material LoadAnimatedMaterial(Shader animShader, Material still)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_AnimMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(animShader) { name = "MembraneRoomAnimated" };
                AssetDatabase.CreateAsset(mat, k_AnimMaterialPath);
            }
            else if (mat.shader != animShader)
            {
                mat.shader = animShader;
            }

            int count = ShaderUtil.GetPropertyCount(animShader);
            for (int i = 0; i < count; i++)
            {
                string name = ShaderUtil.GetPropertyName(animShader, i);
                if (!still.HasProperty(name)) continue;

                switch (ShaderUtil.GetPropertyType(animShader, i))
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        mat.SetColor(name, still.GetColor(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        mat.SetFloat(name, still.GetFloat(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        mat.SetVector(name, still.GetVector(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        mat.SetTexture(name, still.GetTexture(name));
                        break;
                }
            }

            // Motion, in metres and seconds. Tuned against this room: the breath is a little
            // under the 12 mm standoff, so at full slack the sheet swells by about as much again
            // as it already stands off the wall, and the ripple is a quarter of that.
            mat.SetFloat("_BreathAmount", 0.016f);
            mat.SetFloat("_BreathSpeed", 0.22f);
            mat.SetFloat("_BreathScale", 1.4f);
            // An exaggeration over the true slope of the swell, which the shader now scales by
            // the breath's own amplitude. 20 reproduces what the previous free-floating 0.9 gave.
            mat.SetFloat("_BreathNormal", 20f);
            mat.SetFloat("_RippleAmount", 0.0035f);
            mat.SetFloat("_RippleSpeed", 1.1f);
            mat.SetFloat("_RippleScale", 13f);
            mat.SetFloat("_CreaseDrift", 0.35f);
            mat.SetFloat("_SlackPower", 1.0f);
            mat.SetFloat("_ManualTime", -1f);
            mat.SetFloat("_ShowContact", 0f);

            // Formation. The radius and origin are left to MembraneReveal, which measures the
            // radius off the film's own bounds and takes the origin from a transform; these are
            // only what the material shows when nothing is driving it. Fully formed by default,
            // so opening the scene without entering play mode shows the finished room.
            mat.SetFloat("_Reveal", 1f);
            mat.SetFloat("_GrowRadius", 0f);
            mat.SetFloat("_GrowSoftness", 0.22f);
            mat.SetFloat("_GrowInflate", 1f);
            mat.SetFloat("_GrowEdgeFrost", 1f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Lit from inside, and with shadows off. The room is sealed, so a shadow-casting key
        /// light would be stopped by the ceiling and the whole interior would render black --
        /// and the film's read comes from its specular and its fresnel, neither of which needs
        /// the shadow map.
        /// </summary>
        static void BuildEnvironment(out Camera wide, out Camera detail)
        {
            var keyGO = new GameObject("Key");
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.7f;
            key.color = new Color(1f, 0.98f, 0.94f);
            key.shadows = LightShadows.None;
            keyGO.transform.rotation = Quaternion.Euler(28f, 215f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.12f, 0.13f, 0.16f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.08f, 0.10f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.05f);
            RenderSettings.fog = false;

            // Standing in one corner, looking across the room at the table and chairs.
            var wideGO = new GameObject("RoomCamera");
            wide = wideGO.AddComponent<Camera>();
            wideGO.AddComponent<UniversalAdditionalCameraData>();
            wide.clearFlags = CameraClearFlags.SolidColor;
            wide.backgroundColor = Color.black;
            wide.fieldOfView = 62f;
            wide.nearClipPlane = 0.02f;
            wideGO.transform.position = new Vector3(-1.25f, 1.55f, -1.00f);
            wideGO.transform.LookAt(new Vector3(0.25f, 0.70f, 0.70f));

            // Close on where a chair meets the floor, which is where one continuous sheet over
            // separate objects is either visible or it is not.
            var detailGO = new GameObject("DetailCamera");
            detail = detailGO.AddComponent<Camera>();
            detailGO.AddComponent<UniversalAdditionalCameraData>();
            detail.clearFlags = CameraClearFlags.SolidColor;
            detail.backgroundColor = Color.black;
            detail.fieldOfView = 40f;
            detail.nearClipPlane = 0.01f;
            detailGO.transform.position = new Vector3(-1.05f, 0.80f, -1.15f);
            detailGO.transform.LookAt(new Vector3(-0.25f, 0.35f, -0.30f));
        }

        // ---- args ---------------------------------------------------------------------------

        static float ParseFloatArg(string name, float fallback)
        {
            string raw = GetArg(name);
            if (string.IsNullOrEmpty(raw)) return fallback;

            if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float value) && value > 0f)
                return value;

            Debug.LogWarning($"[MembraneRoom] Could not read {name} '{raw}'; using {fallback}.");
            return fallback;
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
