using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompositeBody.PointClouds;

namespace CompositeBody.PointClouds.EditorTools
{
    /// <summary>
    /// Turns built scene geometry into a point cloud, so a modelled room can stand next to the
    /// scanned 真人 in the same language.
    ///
    /// Everything else in this folder starts from a capture: a .ply arrives, <see cref="PlyImporter"/>
    /// turns it into a cloud, and the look follows. The rooms did not arrive that way -- they
    /// are FBX models with materials and a baked lightmap -- so this walks their triangles and
    /// makes the capture that was never taken.
    ///
    /// Three decisions worth knowing about:
    ///
    /// <list type="bullet">
    /// <item><b>It writes a .ply rather than a Mesh asset.</b> The project serialises assets as
    /// text, and half a million points as a text Mesh is a couple of hundred megabytes of hex in
    /// the repository. A binary .ply is 14 MB, it imports through the path every other cloud here
    /// already uses, and the density, topology and point budget stay adjustable in the importer
    /// inspector afterwards without re-running anything.</item>
    ///
    /// <item><b>It does not bake the lighting in, and that is a decision about this scene.</b>
    /// S1_v3 is Shadowmask: its mixed lights hand their direct light to the renderer at runtime
    /// and leave only the bounce in the lightmap. Folding that lightmap into the point colour
    /// therefore keeps the bounce and throws the lamps away -- measured on S1-1, it took a room
    /// whose albedo averages 136,122,86 down to 21,13,9, two thirds of it black, which looks
    /// exactly like a correctly baked cloud of a very dark room and is not one. So the colour
    /// written is the surface's own, the normals are real, and the material is
    /// CompositeBody/PointCloudLit: the lights that reach the walls reach the cloud. Pass
    /// -bakeLighting to fold the irradiance in instead, which is the right choice only once
    /// the scene is fully baked.</item>
    ///
    /// <item><b>It bakes from the scene rather than the prefab</b> because the scene is what
    /// says which objects are actually on, carries the lightmaps that -bakeLighting needs, and
    /// is where these objects were picked.</item>
    ///
    /// <item><b>It samples by area, not per vertex.</b> Modelled geometry has vertices where the
    /// modeller needed them -- crowded along a moulding, four across a whole wall -- so using them
    /// as points gives a cloud that is dense where nothing is happening and empty where the room
    /// is. Area sampling spreads points evenly over the surface instead, which is what a scanner
    /// would have produced.</item>
    /// </list>
    ///
    /// Nothing existing is touched. The scene is opened read-only and never saved, the source
    /// objects are not modified, and everything written is new: a .ply, a material and a prefab
    /// per target, under <see cref="k_OutDir"/>. The prefab carries the source object's own local
    /// transform, so dropping it beside the original lands it exactly over it.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.PointClouds.EditorTools.BakeScenePointCloud.Run
    ///   [-scene "Assets/_Scenes/IX_Scene/S1_v3.unity"]
    ///   [-objects "Base_Model_V3,S1-1"] [-out "Assets/_models/Pointcloud/S1"]
    ///   [-maxPoints 1000000] [-spacing 0.02] [-bakeLighting] [-exposure 1] [-dryRun]
    ///   [-inside "CloudBox"] | [-box "cx,cy,cz,sx,sy,sz"] [-exclude "Cube.005,Plane"]
    ///
    /// Cropping is how a part of a room gets the density the whole of it cannot afford. The
    /// budget is spent on what survives the crop, so the same million points that land 19.2 cm
    /// apart over the whole base model land about 2 cm apart over a tenth of it. Triangles are
    /// clipped to the box rather than tested against it, because the landscape's are large
    /// enough that a whole-triangle test has only wrong answers.
    /// </summary>
    public static class BakeScenePointCloud
    {
        const string k_DefaultScene = "Assets/_Scenes/IX_Scene/S1_v3.unity";
        const string k_DefaultTargets = "Base_Model_V3,S1-1";
        const string k_OutDir = "Assets/_models/Pointcloud/S1";

        /// <summary>
        /// Points per cloud. A room is orders of magnitude more surface than a figure, so a
        /// figure's density is not affordable here -- S1-1 alone is 1,400 m^2 against a body's
        /// two, and the base model is 37,000. A million points is about 3.5 cm apart on the
        /// room and four million quad vertices, which is as much as two of these can cost each
        /// other on a PCVR frame. Raise it with -maxPoints for a cloud meant to be walked up
        /// to; the spacing it actually achieved is logged either way, so it is never a guess.
        /// </summary>
        const int k_DefaultBudget = 1_000_000;

        /// <summary>Metres between points, when the budget allows it. The budget usually wins.</summary>
        const float k_DefaultSpacing = 0.02f;

        /// <summary>Point size as a fraction of the spacing the bake actually achieved.</summary>
        const float k_PointSizeFactor = 0.8f;

        /// <summary>
        /// Working size for a readable copy of an albedo map. Points land centimetres apart, so
        /// nothing finer than this survives the sampling anyway, and a room's worth of textures
        /// at full size does not fit in memory.
        /// </summary>
        const int k_AlbedoSize = 512;

        /// <summary>Lightmaps get more, because a shadow edge across a wall is low frequency but visible.</summary>
        const int k_LightmapSize = 1024;

        static readonly string[] k_ColorProperties = { "_BaseColor", "_Color" };
        static readonly string[] k_MapProperties = { "_BaseMap", "_MainTex" };

        /// <summary>A cloud is not a source of geometry. Re-baking a scene that holds one skips it.</summary>
        static readonly string[] k_CloudShaders =
        {
            "CompositeBody/PointCloud",
            "CompositeBody/PointCloudLit",
            "CompositeBody/PointCloudFro",
        };

        sealed class Settings
        {
            public int budget = k_DefaultBudget;
            public float spacing = k_DefaultSpacing;
            public bool lighting;
            public float exposure = 1f;
            public bool dryRun;
            public string outDir = k_OutDir;
            public Crop crop;

            /// <summary>Renderer names to leave out entirely, matched exactly.</summary>
            public readonly HashSet<string> exclude = new();
        }

        // ------------------------------------------------------------------------------------
        // Entry points
        // ------------------------------------------------------------------------------------

        [MenuItem("Tools/Point Cloud/Bake From Selection", true)]
        static bool BakeSelectionValidate() =>
            Selection.activeTransform != null && Selection.activeTransform.gameObject.scene.IsValid();

