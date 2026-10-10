using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Push what has been moved in O_0.unity back into O_0.prefab.
    ///
    /// O-0 is authored by opening O_0.unity and dragging things, and every one of those drags
    /// lands as a PREFAB OVERRIDE on the instance in that scene. The prefab asset keeps the old
    /// value, MainScene keeps the old value, and the show keeps the old value -- so a figure that
    /// has visibly moved in the authoring scene has not moved in the piece. Nothing warns about
    /// this; the scene simply looks right.
    ///
    /// Applies PROPERTY overrides only. PrefabUtility.ApplyPrefabInstance would also push added
    /// GameObjects into the asset, and the authoring scene has one on purpose -- the hand-made
    /// Timeline (Preview) director -- which inside the prefab would become a second director
    /// fighting the real one in every scene the beat appears in.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.SyncO0Prefab.Run
    /// </summary>
    public static class SyncO0Prefab
    {
        const string k_Scene = "Assets/_Scenes/O_0.unity";
        const string k_Prefab = "Assets/_Scenes/O_0.prefab";

        [MenuItem("Tools/O-0/Apply Scene Edits To Prefab")]
        static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run();
        }

        public static void Run()
        {
            Debug.Log("[Sync] Starting...");

            Scene scene = EditorSceneManager.OpenScene(k_Scene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[Sync] RESULT: FAIL - could not open {k_Scene}.");
                return;
            }

            GameObject instance = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(root)) continue;
                string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (source == k_Prefab) instance = root;
            }

            if (instance == null)
            {
                Debug.LogError($"[Sync] RESULT: FAIL - no {k_Prefab} instance in {k_Scene}.");
                return;
            }

            var overrides = PrefabUtility.GetObjectOverrides(instance);
            if (overrides.Count == 0)
            {
                Debug.Log("[Sync] nothing to apply; the scene already matches the prefab." +
                          "\n[Sync] RESULT: PASS");
                return;
            }

            // Named before applying, because applying clears the list.
            foreach (var ov in overrides)
            {
                var go = ov.instanceObject as GameObject;
                var component = ov.instanceObject as Component;
                string name = go != null ? go.name
                            : component != null ? $"{component.gameObject.name}.{component.GetType().Name}"
                            : ov.instanceObject.name;
                Debug.Log($"[Sync] applying override on {name}");
            }

            int applied = 0;
            foreach (var ov in new System.Collections.Generic.List<ObjectOverride>(overrides))
            {
                ov.Apply();
                applied++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_Scene);
            AssetDatabase.SaveAssets();

            int left = PrefabUtility.GetAddedGameObjects(instance).Count;
            Debug.Log($"[Sync] {applied} override(s) applied to {k_Prefab}. " +
                      $"{left} added object(s) left in the scene on purpose." +
                      "\n[Sync] RESULT: PASS");
        }
    }
}
