using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Unity.XR.Management.AndroidManifest.Editor;
using UnityEditor.Android;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Android;
using UnityEngine.XR.OpenXR.Features.Interactions;
#if XR_HANDS_1_1_OR_NEWER
using UnityEngine.XR.Hands.OpenXR;
#endif // XR_HANDS_1_1_OR_NEWER

namespace UnityEditor.XR.OpenXR.Features.Android
{
    class AndroidXRManifest : IAndroidManifestRequirementProvider, IPostGenerateGradleAndroidProject
    {
        static readonly XNamespace k_Android = "http://schemas.android.com/apk/res/android";
        static readonly XName k_AndroidName = k_Android + "name";
        static readonly XName k_AndroidValue = k_Android + "value";

        const string k_Property = "property";
        const string k_StringArraySeparator = ";";
        const string k_ActivityStartsFullSpaceUnmanaged = "XR_ACTIVITY_START_MODE_FULL_SPACE_UNMANAGED";

        static readonly string[] k_OpenXRQueriesProviders =
        {
            "org.khronos.openxr.runtime_broker",
            "org.khronos.openxr.system_runtime_broker"
        };

        const string k_XRActivityStartMode = "android.window.PROPERTY_XR_ACTIVITY_START_MODE";

        public int callbackOrder => 1;

        public ManifestRequirement ProvideManifestRequirement()
        {
            if (!XRManagerEditorUtility.IsAnyAxrFeatureEnabled())
                return new ManifestRequirement();

            var androidOpenXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var arPlaneFeature = androidOpenXRSettings.GetFeature<ARPlaneFeature>();
            var arAnchorFeature = androidOpenXRSettings.GetFeature<ARAnchorFeature>();
            var arRaycastFeature = androidOpenXRSettings.GetFeature<ARRaycastFeature>();
            var arCameraFeature = androidOpenXRSettings.GetFeature<ARCameraFeature>();
            var arOcclusionFeature = androidOpenXRSettings.GetFeature<AROcclusionFeature>();
            var arBoundingBoxFeature = androidOpenXRSettings.GetFeature<ARBoundingBoxFeature>();
            var arMeshFeature = androidOpenXRSettings.GetFeature<ARMeshFeature>();

            var elements = new List<ManifestElement>();

            AddElement(
                elements,
                new List<string> { "manifest", "application", "uses-native-library" },
                ("name", "libopenxr.google.so"), ("required", "false"));

            AddPermission(elements, "org.khronos.openxr.permission.OPENXR");

            if ((arPlaneFeature != null && arPlaneFeature.enabled) ||
                (arAnchorFeature != null && arAnchorFeature.enabled) ||
                (arRaycastFeature != null && arRaycastFeature.enabled) ||
                (arCameraFeature != null && arCameraFeature.enabled) ||
                (arOcclusionFeature != null && arOcclusionFeature.enabled) ||
                (arBoundingBoxFeature != null && arBoundingBoxFeature.enabled) ||
                (arMeshFeature != null && arMeshFeature.enabled))
            {
                AddPermission(elements, "android.permission.SCENE_UNDERSTANDING_COARSE");
            }

            if ((arOcclusionFeature != null && arOcclusionFeature.enabled) ||
                (arRaycastFeature != null && arRaycastFeature.enabled) ||
                (arMeshFeature != null && arMeshFeature.enabled))
            {
                AddPermission(elements, "android.permission.SCENE_UNDERSTANDING_FINE");
            }

#if XR_HANDS_1_1_OR_NEWER
            var handTrackingFeature = androidOpenXRSettings.GetFeature<HandTracking>();
            if (handTrackingFeature != null && handTrackingFeature.enabled)
            {
                AddPermission(elements, "android.permission.HAND_TRACKING");
            }
#endif // XR_HANDS_1_1_OR_NEWER

            const string k_EyeTrackingCoarsePermission = "android.permission.EYE_TRACKING_COARSE";
            const string k_EyeTrackingFinePermission = "android.permission.EYE_TRACKING_FINE";

            var faceFeature = androidOpenXRSettings.GetFeature<ARFaceFeature>();
            if (faceFeature != null && faceFeature.enabled)
            {
                AddPermission(elements, k_EyeTrackingCoarsePermission);
                AddPermission(elements, "android.permission.FACE_TRACKING");
            }

            var foveatedRenderingFeature = androidOpenXRSettings.GetFeature<FoveatedRenderingFeature>();
            if (foveatedRenderingFeature != null && foveatedRenderingFeature.enabled)
            {
                AddPermission(elements, k_EyeTrackingCoarsePermission);
                AddPermission(elements, k_EyeTrackingFinePermission);
            }

            var gazeFeature = androidOpenXRSettings.GetFeature<EyeGazeInteraction>();
            if (gazeFeature != null && gazeFeature.enabled)
            {
                AddPermission(elements, k_EyeTrackingFinePermission);
            }

            AddFeature(elements, ("name", "android.software.xr.api.openxr"), ("required", "true"), ("version", "0x00010001"));

            AddElement(
                elements,
                new List<string> { "manifest", "queries", "provider" },
                ("authorities", string.Join(k_StringArraySeparator, k_OpenXRQueriesProviders)));

            SpatialAPIFeaturesConfigurator.ConfigureManifest(elements, androidOpenXRSettings);

            return new ManifestRequirement
            {
                SupportedXRLoaders = new HashSet<Type> { typeof(OpenXRLoader) },
                NewElements = elements
            };
        }

