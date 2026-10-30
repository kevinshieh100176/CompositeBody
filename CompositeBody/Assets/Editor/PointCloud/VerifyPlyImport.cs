using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CompositeBody.PointClouds.EditorTools
{
    /// <summary>
    /// Imports real .ply files through <see cref="PlyImporter"/> and checks what comes out.
    ///
    /// The importer's failure modes are quiet ones: a layout it does not understand yields an
    /// empty cloud, a wrong scalar width yields coordinates off by orders of magnitude, and a
    /// 16-bit index buffer silently truncates a cloud at 65,535 points. None of those throw.
    /// So the check is against measurements -- point count, bounds, index format -- on files
    /// that actually came off the scanners, in all three of the layouts they emit.
    ///
    /// Files are copied into a temporary folder under Assets for the duration and deleted
    /// afterwards, so verifying the importer never commits a scan to the repository.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.PointClouds.EditorTools.VerifyPlyImport.Run
    ///   -ply "C:/a.ply;C:/b.ply"
    /// </summary>
    public static class VerifyPlyImport
    {
        const string k_TestDir = "Assets/_PlyImportTest";

        static int s_Checks;
        static int s_Failures;

        public static void Run()
        {
            s_Checks = 0;
            s_Failures = 0;
            Debug.Log("[Ply] Starting...");

            string arg = GetArg("-ply");
            if (string.IsNullOrEmpty(arg))
            {
                Debug.LogError("[Ply] RESULT: FAIL - pass -ply \"path1;path2\".");
                return;
            }

            string[] sources = arg.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            try
            {
                if (!AssetDatabase.IsValidFolder(k_TestDir))
                    AssetDatabase.CreateFolder("Assets", "_PlyImportTest");

                foreach (string raw in sources)
                {
                    string source = raw.Trim().Replace('\\', '/');
                    if (!File.Exists(source))
                    {
                        Fail($"{source} does not exist");
                        continue;
                    }
                    Check(source);
                }
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(k_TestDir))
                {
                    AssetDatabase.DeleteAsset(k_TestDir);
                    AssetDatabase.Refresh();
                }
            }

            if (s_Failures == 0)
                Debug.Log($"[Ply] {s_Checks} checks, 0 failed.\n[Ply] RESULT: PASS");
            else
                Debug.LogError($"[Ply] {s_Checks} checks, {s_Failures} failed.\n[Ply] RESULT: FAIL");
        }

        static void Check(string source)
        {
            string name = Path.GetFileName(source);
            Debug.Log($"[Ply] --- {name} ---");

            string assetPath = $"{k_TestDir}/{name}";
            File.Copy(source, assetPath, true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(assetPath) as PlyImporter;
            if (importer == null)
            {
                Fail($"{name} was not handled by PlyImporter (got " +
                     $"{AssetImporter.GetAtPath(assetPath)?.GetType().Name ?? "nothing"})");
                return;
            }
            Pass($"{name} is handled by PlyImporter");

            Mesh points = Reimport(assetPath, PlyImporter.Topology.Points);
            if (points == null)
            {
                Fail($"{name} produced no mesh as Points");
                return;
            }

            int count = points.vertexCount;
            Pass($"{name} Points: {count:N0} vertices, bounds " +
                 $"{points.bounds.size.x:0.00} x {points.bounds.size.y:0.00} x {points.bounds.size.z:0.00} m");

            Expect(count > 0, $"{name} has vertices");
            Expect(points.GetTopology(0) == MeshTopology.Points,
                   $"{name} submesh topology is Points (got {points.GetTopology(0)})");

            // The one that bites silently: a UInt16 buffer wraps at 65,535 and the cloud comes
            // in looking plausible but truncated.
            if (count > 65535)
            {
                Expect(points.indexFormat == IndexFormat.UInt32,
                       $"{name} uses 32-bit indices for {count:N0} points");
                Expect(points.GetIndexCount(0) == count,
                       $"{name} index count matches vertex count ({points.GetIndexCount(0):N0})");
            }

            Expect(points.colors32 != null && points.colors32.Length == count,
                   $"{name} carries one colour per point");

            Bounds b = points.bounds;
            Expect(b.size.magnitude > 0.01f && b.size.magnitude < 1000f,
                   $"{name} bounds are a plausible scale ({b.size.magnitude:0.00} m diagonal)");
            Expect(!float.IsNaN(b.center.x) && !float.IsNaN(b.size.x),
                   $"{name} bounds are finite");

            // Quads must quadruple the vertices and keep the same bounds: the corner offset
            // lives in UV, so expansion happens in the shader and must not move anything.
            Mesh quads = Reimport(assetPath, PlyImporter.Topology.Quads);
            if (quads != null)
            {
                Expect(quads.vertexCount == count * 4,
                       $"{name} Quads has 4x the vertices ({quads.vertexCount:N0})");
                Expect(quads.GetTopology(0) == MeshTopology.Triangles,
                       $"{name} Quads submesh topology is Triangles");
                Expect((quads.bounds.size - b.size).magnitude < 0.001f,
                       $"{name} Quads bounds match Points bounds");
                var uvs = quads.uv;
                Expect(uvs != null && uvs.Length == quads.vertexCount,
                       $"{name} Quads carries a corner offset per vertex");
                if (uvs != null && uvs.Length >= 4)
                {
                    bool corners = Mathf.Approximately(Mathf.Abs(uvs[0].x), 1f) &&
                                   Mathf.Approximately(Mathf.Abs(uvs[0].y), 1f);
                    Expect(corners, $"{name} Quads corner offsets are +/-1 (got {uvs[0]})");
                }
            }

            // Scale and flip have to act on the asset, not just on the inspector.
            Mesh scaled = Reimport(assetPath, PlyImporter.Topology.Points, scale: 2f);
            if (scaled != null)
            {
                Expect((scaled.bounds.size - b.size * 2f).magnitude < 0.01f,
                       $"{name} Scale 2 doubles the bounds " +
                       $"({scaled.bounds.size.y:0.00} vs {b.size.y:0.00} m)");
            }

            Mesh thinned = Reimport(assetPath, PlyImporter.Topology.Points, maxPoints: 1000);
            if (thinned != null)
            {
                Expect(thinned.vertexCount <= 1100 && thinned.vertexCount > 400,
                       $"{name} Max Points 1000 thinned to {thinned.vertexCount:N0}");
                Expect((thinned.bounds.size - b.size).magnitude < b.size.magnitude * 0.25f,
                       $"{name} thinned cloud keeps roughly the same extent, so the thinning " +
                       $"is spread rather than taken from the front");
            }
        }

        static Mesh Reimport(string assetPath, PlyImporter.Topology topology,
                             float scale = 1f, int maxPoints = 0)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as PlyImporter;
            if (importer == null) return null;

            var so = new SerializedObject(importer);
            so.FindProperty("m_Topology").enumValueIndex = (int)topology;
            so.FindProperty("m_Scale").floatValue = scale;
            so.FindProperty("m_MaxPoints").intValue = maxPoints;
            so.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();

            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (o is Mesh m) return m;
            }
            return null;
        }

        static void Expect(bool condition, string what)
        {
            s_Checks++;
            if (condition) Debug.Log($"[Ply] ok   {what}");
            else { s_Failures++; Debug.LogError($"[Ply] FAIL {what}"); }
        }

        static void Pass(string what)
        {
            s_Checks++;
            Debug.Log($"[Ply] ok   {what}");
        }

        static void Fail(string what)
        {
            s_Checks++;
            s_Failures++;
            Debug.LogError($"[Ply] FAIL {what}");
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
    }
}
