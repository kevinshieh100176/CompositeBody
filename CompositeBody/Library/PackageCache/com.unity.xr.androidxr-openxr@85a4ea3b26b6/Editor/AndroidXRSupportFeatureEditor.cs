using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Android;

namespace UnityEditor.XR.OpenXR.Features.Android
{
    [CustomEditor(typeof(AndroidXRSupportFeature))]
    internal class AndroidXRSupportFeatureEditor : Editor
    {
#if UNITY_6000_1_OR_NEWER
        private static GUIContent s_MultiviewRenderRegionsOptimizationsLabel = EditorGUIUtility.TrTextContent("Multiview Render Regions Optimizations (Vulkan)", "Activates Multiview Render Regions optimizations at application start. Requires usage of Unity 6.1 or later, Vulkan as the Graphics API, Render Mode set to Multi-view and Symmetric rendering enabled.");
#endif
        private SerializedProperty symmetricProjection;
        private SerializedProperty optimizeBufferDiscards;
        private SerializedProperty optionalFeatures;
#if UNITY_6000_1_OR_NEWER
        private SerializedProperty multiviewRenderRegionsOptimizationMode;
#endif

        ReorderableList optionalFeaturesList;
        Dictionary<string, XRFeatureInfoProvider.FeatureInfo> axrFeatureInfosMap = new();
        int deletedElementIndex = -1;

        void OnEnable()
        {
            UpdateFeaturesMap();
            SetupOptionalFeatures();

            symmetricProjection = serializedObject.FindProperty("symmetricProjection");
            optimizeBufferDiscards = serializedObject.FindProperty("optimizeBufferDiscards");

#if UNITY_6000_1_OR_NEWER
            multiviewRenderRegionsOptimizationMode = serializedObject.FindProperty("multiviewRenderRegionsOptimizationMode");
#endif
        }

        void UpdateFeaturesMap()
        {
            axrFeatureInfosMap = XRFeatureInfoProvider.GetFeatureInfos<OpenXRFeature>(SpatialAPIFeaturesConfigurator.RequiresAndroidXRHardware);
        }

