using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Avatar.Cloth.EditorTools;
using CompositeBody.Multiplayer;

namespace CompositeBody.Assembly.EditorTools
{
    /// <summary>
    /// Builds a scene with one object split into two halves and renders the states the
    /// interaction passes through: what each player sees while the halves are apart, the moment
    /// before they snap, and the assembled result.
    ///
    /// The point is that the interesting part of this feature is asymmetric -- the two players
    /// are looking at different things at the same moment -- and that is exactly what cannot be
    /// checked from inside one headset. Rendering both viewpoints side by side from the same
    /// scene state is the only cheap way to see whether the ghost reads as "someone else's" and
    /// still reads as a chair.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; [-compositeOut &lt;dir&gt;]
    ///   -executeMethod CompositeBody.Assembly.EditorTools.BuildCompositeHalvesScene.Run
    /// </summary>
    public static class BuildCompositeHalvesScene
    {
        const string k_ScenePath = "Assets/_Scenes/CompositeHalvesDemo.unity";
        const string k_GhostMaterialPath = "Assets/Materials/GhostHalf.mat";
        const string k_SolidMaterialPath = "Assets/Materials/CompositeHalfSolid.mat";
        const string k_PairId = "chair_01";

        // The chair spans this much in x, split down the middle. Each half's pivot sits at the
        // centre of its own geometry, so the socket offset below is a real offset rather than
        // zero -- a demo where the two pivots coincide would not exercise the snap at all.
        const float k_HalfWidth = 0.25f;
        const float k_SocketOffset = k_HalfWidth;

        public static void Run()
        {
            Debug.Log("[Composite] Starting...");

            var ghostShader = Shader.Find("CompositeBody/GhostHalf");
            if (ghostShader == null)
            {
                Debug.LogError("[Composite] RESULT: FAIL - shader 'CompositeBody/GhostHalf' not found.");
                return;
            }
            if (ShaderUtil.ShaderHasError(ghostShader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(ghostShader))
                    Debug.LogError($"[Composite] Shader {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[Composite] RESULT: FAIL - shader has compile errors.");
                return;
            }
            Debug.Log("[Composite] Shader compiled with no errors.");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ghostMat = LoadOrCreateGhostMaterial(ghostShader);
            var solidMat = LoadOrCreateSolidMaterial();

            var halfA = BuildHalf("ChairHalf_Left", -1f, solidMat);
            var halfB = BuildHalf("ChairHalf_Right", 1f, solidMat);

            var socket = new GameObject("AssemblySocket").transform;
            socket.SetParent(halfA.transform, false);
            socket.localPosition = new Vector3(k_SocketOffset, 0f, 0f);

            var compositeA = Configure(halfA, PlayerRole.Player1, true, socket, ghostMat);
            var compositeB = Configure(halfB, PlayerRole.Player2, false, null, ghostMat);

            var ghostA = halfA.GetComponent<RoleGhost>();
            var ghostB = halfB.GetComponent<RoleGhost>();

            BuildEnvironment(out Camera cam);

            Directory.CreateDirectory("Assets/_Scenes");
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Composite] Saved scene to {k_ScenePath}");

            string outDir = GetArg("-compositeOut");
            if (string.IsNullOrEmpty(outDir))
            {
                Debug.Log("[Composite] RESULT: PASS (no -compositeOut, skipped render)");
                return;
            }
            Directory.CreateDirectory(outDir);

            // Halves apart. Same world state rendered twice, once per player, because the whole
            // premise is that the two of them do not see the same room.
            SetSeparation(halfA, halfB, 0.55f);