        [MenuItem("Tools/Point Cloud/Bake From Selection")]
        static void BakeSelection()
        {
            Transform[] targets = Selection.transforms
                .Where(t => t.gameObject.scene.IsValid())
                .ToArray();
            if (targets.Length == 0)
            {
                Debug.LogError("[Cloud] Select one or more objects in an open scene first.");
                return;
            }

            var settings = new Settings();
            try
            {
                foreach (Transform target in targets)
                {
                    if (Bake(target, settings, out string message)) Debug.Log($"[Cloud] {message}");
                    else Debug.LogError($"[Cloud] {message}");
                }
            }
            finally
            {
                ReleaseTextures();
            }
            AssetDatabase.SaveAssets();
        }

        public static void Run()
        {
            string scenePath = (GetArg("-scene") ?? k_DefaultScene).Replace('\\', '/');
            string targets = GetArg("-objects") ?? k_DefaultTargets;

            var settings = new Settings
            {
                lighting = HasFlag("-bakeLighting"),
                dryRun = HasFlag("-dryRun"),
                outDir = (GetArg("-out") ?? k_OutDir).Replace('\\', '/'),
            };
            if (int.TryParse(GetArg("-maxPoints"), out int budget) && budget > 0) settings.budget = budget;
            if (float.TryParse(GetArg("-spacing"), out float spacing) && spacing > 0f) settings.spacing = spacing;
            if (float.TryParse(GetArg("-exposure"), out float exposure) && exposure > 0f) settings.exposure = exposure;

            string exclude = GetArg("-exclude");
            if (!string.IsNullOrEmpty(exclude))
            {
                foreach (string name in exclude.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
                    settings.exclude.Add(name);
                Debug.Log($"[Cloud] leaving out: {string.Join(", ", settings.exclude)}.");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Debug.LogError($"[Cloud] RESULT: FAIL - no scene at {scenePath}.");
                return;
            }

            // Opened, read, and left alone. Nothing here calls SaveScene, so the version on disk
            // -- and whatever is unsaved in someone's editor -- is not what this is changing.
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Debug.Log($"[Cloud] {scenePath}: {LightmapSettings.lightmaps.Length} lightmaps loaded" +
                      (settings.lighting
                          ? ", baked into the point colour (-bakeLighting)."
                          : ", left alone -- the cloud is lit at runtime instead."));

            if (!ResolveCrop(scene, settings, out string cropError))
            {
                Debug.LogError($"[Cloud] RESULT: FAIL - {cropError}");
                return;
            }

            int done = 0, failed = 0;
            try
            {
                foreach (string name in targets.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0))
                {
                    Transform target = Find(scene, name);
                    if (target == null)
                    {
                        Debug.LogError($"[Cloud] nothing named '{name}' in {scenePath}.");
                        failed++;
                        continue;
                    }

                    if (Bake(target, settings, out string message))
                    {
                        Debug.Log($"[Cloud] {message}");
                        done++;
                    }
                    else
                    {
                        Debug.LogError($"[Cloud] {message}");
                        failed++;
                    }
                }
            }
            finally
            {
                ReleaseTextures();
            }

            AssetDatabase.SaveAssets();

            if (done == 0)
            {
                Debug.LogError("[Cloud] nothing was baked.\n[Cloud] RESULT: FAIL");
                return;
            }
            Debug.Log($"[Cloud] {done} baked, {failed} failed" +
                      (settings.dryRun ? " (dry run, nothing written)" : "") +
                      $".\n[Cloud] RESULT: {(failed == 0 ? "PASS" : "FAIL")}");
        }

        /// <summary>
        /// Works out the crop volume, from an object in the scene or from six numbers.
        ///
        /// The object form is the one to use: drop a Cube in, scale it over the part you want,
        /// pass its name. The catch is that the bake reads the scene from disk, so a box that
        /// has not been saved does not exist as far as this is concerned -- which is why the
        /// numeric form is here too. It takes a world-space centre and size, costs nothing to
        /// iterate on, and does not ask anyone to save a scene they are in the middle of.
        /// </summary>
        static bool ResolveCrop(Scene scene, Settings settings, out string error)
        {
            error = null;

            string inside = GetArg("-inside");
            string box = GetArg("-box");

            if (!string.IsNullOrEmpty(inside))
            {
                Transform t = Find(scene, inside);
                if (t == null)
                {
                    error = $"nothing named '{inside}' in the scene to crop to. " +
                            "Put a Cube over the part you want, name it, and save the scene.";
                    return false;
                }
                settings.crop = CropFrom(t);
            }
            else if (!string.IsNullOrEmpty(box))
            {
                string[] f = box.Split(',');
                if (f.Length != 6 ||
                    !float.TryParse(f[0], out float cx) || !float.TryParse(f[1], out float cy) ||
                    !float.TryParse(f[2], out float cz) || !float.TryParse(f[3], out float sx) ||
                    !float.TryParse(f[4], out float sy) || !float.TryParse(f[5], out float sz))
                {
                    error = $"-box wants six numbers, \"cx,cy,cz,sx,sy,sz\" in world space; got \"{box}\".";
                    return false;
                }

                var size = new Vector3(
                    Mathf.Max(1e-5f, Mathf.Abs(sx)),
                    Mathf.Max(1e-5f, Mathf.Abs(sy)),
                    Mathf.Max(1e-5f, Mathf.Abs(sz)));

                settings.crop = new Crop
                {
                    name = "Box",
                    worldSize = size,
                    worldToUnit =
                        Matrix4x4.Scale(new Vector3(1f / size.x, 1f / size.y, 1f / size.z)) *
                        Matrix4x4.Translate(-new Vector3(cx, cy, cz)),
                };
            }

            if (settings.crop != null)
            {
                Vector3 s = settings.crop.worldSize;
                Debug.Log($"[Cloud] cropping to {settings.crop.name}: " +
                          $"{s.x:0.00} x {s.y:0.00} x {s.z:0.00} m.");
            }
            return true;
        }

        // ------------------------------------------------------------------------------------
        // Baking
        // ------------------------------------------------------------------------------------

