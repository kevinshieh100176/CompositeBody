using System.Collections.Generic;
using UnityEngine;

namespace CompositeBody.Assembly.EditorTools
{
    /// <summary>
    /// Cuts a mesh in two along a plane and caps the exposed cross-section, so each piece reads
    /// as a solid object that was sawn through rather than as a hollow shell.
    ///
    /// The capping is the whole reason this exists rather than a per-triangle sort into two bags.
    /// Sorting by centroid is a dozen lines, but it leaves a ragged seam and an open interior --
    /// and a <see cref="CompositeBody.Multiplayer.CompositeHalf"/> pair is looked at from its
    /// inside edge for the entire approach, which is the one angle a hollow half cannot survive.
    ///
    /// Each half is re-pivoted onto the centre of its own geometry. Leaving both on the source
    /// pivot would be less code and would make the assembly socket a no-op at the origin, but two
    /// objects sharing an origin a metre outside both of them is awkward to place by hand and
    /// tells the snap test nothing -- the halves would already be touching the moment they spawn.
    /// <see cref="Result.socketOffset"/> hands back the separation, so the socket can be placed
    /// from the geometry rather than measured off a screenshot.
    ///
    /// Editor-only: this runs once at authoring time and bakes mesh assets, so nothing here needs
    /// to be fast and nothing here should ship in a player.
    /// </summary>
    public static class MeshHalfSplitter
    {
        /// <summary>One side of the cut, re-pivoted onto the centre of its own geometry.</summary>
        public sealed class Half
        {
            /// <summary>Vertices are relative to <see cref="pivot"/>, not to the source origin.</summary>
            public Mesh mesh;

            /// <summary>Where this half's new origin sits, in the source mesh's space.</summary>
            public Vector3 pivot;

            /// <summary>Triangles in the cap submesh. Zero means the plane missed the surface.</summary>
            public int capTriangles;
        }

        public sealed class Result
        {
            public Half negative;
            public Half positive;

            /// <summary>
            /// Where the positive half's origin belongs relative to the negative half's once
            /// assembled. Put the assembly socket here and the two line up exactly.
            /// </summary>
            public Vector3 socketOffset;

            /// <summary>Source triangles the plane passed through, so they had to be clipped.</summary>
            public int clippedTriangles;

            /// <summary>Closed outlines the plane cut, i.e. how many parts of the model it severed.</summary>
            public int capLoops;
        }

        // Vertices nearer the plane than this count as lying on it. Below roughly this scale a
        // clip produces slivers whose normals are noise, and the cap outline picks up
        // near-duplicate points that then fail to join up into a loop.
        const float k_OnPlaneEpsilon = 1e-5f;

        // Cut points closer together than this are the same point. Adjacent source triangles
        // share an edge and each computes the crossing on it independently, so the two answers
        // differ in the last bits of float -- and the loop walk would be left with a pile of open
        // ends instead of one closed outline.
        const float k_WeldDistance = 1e-4f;

        /// <summary>
        /// Splits <paramref name="source"/> along <paramref name="plane"/>, both expressed in the
        /// source mesh's local space.
        /// </summary>
        /// <param name="cap">Fill the cross-section. Off leaves both halves open at the cut.</param>
        public static Result Split(Mesh source, Plane plane, bool cap = true)
        {
            if (source == null)
            {
                Debug.LogError("[MeshSplit] No source mesh.");
                return null;
            }

            var src = new SourceData(source);
            var negative = new SideBuilder(src, source.name + "_Negative");
            var positive = new SideBuilder(src, source.name + "_Positive");

            // Collected once rather than once per side: the two halves meet along the same
            // outline, and building it twice invites them to disagree by a float and leave a
            // visible crack down the middle of the assembled object.
            var cutEdges = new List<CutEdge>();

            int clipped = 0;

            for (int s = 0; s < source.subMeshCount; s++)
            {
                int[] tris = source.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];

                    float d0 = plane.GetDistanceToPoint(src.positions[i0]);
                    float d1 = plane.GetDistanceToPoint(src.positions[i1]);
                    float d2 = plane.GetDistanceToPoint(src.positions[i2]);

                    bool anyPositive = d0 > k_OnPlaneEpsilon || d1 > k_OnPlaneEpsilon || d2 > k_OnPlaneEpsilon;
                    bool anyNegative = d0 < -k_OnPlaneEpsilon || d1 < -k_OnPlaneEpsilon || d2 < -k_OnPlaneEpsilon;

                    if (!anyNegative)
                    {
                        positive.AddWholeTriangle(i0, i1, i2);
                        continue;
                    }
                    if (!anyPositive)
                    {
                        negative.AddWholeTriangle(i0, i1, i2);
                        continue;
                    }

                    clipped++;
                    ClipTriangle(src, negative, positive, cutEdges, i0, i1, i2, d0, d1, d2);
                }
            }

