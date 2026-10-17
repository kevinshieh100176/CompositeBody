using System.Text;
using UnityEditor;
using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Checks a baked river for the things that are invisible until they are not.
    ///
    /// THE SEAM CHECK IS THE POINT. BuildRiver claims chunk boundaries are watertight because
    /// every LOD level includes the chunk's exact first and last station, and because the lateral
    /// vertex count never varies. Both are easy to break and the symptom -- a hairline crack you
    /// can see the void through, only at certain distances, only when two neighbours happen to
    /// pick different levels -- is close to impossible to catch by looking. So it is asserted
    /// numerically here instead: the shared edge rows must match vertex for vertex.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.VerifyRiver.Run
    /// </summary>
    public static class VerifyRiver
    {
        const float k_Tolerance = 1e-4f;

        [MenuItem("Tools/Verify/River")]
        public static void Run()
        {
            Debug.Log("[VerifyRiver] Starting...");

            RiverSpline[] rivers = Object.FindObjectsByType<RiverSpline>(FindObjectsInactive.Include);
            if (rivers.Length == 0)
            {
                Debug.LogError("[VerifyRiver] RESULT: FAIL - no RiverSpline in the open scene.");
                return;
            }

            var report = new StringBuilder();
            int failures = 0;

            foreach (RiverSpline river in rivers)
                failures += Check(river, report);

            report.Append(failures == 0
                ? "[VerifyRiver] RESULT: PASS"
                : $"[VerifyRiver] {failures} problem(s).\n[VerifyRiver] RESULT: FAIL");

            if (failures == 0) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
        }

        static int Check(RiverSpline river, StringBuilder report)
        {
            int bad = 0;
            report.AppendLine($"[VerifyRiver] {river.name}");

            if (river.material == null)
            {
                report.AppendLine("[VerifyRiver]   FAIL no material - chunks would render magenta");
                bad++;
            }
            else if (river.material.shader == null ||
                     river.material.shader.name != "CompositeBody/River")
            {
                string shader = river.material.shader != null
                    ? river.material.shader.name : "<none>";
                report.AppendLine($"[VerifyRiver]   WARN material uses '{shader}', not " +
                                  "CompositeBody/River - the baked UV1 profile will be ignored");
            }

            Transform chunks = river.transform.Find("Chunks");
            if (chunks == null || chunks.childCount == 0)
            {
                report.AppendLine("[VerifyRiver]   FAIL not baked - run Tools > River > Build");
                return bad + 1;
            }

            int chunkCount = chunks.childCount;
            report.AppendLine($"[VerifyRiver]   {chunkCount} chunk(s)");

            // Collected so neighbouring chunks can be compared after the per-chunk checks.
            var firstRows = new Vector3[chunkCount][];
            var lastRows = new Vector3[chunkCount][];

            for (int c = 0; c < chunkCount; c++)
            {
                Transform chunk = chunks.GetChild(c);
                var group = chunk.GetComponent<LODGroup>();
                if (group == null)
                {
                    report.AppendLine($"[VerifyRiver]   FAIL {chunk.name} has no LODGroup");
                    bad++;
                    continue;
                }

                LOD[] lods = group.GetLODs();
                if (lods.Length == 0)
                {
                    report.AppendLine($"[VerifyRiver]   FAIL {chunk.name} has no LOD levels");
                    bad++;
                    continue;
                }

                for (int l = 1; l < lods.Length; l++)
                {
                    if (lods[l].screenRelativeTransitionHeight <
                        lods[l - 1].screenRelativeTransitionHeight) continue;

                    report.AppendLine($"[VerifyRiver]   FAIL {chunk.name} LOD{l} transition " +
                                      "height is not below the level before it");
                    bad++;
                }

                Vector3[] levelFirst = null;
                Vector3[] levelLast = null;

                for (int l = 0; l < lods.Length; l++)
                {
                    Renderer[] renderers = lods[l].renderers;
                    Mesh mesh = renderers.Length > 0 && renderers[0] != null
                        ? renderers[0].GetComponent<MeshFilter>()?.sharedMesh
                        : null;

                    if (mesh == null)
                    {
                        report.AppendLine($"[VerifyRiver]   FAIL {chunk.name} LOD{l} has no mesh");
                        bad++;
                        continue;
                    }

                    bad += CheckMesh(mesh, $"{chunk.name} LOD{l}", river, report,
                                     out Vector3[] first, out Vector3[] last);

                    // Every level of one chunk must share its end rows, or two chunks meeting at
                    // different levels leave T-junctions.
                    if (levelFirst == null)
                    {
                        levelFirst = first;
                        levelLast = last;
                    }
                    else
                    {
                        bad += CompareRows(levelFirst, first,
                                           $"{chunk.name} LOD{l} start row differs from LOD0",
                                           report);
                        bad += CompareRows(levelLast, last,
                                           $"{chunk.name} LOD{l} end row differs from LOD0",
                                           report);
                    }
                }

                firstRows[c] = levelFirst;
                lastRows[c] = levelLast;
            }

            for (int c = 0; c + 1 < chunkCount; c++)
            {
                if (lastRows[c] == null || firstRows[c + 1] == null) continue;
                bad += CompareRows(lastRows[c], firstRows[c + 1],
                                   $"seam between Chunk_{c:00} and Chunk_{c + 1:00} does not meet",
                                   report);
            }

            return bad;
        }

        static int CheckMesh(Mesh mesh, string label, RiverSpline river, StringBuilder report,
                             out Vector3[] firstRow, out Vector3[] lastRow)
        {
            int bad = 0;
            int across = river.lateralSegments + 1;

            firstRow = null;
            lastRow = null;

            Vector3[] vertices = mesh.vertices;
            if (vertices.Length < across * 2 || vertices.Length % across != 0)
            {
                report.AppendLine($"[VerifyRiver]   FAIL {label} has {vertices.Length} vertices, " +
                                  $"not a whole number of rows of {across}");
                return bad + 1;
            }

            if (vertices.Length > 65535 && mesh.indexFormat != UnityEngine.Rendering.IndexFormat.UInt32)
            {
                report.AppendLine($"[VerifyRiver]   FAIL {label} has {vertices.Length} vertices " +
                                  "but 16-bit indices");
                bad++;
            }

            // UV.y is distance in metres and must only ever increase downstream. If it does not,
            // every scrolling layer in the shader reverses direction mid-river.
            var uv = new System.Collections.Generic.List<Vector2>();
            mesh.GetUVs(0, uv);
            if (uv.Count != vertices.Length)
            {
                report.AppendLine($"[VerifyRiver]   FAIL {label} has no UV0");
                bad++;
            }
            else
            {
                int rows = vertices.Length / across;
                for (int i = 1; i < rows; i++)
                {
                    if (uv[i * across].y > uv[(i - 1) * across].y) continue;
                    report.AppendLine($"[VerifyRiver]   FAIL {label} UV.y is not increasing at " +
                                      $"row {i} - flow would reverse here");
                    bad++;
                    break;
                }
            }

            var profile = new System.Collections.Generic.List<Vector4>();
            mesh.GetUVs(1, profile);
            if (profile.Count != vertices.Length)
            {
                report.AppendLine($"[VerifyRiver]   FAIL {label} has no UV1 profile channel");
                bad++;
            }

            firstRow = new Vector3[across];
            lastRow = new Vector3[across];
            System.Array.Copy(vertices, 0, firstRow, 0, across);
            System.Array.Copy(vertices, vertices.Length - across, lastRow, 0, across);

            return bad;
        }

        static int CompareRows(Vector3[] a, Vector3[] b, string message, StringBuilder report)
        {
            if (a == null || b == null) return 0;

            if (a.Length != b.Length)
            {
                report.AppendLine($"[VerifyRiver]   FAIL {message} (row widths " +
                                  $"{a.Length} vs {b.Length})");
                return 1;
            }

            for (int i = 0; i < a.Length; i++)
            {
                float distance = Vector3.Distance(a[i], b[i]);
                if (distance <= k_Tolerance) continue;

                report.AppendLine($"[VerifyRiver]   FAIL {message} - vertex {i} is " +
                                  $"{distance * 1000f:0.00} mm apart");
                return 1;
            }

            return 0;
        }
    }
}
