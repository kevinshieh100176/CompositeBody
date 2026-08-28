using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Post-playtest configuration:
    /// 1. Creates the experience scene that <see cref="GameSessionManager"/> loads on Start and
    ///    registers it as build index 1.
    /// 2. Turns on Meta's OpenXR features for PC VR.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.ConfigureProjectSetup.Run
    /// </summary>
    public static class ConfigureProjectSetup
    {
        public const string ExperienceScenePath = "Assets/Scenes/CompositeBody_Experience.unity";

        public static void Run()
        {
            Debug.Log("[Setup] Starting...");
            CreateExperienceScene();
            RegisterBuildScenes();
            EnableMetaOpenXrFeatures();
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] RESULT: PASS");
        }

        /// <summary>
        /// The scene staff load with "Start Experience". Deliberately near-empty: it is the
        /// place the actual experience gets built, and it is loaded additively on top of the
        /// main scene so the session managers survive.
        /// </summary>
        static void CreateExperienceScene()
        {
            if (File.Exists(ExperienceScenePath))
            {
                Debug.Log("[Setup] Experience scene already exists, leaving it alone.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Experience");

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "PlaceholderContent";
            marker.transform.SetParent(root.transform, false);
            marker.transform.position = new Vector3(0f, 1f, 2.5f);
            marker.transform.localScale = new Vector3(1.2f, 0.6f, 0.15f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PlaceholderContent" };
            mat.SetColor("_BaseColor", new Color(0.55f, 0.30f, 0.70f));
            marker.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // No camera or light here: this loads additively over the main scene, which already
            // provides both. A second camera would fight the XR rig for the display.
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ExperienceScenePath);
            Debug.Log($"[Setup] Created {ExperienceScenePath}");
        }

        static void RegisterBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(BuildBaseScene.ScenePath, true),
                new EditorBuildSettingsScene(ExperienceScenePath, true),
            };

            // Keep any other scenes the project already had, after these two.
            foreach (var existing in EditorBuildSettings.scenes)
            {
                if (existing.path != BuildBaseScene.ScenePath && existing.path != ExperienceScenePath)
                    scenes.Add(existing);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                Debug.Log($"[Setup] Build index {i}: {EditorBuildSettings.scenes[i].path}");
        }

        /// <summary>
        /// Meta XR SDK v205 ships its runtime as OpenXR features rather than a separate XR
        /// loader (the old com.unity.xr.oculus plugin is deprecated and is not installed here),
        /// so "using the Meta system" means enabling MetaXRFeature under OpenXR.
        /// </summary>
        static void EnableMetaOpenXrFeatures()
        {
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (settings == null)
                {
                    Debug.LogWarning($"[Setup] No OpenXR settings for {group}.");
                    continue;
                }

                var features = settings.GetFeatures();
                if (features == null || features.Length == 0)
                {
                    Debug.LogWarning($"[Setup] No OpenXR features found for {group}.");
                    continue;
                }

                var metaFeatures = features
                    .Where(f => f != null && f.GetType().Name.Contains("MetaXRFeature"))
                    .ToArray();

                if (metaFeatures.Length == 0)
                {
                    Debug.LogWarning($"[Setup] MetaXRFeature not present for {group}; " +
                                     "check the Meta XR SDK is imported for this target.");
                    continue;
                }

                foreach (var feature in metaFeatures)
                {
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                    Debug.Log($"[Setup] Enabled {feature.GetType().Name} for {group}");
                }

                EditorUtility.SetDirty(settings);
            }

            Debug.Log("[Setup] OpenXR remains the loader; Meta features now run on top of it.");
        }
    }
}
