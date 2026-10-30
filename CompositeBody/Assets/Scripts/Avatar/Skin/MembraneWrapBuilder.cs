using System.Collections.Generic;
using UnityEngine;

namespace CompositeBody.Avatar.Skin
{
    /// <summary>
    /// Builds a single vacuum film around a whole object, however that object is modelled.
    ///
    /// <see cref="MembraneShellBuilder"/> offsets the body's own mesh and relaxes it, which only
    /// works because a character is one connected, evenly tessellated surface. A prop usually is
    /// not. The rustic chair is fifteen disconnected shells -- a seat, a rail and thirteen turned
    /// spindles shoved into each other -- and relaxation along topology can never cross from one
    /// to the next, so that route gives fifteen separate bags instead of one sheet.
    ///
    /// This builds the sheet instead of deriving it. The object is rasterised into a voxel grid
    /// and the film is extracted as an isosurface standing off it, which is indifferent to how
    /// many pieces the object is made of, whether they interpenetrate, and whether any of them
    /// is watertight.
    ///
    /// The pipeline, and why each step is there:
    /// <list type="number">
    /// <item>Rasterise every triangle into the grid. One set of surface voxels for the whole
    ///       object, which is where the fifteen shells stop mattering.</item>
    /// <item>Distance transform from those voxels. This is the true distance to the object, and
    ///       it is kept to the end because it is what the shader's contact channel wants.</item>
    /// <item>Dilate by the span radius, then flood fill inward from the grid boundary. The fill
    ///       cannot enter a gap narrower than twice the span radius, because the dilation has
    ///       already sealed it -- so the fill is what decides where film bridges and where it
    ///       goes in. Doing it as a fill rather than a test also makes the whole thing immune to
    ///       pinholes in the rasterisation, since a leak smaller than the span radius seals too.
    ///       </item>
    /// <item>Erode the filled region back by the same radius, undoing the dilation but keeping
    ///       the bridges. This is the shape a sheet drawn down onto the object actually takes.
    ///       </item>
    /// <item>Extract the isosurface standing <c>offset</c> off that shape, with surface nets,
    ///       and Taubin-smooth it. Taubin rather than plain Laplacian because the film must not
    ///       shrink onto the object: a smoothing pass that loses volume closes the very gaps
    ///       step 3 was there to keep open.</item>
    /// <item>Re-sample the step-2 distances at the final vertices for the gap channel. After
    ///       smoothing, so the number describes the film that actually got built.</item>
    /// </list>
    ///
    /// The one setting with a look attached is <see cref="Settings.spanRadius"/>: gaps narrower
    /// than twice it get skinned over, everything wider stays open. At zero the film shrink-fits
    /// into every crevice; large enough and the whole object disappears into a smooth bag.
    ///
    /// Per-vertex channels, matching what the membrane shader reads:
    /// - <b>UV1.x</b> distance from the film to the object at that vertex.
    /// - <b>UV2.xyz</b> the film's rest position, which anchors the crease noise. A static prop
    ///   does not deform, so this is simply the vertex's own position.
    /// </summary>
    public static class MembraneWrapBuilder
    {
        public struct Settings
        {
            /// <summary>Grid resolution. The film's triangles come out about this size.</summary>
            public float voxelSize;

            /// <summary>How far off the object the sheet stands.</summary>
            public float offset;

            /// <summary>Gaps narrower than twice this are bridged rather than entered.</summary>
            public float spanRadius;

            /// <summary>Taubin passes over the extracted surface.</summary>
            public int smoothIterations;

            /// <summary>Ceiling on grid cells; the voxel size is coarsened to fit under it.</summary>
            public int maxVoxels;

            /// <summary>
            /// A point in the space the film is seen from.
            ///
            /// Left null, the fill starts at the grid's own boundary and the sheet closes around
            /// the object from outside: a prop, looked at from across the room. Set, the fill
            /// starts there instead, and the sheet forms on every surface facing that point: a
            /// room, looked at from inside it. Walls, floor and furniture then come out as one
            /// continuous film rather than as separately wrapped objects, because from a seed in
            /// the middle of the room they are all one surface.
            /// </summary>
            public Vector3? interiorSeed;

