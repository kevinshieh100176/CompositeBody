using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Avatar.Skin;

namespace CompositeBody.Avatar.Cloth.EditorTools
{
    /// <summary>
    /// Builds a scene with the Ch36 avatar wearing the tension-transparency second skin, posed
    /// with a bent knee and bent elbows so the effect has something to react to: a figure left
    /// in its bind pose registers zero stretch everywhere and the shader looks broken.
    ///
    /// The garment is the body mesh pushed a few millimetres along its normals, sharing the
    /// body's bone weights and bind poses, so it skins with the avatar and needs no simulation.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; [-clothOut &lt;dir&gt;]
    ///   -executeMethod CompositeBody.Avatar.Cloth.EditorTools.BuildTensionSkinScene.Run
    /// </summary>
    public static class BuildTensionSkinScene
    {
        const string k_ScenePath = "Assets/Scenes/TensionSkinAvatar.unity";
        const string k_SkinMeshPath = "Assets/_models/Ch36_TensionSkin.asset";
        const string k_SkinMaterialPath = "Assets/Materials/TensionSkin.mat";
        const string k_BodyMaterialPath = "Assets/Materials/TensionInnerBody.mat";
        const string k_Prefix = "mixamorig1:";

        // A garment, not a coating: far enough off the body to never let it poke through,
        // close enough to still read as a second skin.
        const float k_ShellOffset = 0.006f;

        public static void Run()
        {
            Debug.Log("[TensionSkin] Starting...");

            var shader = Shader.Find("CompositeBody/TensionSkin");
            if (shader == null)
            {
                Debug.LogError("[TensionSkin] RESULT: FAIL - shader 'CompositeBody/TensionSkin' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[TensionSkin] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[TensionSkin] RESULT: FAIL - shader has compile errors.");
                return;
            }
            Debug.Log("[TensionSkin] Shader compiled with no errors.");

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeAvatarRig.AvatarFbxPath);
            if (fbx == null)
            {
                Debug.LogError("[TensionSkin] RESULT: FAIL - avatar FBX not found.");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            avatar.name = "Ch36_TensionAvatar";
            avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.UnpackPrefabInstance(avatar, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var bodySmr = avatar.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodySmr == null || bodySmr.sharedMesh == null)
            {
                Debug.LogError("[TensionSkin] RESULT: FAIL - no SkinnedMeshRenderer on the avatar.");
                return;
            }

            AssignSkinMaterial(bodySmr);
            PoseForTension(avatar);

            var skinMesh = TensionSkinBuilder.Build(bodySmr.sharedMesh, k_ShellOffset);
            skinMesh.name = "Ch36_TensionSkin";

            if (AssetDatabase.LoadAssetAtPath<Mesh>(k_SkinMeshPath) != null)
                AssetDatabase.DeleteAsset(k_SkinMeshPath);
            Directory.CreateDirectory("Assets/_models");
            AssetDatabase.CreateAsset(skinMesh, k_SkinMeshPath);

            var skinMat = LoadOrCreateMaterial(shader);

            var skinGO = new GameObject("TensionSkin");
            skinGO.transform.SetParent(avatar.transform, false);

            var skinSmr = skinGO.AddComponent<SkinnedMeshRenderer>();
            skinSmr.sharedMesh = skinMesh;
            skinSmr.bones = bodySmr.bones;
            skinSmr.rootBone = bodySmr.rootBone;
            skinSmr.sharedMaterial = skinMat;
            skinSmr.updateWhenOffscreen = true;

            BuildEnvironment(out Camera wide, out Camera knee);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TensionSkin] Skin: {skinMesh.vertexCount} verts, bones={skinSmr.bones.Length}, " +
                      $"restPos channel={skinMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2)}");
            Debug.Log($"[TensionSkin] Saved scene to {k_ScenePath}");