        static bool Bake(Transform source, Settings settings, out string message)
        {
            // A cropped bake gets its own name, so asking for one corner at 2 cm does not
            // overwrite the whole-object cloud someone is already using.
            string cloudName = Sanitise(source.name) +
                               (settings.crop != null ? "_" + Sanitise(settings.crop.name) : "");

            Region region = settings.crop != null
                ? new Region(settings.crop.worldToUnit * source.localToWorldMatrix)
                : default;

            List<Surface> surfaces = Gather(source, settings, out int skipped, out int excluded);
            if (surfaces.Count == 0)
            {
                message = $"{source.name} has no readable mesh under it ({skipped} renderers skipped). " +
                          "If the models are there, their importers have Read/Write disabled.";
                return false;
            }

            double area = 0;
            var areas = new List<(string name, double area, Surface surface)>(surfaces.Count);
            foreach (Surface s in surfaces)
            {
                double own = MeasureArea(s, region);
                if (own <= 0.0) continue;
                area += own;
                areas.Add((s.renderer.name, own, s));
            }

            if (area <= 0.0)
            {
                message = settings.crop != null
                    ? $"{source.name}: nothing inside {settings.crop.name}. Its box is " +
                      $"{settings.crop.worldSize.x:0.0} x {settings.crop.worldSize.y:0.0} x " +
                      $"{settings.crop.worldSize.z:0.0} m -- check it is actually over the geometry."
                    : $"{source.name}: {surfaces.Count} meshes, but no triangle area to sample.";
                return false;
            }

            // Which meshes the budget is actually going to. On a model that covers a landscape
            // this is the difference between "the cloud is sparse" and "one ground plane is
            // eating four fifths of the points".
            areas.Sort((x, y) => y.area.CompareTo(x.area));
            foreach ((string name, double own, Surface surface) in areas.Take(8))
            {
                Color32 colour = ProbeColour(surface, region);
                Debug.Log($"[Cloud]   {source.name}: {name} is {own:N0} m^2, {own / area:P0} " +
                          $"of the surface, colour {colour.r},{colour.g},{colour.b}.");
            }

            long wanted = (long)(area / (settings.spacing * (double)settings.spacing));
            bool budgetWon = wanted > settings.budget;
            int count = (int)Math.Max(1, Math.Min(wanted, settings.budget));
            double density = count / area;
            float achieved = (float)Math.Sqrt(area / count);

            if (settings.dryRun)
            {
                message = $"{source.name}: would sample {count:N0} points over {area:N1} m^2 of " +
                          $"surface from {areas.Count} of {surfaces.Count} meshes, " +
                          $"about {achieved * 100f:0.0} cm apart" +
                          (budgetWon ? $" ({wanted:N0} wanted, capped by -maxPoints)" : "") + ".";
                return true;
            }

            var points = new List<Point>(count + 1024);
            var rng = new System.Random(StableHash(cloudName));
            int unlit = 0, clipped = 0;
            foreach (Surface s in surfaces)
                Emit(s, region, density, rng, settings, points, ref unlit, ref clipped);

            if (points.Count == 0)
            {
                message = $"{source.name}: sampling produced no points.";
                return false;
            }

            // Through the asset database rather than Directory.CreateDirectory: a folder the
            // database has not been told about swallows the import of anything written into it.
            if (!AssetDatabase.IsValidFolder(settings.outDir))
            {
                Directory.CreateDirectory(settings.outDir);
                AssetDatabase.Refresh();
            }

            string plyPath = $"{settings.outDir}/{cloudName}.ply";
            WritePly(plyPath, points, source.name, achieved);

            Mesh mesh = ImportCloud(plyPath, out string importNote);
            if (mesh == null)
            {
                message = $"{source.name}: wrote {plyPath}, but it did not import into a mesh. {importNote}";
                return false;
            }

            string prefabPath = BuildPrefab(source, cloudName, mesh, achieved, settings, out string materialNote);

            // The mean colour is the one number that says whether the sampling actually read
            // anything. A cloud that came out black means the textures did not come back
            // readable; a cloud that came out white means the lightmap was read as an 8-bit
            // target and clipped. Both look like a successful run in every other respect.
            long r = 0, g = 0, b = 0;
            foreach (Point p in points) { r += p.color.r; g += p.color.g; b += p.color.b; }
            int n = points.Count;

            long bytes = new FileInfo(plyPath).Length;
            message = $"{source.name}: {points.Count:N0} points over {area:N0} m^2 " +
                      $"({achieved * 100f:0.0} cm apart) from {areas.Count} of {surfaces.Count} meshes, " +
                      $"{skipped} skipped" +
                      (excluded > 0 ? $", {excluded} excluded by name" : "") +
                      (settings.lighting && unlit > 0 ? $", {unlit:N0} points had no lightmap and used albedo only" : "") +
                      (clipped > 0 ? $", {clipped:N0} dropped through a cut-out" : "") +
                      (budgetWon
                          ? $". {settings.spacing * 100f:0.0} cm would have wanted {wanted:N0} " +
                            "points, so -maxPoints capped it -- raise it or crop tighter"
                          : "") +
                      $".\n[Cloud]   mean colour {r / n},{g / n},{b / n} (sRGB)" +
                      $".\n[Cloud]   {plyPath} ({bytes / (1024f * 1024f):0.0} MB), " +
                      $"{mesh.vertexCount:N0} vertices. {materialNote}" +
                      $"\n[Cloud]   {prefabPath} -- drop it under {PathOf(source.parent)} to land it over the original.";
            return true;
        }

        /// <summary>
        /// One mesh to sample, with the matrix that puts it in the cloud's own space.
        ///
        /// The cloud's space is the source object's local space rather than the world's, so the
        /// cloud's origin is the model's own pivot. That is what the shader's reveal and dissolve
        /// origins are measured in, and it is what makes the generated prefab droppable anywhere
        /// without the effect sliding off the geometry.
        /// </summary>
        readonly struct Surface
        {
            public readonly Mesh mesh;
            public readonly Material[] materials;
            public readonly MeshRenderer renderer;
            public readonly Matrix4x4 toCloud;

            public Surface(Mesh mesh, MeshRenderer renderer, Matrix4x4 toCloud)
            {
                this.mesh = mesh;
                this.renderer = renderer;
                this.materials = renderer.sharedMaterials;
                this.toCloud = toCloud;
            }
        }

        struct Point
        {
            public Vector3 position;
            public Vector3 normal;
            public Color32 color;
        }

        static List<Surface> Gather(Transform source, Settings settings, out int skipped,
                                    out int excluded)
        {
            var surfaces = new List<Surface>();
            skipped = 0;
            excluded = 0;

            Matrix4x4 worldToCloud = source.worldToLocalMatrix;

            foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();

                // Hidden geometry is not part of what the room currently looks like, so it is not
                // part of the cloud either -- but only hidden *within* the object being baked.
                if (mesh == null || renderer == null || !ActiveWithin(source, renderer))
                {
                    skipped++;
                    continue;
                }

                if (DrawsAPointCloud(renderer) || mesh.vertexCount == 0)
                {
                    skipped++;
                    continue;
                }

                // Named out by hand. The backdrops are the reason this exists: a sky plane and a
                // ground slab are real geometry with real materials, so nothing about them says
                // "not part of the cloud" -- but they are half the surface area in the box and
                // they sit between the viewer and everything worth seeing.
                if (settings.exclude.Contains(renderer.name))
                {
                    excluded++;
                    continue;
                }

                surfaces.Add(new Surface(mesh, renderer,
                    worldToCloud * filter.transform.localToWorldMatrix));
            }

