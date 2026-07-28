using UnityEngine.XR.OpenXR.NativeTypes;
using System;
using System.Runtime.InteropServices;
using AOT;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.SubsystemsImplementation.Extensions;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

using XrTrackableTrackerANDROID = System.UIntPtr;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Planes
{
    using XrSession = UIntPtr;
    using XrSpace = UIntPtr;
    using XrTime = UInt64;
    using XrUuidEXT = SerializableGuid;
    using XrTrackableANDROID = System.UInt64;

    internal class PlanesNativeTypes
    {
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct XrTrackableGetInfoANDROID
        {
            internal XrStructureType type;
            internal void* next;
            internal XrTrackableANDROID trackable;
            internal XrSpace baseSpace;
            internal XrTime time;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct XrTrackablePlaneANDROID
        {
            internal XrStructureType type;
            internal void* next;
            internal XrTrackingStateANDROID trackingState;
            internal XrPosef centerPose;
            internal XrExtent2Df extents;
            internal XrPlaneTypeANDROID planeType;
            internal XrPlaneLabelANDROID planeLabel;
            internal XrTrackableANDROID subsumedByPlane;
            internal XrTime lastUpdatedTime;
            internal uint vertexCapacityInput;
            internal uint* vertexCountOutput;
            internal XrVector2f* vertices;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct XrTrackableTrackerCreateInfoANDROID
        {
            XrStructureType type;
            void* next;
            XrTrackableTypeANDROID trackableType;
        }

        internal enum XrStructureType
        {
            XR_TYPE_TRACKABLE_GET_INFO_ANDROID = 1000455000,
            XR_TYPE_ANCHOR_SPACE_CREATE_INFO_ANDROID = 1000455001,
            XR_TYPE_TRACKABLE_PLANE_ANDROID = 1000455003,
            XR_TYPE_TRACKABLE_TRACKER_CREATE_INFO_ANDROID = 1000455004,
            XR_TYPE_SYSTEM_TRACKABLES_PROPERTIES_ANDROID = 1000455005
        }

        internal enum XrTrackingStateANDROID
        {
            XR_TRACKING_STATE_PAUSED_ANDROID = 0,
            XR_TRACKING_STATE_STOPPED_ANDROID = 1,
            XR_TRACKING_STATE_TRACKING_ANDROID = 2,
            XR_TRACKING_STATE_MAX_ENUM_ANDROID = 0x7FFFFFFF
        }

        internal enum XrTrackableTypeANDROID
        {
            XR_TRACKABLE_TYPE_NOT_VALID_ANDROID = 0,
            XR_TRACKABLE_TYPE_PLANE_ANDROID = 1,
            XR_TRACKABLE_TYPE_MAX_ENUM_ANDROID = 0x7FFFFFFF
        }

        internal enum XrPlaneTypeANDROID
        {
            XR_PLANE_TYPE_HORIZONTAL_DOWNWARD_FACING_ANDROID = 0,
            XR_PLANE_TYPE_HORIZONTAL_UPWARD_FACING_ANDROID = 1,
            XR_PLANE_TYPE_VERTICAL_ANDROID = 2,
            XR_PLANE_TYPE_ARBITRARY_ANDROID = 3,
            XR_PLANE_TYPE_MAX_ENUM_ANDROID = 0x7FFFFFFF
        }

        internal enum XrPlaneLabelANDROID
        {
            XR_PLANE_LABEL_UNKNOWN_ANDROID = 0,
            XR_PLANE_LABEL_WALL_ANDROID = 1,
            XR_PLANE_LABEL_FLOOR_ANDROID = 2,
            XR_PLANE_LABEL_CEILING_ANDROID = 3,
            XR_PLANE_LABEL_TABLE_ANDROID = 4,
            XR_PLANE_LABEL_MAX_ENUM_ANDROID = 0x7FFFFFFF
        }

        internal unsafe delegate int CreateTrackableTracker_Delegate(
            XrSession session,
            XrTrackableTrackerCreateInfoANDROID* createInfo,
            XrTrackableTrackerANDROID* trackableTracker);

        internal unsafe delegate int DestroyTrackableTracker_Delegate(
            XrTrackableTrackerANDROID* trackableTracker);

        internal unsafe delegate int GetAllTrackables_Delegate(
            XrTrackableTrackerANDROID trackableTracker,
            uint trackableCapacityInput,
            uint* trackableCountOutput,
            XrTrackableANDROID* trackables);

        internal unsafe delegate int GetAllTrackables_TwoPlanes_Delegate(
            XrTrackableTrackerANDROID trackableTracker,
            uint trackableCapacityInput,
            uint* trackableCountOutput,
            XrTrackableANDROID* trackables);

        internal unsafe delegate int GetAllTrackables_AddUpdateRemovePlanes_Delegate(
            XrTrackableTrackerANDROID trackableTracker,
            uint trackableCapacityInput,
            uint* trackableCountOutput,
            XrTrackableANDROID* trackables);

        internal unsafe delegate int GetAllTrackables_ZeroVertices_Delegate(
            XrTrackableTrackerANDROID trackableTracker,
            uint trackableCapacityInput,
            uint* trackableCountOutput,
            XrTrackableANDROID* trackables);

        internal unsafe delegate int GetTrackablePlane_Delegate(
            XrTrackableTrackerANDROID trackableTracker,
            XrTrackableGetInfoANDROID* getInfo,
            XrTrackablePlaneANDROID* planeOutput);
    }
}
