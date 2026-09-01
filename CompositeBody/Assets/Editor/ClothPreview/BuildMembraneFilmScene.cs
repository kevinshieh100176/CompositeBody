using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Avatar.Skin;

namespace CompositeBody.Avatar.Cloth.EditorTools
{
    /// <summary>
    /// Builds a scene with the Ch36 avatar sealed inside the vacuum-film membrane, lit hard from
    /// the side against black the way the reference photography is, so the streak highlights
    /// along the creases have something to read against.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; [-clothOut &lt;dir&gt;]
    ///   -executeMethod CompositeBody.Avatar.Cloth.EditorTools.BuildMembraneFilmScene.Run
    /// </summary>
    public static class BuildMembraneFilmScene
    {
        const string k_ScenePath = "Assets/Scenes/MembraneFilmAvatar.unity";
        const string k_FilmMeshPath = "Assets/_models/Ch36_MembraneFilm.asset";
        const string k_FilmMaterialPath = "Assets/Materials/VacuumMembrane.mat";
        const string k_BodyMaterialPath = "Assets/Materials/MembraneInnerBody.mat";
        const string k_Prefix = "mixamorig1:";

        // The sheet starts 2 cm off the skin and is never allowed closer than 6 mm, then gets
        // relaxed hard: it is the relaxation, not the offset, that makes it bridge the armpits
        // and the gaps between the fingers instead of shrink-fitting every crevice.
        //
        // The iteration count has to be large. A Laplacian pass only diffuses one edge length,
        // and the body mesh has roughly 1 cm triangles, so spanning a 10 cm armpit needs on the
        // order of a hundred edges of reach -- at a few dozen passes the sheet just sags onto
        // the skin everywhere and the contact channel comes out flat. This is a one-time
        // editor bake, so the passes are cheap in the only currency that matters here.
        const float k_BaseOffset = 0.020f;
        const float k_MinOffset = 0.006f;
        const int k_SmoothIterations = 900;
        const float k_SmoothLambda = 0.60f;

        public static void Run()
        {
            Debug.Log("[Membrane] Starting...");

            var shader = Shader.Find("CompositeBody/VacuumMembrane");
            if (shader == null)
            {
                Debug.LogError("[Membrane] RESULT: FAIL - shader 'CompositeBody/VacuumMembrane' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[Membrane] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[Membrane] RESULT: FAIL - shader has compile errors.");
                return;
            }
            Debug.Log("[Membrane] Shader compiled with no errors.");

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeAvatarRig.AvatarFbxPath);
            if (fbx == null)
            {
                Debug.LogError("[Membrane] RESULT: FAIL - avatar FBX not found.");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            avatar.name = "Ch36_MembraneAvatar";
            avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.UnpackPrefabInstance(avatar, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var bodySmr = avatar.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodySmr == null || bodySmr.sharedMesh == null)
            {
                Debug.LogError("[Membrane] RESULT: FAIL - no SkinnedMeshRenderer on the avatar.");
                return;
            }

            AssignBodyMaterial(bodySmr);

            var filmMesh = MembraneShellBuilder.Build(bodySmr.sharedMesh,
                                                      k_BaseOffset, k_MinOffset,
                                                      k_SmoothIterations, k_SmoothLambda);
            filmMesh.name = "Ch36_MembraneFilm";

            if (AssetDatabase.LoadAssetAtPath<Mesh>(k_FilmMeshPath) != null)
                AssetDatabase.DeleteAsset(k_FilmMeshPath);
            Directory.CreateDirectory("Assets/_models");
            AssetDatabase.CreateAsset(filmMesh, k_FilmMeshPath);

            // Posed after the film is built. The membrane is relaxed against the bind pose, so
            // the contact map it bakes describes the T-pose body; posing afterwards just skins
            // that sheet along with the avatar.
            PoseFigure(avatar);

            var filmMat = LoadOrCreateMaterial(shader);

            var filmGO = new GameObject("MembraneFilm");
            filmGO.transform.SetParent(avatar.transform, false);

            var filmSmr = filmGO.AddComponent<SkinnedMeshRenderer>();
            filmSmr.sharedMesh = filmMesh;
            filmSmr.bones = bodySmr.bones;
            filmSmr.rootBone = bodySmr.rootBone;
            filmSmr.sharedMaterial = filmMat;
            filmSmr.updateWhenOffscreen = true;

            BuildEnvironment(out Camera wide, out Camera torso);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();

            ReportGapRange(filmMesh);
            Debug.Log($"[Membrane] Saved scene to {k_ScenePath}");

            string outDir = GetArg("-clothOut");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, "film_wide.png"));
                RenderClothPreview.RenderCameraToFile(torso, Path.Combine(outDir, "film_torso.png"));