        static void AddElement(
            List<ManifestElement> elements, List<string> elementPath, params (string key, string value)[] attributes)
        {
            var attributesDict = new Dictionary<string, string>();
            foreach (var (key, value) in attributes)
            {
                attributesDict.Add(key, value);
            }

            elements.Add(
                new ManifestElement
                {
                    ElementPath = elementPath,
                    Attributes = attributesDict
                });
        }

        static void AddElement(
            List<ManifestElement> elements, string elementPath, params (string key, string value)[] attributes)
        {
            AddElement(elements, new List<string> { "manifest", elementPath }, attributes);
        }

        static void AddPermission(List<ManifestElement> elements, string permission)
        {
            AddElement(elements, "uses-permission", ("name", permission));
        }

        internal static void AddFeature(List<ManifestElement> elements, params (string key, string value)[] attributes)
        {
            AddElement(elements, "uses-feature", attributes);
        }

        internal static void AddFeatures(List<ManifestElement> elements, IEnumerable<string> features, bool required = true)
        {
            foreach (var feature in features)
            {
                AddFeature(elements, ("name", feature), ("required", required ? "true" : "false"));
            }
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!XRManagerEditorUtility.IsAnyAxrFeatureEnabled())
                return;

            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var manifestDoc = XDocument.Load(manifestPath);
            AddProperties(manifestDoc.Root);
            manifestDoc.Save(manifestPath);
        }

        static void AddProperties(XElement manifest)
        {
            var (onlyOne, launcherActivity) = GetLauncherActivity(manifest);
            if (!onlyOne)
                return;
            if (
                launcherActivity
                    .Elements(k_Property)
                    .Select(x => x.Attribute(k_AndroidName).Value == k_XRActivityStartMode)
                    .Any()
            )
                return;

            launcherActivity.Add(
                new XElement(
                    k_Property,
                    new XAttribute(k_AndroidName, k_XRActivityStartMode),
                    new XAttribute(k_AndroidValue, k_ActivityStartsFullSpaceUnmanaged)
                )
            );
        }

        static (bool success, XElement launcherActivity) GetLauncherActivity(XElement manifest)
        {
            var activities = manifest.Descendants("activity");
            foreach (var activity in activities)
            {
                var hasLauncherIntent = activity.Descendants("intent-filter")
                    .Any(filter =>
                        filter.Elements("action").Any(a => (string)a.Attribute(k_AndroidName) == "android.intent.action.MAIN") &&
                        filter.Elements("category").Any(c => (string)c.Attribute(k_AndroidName) == "android.intent.category.LAUNCHER"));

                if (hasLauncherIntent)
                    return (true, activity);
            }

            Debug.LogWarning("No launcher activity found in manifest.");
            return (false, null);
        }
    }
}