            string outDir = GetArg("-clothOut");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, "skin_wide.png"));
                RenderClothPreview.RenderCameraToFile(knee, Path.Combine(outDir, "skin_knee.png"));

                // Same two framings with the tension read straight out, so the shading can be
                // checked against what the shader actually measured rather than guessed at.
                skinMat.SetFloat("_ShowTension", 1f);
                RenderClothPreview.RenderCameraToFile(wide, Path.Combine(outDir, "skin_wide_tension.png"));
                RenderClothPreview.RenderCameraToFile(knee, Path.Combine(outDir, "skin_knee_tension.png"));
                skinMat.SetFloat("_ShowTension", 0f);
                EditorUtility.SetDirty(skinMat);
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[TensionSkin] RESULT: PASS");
        }

        /// <summary>
        /// Gives the body an actual skin tone. The FBX's own materials import dark, which makes
        /// the whole effect invisible: the garment goes sheer exactly as it should and reveals
        /// another near-black surface, so there is nothing to see through to.
        /// </summary>
        static void AssignSkinMaterial(SkinnedMeshRenderer bodySmr)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_BodyMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "TensionInnerBody" };
                AssetDatabase.CreateAsset(mat, k_BodyMaterialPath);
            }
            mat.SetColor("_BaseColor", new Color(0.72f, 0.48f, 0.38f));
            mat.SetFloat("_Smoothness", 0.26f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            var mats = new Material[bodySmr.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            bodySmr.sharedMaterials = mats;
        }

        static Material LoadOrCreateMaterial(Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_SkinMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(shader) { name = "TensionSkin" };
                AssetDatabase.CreateAsset(mat, k_SkinMaterialPath);
                AssetDatabase.SaveAssets();
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Set the tuned values explicitly rather than relying on shader defaults: an
            // existing .mat keeps whatever values it was created with, so defaults alone make
            // the result depend on whether the asset already existed.
            mat.SetColor("_FabricColor", new Color(0.075f, 0.085f, 0.115f));
            mat.SetColor("_SkinTint", new Color(0.85f, 0.56f, 0.44f));
            mat.SetFloat("_BaseThickness", 1.6f);
            mat.SetFloat("_Density", 2.4f);
            mat.SetFloat("_SheerFloor", 0.05f);
            mat.SetFloat("_TensionGain", 8f);
            mat.SetFloat("_TensionMax", 3f);
            mat.SetFloat("_SkinBleed", 0.25f);
            mat.SetFloat("_KnitScale", 420f);
            mat.SetFloat("_YarnWidth", 0.94f);
            mat.SetFloat("_KnitSoftness", 0.14f);
            mat.SetFloat("_KnitRelief", 0.12f);
            mat.SetFloat("_BunchStrength", 0.55f);
            mat.SetFloat("_BunchScale", 60f);
            mat.SetFloat("_BunchShade", 0.45f);
            mat.SetFloat("_Smoothness", 0.30f);
            mat.SetFloat("_TensionGloss", 0.45f);
            mat.SetFloat("_SpecIntensity", 0.45f);
            mat.SetColor("_SheenColor", new Color(0.56f, 0.55f, 0.57f));
            mat.SetFloat("_FresnelPower", 3.2f);
            mat.SetFloat("_FresnelIntensity", 0.25f);
            mat.SetFloat("_AmbientIntensity", 0.6f);
            mat.SetFloat("_ShowTension", 0f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets(); // SetDirty alone does not write the asset back
            return mat;
        }

        /// <summary>
        /// Bends the joints the effect is meant to show off. The FBX ships in a T-pose, where
        /// every vertex sits exactly at its bind position and the measured stretch is 1.0
        /// everywhere -- the shader would be working perfectly and look like it did nothing.
        /// </summary>
        static void PoseForTension(GameObject avatar)
        {
            Transform Find(string n)
            {
                foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                    if (t.name == k_Prefix + n) return t;
                return null;
            }

            Find("LeftArm")?.Rotate(Vector3.forward, 68f, Space.World);
            Find("RightArm")?.Rotate(Vector3.forward, -68f, Space.World);

            // Elbows folded: the outside of the joint stretches, the crook of it bunches.
            Find("LeftForeArm")?.Rotate(Vector3.up, -72f, Space.World);
            Find("RightForeArm")?.Rotate(Vector3.up, 72f, Space.World);

            // Right knee driven up and folded, which is the case the shader is aimed at.
            Find("RightUpLeg")?.Rotate(Vector3.right, 62f, Space.World);
            Find("RightLeg")?.Rotate(Vector3.right, -95f, Space.World);

            // Standing leg gets a slight bend so it is not a perfectly undeformed control.
            Find("LeftLeg")?.Rotate(Vector3.right, -16f, Space.World);
        }

        static void BuildEnvironment(out Camera wide, out Camera knee)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.19f, 0.20f, 0.22f));
            gm.SetFloat("_Smoothness", 0.15f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = gm;

            var keyGO = new GameObject("Key");
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.8f;
            key.color = new Color(1f, 0.98f, 0.95f);
            key.shadows = LightShadows.Soft;
            // Aimed to travel away from the camera, so the side being photographed is the lit
            // one. Pointed the other way the whole figure renders as a silhouette and the
            // sheer patches have nothing to show through.
            keyGO.transform.rotation = Quaternion.Euler(35f, 200f, 0f);

            var rimGO = new GameObject("Rim");
            var rim = rimGO.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.9f;
            rim.color = new Color(0.72f, 0.80f, 1f);
            rimGO.transform.rotation = Quaternion.Euler(14f, 20f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.37f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.25f, 0.27f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.12f, 0.13f);

            var wideGO = new GameObject("PreviewCamera");
            wide = wideGO.AddComponent<Camera>();
            wideGO.AddComponent<UniversalAdditionalCameraData>();
            wide.clearFlags = CameraClearFlags.SolidColor;
            wide.backgroundColor = new Color(0.12f, 0.14f, 0.16f);
            wide.fieldOfView = 30f;
            wide.nearClipPlane = 0.02f;
            wideGO.transform.position = new Vector3(1.35f, 1.05f, 3.6f);
            wideGO.transform.LookAt(new Vector3(0f, 0.95f, 0f));

            // Close on the raised right knee, where the garment is pulled hardest.
            var kneeGO = new GameObject("KneeCamera");
            knee = kneeGO.AddComponent<Camera>();
            kneeGO.AddComponent<UniversalAdditionalCameraData>();
            knee.clearFlags = CameraClearFlags.SolidColor;
            knee.backgroundColor = new Color(0.12f, 0.14f, 0.16f);
            knee.fieldOfView = 30f;
            knee.nearClipPlane = 0.02f;
            kneeGO.transform.position = new Vector3(0.62f, 0.86f, 1.05f);
            kneeGO.transform.LookAt(new Vector3(0.12f, 0.66f, 0.16f));
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
