#if UNITY_EDITOR && UNITY_ANDROID
using UnityEngine.XR.OpenXR.NativeTypes;
using System;
using System.Runtime.InteropServices;
using XrSession = System.UIntPtr;
using XrSpace = System.UIntPtr;
using XrDepthSwapchainCreateFlagsANDROID = System.UInt64;
using XrDepthSwapchainANDROID = System.IntPtr;
using XrTime = System.UInt64;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Occlusion
{
    internal static class NativeTypes
    {
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct XrDepthViewANDROID
        {
            internal XrStructureType type;
            internal void* next;
            internal XrFovf fov;
            internal XrPosef pose;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct XrDepthAcquireResultANDROID
        {
            internal XrStructureType type;
            internal void* next;
            internal UInt32 acquiredIndex;
            internal XrTime exposureTimestamp;
            internal XrDepthViewANDROID view0;
            internal XrDepthViewANDROID view1;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct XrDepthAcquireInfoANDROID
        {
            internal XrStructureType type;
            internal void* next;
            internal XrSpace space;
            internal XrTime displayTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct XrDepthSwapchainCreateInfoANDROID
        {
            internal XrStructureType type;
            internal void* next;
            internal XrDepthCameraResolutionANDROID resolution;
            internal XrDepthSwapchainCreateFlagsANDROID createFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct XrDepthSwapchainImageANDROID
        {
            internal XrStructureType type;
            internal void* next;
            internal float* rawDepthImage;
            internal byte* rawDepthConfidenceImage;
            internal float* smoothDepthImage;
            internal byte* smoothDepthConfidenceImage;
        }

        internal enum XrDepthCameraResolutionANDROID
        {
            XR_DEPTH_CAMERA_RESOLUTION_80x80_ANDROID = 0,
            XR_DEPTH_CAMERA_RESOLUTION_160x160_ANDROID = 1,
            XR_DEPTH_CAMERA_RESOLUTION_320x320_ANDROID = 2,
            XR_DEPTH_CAMERA_RESOLUTION_MAX_ENUM_ANDROID = 0x7FFFFFFF
        }

        internal unsafe delegate int EnumerateDepthSwapchainImages_Delegate(
            XrDepthSwapchainANDROID depthSwapchain,
            UInt32 depthImageCapacityInput,
            UInt32* depthImageCountOutput,
            XrDepthSwapchainImageANDROID* depthImages);

        internal unsafe delegate int CreateDepthSwapchain_Delegate(
            XrSession xrSession,
            XrDepthSwapchainCreateInfoANDROID* swapchainCreateInfo,
            XrDepthSwapchainANDROID* depthSwapchain);

        internal unsafe delegate int EnumerateResolutions_Delegate(
            XrSession session,
            UInt32 resolutionCapacityInput,
            UInt32* resolutionCountOutput,
            XrDepthCameraResolutionANDROID* resolutions);

        internal unsafe delegate int AcquireDepthSwapchainImagesANDROID_Delegate(
            XrDepthSwapchainANDROID depthSwapchain,
            XrDepthAcquireInfoANDROID* acquireInfo,
            ref XrDepthAcquireResultANDROID acquireResult);
    }
}
#endif