            public static Settings Default => new Settings
            {
                voxelSize = 0.005f,
                offset = 0.012f,
                spanRadius = 0.025f,
                smoothIterations = 12,
                maxVoxels = 40_000_000,
                interiorSeed = null,
            };
        }

        // Finite rather than float.PositiveInfinity: the distance transform subtracts these from
        // one another, and infinity minus infinity is a NaN that silently poisons the whole grid.
        const float k_Far = 1e10f;

        public static Mesh Build(Mesh source, Settings s, out string report)
        {
            var log = new System.Text.StringBuilder();

            var srcVerts = source.vertices;
            var tris = new List<int>();
            for (int sub = 0; sub < source.subMeshCount; sub++) tris.AddRange(source.GetTriangles(sub));

            // Margin has to clear everything the pipeline reaches outward: the standoff, the
            // dilation, and a few cells of slack so the isosurface is never clipped by the grid
            // wall -- which would leave the film with a hole where it ran out of room.
            Bounds b = source.bounds;
            float margin = s.offset + s.spanRadius + 4f * s.voxelSize;
            Vector3 min = b.min - Vector3.one * margin;
            Vector3 max = b.max + Vector3.one * margin;

            float voxel = s.voxelSize;
            Vector3Int dims = GridDims(min, max, voxel);
            while ((long)dims.x * dims.y * dims.z > s.maxVoxels)
            {
                voxel *= 1.25f;
                dims = GridDims(min, max, voxel);
            }
            if (voxel > s.voxelSize)
                log.AppendLine($"voxel coarsened {s.voxelSize * 1000f:F1}mm -> {voxel * 1000f:F1}mm to fit the cell ceiling");

            int n = dims.x * dims.y * dims.z;
            log.AppendLine($"grid {dims.x}x{dims.y}x{dims.z} = {n:N0} cells at {voxel * 1000f:F1}mm");

            // --- 1. the object, as voxels -------------------------------------------------
            var surface = new bool[n];
            int marked = Rasterise(srcVerts, tris, min, voxel, dims, surface);
            log.AppendLine($"rasterised {tris.Count / 3:N0} triangles into {marked:N0} surface cells");

            if (marked == 0)
            {
                report = log + "nothing rasterised";
                return null;
            }

            // --- 2. true distance to the object -------------------------------------------
            float[] distSurface = DistanceField(surface, dims, voxel);

            // --- 3. dilate, then fill from outside -----------------------------------------
            var blocked = new bool[n];
            for (int i = 0; i < n; i++) blocked[i] = distSurface[i] <= s.spanRadius;

            List<int> seeds;
            if (s.interiorSeed.HasValue)
            {
                seeds = InteriorSeed(blocked, dims, min, voxel, s.interiorSeed.Value);
                if (seeds.Count == 0)
                {
                    report = log + "the interior seed is buried in solid; nowhere for the fill to start";
                    return null;
                }
                log.AppendLine($"filling from inside, seeded at {s.interiorSeed.Value}");
            }
            else
            {
                seeds = BoundarySeeds(dims);
                log.AppendLine("filling from outside the grid");
            }

            bool[] free = Flood(blocked, dims, seeds);

            // --- 4. erode the fill back ----------------------------------------------------
            float[] distFree = DistanceField(free, dims, voxel);

            var wrapped = new bool[n];
            int wrappedCount = 0;
            for (int i = 0; i < n; i++)
            {
                wrapped[i] = distFree[i] > s.spanRadius;
                if (wrapped[i]) wrappedCount++;
            }

            if (wrappedCount == 0)
            {
                report = log + "the span radius erased the object; it is larger than the object is thick";
                return null;
            }

            // --- 5. the sheet standing off it ----------------------------------------------
            float[] distWrapped = DistanceField(wrapped, dims, voxel);

            var field = new float[n];
            for (int i = 0; i < n; i++) field[i] = distWrapped[i] - s.offset;

            SurfaceNets(field, dims, min, voxel, out List<Vector3> verts, out List<int> filmTris);
            if (verts.Count == 0)
            {
                report = log + "the isosurface came out empty";
                return null;
            }
            log.AppendLine($"extracted {verts.Count:N0} verts, {filmTris.Count / 3:N0} triangles");

            TaubinSmooth(verts, filmTris, s.smoothIterations);

            // --- 6. the contact channel ----------------------------------------------------
            var gaps = new Vector2[verts.Count];
            var rest = new List<Vector3>(verts.Count);
            float gMin = float.MaxValue, gMax = 0f, gSum = 0f;

            for (int i = 0; i < verts.Count; i++)
            {
                float g = SampleTrilinear(distSurface, dims, min, voxel, verts[i]);
                gaps[i] = new Vector2(g, 0f);
                rest.Add(verts[i]);
                gMin = Mathf.Min(gMin, g);
                gMax = Mathf.Max(gMax, g);
                gSum += g;
            }

            log.AppendLine($"gap min={gMin * 1000f:F1}mm mean={gSum / verts.Count * 1000f:F1}mm max={gMax * 1000f:F1}mm");
            log.AppendLine($"film shells: {CountShells(verts.Count, filmTris)}");

            var film = new Mesh
            {
                name = source.name + "_MembraneWrap",
                indexFormat = verts.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };

            film.SetVertices(verts);
            film.SetTriangles(filmTris, 0);
            film.uv2 = gaps;
            film.SetUVs(2, rest);
            film.RecalculateNormals();
            film.RecalculateBounds();

            report = log.ToString();
            return film;
        }

