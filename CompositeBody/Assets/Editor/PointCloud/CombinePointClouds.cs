using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompositeBody.PointClouds.EditorTools
{
    /// <summary>
    /// Merges the point clouds standing in a scene into one .ply, for TouchDesigner.
    ///
    /// Each cloud is baked in its own object's local space, which is what lets a prefab be
    /// dropped beside the thing it was made from. TD does not want that: it wants one file, in
    /// one space, that lines up with everything else arriving from this show.
    ///
    /// <b>That space is Unity world space, unflipped.</b> The temptation is to convert
    /// handedness on the way out, because Unity is left-handed and most of the point-cloud world
    /// is not. It would be wrong here. <see cref="CompositeBody.Experience.TouchDesignerSender"/>
    /// already streams the players' heads and hands to the same TD network as OSC, in Unity world
    /// space -- "the orbits circle (0,0,0) and the floor is y = 0, so TD receives coordinates
    /// already in stage metres". A cloud mirrored in Z would sit in a different space from the
    /// live poses it has to agree with, and the error would read as the players standing on the
    /// wrong side of a room rather than as a coordinate convention.
    ///
    /// The points are read back from each cloud's source .ply rather than from the imported Mesh.
    /// The mesh is the wrong thing to read twice over: PlyImporter expands Points topology to
    /// Quads, so every point is four identical vertices, and it converts the file's sRGB colour
    /// to linear on the way in -- writing those bytes back out would hand TD a cloud four times
    /// too big and a stop and a half too dark. The file still holds exactly what was baked.
    ///
    /// Because of that, the importer settings have to be the identity ones the bake writes --
    /// scale 1, no recentring, no thinning, no Z flip -- or the file and the thing in the scene
    /// are different clouds. That is checked rather than assumed.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.PointClouds.EditorTools.CombinePointClouds.Run
    ///   [-scene "Assets/_Scenes/IX_Scene/S1_v3.unity"]
    ///   [-objects "S1-1_cloudbox,Base_Model_V3_cloudbox"]
    ///   [-out "TouchDesigner/S1_Combined.ply"] [-maxPoints 0] [-recentre]
    /// </summary>
    public static class CombinePointClouds
    {
        const string k_DefaultScene = "Assets/_Scenes/IX_Scene/S1_v3.unity";
        const string k_DefaultObjects = "S1-1_cloudbox,Base_Model_V3_cloudbox";

        /// <summary>
        /// Outside Assets on purpose. A 45 MB .ply dropped into the project would be imported --
        /// expanded to quads, roughly 6.6 million vertices -- to make a Mesh nobody is going to
        /// use, on every reimport. This is an export for another application, so it leaves.
        /// </summary>
        const string k_DefaultOut = "TouchDesigner/S1_Combined.ply";

        public static void Run()
        {
            string scenePath = (GetArg("-scene") ?? k_DefaultScene).Replace('\\', '/');
            string objects = GetArg("-objects") ?? k_DefaultObjects;
            string outArg = (GetArg("-out") ?? k_DefaultOut).Replace('\\', '/');
            bool recentre = HasFlag("-recentre");
            int.TryParse(GetArg("-maxPoints"), out int maxPoints);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Debug.LogError($"[Combine] RESULT: FAIL - no scene at {scenePath}.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            bool anyNormals = true;
            int clouds = 0;

            foreach (string name in objects.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
            {
                Transform t = Find(scene, name);
                if (t == null)
                {
                    Debug.LogError($"[Combine] nothing named '{name}' in {scenePath}.");
                    continue;
                }

                if (!TryReadCloud(t, out PlyData data, out string plyPath))
                    continue;

                // Into world space. The normal needs the inverse transpose rather than the
                // matrix, so a cloud someone scaled non-uniformly in the scene does not come out
                // with its normals leaning off the surface.
                Matrix4x4 toWorld = t.localToWorldMatrix;
                Matrix4x4 normalMatrix = toWorld.inverse.transpose;

                int[] keep = Stride(data.positions.Length, maxPoints, out int kept);
                bool hasNormals = data.normals != null && data.normals.Length == data.positions.Length;
                if (!hasNormals) anyNormals = false;

                for (int i = 0; i < kept; i++)
                {
                    int index = keep == null ? i : keep[i];
                    positions.Add(toWorld.MultiplyPoint3x4(data.positions[index]));
                    if (hasNormals)
                        normals.Add(normalMatrix.MultiplyVector(data.normals[index]).normalized);
                    colors.Add(data.colors != null
                        ? data.colors[index]
                        : new Color32(200, 200, 200, 255));
                }

                Debug.Log($"[Combine] {name}: {kept:N0} points from {plyPath}" +
                          (keep != null ? $" (thinned from {data.positions.Length:N0})" : "") +
                          $", placed at {t.position.x:0.00},{t.position.y:0.00},{t.position.z:0.00}.");
                clouds++;
            }

            if (positions.Count == 0)
            {
                Debug.LogError("[Combine] no points were read.\n[Combine] RESULT: FAIL");
                return;
            }

            Vector3 min = positions[0], max = positions[0];
            foreach (Vector3 p in positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            Vector3 offset = Vector3.zero;
            if (recentre)
            {
                // Horizontal centre, floor on zero -- the same convention the show uses, so a
                // recentred export still has its floor where TD expects a floor.
                offset = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);
                for (int i = 0; i < positions.Count; i++) positions[i] -= offset;
                min -= offset;
                max -= offset;
            }

            string outPath = Path.IsPathRooted(outArg)
                ? outArg
                : Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, outArg);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            string comment = $"combined from {clouds} clouds in {Path.GetFileName(scenePath)}, " +
                             (recentre ? "recentred, " : "Unity world space, ") +
                             "left-handed, metres";
            Write(outPath, positions, anyNormals ? normals : null, colors, comment);

            long bytes = new FileInfo(outPath).Length;
            Debug.Log($"[Combine] {outPath}\n" +
                      $"[Combine]   {positions.Count:N0} points from {clouds} clouds, " +
                      $"{bytes / (1024f * 1024f):0.0} MB" +
                      (anyNormals ? ", with normals" : ", no normals") + ".\n" +
                      $"[Combine]   bounds {min.x:0.00},{min.y:0.00},{min.z:0.00} to " +
                      $"{max.x:0.00},{max.y:0.00},{max.z:0.00} " +
                      $"({max.x - min.x:0.0} x {max.y - min.y:0.0} x {max.z - min.z:0.0} m)\n" +
                      "[Combine] RESULT: PASS");
        }

        /// <summary>
        /// The .ply behind a cloud in the scene, with a check that the importer is not quietly
        /// making the mesh different from the file.
        /// </summary>
        static bool TryReadCloud(Transform t, out PlyData data, out string plyPath)
        {
            data = null;
            plyPath = null;

            var filter = t.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogError($"[Combine] {t.name} has no mesh on it.");
                return false;
            }

            plyPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
            if (string.IsNullOrEmpty(plyPath) || !plyPath.EndsWith(".ply", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"[Combine] {t.name}'s mesh does not come from a .ply " +
                               $"(it is '{plyPath}'), so there is no source cloud to read.");
                return false;
            }

            if (AssetImporter.GetAtPath(plyPath) is PlyImporter importer)
            {
                var so = new SerializedObject(importer);
                string drift = Drift(so);
                if (drift != null)
                {
                    Debug.LogError($"[Combine] {plyPath} is imported with {drift}, so the file " +
                                   "and what is in the scene are not the same cloud. Combining " +
                                   "would move it. Reset those settings or re-bake.");
                    return false;
                }
            }

            try
            {
                data = PlyReader.Read(plyPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Combine] could not read {plyPath}: {e.Message}");
                return false;
            }

            return data.positions.Length > 0;
        }

        /// <summary>Names any import setting that makes the mesh differ from the file, or null.</summary>
        static string Drift(SerializedObject so)
        {
            var reasons = new List<string>();

            SerializedProperty scale = so.FindProperty("m_Scale");
            if (scale != null && !Mathf.Approximately(scale.floatValue, 1f))
                reasons.Add($"Scale {scale.floatValue}");

            SerializedProperty recenter = so.FindProperty("m_Recenter");
            if (recenter != null && recenter.enumValueIndex != (int)PlyImporter.Recenter.None)
                reasons.Add("Recenter set");

            SerializedProperty flip = so.FindProperty("m_FlipZ");
            if (flip != null && flip.boolValue) reasons.Add("Flip Z on");

            SerializedProperty max = so.FindProperty("m_MaxPoints");
            if (max != null && max.intValue > 0) reasons.Add($"Max Points {max.intValue}");

            return reasons.Count == 0 ? null : string.Join(", ", reasons);
        }

        /// <summary>
        /// Indices spread evenly through the cloud, or null to keep everything. A stride rather
        /// than the first N, for the same reason PlyImporter uses one: points are stored in the
        /// order they were generated, so the front of the file is one corner of the room.
        /// </summary>
        static int[] Stride(int count, int maxPoints, out int kept)
        {
            if (maxPoints <= 0 || maxPoints >= count)
            {
                kept = count;
                return null;
            }

            int stride = Mathf.Max(2, Mathf.CeilToInt(count / (float)maxPoints));
            var keep = new List<int>(count / stride + 1);
            for (int i = 0; i < count; i += stride) keep.Add(i);
            kept = keep.Count;
            return keep.ToArray();
        }

        /// <summary>
        /// Binary little-endian .ply. The property layout is deliberately the same one
        /// <see cref="BakeScenePointCloud"/> writes -- x y z nx ny nz then uchar rgb -- so a
        /// combined export and a single cloud are the same kind of file to whatever reads them.
        /// Change one and change the other.
        /// </summary>
        static void Write(string path, List<Vector3> positions, List<Vector3> normals,
                          List<Color32> colors, string comment)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
                                              FileShare.None, 1 << 20);

            var header = new StringBuilder();
            header.Append("ply\n");
            header.Append("format binary_little_endian 1.0\n");
            header.Append($"comment {comment}\n");
            header.Append($"element vertex {positions.Count}\n");
            header.Append("property float x\nproperty float y\nproperty float z\n");
            if (normals != null)
                header.Append("property float nx\nproperty float ny\nproperty float nz\n");
            header.Append("property uchar red\nproperty uchar green\nproperty uchar blue\n");
            header.Append("end_header\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);

            int stride = normals != null ? 3 * 4 + 3 * 4 + 3 : 3 * 4 + 3;
            var record = new byte[stride];

            for (int i = 0; i < positions.Count; i++)
            {
                int o = 0;
                Vector3 p = positions[i];
                o = WriteFloat(record, o, p.x);
                o = WriteFloat(record, o, p.y);
                o = WriteFloat(record, o, p.z);

                if (normals != null)
                {
                    Vector3 n = normals[i];
                    o = WriteFloat(record, o, n.x);
                    o = WriteFloat(record, o, n.y);
                    o = WriteFloat(record, o, n.z);
                }

                Color32 c = colors[i];
                record[o++] = c.r;
                record[o++] = c.g;
                record[o] = c.b;
                stream.Write(record, 0, stride);
            }
        }

        static int WriteFloat(byte[] into, int offset, float value)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            into[offset] = (byte)bits;
            into[offset + 1] = (byte)(bits >> 8);
            into[offset + 2] = (byte)(bits >> 16);
            into[offset + 3] = (byte)(bits >> 24);
            return offset + 4;
        }

        static Transform Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
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
