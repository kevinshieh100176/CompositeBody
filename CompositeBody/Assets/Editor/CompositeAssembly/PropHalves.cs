using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CompositeBody.Assembly.EditorTools
{
    /// <summary>
    /// Any prop, cut in two, as a pair of mesh assets at real-world size plus the socket offset
    /// that puts them back together.
    ///
    /// The general form of what <see cref="ChairHalves"/> does for the rustic chair. The chair's
    /// version stays as it is because two builders share it and the cut has to be the same cut
    /// in both; this is for the show's own 碎片 beats, which cut a SmallProps model each -- S2-2
    /// the lamp -- and want the same treatment with a different model path.
    ///
    /// IT SCALES BEFORE IT CUTS, which the chair's version does not have to. The chair OBJ
    /// imports in metres; SmallProps models do not agree with each other about units, and O-1
    /// already deals with that by measuring each model's bounds and scaling to an authored size
    /// in metres. Doing that first means the half meshes, the socket offset and the snap radius
    /// are all in metres from the start -- the alternative is two halves that only line up at
    /// one particular transform scale, and a snap radius that silently means something different
    /// per prop.
    /// </summary>
    public static class PropHalves
    {
        /// <summary>What to cut, how big it really is, and where the pieces go.</summary>
        public sealed class Spec
        {
            /// <summary>FBX, OBJ or prefab -- anything that loads as a GameObject.</summary>
            public string modelPath;

            /// <summary>The prop's own material. Submesh 0 of both halves gets this.</summary>
            public string bodyMaterialPath;

            /// <summary>Metres along the model's longest axis. A real measurement, not a guess.</summary>
            public float sizeMetres = 0.4f;

            /// <summary>Folder the two mesh assets are written to.</summary>
            public string meshDir;

            /// <summary>Used for the mesh assets' names and in log lines.</summary>
            public string name;

            /// <summary>Where the cut-face material is written.</summary>
            public string cutFaceMaterialPath;

            /// <summary>
            /// The cut face's colour. Pale and dry against the prop's own surfaces: the inside
            /// of a broken thing is lighter than its outside, and that contrast is what makes
            /// the edge to aim at legible across a room.
            /// </summary>
            public Color cutFaceColour = new(0.74f, 0.70f, 0.64f);

            /// <summary>
            /// The plane's normal, through the middle of the prop's own bounds. Vector3.right
            /// cuts it down the middle as seen from the front.
            /// </summary>
            public Vector3 axis = Vector3.right;

            /// <summary>
            /// Metres to move the plane off the exact middle, and NOT a cosmetic preference.
            ///
            /// A LATHED PROP CUT EXACTLY DOWN THE MIDDLE IS NOT CUT AT ALL. The SmallProps
            /// models are radial shells with an even segment count, so x = 0 falls on a segment
            /// boundary of every piece: no triangle straddles the plane, the splitter finds no
            /// cut edges, it closes no outline, and both halves come back as open shells. In a
            /// headset that is a half-lamp you can see straight through from the side you are
            /// holding it by -- the one side a player looks at the whole time -- and it reads as
            /// the mesh being broken rather than as the prop being in pieces. The rustic chair
            /// hid this: it is pre-split at x = 0 too, so the capping was barely exercised.
            ///
            /// A few millimetres puts the plane mid-segment, which clips real triangles and caps
            /// both faces. The halves then differ by twice this across a prop's whole width --
            /// 5% for the lamp, invisible, and a break is not a bisection anyway.
            /// </summary>
            public float axisOffset;
        }

        public sealed class Cut
        {
            public Mesh negative;
            public Mesh positive;

            public Material body;
            public Material cutFace;

            /// <summary>Where the positive half's origin belongs relative to the negative half's.</summary>
            public Vector3 socketOffset;

            /// <summary>Each half's new origin, in the scaled prop's space.</summary>
            public Vector3 negativePivot;
            public Vector3 positivePivot;

            /// <summary>The uncut prop's bounds at real size, for placing and for checking.</summary>
            public Bounds sourceBounds;

            /// <summary>Body material then cut face, in submesh order.</summary>
            public Material[] materials => new[] { body, cutFace };

            /// <summary>
            /// Where to put the negative half's origin so the assembled prop stands with its
            /// base on the floor and its centre over <paramref name="footprint"/>. Worth
            /// computing rather than authoring: a half's pivot is the centre of its own
            /// geometry, which is nowhere in particular relative to the model's own origin.
            /// </summary>
            public Vector3 StandingAt(Vector3 footprint) => footprint + new Vector3(
                negativePivot.x - sourceBounds.center.x,
                negativePivot.y - sourceBounds.min.y,
                negativePivot.z - sourceBounds.center.z);

            /// <summary>
            /// As <see cref="StandingAt"/>, but with the prop's centre at the point rather than
            /// its base -- for a fragment hanging in the air, which is what the 碎片 beats want.
            /// </summary>
            public Vector3 FloatingAt(Vector3 point) => point + (negativePivot - sourceBounds.center);
        }

        /// <summary>
        /// Cuts the prop and writes both halves out as mesh assets. Returns null and logs the
        /// reason if anything is missing; the caller owns its own RESULT line.
        /// </summary>
        public static Cut Build(Spec spec)
        {
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("[PropCut] URP/Lit not found.");
                return null;
            }

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.modelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"[PropCut] {spec.name}: nothing at {spec.modelPath}");
                return null;
            }

            var body = AssetDatabase.LoadAssetAtPath<Material>(spec.bodyMaterialPath);
            if (body == null)
            {
                Debug.LogError($"[PropCut] {spec.name}: no material at {spec.bodyMaterialPath}");
                return null;
            }

            Mesh source = BakeAtRealSize(modelAsset, spec);
            if (source == null) return null;

            Debug.Log($"[PropCut] {spec.name} is {source.bounds.size.x:0.###} x " +
                      $"{source.bounds.size.y:0.###} x {source.bounds.size.z:0.###} m after scaling.");

            // Through the middle of the prop's OWN bounds rather than through the model origin,
            // which for a SmallProps model is rarely inside the mesh at all -- the lamp's
            // geometry sits most of a metre off its FBX origin. A plane through the origin would
            // cut one of these into a sliver and the rest of itself.
            Vector3 normal = spec.axis.normalized;
            Vector3 centre = source.bounds.center + normal * spec.axisOffset;
            var plane = new Plane(normal, centre);

            MeshHalfSplitter.Result split = MeshHalfSplitter.Split(source, plane);
            if (split == null)
            {
                Debug.LogError($"[PropCut] {spec.name}: the split returned nothing.");
                return null;
            }

            Debug.Log($"[PropCut] {spec.name}: cut {source.vertexCount:N0} verts / " +
                      $"{source.triangles.Length / 3:N0} tris into " +
                      $"-{split.negative.mesh.vertexCount:N0}v (cap {split.negative.capTriangles} tris) + " +
                      $"+{split.positive.mesh.vertexCount:N0}v (cap {split.positive.capTriangles} tris); " +
                      $"{split.clippedTriangles} triangles clipped across {split.capLoops} cut " +
                      $"outline(s); socket offset {split.socketOffset:F4}.");

            if (split.capLoops == 0)
                Debug.LogWarning($"[PropCut] {spec.name}: the plane closed no outline -- the " +
                                 "halves will be open at the cut, and a player looking into one " +
                                 "sees the inside of its far wall.");

            // Created on disk and then imported, in that order: AssetDatabase.CreateAsset into a
            // folder the database has never seen fails, and a folder made the moment before is
            // exactly that folder on a first run.
            if (!Directory.Exists(spec.meshDir))
            {
                Directory.CreateDirectory(spec.meshDir);
                AssetDatabase.Refresh();
            }

            return new Cut
            {
                negative = SaveMesh(split.negative.mesh, $"{spec.meshDir}/{spec.name}Half_A.asset",
                                    $"{spec.name}Half_A"),
                positive = SaveMesh(split.positive.mesh, $"{spec.meshDir}/{spec.name}Half_B.asset",
                                    $"{spec.name}Half_B"),
                body = body,
                cutFace = LoadOrCreateCutFace(litShader, spec),
                socketOffset = split.socketOffset,
                negativePivot = split.negative.pivot,
                positivePivot = split.positive.pivot,
                sourceBounds = source.bounds,
            };
        }

        /// <summary>
        /// Everything the model is made of, in one mesh, in the root's space, uniformly scaled so
        /// its longest axis is <see cref="Spec.sizeMetres"/>. Normals and UVs come across because
        /// the splitter interpolates them at the cut; dropping them would leave the new rim unlit
        /// and untextured.
        /// </summary>
        static Mesh BakeAtRealSize(GameObject modelAsset, Spec spec)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            try
            {
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var uv = new List<Vector2>();
                var triangles = new List<int>();

                foreach (MeshFilter mf in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh m = mf.sharedMesh;
                    if (m == null) continue;

                    Matrix4x4 toRoot = instance.transform.worldToLocalMatrix *
                                       mf.transform.localToWorldMatrix;
                    int baseIndex = positions.Count;

                    foreach (Vector3 v in m.vertices) positions.Add(toRoot.MultiplyPoint3x4(v));

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
                    Debug.LogError($"[PropCut] {spec.name}: the model has no mesh data.");
                    return null;
                }

                // Measured and scaled here rather than on the GameObjects later, so the halves
                // are assets in metres and the socket offset is in metres with them. The factor
                // is logged, so a model that turns out to be in centimetres says so rather than
                // merely looking wrong in a headset.
                Bounds raw = Bound(positions);
                float longest = Mathf.Max(raw.size.x, Mathf.Max(raw.size.y, raw.size.z));
                float scale = longest > 1e-5f ? spec.sizeMetres / longest : 1f;

                Debug.Log($"[PropCut] {spec.name} imports {raw.size.x:0.###} x {raw.size.y:0.###} " +
                          $"x {raw.size.z:0.###}, scaled by {scale:0.####} for " +
                          $"{spec.sizeMetres:0.##} m.");

                if (!Mathf.Approximately(scale, 1f))
                {
                    for (int i = 0; i < positions.Count; i++) positions[i] *= scale;
                }

                var combined = new Mesh
                {
                    name = spec.name,
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

        static Bounds Bound(List<Vector3> positions)
        {
            var bounds = new Bounds(positions[0], Vector3.zero);
            for (int i = 1; i < positions.Count; i++) bounds.Encapsulate(positions[i]);
            return bounds;
        }

        /// <summary>
        /// Overwritten in place rather than deleted and recreated, so a scene or prefab already
        /// pointing at this mesh still points at it after a re-cut.
        /// </summary>
        static Mesh SaveMesh(Mesh mesh, string path, string name)
        {
            mesh.name = name;

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

        static Material LoadOrCreateCutFace(Shader litShader, Spec spec)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(spec.cutFaceMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(spec.cutFaceMaterialPath));
                mat = new Material(litShader) { name = Path.GetFileNameWithoutExtension(spec.cutFaceMaterialPath) };
                AssetDatabase.CreateAsset(mat, spec.cutFaceMaterialPath);
            }
            else if (mat.shader != litShader)
            {
                mat.shader = litShader;
            }

            // OPAQUE, and nothing here touches _Surface or the queue. An alpha-blended material
            // is invisible behind this show's water -- KWS draws through passes that assume the
            // scene is in the depth buffer and in _CameraOpaqueTexture, and a blended one is in
            // neither. See the remarks in BuildO1Grab.
            mat.SetTexture("_BaseMap", null);
            mat.SetColor("_BaseColor", spec.cutFaceColour);
            mat.SetFloat("_Smoothness", 0.08f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
