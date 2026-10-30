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
    /// Cuts the rustic chair down the middle and renders the states the assembly interaction
    /// passes through: what each player sees while the halves are apart, the moment before they
    /// snap, and the assembled result.
    ///
    /// The point is that the interesting part of this feature is asymmetric -- the two players
    /// are looking at different things at the same moment -- and that is exactly what cannot be
    /// checked from inside one headset. Rendering both viewpoints side by side from the same
    /// scene state is the only cheap way to see whether the ghost reads as "someone else's" and
    /// still reads as a chair.
    ///
    /// It used to build its chair out of boxes, on the grounds that a primitive makes it obvious
    /// where the seam landed. That was true but it tested the easy case: a box has one flat
    /// cross-section and no silhouette to lose. Whether the mechanic reads at all depends on how
    /// half of an actual object looks held out at arm's length, which only the real prop answers.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; [-compositeOut &lt;dir&gt;]
    ///   -executeMethod CompositeBody.Assembly.EditorTools.BuildCompositeHalvesScene.Run
    /// </summary>
    public static class BuildCompositeHalvesScene
    {
        const string k_ScenePath = "Assets/_Scenes/CompositeHalvesDemo.unity";
        const string k_GhostMaterialPath = "Assets/Materials/GhostHalf.mat";

        /// <summary>
        /// How far the reassembled pair may differ from the uncut chair before the seam is wrong.
        /// </summary>
        const float k_SeamTolerance = 0.002f;

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

            var chair = ChairHalves.Build();
            if (chair == null)
            {
                Debug.LogError("[Composite] RESULT: FAIL - the chair could not be cut.");
                return;
            }

            Material ghostMat = LoadOrCreateGhostMaterial(ghostShader);

            var halfA = BuildHalf("ChairHalf_Left", chair.left, chair.materials);
            var halfB = BuildHalf("ChairHalf_Right", chair.right, chair.materials);

            var socket = new GameObject("AssemblySocket").transform;
            socket.SetParent(halfA.transform, false);

            // Straight from the geometry. The two pivots are the centres of their own halves, so
            // the gap between them is whatever the chair's shape made it, not a number to tune.
            socket.localPosition = chair.socketOffset;

            var compositeA = Configure(halfA, PlayerRole.Player1, true, socket, ghostMat);
            var compositeB = Configure(halfB, PlayerRole.Player2, false, null, ghostMat);

            var ghostA = halfA.GetComponent<RoleGhost>();
            var ghostB = halfB.GetComponent<RoleGhost>();

            Vector3 anchorHome = chair.AnchorStandingAt(Vector3.zero);

            BuildEnvironment(chair.sourceBounds.size, out Camera cam, out Camera detail);

            Directory.CreateDirectory("Assets/_Scenes");
            SetSeparation(halfA, halfB, anchorHome, chair.socketOffset, 0.55f);
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Composite] Saved scene to {k_ScenePath}");

            string outDir = GetArg("-compositeOut");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                WarmUp(cam, outDir);

                // The cut face, which none of the three-quarter views below ever shows: it faces
                // the other half the whole time. It is also the surface a player aims at, and the
                // only place a mis-stitched cap would be visible, so it gets its own frame.
                SetSeparation(halfA, halfB, anchorHome, chair.socketOffset, 1.2f);
                ghostA.SetGhosted(false);
                ghostB.SetGhosted(true);
                AimAtCutFace(detail, halfA.transform, chair.left.bounds);
                RenderClothPreview.RenderCameraToFile(detail, Path.Combine(outDir, "composite_cut_face.png"));

                SetSeparation(halfA, halfB, anchorHome, chair.socketOffset, 0.55f);

                // Halves apart. The same world state rendered twice, once per player, because the
                // whole premise is that the two of them do not see the same room.
                ghostA.SetGhosted(false);
                ghostB.SetGhosted(true);
                RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, "composite_p1_apart.png"));

                ghostA.SetGhosted(true);
                ghostB.SetGhosted(false);
                RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, "composite_p2_apart.png"));

                // Just short of the snap radius, from Player 1's side: the last frame before the
                // halves become one, and the frame where the ghost has to read clearly enough for
                // a player to aim their own half at it.
                SetSeparation(halfA, halfB, anchorHome, chair.socketOffset, 0.14f);
                ghostA.SetGhosted(false);
                ghostB.SetGhosted(true);
                RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, "composite_p1_near.png"));
            }

            // --- the interaction itself ------------------------------------------------------
            SetSeparation(halfA, halfB, anchorHome, chair.socketOffset, 0.14f);
            compositeA.AssembleLocally(compositeB);

            Debug.Log($"[Composite] Assembled: anchorAssembled={compositeA.isAssembled} " +
                      $"followerAssembled={compositeB.isAssembled} " +
                      $"followerParent={halfB.transform.parent?.name ?? "(none)"} " +
                      $"followerGhosted={ghostB.isGhosted}");

            if (!string.IsNullOrEmpty(outDir))
                RenderClothPreview.RenderCameraToFile(cam, Path.Combine(outDir, "composite_merged.png"));

            if (!compositeA.isAssembled || !compositeB.isAssembled || ghostB.isGhosted)
            {
                Debug.LogError("[Composite] RESULT: FAIL - the pair did not end up assembled and solid for both.");
                return;
            }

            if (!CheckSeam(halfA, halfB, chair.sourceBounds.size)) return;

            Debug.Log("[Composite] RESULT: PASS");
        }

        /// <summary>
        /// The reassembled pair has to occupy the same box the uncut chair did. This is the one
        /// check that actually tests the socket: a seam a centimetre out still looks like a chair
        /// in a render, and still reports itself assembled, but it is a chair with a step in it.
        /// </summary>
        static bool CheckSeam(GameObject halfA, GameObject halfB, Vector3 sourceSize)
        {
            Bounds assembled = halfA.GetComponent<Renderer>().bounds;
            assembled.Encapsulate(halfB.GetComponent<Renderer>().bounds);

            Vector3 drift = assembled.size - sourceSize;
            if (Mathf.Abs(drift.x) > k_SeamTolerance ||
                Mathf.Abs(drift.y) > k_SeamTolerance ||
                Mathf.Abs(drift.z) > k_SeamTolerance)
            {
                Debug.LogError($"[Composite] RESULT: FAIL - the assembled pair measures {assembled.size} " +
                               $"against the uncut chair's {sourceSize} (out by {drift}). The assembly " +
                               "socket is not where the cut was.");
                return false;
            }

            Debug.Log($"[Composite] Seam checks out: assembled {assembled.size} vs uncut {sourceSize} " +
                      $"(out by {drift.magnitude * 1000f:0.##}mm).");
            return true;
        }

        #region Scene

        /// <summary>Pushes the two halves symmetrically apart, measured across the cut.</summary>
        static void SetSeparation(GameObject halfA, GameObject halfB, Vector3 anchorHome,
                                  Vector3 socketOffset, float gap)
        {
            var spread = new Vector3(gap * 0.5f, 0f, 0f);
            halfA.transform.SetPositionAndRotation(anchorHome - spread, Quaternion.identity);
            halfB.transform.SetPositionAndRotation(anchorHome + socketOffset + spread, Quaternion.identity);
        }

        static GameObject BuildHalf(string name, Mesh mesh, Material[] materials)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            // One slot per submesh: the body takes the chair's own wood, the cut face takes raw
            // sawn timber. A single material across both would hide the seam, which is the thing
            // this scene exists to look at.
            go.GetComponent<MeshRenderer>().sharedMaterials = mesh.subMeshCount > 1
                ? materials
                : new[] { materials[0] };

            return go;
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
            so.FindProperty("m_PairId").stringValue = ChairHalves.PairId;
            so.FindProperty("m_OwnedBy").intValue = (int)role;
            so.FindProperty("m_IsAnchor").boolValue = isAnchor;
            so.FindProperty("m_SnapRadius").floatValue = 0.12f;
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

            // Cool and cold against the warm wood of the solid half, so which one is yours is
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

        /// <summary>
        /// Throws away one frame before the real renders. Ambient and shadows are applied a frame
        /// late in a fresh batch-mode session, so without this the first image or two come back
        /// lit by the default sky -- a warm grey wash that buries a warm wood prop, and which
        /// reads as a lighting bug in the scene rather than as a frame taken too early.
        /// </summary>
        static void WarmUp(Camera cam, string outDir)
        {
            string path = Path.Combine(outDir, "_warmup.png");
            RenderClothPreview.RenderCameraToFile(cam, path);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// Frames the anchor half's cut face from just inside the gap, close enough to read the
        /// sawn surfaces and far enough back to keep the whole silhouette.
        /// </summary>
        static void AimAtCutFace(Camera detail, Transform anchor, Bounds halfBounds)
        {
            // The cut is the half's +x face, the anchor being the negative side of the plane.
            Vector3 face = anchor.position + new Vector3(halfBounds.max.x, halfBounds.center.y, halfBounds.center.z);

            detail.transform.position = face + new Vector3(0.62f, 0.30f, 0.52f);
            detail.transform.LookAt(face);
        }

        static void BuildEnvironment(Vector3 chairSize, out Camera cam, out Camera detail)
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

            // An empty scene inherits no sky, so the ambient set above is the only environment
            // light there is -- and it does not reach the renderer until something asks for it.
            RenderSettings.skybox = null;
            DynamicGI.UpdateEnvironment();

            cam = MakeCamera("PreviewCamera", 34f);

            // Three-quarter view, pulled back from the chair's own size rather than from a
            // hardcoded distance: the halves travel more than half a metre apart in this scene,
            // and a frame tight enough for the merged state loses them in the separated one.
            var focus = new Vector3(0f, chairSize.y * 0.5f, 0f);
            float reach = Mathf.Max(chairSize.x, chairSize.y) * 2.6f + 0.9f;
            cam.transform.position = focus + new Vector3(0.62f, 0.52f, 1f).normalized * reach;
            cam.transform.LookAt(focus);

            // Aimed per shot by AimAtCutFace, and wider than the main camera because it works
            // from inside the gap between the halves and has no room to back off.
            detail = MakeCamera("CutFaceCamera", 46f);
        }

        static Camera MakeCamera(string name, float fieldOfView)
        {
            var go = new GameObject(name);
            var cam = go.AddComponent<Camera>();
            go.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.045f, 0.048f, 0.055f);
            cam.fieldOfView = fieldOfView;
            cam.nearClipPlane = 0.02f;
            return cam;
        }

        #endregion

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
