using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Avatar.Goo;

namespace CompositeBody.Avatar.Cloth.EditorTools
{
    /// <summary>
    /// Builds a scene with the Ch36 avatar wrapped in the animated goo shell, and optionally
    /// renders a still for inspection.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; [-clothOut shot.png]
    ///   -executeMethod CompositeBody.Avatar.Cloth.EditorTools.BuildGooAvatarScene.Run
    /// </summary>
    public static class BuildGooAvatarScene
    {
        const string k_ScenePath = "Assets/Scenes/GooAvatar.unity";
        const string k_ShellMeshPath = "Assets/_models/Ch36_GooShell.asset";
        const string k_GooMaterialPath = "Assets/Materials/GooJelly.mat";
        const string k_BodyMaterialPath = "Assets/Materials/GooInnerBody.mat";
        const string k_Prefix = "mixamorig1:";

        public static void Run()
        {
            Debug.Log("[Goo] Starting...");

            var shader = Shader.Find("CompositeBody/GooJelly");
            if (shader == null)
            {
                Debug.LogError("[Goo] RESULT: FAIL - shader 'CompositeBody/GooJelly' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[Goo] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[Goo] RESULT: FAIL - shader has compile errors.");
                return;
            }
            Debug.Log("[Goo] Shader compiled with no errors.");

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeAvatarRig.AvatarFbxPath);
            if (fbx == null)
            {
                Debug.LogError("[Goo] RESULT: FAIL - avatar FBX not found.");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            avatar.name = "Ch36_GooAvatar";
            avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.UnpackPrefabInstance(avatar, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var bodySmr = avatar.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodySmr == null || bodySmr.sharedMesh == null)
            {
                Debug.LogError("[Goo] RESULT: FAIL - no SkinnedMeshRenderer on the avatar.");
                return;
            }

            LowerArms(avatar);
            AssignInnerBodyMaterial(bodySmr);

            // --- goo shell -------------------------------------------------------------
            // Thick enough that the wobble actually shows in the silhouette -- at ~3cm the
            // displacement is invisible against a 1.9m body and it just reads as green skin.
            var shellMesh = GooShellBuilder.Build(bodySmr.sharedMesh,
                                                  baseThickness: 0.085f,
                                                  thicknessVariation: 0.5f,
                                                  variationScale: 5f);

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(k_ShellMeshPath);
            if (existing != null) AssetDatabase.DeleteAsset(k_ShellMeshPath);
            AssetDatabase.CreateAsset(shellMesh, k_ShellMeshPath);

            var gooMat = LoadOrCreateMaterial(shader);

            var gooGO = new GameObject("GooShell");
            gooGO.transform.SetParent(avatar.transform, false);

            var gooSmr = gooGO.AddComponent<SkinnedMeshRenderer>();
            gooSmr.sharedMesh = shellMesh;
            gooSmr.bones = bodySmr.bones;
            gooSmr.rootBone = bodySmr.rootBone;
            gooSmr.sharedMaterial = gooMat;
            gooSmr.updateWhenOffscreen = true;

            var binder = gooGO.AddComponent<GooInfluencerBinder>();
            WireHandInfluencers(avatar, binder);

            BuildEnvironment(out Camera cam);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Goo] Shell: {shellMesh.vertexCount} verts, uv2(thickness)={shellMesh.uv2.Length}, bones={gooSmr.bones.Length}");
            Debug.Log($"[Goo] Saved scene to {k_ScenePath}");

            string outPath = GetArg("-clothOut");
            if (!string.IsNullOrEmpty(outPath))
            {
                // Push the deformers now: batch mode may never tick LateUpdate between building
                // the scene and rendering it, which would silently render undeformed goo.
                binder.Apply();
                RenderClothPreview.RenderCameraToFile(cam, outPath);
            }

            Debug.Log("[Goo] RESULT: PASS");
        }

