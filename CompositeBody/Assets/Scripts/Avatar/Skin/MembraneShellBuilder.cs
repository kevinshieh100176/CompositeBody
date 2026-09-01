using System.Collections.Generic;
using UnityEngine;

namespace CompositeBody.Avatar.Skin
{
    /// <summary>
    /// Builds a vacuum-film membrane around a body: the body mesh pushed out along its normals
    /// and then relaxed, so the sheet bridges concavities the way a wrapped film actually does
    /// -- spanning the armpits, the neck, the gaps between fingers -- while still pulling tight
    /// onto whatever sticks out.
    ///
    /// The relaxation is the whole point. A plain normal offset is a scale model of the body and
    /// touches it everywhere, which leaves the shader no contact information to shade with. A
    /// Laplacian pass pulls concave regions outward toward the average of their neighbours while
    /// the outward constraint stops convex regions collapsing inward, which is exactly the shape
    /// a sheet takes when it is drawn down onto a form.
    ///
    /// Per-vertex channels the membrane shader reads:
    /// - <b>UV1.x</b> is the gap between the film and the body at that vertex. Near zero the film
    ///   is against skin and renders clear; large, it is spanning air and renders milky.
    /// - <b>UV2.xyz</b> is the membrane's own bind-pose position, which anchors the crease noise
    ///   to the sheet. Sampling from the live position instead would make the creases swim
    ///   through the film whenever the body moved.
    ///
    /// Bone weights and bind poses are copied across, so the film skins with the body.
    /// </summary>
    public static class MembraneShellBuilder
    {
        /// <summary>
        /// Creates the membrane mesh. <paramref name="sourceMesh"/> must be readable; when
        /// driving this from the editor an imported FBX mesh works even with Read/Write off.
        /// </summary>
        /// <param name="baseOffset">How far the sheet starts off the skin, in metres.</param>
        /// <param name="minOffset">Closest the sheet is ever allowed to sit to the body.</param>
        /// <param name="smoothIterations">Relaxation passes; more bridges wider concavities.</param>
        /// <param name="smoothLambda">Relaxation strength per pass, 0..1.</param>
        public static Mesh Build(Mesh sourceMesh, float baseOffset, float minOffset,
                                 int smoothIterations, float smoothLambda)
        {
            var srcVerts = sourceMesh.vertices;
            var srcNormals = sourceMesh.normals;

            if (srcNormals == null || srcNormals.Length != srcVerts.Length)
            {
                sourceMesh.RecalculateNormals();
                srcNormals = sourceMesh.normals;
            }

            int count = srcVerts.Length;

            // A character mesh is split into separate vertices along every UV seam. Relaxing on
            // the raw index buffer would treat those as unconnected islands and tear visible
            // cracks down each seam, so the smoothing runs on positions welded back together and
            // the result is scattered out to the duplicates afterwards.
            var weldOf = new int[count];
            var repOfKey = new Dictionary<Vector3Int, int>(count);
            var repBody = new List<Vector3>(count);
            var repNormalSum = new List<Vector3>(count);

            for (int i = 0; i < count; i++)
            {
                var key = new Vector3Int(
                    Mathf.RoundToInt(srcVerts[i].x * 10000f),
                    Mathf.RoundToInt(srcVerts[i].y * 10000f),
                    Mathf.RoundToInt(srcVerts[i].z * 10000f));

                if (!repOfKey.TryGetValue(key, out int rep))
                {
                    rep = repBody.Count;
                    repOfKey.Add(key, rep);
                    repBody.Add(srcVerts[i]);
                    repNormalSum.Add(Vector3.zero);
                }

                weldOf[i] = rep;
                repNormalSum[rep] += srcNormals[i];
            }

            int repCount = repBody.Count;
            var repNormal = new Vector3[repCount];
            for (int r = 0; r < repCount; r++)
            {
                Vector3 n = repNormalSum[r];
                repNormal[r] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            }

            // Adjacency over welded vertices, from the triangle edges.
            var neighbours = new List<int>[repCount];
            for (int r = 0; r < repCount; r++) neighbours[r] = new List<int>(6);

            for (int s = 0; s < sourceMesh.subMeshCount; s++)
            {
                var tris = sourceMesh.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = weldOf[tris[t]], b = weldOf[tris[t + 1]], c = weldOf[tris[t + 2]];
                    AddNeighbour(neighbours, a, b); AddNeighbour(neighbours, b, a);
                    AddNeighbour(neighbours, b, c); AddNeighbour(neighbours, c, b);
                    AddNeighbour(neighbours, c, a); AddNeighbour(neighbours, a, c);
                }
            }

            var repPos = new Vector3[repCount];
            for (int r = 0; r < repCount; r++)
                repPos[r] = repBody[r] + repNormal[r] * baseOffset;

            var scratch = new Vector3[repCount];
            for (int iter = 0; iter < smoothIterations; iter++)
            {
                for (int r = 0; r < repCount; r++)
                {
                    var list = neighbours[r];
                    if (list.Count == 0) { scratch[r] = repPos[r]; continue; }

                    Vector3 sum = Vector3.zero;
                    for (int k = 0; k < list.Count; k++) sum += repPos[list[k]];
                    scratch[r] = Vector3.Lerp(repPos[r], sum / list.Count, smoothLambda);
                }

                // Relaxation on its own collapses the sheet onto -- and through -- the body.
                // Re-projecting anything that ended up too close is what keeps the film outside
                // the skin and pulled taut over convex regions.
                for (int r = 0; r < repCount; r++)
                {
                    Vector3 delta = scratch[r] - repBody[r];
                    float along = Vector3.Dot(delta, repNormal[r]);
                    if (along < minOffset)
                        scratch[r] = repBody[r] + repNormal[r] * minOffset + (delta - repNormal[r] * along);
                    repPos[r] = scratch[r];
                }
            }

            var verts = new Vector3[count];
            var gapData = new Vector2[count];
            var restPositions = new List<Vector3>(count);

            for (int i = 0; i < count; i++)
            {
                int r = weldOf[i];
                Vector3 p = repPos[r];
                verts[i] = p;
                gapData[i] = new Vector2(Vector3.Distance(p, repBody[r]), 0f);
                restPositions.Add(p);
            }

            var film = new Mesh
            {
                name = sourceMesh.name + "_Membrane",
                indexFormat = count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };

            film.vertices = verts;
            film.uv = sourceMesh.uv;
            film.uv2 = gapData;
            film.SetUVs(2, restPositions);

            film.subMeshCount = sourceMesh.subMeshCount;
            for (int s = 0; s < sourceMesh.subMeshCount; s++)
                film.SetTriangles(sourceMesh.GetTriangles(s), s);

            film.boneWeights = sourceMesh.boneWeights;
            film.bindposes = sourceMesh.bindposes;

            // Normals come from the relaxed shape, not the body: the film has its own silhouette
            // wherever it bridges a gap, and reusing the body's normals there would light the
            // sheet as though it were still lying on the skin.
            film.RecalculateNormals();
            film.RecalculateBounds();
            return film;
        }

        static void AddNeighbour(List<int>[] neighbours, int from, int to)
        {
            var list = neighbours[from];
            for (int i = 0; i < list.Count; i++)
                if (list[i] == to) return;
            list.Add(to);
        }
    }
}
