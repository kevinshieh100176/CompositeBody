using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace Unity.XR.CompositionLayers.Editor
{
    /// <summary>
    /// Custom editor for <see cref="CompositionLayersRuntimeSettings"/>.
    /// </summary>
    [CustomEditor(typeof(CompositionLayersRuntimeSettings))]
    public class CompositionLayersRuntimeSettingsEditor : UnityEditor.Editor
    {
        const string k_CompositionSplashSceneTemplate = "Packages/com.unity.xr.compositionlayers/Runtime/Scenes/CompositionSplash.unity";
        const string k_CompositionFolder = "Assets/XR/CompositionLayers/";
        const string k_CompositionSplashSceneName = "CompositionSplash.unity";
        readonly string k_CompositionSplashScenePath = Path.Combine(k_CompositionFolder, k_CompositionSplashSceneName);

        const string k_ScriptPropertyName = "m_Script";

        // Splash Settings
        const string k_EnableSplashScreen = "m_EnableSplashScreen";
        const string k_SplashImage = "m_SplashImage";
        const string k_BackgroundType = "m_BackgroundType";
        const string k_BackgroundColor = "m_BackgroundColor";
        const string k_SplashDuration = "m_SplashDuration";
        const string k_FadeInDuration = "m_FadeInDuration";
        const string k_FadeOutDuration = "m_FadeOutDuration";
        const string k_FollowSpeed = "m_FollowSpeed";
        const string k_FollowDistance = "m_FollowDistance";
        const string k_LockToHorizon = "m_LockToHorizon";
        const string k_LayerType = "m_LayerType";
        const string k_QuadLayerData = "m_QuadLayerData";
        const string k_CylinderLayerData = "m_CylinderLayerData";
        const string k_ApplyTransformScale = "m_ApplyTransformScale";

        readonly List<string> m_SplashSettings = new List<string>
        {
            k_SplashImage,
            k_BackgroundType,
            k_BackgroundColor,
            k_SplashDuration,
            k_FadeInDuration,
            k_FadeOutDuration,
            k_FollowSpeed,
            k_FollowDistance,
            k_LockToHorizon,
            k_LayerType,
            k_QuadLayerData,
            k_CylinderLayerData
        };

        readonly List<string> m_IgnoredLayerDataSettings = new List<string>
        {
            k_ApplyTransformScale
        };

        SceneAsset m_SplashScene;
        SceneAsset m_SplashSceneTemplate;

        /// <summary>
        /// Draws the custom inspector GUI for the <see cref="CompositionLayersRuntimeSettings"/>.
        /// </summary>
        public override void OnInspectorGUI()
        {
            var settings = target as CompositionLayersRuntimeSettings;
            SerializedProperty prop = serializedObject.GetIterator();

            if (prop.NextVisible(true))
            {
                do
                {
                    // Since we are rendering the properties manually, we need to disable editing of the script field
                    if (prop.name == k_ScriptPropertyName)
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.PropertyField(prop, true);
                        }
                    }
                    // Draw the Splash Screen settings if the splash screen is enabled
                    else if (prop.name == k_EnableSplashScreen)
                    {
                        EditorGUI.BeginChangeCheck();

                        EditorGUILayout.PropertyField(serializedObject.FindProperty(prop.name), true);

                        // Show a warning if the splash screen is enabled in the Player Settings
                        bool splashEnabled = serializedObject.FindProperty(prop.name).boolValue;
                        if(splashEnabled && (PlayerSettings.SplashScreen.show || PlayerSettings.virtualRealitySplashScreen != null))
                            EditorGUILayout.HelpBox("The Splash Screen should be disabled in the Player Settings to use the Composition Layers Splash Screen.", MessageType.Warning);

                        // Show an error if the splash screen is enabled but the splash scene does not exist in the Build Settings
                        if (splashEnabled && !SplashSceneExistsInBuildSettings())
                            EditorGUILayout.HelpBox("The Composition Layers Splash Scene is missing from the Build Settings.", MessageType.Error);
                        // Show a warning if the splash screen is enabled but is not located at the first index in the Build Settings
                        else if (splashEnabled && !SplashSceneAtFirstIndex())
                            EditorGUILayout.HelpBox("The Composition Layers Splash Scene should be at the first index in the Build Settings.", MessageType.Error);
                        // Show a warning if the splash screen is enabled and there is no scene assigned after the splash scene
                        else if (splashEnabled && !SceneAfterSplashInBuildSettings())
                            EditorGUILayout.HelpBox("There is no scene assigned after the splash scene in the Build Settings. The splash screen will display indefinitely.", MessageType.Warning);

                        if (EditorGUI.EndChangeCheck())
                        {
                            serializedObject.ApplyModifiedProperties();

                            if (settings.EnableSplashScreen && !SplashSceneExistsInBuildSettings())
                                AddSplashScene();
                            else if (!settings.EnableSplashScreen && SplashSceneExistsInBuildSettings())
                                RemoveSceneAtPath(k_CompositionSplashScenePath);
                        }
                    }
                    // Hide the Splash Settings if the splash screen is disabled, or if the LayerData is not relevant to the LayerType
                    else if(IsSplashSetting(prop.name) && !settings.EnableSplashScreen
                            || prop.name == k_QuadLayerData && settings.LayerType == CompositionLayersRuntimeSettings.Layer.Cylinder
                            || prop.name == k_CylinderLayerData && settings.LayerType == CompositionLayersRuntimeSettings.Layer.Quad
                            || prop.name == k_BackgroundColor && settings.BackgroundType == CompositionLayersRuntimeSettings.SplashBackgroundType.Passthrough)
                    {
                        continue;
                    }
                     // Draw the LayerData properties
                    else if(prop.name == k_QuadLayerData || prop.name == k_CylinderLayerData)
                    {
                        EditorGUI.BeginChangeCheck();

                        // Display each parameter of the LayerData
                        SerializedProperty layerData;
                        switch(settings.LayerType)
                        {
                            case CompositionLayersRuntimeSettings.Layer.Quad:
                                layerData = serializedObject.FindProperty(k_QuadLayerData);
                                break;
                            case CompositionLayersRuntimeSettings.Layer.Cylinder:
                                layerData = serializedObject.FindProperty(k_CylinderLayerData);
                                break;
                            default:
                                layerData = serializedObject.FindProperty(k_QuadLayerData);
                                break;
                        }

                        SerializedProperty layerDataIterator = layerData.Copy();
                        layerDataIterator.NextVisible(true);

                        do
                        {
                            if(IsIgnoredLayerDataSetting(layerDataIterator.name))
                                continue;

                            EditorGUILayout.PropertyField(layerDataIterator, true);
                        }
                        while(layerDataIterator.NextVisible(false));

                        if (EditorGUI.EndChangeCheck())
                            serializedObject.ApplyModifiedProperties();
                    }
                    // Draw the default inspector for all other properties
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(prop, true);
                        if (EditorGUI.EndChangeCheck())
                            serializedObject.ApplyModifiedProperties();
                    }
                }
                while (prop.NextVisible(false));
            }
        }

        void AddSplashScene()
        {
            if (SplashSceneExistsInBuildSettings())
                RemoveSceneAtPath(k_CompositionSplashScenePath);

            SceneAsset newSplashScene = GetOrCopySplashTemplateToResources();

            AddSceneAtIndex(0, newSplashScene);
        }

        bool SplashSceneExistsInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            foreach (var scene in scenes)
            {
                if (scene.path == k_CompositionSplashScenePath)
                    return true;
            }

            return false;
        }

        bool SplashSceneAtFirstIndex()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            if(scenes.Count > 0 && scenes[0].path == k_CompositionSplashScenePath)
                return true;

            return false;
        }

        bool SceneAfterSplashInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            int splashIndex = scenes.FindIndex(scene => scene.path == k_CompositionSplashScenePath);
            if(splashIndex == -1)
                return false;

            for(int i = splashIndex + 1; i < scenes.Count; i++)
                if(scenes[i].enabled)
                    return true;

            return false;
        }

        void AddSceneAtIndex(int index, SceneAsset scene)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.Insert(index, new EditorBuildSettingsScene(AssetDatabase.GetAssetPath(scene), true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        void RemoveSceneAtPath(string path)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(scene => scene.path == path);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        SceneAsset GetOrCopySplashTemplateToResources()
        {
            var template = GetSplashSceneTemplate();
            if (template == null)
                return null;

            if (m_SplashScene != null)
                return m_SplashScene;

            if (!AssetDatabase.IsValidFolder(k_CompositionFolder))
                AssetDatabase.CreateFolder("Assets/XR", "CompositionLayers");

            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(template), k_CompositionSplashScenePath);
            AssetDatabase.Refresh();
            m_SplashScene = (SceneAsset)AssetDatabase.LoadAssetAtPath(k_CompositionSplashScenePath, typeof(SceneAsset));
            return m_SplashScene;
        }

        SceneAsset GetSplashSceneTemplate()
        {
            if (m_SplashSceneTemplate == null)
                m_SplashSceneTemplate = (SceneAsset)AssetDatabase.LoadAssetAtPath(k_CompositionSplashSceneTemplate, typeof(SceneAsset));

            return m_SplashSceneTemplate;
        }

        bool IsSplashSetting(string name)
        {
            return m_SplashSettings.Contains(name);
        }

        bool IsIgnoredLayerDataSetting(string name)
        {
            return m_IgnoredLayerDataSettings.Contains(name);
        }
    }
}