        static Material LoadOrCreateMaterial(Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_GooMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(shader) { name = "GooJelly" };
                AssetDatabase.CreateAsset(mat, k_GooMaterialPath);
                AssetDatabase.SaveAssets();
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Set the tuned values explicitly rather than relying on shader defaults: an
            // existing .mat keeps whatever values it was created with, so defaults alone make
            // the result depend on whether the asset already existed.
            mat.SetColor("_ShallowColor", new Color(0.24f, 0.78f, 0.42f));
            mat.SetColor("_DeepColor", new Color(0.01f, 0.07f, 0.035f));
            mat.SetFloat("_BaseAlpha", 0.24f);
            mat.SetFloat("_EdgeOpacity", 1.1f);
            mat.SetFloat("_Smoothness", 0.965f);
            mat.SetFloat("_SpecIntensity", 2.6f);
            mat.SetFloat("_FresnelPower", 2.6f);
            mat.SetFloat("_FresnelIntensity", 0.85f);
            mat.SetFloat("_TranslucencyPower", 3.5f);
            mat.SetFloat("_TranslucencyIntensity", 1.6f);
            mat.SetFloat("_AmbientIntensity", 0.18f);
            mat.SetFloat("_NoiseScale", 7.0f);
            mat.SetFloat("_FlowSpeed", 0.40f);
            mat.SetFloat("_WobbleAmount", 1.50f);
            mat.SetFloat("_BaseFill", 0.55f);
            mat.SetFloat("_MinGapFrac", 0.04f);
            mat.SetFloat("_RippleScale", 22f);
            mat.SetFloat("_RippleStrength", 0.55f);
            mat.SetFloat("_RippleSpeed", 1.1f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets(); // SetDirty alone does not write the asset back
            return mat;
        }

        static void AssignInnerBodyMaterial(SkinnedMeshRenderer bodySmr)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_BodyMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "GooInnerBody" };
                mat.SetColor("_BaseColor", new Color(0.10f, 0.13f, 0.11f));
                mat.SetFloat("_Smoothness", 0.30f);
                AssetDatabase.CreateAsset(mat, k_BodyMaterialPath);
                AssetDatabase.SaveAssets();
            }
            bodySmr.sharedMaterial = mat;
        }

        static void LowerArms(GameObject avatar)
        {
            Transform Find(string n)
            {
                foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                    if (t.name == k_Prefix + n) return t;
                return null;
            }

            Find("LeftArm")?.Rotate(Vector3.forward, 68f, Space.World);
            Find("RightArm")?.Rotate(Vector3.forward, -68f, Space.World);
            Find("LeftForeArm")?.Rotate(Vector3.forward, 14f, Space.World);
            Find("RightForeArm")?.Rotate(Vector3.forward, -14f, Space.World);
        }

        /// <summary>
        /// Hooks the hands up as touch deformers so the goo dents where they press into it.
        /// </summary>
        static void WireHandInfluencers(GameObject avatar, GooInfluencerBinder binder)
        {
            Transform Find(string n)
            {
                foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                    if (t.name == k_Prefix + n) return t;
                return null;
            }

            var so = new SerializedObject(binder);
            var arr = so.FindProperty("m_Influencers");

            // Kept just above the limb radius: any larger and the goo inflates into obvious
            // spheres around the hands instead of bulging subtly where they press.
            var wanted = new (Transform source, float radius)[]
            {
                (Find("LeftHand"), 0.052f),
                (Find("RightHand"), 0.052f),
                (CreateToucherProbe(), 0.135f), // visible demo object pressing into the chest
            };

            arr.arraySize = wanted.Length;
            for (int i = 0; i < wanted.Length; i++)
            {
                var element = arr.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("source").objectReferenceValue = wanted[i].source;
                element.FindPropertyRelative("radius").floatValue = wanted[i].radius;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A visible ball pushed into the goo at chest height, so the touch deformation is
        /// something you can see rather than take on trust. Delete it in the scene once the
        /// real hands/props are wired up.
        /// </summary>
        static Transform CreateToucherProbe()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            probe.name = "ToucherProbe";
            probe.transform.position = new Vector3(0.10f, 1.30f, 0.16f);
            probe.transform.localScale = Vector3.one * 0.22f;
            Object.DestroyImmediate(probe.GetComponent<Collider>());

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.75f, 0.22f, 0.20f));
            mat.SetFloat("_Smoothness", 0.5f);
            probe.GetComponent<MeshRenderer>().sharedMaterial = mat;

            return probe.transform;
        }

        static void BuildEnvironment(out Camera cam)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.20f, 0.21f, 0.23f));
            gm.SetFloat("_Smoothness", 0.15f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = gm;

            var keyGO = new GameObject("Key");
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.7f;
            key.color = new Color(1f, 0.98f, 0.94f);
            key.shadows = LightShadows.Soft;
            keyGO.transform.rotation = Quaternion.Euler(36f, 28f, 0f);

            // Rim from behind sells the translucency, which is most of the jelly read.
            var rimGO = new GameObject("Rim");
            var rim = rimGO.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 1.1f;
            rim.color = new Color(0.75f, 1f, 0.85f);
            rimGO.transform.rotation = Quaternion.Euler(18f, 205f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.30f, 0.38f, 0.36f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.26f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.10f, 0.11f, 0.11f);

            var camGO = new GameObject("PreviewCamera");
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.16f, 0.17f);
            cam.fieldOfView = 30f;
            // Pulled back to fit the whole figure; at 2.9m the head and feet were cropped.
            camGO.transform.position = new Vector3(1.15f, 1.05f, 4.15f);
            camGO.transform.LookAt(new Vector3(0f, 0.95f, 0f));
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