                filmMat.SetFloat("_ShowContact", 1f);
                RenderClothPreview.RenderCameraToFile(torso, Path.Combine(outDir, "film_contact.png"));
                filmMat.SetFloat("_ShowContact", 0f);
                EditorUtility.SetDirty(filmMat);
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[Membrane] RESULT: PASS");
        }

        /// <summary>
        /// The contact channel is the shader's main input, so its spread is worth printing: if
        /// the relaxation did nothing the gaps all sit at the base offset and the film shades
        /// uniformly, which looks like a broken shader rather than a mis-built mesh.
        /// </summary>
        static void ReportGapRange(Mesh filmMesh)
        {
            var gaps = filmMesh.uv2;
            float min = float.MaxValue, max = 0f, sum = 0f;
            foreach (var g in gaps)
            {
                min = Mathf.Min(min, g.x);
                max = Mathf.Max(max, g.x);
                sum += g.x;
            }
            Debug.Log($"[Membrane] Film: {filmMesh.vertexCount} verts, " +
                      $"gap min={min:F4}m mean={sum / gaps.Length:F4}m max={max:F4}m");
        }

        static void AssignBodyMaterial(SkinnedMeshRenderer bodySmr)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_BodyMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "MembraneInnerBody" };
                AssetDatabase.CreateAsset(mat, k_BodyMaterialPath);
            }
            // Mid flesh tone, quite matte: the film supplies all the gloss, and a shiny body
            // underneath competes with the streak highlights that are meant to carry the shot.
            mat.SetColor("_BaseColor", new Color(0.60f, 0.42f, 0.35f));
            mat.SetFloat("_Smoothness", 0.18f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            var mats = new Material[bodySmr.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            bodySmr.sharedMaterials = mats;
        }

        static Material LoadOrCreateMaterial(Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_FilmMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(shader) { name = "VacuumMembrane" };
                AssetDatabase.CreateAsset(mat, k_FilmMaterialPath);
                AssetDatabase.SaveAssets();
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Explicit rather than relying on shader defaults: an existing .mat keeps whatever
            // it was created with, so defaults alone make the result depend on whether the asset
            // happened to exist already.
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
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>
        /// Arms down and slightly forward. The armpits and the inside of the elbows are where
        /// the sheet has to bridge the widest, so they are where the frosting reads.
        /// </summary>
        static void PoseFigure(GameObject avatar)
        {
            Transform Find(string n)
            {
                foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                    if (t.name == k_Prefix + n) return t;
                return null;
            }

            Find("LeftArm")?.Rotate(Vector3.forward, 62f, Space.World);
            Find("RightArm")?.Rotate(Vector3.forward, -62f, Space.World);
            Find("LeftForeArm")?.Rotate(Vector3.up, -46f, Space.World);
            Find("RightForeArm")?.Rotate(Vector3.up, 46f, Space.World);
            Find("Head")?.Rotate(Vector3.right, -12f, Space.World);
        }

        static void BuildEnvironment(out Camera wide, out Camera torso)
        {
            // No ground plane and no ambient sky: the references are all a lit figure floating
            // in black, and any fill light at all washes the streak highlights straight out.
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

            var wideGO = new GameObject("PreviewCamera");
            wide = wideGO.AddComponent<Camera>();
            wideGO.AddComponent<UniversalAdditionalCameraData>();
            wide.clearFlags = CameraClearFlags.SolidColor;
            wide.backgroundColor = Color.black;
            wide.fieldOfView = 28f;
            wide.nearClipPlane = 0.02f;
            wideGO.transform.position = new Vector3(0.55f, 1.15f, 3.5f);
            wideGO.transform.LookAt(new Vector3(0f, 1.05f, 0f));

            // Head and shoulders, which is the framing every one of the references uses.
            var torsoGO = new GameObject("TorsoCamera");
            torso = torsoGO.AddComponent<Camera>();
            torsoGO.AddComponent<UniversalAdditionalCameraData>();
            torso.clearFlags = CameraClearFlags.SolidColor;
            torso.backgroundColor = Color.black;
            torso.fieldOfView = 30f;
            torso.nearClipPlane = 0.02f;
            torsoGO.transform.position = new Vector3(0.42f, 1.42f, 1.45f);
            torsoGO.transform.LookAt(new Vector3(0f, 1.36f, 0f));
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