            ghostA.SetGhosted(false);
            ghostB.SetGhosted(true);
            RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, "composite_p1_apart.png"));

            ghostA.SetGhosted(true);
            ghostB.SetGhosted(false);
            RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, "composite_p2_apart.png"));

            // Just short of the snap radius, from Player 1's side: the last frame before the
            // halves become one, and the frame where the ghost has to read clearly enough for a
            // player to aim with.
            SetSeparation(halfA, halfB, 0.14f);
            ghostA.SetGhosted(false);
            ghostB.SetGhosted(true);
            RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, "composite_p1_near.png"));

            compositeA.AssembleLocally(compositeB);
            Debug.Log($"[Composite] Assembled: anchorAssembled={compositeA.isAssembled} " +
                      $"followerAssembled={compositeB.isAssembled} " +
                      $"followerParent={halfB.transform.parent?.name ?? "(none)"} " +
                      $"followerGhosted={ghostB.isGhosted}");

            RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, "composite_merged.png"));

            if (!compositeA.isAssembled || !compositeB.isAssembled || ghostB.isGhosted)
            {
                Debug.LogError("[Composite] RESULT: FAIL - the pair did not end up assembled and solid for both.");
                return;
            }

            Debug.Log("[Composite] RESULT: PASS");
        }

        /// <summary>Pushes the two halves symmetrically apart, measured between their pivots.</summary>
        static void SetSeparation(GameObject halfA, GameObject halfB, float gap)
        {
            float half = (k_SocketOffset + gap) * 0.5f;
            halfA.transform.position = new Vector3(-half, 0f, 0f);
            halfB.transform.position = new Vector3(half, 0f, 0f);
        }

        /// <summary>
        /// Half a chair built from boxes. Deliberately not an art asset: the feature has to work
        /// on whatever a designer splits in two, and a primitive chair makes it obvious whether
        /// the seam lands where the socket says it should.
        /// </summary>
        static GameObject BuildHalf(string name, float sign, Material solid)
        {
            var root = new GameObject(name);

            // Local x runs half of k_HalfWidth either side of the pivot; the outer edge is the end
            // this half sits on, which is what sign picks out.
            AddBox(root, "Seat", new Vector3(0f, 0.45f, 0f), new Vector3(k_HalfWidth, 0.05f, 0.5f), solid);
            AddBox(root, "Back", new Vector3(0f, 0.70f, -0.225f), new Vector3(k_HalfWidth, 0.45f, 0.05f), solid);
            AddBox(root, "LegFront", new Vector3(sign * 0.085f, 0.215f, 0.20f), new Vector3(0.05f, 0.43f, 0.05f), solid);
            AddBox(root, "LegBack", new Vector3(sign * 0.085f, 0.215f, -0.20f), new Vector3(0.05f, 0.43f, 0.05f), solid);

            return root;
        }

        static void AddBox(GameObject parent, string name, Vector3 localPos, Vector3 size, Material mat)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent.transform, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Colliders would only get in the way here; nothing in this scene simulates.
            Object.DestroyImmediate(box.GetComponent<Collider>());
        }

        static CompositeHalf Configure(GameObject go, PlayerRole role, bool isAnchor, Transform socket, Material ghostMat)
        {
            // RoleGhost first: it caches the solid materials the first time it is asked to swap,
            // and it has to see the real ones rather than the ghost.
            var ghost = go.AddComponent<RoleGhost>();
            var ghostSo = new SerializedObject(ghost);
            ghostSo.FindProperty("m_GhostMaterial").objectReferenceValue = ghostMat;

            // Through SerializedObject rather than SetSolidFor, so the authored value is what
            // gets written into the saved scene and not just what this session happens to hold.
            var solidFor = ghostSo.FindProperty("m_SolidForRoles");
            solidFor.arraySize = 1;
            solidFor.GetArrayElementAtIndex(0).intValue = (int)role;
            ghostSo.ApplyModifiedPropertiesWithoutUndo();

            var half = go.AddComponent<CompositeHalf>();
            var so = new SerializedObject(half);
            so.FindProperty("m_PairId").stringValue = k_PairId;
            so.FindProperty("m_OwnedBy").intValue = (int)role;
            so.FindProperty("m_IsAnchor").boolValue = isAnchor;
            so.FindProperty("m_SnapRadius").floatValue = 0.25f;
            so.FindProperty("m_Ghost").objectReferenceValue = ghost;
            if (socket != null) so.FindProperty("m_AssemblySocket").objectReferenceValue = socket;
            so.ApplyModifiedPropertiesWithoutUndo();

            return half;
        }

        static Material LoadOrCreateGhostMaterial(Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_GhostMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(shader) { name = "GhostHalf" };
                AssetDatabase.CreateAsset(mat, k_GhostMaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Cool and cold against the warm neutral of the solid half, so which one is yours is
            // legible at a glance rather than something to work out.
            mat.SetColor("_GhostColor", new Color(0.42f, 0.72f, 1f));
            mat.SetFloat("_FillAlpha", 0.05f);
            mat.SetFloat("_RimPower", 4f);
            mat.SetFloat("_RimIntensity", 1.5f);
            mat.SetFloat("_EdgeAlpha", 0.9f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Material LoadOrCreateSolidMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_SolidMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CompositeHalfSolid" };
                AssetDatabase.CreateAsset(mat, k_SolidMaterialPath);
            }
            mat.SetColor("_BaseColor", new Color(0.52f, 0.44f, 0.35f));
            mat.SetFloat("_Smoothness", 0.18f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void BuildEnvironment(out Camera cam)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
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
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.02f;

            // Three-quarter view. Straight on, the split runs down the centre line and the seam
            // is the one thing you cannot see; off to one side, both halves keep their depth and
            // the gap between them is legible.
            camGO.transform.position = new Vector3(1.85f, 1.45f, 2.45f);
            camGO.transform.LookAt(new Vector3(0f, 0.5f, 0f));
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
