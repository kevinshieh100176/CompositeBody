using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompositeBody.Diagnostics;
using CompositeBody.PointClouds;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Two repairs that both came out of the same afternoon: the 真人 lost their mesh, and the
    /// debug scenery had no way to be switched off from the desk.
    /// </summary>
    public static class RepairO0
    {
        const string k_Prefab = "Assets/_Scenes/O_0.prefab";
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_Ply = "Assets/_models/Pointcloud/Figure_A.ply";

        /// <summary>
        /// Point the figures back at the point cloud.
        ///
        /// The _models/PointCloud folder was renamed to _models/Pointcloud outside Unity, so the
        /// .ply was reimported as a NEW asset with a new guid and its .meta regenerated. The
        /// prefab still referenced the old guid, which no longer exists anywhere -- so both
        /// figures had a missing mesh and rendered nothing, in a beat that is mostly black
        /// anyway, which is why it read as a lighting problem.
        ///
        /// The mesh is assigned through the AssetDatabase rather than by editing the guid in the
        /// YAML, because the mesh is a sub-asset of a ScriptedImporter and its local file id is
        /// assigned at import. Writing the guid by hand would fix half the reference.
        /// </summary>
        [MenuItem("Tools/O-0/Repair Figure Mesh")]
        public static void RepairFigureMesh()
        {
            Debug.Log("[Repair] Starting...");

            Mesh mesh = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(k_Ply))
            {
                if (o is Mesh m) { mesh = m; break; }
            }

            if (mesh == null)
            {
                Debug.LogError($"[Repair] RESULT: FAIL - no mesh in {k_Ply}. If the file is there, " +
                               "the PlyImporter may not have run; reimport it and try again.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(k_Prefab);
            if (root == null)
            {
                Debug.LogError($"[Repair] RESULT: FAIL - could not open {k_Prefab}.");
                return;
            }

            try
            {
                int fixedCount = 0;
                foreach (PointCloudFigure figure in root.GetComponentsInChildren<PointCloudFigure>(true))
                {
                    var filter = figure.GetComponent<MeshFilter>();
                    if (filter == null) filter = figure.gameObject.AddComponent<MeshFilter>();

                    bool wasMissing = filter.sharedMesh == null;
                    filter.sharedMesh = mesh;

                    // Re-authored with the mesh: a point cloud drifts and dissolves outside its
                    // own bounds, so the renderer is given 2.5 m of slack to stop it culling
                    // while it is still on screen.
                    var renderer = figure.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        Bounds b = mesh.bounds;
                        b.Expand(2.5f);
                        renderer.localBounds = b;
                    }

                    fixedCount++;
                    Debug.Log($"[Repair] {figure.transform.parent?.name}/{figure.name}: " +
                              $"{(wasMissing ? "mesh was MISSING, now" : "re-pointed to")} " +
                              $"{mesh.name} ({mesh.vertexCount:N0} points)");
                }

                if (fixedCount == 0)
                {
                    Debug.LogError("[Repair] RESULT: FAIL - no PointCloudFigure in the prefab.");
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, k_Prefab);
                Debug.Log($"[Repair] {fixedCount} figure(s) repaired.\n[Repair] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Put the on-screen debug switches into MainScene.
        /// </summary>
        [MenuItem("Tools/O-0/Add Debug Toggles")]
        public static void AddDebugToggles()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("[DebugToggles] Starting...");

            Scene scene = EditorSceneManager.OpenScene(k_MainScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[DebugToggles] RESULT: FAIL - could not open {k_MainScene}.");
                return;
            }

            var existing = Object.FindFirstObjectByType<DebugViewToggle>(FindObjectsInactive.Include);
            if (existing == null)
            {
                var go = new GameObject("Debug View");
                existing = go.AddComponent<DebugViewToggle>();
                Debug.Log("[DebugToggles] added 'Debug View' to the scene.");
            }
            else
            {
                Debug.Log("[DebugToggles] 'Debug View' already present.");
            }

            // The stick locomotion sits on the same object and is switched from the same panel,
            // so there is one place that answers "is anything debug still on".
            var move = existing.GetComponent<DebugStickMove>();
            if (move == null) move = existing.gameObject.AddComponent<DebugStickMove>();

            var so = new SerializedObject(existing);
            SerializedProperty behaviours = so.FindProperty("m_Behaviours");
            behaviours.arraySize = 1;
            behaviours.GetArrayElementAtIndex(0).objectReferenceValue = move;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[DebugToggles] stick locomotion wired into the panel.");

            // Report what the names will actually find, so a typo shows up here rather than as a
            // button that does nothing.
            foreach (string name in new[] { "PlaySpace_Debug", "Debug_Enviroment" })
            {
                bool found = false;
                foreach (Transform t in Object.FindObjectsByType<Transform>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t.name == name) { found = true; break; }
                }
                Debug.Log($"[DebugToggles] {(found ? "found" : "NOT FOUND")}: {name}");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_MainScene);
            Debug.Log("[DebugToggles] RESULT: PASS");
        }
    }
}