        static Vector3Int GridDims(Vector3 min, Vector3 max, float voxel)
        {
            Vector3 size = max - min;
            return new Vector3Int(
                Mathf.Max(2, Mathf.CeilToInt(size.x / voxel) + 1),
                Mathf.Max(2, Mathf.CeilToInt(size.y / voxel) + 1),
                Mathf.Max(2, Mathf.CeilToInt(size.z / voxel) + 1));
        }

        static int Index(Vector3Int d, int x, int y, int z) => x + d.x * (y + d.y * z);

        // ---- rasterisation ----------------------------------------------------------------

        /// <summary>
        /// Separating-axis triangle/box overlap for every cell in each triangle's bounds. Exact
        /// rather than point-sampled: a sampled rasterisation leaves pinholes on thin or
        /// obliquely-angled triangles, and a pinhole is not a cosmetic defect here -- the flood
        /// fill pours through it and the whole interior of the object is declared outside.
        /// </summary>
        static int Rasterise(Vector3[] verts, List<int> tris, Vector3 origin, float voxel,
                             Vector3Int dims, bool[] surface)
        {
            int marked = 0;
            Vector3 half = Vector3.one * (voxel * 0.5f);

            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = verts[tris[t]], bb = verts[tris[t + 1]], c = verts[tris[t + 2]];

                Vector3 lo = Vector3.Min(Vector3.Min(a, bb), c);
                Vector3 hi = Vector3.Max(Vector3.Max(a, bb), c);

                int x0 = Mathf.Clamp(Mathf.FloorToInt((lo.x - origin.x) / voxel) - 1, 0, dims.x - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt((lo.y - origin.y) / voxel) - 1, 0, dims.y - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((lo.z - origin.z) / voxel) - 1, 0, dims.z - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((hi.x - origin.x) / voxel) + 1, 0, dims.x - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt((hi.y - origin.y) / voxel) + 1, 0, dims.y - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((hi.z - origin.z) / voxel) + 1, 0, dims.z - 1);

                for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int idx = Index(dims, x, y, z);
                    if (surface[idx]) continue;

                    Vector3 centre = origin + new Vector3(x * voxel, y * voxel, z * voxel);
                    if (!TriBoxOverlap(centre, half, a, bb, c)) continue;

                    surface[idx] = true;
                    marked++;
                }
            }

