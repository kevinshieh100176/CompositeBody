using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Bakes a <see cref="RiverSpline"/> into chunked LOD meshes.
    ///
    /// WHY CHUNKS AND NOT ONE MESH. The river is wanted both underfoot and far away, and one mesh
    /// has one LOD level for its whole length -- the section two metres away and the section sixty
    /// metres away would get the same density. Worse, a long thin mesh is never frustum-culled,
    /// because its bounds always intersect the view. Splitting along the length gives each stretch
    /// its own LODGroup and lets culling work.
    ///
    /// WHY V IS IN METRES. UV.y carries distance along the spline rather than 0-1, so a normal map
    /// scrolling in V travels DOWN THE CHANNEL and follows every bend. World-space UVs would flow
    /// in one fixed direction and ignore the river's shape; a normalised V would scroll faster
    /// through narrow stretches than wide ones. Baked here, so the shader gets it for free.
    ///
    /// WHY LATERAL DENSITY IS CONSTANT ACROSS LODS. Only the lengthwise spacing changes between
    /// levels. If the number of vertices across the river differed, two neighbouring chunks that
    /// picked different levels would meet at a seam with mismatched vertex counts -- T-junctions,
    /// and a hairline crack you can see through. Every level also includes the chunk's exact start
    /// and end stations, so the shared edge is identical whichever levels meet there.
    ///
    /// Meshes are written as assets and REUSED IN PLACE on a rebuild rather than deleted and
    /// recreated, so their guids survive. Deleting and recreating a generated asset is how this
    /// project orphaned fourteen references once already.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildRiver.Run
    /// </summary>
    public static class BuildRiver
    {
        const string k_MeshRoot = "Assets/_Generated/River";

        /// <summary>Name of the generated subtree. Everything under it is rebuilt from scratch;
        /// the spline and the curves on the root are never touched.</summary>
        const string k_ChunkRoot = "Chunks";

        // Dense enough that chord-summing a spline is accurate to well under a millimetre over an
        // eighty-metre river, and cheap enough to do at every bake.
        const int k_ArcSamples = 2048;

        [MenuItem("Tools/River/Build Selected River")]
        static void BuildSelected()
        {
            var river = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<RiverSpline>()
                : null;

            if (river == null)
            {
                Debug.LogError("[River] select a GameObject with a RiverSpline on it first.");
                return;
            }

            if (Build(river)) MarkDirty(river);
        }

        [MenuItem("Tools/River/Build All Rivers In Scene")]
        public static void Run()
        {
            Debug.Log("[River] Starting...");

            RiverSpline[] rivers = Object.FindObjectsByType<RiverSpline>(FindObjectsInactive.Include);
            if (rivers.Length == 0)
            {
                Debug.LogError("[River] RESULT: FAIL - no RiverSpline in the open scene.");
                return;
            }

            int built = rivers.Count(Build);

            AssetDatabase.SaveAssets();
            if (built > 0)
            {
                UnityEngine.SceneManagement.Scene scene =
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
            }

            Debug.Log(built == rivers.Length
                ? $"[River] built {built} river(s).\n[River] RESULT: PASS"
                : $"[River] built {built} of {rivers.Length}; see the errors above.\n" +
                  "[River] RESULT: FAIL");
        }

        public static bool Build(RiverSpline river)
        {
            var container = river.GetComponent<SplineContainer>();
            if (container == null || container.Spline == null || container.Spline.Count < 2)
            {
                Debug.LogError($"[River] '{river.name}' needs a SplineContainer with at least " +
                               "two knots.", river);
                return false;
            }

            if (river.material == null)
            {
                Debug.LogError($"[River] '{river.name}' has no material, so every chunk would " +
                               "render magenta. Assign the water material first.", river);
                return false;
            }

            if (river.bedMaterial == null)
            {
                Debug.LogError($"[River] '{river.name}' has no bed material. The water now reads " +
                               "its depth off the bed, so without one the river has nothing to " +
                               "be deep over. Run Tools > River > 1 first.", river);
                return false;
            }

            Spline spline = container.Spline;

            // --- arc length --------------------------------------------------------------------
            // Built by chord-summing dense samples rather than through SplineUtility's distance
            // index units: it is the same approach LightOrbit already uses, it needs only
            // EvaluatePosition, and it cannot be broken by a package API change.
            float[] cumulative = new float[k_ArcSamples];
            Vector3 previous = SplineUtility.EvaluatePosition(spline, 0f);
            for (int i = 1; i < k_ArcSamples; i++)
            {
                Vector3 point = SplineUtility.EvaluatePosition(spline, i / (k_ArcSamples - 1f));
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(previous, point);
                previous = point;
            }

            float total = cumulative[k_ArcSamples - 1];
            if (total < 0.01f)
            {
                Debug.LogError($"[River] '{river.name}' has no length.", river);
                return false;
            }

            // --- chunk layout -----------------------------------------------------------------
            int chunkCount = Mathf.Max(1, Mathf.CeilToInt(total / river.chunkLength));
            float chunkSpan = total / chunkCount;

            int levels = Mathf.Min(
                river.lodSpacing?.Length ?? 0,
                river.lodScreenHeights?.Length ?? 0);
            if (levels < 1)
            {
                Debug.LogError($"[River] '{river.name}' needs at least one LOD level, with a " +
                               "spacing and a screen height for it.", river);
                return false;
            }

            string folder = $"{k_MeshRoot}/{SanitizeName(river.name)}";
            EnsureFolder(folder);

            Transform root = ResetChunkRoot(river);
            var written = new HashSet<string>();
            int totalVerts = 0;

            for (int c = 0; c < chunkCount; c++)
            {
                float from = c * chunkSpan;
                float to = (c + 1) * chunkSpan;

                var chunk = new GameObject($"Chunk_{c:00}");
                chunk.transform.SetParent(root, false);

                var lodGroup = chunk.AddComponent<LODGroup>();
                var lods = new LOD[levels];

                for (int l = 0; l < levels; l++)
                {
                    string waterPath = $"{folder}/chunk{c:00}_lod{l}.asset";
                    string bedPath = $"{folder}/chunk{c:00}_lod{l}_bed.asset";
                    written.Add(waterPath);
                    written.Add(bedPath);

                    Mesh waterMesh = LoadOrCreate(waterPath);
                    Mesh bedMesh = LoadOrCreate(bedPath);

                    totalVerts += Sweep(waterMesh, river, spline, cumulative, total,
                                        from, to, river.lodSpacing[l], false);
                    totalVerts += Sweep(bedMesh, river, spline, cumulative, total,
                                        from, to, river.lodSpacing[l], true);

                    var level = new GameObject($"LOD{l}");
                    level.transform.SetParent(chunk.transform, false);
                    level.AddComponent<MeshFilter>().sharedMesh = waterMesh;

                    var renderer = level.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = river.material;

                    // Water neither casts nor receives: it is a surface in a black void lit by
                    // the orbits, and shadow passes on a long thin mesh buy nothing here.
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;

                    // The bed is a sibling inside the same LOD level, so it switches density with
                    // the water it sits under. If the two ever picked different levels the
                    // waterline would crawl against the shore as you walked toward it.
                    var bedObject = new GameObject($"LOD{l}_Bed");
                    bedObject.transform.SetParent(chunk.transform, false);
                    bedObject.AddComponent<MeshFilter>().sharedMesh = bedMesh;

                    var bedRenderer = bedObject.AddComponent<MeshRenderer>();
                    bedRenderer.sharedMaterial = river.bedMaterial;
                    bedRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    bedRenderer.receiveShadows = true;

                    lods[l] = new LOD(river.lodScreenHeights[l],
                                      new Renderer[] { renderer, bedRenderer });
                }

                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
            }

            PruneStaleMeshes(folder, written);
            AssetDatabase.SaveAssets();

            Debug.Log($"[River] '{river.name}': {total:0.0} m, {chunkCount} chunk(s) of " +
                      $"{chunkSpan:0.0} m, {levels} LOD level(s), {totalVerts} vertices total. " +
                      $"Meshes in {folder}.", river);
            return true;
        }

        /// <summary>
        /// Sweeps the cross-section along one stretch of the river. Returns the vertex count.
        ///
        /// The station list always contains <paramref name="from"/> and <paramref name="to"/>
        /// exactly, at every LOD level -- that is what keeps chunk seams watertight.
        /// </summary>
        static int Sweep(Mesh mesh, RiverSpline river, Spline spline, float[] cumulative,
                         float total, float from, float to, float spacing, bool bed)
        {
            int stations = Mathf.Max(1, Mathf.CeilToInt((to - from) / Mathf.Max(spacing, 0.01f)));
            int across = river.lateralSegments;

            int vertexCount = (stations + 1) * (across + 1);

            var positions = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var tangents = new Vector4[vertexCount];
            var uv0 = new Vector2[vertexCount];
            var uv1 = new Vector4[vertexCount];

            for (int i = 0; i <= stations; i++)
            {
                float distance = Mathf.Lerp(from, to, i / (float)stations);
                float t = TimeAtDistance(cumulative, distance);
                float t01 = Mathf.Clamp01(distance / total);

                Vector3 centre = SplineUtility.EvaluatePosition(spline, t);
                Vector3 flow = ((Vector3)SplineUtility.EvaluateTangent(spline, t)).normalized;
                if (flow.sqrMagnitude < 1e-6f) flow = Vector3.forward;

                // Crossed with world up, so the cross-section stays horizontal however the spline
                // is tilted: a river's surface is level across its width even where it descends.
                Vector3 right = Vector3.Cross(Vector3.up, flow);
                right = right.sqrMagnitude < 1e-6f ? Vector3.right : right.normalized;

                Vector3 normal = Vector3.Cross(flow, right).normalized;

                // Aligned so the normal map's V runs downstream: U is across (the mesh tangent),
                // and the handedness sign makes the bitangent come out along the flow.
                float handedness = Vector3.Dot(Vector3.Cross(normal, right), flow) >= 0f ? 1f : -1f;

                float width = river.WidthAt(t01);
                float flowSpeed = river.flow.Evaluate(t01);
                float foam = Mathf.Clamp01(river.foam.Evaluate(t01));

                // The bed spans wider than the water so there is dry shore behind the waterline.
                float span = bed ? width + 2f * river.bankWidth : width;

                for (int j = 0; j <= across; j++)
                {
                    int v = i * (across + 1) + j;
                    float u = j / (float)across;

                    Vector3 point = centre + right * ((u - 0.5f) * span);

                    // Vertically, not along the surface normal: depth is a plumb measurement, and
                    // on a sloping reach offsetting along the normal would tilt the bed with the
                    // flow and make the water deeper on one bank than the other.
                    if (bed) point += Vector3.up * river.BedHeightAt(t01, u, width);

                    positions[v] = point;
                    normals[v] = normal;
                    tangents[v] = new Vector4(right.x, right.y, right.z, handedness);

                    // V in METRES, so scrolling follows the channel at a constant rate.
                    uv0[v] = new Vector2(u, distance);

                    // Depth is the one profile value that varies ACROSS the river as well as
                    // along it, so it is evaluated per vertex. It replaces the normalised
                    // position that used to sit in w, which nothing read -- t01 is recoverable
                    // from uv0.y if it is ever wanted.
                    uv1[v] = new Vector4(flowSpeed, foam, river.DepthAt(t01, u), width);
                }
            }

            var indices = new int[stations * across * 6];
            int w = 0;
            for (int i = 0; i < stations; i++)
            {
                for (int j = 0; j < across; j++)
                {
                    int a = i * (across + 1) + j;
                    int b = a + 1;
                    int c = a + (across + 1);
                    int d = c + 1;

                    // Wound so the face normal comes out along +up for a flat river.
                    indices[w++] = a; indices[w++] = c; indices[w++] = b;
                    indices[w++] = b; indices[w++] = c; indices[w++] = d;
                }
            }

            mesh.Clear();
            mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.triangles = indices;

            // The bed's real normals come from its own shape, which the swept frame does not
            // describe -- the frame is the water's. The water keeps the swept normals because the
            // shader perturbs them anyway and they are exactly right for a flat surface.
            if (bed) mesh.RecalculateNormals();

            mesh.RecalculateBounds();

            EditorUtility.SetDirty(mesh);
            return vertexCount;
        }

        /// <summary>
        /// Spline parameter at a given arc length, by binary search through the cumulative table
        /// and a linear step inside the bracketing pair.
        /// </summary>
        static float TimeAtDistance(float[] cumulative, float distance)
        {
            int last = cumulative.Length - 1;
            if (distance <= 0f) return 0f;
            if (distance >= cumulative[last]) return 1f;

            int low = 0;
            int high = last;
            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (cumulative[mid] <= distance) low = mid; else high = mid;
            }

            float span = cumulative[high] - cumulative[low];
            float frac = span > 1e-6f ? (distance - cumulative[low]) / span : 0f;
            return (low + frac) / last;
        }

        // --- scene and asset housekeeping -------------------------------------------------------

        static Transform ResetChunkRoot(RiverSpline river)
        {
            Transform existing = river.transform.Find(k_ChunkRoot);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var root = new GameObject(k_ChunkRoot);
            root.transform.SetParent(river.transform, false);
            return root.transform;
        }

        /// <summary>
        /// Reuses the asset already at the path so its guid survives a rebuild; only creates one
        /// when there is nothing there.
        /// </summary>
        static Mesh LoadOrCreate(string path)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;

            mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>
        /// Removes meshes from a previous bake that this one no longer produces -- a river made
        /// shorter leaves chunks behind otherwise.
        /// </summary>
        static void PruneStaleMeshes(string folder, HashSet<string> keep)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!keep.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
        }

        static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string path = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{path}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(path, parts[i]);
                path = next;
            }
        }

        static string SanitizeName(string name)
        {
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        }

        static void MarkDirty(RiverSpline river)
        {
            EditorSceneManager.MarkSceneDirty(river.gameObject.scene);
        }
    }
}