            int loops = 0;
            if (cap && cutEdges.Count > 0)
            {
                List<List<Vector3>> outlines = BuildLoops(cutEdges);
                loops = outlines.Count;

                foreach (var loop in outlines)
                {
                    // Logged per outline because the count alone cannot tell a correct cap from a
                    // wrong one: a plane through a chair ought to sever the seat, the back and a
                    // rail as three separate shapes, and three sections wrongly chained into one
                    // outline still reports a plausible-looking total.
                    var extent = new Bounds(loop[0], Vector3.zero);
                    foreach (var p in loop) extent.Encapsulate(p);
                    Debug.Log($"[MeshSplit] Cut outline: {loop.Count} points, " +
                              $"centre {extent.center}, size {extent.size}.");

                    // Each side's cap faces away from the other half, which is what lets the two
                    // of them look like one solid object again once they are welded back together.
                    negative.AddCap(loop, -plane.normal);
                    positive.AddCap(loop, plane.normal);
                }
            }

            var result = new Result
            {
                negative = negative.Build(),
                positive = positive.Build(),
                clippedTriangles = clipped,
                capLoops = loops,
            };

            result.socketOffset = result.positive.pivot - result.negative.pivot;
            return result;
        }

        struct CutEdge
        {
            public Vector3 a;
            public Vector3 b;
        }

        /// <summary>
        /// Sutherland-Hodgman against a single plane, accumulating both sides in one pass. What
        /// each side keeps is a triangle or a quad -- a convex shape cut by a plane stays convex
        /// -- so a fan from the polygon's first corner triangulates it correctly.
        /// </summary>
        static void ClipTriangle(SourceData src, SideBuilder negative, SideBuilder positive,
                                 List<CutEdge> cutEdges,
                                 int i0, int i1, int i2, float d0, float d1, float d2)
        {
            var idx = new[] { i0, i1, i2 };
            var dist = new[] { d0, d1, d2 };

            var negPoly = new List<int>(4);
            var posPoly = new List<int>(4);
            var crossings = new List<Vector3>(2);

            for (int k = 0; k < 3; k++)
            {
                int cur = k, next = (k + 1) % 3;
                float dc = dist[cur], dn = dist[next];

                if (dc >= 0f) posPoly.Add(positive.AddSourceVertex(idx[cur]));
                if (dc <= 0f) negPoly.Add(negative.AddSourceVertex(idx[cur]));

                // Strictly opposite signs only. An endpoint sitting on the plane is already in
                // both polygons from the two tests above, and counting it as a crossing as well
                // would add the same point twice and collapse the triangle built from it.
                if ((dc > 0f && dn < 0f) || (dc < 0f && dn > 0f))
                {
                    float t = dc / (dc - dn);
                    posPoly.Add(positive.AddEdgeVertex(idx[cur], idx[next], t));
                    negPoly.Add(negative.AddEdgeVertex(idx[cur], idx[next], t));
                    crossings.Add(Vector3.Lerp(src.positions[idx[cur]], src.positions[idx[next]], t));
                }
            }

            positive.AddFan(posPoly);
            negative.AddFan(negPoly);

            // A triangle crossing a plane enters and leaves exactly once. Anything else is a
            // degenerate case the epsilon above should already have folded into a whole-side
            // triangle, and feeding it to the cap would leave a stray edge in the outline.
            if (crossings.Count == 2)
                cutEdges.Add(new CutEdge { a = crossings[0], b = crossings[1] });
        }

        #region Cap outlines

        /// <summary>
        /// Chains the loose cut edges into closed outlines -- one per part of the model the plane
        /// severed, so a chair cut down the middle yields one for the seat, one for the back and
        /// one for whichever rail happened to be in the way.
        /// </summary>
        static List<List<Vector3>> BuildLoops(List<CutEdge> edges)
        {
            var points = new List<Vector3>();
            var lookup = new Dictionary<Vector3Int, int>();
            var adjacency = new List<List<int>>();

            foreach (var edge in edges)
            {
                int ia = Weld(edge.a, points, lookup, adjacency);
                int ib = Weld(edge.b, points, lookup, adjacency);
                if (ia == ib) continue;                   // an edge shorter than the weld grid

                if (!adjacency[ia].Contains(ib)) adjacency[ia].Add(ib);
                if (!adjacency[ib].Contains(ia)) adjacency[ib].Add(ia);
            }

            var loops = new List<List<Vector3>>();
            var visited = new bool[points.Count];

            for (int start = 0; start < points.Count; start++)
            {
                if (visited[start] || adjacency[start].Count == 0) continue;

                var loop = new List<Vector3>();
                int current = start, previous = -1;

                while (current >= 0 && !visited[current])
                {
                    visited[current] = true;
                    loop.Add(points[current]);

                    int next = -1;
                    foreach (int candidate in adjacency[current])
                    {
                        if (candidate == previous || visited[candidate]) continue;
                        next = candidate;
                        break;
                    }

                    previous = current;
                    current = next;
                }

                // Two points enclose nothing. Dropping them quietly is right: an outline that
                // short comes from the plane grazing a corner, where there is no area to fill.
                if (loop.Count >= 3) loops.Add(loop);
            }

            return loops;
        }

        static int Weld(Vector3 p, List<Vector3> points,
                        Dictionary<Vector3Int, int> lookup, List<List<int>> adjacency)
        {
            var cell = new Vector3Int(
                Mathf.RoundToInt(p.x / k_WeldDistance),
                Mathf.RoundToInt(p.y / k_WeldDistance),
                Mathf.RoundToInt(p.z / k_WeldDistance));

            if (lookup.TryGetValue(cell, out int existing)) return existing;

            lookup[cell] = points.Count;
            points.Add(p);
            adjacency.Add(new List<int>(2));
            return points.Count - 1;
        }

        #endregion

        #region Buffers

        /// <summary>The source mesh's arrays, read once and length-checked so every stream can be
        /// addressed by vertex index without re-testing it per vertex.</summary>
        sealed class SourceData
        {
            public readonly Vector3[] positions;
            public readonly Vector3[] normals;
            public readonly Vector4[] tangents;
            public readonly Vector2[] uv;
            public readonly Vector2[] uv2;
            public readonly Color[] colors;

            public readonly bool hasNormals, hasTangents, hasUv, hasUv2, hasColors;

            public SourceData(Mesh mesh)
            {
                positions = mesh.vertices;
                normals = mesh.normals;
                tangents = mesh.tangents;
                uv = mesh.uv;
                uv2 = mesh.uv2;
                colors = mesh.colors;

                int n = positions.Length;
                hasNormals = normals.Length == n;
                hasTangents = tangents.Length == n;
                hasUv = uv.Length == n;
                hasUv2 = uv2.Length == n;
                hasColors = colors.Length == n;
            }
        }

        /// <summary>
        /// Accumulates one side of the cut. Vertices are emitted on demand and memoised, so a
        /// source vertex shared by twenty triangles stays one vertex and each half keeps the
        /// source's topology instead of exploding into loose triangles.
        /// </summary>
        sealed class SideBuilder
        {
            readonly SourceData m_Src;
            readonly string m_Name;

            readonly List<Vector3> m_Positions = new List<Vector3>();
            readonly List<Vector3> m_Normals = new List<Vector3>();
            readonly List<Vector4> m_Tangents = new List<Vector4>();
            readonly List<Vector2> m_Uv = new List<Vector2>();
            readonly List<Vector2> m_Uv2 = new List<Vector2>();
            readonly List<Color> m_Colors = new List<Color>();

            readonly List<int> m_BodyTriangles = new List<int>();
            readonly List<int> m_CapTriangles = new List<int>();

            readonly Dictionary<int, int> m_FromSource = new Dictionary<int, int>();
            readonly Dictionary<(int, int), int> m_FromEdge = new Dictionary<(int, int), int>();

            public SideBuilder(SourceData src, string name)
            {
                m_Src = src;
                m_Name = name;
            }

            public int AddSourceVertex(int index)
            {
                if (m_FromSource.TryGetValue(index, out int existing)) return existing;

                m_Positions.Add(m_Src.positions[index]);
                if (m_Src.hasNormals) m_Normals.Add(m_Src.normals[index]);
                if (m_Src.hasTangents) m_Tangents.Add(m_Src.tangents[index]);
                if (m_Src.hasUv) m_Uv.Add(m_Src.uv[index]);
                if (m_Src.hasUv2) m_Uv2.Add(m_Src.uv2[index]);
                if (m_Src.hasColors) m_Colors.Add(m_Src.colors[index]);

                m_FromSource[index] = m_Positions.Count - 1;
                return m_Positions.Count - 1;
            }

            /// <summary>
            /// A vertex on the cut, interpolated along a source edge. Keyed by the edge rather
            /// than by position: the two triangles sharing that edge ask for the same point, and
            /// a duplicate there would split the smooth shading right along the new rim.
            /// </summary>
            public int AddEdgeVertex(int a, int b, float t)
            {
                // Ordered key, with t re-expressed to match, so both triangles sharing the edge
                // land on the same dictionary entry whichever way round they walk it.
                var key = a < b ? (a, b) : (b, a);
                if (a > b) t = 1f - t;

                if (m_FromEdge.TryGetValue(key, out int existing)) return existing;

                int ia = key.Item1, ib = key.Item2;

                m_Positions.Add(Vector3.Lerp(m_Src.positions[ia], m_Src.positions[ib], t));
                if (m_Src.hasNormals)
                    m_Normals.Add(Vector3.Slerp(m_Src.normals[ia], m_Src.normals[ib], t));
                if (m_Src.hasTangents)
                    m_Tangents.Add(LerpTangent(m_Src.tangents[ia], m_Src.tangents[ib], t));
                if (m_Src.hasUv) m_Uv.Add(Vector2.Lerp(m_Src.uv[ia], m_Src.uv[ib], t));
                if (m_Src.hasUv2) m_Uv2.Add(Vector2.Lerp(m_Src.uv2[ia], m_Src.uv2[ib], t));
                if (m_Src.hasColors) m_Colors.Add(Color.Lerp(m_Src.colors[ia], m_Src.colors[ib], t));

                m_FromEdge[key] = m_Positions.Count - 1;
                return m_Positions.Count - 1;
            }

            public void AddWholeTriangle(int i0, int i1, int i2)
            {
                m_BodyTriangles.Add(AddSourceVertex(i0));
                m_BodyTriangles.Add(AddSourceVertex(i1));
                m_BodyTriangles.Add(AddSourceVertex(i2));
            }

            /// <summary>Fans a convex polygon of already-emitted vertices from its first corner.</summary>
            public void AddFan(List<int> polygon)
            {
                for (int i = 1; i + 1 < polygon.Count; i++)
                {
                    m_BodyTriangles.Add(polygon[0]);
                    m_BodyTriangles.Add(polygon[i]);
                    m_BodyTriangles.Add(polygon[i + 1]);
                }
            }

            /// <summary>
            /// Fills one cut outline, fanned from its centroid rather than from a corner so that
            /// a mildly concave cross-section -- a turned leg, a moulded rail -- still tiles
            /// without triangles escaping the outline.
            /// </summary>
            public void AddCap(List<Vector3> loop, Vector3 facing)
            {
                Vector3 centre = Vector3.zero;
                foreach (var p in loop) centre += p;
                centre /= loop.Count;

                // Taken from the geometry rather than from the winding: the outline comes back
                // from a walk over undirected edges, so which way round it runs is arbitrary, and
                // half the caps would otherwise face into the half they belong to.
                Vector3 sample = Vector3.Cross(loop[0] - centre, loop[1] - centre);
                bool flip = Vector3.Dot(sample, facing) < 0f;

                int hub = AddCapVertex(centre, facing, centre);

                for (int i = 0; i < loop.Count; i++)
                {
                    Vector3 a = loop[i];
                    Vector3 b = loop[(i + 1) % loop.Count];
                    if (flip) { Vector3 swap = a; a = b; b = swap; }

                    m_CapTriangles.Add(hub);
                    m_CapTriangles.Add(AddCapVertex(a, facing, centre));
                    m_CapTriangles.Add(AddCapVertex(b, facing, centre));
                }
            }

            /// <summary>
            /// A cap vertex is always its own vertex, never shared with the rim beside it: the
            /// cut face meets the original surface at a hard edge, and sharing would average the
            /// two normals into something that reads as a rounded edge rather than a sawn one.
            /// </summary>
            int AddCapVertex(Vector3 position, Vector3 normal, Vector3 origin)
            {
                m_Positions.Add(position);
                if (m_Src.hasNormals) m_Normals.Add(normal);
                if (m_Src.hasTangents) m_Tangents.Add(new Vector4(1f, 0f, 0f, -1f));

                // Planar coordinates taken off the cut face itself. The chair's own UVs mean
                // nothing on a surface that did not exist before the cut, and laying a texture
                // across the face at its real-world scale at least reads as material rather than
                // as a smear of whichever texel the rim happened to land on.
                if (m_Src.hasUv) m_Uv.Add(PlanarUv(position - origin, normal));
                if (m_Src.hasUv2) m_Uv2.Add(Vector2.zero);
                if (m_Src.hasColors) m_Colors.Add(Color.white);

                return m_Positions.Count - 1;
            }

            public Half Build()
            {
                // Re-pivot onto the centre of whatever this side actually ended up with.
                var bounds = new Bounds(m_Positions.Count > 0 ? m_Positions[0] : Vector3.zero, Vector3.zero);
                foreach (var p in m_Positions) bounds.Encapsulate(p);

                Vector3 pivot = bounds.center;
                for (int i = 0; i < m_Positions.Count; i++) m_Positions[i] -= pivot;

                var mesh = new Mesh
                {
                    name = m_Name,
                    indexFormat = m_Positions.Count > 65000
                        ? UnityEngine.Rendering.IndexFormat.UInt32
                        : UnityEngine.Rendering.IndexFormat.UInt16,
                };

                mesh.SetVertices(m_Positions);
                if (m_Normals.Count == m_Positions.Count) mesh.SetNormals(m_Normals);
                if (m_Tangents.Count == m_Positions.Count) mesh.SetTangents(m_Tangents);
                if (m_Uv.Count == m_Positions.Count) mesh.SetUVs(0, m_Uv);
                if (m_Uv2.Count == m_Positions.Count) mesh.SetUVs(1, m_Uv2);
                if (m_Colors.Count == m_Positions.Count) mesh.SetColors(m_Colors);

                // The cap goes in its own submesh so it can take its own material. The cut face
                // is the one surface that has to be legible while the halves are apart -- it is
                // what a player aims at -- and it is also the only way to see at a glance whether
                // the seam landed where the socket says it did.
                bool hasCap = m_CapTriangles.Count > 0;
                mesh.subMeshCount = hasCap ? 2 : 1;
                mesh.SetTriangles(m_BodyTriangles, 0);
                if (hasCap) mesh.SetTriangles(m_CapTriangles, 1);

                if (m_Normals.Count != m_Positions.Count) mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                return new Half { mesh = mesh, pivot = pivot, capTriangles = m_CapTriangles.Count / 3 };
            }

            static Vector2 PlanarUv(Vector3 offset, Vector3 normal)
            {
                Vector3 u = Vector3.Normalize(Vector3.Cross(normal,
                    Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right));
                Vector3 v = Vector3.Cross(normal, u);
                return new Vector2(Vector3.Dot(offset, u), Vector3.Dot(offset, v));
            }

            static Vector4 LerpTangent(Vector4 a, Vector4 b, float t)
            {
                Vector3 dir = Vector3.Slerp(new Vector3(a.x, a.y, a.z), new Vector3(b.x, b.y, b.z), t);

                // The w is a handedness flag, not a quantity -- halfway between -1 and 1 is 0,
                // which would flip the normal map inside out along the whole rim.
                return new Vector4(dir.x, dir.y, dir.z, t < 0.5f ? a.w : b.w);
            }
        }

        #endregion
    }
}
