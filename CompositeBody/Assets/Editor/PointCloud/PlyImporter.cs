using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace CompositeBody.PointClouds.EditorTools
{
    /// <summary>
    /// Imports a .ply as a Unity asset, so a scan can be dropped into the project and used
    /// without a conversion step through Blender or MeshLab.
    ///
    /// This is a <see cref="ScriptedImporter"/> rather than a menu command on purpose. The
    /// 真人 figures arrive as a sequence of captures that get recleaned and recropped as the
    /// poses are chosen, and every one of those revisions has to land in the project without
    /// anyone remembering to re-run a tool. Registering the extension means a changed file is
    /// reimported by the asset pipeline, keeps its settings in its .meta, and keeps every
    /// reference to it intact.
    ///
    /// Three topologies, because the clouds are wanted for different things:
    ///
    /// <list type="bullet">
    /// <item><b>Points</b> -- one vertex per point. The cheapest, and the one VFX Graph can
    /// sample directly (Position/Sample Mesh read vertices), so it is also the route to a
    /// point cache. Renders as single pixels, which at the density these scans reach is very
    /// nearly solid: 1 mm spacing subtends about 1.25 px at a metre on a 2K-per-eye headset.</item>
    /// <item><b>Quads</b> -- four vertices per point, carrying a corner offset in UV so the
    /// shader can billboard them at a world-space size. Four times the memory, and the only
    /// option that lets the points keep an apparent size as the viewer walks up to them.</item>
    /// <item><b>Triangles</b> -- the mesh as authored, for the files that have faces. The room
    /// scans are wanted this way; the figures are not.</item>
    /// </list>
    ///
    /// The reader handles what the scanners here actually emit, which is more than one thing:
    /// ascii and both binary byte orders, float and double positions, uchar and float colour,
    /// properties in any order, and a face element whose list counts and indices have their own
    /// types. A loader that assumed one layout would silently return an empty cloud for the
    /// others, which is a bad afternoon to debug.
    /// </summary>
    [ScriptedImporter(k_Version, "ply")]
    public class PlyImporter : ScriptedImporter
    {
        const int k_Version = 1;

        /// <summary>Beyond this, quad expansion is refused rather than quietly eating a gigabyte.</summary>
        const int k_MaxQuadPoints = 4_000_000;

        public enum Topology
        {
            /// <summary>One vertex per point. For VFX Graph, point caches, and dense clouds.</summary>
            Points,
            /// <summary>Billboarded quads at a world-space size. Four times the vertices.</summary>
            Quads,
            /// <summary>The authored faces. Only available on a .ply that has them.</summary>
            Triangles,
        }

        public enum Recenter
        {
            /// <summary>Leave coordinates exactly as they are on disk.</summary>
            None,
            /// <summary>Put the bounding-box centre on the origin.</summary>
            BoundsCenter,
            /// <summary>Centre horizontally, and put the lowest point on y = 0.</summary>
            FeetOnFloor,
        }

        [SerializeField, Tooltip("How the points become a mesh.")]
        Topology m_Topology = Topology.Points;

        [SerializeField, Tooltip("Multiplies every coordinate. Scans are metric already; this is for fixing a scan whose scale is wrong.")]
        float m_Scale = 1f;

        [SerializeField, Tooltip("Negate Z, converting a right-handed scan to Unity's left-handed space. Leave off for a file already exported for Unity -- flipping twice mirrors it back.")]
        bool m_FlipZ;

        [SerializeField, Tooltip("Where the origin ends up.")]
        Recenter m_Recenter = Recenter.None;

        [SerializeField, Min(0), Tooltip("Thin to about this many points, keeping an even spread. 0 imports all of them.")]
        int m_MaxPoints;

        [SerializeField, Tooltip("Treat the file's colours as sRGB and convert to linear. On in a linear project, which this one is.")]
        bool m_ConvertColorSpace = true;

        [SerializeField, Tooltip("Measure how crowded each point's neighbourhood is and store it in UV1.x, with the point's luminance in UV1.y. CompositeBody/PointCloudFro reads both; the plain PointCloud shader ignores them.")]
        bool m_ComputeDensity;

        [SerializeField, Min(0.001f), Tooltip("Radius the density is counted over. Roughly ten times the point spacing is a good start.")]
        float m_DensityRadius = 0.01f;

        [SerializeField, Tooltip("Estimate a surface normal per point from its neighbours. Required by CompositeBody/PointCloudLit -- without it there is nothing for a light to fall on. Costs a few seconds at import.")]
        bool m_EstimateNormals;

        [SerializeField, Min(0.002f), Tooltip("Neighbourhood the normal is fitted over. Too small and it fits noise; too large and it rounds off fingers. Roughly fifteen times the point spacing.")]
        float m_NormalRadius = 0.015f;

        [SerializeField, Tooltip("Material created alongside the mesh. Left empty, the CompositeBody/PointCloud shader is used.")]
        Shader m_Shader;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            PlyData data;
            try
            {
                data = PlyReader.Read(ctx.assetPath);
            }
            catch (Exception e)
            {
                ctx.LogImportError($"[Ply] {Path.GetFileName(ctx.assetPath)}: {e.Message}");
                return;
            }

            if (data.positions.Length == 0)
            {
                ctx.LogImportError($"[Ply] {Path.GetFileName(ctx.assetPath)} has no vertices.");
                return;
            }

            Topology topology = m_Topology;
            if (topology == Topology.Triangles && data.indices == null)
            {
                ctx.LogImportWarning($"[Ply] {Path.GetFileName(ctx.assetPath)} has no faces; " +
                                     "importing as Points instead.");
                topology = Topology.Points;
            }

            int[] keep = SelectPoints(data.positions.Length, topology == Topology.Triangles);
            Vector3[] positions = Transform(data.positions, keep);
            Color32[] colors = Recolor(data.colors, keep, data.positions.Length);

            Vector2[] attributes = m_ComputeDensity ? MeasureDensity(positions, colors) : null;
            Vector3[] normals = m_EstimateNormals ? EstimateNormals(positions) : null;

            Mesh mesh;
            switch (topology)
            {
                case Topology.Quads:
                    if (positions.Length > k_MaxQuadPoints)
                    {
                        ctx.LogImportError($"[Ply] {positions.Length:N0} points is too many to expand " +
                                           $"to quads (limit {k_MaxQuadPoints:N0}). Use Points, or set Max Points.");
                        return;
                    }
                    mesh = BuildQuads(positions, colors, attributes, normals);
                    break;
                case Topology.Triangles:
                    mesh = BuildTriangles(positions, colors, data.indices);
                    break;
                default:
                    mesh = BuildPoints(positions, colors, attributes, normals);
                    break;
            }

            mesh.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            mesh.RecalculateBounds();

            var shader = m_Shader != null ? m_Shader : Shader.Find("CompositeBody/PointCloud");
            Material material = null;
            if (shader != null)
            {
                material = new Material(shader) { name = mesh.name + " Points" };
            }
            else
            {
                ctx.LogImportWarning("[Ply] CompositeBody/PointCloud not found; the mesh imports " +
                                     "without a material.");
            }

            // The main object is a GameObject so the asset can be dragged straight into a
            // scene. The mesh is a sub-asset rather than the main one, which keeps it
            // referenceable on its own -- VFX Graph wants the Mesh, not the GameObject.
            var go = new GameObject(mesh.name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;

            ctx.AddObjectToAsset("mesh", mesh);
            if (material != null) ctx.AddObjectToAsset("material", material);
            ctx.AddObjectToAsset("root", go);
            ctx.SetMainObject(go);

            string shape = topology == Topology.Triangles
                ? $"{(data.indices.Length / 3):N0} triangles"
                : $"{positions.Length:N0} points as {topology}";
            ctx.LogImportWarning($"[Ply] {mesh.name}: {shape}, " +
                                 $"{mesh.vertexCount:N0} vertices, " +
                                 $"bounds {mesh.bounds.size.x:0.00} x {mesh.bounds.size.y:0.00} x " +
                                 $"{mesh.bounds.size.z:0.00} m" +
                                 (data.hasColor ? "" : ", no colour in file"));
        }

        /// <summary>
        /// Indices to keep, spread evenly through the file rather than taken from the front.
        ///
        /// A stride is used instead of a random sample because scanners write points in
        /// acquisition order: taking the first N of a sweep keeps one side of the subject and
        /// discards the rest. A stride is also stable, so the same file thins the same way on
        /// every reimport.
        /// </summary>
        int[] SelectPoints(int count, bool keepAll)
        {
            if (keepAll || m_MaxPoints <= 0 || m_MaxPoints >= count) return null;

            int stride = Mathf.Max(2, Mathf.CeilToInt(count / (float)m_MaxPoints));
            var keep = new List<int>(count / stride + 1);
            for (int i = 0; i < count; i += stride) keep.Add(i);
            return keep.ToArray();
        }

        Vector3[] Transform(Vector3[] source, int[] keep)
        {
            int n = keep?.Length ?? source.Length;
            var result = new Vector3[n];
            float flip = m_FlipZ ? -1f : 1f;

            for (int i = 0; i < n; i++)
            {
                Vector3 p = source[keep?[i] ?? i];
                result[i] = new Vector3(p.x * m_Scale, p.y * m_Scale, p.z * m_Scale * flip);
            }

            if (m_Recenter == Recenter.None) return result;

            var bounds = new Bounds(result[0], Vector3.zero);
            for (int i = 1; i < n; i++) bounds.Encapsulate(result[i]);

            Vector3 offset = m_Recenter == Recenter.FeetOnFloor
                ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)
                : bounds.center;

            for (int i = 0; i < n; i++) result[i] -= offset;
            return result;
        }

        Color32[] Recolor(Color32[] source, int[] keep, int sourceCount)
        {
            int n = keep?.Length ?? sourceCount;
            var result = new Color32[n];

            if (source == null)
            {
                for (int i = 0; i < n; i++) result[i] = new Color32(200, 200, 200, 255);
                return result;
            }

            for (int i = 0; i < n; i++)
            {
                Color32 c = source[keep?[i] ?? i];
                // Scanner colour is sRGB. Unity's vertex colours are consumed raw by the
                // shader, so in a linear project the conversion has to happen somewhere, and
                // at import time it happens once rather than per vertex per frame.
                result[i] = m_ConvertColorSpace
                    ? (Color32)((Color)c).linear
                    : c;
                result[i].a = c.a;
            }
            return result;
        }

        /// <summary>
        /// Per-point neighbourhood crowding in x, luminance in y, both 0-1.
        ///
        /// RubenFro drives his cascade off "angle, luminosity as well as a rough calculation on
        /// density". Luminance is free from the colour, but density needs neighbours, which a
        /// vertex shader has no way to ask about -- so it is measured once here and carried in
        /// UV1. What it buys is that thin, sparse parts of a scan come apart before solid ones,
        /// which is the difference between a figure dissolving and a figure falling over.
        ///
        /// Counted on a voxel grid rather than by true radius search: one pass, no tree, and at
        /// this density the 27-cell neighbourhood is a good enough proxy for a sphere.
        /// </summary>
        Vector2[] MeasureDensity(Vector3[] positions, Color32[] colors)
        {
            float cell = Mathf.Max(0.001f, m_DensityRadius);
            var grid = new Dictionary<(int, int, int), int>(positions.Length);

            foreach (Vector3 p in positions)
            {
                var key = (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
                grid.TryGetValue(key, out int n);
                grid[key] = n + 1;
            }

            var counts = new int[positions.Length];
            int highest = 1;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                int cx = Mathf.FloorToInt(p.x / cell), cy = Mathf.FloorToInt(p.y / cell), cz = Mathf.FloorToInt(p.z / cell);
                int total = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (grid.TryGetValue((cx + dx, cy + dy, cz + dz), out int n)) total += n;
                }
                counts[i] = total;
                if (total > highest) highest = total;
            }

            // Normalised against a high percentile rather than the maximum, so one unusually
            // dense cluster does not push the whole body down into the bottom of the range.
            var sorted = (int[])counts.Clone();
            Array.Sort(sorted);
            float reference = Mathf.Max(1, sorted[Mathf.Clamp((int)(sorted.Length * 0.95f), 0, sorted.Length - 1)]);

            var result = new Vector2[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                Color32 c = colors[i];
                float luminance = (c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f) / 255f;
                result[i] = new Vector2(Mathf.Clamp01(counts[i] / reference), luminance);
            }
            return result;
        }

        /// <summary>
        /// A surface normal per point, fitted to its neighbourhood.
        ///
        /// Scans arrive without usable normals -- the files here carry 0,1,0 on every point,
        /// which is the scanner saying it never computed any -- so a lit point cloud has
        /// nothing to light until they are made. The standard construction: take the points
        /// nearby, build their covariance, and the direction of least variance is the one
        /// pointing off the surface.
        ///
        /// Signs are then resolved outward from the centroid. PCA cannot tell inside from
        /// outside, and a cloud with half its normals inverted lights as a mess of dark
        /// patches. For a figure scanned from the outside, away-from-the-middle is right
        /// nearly everywhere; it is wrong deep inside a concavity, which a body standing with
        /// its arms down does not really have.
        /// </summary>
        Vector3[] EstimateNormals(Vector3[] positions)
        {
            float cell = Mathf.Max(0.002f, m_NormalRadius);
            var grid = new Dictionary<(int, int, int), List<int>>(positions.Length / 4 + 1);

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                var key = (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell),
                           Mathf.FloorToInt(p.z / cell));
                if (!grid.TryGetValue(key, out List<int> bucket))
                {
                    bucket = new List<int>(8);
                    grid[key] = bucket;
                }
                bucket.Add(i);
            }

            var centroid = Vector3.zero;
            foreach (Vector3 p in positions) centroid += p;
            centroid /= Mathf.Max(1, positions.Length);

            var normals = new Vector3[positions.Length];
            const int k_MaxNeighbours = 48;   // bounds the cost; more adds nothing to a plane fit
            var neighbours = new Vector3[k_MaxNeighbours];

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                int cx = Mathf.FloorToInt(p.x / cell);
                int cy = Mathf.FloorToInt(p.y / cell);
                int cz = Mathf.FloorToInt(p.z / cell);

                int count = 0;
                for (int dx = -1; dx <= 1 && count < k_MaxNeighbours; dx++)
                for (int dy = -1; dy <= 1 && count < k_MaxNeighbours; dy++)
                for (int dz = -1; dz <= 1 && count < k_MaxNeighbours; dz++)
                {
                    if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int> bucket))
                        continue;
                    // Strided, so a dense cell contributes a spread of its points rather than
                    // whichever ones happen to sit at the front of the list.
                    int stride = Mathf.Max(1, bucket.Count / 8);
                    for (int b = 0; b < bucket.Count && count < k_MaxNeighbours; b += stride)
                        neighbours[count++] = positions[bucket[b]];
                }

                if (count < 4) { normals[i] = (p - centroid).normalized; continue; }

                var mean = Vector3.zero;
                for (int n = 0; n < count; n++) mean += neighbours[n];
                mean /= count;

                float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;
                for (int n = 0; n < count; n++)
                {
                    Vector3 d = neighbours[n] - mean;
                    xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
                    yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
                }

                Vector3 normal = SmallestEigenvector(xx, xy, xz, yy, yz, zz);
                if (Vector3.Dot(normal, p - centroid) < 0f) normal = -normal;
                normals[i] = normal.sqrMagnitude > 1e-12f
                    ? normal.normalized
                    : (p - centroid).normalized;
            }

            return normals;
        }

        /// <summary>
        /// Eigenvector of the smallest eigenvalue of a symmetric 3x3, by Jacobi rotation.
        ///
        /// Jacobi rather than the closed form, because the analytic solution for a symmetric
        /// 3x3 loses precision badly when two eigenvalues are close -- which is exactly a flat
        /// patch of a scan, the commonest case here, and the one where a wrong answer shows up
        /// as shading noise across every flat surface.
        /// </summary>
        static Vector3 SmallestEigenvector(float xx, float xy, float xz,
                                           float yy, float yz, float zz)
        {
            var a = new float[3, 3] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } };
            var v = new float[3, 3] { { 1f, 0f, 0f }, { 0f, 1f, 0f }, { 0f, 0f, 1f } };

            for (int sweep = 0; sweep < 12; sweep++)
            {
                float off = Mathf.Abs(a[0, 1]) + Mathf.Abs(a[0, 2]) + Mathf.Abs(a[1, 2]);
                if (off < 1e-12f) break;

                for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Mathf.Abs(a[p, q]) < 1e-14f) continue;

                    float theta = (a[q, q] - a[p, p]) / (2f * a[p, q]);
                    float t = theta == 0f
                        ? 1f
                        : Mathf.Sign(theta) / (Mathf.Abs(theta) + Mathf.Sqrt(theta * theta + 1f));
                    float c = 1f / Mathf.Sqrt(t * t + 1f);
                    float sn = t * c;

                    for (int k = 0; k < 3; k++)
                    {
                        float akp = a[k, p], akq = a[k, q];
                        a[k, p] = c * akp - sn * akq;
                        a[k, q] = sn * akp + c * akq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        float apk = a[p, k], aqk = a[q, k];
                        a[p, k] = c * apk - sn * aqk;
                        a[q, k] = sn * apk + c * aqk;

                        float vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - sn * vkq;
                        v[k, q] = sn * vkp + c * vkq;
                    }
                }
            }

            int smallest = 0;
            float best = a[0, 0];
            if (a[1, 1] < best) { best = a[1, 1]; smallest = 1; }
            if (a[2, 2] < best) smallest = 2;
            return new Vector3(v[0, smallest], v[1, smallest], v[2, smallest]);
        }

        static Mesh BuildPoints(Vector3[] positions, Color32[] colors, Vector2[] attributes,
                                Vector3[] normals)
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(positions);
            mesh.SetColors(colors);
            if (attributes != null) mesh.SetUVs(1, attributes);
            if (normals != null) mesh.SetNormals(normals);

            var indices = new int[positions.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            mesh.SetIndices(indices, MeshTopology.Points, 0, false);
            return mesh;
        }

        /// <summary>
        /// Four vertices per point, all at the point's position, with the corner stored in UV.
        ///
        /// The quad is not built here -- it is built in the vertex shader, which offsets each
        /// corner in view space so the sprite faces the camera and keeps a world-space size.
        /// Baking the offsets into positions instead would freeze the facing and the size into
        /// the asset, and a cloud reimported every time an artist wanted bigger points is not
        /// a workflow.
        /// </summary>
        static Mesh BuildQuads(Vector3[] positions, Color32[] colors, Vector2[] attributes,
                               Vector3[] normals)
        {
            int n = positions.Length;
            var verts = new Vector3[n * 4];
            var cols = new Color32[n * 4];
            var uvs = new Vector2[n * 4];
            var attr = attributes != null ? new Vector2[n * 4] : null;
            var norms = normals != null ? new Vector3[n * 4] : null;
            var tris = new int[n * 6];

            for (int i = 0; i < n; i++)
            {
                int v = i * 4;
                Vector3 p = positions[i];
                Color32 c = colors[i];

                verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = p;
                cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c;
                uvs[v] = new Vector2(-1f, -1f);
                uvs[v + 1] = new Vector2(1f, -1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(-1f, 1f);
                if (attr != null)
                    attr[v] = attr[v + 1] = attr[v + 2] = attr[v + 3] = attributes[i];
                if (norms != null)
                    norms[v] = norms[v + 1] = norms[v + 2] = norms[v + 3] = normals[i];

                int t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uvs);
            if (attr != null) mesh.SetUVs(1, attr);
            if (norms != null) mesh.SetNormals(norms);
            mesh.SetIndices(tris, MeshTopology.Triangles, 0, false);
            return mesh;
        }

        static Mesh BuildTriangles(Vector3[] positions, Color32[] colors, int[] indices)
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(positions);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.RecalculateNormals();
            return mesh;
        }
    }

    /// <summary>Vertices, colour and faces as they were on disk, before any import settings apply.</summary>
    public sealed class PlyData
    {
        public Vector3[] positions = Array.Empty<Vector3>();
        public Color32[] colors;                 // null when the file carries no colour
        public int[] indices;                    // null when the file has no face element
        public bool hasColor => colors != null;
    }

    /// <summary>
    /// A .ply reader covering the variants the scanners in use actually produce.
    ///
    /// Written by hand because the alternative -- assuming one layout -- fails by returning
    /// nothing rather than by throwing, and because the vertex block is read as one buffer and
    /// indexed by byte offset, which a general-purpose reader built on BinaryReader cannot do
    /// at seven hundred thousand points without being noticeably slow at import.
    /// </summary>
    public static class PlyReader
    {
        enum Format { Ascii, BinaryLittleEndian, BinaryBigEndian }

        struct Property
        {
            public string name;
            public string type;
            public bool isList;
            public string countType;        // list only
            public int offset;              // binary only: bytes from the start of the record
            public int size;
        }

        sealed class Element
        {
            public string name;
            public int count;
            public readonly List<Property> properties = new();
            public int stride;              // binary, fixed-size elements only
            public bool hasList;
        }

        static int SizeOf(string type) => type switch
        {
            "char" or "uchar" or "int8" or "uint8" => 1,
            "short" or "ushort" or "int16" or "uint16" => 2,
            "int" or "uint" or "int32" or "uint32" or "float" or "float32" => 4,
            "double" or "float64" => 8,
            _ => throw new FormatException($"unknown property type '{type}'")
        };

        public static PlyData Read(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);

            Format format = Format.Ascii;
            var elements = new List<Element>();
            Element current = null;
            int headerBytes = ReadHeader(stream, ref format, elements, ref current);

            Element vertex = elements.Find(e => e.name == "vertex");
            if (vertex == null) throw new FormatException("no vertex element");

            var data = new PlyData();
            if (format == Format.Ascii)
            {
                ReadAscii(path, headerBytes, elements, vertex, data);
            }
            else
            {
                ReadBinary(stream, elements, vertex, data, format == Format.BinaryBigEndian);
            }
            return data;
        }

        static int ReadHeader(FileStream stream, ref Format format, List<Element> elements,
                              ref Element current)
        {
            var line = new StringBuilder();
            int bytes = 0;

            string NextLine()
            {
                line.Clear();
                int b;
                while ((b = stream.ReadByte()) != -1)
                {
                    bytes++;
                    if (b == '\n') break;
                    if (b != '\r') line.Append((char)b);
                }
                return line.ToString();
            }

            if (NextLine().Trim() != "ply") throw new FormatException("not a PLY file");

            while (true)
            {
                string text = NextLine();
                if (text.Length == 0 && stream.Position >= stream.Length)
                    throw new FormatException("header never ended");

                string[] parts = text.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "format":
                        format = parts[1] switch
                        {
                            "ascii" => Format.Ascii,
                            "binary_little_endian" => Format.BinaryLittleEndian,
                            "binary_big_endian" => Format.BinaryBigEndian,
                            _ => throw new FormatException($"unknown format '{parts[1]}'")
                        };
                        break;

                    case "element":
                        current = new Element { name = parts[1], count = int.Parse(parts[2]) };
                        elements.Add(current);
                        break;

                    case "property":
                        if (current == null) break;
                        if (parts[1] == "list")
                        {
                            current.properties.Add(new Property
                            {
                                name = parts[4], countType = parts[2], type = parts[3], isList = true
                            });
                            current.hasList = true;
                        }
                        else
                        {
                            int size = SizeOf(parts[1]);
                            current.properties.Add(new Property
                            {
                                name = parts[2], type = parts[1], offset = current.stride, size = size
                            });
                            current.stride += size;
                        }
                        break;

                    case "end_header":
                        return bytes;
                }
            }
        }

        static int IndexOfProperty(Element e, string name) =>
            e.properties.FindIndex(p => p.name == name);

        static void ReadAscii(string path, int headerBytes, List<Element> elements,
                              Element vertex, PlyData data)
        {
            var positions = new Vector3[vertex.count];
            Color32[] colors = null;

            int ix = IndexOfProperty(vertex, "x");
            int iy = IndexOfProperty(vertex, "y");
            int iz = IndexOfProperty(vertex, "z");
            if (ix < 0 || iy < 0 || iz < 0) throw new FormatException("vertex element has no x/y/z");

            int ir = IndexOfProperty(vertex, "red");
            int ig = IndexOfProperty(vertex, "green");
            int ib = IndexOfProperty(vertex, "blue");
            int ia = IndexOfProperty(vertex, "alpha");
            bool hasColor = ir >= 0 && ig >= 0 && ib >= 0;
            bool floatColor = hasColor && vertex.properties[ir].type is "float" or "float32"
                                                                    or "double" or "float64";
            if (hasColor) colors = new Color32[vertex.count];

            var culture = CultureInfo.InvariantCulture;
            using var reader = new StreamReader(path, Encoding.ASCII);

            // Skip the header by line rather than by byte, because StreamReader buffers and
            // seeking the underlying stream would desynchronise it.
            string text;
            while ((text = reader.ReadLine()) != null)
            {
                if (text.Trim() == "end_header") break;
            }

            var faces = new List<int>();
            foreach (Element element in elements)
            {
                bool isVertex = element == vertex;
                bool isFace = element.name == "face";

                for (int i = 0; i < element.count; i++)
                {
                    text = reader.ReadLine();
                    if (text == null)
                        throw new FormatException($"file ended inside '{element.name}' at {i:N0} of {element.count:N0}");

                    string[] f = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length == 0) { i--; continue; }

                    if (isVertex)
                    {
                        positions[i] = new Vector3(
                            float.Parse(f[ix], culture),
                            float.Parse(f[iy], culture),
                            float.Parse(f[iz], culture));

                        if (hasColor)
                        {
                            colors[i] = floatColor
                                ? new Color32(
                                    (byte)Mathf.Clamp(float.Parse(f[ir], culture) * 255f, 0, 255),
                                    (byte)Mathf.Clamp(float.Parse(f[ig], culture) * 255f, 0, 255),
                                    (byte)Mathf.Clamp(float.Parse(f[ib], culture) * 255f, 0, 255),
                                    ia >= 0 ? (byte)Mathf.Clamp(float.Parse(f[ia], culture) * 255f, 0, 255) : (byte)255)
                                : new Color32(
                                    byte.Parse(f[ir], culture),
                                    byte.Parse(f[ig], culture),
                                    byte.Parse(f[ib], culture),
                                    ia >= 0 ? byte.Parse(f[ia], culture) : (byte)255);
                        }
                    }
                    else if (isFace && f.Length >= 4)
                    {
                        int n = int.Parse(f[0], culture);
                        // Fan-triangulate, which is correct for the convex quads and triangles
                        // reconstruction produces and harmless on anything already a triangle.
                        for (int t = 1; t + 1 < n; t++)
                        {
                            faces.Add(int.Parse(f[1], culture));
                            faces.Add(int.Parse(f[1 + t], culture));
                            faces.Add(int.Parse(f[2 + t], culture));
                        }
                    }
                }
            }

            data.positions = positions;
            data.colors = colors;
            data.indices = faces.Count > 0 ? faces.ToArray() : null;
        }

        static void ReadBinary(FileStream stream, List<Element> elements, Element vertex,
                               PlyData data, bool bigEndian)
        {
            var positions = new Vector3[vertex.count];
            Color32[] colors = null;
            List<int> faces = null;

            foreach (Element element in elements)
            {
                if (element == vertex)
                {
                    ReadBinaryVertices(stream, element, bigEndian, positions, ref colors);
                }
                else if (element.name == "face")
                {
                    faces = ReadBinaryFaces(stream, element, bigEndian);
                }
                else
                {
                    SkipBinaryElement(stream, element, bigEndian);
                }
            }

            data.positions = positions;
            data.colors = colors;
            data.indices = faces != null && faces.Count > 0 ? faces.ToArray() : null;
        }

        static void ReadBinaryVertices(FileStream stream, Element vertex, bool bigEndian,
                                       Vector3[] positions, ref Color32[] colors)
        {
            if (vertex.hasList)
                throw new FormatException("a list property inside the vertex element is not supported");

            int ix = IndexOfProperty(vertex, "x");
            int iy = IndexOfProperty(vertex, "y");
            int iz = IndexOfProperty(vertex, "z");
            if (ix < 0 || iy < 0 || iz < 0) throw new FormatException("vertex element has no x/y/z");

            int ir = IndexOfProperty(vertex, "red");
            int ig = IndexOfProperty(vertex, "green");
            int ib = IndexOfProperty(vertex, "blue");
            int ia = IndexOfProperty(vertex, "alpha");
            bool hasColor = ir >= 0 && ig >= 0 && ib >= 0;
            if (hasColor) colors = new Color32[vertex.count];

            Property px = vertex.properties[ix], py = vertex.properties[iy], pz = vertex.properties[iz];

            // One buffer for the whole block. At 700k points a per-field read through
            // BinaryReader costs millions of virtual calls and shows up as a visible stall on
            // every reimport.
            long total = (long)vertex.count * vertex.stride;
            var buffer = new byte[vertex.stride];
            const int k_Batch = 1 << 16;
            var batch = new byte[Math.Min(total, (long)k_Batch * vertex.stride)];

            int done = 0;
            while (done < vertex.count)
            {
                int want = Math.Min(k_Batch, vertex.count - done);
                int wantBytes = want * vertex.stride;
                ReadExactly(stream, batch, wantBytes);

                for (int i = 0; i < want; i++)
                {
                    int b = i * vertex.stride;
                    positions[done + i] = new Vector3(
                        (float)ReadScalar(batch, b + px.offset, px.type, bigEndian),
                        (float)ReadScalar(batch, b + py.offset, py.type, bigEndian),
                        (float)ReadScalar(batch, b + pz.offset, pz.type, bigEndian));

                    if (hasColor)
                    {
                        colors[done + i] = new Color32(
                            ToByte(ReadScalar(batch, b + vertex.properties[ir].offset, vertex.properties[ir].type, bigEndian), vertex.properties[ir].type),
                            ToByte(ReadScalar(batch, b + vertex.properties[ig].offset, vertex.properties[ig].type, bigEndian), vertex.properties[ig].type),
                            ToByte(ReadScalar(batch, b + vertex.properties[ib].offset, vertex.properties[ib].type, bigEndian), vertex.properties[ib].type),
                            ia >= 0 ? ToByte(ReadScalar(batch, b + vertex.properties[ia].offset, vertex.properties[ia].type, bigEndian), vertex.properties[ia].type) : (byte)255);
                    }
                }
                done += want;
            }
            _ = buffer;
        }

        static List<int> ReadBinaryFaces(FileStream stream, Element face, bool bigEndian)
        {
            var faces = new List<int>(face.count * 3);
            Property list = face.properties.Find(p => p.isList);
            if (list.name == null)
            {
                SkipBinaryElement(stream, face, bigEndian);
                return faces;
            }

            int countSize = SizeOf(list.countType);
            int indexSize = SizeOf(list.type);
            var head = new byte[countSize];
            var body = new byte[indexSize * 32];
            var corners = new int[32];

            for (int i = 0; i < face.count; i++)
            {
                ReadExactly(stream, head, countSize);
                int n = (int)ReadScalar(head, 0, list.countType, bigEndian);
                if (n <= 0) continue;

                if (n > corners.Length)
                {
                    corners = new int[n];
                    body = new byte[indexSize * n];
                }
                ReadExactly(stream, body, indexSize * n);
                for (int c = 0; c < n; c++)
                    corners[c] = (int)ReadScalar(body, c * indexSize, list.type, bigEndian);

                for (int t = 1; t + 1 < n; t++)
                {
                    faces.Add(corners[0]);
                    faces.Add(corners[t]);
                    faces.Add(corners[t + 1]);
                }
            }
            return faces;
        }

        static void SkipBinaryElement(FileStream stream, Element element, bool bigEndian)
        {
            if (!element.hasList)
            {
                stream.Seek((long)element.count * element.stride, SeekOrigin.Current);
                return;
            }

            // A variable-length element has to be walked; its size is not known up front.
            foreach (Property p in element.properties)
            {
                if (!p.isList) continue;
                int countSize = SizeOf(p.countType);
                int indexSize = SizeOf(p.type);
                var head = new byte[countSize];
                for (int i = 0; i < element.count; i++)
                {
                    ReadExactly(stream, head, countSize);
                    int n = (int)ReadScalar(head, 0, p.countType, bigEndian);
                    stream.Seek((long)n * indexSize, SeekOrigin.Current);
                }
            }
        }

        static void ReadExactly(Stream stream, byte[] into, int count)
        {
            int read = 0;
            while (read < count)
            {
                int got = stream.Read(into, read, count - read);
                if (got <= 0) throw new EndOfStreamException("file ended early");
                read += got;
            }
        }

        static double ReadScalar(byte[] b, int offset, string type, bool bigEndian)
        {
            int size = SizeOf(type);
            if (bigEndian && size > 1)
            {
                // Reverse into place. Big-endian PLY is rare but MeshLab still writes it, and
                // reading one as little-endian produces coordinates wrong by many orders of
                // magnitude rather than an error.
                Span<byte> tmp = stackalloc byte[8];
                for (int i = 0; i < size; i++) tmp[i] = b[offset + size - 1 - i];
                return ScalarFromSpan(tmp, type);
            }
            return ScalarFromSpan(new ReadOnlySpan<byte>(b, offset, size), type);
        }

        static double ScalarFromSpan(ReadOnlySpan<byte> s, string type) => type switch
        {
            "char" or "int8" => (sbyte)s[0],
            "uchar" or "uint8" => s[0],
            "short" or "int16" => BitConverter.ToInt16(s),
            "ushort" or "uint16" => BitConverter.ToUInt16(s),
            "int" or "int32" => BitConverter.ToInt32(s),
            "uint" or "uint32" => BitConverter.ToUInt32(s),
            "float" or "float32" => BitConverter.ToSingle(s),
            "double" or "float64" => BitConverter.ToDouble(s),
            _ => throw new FormatException($"unknown property type '{type}'")
        };

        /// <summary>Colour as a byte, whether the file stored 0-255 integers or 0-1 floats.</summary>
        static byte ToByte(double value, string type)
        {
            bool normalized = type is "float" or "float32" or "double" or "float64";
            double scaled = normalized ? value * 255.0 : value;
            return (byte)Math.Clamp(Math.Round(scaled), 0, 255);
        }
    }
}