            return surfaces;
        }

        /// <summary>
        /// Triangle area in the cloud's space, which is what the sampling density is per.
        ///
        /// Measured rather than taken from the bounds because the two disagree by a lot: a room is
        /// mostly empty volume, and its surface is what gets points.
        /// </summary>
        static double MeasureArea(Surface s, Region region)
        {
            Vector3[] vertices = s.mesh.vertices;
            if (vertices.Length == 0) return 0.0;

            double total = 0.0;
            for (int sub = 0; sub < s.mesh.subMeshCount; sub++)
            {
                if (s.mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                if (!Samples(MaterialFor(s, sub))) continue;
                int[] indices = s.mesh.GetTriangles(sub);

                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = s.toCloud.MultiplyPoint3x4(vertices[indices[i]]);
                    Vector3 b = s.toCloud.MultiplyPoint3x4(vertices[indices[i + 1]]);
                    Vector3 c = s.toCloud.MultiplyPoint3x4(vertices[indices[i + 2]]);

                    int pieces = Split(region, a, b, c);
                    for (int p = 0; p < pieces; p++)
                    {
                        Piece piece = s_Pieces[p];
                        Vector3 pa = At(a, b, c, piece.b0);
                        Vector3 pb = At(a, b, c, piece.b1);
                        Vector3 pc = At(a, b, c, piece.b2);
                        total += Vector3.Cross(pb - pa, pc - pa).magnitude * 0.5;
                    }
                }
            }
            return total;
        }

        /// <summary>
        /// Scatters points over one mesh's triangles at the given density.
        ///
        /// A triangle's share is its area times the density, which is almost never a whole number,
        /// so the fraction is carried into the next triangle rather than rounded away. Rounding
        /// down loses every triangle smaller than one point's worth -- which on a room is most of
        /// the trim and all of the small props -- and rounding up puts a point on each of the
        /// thousands of slivers a boolean leaves behind.
        /// </summary>
        static void Emit(Surface s, Region region, double density, System.Random rng,
                         Settings settings, List<Point> points, ref int unlit, ref int clipped)
        {
            Mesh mesh = s.mesh;
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length == 0) return;

            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;
            Vector2[] uv2 = mesh.uv2;

            bool hasNormals = normals.Length == vertices.Length;
            bool hasUv = uv.Length == vertices.Length;

            Lightmap lightmap = settings.lighting ? LightmapOf(s.renderer) : null;
            bool hasLightUv = lightmap != null && uv2.Length == vertices.Length;
            if (lightmap != null && !hasLightUv) lightmap = null;

            // Normals need the inverse transpose, not the matrix: a non-uniformly scaled prop
            // would otherwise come out with normals leaning off its surface.
            Matrix4x4 normalMatrix = s.toCloud.inverse.transpose;

            double carry = 0.0;

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                Material material = MaterialFor(s, sub);
                if (!Samples(material)) continue;
                int[] indices = mesh.GetTriangles(sub);
                Look look = LookOf(material);

                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    Vector3 a = s.toCloud.MultiplyPoint3x4(vertices[ia]);
                    Vector3 b = s.toCloud.MultiplyPoint3x4(vertices[ib]);
                    Vector3 c = s.toCloud.MultiplyPoint3x4(vertices[ic]);

