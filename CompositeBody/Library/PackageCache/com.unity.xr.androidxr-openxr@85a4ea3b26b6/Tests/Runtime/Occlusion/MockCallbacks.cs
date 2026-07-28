#if UNITY_EDITOR && UNITY_ANDROID
using System;
using System.Runtime.InteropServices;
using AOT;

using XrDepthSwapchainANDROID = System.IntPtr;
using XrSession = System.UIntPtr;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Occlusion
{
    internal static class MockCallbacks
    {
        const NativeTypes.XrDepthCameraResolutionANDROID SelectedResolution =
            NativeTypes.XrDepthCameraResolutionANDROID.XR_DEPTH_CAMERA_RESOLUTION_160x160_ANDROID;

        internal const int DepthWidth = 160;
        internal const int DepthHeight = 160;

        internal static readonly unsafe IntPtr EnumerateDepthSwapchainImages =
            Marshal.GetFunctionPointerForDelegate(
                new NativeTypes.EnumerateDepthSwapchainImages_Delegate(EnumerateDepthSwapchainImages_MockCallback));

        internal static readonly unsafe IntPtr CreateDepthSwapchain =
            Marshal.GetFunctionPointerForDelegate(
                new NativeTypes.CreateDepthSwapchain_Delegate(CreateDepthSwapchain_MockCallback));

        internal static readonly unsafe IntPtr EnumerateDepthResolutions =
            Marshal.GetFunctionPointerForDelegate(
                new NativeTypes.EnumerateResolutions_Delegate(EnumerateDepthResolutions_MockCallback));

        internal static readonly unsafe IntPtr AcquireDepthSwapchainImages =
            Marshal.GetFunctionPointerForDelegate(
                new NativeTypes.AcquireDepthSwapchainImagesANDROID_Delegate(AcquireDepthSwapchainImagesANDROID_MockCallback));

        [MonoPInvokeCallback(typeof(NativeTypes.EnumerateDepthSwapchainImages_Delegate))]
        internal static unsafe int EnumerateDepthSwapchainImages_MockCallback(
            XrDepthSwapchainANDROID depthSwapchain,
            UInt32 depthImageCapacityInput,
            UInt32* depthImageCountOutput,
            NativeTypes.XrDepthSwapchainImageANDROID* depthImages)
        {
            *depthImageCountOutput = 2;
            return 0;
        }

        [MonoPInvokeCallback(typeof(NativeTypes.CreateDepthSwapchain_Delegate))]
        internal static unsafe int CreateDepthSwapchain_MockCallback(
            XrSession xrSession,
            NativeTypes.XrDepthSwapchainCreateInfoANDROID* swapchainCreateInfo,
            XrDepthSwapchainANDROID* depthSwapchain)
        {
            return 0;
        }

        [MonoPInvokeCallback(typeof(NativeTypes.EnumerateResolutions_Delegate))]
        internal static unsafe int EnumerateDepthResolutions_MockCallback(
            XrSession session,
            UInt32 resolutionCapacityInput,
            UInt32* resolutionCountOutput,
            NativeTypes.XrDepthCameraResolutionANDROID* resolutions)
        {
            *resolutionCountOutput = 1;
            *resolutions = SelectedResolution;
            return 0;
        }

        [MonoPInvokeCallback(typeof(NativeTypes.AcquireDepthSwapchainImagesANDROID_Delegate))]
        internal static unsafe int AcquireDepthSwapchainImagesANDROID_MockCallback(
            XrDepthSwapchainANDROID depthSwapchain,
            NativeTypes.XrDepthAcquireInfoANDROID* acquireInfo,
            ref NativeTypes.XrDepthAcquireResultANDROID acquireResult)
        {
            return 0;
        }
    }
}
#endif
