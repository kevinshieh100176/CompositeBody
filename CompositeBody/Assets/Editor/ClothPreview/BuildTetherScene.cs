using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Multiplayer;

namespace CompositeBody.Avatar.Cloth.EditorTools
{
    /// <summary>
    /// Builds a scene with two avatars joined by the elastic tether, and renders it at a series
    /// of separations so the sag-to-taut progression can actually be checked. A single still of
    /// a rope tells you nothing about whether it responds to distance, which is the entire
    /// behaviour being asked for here.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; [-clothOut &lt;dir&gt;]
    ///   -executeMethod CompositeBody.Avatar.Cloth.EditorTools.BuildTetherScene.Run
    /// </summary>
    public static class BuildTetherScene
    {
        const string k_ScenePath = "Assets/Scenes/PlayerTetherDemo.unity";
        const string k_CordMaterialPath = "Assets/Materials/TetherCord.mat";
        const string k_BodyMaterialPath = "Assets/Materials/TetherBody.mat";
        const string k_Prefix = "mixamorig1:";

        const float k_RestLength = 2f;
        const float k_Dt = 1f / 72f;
        const int k_SettleSteps = 150; // enough for the spring to come to rest at each distance

        // Chosen to straddle the interesting range: deep sag, moderate sag, just short of
        // straight, and stretched past rest.
        static readonly float[] k_Separations = { 0.7f, 1.4f, 1.9f, 2.4f };

        public static void Run()
        {
            Debug.Log("[Tether] Starting...");

            var shader = Shader.Find("CompositeBody/TetherCord");
            if (shader == null)
            {
                Debug.LogError("[Tether] RESULT: FAIL - shader 'CompositeBody/TetherCord' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[Tether] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[Tether] RESULT: FAIL - shader has compile errors.");
                return;
            }
            Debug.Log("[Tether] Shader compiled with no errors.");

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeAvatarRig.AvatarFbxPath);
            if (fbx == null)
            {
                Debug.LogError("[Tether] RESULT: FAIL - avatar FBX not found.");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bodyMat = LoadOrCreateBodyMaterial();
            var playerA = SpawnPlayer(fbx, "PlayerA", bodyMat, out Transform anchorA);
            var playerB = SpawnPlayer(fbx, "PlayerB", bodyMat, out Transform anchorB);
            if (anchorA == null || anchorB == null)
            {
                Debug.LogError("[Tether] RESULT: FAIL - could not find the waist bone to anchor to.");
                return;
            }

            var tether = BuildTether(shader, anchorA, anchorB);
            BuildEnvironment(out Camera cam);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Tether] Saved scene to {k_ScenePath}");

            string outDir = GetArg("-clothOut");
            if (string.IsNullOrEmpty(outDir))
            {
                Debug.Log("[Tether] RESULT: PASS (no -clothOut, skipped render)");
                return;
            }
            Directory.CreateDirectory(outDir);

            foreach (float separation in k_Separations)
            {
                playerA.transform.position = new Vector3(-separation * 0.5f, 0f, 0f);
                playerB.transform.position = new Vector3(separation * 0.5f, 0f, 0f);

                for (int step = 0; step < k_SettleSteps; step++)
                    tether.Tick(k_Dt);

                float sag = MeasuredSag(tether, anchorA, anchorB);
                Debug.Log($"[Tether] separation={separation:F2}m rest={k_RestLength:F2}m " +
                          $"sag={sag:F3}m tension={tether.tension:F2}");

                string tag = separation.ToString("0.00").Replace(".", "");
                RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, $"tether_{tag}.png"));
            }

            Debug.Log("[Tether] RESULT: PASS");
        }

        /// <summary>
        /// Reads the sag back off the rendered curve rather than off the component's internal
        /// field, so the log is describing the cord that was actually drawn.
        /// </summary>
        static float MeasuredSag(PlayerTether tether, Transform a, Transform b)
        {
            var line = tether.GetComponent<LineRenderer>();
            if (line.positionCount == 0) return 0f;

            var mid = line.GetPosition(line.positionCount / 2);
            float chordY = (a.position.y + b.position.y) * 0.5f;
            return chordY - mid.y;
        }

        static GameObject SpawnPlayer(GameObject fbx, string name, Material bodyMat, out Transform anchor)
        {
            var player = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            player.name = name;
            PrefabUtility.UnpackPrefabInstance(player, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            anchor = null;
            foreach (var t in player.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == k_Prefix + "Hips") anchor = t;

                // Arms out of the T-pose so the silhouettes read as two people rather than two
                // crosses, and so nothing overlaps the cord.
                if (t.name == k_Prefix + "LeftArm") t.Rotate(Vector3.forward, 68f, Space.World);
                if (t.name == k_Prefix + "RightArm") t.Rotate(Vector3.forward, -68f, Space.World);
            }

            foreach (var r in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = bodyMat;
                r.sharedMaterials = mats;
            }

            return player;
        }

        static PlayerTether BuildTether(Shader shader, Transform anchorA, Transform anchorB)
        {
            var go = new GameObject("PlayerTether");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = LoadOrCreateCordMaterial(shader);

            var tether = go.AddComponent<PlayerTether>();

            var so = new SerializedObject(tether);
            so.FindProperty("m_AnchorA").objectReferenceValue = anchorA;
            so.FindProperty("m_AnchorB").objectReferenceValue = anchorB;
            so.FindProperty("m_RestLength").floatValue = k_RestLength;
            so.ApplyModifiedPropertiesWithoutUndo();

            tether.ConfigureLine();
            return tether;
        }

        static Material LoadOrCreateCordMaterial(Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_CordMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(shader) { name = "TetherCord" };
                AssetDatabase.CreateAsset(mat, k_CordMaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.SetColor("_CordColor", new Color(0.45f, 0.03f, 0.04f));
            mat.SetColor("_HotColor", new Color(1f, 0.20f, 0.14f));
            mat.SetFloat("_Intensity", 1.9f);
            mat.SetFloat("_CoreWidth", 0.45f);
            mat.SetFloat("_EdgeFalloff", 1.8f);
            mat.SetFloat("_TensionGlow", 1.7f);
            mat.SetFloat("_Tension", 0f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Material LoadOrCreateBodyMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_BodyMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "TetherBody" };
                AssetDatabase.CreateAsset(mat, k_BodyMaterialPath);
            }
            // Desaturated and mid-dark, so the red cord is the only saturated thing in frame.
            mat.SetColor("_BaseColor", new Color(0.20f, 0.21f, 0.24f));
            mat.SetFloat("_Smoothness", 0.22f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void BuildEnvironment(out Camera cam)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.085f, 0.088f, 0.10f));
            gm.SetFloat("_Smoothness", 0.10f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = gm;

            var keyGO = new GameObject("Key");
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.35f;
            key.color = new Color(0.92f, 0.94f, 1f);
            key.shadows = LightShadows.Soft;
            keyGO.transform.rotation = Quaternion.Euler(38f, 200f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.18f, 0.22f);
            RenderSettings.ambientEquatorColor = new Color(0.11f, 0.12f, 0.14f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.06f);
            RenderSettings.fog = false;

            var camGO = new GameObject("PreviewCamera");
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.045f, 0.048f, 0.055f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.02f;

            // Straight on from the side and a little low: the sag is a vertical drop, and any
            // camera looking down the cord's length hides the whole effect.
            camGO.transform.position = new Vector3(0f, 1.15f, 7.2f);
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
