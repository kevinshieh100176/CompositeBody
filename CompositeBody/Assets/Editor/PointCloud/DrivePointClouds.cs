using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CompositeBody.PointClouds.EditorTools
{
    /// <summary>
    /// Give a point cloud dropped into a scene the component that makes it move.
    ///
    /// A .ply comes in through <see cref="PlyImporter"/>, so dragging one out of the project
    /// window instantiates the imported asset: a GameObject with a MeshFilter and a
    /// MeshRenderer and nothing else. The idle motion is not in the mesh and not in the
    /// material -- the material's _Phase is a number sitting in an asset, and the shader reads
    /// it exactly as written. <see cref="PointCloudFigure"/> is what advances it, every frame,
    /// through a property block. Without the component the figure renders at one fixed phase
    /// forever, which looks like the amplitude being too low and is not.
    ///
    /// Nothing warns about this. The figure appears, it is lit, it is the right size, and it
    /// stands perfectly still -- so this exists to find the ones that were missed rather than
    /// to rely on remembering.
    ///
    /// Only objects already drawing with one of the point cloud shaders are touched, and only
    /// those without a figure component, so running it twice is a no-op and running it over a
    /// prefab full of ordinary meshes does nothing at all.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.PointClouds.EditorTools.DrivePointClouds.Run
    ///   [-prefab "Assets/_Scenes/IX_Scene/S1.prefab"] [-dryRun]
    /// </summary>
    public static class DrivePointClouds
    {
        const string k_DefaultPrefab = "Assets/_Scenes/IX_Scene/S1.prefab";

        static readonly string[] k_CloudShaders =
        {
            "CompositeBody/PointCloud",
            "CompositeBody/PointCloudLit",
            "CompositeBody/PointCloudFro",
        };

        public static void Run()
        {
            string prefabPath = (GetArg("-prefab") ?? k_DefaultPrefab).Replace('\\', '/');
            bool dryRun = HasFlag("-dryRun");

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogError($"[Drive] RESULT: FAIL - no prefab at {prefabPath}.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            int added = 0, already = 0;
            var names = new List<string>();

            try
            {
                // Which renderers are spoken for, asked of the components rather than of the
                // hierarchy: a figure component anywhere in the prefab may be driving a
                // renderer some distance from it, and one sitting on an ancestor for unrelated
                // reasons is not driving this one.
                var driven = new HashSet<Renderer>();
                foreach (PointCloudFigure f in root.GetComponentsInChildren<PointCloudFigure>(true))
                {
                    var so = new SerializedObject(f);
                    SerializedProperty cloud = so.FindProperty("m_Cloud");
                    var target = cloud?.objectReferenceValue as Renderer;
                    driven.Add(target != null ? target : f.GetComponentInChildren<Renderer>(true));
                }

                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!DrawsAPointCloud(r)) continue;

                    if (driven.Contains(r) || r.GetComponent<PointCloudFigure>() != null)
                    {
                        already++;
                        continue;
                    }

                    names.Add(Path(r.transform, root.transform));
                    if (dryRun)
                    {
                        added++;
                        continue;
                    }

                    var figure = r.gameObject.AddComponent<PointCloudFigure>();

                    // Set the renderer explicitly rather than leaving the component to find it.
                    // It would find this one anyway, but a reference written into the prefab is
                    // a reference someone can see in the inspector and follow, and it survives
                    // the renderer later moving to a child.
                    var so = new SerializedObject(figure);
                    SerializedProperty cloud = so.FindProperty("m_Cloud");
                    if (cloud != null)
                    {
                        cloud.objectReferenceValue = r;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    else
                    {
                        Debug.LogWarning("[Drive] PointCloudFigure has no m_Cloud field any more; " +
                                         "left it to resolve itself at runtime.");
                    }
                    added++;
                }

                if (added > 0 && !dryRun)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            foreach (string n in names)
                Debug.Log($"[Drive] {(dryRun ? "would drive" : "now driven")}: {n}");

            if (added == 0 && already == 0)
            {
                Debug.LogError($"[Drive] no point cloud renderers found in {prefabPath}, so " +
                               "either the path is wrong or they draw with another shader.\n" +
                               "[Drive] RESULT: FAIL");
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Drive] {prefabPath}: {added} given a PointCloudFigure, " +
                      $"{already} already had one{(dryRun ? " (dry run, nothing written)" : "")}.\n" +
                      "[Drive] RESULT: PASS");
        }

        /// <summary>
        /// Whether this renderer is drawing a cloud, asked of the shader rather than of the
        /// mesh. A scan that someone pointed at a different material is not ours to drive, and
        /// a cloud wearing one of these shaders is, whatever its mesh came from.
        /// </summary>
        static bool DrawsAPointCloud(Renderer r)
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                if (k_CloudShaders.Contains(m.shader.name)) return true;
            }
            return false;
        }

        static string Path(Transform t, Transform root)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }

        static string GetArg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static bool HasFlag(string name) =>
            System.Environment.GetCommandLineArgs().Contains(name);
    }
}
