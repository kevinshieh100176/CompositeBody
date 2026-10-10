using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CompositeBody.PointClouds.EditorTools
{
    /// <summary>
    /// Writes a .pcache from a point-cloud Mesh, so a scan can be driven by VFX Graph.
    ///
    /// The format is written here rather than through the package's own PCache class, which is
    /// <c>internal</c> and visible only to Unity's test assemblies. It is a short text header
    /// followed by interleaved values, and the one detail that matters is the line ending: the
    /// package's reader takes a line as finished at the first \r or \n *after* at least one
    /// character, so a CRLF header leaves the \n to start the following line and every line
    /// after the first arrives with a leading newline. Its parser compares words exactly, so
    /// "\nformat" does not match "format". Writing LF only keeps every line clean and leaves
    /// the data offset exact.
    ///
    /// <b>A pcache costs precision.</b> The importer turns float properties into RGBAHalf
    /// textures, so positions land in 16-bit floats -- about 0.5 mm of quantisation at a metre
    /// from the origin, against the 0.94 mm spacing these scans reach. That is close enough to
    /// the point spacing to show up as a faint lattice. Two things follow: the cloud is centred
    /// before baking by default, which halves the magnitudes and so halves the error; and if
    /// the quantisation shows, sample the Mesh directly in VFX Graph instead, where positions
    /// stay float32.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.PointClouds.EditorTools.BakePointCache.Run
    ///   -source "Assets/_models/PointCloud/figure.ply"
    ///   [-out "Assets/_models/PointCloud/figure.pcache"] [-maxPoints 300000] [-noCenter] [-ascii]
    /// </summary>
    public static class BakePointCache
    {
        [MenuItem("Assets/Bake Point Cache (.pcache)", true)]
        static bool BakeSelectedValidate() => TryGetMesh(Selection.activeObject, out _);

        [MenuItem("Assets/Bake Point Cache (.pcache)")]
        static void BakeSelected()
        {
            if (!TryGetMesh(Selection.activeObject, out Mesh mesh)) return;

            string sourcePath = AssetDatabase.GetAssetPath(Selection.activeObject);
            string outPath = Path.ChangeExtension(sourcePath, ".pcache");
            if (Bake(mesh, outPath, 0, true, false, GlitchSettings.None, out string message))
                Debug.Log($"[PCache] {message}");
            else
                Debug.LogError($"[PCache] {message}");
        }

        public static void Run()
        {
            string source = GetArg("-source");
            if (string.IsNullOrEmpty(source))
            {
                Debug.LogError("[PCache] RESULT: FAIL - pass -source \"Assets/.../cloud.ply\".");
                return;
            }
            source = source.Replace('\\', '/');

            var asset = AssetDatabase.LoadMainAssetAtPath(source);
            if (asset == null)
            {
                Debug.LogError($"[PCache] RESULT: FAIL - nothing at {source}.");
                return;
            }
            if (!TryGetMesh(asset, out Mesh mesh))
            {
                Debug.LogError($"[PCache] RESULT: FAIL - {source} holds no Mesh.");
                return;
            }

            string outPath = GetArg("-out");
            if (string.IsNullOrEmpty(outPath)) outPath = Path.ChangeExtension(source, ".pcache");
            outPath = outPath.Replace('\\', '/');

            int maxPoints = 0;
            string maxArg = GetArg("-maxPoints");
            if (!string.IsNullOrEmpty(maxArg)) int.TryParse(maxArg, out maxPoints);

            bool center = !HasFlag("-noCenter");
            bool ascii = HasFlag("-ascii");

            GlitchSettings glitch = GlitchSettings.None;
            string glitchArg = GetArg("-glitch");
            if (!string.IsNullOrEmpty(glitchArg) && float.TryParse(glitchArg, out float amount) && amount > 0f)
            {
                glitch = GlitchSettings.Default;
                glitch.amount = Mathf.Clamp01(amount);
                string seedArg = GetArg("-glitchSeed");
                if (!string.IsNullOrEmpty(seedArg) && float.TryParse(seedArg, out float seed))
                    glitch.seed = seed;
                string shiftArg = GetArg("-glitchShift");
                if (!string.IsNullOrEmpty(shiftArg) && float.TryParse(shiftArg, out float shift))
                    glitch.shift = shift;
            }

            if (Bake(mesh, outPath, maxPoints, center, ascii, glitch, out string message))
                Debug.Log($"[PCache] {message}\n[PCache] RESULT: PASS");
            else
                Debug.LogError($"[PCache] {message}\n[PCache] RESULT: FAIL");
        }

        /// <summary>Finds the Mesh on whatever was selected: a Mesh, a .ply's root GameObject, or a prefab.</summary>
        static bool TryGetMesh(UnityEngine.Object asset, out Mesh mesh)
        {
            mesh = null;
            if (asset == null) return false;

            if (asset is Mesh direct) { mesh = direct; return true; }

            if (asset is GameObject go)
            {
                var filter = go.GetComponentInChildren<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) { mesh = filter.sharedMesh; return true; }
            }

            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return false;
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is Mesh m) { mesh = m; return true; }
            }
            return false;
        }

        /// <summary>
        /// A frozen glitch baked into the points themselves.
        ///
        /// Needed because a pcache carries positions, not a shader: VFX Graph reads the baked
        /// coordinates and nothing in the CompositeBody/PointCloud shader reaches it. So the
        /// still, misplaced version of the figure has to exist as data. The maths is the same
        /// as PointCloudCommon.hlsl's PointGlitch at rate 0 -- deliberately duplicated rather
        /// than shared, because HLSL and C# cannot share a function and a baked cloud that
        /// disagreed with the live one would be worse than two copies that are checked against
        /// each other in the preview.
        /// </summary>
        public struct GlitchSettings
        {
            public float amount;
            public float slabs;
            public float shift;
            public float dropout;
            public float scatter;
            public float seed;

            public static GlitchSettings None => new() { amount = 0f };

            public bool active => amount > 0.0001f;

            public static GlitchSettings Default => new()
            {
                amount = 0.45f, slabs = 48f, shift = 0.05f,
                dropout = 0.25f, scatter = 0.18f, seed = 3f
            };
        }

        static float GlitchHash(float a, float b, float seed)
        {
            var p = new Vector3(a * 0.1031f, b * 0.1030f, seed * 0.0973f);
            p = new Vector3(p.x - Mathf.Floor(p.x), p.y - Mathf.Floor(p.y), p.z - Mathf.Floor(p.z));
            float d = Vector3.Dot(p, new Vector3(p.y, p.z, p.x) + Vector3.one * 19.19f);
            p += Vector3.one * d;
            float v = (p.x + p.y) * p.z;
            return v - Mathf.Floor(v);
        }

        /// <summary>Returns false when the point is dropped entirely.</summary>
        static bool ApplyGlitch(ref Vector3 position, Vector3 basePosition, in GlitchSettings g)
        {
            if (!g.active) return true;

            float slab = Mathf.Floor(basePosition.y * g.slabs);
            float pick = GlitchHash(slab, 0f, g.seed);
            if (pick > g.amount) return true;

            float dx = GlitchHash(slab, 17f, g.seed) * 2f - 1f;
            float dz = GlitchHash(slab, 41f, g.seed) * 2f - 1f;
            position.x += dx * g.shift;
            position.z += dz * g.shift * 0.6f;

            float hash = PointHash(basePosition);
            if (GlitchHash(hash * 977f, 0f, g.seed + 3.7f) > 0.93f)
            {
                position += (new Vector3(
                    GlitchHash(hash, 0f, g.seed + 11f),
                    GlitchHash(hash, 0f, g.seed + 23f),
                    GlitchHash(hash, 0f, g.seed + 37f)) - Vector3.one * 0.5f) * g.scatter;
            }

            return GlitchHash(slab, 73f, g.seed) >= g.dropout * g.amount;
        }

        /// <summary>Mirror of PointHash in PointCloudCommon.hlsl.</summary>
        static float PointHash(Vector3 p)
        {
            var q = new Vector3(p.x * 0.1031f, p.y * 0.1030f, p.z * 0.0973f);
            q = new Vector3(q.x - Mathf.Floor(q.x), q.y - Mathf.Floor(q.y), q.z - Mathf.Floor(q.z));
            float d = Vector3.Dot(q, new Vector3(q.y, q.z, q.x) + Vector3.one * 33.33f);
            q += Vector3.one * d;
            float v = (q.x + q.y) * q.z;
            return v - Mathf.Floor(v);
        }

        public static bool Bake(Mesh mesh, string outPath, int maxPoints, bool center, bool ascii,
                                out string message)
            => Bake(mesh, outPath, maxPoints, center, ascii, GlitchSettings.None, out message);

        public static bool Bake(Mesh mesh, string outPath, int maxPoints, bool center, bool ascii,
                                GlitchSettings glitch, out string message)
        {
            if (mesh == null) { message = "no mesh"; return false; }

            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors;
            if (vertices == null || vertices.Length == 0) { message = $"{mesh.name} has no vertices"; return false; }

            // A Quads mesh carries each point four times. Baking that would quadruple the cache
            // and put four particles on every point, so the duplicates are collapsed here
            // rather than left for whoever notices the particle count is wrong.
            int stride = 1;
            if (mesh.GetTopology(0) == MeshTopology.Triangles && LooksLikeQuadExpanded(mesh))
                stride = 4;

            int available = vertices.Length / stride;
            int step = 1;
            if (maxPoints > 0 && maxPoints < available)
                step = Mathf.Max(2, Mathf.CeilToInt(available / (float)maxPoints));

            var positions = new System.Collections.Generic.List<Vector3>(available / step + 1);
            var tints = new System.Collections.Generic.List<Vector4>(available / step + 1);
            bool hasColor = colors != null && colors.Length == vertices.Length;

            int dropped = 0;
            for (int i = 0; i < available; i += step)
            {
                int v = i * stride;
                Vector3 p = vertices[v];
                if (!ApplyGlitch(ref p, vertices[v], glitch)) { dropped++; continue; }

                positions.Add(p);
                tints.Add(hasColor
                    ? new Vector4(colors[v].r, colors[v].g, colors[v].b, colors[v].a)
                    : new Vector4(0.78f, 0.78f, 0.78f, 1f));
            }

            Vector3 offset = Vector3.zero;
            if (center && positions.Count > 0)
            {
                var bounds = new Bounds(positions[0], Vector3.zero);
                for (int i = 1; i < positions.Count; i++) bounds.Encapsulate(positions[i]);
                offset = bounds.center;
                for (int i = 0; i < positions.Count; i++) positions[i] -= offset;
            }

            try
            {
                Write(outPath, positions, tints, ascii);
            }
            catch (Exception e)
            {
                message = $"could not write {outPath}: {e.Message}";
                return false;
            }

            AssetDatabase.ImportAsset(outPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            if (!Verify(outPath, positions.Count, out string problem))
            {
                message = $"wrote {outPath} but Unity could not read it back: {problem}";
                return false;
            }

            float worst = 0f;
            foreach (Vector3 p in positions)
                worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs(p.x), Mathf.Max(Mathf.Abs(p.y), Mathf.Abs(p.z))));

            message = $"{mesh.name} -> {outPath}: {positions.Count:N0} points" +
                      (stride == 4 ? " (collapsed from a Quads mesh)" : "") +
                      (step > 1 ? $", every {step}th" : "") +
                      (glitch.active ? $", glitched at {glitch.amount:0.00} seed {glitch.seed:0} ({dropped:N0} dropped)" : "") +
                      (center ? $", centred by {offset.x:0.00},{offset.y:0.00},{offset.z:0.00}" : "") +
                      $". float16 quantisation at the far edge: ~{HalfStep(worst) * 1000f:0.00} mm";
            return true;
        }

        /// <summary>
        /// Reads the file back through Unity's own pcache importer and checks it arrived whole.
        ///
        /// Worth doing on every bake, because the format is hand-written against an internal
        /// reader: a wrong line ending or a stride that does not match the declared properties
        /// produces a file the importer accepts and silently mis-reads, and the first symptom
        /// would be a VFX Graph full of particles at the origin.
        ///
        /// PointCacheAsset is internal, so it is inspected by reflection. A failure to reflect
        /// is not treated as a failed bake -- that would make the baker break on a package
        /// upgrade that only renamed a field -- but anything it can read is checked.
        /// </summary>
        static bool Verify(string outPath, int expected, out string problem)
        {
            problem = null;
            var asset = AssetDatabase.LoadMainAssetAtPath(outPath);
            if (asset == null)
            {
                problem = "the importer produced no asset";
                return false;
            }

            Type type = asset.GetType();
            if (type.Name != "PointCacheAsset")
            {
                problem = $"the importer produced a {type.Name}, not a PointCacheAsset";
                return false;
            }

            var countField = type.GetField("PointCount");
            if (countField != null)
            {
                int actual = (int)countField.GetValue(asset);
                if (actual != expected)
                {
                    problem = $"PointCount is {actual:N0}, expected {expected:N0}";
                    return false;
                }
            }

            var surfacesField = type.GetField("surfaces");
            if (surfacesField != null)
            {
                var surfaces = surfacesField.GetValue(asset) as Texture2D[];
                if (surfaces == null || surfaces.Length < 2)
                {
                    problem = $"expected position and colour surfaces, got {surfaces?.Length ?? 0}";
                    return false;
                }
                foreach (Texture2D s in surfaces)
                {
                    if (s == null) { problem = "a surface came back null"; return false; }
                }
                Debug.Log($"[PCache] read back: PointCount ok, {surfaces.Length} surfaces at " +
                          $"{surfaces[0].width}x{surfaces[0].height} {surfaces[0].format}");
            }
            return true;
        }

        /// <summary>
        /// True when every run of four vertices shares a position, which is what
        /// <see cref="PlyImporter"/>'s Quads topology produces.
        /// </summary>
        static bool LooksLikeQuadExpanded(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            if (v.Length < 8 || v.Length % 4 != 0) return false;

            int samples = Mathf.Min(64, v.Length / 4);
            int step = Mathf.Max(1, (v.Length / 4) / samples);
            for (int i = 0; i < v.Length / 4; i += step)
            {
                int b = i * 4;
                if (v[b] != v[b + 1] || v[b] != v[b + 2] || v[b] != v[b + 3]) return false;
            }
            return true;
        }

        /// <summary>Spacing between representable float16 values near <paramref name="magnitude"/>.</summary>
        static float HalfStep(float magnitude)
        {
            if (magnitude <= 0f) return 0f;
            int exponent = Mathf.FloorToInt(Mathf.Log(magnitude, 2f));
            return Mathf.Pow(2f, exponent - 10);   // 10 mantissa bits
        }

        static void Write(string path, System.Collections.Generic.List<Vector3> positions,
                          System.Collections.Generic.List<Vector4> colors, bool ascii)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var header = new StringBuilder();
            // LF only, and '\n' written explicitly rather than through AppendLine, which would
            // emit CRLF on Windows. See the note on the class.
            header.Append("pcache\n");
            header.Append(ascii ? "format ascii 1.0\n" : "format binary 1.0\n");
            header.Append("comment Baked by CompositeBody BakePointCache\n");
            header.Append($"elements {positions.Count}\n");
            header.Append("property float position.x\n");
            header.Append("property float position.y\n");
            header.Append("property float position.z\n");
            header.Append("property float color.r\n");
            header.Append("property float color.g\n");
            header.Append("property float color.b\n");
            header.Append("property float color.a\n");
            header.Append("end_header\n");

            using var stream = File.Create(path);
            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);

            if (ascii)
            {
                var sb = new StringBuilder();
                var culture = CultureInfo.InvariantCulture;
                for (int i = 0; i < positions.Count; i++)
                {
                    Vector3 p = positions[i];
                    Vector4 c = colors[i];
                    sb.Append(p.x.ToString(culture)).Append(' ')
                      .Append(p.y.ToString(culture)).Append(' ')
                      .Append(p.z.ToString(culture)).Append(' ')
                      .Append(c.x.ToString(culture)).Append(' ')
                      .Append(c.y.ToString(culture)).Append(' ')
                      .Append(c.z.ToString(culture)).Append(' ')
                      .Append(c.w.ToString(culture)).Append('\n');

                    if (sb.Length > 1 << 16)
                    {
                        byte[] chunk = Encoding.ASCII.GetBytes(sb.ToString());
                        stream.Write(chunk, 0, chunk.Length);
                        sb.Clear();
                    }
                }
                if (sb.Length > 0)
                {
                    byte[] chunk = Encoding.ASCII.GetBytes(sb.ToString());
                    stream.Write(chunk, 0, chunk.Length);
                }
                return;
            }

            // Interleaved per element, in the order the properties were declared, little-endian
            // -- which is what BinaryWriter produces and so what the reader expects.
            const int k_Batch = 4096;
            var buffer = new byte[k_Batch * 7 * sizeof(float)];
            int inBuffer = 0;
            int at = 0;

            void Put(float value)
            {
                BitConverter.TryWriteBytes(new Span<byte>(buffer, at, 4), value);
                at += 4;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 p = positions[i];
                Vector4 c = colors[i];
                Put(p.x); Put(p.y); Put(p.z);
                Put(c.x); Put(c.y); Put(c.z); Put(c.w);
                inBuffer++;

                if (inBuffer == k_Batch)
                {
                    stream.Write(buffer, 0, at);
                    at = 0;
                    inBuffer = 0;
                }
            }
            if (at > 0) stream.Write(buffer, 0, at);
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }

        static bool HasFlag(string name)
        {
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (a == name) return true;
            }
            return false;
        }
    }
}
