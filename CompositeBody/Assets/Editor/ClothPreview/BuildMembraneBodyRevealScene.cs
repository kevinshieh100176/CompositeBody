using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Avatar.Skin;
using CompositeBody.Multiplayer.EditorSetup;

namespace CompositeBody.Avatar.Cloth.EditorTools
{
    /// <summary>
    /// The Ch36 figure with its membrane growing on, for looking at the formation on a body
    /// rather than on a room.
    ///
    /// This exists to check the one part of the reveal that the room never exercises: the film is
    /// a SkinnedMeshRenderer, and two things in the path care. The growth radius is measured off
    /// the film's mesh, which a skinned renderer carries itself rather than on a MeshFilter. And
    /// the growth is measured against the shader's rest positions, which for a skinned film are
    /// the bind pose -- so the front has to stay put on the body as the body moves, which means
    /// the origin is a point in mesh space and not a Transform in the scene.
    ///
    /// Reuses the film already baked for the avatar rather than relaxing a new one; that bake is
    /// the slow part and nothing here changes its inputs.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; [-clothOut &lt;dir&gt;]
    ///   -executeMethod CompositeBody.Avatar.Cloth.EditorTools.BuildMembraneBodyRevealScene.Run
    /// </summary>
    public static class BuildMembraneBodyRevealScene
    {
        const string k_ScenePath = "Assets/_Scenes/MembraneBodyReveal.unity";
        const string k_FilmMeshPath = "Assets/_models/AvatarMembrane/MembraneBody_Ch36_MembraneFilm.asset";
        const string k_StillMaterialPath = "Assets/Materials/MembraneAvatar.mat";
        const string k_BodyMaterialPath = "Assets/Materials/MembraneAvatarBody.mat";

        static readonly float[] k_RevealSweep = { 0.10f, 0.30f, 0.50f, 0.70f, 1.00f };

        public static void Run()
        {
            Debug.Log("[BodyRevealScene] Starting...");

            var animShader = Shader.Find("CompositeBody/VacuumMembraneAnimated");
            if (animShader == null)
            {
                Debug.LogError("[BodyRevealScene] RESULT: FAIL - the animated membrane shader was not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(animShader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(animShader))
                    Debug.LogError($"[BodyRevealScene] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[BodyRevealScene] RESULT: FAIL - the animated membrane shader has compile errors.");
                return;
            }

            var still = AssetDatabase.LoadAssetAtPath<Material>(k_StillMaterialPath);
            if (still == null)
            {
                Debug.LogError($"[BodyRevealScene] RESULT: FAIL - no avatar film material at {k_StillMaterialPath}.");
                return;
            }

            var filmMesh = AssetDatabase.LoadAssetAtPath<Mesh>(k_FilmMeshPath);
            if (filmMesh == null)
            {
                Debug.LogError($"[BodyRevealScene] RESULT: FAIL - no baked avatar film at {k_FilmMeshPath}. " +
                               "Run ConfigureMembraneBody first.");
                return;
            }

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeAvatarRig.AvatarFbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[BodyRevealScene] RESULT: FAIL - avatar FBX not found at {ProbeAvatarRig.AvatarFbxPath}");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var body = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            body.name = "Ch36_MembraneBodyReveal";
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var bodySmr = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodySmr == null || bodySmr.sharedMesh == null)
            {
                Debug.LogError("[BodyRevealScene] RESULT: FAIL - the Ch36 FBX has no skinned mesh.");
                return;
            }

            AssignBodyMaterial(bodySmr);

            // --- the film ------------------------------------------------------------------
            Material filmMat = ConfigureMembraneBodyReveal.LoadOrCreateRevealMaterial(animShader, still);

            var filmGO = new GameObject(ConfigureMembraneBody.FilmName);
            filmGO.transform.SetParent(body.transform, false);

            var filmSmr = filmGO.AddComponent<SkinnedMeshRenderer>();
            filmSmr.sharedMesh = filmMesh;
            filmSmr.bones = bodySmr.bones;
            filmSmr.rootBone = bodySmr.rootBone;
            filmSmr.sharedMaterial = filmMat;
            filmSmr.updateWhenOffscreen = true;
            filmSmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            filmSmr.receiveShadows = false;

            // Up from the soles, in the film's own mesh space.
            Bounds b = filmMesh.bounds;
            var origin = new Vector3(b.center.x, b.min.y, b.center.z);

            var reveal = filmGO.AddComponent<MembraneReveal>();
            var so = new SerializedObject(reveal);
            so.FindProperty("m_Film").objectReferenceValue = filmSmr;
            so.FindProperty("m_GrowFrom").objectReferenceValue = null;
            so.FindProperty("m_GrowFromLocal").vector3Value = origin;
            so.FindProperty("m_AutoRadius").boolValue = true;
            so.FindProperty("m_GrowOnEnable").boolValue = true;
            so.FindProperty("m_FadeInSeconds").floatValue = 3.5f;
            so.FindProperty("m_FadeOutSeconds").floatValue = 2.0f;
            so.FindProperty("m_Reveal").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[BodyRevealScene] Film '{filmMesh.name}': {filmMesh.vertexCount:N0} verts, " +
                      $"bind-pose bounds {b.size}, growing from {origin} upward.");

            BuildEnvironment(out Camera wide, out Camera torso);

            Directory.CreateDirectory("Assets/_Scenes");
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BodyRevealScene] Saved the scene to {k_ScenePath}");

            string outDir = GetArg("-clothOut");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);

                foreach (float r in k_RevealSweep)
                {
                    reveal.SetRevealImmediate(r);
                    string tag = Mathf.RoundToInt(r * 100f).ToString("D3");
                    RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, $"body_grow_{tag}.png"));
                    RenderClothPreview.RenderCameraToFile(torso, Path.Combine(outDir, $"body_torso_{tag}.png"));
                }

                reveal.SetRevealImmediate(1f);
                Debug.Log($"[BodyRevealScene] Wrote {k_RevealSweep.Length} growth frames to {outDir}");
            }

            Debug.Log("[BodyRevealScene] RESULT: PASS");
        }

        static void AssignBodyMaterial(SkinnedMeshRenderer bodySmr)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_BodyMaterialPath);
            if (mat == null)
            {
                Debug.LogWarning($"[BodyRevealScene] No body material at {k_BodyMaterialPath}; leaving the FBX's own.");
                return;
            }

            var mats = new Material[bodySmr.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            bodySmr.sharedMaterials = mats;
            bodySmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// The film scene's lighting: hard key from one side, cool rim from the other, against
        /// black. The same setup the still avatar look was developed in, so the formation can be
        /// judged against those shots rather than against a different room.
        /// </summary>
        static void BuildEnvironment(out Camera wide, out Camera torso)
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

            var wideGO = new GameObject("PreviewCamera");
            wide = wideGO.AddComponent<Camera>();
            wideGO.AddComponent<UniversalAdditionalCameraData>();
            wide.clearFlags = CameraClearFlags.SolidColor;
            wide.backgroundColor = Color.black;
            wide.fieldOfView = 28f;
            wide.nearClipPlane = 0.02f;
            wideGO.transform.position = new Vector3(0.55f, 1.15f, 3.5f);
            wideGO.transform.LookAt(new Vector3(0f, 1.05f, 0f));

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
