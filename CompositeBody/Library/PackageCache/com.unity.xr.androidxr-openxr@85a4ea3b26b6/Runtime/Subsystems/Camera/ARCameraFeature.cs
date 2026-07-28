using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.XR.CoreUtils;
using UnityEngine.Assertions;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.NativeTypes;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace UnityEngine.XR.OpenXR.Features.Android
{
    /// <summary>
    /// Enables AR Foundation passthrough support via OpenXR for Android XR devices.
    /// </summary>
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "Android XR: AR Camera",
        BuildTargetGroups = new[] {
            BuildTargetGroup.Android,
#if UNITY_STANDALONE_WIN
            BuildTargetGroup.Standalone,
#endif // UNITY_STANDALONE_WIN
        },
        Company = Constants.k_CompanyName,
        Desc = "AR Foundation camera support on Android XR devices.",
        DocumentationLink = Constants.DocsUrls.k_CameraUrl,
        OpenxrExtensionStrings = k_OpenXRRequestedExtensions,
        Category = FeatureCategory.Feature,
        FeatureId = featureId,
        Version = "0.1.0")]
#endif
    public class ARCameraFeature : AndroidXROpenXRFeature
    {
        /// <summary>
        /// The feature id string. This is used to give the feature an id for reference.
        /// </summary>
        public const string featureId = "com.unity.openxr.feature.arfoundation-androidxr-camera";

        /// <summary>
        /// Raised when the global passthrough dimming level changes externally.
        /// </summary>
        public static event Action dimmingLevelChanged = delegate { };

        /// <summary>
        /// The set of OpenXR spec extension strings to enable, separated by spaces.
        /// </summary>
        const string k_OpenXRRequestedExtensions =
            Constants.OpenXRExtensions.k_XR_ANDROID_passthrough_camera_state + " " +
            Constants.OpenXRExtensions.k_XR_ANDROID_light_estimation + " " +
            Constants.OpenXRExtensions.k_XR_ANDROID_global_passthrough_dimming;

        static List<XRCameraSubsystemDescriptor> s_CameraDescriptors = new();

        static bool s_PassthroughEnabled = false;

        /// <summary>
        /// Instantiates Android OpenXR Session subsystem instance, but does not start it.
        /// (Start/Stop is typically handled by AR Foundation managers.)
        /// </summary>
        protected override void OnSubsystemCreate()
        {
            CreateSubsystem<XRCameraSubsystemDescriptor, XRCameraSubsystem>(
                s_CameraDescriptors,
                AndroidOpenXRCameraSubsystem.k_SubsystemId);
        }

        /// <summary>
        /// Destroys the camera subsystem.
        /// </summary>
        protected override void OnSubsystemDestroy()
        {
            DestroySubsystem<XRCameraSubsystem>();
        }

        /// <inheritdoc/>
        /// <param name="xrSession">The handle of the newly created XrSession.</param>
        protected override unsafe void OnSessionCreate(ulong xrSession)
        {
            base.OnSessionCreate(xrSession);
            PollEventRouter.TrySubscribeToEventType(XrStructureType.EventDataGlobalDimmingLevelChangedAndroid, OnDimmingLevelChanged);
        }

        /// <inheritdoc/>
        /// <param name="xrSession">The handle of the XrSession being destroyed.</param>
        protected override unsafe void OnSessionDestroy(ulong xrSession)
        {
            base.OnSessionDestroy(xrSession);
            PollEventRouter.TryUnsubscribeFromEventType(XrStructureType.EventDataGlobalDimmingLevelChangedAndroid, OnDimmingLevelChanged);
        }

        /// <summary>
        /// Called when the runtime fires an <c>XrEventDataGlobalDimmingLevelChangedANDROID</c> event,
        /// indicating that the dimming level has changed externally. Reads the new dimming level from
        /// the runtime and raises <see cref="dimmingLevelChanged"/> if the read succeeds.
        /// </summary>
        /// <param name="baseHeader">Pointer to the base event header, cast internally to
        /// <c>XrEventDataGlobalDimmingLevelChangedANDROID</c>.</param>
        static unsafe void OnDimmingLevelChanged(XrEventDataBaseHeader* baseHeader)
        {
            var eventData = *(XrEventDataGlobalDimmingLevelChangedANDROID*)baseHeader;
            Assert.IsTrue(eventData.type == XrStructureType.EventDataGlobalDimmingLevelChangedAndroid);
            dimmingLevelChanged.Invoke();
        }

        /// <summary>
        /// Realigns environment blend mode with current passthrough state if they are misaligned after the mode was changed.
        /// </summary>
        /// <param name="xrEnvironmentBlendMode">New environment blend mode value</param>
        protected override void OnEnvironmentBlendModeChange(XrEnvironmentBlendMode xrEnvironmentBlendMode)
        {
            if (s_PassthroughEnabled && xrEnvironmentBlendMode != XrEnvironmentBlendMode.AlphaBlend)
                SetEnvironmentBlendMode(XrEnvironmentBlendMode.AlphaBlend);

            else if (!s_PassthroughEnabled && xrEnvironmentBlendMode != XrEnvironmentBlendMode.Opaque)
                SetEnvironmentBlendMode(XrEnvironmentBlendMode.Opaque);
        }

        /// <summary>
        /// Controls passthrough activity by setting the environment blend mode.
        /// </summary>
        internal static void SetPassthrough(bool active)
        {
            s_PassthroughEnabled = active;
            SetEnvironmentBlendMode(active ? XrEnvironmentBlendMode.AlphaBlend : XrEnvironmentBlendMode.Opaque);
        }

        internal static bool GetPassthrough()
        {
            return s_PassthroughEnabled;
        }

        static XRResultStatus ValidateDimmingExtension()
        {
            if (!OpenXRRuntime.IsExtensionEnabled(Constants.OpenXRExtensions.k_XR_ANDROID_global_passthrough_dimming))
            {
                Debug.LogWarning($"Extension {Constants.OpenXRExtensions.k_XR_ANDROID_global_passthrough_dimming} is not enabled on current runtime.");
                return new XRResultStatus(XRResultStatus.StatusCode.Unsupported);
            }
            return new XRResultStatus(XRResultStatus.StatusCode.UnqualifiedSuccess);
        }

        /// <summary>
        /// Gets the dimming levels supported by the runtime.
        /// </summary>
        /// <param name="allocator">The allocator to use for the output array.</param>
        /// <returns>
        /// A result containing a <see cref="NativeArray{T}"/> of floating-point values in the range [0.0, 1.0],
        /// where 0.0 represents no dimming (full brightness) and 1.0 represents maximum dimming.
        /// The caller is responsible for disposing the array when the result indicates success.
        /// On failure, the result value is <see langword="default"/> and does not require disposal.
        /// </returns>
        public static unsafe Result<NativeArray<float>> TryGetSupportedDimmingLevels(Allocator allocator)
        {
            var validationStatus = ValidateDimmingExtension();
            if (validationStatus.IsError())
                return new Result<NativeArray<float>>(validationStatus, default);

            uint levelCount = 0;
            var result = NativeApi.TryGetSupportedDimmingLevelsCount(ref levelCount);
            if (result.IsError())
            {
                Debug.LogError($"Failed to retrieve dimming levels count. Status: {result.statusCode}, Native status code: {result.nativeStatusCode}");
                return new Result<NativeArray<float>>(result, default);
            }

            if (levelCount == 0)
                return new Result<NativeArray<float>>(result, new NativeArray<float>(0, allocator));

            if (levelCount > int.MaxValue)
            {
                Debug.LogError($"Level count {levelCount} exceeds maximum array size {int.MaxValue}");
                return new Result<NativeArray<float>>(new XRResultStatus(XRResultStatus.StatusCode.UnknownError), default);
            }

            var dimmingLevels = new NativeArray<float>((int)levelCount, allocator);
            result = NativeApi.TryGetSupportedDimmingLevels(dimmingLevels.GetUnsafePtr(), levelCount);
            if (result.IsError())
            {
                Debug.LogError($"Failed to retrieve dimming levels. Status: {result.statusCode}, Native status code: {result.nativeStatusCode}");
                dimmingLevels.Dispose();
                return new Result<NativeArray<float>>(result, default);
            }

            return new Result<NativeArray<float>>(result, dimmingLevels);
        }

        /// <summary>
        /// Gets the current global passthrough dimming level applied by the runtime.
        /// </summary>
        /// <returns>
        /// A result containing the current dimming intensity in the range [0.0, 1.0], where 0.0 represents no dimming
        /// (full passthrough brightness) and 1.0 represents maximum dimming (darkest passthrough).
        /// </returns>
        public static Result<float> TryGetGlobalDimmingLevel()
        {
            var validationStatus = ValidateDimmingExtension();
            if (validationStatus.IsError())
            {
                return new Result<float>(validationStatus, 0.0f);
            }

            var currentDimmingLevel = 0.0f;
            var resultStatus = NativeApi.TryGetGlobalDimmingLevelNative(ref currentDimmingLevel);
            return new Result<float>(resultStatus, currentDimmingLevel);
        }

        /// <summary>
        /// Requests the runtime to set the global passthrough dimming level.
        /// </summary>
        /// <param name="dimmingLevel">
        /// Desired dimming intensity in the range [0.0, 1.0], where 0.0 represents no dimming (full passthrough brightness)
        /// and 1.0 represents maximum dimming (darkest passthrough). Values outside this range will be clamped.
        /// </param>
        /// <returns>
        /// Status indicating whether the request was accepted by the runtime.
        /// </returns>
        /// <remarks>
        /// A dimming level that doesn't map exactly to a supported level will be rounded to the closest one.
        /// </remarks>
        public static XRResultStatus TryRequestGlobalDimmingLevel(float dimmingLevel)
        {
            var validationStatus = ValidateDimmingExtension();
            if (validationStatus.IsError())
                return validationStatus;

            return NativeApi.TryRequestGlobalDimmingLevelNative(Mathf.Clamp01(dimmingLevel));
        }

#if UNITY_EDITOR
        /// <summary>
        /// Validation Rules for ARCameraFeature.
        /// </summary>
        protected override void GetValidationChecks(List<ValidationRule> rules, BuildTargetGroup targetGroup)
        {
            var AdditionalRules = new ValidationRule[]
            {
                new ValidationRule(this)
                {
                    message = "Passthrough requires Camera clear flags set to solid color with alpha value zero.",
                    checkPredicate = () =>
                    {
                        var xrOrigin = FindAnyObjectByType<XROrigin>();
                        if (xrOrigin == null || !xrOrigin.enabled) return true;

                        var camera = xrOrigin.Camera;
                        if (camera == null || camera.GetComponent<ARCameraManager>() == null) return true;

                        return camera.clearFlags == CameraClearFlags.SolidColor && Mathf.Approximately(camera.backgroundColor.a, 0);
                    },
                    fixItAutomatic = true,
                    fixItMessage = "Set your XR Origin camera's Clear Flags to solid color with alpha value zero.",
                    fixIt = () =>
                    {
                        var xrOrigin = FindAnyObjectByType<XROrigin>();
                        if (xrOrigin != null || xrOrigin.enabled)
                        {
                            var camera = xrOrigin.Camera;
                            if (camera != null || camera.GetComponent<ARCameraManager>() != null)
                            {
                                camera.clearFlags = CameraClearFlags.SolidColor;
                                Color clearColor = camera.backgroundColor;
                                clearColor.a = 0;
                                camera.backgroundColor = clearColor;
                            }
                        }
                    },
                    error = false
                },
                new ValidationRule(this)
                {
                    message = "AR Camera Manager component should be enabled for Passthrough to function correctly.",
                    checkPredicate = () =>
                    {
                        var cameraManager = FindAnyObjectByType<ARCameraManager>();
                        return cameraManager != null && cameraManager.enabled;
                    },
                    fixItAutomatic = true,
                    fixItMessage = "Find the object with ARCameraManager component and enable it.",
                    fixIt = () =>
                    {
                        var cameraManager = FindAnyObjectByType<ARCameraManager>();
                        if (cameraManager != null)
                            cameraManager.enabled = true;
                    },
                    error = false
                }
            };

            rules.AddRange(AdditionalRules);
            rules.AddRange(SharedValidationRules.EnableARSessionValidationRules(this));
        }
#endif

        static class NativeApi
        {
            [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Camera_TryGetSupportedDimmingLevelsCount")]
            public static extern XRResultStatus TryGetSupportedDimmingLevelsCount(ref uint levelsCount);

            [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Camera_TryGetSupportedDimmingLevels")]
            public static extern unsafe XRResultStatus TryGetSupportedDimmingLevels(void* dimmingLevels, uint levelCount);

            [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Camera_TryGetGlobalDimmingLevel")]
            public static extern XRResultStatus TryGetGlobalDimmingLevelNative(ref float currentDimmingLevel);

            [DllImport(Constants.k_ARFoundationLibrary, EntryPoint = "UnityOpenXRAndroid_Camera_TryRequestGlobalDimmingLevel")]
            public static extern XRResultStatus TryRequestGlobalDimmingLevelNative(float dimmingLevel);
        }
    }
}