            return marked;
        }

        static bool TriBoxOverlap(Vector3 centre, Vector3 half, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 v0 = a - centre, v1 = b - centre, v2 = c - centre;
            Vector3 e0 = v1 - v0, e1 = v2 - v1, e2 = v0 - v2;

            if (!AxisTest(v0, v1, v2, e0, half)) return false;
            if (!AxisTest(v0, v1, v2, e1, half)) return false;
            if (!AxisTest(v0, v1, v2, e2, half)) return false;

            // The box's own three axes.
            if (Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x)) > half.x || Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x)) < -half.x) return false;
            if (Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y)) > half.y || Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y)) < -half.y) return false;
            if (Mathf.Min(v0.z, Mathf.Min(v1.z, v2.z)) > half.z || Mathf.Max(v0.z, Mathf.Max(v1.z, v2.z)) < -half.z) return false;

            // The triangle's plane.
            Vector3 normal = Vector3.Cross(e0, e1);
            float d = Vector3.Dot(normal, v0);
            float r = half.x * Mathf.Abs(normal.x) + half.y * Mathf.Abs(normal.y) + half.z * Mathf.Abs(normal.z);
            return Mathf.Abs(d) <= r;
        }

        /// <summary>The nine cross-product axes, as three tests against one triangle edge.</summary>
        static bool AxisTest(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 e, Vector3 half)
        {
            return Axis(v0, v1, v2, new Vector3(0, -e.z, e.y), half)
                && Axis(v0, v1, v2, new Vector3(e.z, 0, -e.x), half)
                && Axis(v0, v1, v2, new Vector3(-e.y, e.x, 0), half);
        }

        static bool Axis(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 axis, Vector3 half)
        {
            if (axis.sqrMagnitude < 1e-20f) return true;

            float p0 = Vector3.Dot(v0, axis), p1 = Vector3.Dot(v1, axis), p2 = Vector3.Dot(v2, axis);
            float r = half.x * Mathf.Abs(axis.x) + half.y * Mathf.Abs(axis.y) + half.z * Mathf.Abs(axis.z);
            return Mathf.Min(p0, Mathf.Min(p1, p2)) <= r && Mathf.Max(p0, Mathf.Max(p1, p2)) >= -r;
        }

        // ---- distance transform -------------------------------------------------------------

        /// <summary>
        /// Exact Euclidean distance to the nearest set cell, by Felzenszwalb's separable
        /// transform: three one-dimensional passes rather than a propagating chamfer, so the
        /// result is the real distance and not an eight-way approximation of it. The gap channel
        /// is the shader's main input, so the error in a chamfer mask would show up directly as
        /// banding in the contact split.
        /// </summary>
        static float[] DistanceField(bool[] set, Vector3Int dims, float voxel)
        {
            int n = dims.x * dims.y * dims.z;
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = set[i] ? 0f : k_Far;

            int longest = Mathf.Max(dims.x, Mathf.Max(dims.y, dims.z));
            var f = new float[longest];
            var dd = new float[longest];
            var v = new int[longest];
            var z = new float[longest + 1];

            for (int zz = 0; zz < dims.z; zz++)
            for (int yy = 0; yy < dims.y; yy++)
            {
                for (int xx = 0; xx < dims.x; xx++) f[xx] = d[Index(dims, xx, yy, zz)];
                Edt1D(f, dd, dims.x, v, z);
                for (int xx = 0; xx < dims.x; xx++) d[Index(dims, xx, yy, zz)] = dd[xx];
            }

            for (int zz = 0; zz < dims.z; zz++)
            for (int xx = 0; xx < dims.x; xx++)
            {
                for (int yy = 0; yy < dims.y; yy++) f[yy] = d[Index(dims, xx, yy, zz)];
                Edt1D(f, dd, dims.y, v, z);
                for (int yy = 0; yy < dims.y; yy++) d[Index(dims, xx, yy, zz)] = dd[yy];
            }

            for (int yy = 0; yy < dims.y; yy++)
            for (int xx = 0; xx < dims.x; xx++)
            {
                for (int zz = 0; zz < dims.z; zz++) f[zz] = d[Index(dims, xx, yy, zz)];
                Edt1D(f, dd, dims.z, v, z);
                for (int zz = 0; zz < dims.z; zz++) d[Index(dims, xx, yy, zz)] = Mathf.Sqrt(dd[zz]) * voxel;
            }

            return d;
        }

        /// <summary>Lower envelope of the parabolas rooted at each sample.</summary>
        static void Edt1D(float[] f, float[] d, int n, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = -k_Far;
            z[1] = k_Far;

            for (int q = 1; q < n; q++)
            {
                float s;
                while (true)
                {
                    int vk = v[k];
                    s = ((f[q] + q * q) - (f[vk] + vk * vk)) / (2f * q - 2f * vk);
                    if (k > 0 && s <= z[k]) k--;
                    else break;
                }

                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = k_Far;
            }

            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                int vk = v[k];
                d[q] = (q - vk) * (q - vk) + f[vk];
            }
        }

        // ---- fill ---------------------------------------------------------------------------

        /// <summary>
        /// Everything reachable from the seeds without crossing a blocked cell.
        ///
        /// Six-connected on purpose: a diagonal fill would squeeze through a one-cell seam
        /// between two cells that touch only at a corner, which is exactly the crack between two
        /// interpenetrating parts that the film is supposed to pass over.
        /// </summary>
        static bool[] Flood(bool[] blocked, Vector3Int dims, List<int> seeds)
        {
            int n = dims.x * dims.y * dims.z;
            var reached = new bool[n];
            var queue = new Queue<int>();

            foreach (int i in seeds)
            {
                if (blocked[i] || reached[i]) continue;
                reached[i] = true;
                queue.Enqueue(i);
            }

            void Visit(int x, int y, int z)
            {
                int j = Index(dims, x, y, z);
                if (blocked[j] || reached[j]) return;
                reached[j] = true;
                queue.Enqueue(j);
            }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % dims.x;
                int y = (i / dims.x) % dims.y;
                int z = i / (dims.x * dims.y);

                if (x > 0) Visit(x - 1, y, z);
                if (x < dims.x - 1) Visit(x + 1, y, z);
                if (y > 0) Visit(x, y - 1, z);
                if (y < dims.y - 1) Visit(x, y + 1, z);
                if (z > 0) Visit(x, y, z - 1);
                if (z < dims.z - 1) Visit(x, y, z + 1);
            }

            return reached;
        }

        /// <summary>Every cell on the grid's six faces: the fill comes at the object from outside.</summary>
        static List<int> BoundarySeeds(Vector3Int dims)
        {
            var seeds = new List<int>();

            for (int z = 0; z < dims.z; z++)
            for (int y = 0; y < dims.y; y++)
            {
                seeds.Add(Index(dims, 0, y, z));
                seeds.Add(Index(dims, dims.x - 1, y, z));
            }
            for (int z = 0; z < dims.z; z++)
            for (int x = 0; x < dims.x; x++)
            {
                seeds.Add(Index(dims, x, 0, z));
                seeds.Add(Index(dims, x, dims.y - 1, z));
            }
            for (int y = 0; y < dims.y; y++)
            for (int x = 0; x < dims.x; x++)
            {
                seeds.Add(Index(dims, x, y, 0));
                seeds.Add(Index(dims, x, y, dims.z - 1));
            }

            return seeds;
        }

        /// <summary>
        /// The one cell the given point lands in, or the nearest open cell to it. The search
        /// matters: the seed is a point someone picked in the middle of a room, and after the
        /// dilation that cell can easily be inside the sealed band around a nearby surface --
        /// which would silently produce an empty fill and a film covering nothing.
        /// </summary>
        static List<int> InteriorSeed(bool[] blocked, Vector3Int dims, Vector3 origin, float voxel, Vector3 point)
        {
            var seeds = new List<int>();

            Vector3 g = (point - origin) / voxel;
            int sx = Mathf.Clamp(Mathf.RoundToInt(g.x), 0, dims.x - 1);
            int sy = Mathf.Clamp(Mathf.RoundToInt(g.y), 0, dims.y - 1);
            int sz = Mathf.Clamp(Mathf.RoundToInt(g.z), 0, dims.z - 1);

            int reach = Mathf.Max(dims.x, Mathf.Max(dims.y, dims.z)) / 2;
            for (int r = 0; r <= reach; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    // Shell only; the interior of the cube was covered by a smaller r.
                    if (r > 0 && Mathf.Abs(dx) != r && Mathf.Abs(dy) != r && Mathf.Abs(dz) != r) continue;

                    int x = sx + dx, y = sy + dy, z = sz + dz;
                    if (x < 0 || y < 0 || z < 0 || x >= dims.x || y >= dims.y || z >= dims.z) continue;

                    int i = Index(dims, x, y, z);
                    if (blocked[i]) continue;

                    seeds.Add(i);
                    return seeds;
                }
            }

            return seeds;
        }

        // ---- isosurface ---------------------------------------------------------------------

        /// <summary>
        /// Naive surface nets: one vertex per sign-changing cell, placed at the average of the
        /// crossings on its edges, and a quad across every sign-changing grid edge. Chosen over
        /// marching cubes because it gives a far more even tessellation -- the film's crease
        /// shading reads off the surface normal, and marching cubes' slivers would show up as
        /// glinting along every sliver edge.
        /// </summary>
        static void SurfaceNets(float[] field, Vector3Int dims, Vector3 origin, float voxel,
                                out List<Vector3> verts, out List<int> tris)
        {
            // Written through locals rather than the out parameters directly: TryQuad below is a
            // local function, and a local function cannot capture an out parameter.
            var outVerts = new List<Vector3>();
            var outTris = new List<int>();
            verts = outVerts;
            tris = outTris;

            var cellVert = new int[dims.x * dims.y * dims.z];
            for (int i = 0; i < cellVert.Length; i++) cellVert[i] = -1;

            // The eight corners of a cell, and the twelve edges as corner pairs.
            var corner = new[]
            {
                new Vector3Int(0,0,0), new Vector3Int(1,0,0), new Vector3Int(1,1,0), new Vector3Int(0,1,0),
                new Vector3Int(0,0,1), new Vector3Int(1,0,1), new Vector3Int(1,1,1), new Vector3Int(0,1,1),
            };
            var edge = new[]
            {
                (0,1),(1,2),(2,3),(3,0),
                (4,5),(5,6),(6,7),(7,4),
                (0,4),(1,5),(2,6),(3,7),
            };

            var cv = new float[8];

            for (int z = 0; z < dims.z - 1; z++)
            for (int y = 0; y < dims.y - 1; y++)
            for (int x = 0; x < dims.x - 1; x++)
            {
                int mask = 0;
                for (int c = 0; c < 8; c++)
                {
                    var o = corner[c];
                    cv[c] = field[Index(dims, x + o.x, y + o.y, z + o.z)];
                    if (cv[c] < 0f) mask |= 1 << c;
                }

                if (mask == 0 || mask == 255) continue;

                Vector3 sum = Vector3.zero;
                int crossings = 0;

                foreach (var (ea, eb) in edge)
                {
                    float fa = cv[ea], fb = cv[eb];
                    if ((fa < 0f) == (fb < 0f)) continue;

                    float t = fa / (fa - fb);
                    Vector3 pa = corner[ea], pb = corner[eb];
                    sum += Vector3.Lerp(pa, pb, t);
                    crossings++;
                }

                if (crossings == 0) continue;

                Vector3 local = sum / crossings;
                cellVert[Index(dims, x, y, z)] = outVerts.Count;
                outVerts.Add(origin + new Vector3((x + local.x) * voxel, (y + local.y) * voxel, (z + local.z) * voxel));
            }

            // A quad per sign-changing grid edge, spanning the four cells around it.
            for (int z = 1; z < dims.z - 1; z++)
            for (int y = 1; y < dims.y - 1; y++)
            for (int x = 1; x < dims.x - 1; x++)
            {
                float f0 = field[Index(dims, x, y, z)];

                TryQuad(f0, field[Index(dims, x + 1, y, z)],
                        cellVert[Index(dims, x, y - 1, z - 1)], cellVert[Index(dims, x, y, z - 1)],
                        cellVert[Index(dims, x, y, z)], cellVert[Index(dims, x, y - 1, z)]);

                TryQuad(f0, field[Index(dims, x, y + 1, z)],
                        cellVert[Index(dims, x - 1, y, z - 1)], cellVert[Index(dims, x - 1, y, z)],
                        cellVert[Index(dims, x, y, z)], cellVert[Index(dims, x, y, z - 1)]);

                TryQuad(f0, field[Index(dims, x, y, z + 1)],
                        cellVert[Index(dims, x - 1, y - 1, z)], cellVert[Index(dims, x, y - 1, z)],
                        cellVert[Index(dims, x, y, z)], cellVert[Index(dims, x - 1, y, z)]);
            }

            void TryQuad(float fa, float fb, int a, int b, int c, int d)
            {
                if ((fa < 0f) == (fb < 0f)) return;
                if (a < 0 || b < 0 || c < 0 || d < 0) return;

                // Wound so the face points away from the solid side.
                if (fa < 0f)
                {
                    outTris.Add(a); outTris.Add(b); outTris.Add(c);
                    outTris.Add(a); outTris.Add(c); outTris.Add(d);
                }
                else
                {
                    outTris.Add(a); outTris.Add(c); outTris.Add(b);
                    outTris.Add(a); outTris.Add(d); outTris.Add(c);
                }
            }
        }

        // ---- smoothing ------------------------------------------------------------------------

        /// <summary>
        /// Taubin: a positive Laplacian pass followed by a slightly larger negative one, which
        /// takes the voxel staircase off without the steady shrinkage plain smoothing causes.
        /// Shrinkage matters more here than it looks -- the film losing a millimetre a pass walks
        /// straight back down into the gaps the span radius was there to bridge.
        /// </summary>
        static void TaubinSmooth(List<Vector3> verts, List<int> tris, int iterations)
        {
            if (iterations <= 0) return;

            var neighbours = new List<int>[verts.Count];
            for (int i = 0; i < verts.Count; i++) neighbours[i] = new List<int>(6);

            void Link(int a, int b)
            {
                if (!neighbours[a].Contains(b)) neighbours[a].Add(b);
            }

            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                Link(a, b); Link(b, a);
                Link(b, c); Link(c, b);
                Link(c, a); Link(a, c);
            }

            const float lambda = 0.50f;
            const float mu = -0.53f;
            var scratch = new Vector3[verts.Count];

            for (int iter = 0; iter < iterations; iter++)
            {
                float w = (iter % 2 == 0) ? lambda : mu;

                for (int i = 0; i < verts.Count; i++)
                {
                    var list = neighbours[i];
                    if (list.Count == 0) { scratch[i] = verts[i]; continue; }

                    Vector3 sum = Vector3.zero;
                    for (int k = 0; k < list.Count; k++) sum += verts[list[k]];
                    scratch[i] = verts[i] + (sum / list.Count - verts[i]) * w;
                }

                for (int i = 0; i < verts.Count; i++) verts[i] = scratch[i];
            }
        }

        // ---- sampling -------------------------------------------------------------------------

        static float SampleTrilinear(float[] field, Vector3Int dims, Vector3 origin, float voxel, Vector3 p)
        {
            Vector3 g = (p - origin) / voxel;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(g.x), 0, dims.x - 2);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(g.y), 0, dims.y - 2);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(g.z), 0, dims.z - 2);

            float tx = Mathf.Clamp01(g.x - x0), ty = Mathf.Clamp01(g.y - y0), tz = Mathf.Clamp01(g.z - z0);

            float c000 = field[Index(dims, x0, y0, z0)];
            float c100 = field[Index(dims, x0 + 1, y0, z0)];
            float c010 = field[Index(dims, x0, y0 + 1, z0)];
            float c110 = field[Index(dims, x0 + 1, y0 + 1, z0)];
            float c001 = field[Index(dims, x0, y0, z0 + 1)];
            float c101 = field[Index(dims, x0 + 1, y0, z0 + 1)];
            float c011 = field[Index(dims, x0, y0 + 1, z0 + 1)];
            float c111 = field[Index(dims, x0 + 1, y0 + 1, z0 + 1)];

            float x00 = Mathf.Lerp(c000, c100, tx), x10 = Mathf.Lerp(c010, c110, tx);
            float x01 = Mathf.Lerp(c001, c101, tx), x11 = Mathf.Lerp(c011, c111, tx);

            return Mathf.Lerp(Mathf.Lerp(x00, x10, ty), Mathf.Lerp(x01, x11, ty), tz);
        }

        // ---- reporting --------------------------------------------------------------------------

        /// <summary>
        /// How many separate pieces the film came out as. One is the goal: it means the span
        /// radius was enough to bridge every gap in the object, which is the difference between
        /// a sheet over the whole thing and a set of bags around its parts.
        /// </summary>
        static int CountShells(int vertCount, List<int> tris)
        {
            var parent = new int[vertCount];
            for (int i = 0; i < vertCount; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            for (int t = 0; t < tris.Count; t += 3)
            {
                Union(tris[t], tris[t + 1]);
                Union(tris[t + 1], tris[t + 2]);
            }

            var roots = new HashSet<int>();
            for (int i = 0; i < vertCount; i++) roots.Add(Find(i));
            return roots.Count;
        }
    }
}