        public override void OnInspectorGUI()
        {
            EditorGUIUtility.labelWidth = 300.0f;

            serializedObject.Update();

            EditorGUILayout.LabelField("Rendering Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(symmetricProjection, new GUIContent("Symmetric Projection (Vulkan)"));
            EditorGUILayout.PropertyField(optimizeBufferDiscards, new GUIContent("Optimize Buffer Discards (Vulkan)"));
#if UNITY_6000_1_OR_NEWER
            EditorGUILayout.PropertyField(multiviewRenderRegionsOptimizationMode, s_MultiviewRenderRegionsOptimizationsLabel);
#endif
            EditorGUILayout.Space();

            UpdateFeaturesMap();

            if (deletedElementIndex >= 0)
            {
                optionalFeatures.DeleteArrayElementAtIndex(deletedElementIndex);
                deletedElementIndex = -1;
            }

            optionalFeaturesList.DoLayoutList();

            if (optionalFeatures.arraySize > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "IMPORTANT: Implement runtime checks and fallback logic!\n" +
                    "Apps can be installed on devices without these optional capabilities.\n\n" +
                    "Note: Hardware features are marked android:required=\"false\" only if NOT used by REQUIRED OpenXR features.",
                    MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();

            OpenXRSettings androidOpenXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var serializedOpenXrSettings = new SerializedObject(androidOpenXRSettings);

            androidOpenXRSettings.symmetricProjection = symmetricProjection.boolValue;
            androidOpenXRSettings.optimizeBufferDiscards = optimizeBufferDiscards.boolValue;
#if UNITY_6000_1_OR_NEWER
            androidOpenXRSettings.multiviewRenderRegionsOptimizationMode = (OpenXRSettings.MultiviewRenderRegionsOptimizationMode)multiviewRenderRegionsOptimizationMode.intValue;
#endif
            serializedOpenXrSettings.ApplyModifiedProperties();

            EditorGUIUtility.labelWidth = 0.0f;
        }

        void SetupOptionalFeatures()
        {
            optionalFeatures = serializedObject.FindProperty("optionalFeatures");

            optionalFeaturesList = new ReorderableList(serializedObject, optionalFeatures)
            {
                draggable = false,
                elementHeightCallback = index =>
                {
                    var element = optionalFeatures.GetArrayElementAtIndex(index);
                    var featureIdProp = element.FindPropertyRelative("m_FeatureId");

                    if (!axrFeatureInfosMap.ContainsKey(featureIdProp.stringValue))
                    {
                        return 0f; // parent element was deleted
                    }

                    return EditorGUIUtility.singleLineHeight + 4f;
                },
                drawHeaderCallback = rect => { EditorGUI.LabelField(rect, "Optional Android XR Features (feature/hardware):", EditorStyles.boldLabel); },
                drawElementCallback = (rect, index, _, _) =>
                {
                    var element = optionalFeatures.GetArrayElementAtIndex(index);
                    var featureIdProp = element.FindPropertyRelative("m_FeatureId");

                    if (!axrFeatureInfosMap.ContainsKey(featureIdProp.stringValue))
                    {
                        deletedElementIndex = index;
                        return; // parent element was deleted
                    }

                    var featureNameProp = element.FindPropertyRelative("m_FeatureName");
                    var extensionsProp = element.FindPropertyRelative("m_ExtensionsString");

                    var existingHardware = new HashSet<string>();

                    rect.y += 2;

                    if (!string.IsNullOrEmpty(featureNameProp.stringValue))
                    {
                        const float nameWidthPercent = 0.2f;
                        const float separatorWidth = 30f;
                        const float padding = 5f;

                        var currentX = rect.x;

                        var nameWidth = rect.width * nameWidthPercent;
                        var nameRect = new Rect(currentX, rect.y, nameWidth - padding, rect.height);
                        EditorGUI.LabelField(nameRect, featureNameProp.stringValue, EditorStyles.miniLabel);
                        currentX += nameWidth;

                        var separatorRect = new Rect(currentX, rect.y, separatorWidth, rect.height);
                        EditorGUI.LabelField(separatorRect, "|", EditorStyles.centeredGreyMiniLabel);
                        currentX += separatorWidth;

                        var extensionsWidth = rect.width - (currentX - rect.x) - padding;
                        var extensionsRect = new Rect(currentX + padding, rect.y, extensionsWidth, rect.height);

                        if (extensionsProp == null)
                        {
                            return;
                        }

                        var extensions = extensionsProp.stringValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                        foreach (var extension in extensions)
                        {
                            var hardware = SpatialAPIFeaturesConfigurator.GetHardwareFeature(extension);

                            if (!string.IsNullOrEmpty(hardware))
                            {
                                existingHardware.Add(hardware);
                            }
                        }

                        EditorGUI.LabelField(extensionsRect, string.Join(", ", existingHardware), EditorStyles.miniLabel);
                    }
                },
                onAddDropdownCallback = (_, _) =>
                {
                    var menu = new GenericMenu();
                    var existingIds = new HashSet<string>();

                    for (int i = 0; i < optionalFeatures.arraySize; i++)
                    {
                        var element = optionalFeatures.GetArrayElementAtIndex(i);
                        var featureIdProp = element.FindPropertyRelative("m_FeatureId");
                        if (featureIdProp != null && !string.IsNullOrEmpty(featureIdProp.stringValue))
                        {
                            existingIds.Add(featureIdProp.stringValue);
                        }
                    }

                    var featuresToAdd = axrFeatureInfosMap
                        .Where(f => !existingIds.Contains(f.Key))
                        .ToList();


                    if (featuresToAdd.Count == 0)
                    {
                        menu.AddDisabledItem(new GUIContent("No features available"));
                    }
                    else
                    {
                        foreach (var feature in featuresToAdd)
                        {
                            var capturedFeatureId = feature.Key;
                            var capturedDisplayName = feature.Value.FeatureName;
                            var capturedExtensionsString = feature.Value.ExtensionsString;

                            menu.AddItem(new GUIContent(capturedDisplayName), false, () =>
                            {
                                serializedObject.Update();

                                var newIndex = optionalFeatures.arraySize;
                                optionalFeatures.InsertArrayElementAtIndex(newIndex);
                                var newElement = optionalFeatures.GetArrayElementAtIndex(newIndex);
                                newElement.FindPropertyRelative("m_FeatureName").stringValue = capturedDisplayName;
                                newElement.FindPropertyRelative("m_FeatureId").stringValue = capturedFeatureId;
                                newElement.FindPropertyRelative("m_ExtensionsString").stringValue = capturedExtensionsString;

                                serializedObject.ApplyModifiedProperties();
                            });
                        }
                    }

                    menu.ShowAsContext();
                }
            };
        }
    }
}
