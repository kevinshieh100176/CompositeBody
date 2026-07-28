using UnityEngine.XR.OpenXR.Features.Android;

namespace UnityEditor.XR.OpenXR.Features.Android
{
    [OpenXRFeatureSet(
        FeatureIds = new[]
        {
            ARSessionFeature.featureId,
            ARPlaneFeature.featureId,
            ARCameraFeature.featureId,
            ARRaycastFeature.featureId,
            ARAnchorFeature.featureId,
            DisplayUtilitiesFeature.featureId,
            AROcclusionFeature.featureId,
            AndroidXRPerformanceMetrics.featureId,
            AndroidXRSupportFeature.featureId,
            ARFaceFeature.featureId,
            ARBoundingBoxFeature.featureId,
            ARMeshFeature.featureId,
            AndroidXRHandMeshData.featureId
        },
        DefaultFeatureIds = new[]
        {
            ARSessionFeature.featureId,
            ARPlaneFeature.featureId,
            ARCameraFeature.featureId,
            ARRaycastFeature.featureId,
            ARAnchorFeature.featureId,
            DisplayUtilitiesFeature.featureId,
            AROcclusionFeature.featureId,
            AndroidXRPerformanceMetrics.featureId,
            AndroidXRSupportFeature.featureId,
            ARFaceFeature.featureId,
            ARBoundingBoxFeature.featureId,
            ARMeshFeature.featureId,
            AndroidXRHandMeshData.featureId
        },
        UiName = "Android XR",
        FeatureSetId = featureSetId,
        SupportedBuildTargets = new[] {
            BuildTargetGroup.Android, BuildTargetGroup.Standalone
        }
    )]
    internal class AndroidFeatureSet
    {
        internal const string featureSetId = "com.unity.openxr.featureset.android";

        public static bool IsEnabled
        {
            get
            {
                var androidXrFeatureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(
                    EditorUserBuildSettings.selectedBuildTargetGroup,
                    featureSetId
                );
                return androidXrFeatureSet.isEnabled;
            }
        }
    }
}
