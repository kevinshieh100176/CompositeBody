using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Avatar.Vfx;

namespace CompositeBody.Avatar.Cloth.EditorTools
{
    /// <summary>
    /// Builds a scene with the Ch36 avatar shedding particles off its whole surface, then walks
    /// it sideways and renders the result.
    ///
    /// The preview renders twice from a camera that never moves: once standing, once after the
    /// walk. Because the particles simulate in world space, the second frame should show the
    /// body somewhere else entirely with the cloud still hanging where it was shed. That is the
    /// one thing about this effect that a single still cannot show.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; [-clothOut &lt;dir&gt;]
    ///   -executeMethod CompositeBody.Avatar.Cloth.EditorTools.BuildBodyVfxScene.Run
    /// </summary>
    public static class BuildBodyVfxScene
    {
        const string k_ScenePath = "Assets/Scenes/BodyShedVfxAvatar.unity";
        const string k_MoteMaterialPath = "Assets/Materials/TraceMotes.mat";
        const string k_StreakMaterialPath = "Assets/Materials/TraceStreaks.mat";
        const string k_BodyMaterialPath = "Assets/Materials/TraceInnerBody.mat";
        const string k_Prefix = "mixamorig1:";

        const float k_Dt = 1f / 72f;         // Quest frame time, so the emission rates are honest
        const int k_SettleSteps = 380;       // long enough to reach steady state, not just start filling
        const int k_WalkSteps = 120;
        const float k_WalkDistance = 0.90f;

        public static void Run()
        {
            Debug.Log("[BodyVfx] Starting...");

            var shader = Shader.Find("CompositeBody/TraceParticle");
            if (shader == null)
            {
                Debug.LogError("[BodyVfx] RESULT: FAIL - shader 'CompositeBody/TraceParticle' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[BodyVfx] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[BodyVfx] RESULT: FAIL - shader has compile errors.");
                return;
            }
            Debug.Log("[BodyVfx] Shader compiled with no errors.");

            EnsureAvatarMeshReadable();

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeAvatarRig.AvatarFbxPath);
            if (fbx == null)
            {
                Debug.LogError("[BodyVfx] RESULT: FAIL - avatar FBX not found.");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            avatar.name = "Ch36_ShedAvatar";
            avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.UnpackPrefabInstance(avatar, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var bodySmr = avatar.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodySmr == null)
            {
                Debug.LogError("[BodyVfx] RESULT: FAIL - no SkinnedMeshRenderer on the avatar.");
                return;
            }
            AssignBodyMaterial(bodySmr);

            var bones = MapBones(avatar);
            StartPose(bones);

            BuildVfx(avatar, bodySmr, shader, out ParticleSystem motes, out ParticleSystem streaks);
            BuildEnvironment(out Camera cam);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BodyVfx] Saved scene to {k_ScenePath}");

            string outDir = GetArg("-clothOut");
            if (string.IsNullOrEmpty(outDir))
            {
                Debug.Log("[BodyVfx] RESULT: PASS (no -clothOut, skipped render)");
                return;
            }

            Directory.CreateDirectory(outDir);

            motes.Clear(true);
            streaks.Clear(true);
            motes.Play(true);
            streaks.Play(true);

            for (int step = 0; step < k_SettleSteps; step++)
            {
                motes.Simulate(k_Dt, true, false, true);
                streaks.Simulate(k_Dt, true, false, true);
            }

            Debug.Log($"[BodyVfx] Standing: motes={motes.particleCount} streaks={streaks.particleCount}");
            if (motes.particleCount == 0)
            {
                // Almost always the readable-mesh requirement: the skinned-mesh shape fails
                // quietly rather than erroring, so it is worth calling out by name.
                Debug.LogError("[BodyVfx] RESULT: FAIL - the body shape emitted nothing. " +
                               "Check Read/Write Enabled on the avatar mesh.");
                return;
            }

            RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, "shed_standing.png"));

            // --- walk away from it ------------------------------------------------------
            // The camera does not move. In world space the cloud already shed should stay put
            // and the body should walk out of it.
            for (int step = 0; step < k_WalkSteps; step++)
            {
                avatar.transform.position += new Vector3(k_WalkDistance / k_WalkSteps, 0f, 0f);
                motes.Simulate(k_Dt, true, false, true);
                streaks.Simulate(k_Dt, true, false, true);
            }

            Debug.Log($"[BodyVfx] After walk: motes={motes.particleCount} streaks={streaks.particleCount}");
            RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, "shed_after_walk.png"));

            Debug.Log("[BodyVfx] RESULT: PASS");
        }

        /// <summary>
        /// Unity's skinned-mesh particle shape reads vertex data on the CPU, so the source mesh
        /// has to be readable. Without it the shape emits nothing and reports no error at all,
        /// which is a miserable thing to debug -- so the builder turns it on rather than leaving
        /// it as a setup step somebody has to already know about.
        /// </summary>
        static void EnsureAvatarMeshReadable()
        {
            var importer = AssetImporter.GetAtPath(ProbeAvatarRig.AvatarFbxPath) as ModelImporter;
            if (importer == null || importer.isReadable) return;

            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log("[BodyVfx] Enabled Read/Write on the avatar FBX; the skinned-mesh emitter " +
                      "shape cannot read the surface without it. Costs a CPU copy of the mesh.");
        }

        static void BuildVfx(GameObject avatar, SkinnedMeshRenderer body, Shader shader,
                             out ParticleSystem motes, out ParticleSystem streaks)
        {
            // Parented to the avatar so the emitter travels with it. The particles themselves
            // simulate in world space, so this parenting only affects where they are born.
            var vfxGO = new GameObject("BodyShedVfx");
            vfxGO.transform.SetParent(avatar.transform, false);

            motes = CreateSystem(vfxGO.transform, "Motes",
                                 LoadOrCreateMaterial(shader, k_MoteMaterialPath, "TraceMotes", streak: false));
            streaks = CreateSystem(vfxGO.transform, "Streaks",
                                   LoadOrCreateMaterial(shader, k_StreakMaterialPath, "TraceStreaks", streak: true));

            var vfx = vfxGO.AddComponent<BodyShedVfx>();

            var so = new SerializedObject(vfx);
            so.FindProperty("m_Body").objectReferenceValue = body;
            so.FindProperty("m_Motes").objectReferenceValue = motes;
            so.FindProperty("m_Streaks").objectReferenceValue = streaks;
            so.ApplyModifiedPropertiesWithoutUndo();

            vfx.ConfigureSystems();
        }

        static ParticleSystem CreateSystem(Transform parent, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return system;
        }

        static Material LoadOrCreateMaterial(Shader shader, string path, string name, bool streak)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_Streak", streak ? 1f : 0f);
            // Streaks are drawn much fainter than the motes: in the reference they read as
            // hairlines catching the light, and at mote brightness they blow into solid rods.
            mat.SetFloat("_Intensity", streak ? 0.9f : 3.2f);
            mat.SetFloat("_Falloff", streak ? 1.2f : 1.3f);
            mat.SetFloat("_StreakTaper", 1.6f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void AssignBodyMaterial(SkinnedMeshRenderer bodySmr)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_BodyMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "TraceInnerBody" };
                AssetDatabase.CreateAsset(mat, k_BodyMaterialPath);
            }
            // Nearly black. The reference is a bright cloud against nothing, and a lit body
            // competing with it turns the effect into a costume rather than the subject.
            mat.SetColor("_BaseColor", new Color(0.035f, 0.037f, 0.042f));
            mat.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            var mats = new Material[bodySmr.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            bodySmr.sharedMaterials = mats;
        }

        static Dictionary<string, Transform> MapBones(GameObject avatar)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(k_Prefix))
                    map[t.name.Substring(k_Prefix.Length)] = t;
            return map;
        }

        static void StartPose(Dictionary<string, Transform> bones)
        {
            // Arms out of the T-pose and away from the torso, so the silhouette the cloud takes
            // is legible rather than one solid slab.
            if (bones.TryGetValue("LeftArm", out var la)) la.Rotate(Vector3.forward, 52f, Space.World);
            if (bones.TryGetValue("RightArm", out var ra)) ra.Rotate(Vector3.forward, -52f, Space.World);
            if (bones.TryGetValue("LeftForeArm", out var lf)) lf.Rotate(Vector3.up, -32f, Space.World);
            if (bones.TryGetValue("RightForeArm", out var rf)) rf.Rotate(Vector3.up, 32f, Space.World);
        }

        static void BuildEnvironment(out Camera cam)
        {
            var keyGO = new GameObject("Key");
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 0.7f;
            key.color = new Color(0.80f, 0.86f, 1f);
            key.shadows = LightShadows.None;
            keyGO.transform.rotation = Quaternion.Euler(28f, 205f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.03f, 0.033f, 0.04f);
            RenderSettings.fog = false;

            var camGO = new GameObject("PreviewCamera");
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.02f;

            // Wide enough to hold both the shed cloud and the body after it has walked clear of
            // it -- the second render is worthless if either leaves the frame.
            camGO.transform.position = new Vector3(0.45f, 1.10f, 4.20f);
            camGO.transform.LookAt(new Vector3(0.45f, 1.00f, 0f));
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
