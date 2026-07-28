using System;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.Management.AndroidManifest.Editor;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Android;

namespace UnityEditor.XR.OpenXR.Features.Android
{
    internal static class SpatialAPIFeaturesConfigurator
    {
        internal const string k_ControllerHardwareFeature = "android.hardware.xr.input.controller";
        internal const string k_HandTrackingHardwareFeature = "android.hardware.xr.input.hand_tracking";
        internal const string k_EyeTrackingHardwareFeature = "android.hardware.xr.input.eye_tracking";
        internal const string k_FaceTrackingHardwareFeature = "android.hardware.xr.input.face_tracking";
        internal const string k_GPSHardwareFeature = "android.hardware.location.gps";
        internal const string k_SpatialAPIFeature = "android.software.xr.api.SPATIAL";

        enum SpatialApiVersion
        {
            Unknown = 0,
            MoohanRelease = 1,
            MoohanOTA1 = 3,
            Post3 = 4
        }

        struct ExtensionInfo
        {
            public SpatialApiVersion version;
            public string hardwareFeatureRequirement;

            public ExtensionInfo(SpatialApiVersion version, string hardwareFeatureRequirement = null)
            {
                this.version = version;
                this.hardwareFeatureRequirement = hardwareFeatureRequirement;
            }
        }

        static readonly Dictionary<string, ExtensionInfo> k_ExtToInfoMap = new()
        {
            ["XR_FB_color_space"] = new(SpatialApiVersion.MoohanRelease, k_ControllerHardwareFeature),
            ["XR_EXT_dpad_binding"] = new(SpatialApiVersion.MoohanRelease, k_ControllerHardwareFeature),
            ["XR_ANDROID_eye_tracking"] = new(SpatialApiVersion.MoohanRelease, k_EyeTrackingHardwareFeature),
            ["XR_EXT_eye_gaze_interaction"] = new(SpatialApiVersion.MoohanRelease, k_EyeTrackingHardwareFeature),
            ["XR_META_foveation_eye_tracked"] = new(SpatialApiVersion.MoohanRelease, k_EyeTrackingHardwareFeature),
            ["XR_VARJO_foveated_rendering"] = new(SpatialApiVersion.MoohanRelease, k_EyeTrackingHardwareFeature),
            ["XR_FB_hand_tracking_mesh"] = new(SpatialApiVersion.MoohanRelease, k_HandTrackingHardwareFeature),
            ["XR_FB_hand_tracking_aim"] = new(SpatialApiVersion.MoohanRelease, k_HandTrackingHardwareFeature),
            ["XR_EXT_palm_pose"] = new(SpatialApiVersion.MoohanRelease, k_HandTrackingHardwareFeature),
            ["XR_ANDROID_hand_mesh"] = new(SpatialApiVersion.MoohanRelease, k_HandTrackingHardwareFeature),
            ["XR_EXT_hand_interaction"] = new(SpatialApiVersion.MoohanRelease, k_HandTrackingHardwareFeature),
            ["XR_EXT_hand_tracking"] = new(SpatialApiVersion.MoohanRelease, k_HandTrackingHardwareFeature),

            ["XR_ANDROID_face_tracking_data_source"] = new(SpatialApiVersion.MoohanOTA1, k_EyeTrackingHardwareFeature),
            ["XR_ANDROID_eye_tracking_calibration_state"] = new(SpatialApiVersion.MoohanOTA1, k_EyeTrackingHardwareFeature),

            ["XR_ANDROID_geospatial"] = new(SpatialApiVersion.Post3, k_GPSHardwareFeature),
        };

        static int CalculateMinVersion(IEnumerable<string> extensions)
        {
            var minVersion = 1;

            if (extensions == null)
            {
                return minVersion;
            }

            foreach (var extension in extensions)
            {
                if (string.IsNullOrEmpty(extension))
                {
                    continue;
                }

                if (k_ExtToInfoMap.TryGetValue(extension, out var info))
                {
                    minVersion = Math.Max(minVersion, (int)info.version);
                }
            }

            return minVersion;
        }

        static IEnumerable<string> EnumerateExtensions(string extensionsString)
        {
            var extensions = extensionsString.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var extension in extensions)
            {
                yield return extension;
            }
        }

        internal static string GetHardwareFeature(string extension)
        {
            return k_ExtToInfoMap.TryGetValue(extension, out var extensionInfo) ? extensionInfo.hardwareFeatureRequirement : null;
        }

        internal static bool RequiresAndroidXRHardware(OpenXRFeature feature, OpenXRFeatureAttribute attribute)
        {
            return EnumerateExtensions(attribute.OpenxrExtensionStrings)
                .Any(extension => k_ExtToInfoMap.TryGetValue(extension, out var info)
                    && !string.IsNullOrEmpty(info.hardwareFeatureRequirement));
        }

        internal static void ConfigureManifest(List<ManifestElement> elements, OpenXRSettings androidOpenXRSettings)
        {
            var axrSupportFeature = androidOpenXRSettings.GetFeature<AndroidXRSupportFeature>();
            var axrFeatureInfosMap = XRFeatureInfoProvider.GetFeatureInfos<OpenXRFeature>(RequiresAndroidXRHardware);

            var optionalFeatureIds = axrSupportFeature.optionalFeatures.Select(f => f.FeatureId).ToHashSet();
            var requiredHardware = new HashSet<string>();
            var optionalHardware = new HashSet<string>();
            var requiredExtensions = new HashSet<string>();

            foreach (var feature in axrFeatureInfosMap)
            {
                var isRequiredFeature = !optionalFeatureIds.Contains(feature.Key);

                foreach (var extension in EnumerateExtensions(feature.Value.ExtensionsString))
                {
                    if (isRequiredFeature)
                    {
                        requiredExtensions.Add(extension);
                    }

                    if (!k_ExtToInfoMap.TryGetValue(extension, out var extensionInfo) ||
                        string.IsNullOrEmpty(extensionInfo.hardwareFeatureRequirement))
                    {
                        continue;
                    }

                    if (isRequiredFeature)
                    {
                        requiredHardware.Add(extensionInfo.hardwareFeatureRequirement);
                        optionalHardware.Remove(extensionInfo.hardwareFeatureRequirement);
                    }
                    else if (!requiredHardware.Contains(extensionInfo.hardwareFeatureRequirement))
                    {
                        optionalHardware.Add(extensionInfo.hardwareFeatureRequirement);
                    }
                }
            }

            var spatialAPIminVersion = CalculateMinVersion(requiredExtensions);

            AndroidXRManifest.AddFeatures(elements, requiredHardware, required: true);
            AndroidXRManifest.AddFeatures(elements, optionalHardware, required: false);
            AndroidXRManifest.AddFeature(elements,
                ("name", k_SpatialAPIFeature),
                ("required", "true"),
                ("version", spatialAPIminVersion.ToString()));
        }
    }
}
