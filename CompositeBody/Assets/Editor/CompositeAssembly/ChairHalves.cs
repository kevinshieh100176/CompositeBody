using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CompositeBody.Assembly.EditorTools
{
    /// <summary>
    /// The rustic chair, cut down the middle, as two mesh assets plus the materials and the
    /// socket offset that belong with them.
    ///
    /// Shared by the preview harness and the playable test scene because the cut has to be the
    /// same cut. If each built its own, the one that can be rendered and measured and the one
    /// that can actually be picked up in a headset would be free to drift apart -- and the whole
    /// point of checking the mechanic in a render is that the render is standing in for the
    /// thing a player will hold.
    /// </summary>
    public static class ChairHalves
    {
        const string k_PropPath = "Assets/_models/rustic-chair/source/chair/chair_low.obj";
        const string k_AlbedoPath = "Assets/_models/rustic-chair/textures/chair_albedo.png";
        const string k_NormalPath = "Assets/_models/rustic-chair/textures/chair_normal.png";

        const string k_WoodMaterialPath = "Assets/Materials/RusticChairWood.mat";
        const string k_CutMaterialPath = "Assets/Materials/ChairCutFace.mat";

        const string k_HalfMeshDir = "Assets/_models/rustic-chair/halves";

        public const string LeftMeshPath = k_HalfMeshDir + "/ChairHalf_Left.asset";
        public const string RightMeshPath = k_HalfMeshDir + "/ChairHalf_Right.asset";

        /// <summary>The pair id both scenes author, so a half from one is a half of the other.</summary>
        public const string PairId = "chair_01";

        /// <summary>A chair that imports outside this range is a scale mistake, not a big chair.</summary>
        const float k_MinPlausibleHeight = 0.15f;
        const float k_MaxPlausibleHeight = 4.0f;

        public sealed class Cut
        {
            public Mesh left;
            public Mesh right;

            public Material wood;
            public Material cutFace;

            /// <summary>Where the right half's origin belongs relative to the left half's.</summary>
            public Vector3 socketOffset;

            /// <summary>Each half's new origin, in the uncut chair's space.</summary>
            public Vector3 leftPivot;
            public Vector3 rightPivot;

            /// <summary>The uncut chair's bounds, for checking that the pair reassembles into it.</summary>
            public Bounds sourceBounds;

            /// <summary>Body material then cut face, in submesh order.</summary>
            public Material[] materials => new[] { wood, cutFace };

            /// <summary>
            /// Where to put the anchor half's origin so the assembled chair stands with its feet
            /// on the floor and its centre over <paramref name="footprint"/>. Worth computing
            /// rather than authoring: this OBJ's pivot is at the mesh centroid and sits over a
            /// metre down the -Z axis, so a hand-placed transform puts the chair half underground
            /// and well off to one side.
            /// </summary>
            public Vector3 AnchorStandingAt(Vector3 footprint) => footprint + new Vector3(
                leftPivot.x - sourceBounds.center.x,
                leftPivot.y - sourceBounds.min.y,
                leftPivot.z - sourceBounds.center.z);
        }

        /// <summary>
        /// Cuts the chair and writes both halves out as mesh assets. Returns null and logs the
        /// reason if anything is missing; the caller owns its own RESULT line.
        /// </summary>
        public static Cut Build()
        {
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("[ChairCut] URP/Lit not found.");
                return null;
            }

            var propAsset = AssetDatabase.LoadAssetAtPath<GameObject>(k_PropPath);
            if (propAsset == null)
            {
                Debug.LogError($"[ChairCut] Chair not found at {k_PropPath}");
                return null;
            }

            Mesh source = BakeIntoRootSpace(propAsset);
            if (source == null) return null;

            float height = source.bounds.size.y;
            if (height < k_MinPlausibleHeight || height > k_MaxPlausibleHeight)
            {
                Debug.LogError($"[ChairCut] The chair imports {height:0.###}m tall, which is a scale " +
                               "mistake rather than a chair. Check the model's import scale before cutting it.");
                return null;
            }

            Debug.Log($"[ChairCut] Chair is {source.bounds.size.x:0.###} x {source.bounds.size.y:0.###} " +
                      $"x {source.bounds.size.z:0.###}m.");

            // Down the middle of the chair's own bounds rather than through the model origin.
            // This OBJ happens to be symmetric about x = 0, but a prop whose pivot sits off to
            // one side would otherwise be cut into a sliver and the rest of itself.
            var plane = new Plane(Vector3.right, new Vector3(source.bounds.center.x, 0f, 0f));

            var split = MeshHalfSplitter.Split(source, plane);
            if (split == null)
            {
                Debug.LogError("[ChairCut] The split returned nothing.");
                return null;
            }

            Debug.Log($"[ChairCut] Cut {source.vertexCount:N0} verts / {source.triangles.Length / 3:N0} tris " +
                      $"into L {split.negative.mesh.vertexCount:N0}v (cap {split.negative.capTriangles} tris) + " +
                      $"R {split.positive.mesh.vertexCount:N0}v (cap {split.positive.capTriangles} tris); " +
                      $"{split.clippedTriangles} triangles clipped across {split.capLoops} cut outline(s); " +
                      $"socket offset {split.socketOffset:F4}.");

            if (split.capLoops == 0)
                Debug.LogWarning("[ChairCut] The plane closed no outline -- the halves will be open at the cut.");

            return new Cut
            {
                left = SaveMesh(split.negative.mesh, LeftMeshPath, "ChairHalf_Left"),
                right = SaveMesh(split.positive.mesh, RightMeshPath, "ChairHalf_Right"),
                wood = LoadOrCreateWood(litShader),
                cutFace = LoadOrCreateCutFace(litShader),
                socketOffset = split.socketOffset,
                leftPivot = split.negative.pivot,
                rightPivot = split.positive.pivot,
                sourceBounds = source.bounds,
            };
        }

        /// <summary>
        /// Everything the model is made of, in one mesh in the root's space, with normals and UVs
        /// carried across. The splitter has to interpolate those at the cut, so unlike the
        /// membrane wrap -- which only ever wanted positions -- dropping them here would leave
        /// the new rim unlit and untextured.
        /// </summary>
        static Mesh BakeIntoRootSpace(GameObject propAsset)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(propAsset);
            try
            {
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var uv = new List<Vector2>();
                var triangles = new List<int>();

                foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh m = mf.sharedMesh;
                    if (m == null) continue;

                    Matrix4x4 toRoot = instance.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    int baseIndex = positions.Count;

                    foreach (var v in m.vertices) positions.Add(toRoot.MultiplyPoint3x4(v));

                    Vector3[] n = m.normals;
                    Vector2[] t = m.uv;
                    for (int i = 0; i < m.vertexCount; i++)
                    {
                        normals.Add(n.Length == m.vertexCount
                            ? toRoot.MultiplyVector(n[i]).normalized
                            : Vector3.up);
                        uv.Add(t.Length == m.vertexCount ? t[i] : Vector2.zero);
                    }

                    for (int s = 0; s < m.subMeshCount; s++)
                        foreach (int i in m.GetTriangles(s))
                            triangles.Add(baseIndex + i);
                }

                if (positions.Count == 0)
                {
                    Debug.LogError("[ChairCut] The chair has no mesh data.");
                    return null;
                }

                var combined = new Mesh
                {
                    name = "Chair",
                    indexFormat = positions.Count > 65000
                        ? UnityEngine.Rendering.IndexFormat.UInt32
                        : UnityEngine.Rendering.IndexFormat.UInt16,
                };
                combined.SetVertices(positions);
                combined.SetNormals(normals);
                combined.SetUVs(0, uv);
                combined.SetTriangles(triangles, 0);
                combined.RecalculateBounds();
                return combined;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static Mesh SaveMesh(Mesh mesh, string path, string name)
        {
            Directory.CreateDirectory(k_HalfMeshDir);
            mesh.name = name;

            // Overwritten in place rather than deleted and recreated, so a scene or prefab that
            // already points at this mesh still points at it after a re-cut.
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.SetVertices(mesh.vertices);
            existing.SetNormals(mesh.normals);
            existing.SetUVs(0, mesh.uv);
            existing.subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) existing.SetTriangles(mesh.GetTriangles(s), s);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);

            Object.DestroyImmediate(mesh);
            return existing;
        }

        static Material LoadOrCreateWood(Shader litShader)
        {
            Material mat = LoadOrCreate(k_WoodMaterialPath, litShader, "RusticChairWood");

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(k_AlbedoPath);
            if (albedo == null)
                Debug.LogWarning($"[ChairCut] No albedo at {k_AlbedoPath}; the chair will be flat.");

            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.20f);

            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(k_NormalPath);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreateCutFace(Shader litShader)
        {
            Material mat = LoadOrCreate(k_CutMaterialPath, litShader, "ChairCutFace");

            // Pale and dry against the chair's finished surfaces: sawn timber is lighter than the
            // face of the same board, and that contrast is what makes the cut legible across a
            // room -- which is how far away a player is when they start aiming their half at it.
            mat.SetTexture("_BaseMap", null);
            mat.SetColor("_BaseColor", new Color(0.72f, 0.60f, 0.44f));
            mat.SetFloat("_Smoothness", 0.06f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreate(string path, Shader shader, string name)
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
            return mat;
        }
    }
}
