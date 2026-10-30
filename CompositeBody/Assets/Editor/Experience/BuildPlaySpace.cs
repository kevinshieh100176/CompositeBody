using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// The 4 x 5 m room outline, as a prefab, dropped into MainScene and into O-0's authoring
    /// scene. Debug only.
    ///
    /// A prefab rather than an object per scene, because it will be wrong at least once: the
    /// venue's real floor is never quite the drawing, and when it turns out to be 4.2 x 2.8 that
    /// should be one edit rather than one per scene it was pasted into.
    ///
    /// It lives in MainScene rather than in O_0.prefab. The room is the room for the whole
    /// 38 minutes -- it is not O-0's art, and a copy inside every beat prefab would be ten
    /// chances for them to disagree about how big the floor is.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildPlaySpace.Run
    /// </summary>
    public static class BuildPlaySpace
    {
        const string k_Prefab = "Assets/_Scenes/PlaySpace_Debug.prefab";
        const string k_Material = "Assets/Materials/PlaySpaceDebug.mat";
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_BeatScene = "Assets/_Scenes/O_0.unity";
        const string k_ObjectName = "PlaySpace (Debug)";

        const float k_Width = 4f;
        const float k_Depth = 5f;

        /// <summary>Menu entry too: the batch one cannot run while the editor has the project open.</summary>
        [MenuItem("Tools/O-0/Build Play Space")]
        static void RunFromMenu()
        {
            // This opens and saves two scenes, so anything unsaved would go with it.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run();
        }

        public static void Run()
        {
            Debug.Log("[PlaySpace] Starting...");
            if (!BuildPrefab()) return;

            int placed = 0;
            if (PlaceIn(k_MainScene)) placed++;
            if (PlaceIn(k_BeatScene)) placed++;

            if (placed == 2)
                Debug.Log($"[PlaySpace] {k_Width} x {k_Depth} m in {placed} scene(s)." +
                          "\n[PlaySpace] RESULT: PASS");
            else
                Debug.LogError($"[PlaySpace] only {placed} of 2 scenes." +
                               "\n[PlaySpace] RESULT: FAIL");
        }

        static bool BuildPrefab()
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
            {
                Debug.LogError("[PlaySpace] RESULT: FAIL - URP/Unlit not found.");
                return false;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_Material);
            if (mat == null)
            {
                mat = new Material(unlit) { name = "PlaySpaceDebug" };
                AssetDatabase.CreateAsset(mat, k_Material);
            }
            // Unlit on purpose. A debug marker that responds to the beat's lighting would go dark
            // exactly when the beat goes dark, which is when you most want to know where the
            // walls are. Cyan because nothing in the piece is cyan -- it cannot be mistaken for
            // content.
            mat.SetColor("_BaseColor", new Color(0.15f, 0.85f, 1f, 1f));
            EditorUtility.SetDirty(mat);

            var go = new GameObject(k_ObjectName, typeof(MeshFilter), typeof(MeshRenderer));
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var bounds = go.AddComponent<PlaySpaceBounds>();
            var so = new SerializedObject(bounds);
            so.FindProperty("m_Width").floatValue = k_Width;
            so.FindProperty("m_Depth").floatValue = k_Depth;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(go, k_Prefab, out bool ok);
            Object.DestroyImmediate(go);

            if (!ok)
            {
                Debug.LogError($"[PlaySpace] RESULT: FAIL - could not save {k_Prefab}.");
                return false;
            }
            Debug.Log($"[PlaySpace] {k_Prefab} saved.");
            return true;
        }

        static bool PlaceIn(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[PlaySpace] could not open {scenePath}.");
                return false;
            }

            // Idempotent: running this twice should not leave two floors.
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != k_ObjectName) continue;
                Object.DestroyImmediate(root);
                break;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(k_Prefab);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[PlaySpace] placed in {scenePath}.");
            return true;
        }
    }
}