                    int pieces = Split(region, a, b, c);
                    for (int p = 0; p < pieces; p++)
                    {
                        Piece piece = s_Pieces[p];
                        Vector3 pa = At(a, b, c, piece.b0);
                        Vector3 pb = At(a, b, c, piece.b1);
                        Vector3 pc = At(a, b, c, piece.b2);

                        double share = Vector3.Cross(pb - pa, pc - pa).magnitude * 0.5 * density + carry;
                        int n = (int)share;
                        carry = share - n;
                        if (n <= 0) continue;

                        for (int k = 0; k < n; k++)
                        {
                            // Uniform over the piece: the fold is what stops the samples piling
                            // into its first corner.
                            float su = (float)rng.NextDouble();
                            float sv = (float)rng.NextDouble();
                            if (su + sv > 1f) { su = 1f - su; sv = 1f - sv; }

                            // Back into the source triangle's barycentrics, so every attribute
                            // below is read from the mesh's own vertices even when this piece's
                            // corners were invented by the clipper.
                            Vector2 bary = piece.b0 + su * (piece.b1 - piece.b0) + sv * (piece.b2 - piece.b0);
                            float u = bary.x, v = bary.y;

                            Vector3 position = a + u * (b - a) + v * (c - a);

                            Vector3 normal = hasNormals
                                ? normalMatrix.MultiplyVector(
                                    Barycentric(normals[ia], normals[ib], normals[ic], u, v)).normalized
                                : Vector3.Cross(b - a, c - a).normalized;

                            Color albedo = look.tint;
                            if (look.map != null && hasUv)
                            {
                                Vector2 t = Barycentric(uv[ia], uv[ib], uv[ic], u, v);
                                Color texel = look.map.GetPixelBilinear(
                                    t.x * look.scale.x + look.offset.x,
                                    t.y * look.scale.y + look.offset.y);

                                // A leaf card is a quad that is mostly nothing. Its area asks for a
                                // share of the budget and most of that share lands in the cut-out
                                // part, so the samples that fall through it are dropped instead of
                                // becoming points hanging around the branch. Only materials that
                                // actually clip are tested: plenty of opaque ones carry an alpha
                                // channel that means something else entirely.
                                if (look.clip && texel.a < look.cutoff) { clipped++; continue; }

                                // The copy holds the texture's own sRGB bytes; the project is linear.
                                albedo *= texel.linear;
                            }

                            Color lit = albedo;
                            if (lightmap != null)
                            {
                                Vector2 t = Barycentric(uv2[ia], uv2[ib], uv2[ic], u, v);
                                Color irradiance = lightmap.Sample(t);
                                lit = albedo * irradiance;
                            }
                            else if (settings.lighting)
                            {
                                unlit++;
                            }

                            points.Add(new Point
                            {
                                position = position,
                                normal = normal,
                                // Written back out as sRGB bytes, which is where PlyImporter expects
                                // to find colour and the right place to spend the eight bits.
                                color = ToColor32(lit * settings.exposure),
                            });
                        }
                    }
                }
            }
        }

        static Vector3 Barycentric(Vector3 a, Vector3 b, Vector3 c, float u, float v) =>
            a + u * (b - a) + v * (c - a);

        static Vector2 Barycentric(Vector2 a, Vector2 b, Vector2 c, float u, float v) =>
            a + u * (b - a) + v * (c - a);

        static Color32 ToColor32(Color linear)
        {
            Color srgb = linear.gamma;
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(srgb.r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(srgb.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(srgb.b * 255f), 0, 255),
                255);
        }

        // ------------------------------------------------------------------------------------
        // Cropping to a box
        //
        // The reason this clips triangles rather than testing whether each one is inside: the
        // landscape's triangles are enormous. Cube.007 covers 13,942 m^2 in a handful of them,
        // so "is this triangle in the box" has only two wrong answers -- drop the ground
        // entirely, or keep all 14,000 m^2 of it and spend the whole budget outside the box.
        // Clipping gives the exact area inside, which is what the density has to be computed
        // against, and a cut edge instead of a ragged one.
        //
        // Everything is done in the box's own space, scaled so that inside means no coordinate
        // is outside +/-0.5. That makes the six planes trivial and lets the box be rotated.
        // ------------------------------------------------------------------------------------

        /// <summary>One piece of a clipped triangle, as barycentrics of the triangle it came from.</summary>
        struct Piece
        {
            public Vector2 b0, b1, b2;
        }

        /// <summary>The crop, already folded into the cloud's own space. Inactive means no crop.</summary>
        readonly struct Region
        {
            public readonly bool active;
            public readonly Matrix4x4 cloudToUnit;

            public Region(Matrix4x4 cloudToUnit)
            {
                active = true;
                this.cloudToUnit = cloudToUnit;
            }
        }

        sealed class Crop
        {
            public string name;
            public Matrix4x4 worldToUnit;
            public Vector3 worldSize;
        }

        static readonly Piece[] s_Pieces = new Piece[16];
        static readonly Vector3[] s_UnitA = new Vector3[16];
        static readonly Vector2[] s_BaryA = new Vector2[16];
        static readonly Vector3[] s_UnitB = new Vector3[16];
        static readonly Vector2[] s_BaryB = new Vector2[16];

        static readonly Piece k_WholeTriangle = new()
        {
            b0 = new Vector2(0f, 0f), b1 = new Vector2(1f, 0f), b2 = new Vector2(0f, 1f),
        };

        /// <summary>
        /// Splits a triangle into the pieces of it that are inside the crop, as barycentrics of
        /// the original. Returns 0 when none of it is, and 1 whole piece when there is no crop
        /// or the triangle is entirely inside -- which is the common case and costs two
        /// comparisons.
        /// </summary>
        static int Split(Region region, Vector3 a, Vector3 b, Vector3 c)
        {
            if (!region.active)
            {
                s_Pieces[0] = k_WholeTriangle;
                return 1;
            }

            Vector3 ua = region.cloudToUnit.MultiplyPoint3x4(a);
            Vector3 ub = region.cloudToUnit.MultiplyPoint3x4(b);
            Vector3 uc = region.cloudToUnit.MultiplyPoint3x4(c);

            Vector3 lo = Vector3.Min(ua, Vector3.Min(ub, uc));
            Vector3 hi = Vector3.Max(ua, Vector3.Max(ub, uc));

            if (hi.x < -0.5f || lo.x > 0.5f ||
                hi.y < -0.5f || lo.y > 0.5f ||
                hi.z < -0.5f || lo.z > 0.5f) return 0;

            if (lo.x >= -0.5f && hi.x <= 0.5f &&
                lo.y >= -0.5f && hi.y <= 0.5f &&
                lo.z >= -0.5f && hi.z <= 0.5f)
            {
                s_Pieces[0] = k_WholeTriangle;
                return 1;
            }

            s_UnitA[0] = ua; s_UnitA[1] = ub; s_UnitA[2] = uc;
            s_BaryA[0] = k_WholeTriangle.b0;
            s_BaryA[1] = k_WholeTriangle.b1;
            s_BaryA[2] = k_WholeTriangle.b2;

            int count = 3;
            for (int axis = 0; axis < 3 && count > 0; axis++)
            {
                count = ClipPlane(axis, 1f, count);
                if (count > 0) count = ClipPlane(axis, -1f, count);
            }
            if (count < 3) return 0;

            int pieces = 0;
            for (int k = 1; k + 1 < count; k++)
                s_Pieces[pieces++] = new Piece { b0 = s_BaryA[0], b1 = s_BaryA[k], b2 = s_BaryA[k + 1] };
            return pieces;
        }

        /// <summary>
        /// One Sutherland-Hodgman pass, keeping the half-space where sign * p[axis] &lt;= 0.5.
        /// The barycentrics ride along and are interpolated at each crossing, which is what lets
        /// the caller read colour, normal and both UV sets at a vertex that did not exist in the
        /// mesh.
        /// </summary>
        static int ClipPlane(int axis, float sign, int count)
        {
            int kept = 0;

            for (int i = 0; i < count; i++)
            {
                int j = i + 1 == count ? 0 : i + 1;
                Vector3 p = s_UnitA[i], q = s_UnitA[j];

                float dp = 0.5f - sign * p[axis];
                float dq = 0.5f - sign * q[axis];
                bool insideP = dp >= 0f, insideQ = dq >= 0f;

                if (insideP && kept < s_UnitB.Length)
                {
                    s_UnitB[kept] = p;
                    s_BaryB[kept] = s_BaryA[i];
                    kept++;
                }

                if (insideP != insideQ && kept < s_UnitB.Length)
                {
                    float t = dp / (dp - dq);
                    s_UnitB[kept] = Vector3.LerpUnclamped(p, q, t);
                    s_BaryB[kept] = Vector2.LerpUnclamped(s_BaryA[i], s_BaryA[j], t);
                    kept++;
                }
            }

            for (int i = 0; i < kept; i++)
            {
                s_UnitA[i] = s_UnitB[i];
                s_BaryA[i] = s_BaryB[i];
            }
            return kept;
        }

        /// <summary>A barycentric corner back in the cloud's space.</summary>
        static Vector3 At(Vector3 a, Vector3 b, Vector3 c, Vector2 bary) =>
            a + bary.x * (b - a) + bary.y * (c - a);

        /// <summary>
        /// The crop volume an object stands for. A BoxCollider if it has one, because that is
        /// someone saying explicitly where the box is; otherwise its mesh's bounds, which is what
        /// makes dropping in a Cube and scaling it work. The object's rotation is kept, so the
        /// box does not have to be axis-aligned.
        /// </summary>
        static Crop CropFrom(Transform t)
        {
            Vector3 center, size;

            if (t.TryGetComponent(out BoxCollider box))
            {
                center = box.center;
                size = box.size;
            }
            else if (t.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                center = filter.sharedMesh.bounds.center;
                size = filter.sharedMesh.bounds.size;
            }
            else
            {
                center = Vector3.zero;
                size = Vector3.one;
            }

            size = new Vector3(
                Mathf.Max(1e-5f, Mathf.Abs(size.x)),
                Mathf.Max(1e-5f, Mathf.Abs(size.y)),
                Mathf.Max(1e-5f, Mathf.Abs(size.z)));

            Matrix4x4 localToUnit =
                Matrix4x4.Scale(new Vector3(1f / size.x, 1f / size.y, 1f / size.z)) *
                Matrix4x4.Translate(-center);

            Vector3 scale = t.lossyScale;
            return new Crop
            {
                name = t.name,
                worldToUnit = localToUnit * t.worldToLocalMatrix,
                worldSize = new Vector3(size.x * scale.x, size.y * scale.y, size.z * scale.z),
            };
        }

        /// <summary>
        /// Whether a renderer is switched on, as far as the object being baked is concerned.
        ///
        /// Deliberately not activeInHierarchy. The first thing anyone does once they have a
        /// cloud to look at is switch the original geometry off -- and with activeInHierarchy
        /// that makes the next bake read an empty scene and report that none of the meshes are
        /// readable, which sounds like a broken model importer and is not. What matters is
        /// whether the renderer is on *inside* the thing being baked, so <paramref name="root"/>
        /// and everything above it is ignored. A prop switched off within the room is still a
        /// prop someone chose to hide, and is still left out.
        /// </summary>
        static bool ActiveWithin(Transform root, Renderer renderer)
        {
            if (!renderer.enabled) return false;
            for (Transform t = renderer.transform; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        /// <summary>
        /// A rough mean colour for one mesh, from a handful of samples.
        ///
        /// Exists so the per-mesh report can say what a mesh looks like, not just how big it is.
        /// A name like "Cube.005" and an area of 700 m^2 does not tell anyone whether that is a
        /// wall worth keeping or the sky; "colour 110,170,230" does, and it is the difference
        /// between naming the thing to exclude and guessing at it.
        /// </summary>
        static Color32 ProbeColour(Surface s, Region region)
        {
            Mesh mesh = s.mesh;
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            bool hasUv = uv.Length == vertices.Length;

            double r = 0, g = 0, b = 0;
            int taken = 0;
            const int k_Wanted = 64;

            for (int sub = 0; sub < mesh.subMeshCount && taken < k_Wanted; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                Material material = MaterialFor(s, sub);
                if (!Samples(material)) continue;

                int[] indices = mesh.GetTriangles(sub);
                Look look = LookOf(material);

                // Strided through the mesh rather than taken from the front, so a model whose
                // first triangles are all one trim piece does not report that piece's colour.
                int triangles = indices.Length / 3;
                if (triangles == 0) continue;
                int stride = Mathf.Max(1, triangles / k_Wanted);

                for (int t = 0; t < triangles && taken < k_Wanted; t += stride)
                {
                    int i = t * 3;
                    int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];

                    Vector3 a = s.toCloud.MultiplyPoint3x4(vertices[ia]);
                    Vector3 bb = s.toCloud.MultiplyPoint3x4(vertices[ib]);
                    Vector3 c = s.toCloud.MultiplyPoint3x4(vertices[ic]);
                    if (Split(region, a, bb, c) == 0) continue;

                    Color albedo = look.tint;
                    if (look.map != null && hasUv)
                    {
                        // The centroid, which for a backdrop is as good as anywhere.
                        Vector2 t0 = (uv[ia] + uv[ib] + uv[ic]) / 3f;
                        Color texel = look.map.GetPixelBilinear(
                            t0.x * look.scale.x + look.offset.x,
                            t0.y * look.scale.y + look.offset.y);
                        if (look.clip && texel.a < look.cutoff) continue;
                        albedo *= texel.linear;
                    }

                    Color32 srgb = ToColor32(albedo);
                    r += srgb.r; g += srgb.g; b += srgb.b;
                    taken++;
                }
            }

            if (taken == 0) return new Color32(0, 0, 0, 255);
            return new Color32((byte)(r / taken), (byte)(g / taken), (byte)(b / taken), 255);
        }

        static Material MaterialFor(Surface s, int sub) =>
            sub < s.materials.Length ? s.materials[sub] : null;

        /// <summary>
        /// Whether a material is a surface a point can sit on.
        ///
        /// Transparent ones are not, here. The fog is a ray-marched box, the water is a plane
        /// standing in for a body of water, and the glass is there to be seen through -- none of
        /// them is somewhere a scanner would have returned a point. Left in, the fog box alone
        /// took a fifth of this room's budget and laid it across the floor as a sheet of points
        /// hanging in mid-air. The render queue is asked rather than the shader's name, so
        /// anything authored transparent later is covered without a list to keep up to date.
        ///
        /// A submesh with no material is not drawn by Unity either, so it is not sampled.
        /// </summary>
        static bool Samples(Material m) => m != null && m.renderQueue < 3000;

        static bool DrawsAPointCloud(Renderer r)
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                if (k_CloudShaders.Contains(m.shader.name)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------
        // Reading what the renderers are wearing
        //
        // Both albedo maps and lightmaps are sampled through a readable copy taken with a blit.
        // Ticking Read/Write on the real textures would be a project-wide change -- more memory
        // at runtime on every one of them -- to serve a tool that runs occasionally, and the
        // lightmaps are compressed HDR that cannot be read directly at all.
        // ------------------------------------------------------------------------------------

        sealed class Look
        {
            public Color tint = Color.white;
            public Texture2D map;
            public Vector2 scale = Vector2.one;
            public Vector2 offset = Vector2.zero;
            public bool clip;
            public float cutoff = 0.5f;
        }

        /// <summary>RGBM's shared multiplier, the same 5.0 the lightmap decode in URP uses.</summary>
        const float k_RgbmRange = 5f;

        sealed class Lightmap
        {
            public Texture2D color;
            public Vector4 scaleOffset;
            public bool rgbm;

            public Color Sample(Vector2 uv2)
            {
                Color c = color.GetPixelBilinear(
                    uv2.x * scaleOffset.x + scaleOffset.z,
                    uv2.y * scaleOffset.y + scaleOffset.w);

                // RGBM keeps eight-bit colour and a shared multiplier in alpha. Sampling the
                // rgb alone is not a dim version of the right answer, it is the wrong answer:
                // this room came out around a sixth as bright as its own albedo, which read as
                // a correctly-baked cloud of a very dark room.
                if (!rgbm) return c;
                float m = c.a * k_RgbmRange;
                return new Color(c.r * m, c.g * m, c.b * m, 1f);
            }
        }

        static readonly Dictionary<Material, Look> s_Looks = new();
        static readonly Dictionary<int, Lightmap> s_Lightmaps = new();
        static readonly List<Texture2D> s_Copies = new();

        static Look LookOf(Material material)
        {
            if (material == null) return new Look { tint = new Color(0.5f, 0.5f, 0.5f) };
            if (s_Looks.TryGetValue(material, out Look cached)) return cached;

            var look = new Look();
            foreach (string name in k_ColorProperties)
            {
                if (!material.HasProperty(name)) continue;
                look.tint = material.GetColor(name);
                break;
            }

            foreach (string name in k_MapProperties)
            {
                if (!material.HasProperty(name)) continue;
                if (material.GetTexture(name) is not Texture source) continue;
                look.map = ReadableCopy(source, k_AlbedoSize, false);
                look.scale = material.GetTextureScale(name);
                look.offset = material.GetTextureOffset(name);
                break;
            }

            // Asked of the keyword and the queue rather than of one shader's _AlphaClip, so a
            // cut-out authored in URP Lit, in a Simple Lit, or in something hand-written all
            // answer the same way.
            look.clip = material.IsKeywordEnabled("_ALPHATEST_ON") ||
                        (material.renderQueue >= 2450 && material.renderQueue < 2500);
            if (look.clip && material.HasProperty("_Cutoff"))
                look.cutoff = Mathf.Clamp01(material.GetFloat("_Cutoff"));

            // Alpha is not a thing a point has. Keeping it would only darken the colour when the
            // tint is a faded one.
            look.tint.a = 1f;

            s_Looks[material] = look;
            return look;
        }

        static Lightmap LightmapOf(Renderer renderer)
        {
            int index = renderer.lightmapIndex;
            LightmapData[] maps = LightmapSettings.lightmaps;
            if (index < 0 || index >= maps.Length) return null;

            if (s_Lightmaps.TryGetValue(index, out Lightmap cached))
            {
                if (cached == null) return null;
                return new Lightmap
                {
                    color = cached.color, rgbm = cached.rgbm,
                    scaleOffset = renderer.lightmapScaleOffset
                };
            }

            Texture source = maps[index].lightmapColor;
            Texture2D copy = source != null ? ReadableCopy(source, k_LightmapSize, true) : null;
            if (copy == null)
            {
                s_Lightmaps[index] = null;
                return null;
            }

            // Which encoding these lightmaps are in, asked of the texture rather than assumed.
            // Unity re-encodes baked lightmaps per build target -- High Quality gives a true HDR
            // format, Normal Quality gives RGBM -- so the .exr on disk says nothing about what is
            // in memory, and switching the active build target silently changes the answer.
            string format = source.graphicsFormat.ToString();
            bool hdr = format.Contains("SFloat") || format.Contains("UFloat");
            Debug.Log($"[Cloud]   lightmap {index} is {format}, read as {(hdr ? "HDR" : "RGBM")}.");

            var lightmap = new Lightmap { color = copy, rgbm = !hdr };
            s_Lightmaps[index] = lightmap;

            return new Lightmap
            {
                color = copy, rgbm = lightmap.rgbm,
                scaleOffset = renderer.lightmapScaleOffset
            };
        }

        /// <summary>
        /// A sampleable copy of a texture the project never marked readable.
        ///
        /// <paramref name="hdr"/> picks the precision: a lightmap holds irradiance well above 1
        /// and copying it through an 8-bit target would clip every lit surface to white, which on
        /// a baked room is most of them.
        /// </summary>
        static Texture2D ReadableCopy(Texture source, int maxSize, bool hdr)
        {
            int width = Mathf.Max(1, Mathf.Min(source.width, maxSize));
            int height = Mathf.Max(1, Mathf.Min(source.height, maxSize));

            RenderTexture rt = RenderTexture.GetTemporary(
                width, height, 0,
                hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32,
                hdr ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);

            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                var copy = new Texture2D(width, height,
                    hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr)
                {
                    name = source.name + " (readable)",
                    wrapMode = source.wrapMode,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                copy.Apply(false, false);
                s_Copies.Add(copy);
                return copy;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Cloud] could not read {source.name}: {e.Message}. " +
                                 "Those points fall back to the material's flat colour.");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static void ReleaseTextures()
        {
            foreach (Texture2D copy in s_Copies)
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            s_Copies.Clear();
            s_Looks.Clear();
            s_Lightmaps.Clear();
        }

        // ------------------------------------------------------------------------------------
        // Writing
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Binary little-endian .ply, with normals.
        ///
        /// Normals are written even though they cost 12 bytes a point, because they are the one
        /// thing this tool can give that a scan cannot: they come from the modelled surface, so a
        /// wall is flat and an edge is sharp. <see cref="PlyImporter"/>'s Use File Normals reads
        /// them; its Estimate Normals refits them from the neighbourhood instead, which rounds
        /// every corner in the room off by the fitting radius.
        ///
        /// The header is written LF-only. Nothing downstream here minds CRLF, but half the point
        /// cloud tools in the world do.
        /// </summary>
        static void WritePly(string path, List<Point> points, string sourceName, float spacing)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);

            var header = new StringBuilder();
            header.Append("ply\n");
            header.Append("format binary_little_endian 1.0\n");
            header.Append($"comment baked from {sourceName} by BakeScenePointCloud, " +
                          $"{spacing * 100f:0.0} cm spacing\n");
            header.Append($"element vertex {points.Count}\n");
            header.Append("property float x\nproperty float y\nproperty float z\n");
            header.Append("property float nx\nproperty float ny\nproperty float nz\n");
            header.Append("property uchar red\nproperty uchar green\nproperty uchar blue\n");
            header.Append("end_header\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);

            const int k_Stride = 3 * 4 + 3 * 4 + 3;
            var record = new byte[k_Stride];
            foreach (Point p in points)
            {
                int o = 0;
                o = WriteFloat(record, o, p.position.x);
                o = WriteFloat(record, o, p.position.y);
                o = WriteFloat(record, o, p.position.z);
                o = WriteFloat(record, o, p.normal.x);
                o = WriteFloat(record, o, p.normal.y);
                o = WriteFloat(record, o, p.normal.z);
                record[o++] = p.color.r;
                record[o++] = p.color.g;
                record[o] = p.color.b;
                stream.Write(record, 0, k_Stride);
            }
        }

        static int WriteFloat(byte[] into, int offset, float value)
        {
            // BitConverter is little-endian on every platform this project builds for, and the
            // header says so.
            int bits = BitConverter.SingleToInt32Bits(value);
            into[offset] = (byte)bits;
            into[offset + 1] = (byte)(bits >> 8);
            into[offset + 2] = (byte)(bits >> 16);
            into[offset + 3] = (byte)(bits >> 24);
            return offset + 4;
        }

        /// <summary>
        /// Imports the written .ply with the settings a modelled cloud wants, and hands back its
        /// mesh. Quads rather than Points: at centimetres apart single pixels leave the room full
        /// of holes from any distance, and quads hold a world-space size as the player walks in.
        /// </summary>
        static Mesh ImportCloud(string plyPath, out string note)
        {
            note = string.Empty;
            AssetDatabase.ImportAsset(plyPath, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(plyPath) is not PlyImporter importer)
            {
                note = "PlyImporter did not claim the file.";
                return null;
            }

            var so = new SerializedObject(importer);
            Set(so, "m_Topology", (int)PlyImporter.Topology.Quads);
            Set(so, "m_Recenter", (int)PlyImporter.Recenter.None);
            SetBool(so, "m_UseFileNormals", true);
            SetBool(so, "m_EstimateNormals", false);
            SetBool(so, "m_ConvertColorSpace", true);
            SetBool(so, "m_FlipZ", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();

            Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(plyPath).OfType<Mesh>().FirstOrDefault();
            if (mesh == null) note = "the import produced no mesh; check the console for a [Ply] error.";
            return mesh;
        }

        static void Set(SerializedObject so, string property, int value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null) p.enumValueIndex = value;
        }

        static void SetBool(SerializedObject so, string property, bool value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null) p.boolValue = value;
        }

        /// <summary>
        /// A prefab holding the cloud, wearing its own material and already driven.
        ///
        /// The imported .ply can be dragged into a scene on its own, but what comes out is a
        /// renderer and nothing else: no <see cref="PointCloudFigure"/>, so it stands at one
        /// fixed phase forever, and the shared material, so changing the point size changes it
        /// for every other cloud too. This is the version that is ready to look at.
        /// </summary>
        static string BuildPrefab(Transform source, string cloudName, Mesh mesh, float spacing,
                                  Settings settings, out string materialNote)
        {
            string materialPath = $"{settings.outDir}/{cloudName}_Points.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (material == null)
            {
                // Lit, not the plain unlit cloud, unless the lighting was baked into the colour.
                // This scene is Shadowmask: its mixed lights deliver their direct light at
                // runtime and leave only the bounce in the lightmap, so a cloud carrying a
                // colour and no normals is a room with its lamps switched off. Lit, with the
                // normals the bake wrote, the same lights reach the cloud that reach the walls.
                string shaderName = settings.lighting
                    ? "CompositeBody/PointCloud"
                    : "CompositeBody/PointCloudLit";

                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    materialNote = $"{shaderName} was not found, so the prefab has no material.";
                    return BuildPrefabWith(source, cloudName, mesh, null, settings);
                }

                material = new Material(shader) { name = cloudName + " Points" };

                if (!settings.lighting)
                {
                    // The surface's own albedo rather than the white the figures use: a room
                    // read as uniformly white stops being that room. Set as a float and no
                    // keyword, because the shader branches on the value rather than compiling
                    // a variant -- enabling _KEEPSCANCOLOR_ON would do nothing but leave an
                    // undeclared keyword sitting in the material.
                    material.SetFloat("_KeepScanColor", 1f);
                    material.SetFloat("_ScanColorMix", 1f);
                }
                // Just under the spacing, so the surface nearly closes but still reads as
                // points. Sizing them past the spacing would hide the thing that makes this a
                // cloud, and these have to stand next to the 真人 without looking like a
                // different idea.
                float size = Mathf.Clamp(spacing * k_PointSizeFactor, 0.0002f, 0.5f);
                material.SetFloat("_PointSize", size);
                AssetDatabase.CreateAsset(material, materialPath);
                materialNote = $"{materialPath} created on {shaderName} at {size * 100f:0.0} cm points.";
            }
            else
            {
                materialNote = $"{materialPath} already existed and was left as it is.";
            }

            return BuildPrefabWith(source, cloudName, mesh, material, settings);
        }

        static string BuildPrefabWith(Transform source, string cloudName, Mesh mesh,
                                      Material material, Settings settings)
        {
            var go = new GameObject(cloudName + "_Points");
            try
            {
                // The source's own local transform, so this drops in beside the original and
                // lands on top of it rather than at the scene origin.
                go.transform.localPosition = source.localPosition;
                go.transform.localRotation = source.localRotation;
                go.transform.localScale = source.localScale;

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                var figure = go.AddComponent<PointCloudFigure>();
                var so = new SerializedObject(figure);
                SerializedProperty cloud = so.FindProperty("m_Cloud");
                if (cloud != null) cloud.objectReferenceValue = renderer;

                // A room is not a person. The idle amplitude that reads as breath on a figure
                // reads as an earthquake on a wall, and the figure's spatial scale would put a
                // whole lobe inside a chair; both are turned down and spread out here.
                SetFloat(so, "m_Turbulence", 0.004f);
                SetFloat(so, "m_TurbulenceScale", 1.5f);

                Bounds bounds = mesh.bounds;
                SetVector(so, "m_RevealFrom", new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                SetFloat(so, "m_RevealRadius", bounds.size.magnitude);
                SetVector(so, "m_DissolveFrom", bounds.center);
                SetFloat(so, "m_DissolveRadius", bounds.extents.magnitude);
                so.ApplyModifiedPropertiesWithoutUndo();

                string prefabPath = $"{settings.outDir}/{cloudName}_Points.prefab";
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                return prefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void SetFloat(SerializedObject so, string property, float value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null) p.floatValue = value;
        }

        static void SetVector(SerializedObject so, string property, Vector3 value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null) p.vector3Value = value;
        }

        // ------------------------------------------------------------------------------------
        // Odds and ends
        // ------------------------------------------------------------------------------------

        static Transform Find(Scene scene, string name)
        {
            bool byPath = name.Contains('/');
            var matches = new List<Transform>();

            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (byPath ? PathOf(t) == name : t.name == name) matches.Add(t);
            }

            if (matches.Count > 1)
            {
                Debug.LogWarning($"[Cloud] '{name}' matches {matches.Count} objects; using the first.\n" +
                                 string.Join("\n", matches.Select(m => "[Cloud]   " + PathOf(m))) +
                                 "\n[Cloud] Pass a full path like \"S1/Base_Model_V3\" to pick one.");
            }
            return matches.Count > 0 ? matches[0] : null;
        }

        static string PathOf(Transform t)
        {
            if (t == null) return "the scene root";
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// <summary>A file name the asset database will accept, from an object name that may not be one.</summary>
        static string Sanitise(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Seeds the sampling. string.GetHashCode is randomised per process on modern runtimes, so
        /// using it would scatter the points differently on every run and make two bakes of an
        /// unchanged room produce two different files.
        /// </summary>
        static int StableHash(string s)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in s) hash = hash * 31 + c;
                return hash;
            }
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static bool HasFlag(string name) => Environment.GetCommandLineArgs().Contains(name);
    }
}
