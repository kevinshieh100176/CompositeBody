using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine.Android;
using UnityEngine.XR.ARSubsystems;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace UnityEngine.XR.OpenXR.Features.Android
{
    /// <summary>
    /// Enables AR Foundation scene meshing support via OpenXR for Android XR devices.
    /// </summary>
#if UNITY_EDITOR
    [OpenXRFeature(
        UiName = "Android XR: AR Scene Meshing",
        BuildTargetGroups = new[] { BuildTargetGroup.Android },
        Company = Constants.k_CompanyName,
        Desc = "AR Foundation scene meshing support on Android XR devices",
        DocumentationLink = Constants.DocsUrls.k_SceneMeshingUrl,
        OpenxrExtensionStrings = k_OpenXRRequestedExtensions,
        Category = FeatureCategory.Feature,
        FeatureId = featureId,
        Version = "0.1.0"
    )]
#endif

    public class ARMeshFeature : AndroidXROpenXRFeature
    {
        /// <summary>
        /// The feature id string. This is used to give the feature a well known id for reference.
        /// </summary>
        public const string featureId =
            "com.unity.openxr.feature.arfoundation-androidxr-scene-meshing";

        internal const string k_SubsystemId = "Android-Scene-Mesh";

        /// <summary>
        /// The set of OpenXR spec extension strings to enable, separated by spaces.
        /// </summary>
        const string k_OpenXRRequestedExtensions = Constants
            .OpenXRExtensions
            .k_XR_ANDROID_scene_meshing;

        static readonly List<XRMeshSubsystemDescriptor> s_MeshDescriptors = new();

        /// <summary>
        /// Instantiates Android OpenXR Mesh subsystem instance, but does not start it.
        /// (Start/Stop is typically handled by AR Foundation managers.)
        /// </summary>
        protected override void OnSubsystemCreate()
        {
            CreateSubsystem<XRMeshSubsystemDescriptor, XRMeshSubsystem>(
                s_MeshDescriptors,
                k_SubsystemId
            );
        }

        /// <summary>
        /// Destroys the mesh subsystem.
        /// </summary>
        protected override void OnSubsystemDestroy()
        {
            DestroySubsystem<XRMeshSubsystem>();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Validation Rules for ARMeshFeature.
        /// </summary>
        protected override void GetValidationChecks(
            List<ValidationRule> rules,
            BuildTargetGroup targetGroup
        )
        {
            rules.AddRange(SharedValidationRules.EnableARSessionValidationRules(this));
        }
#endif
    }
}
