using System;
using System.Runtime.InteropServices;
using AOT;
using System.Collections.Generic;
using UnityEngine.XR.OpenXR.NativeTypes;
using XrSession = System.UIntPtr;
using XrTime = System.UInt64;
using XrTrackableTrackerANDROID = System.UIntPtr;
using XrTrackableANDROID = System.UInt64;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Planes
{
    internal static class PlaneMockCallbacks
    {
        // Time from is currently outside of my control, so override it
        internal static XrTime CurrentTime = 7088126434811U;

        internal static readonly unsafe IntPtr CreateTrackableTracker =
            Marshal.GetFunctionPointerForDelegate(
                new PlanesNativeTypes.CreateTrackableTracker_Delegate(CreateTrackableTracker_MockCallback));

        internal static readonly unsafe IntPtr DestroyTrackableTracker =
            Marshal.GetFunctionPointerForDelegate(
                new PlanesNativeTypes.DestroyTrackableTracker_Delegate(DestroyTrackableTracker_MockCallback));

        internal static readonly unsafe IntPtr GetAllTrackables =
            Marshal.GetFunctionPointerForDelegate(
                new PlanesNativeTypes.GetAllTrackables_Delegate(GetAllTrackables_MockCallback));

        internal static readonly unsafe IntPtr GetTrackablePlane =
            Marshal.GetFunctionPointerForDelegate(
                new PlanesNativeTypes.GetTrackablePlane_Delegate(GetTrackablePlane_MockCallback));

        internal static readonly unsafe IntPtr GetAllTrackables_TwoPlanes =
            Marshal.GetFunctionPointerForDelegate(
                new PlanesNativeTypes.GetAllTrackables_TwoPlanes_Delegate(GetAllTrackables_TwoPlanes_MockCallback));

        internal static readonly unsafe IntPtr GetAllTrackables_AddUpdateRemovePlanes =
            Marshal.GetFunctionPointerForDelegate(
                new PlanesNativeTypes.GetAllTrackables_AddUpdateRemovePlanes_Delegate(GetAllTrackables_AddUpdateRemovePlanes_MockCallback));

        internal static readonly unsafe IntPtr GetAllTrackables_ZeroVertices =
            Marshal.GetFunctionPointerForDelegate(
                new PlanesNativeTypes.GetAllTrackables_ZeroVertices_Delegate(GetAllTrackables_ZeroVertices_MockCallback));

        [MonoPInvokeCallback(typeof(PlanesNativeTypes.CreateTrackableTracker_Delegate))]
        internal static unsafe int CreateTrackableTracker_MockCallback(
            XrSession session,
            PlanesNativeTypes.XrTrackableTrackerCreateInfoANDROID* createInfo,
            XrTrackableTrackerANDROID* trackableTracker)
        {
            return 0;
        }

        [MonoPInvokeCallback(typeof(PlanesNativeTypes.DestroyTrackableTracker_Delegate))]
        internal static unsafe int DestroyTrackableTracker_MockCallback(
            XrTrackableTrackerANDROID* trackableTracker)
        {
            return 0;
        }

        [MonoPInvokeCallback(typeof(PlanesNativeTypes.GetAllTrackables_Delegate))]
        internal static unsafe int GetAllTrackables_MockCallback(
            XrTrackableTrackerANDROID trackableTracker,
            uint trackableCapacityInput,
            uint* trackableCountOutput,
            XrTrackableANDROID* trackables)
        {
            *trackableCountOutput = 0;
            return 0;
        }

        [MonoPInvokeCallback(typeof(PlanesNativeTypes.GetAllTrackables_Delegate))]
        internal static unsafe int GetAllTrackables_TwoPlanes_MockCallback(
            XrTrackableTrackerANDROID trackableTracker,
            uint trackableCapacityInput,
            uint* trackableCountOutput,
            XrTrackableANDROID* trackables)
        {
            *trackableCountOutput = 2;
            if (trackableCapacityInput == 0)
            {
                return 0;
            }
            trackables[0] = 644245094401U;
            trackables[1] = 25769803777U;
            return 0;
        }

        [MonoPInvokeCallback(typeof(PlanesNativeTypes.GetAllTrackables_Delegate))]
        internal static unsafe int GetAllTrackables_AddUpdateRemovePlanes_MockCallback(
            XrTrackableTrackerANDROID trackableTracker,
            uint trackableCapacityInput,
            uint* trackableCountOutput,
            XrTrackableANDROID* trackables)
        {
            *trackableCountOutput = 3;
            if (trackableCapacityInput == 0)
            {
                return 0;
            }
            // 25769803777 was removed
            trackables[0] = 644245094401U; // Updated (vertical)
            trackables[1] = 3478923509761U; // Added (horizontal)
            trackables[2] = 3904125272065U; // Added (horizontal)
            return 0;
        }

        [MonoPInvokeCallback(typeof(PlanesNativeTypes.GetAllTrackables_Delegate))]
        internal static unsafe int GetAllTrackables_ZeroVertices_MockCallback(
            XrTrackableTrackerANDROID trackableTracker,
            uint trackableCapacityInput,
            uint* trackableCountOutput,
            XrTrackableANDROID* trackables)
        {
            *trackableCountOutput = 1;
            if (trackableCapacityInput == 0)
            {
                return 0;
            }
            trackables[0] = 661424963585U;
            return 0;
        }

        [MonoPInvokeCallback(typeof(PlanesNativeTypes.GetTrackablePlane_Delegate))]
        internal static unsafe int GetTrackablePlane_MockCallback(
            XrTrackableTrackerANDROID trackableTracker,
            PlanesNativeTypes.XrTrackableGetInfoANDROID* getInfo,
            PlanesNativeTypes.XrTrackablePlaneANDROID* planeOutput)
        {
            if (planeOutput->vertexCapacityInput == 0)
            {
                *(planeOutput->vertexCountOutput) = GetVertexCount(getInfo->trackable);
            }
            else
            {
                SetPlaneInfoFromTimeAndId(getInfo->trackable, planeOutput);
            }
            return 0;
        }

        // Plane data from this function was taken from data collected from the Moohan device
        internal static unsafe void SetPlaneInfoFromTimeAndId(
            XrTrackableANDROID planeId,
            PlanesNativeTypes.XrTrackablePlaneANDROID* plane)
        {
            if (planeId == 644245094401U) // Used in Two Planes and Added/Updated/Removed
            {
                if (CurrentTime == 7088126434811U) // Two Planes
                {
                    var planePose = new XrPosef();
                    planePose.Position = new XrVector3f(1.093416F, 1.626085F, 2.438722F);
                    planePose.Orientation = new XrQuaternionf(-0.110304F, -0.149221F, 0.695875F, 0.693775F);
                    XrVector2f[] points =
                    {
                        new(-0.871075F, 0.452861F), // 1
                        new(0.676295F, 1.443324F), // 2
                        new(0.767670F, 1.435223F), // 3
                        new(0.845309F, 1.364514F), // 4
                        new(0.864124F, 1.115645F), // 5
                        new(0.871075F, 0.737151F), // 6
                        new(0.871075F, 0.225101F), // 7
                        new(0.869446F, 0.017170F), // 8
                        new(0.853406F, -0.727843F), // 9
                        new(0.848436F, -0.832505F), // 10
                        new(0.829442F, -1.228444F), // 11
                        new(0.808567F, -1.398244F), // 12
                        new(0.789651F, -1.443324F), // 13
                        new(-0.753807F, -1.225954F), // 14
                        new(-0.796433F, -1.178573F) // 15
                    };
                    plane->type = PlanesNativeTypes.XrStructureType.XR_TYPE_TRACKABLE_PLANE_ANDROID;
                    plane->next = null;
                    plane->trackingState = PlanesNativeTypes.XrTrackingStateANDROID.XR_TRACKING_STATE_TRACKING_ANDROID;
                    plane->centerPose = planePose;
                    plane->extents = new XrExtent2Df(1.742149F, 2.886648F);
                    plane->planeType = PlanesNativeTypes.XrPlaneTypeANDROID.XR_PLANE_TYPE_VERTICAL_ANDROID;
                    plane->planeLabel = PlanesNativeTypes.XrPlaneLabelANDROID.XR_PLANE_LABEL_WALL_ANDROID;
                    plane->subsumedByPlane = 0U;
                    plane->lastUpdatedTime = 1253657829U;
                    *(plane->vertexCountOutput) = (uint)points.Length;
                    for (uint p = 0; p < *(plane->vertexCountOutput); p++)
                    {
                        plane->vertices[p] = points[p];
                    }
                }
                if (CurrentTime == 7088140323811U) // Added/Updated/Removed for Updated
                {
                    var planePose = new XrPosef();
                    planePose.Position = new XrVector3f(1.093420F, 1.626078F, 2.438721F);
                    planePose.Orientation = new XrQuaternionf(-0.110305F, -0.149220F, 0.695875F, 0.693775F);
                    XrVector2f[] points =
                    {
                        new(-0.871075F, 0.452861F), // 1
                        new(0.676295F, 1.443324F), // 2
                        new(0.767670F, 1.435223F), // 3
                        new(0.845309F, 1.364514F), // 4
                        new(0.864124F, 1.115645F), // 5
                        new(0.871075F, 0.737151F), // 6
                        new(0.871075F, 0.225101F), // 7
                        new(0.869446F, 0.017170F), // 8
                        new(0.853406F, -0.727843F), // 9
                        new(0.848436F, -0.832505F), // 10
                        new(0.829442F, -1.228444F), // 11
                        new(0.808567F, -1.398244F), // 12
                        new(0.789651F, -1.443324F), // 13
                        new(-0.753807F, -1.225954F), // 14
                        new(-0.796433F, -1.178573F) // 15
                    };
                    plane->type = PlanesNativeTypes.XrStructureType.XR_TYPE_TRACKABLE_PLANE_ANDROID;
                    plane->next = null;
                    plane->trackingState = PlanesNativeTypes.XrTrackingStateANDROID.XR_TRACKING_STATE_TRACKING_ANDROID;
                    plane->centerPose = planePose;
                    plane->extents = new XrExtent2Df(1.742149F, 2.886648F);
                    plane->planeType = PlanesNativeTypes.XrPlaneTypeANDROID.XR_PLANE_TYPE_VERTICAL_ANDROID;
                    plane->planeLabel = PlanesNativeTypes.XrPlaneLabelANDROID.XR_PLANE_LABEL_WALL_ANDROID;
                    plane->subsumedByPlane = 0U;
                    plane->lastUpdatedTime = 1353693611U;
                    *(plane->vertexCountOutput) = (uint)points.Length;
                    for (uint p = 0; p < *(plane->vertexCountOutput); p++)
                    {
                        plane->vertices[p] = points[p];
                    }
                }
            }
            // Plane that has 0 vertices
            if (planeId == 661424963585U)
            {
                if (CurrentTime == 7088126434811U)
                {
                    var planePose = new XrPosef();
                    planePose.Position = new XrVector3f(0.236017F, 2.533558F, 1.548240F);
                    planePose.Orientation = new XrQuaternionf(-0.556027F, 0.005222F, -0.831147F, 0.001189F);
                    XrVector2f[] points = {};
                    plane->type = PlanesNativeTypes.XrStructureType.XR_TYPE_TRACKABLE_PLANE_ANDROID;
                    plane->next = null;
                    plane->trackingState = PlanesNativeTypes.XrTrackingStateANDROID.XR_TRACKING_STATE_TRACKING_ANDROID;
                    plane->centerPose = planePose;
                    plane->extents = new XrExtent2Df(3.433020F, 3.150680F);
                    plane->planeType = PlanesNativeTypes.XrPlaneTypeANDROID.XR_PLANE_TYPE_HORIZONTAL_DOWNWARD_FACING_ANDROID;
                    plane->planeLabel = PlanesNativeTypes.XrPlaneLabelANDROID.XR_PLANE_LABEL_UNKNOWN_ANDROID;
                    plane->subsumedByPlane = 1U;
                    plane->lastUpdatedTime = 3999536090U;
                    *(plane->vertexCountOutput) = (uint)points.Length;
                }
            }
            if (planeId == 25769803777U) // Used in Two Planes
            {
                if (CurrentTime == 7088126434811U)
                {
                    var planePose = new XrPosef();
                    planePose.Position = new XrVector3f(-1.224582F, 1.826998F, 1.002754F);
                    planePose.Orientation = new XrQuaternionf(0.199364F, -0.106233F, -0.681251F, 0.696323F);
                    XrVector2f[] points =
                    {
                        new(-0.762518F, 1.832155F), // 1
                        new(0.500113F, 1.519057F), // 2
                        new(0.797556F, 1.051117F), // 3
                        new(0.786389F, -1.032876F), // 4
                        new(0.555857F, -1.581969F), // 5
                        new(0.544565F, -1.600392F), // 6
                        new(0.341389F, -1.656155F), // 7
                        new(0.336547F, -1.657309F), // 8
                        new(0.175344F, -1.695285F), // 9
                        new(-0.374733F, -1.818800F), // 10
                        new(-0.449233F, -1.832155F), // 11
                        new(-0.464513F, -1.818411F), // 12
                        new(-0.471124F, -1.812317F), // 13
                        new(-0.535297F, -1.623261F), // 14
                        new(-0.645676F, -1.063636F), // 15
                        new(-0.703946F, -0.724609F), // 16
                        new(-0.706189F, -0.710497F), // 17
                        new(-0.797556F, 0.875951F), // 18
                        new(-0.797556F, 1.223339F), // 19
                        new(-0.789830F, 1.513467F) // 20
                    };
                    plane->type = PlanesNativeTypes.XrStructureType.XR_TYPE_TRACKABLE_PLANE_ANDROID;
                    plane->next = null;
                    plane->trackingState = PlanesNativeTypes.XrTrackingStateANDROID.XR_TRACKING_STATE_TRACKING_ANDROID;
                    plane->centerPose = planePose;
                    plane->extents = new XrExtent2Df(1.595112F, 3.664310F);
                    plane->planeType = PlanesNativeTypes.XrPlaneTypeANDROID.XR_PLANE_TYPE_VERTICAL_ANDROID;
                    plane->planeLabel = PlanesNativeTypes.XrPlaneLabelANDROID.XR_PLANE_LABEL_WALL_ANDROID;
                    plane->subsumedByPlane = 0U;
                    plane->lastUpdatedTime = 1253657829U;
                    *(plane->vertexCountOutput) = (uint)points.Length;
                    for (uint p = 0; p < *(plane->vertexCountOutput); p++)
                    {
                        plane->vertices[p] = points[p];
                    }
                }
            }
            if (planeId == 3478923509761U) // Used in Added/Updated/Removed for Added
            {
                if (CurrentTime == 7088140323811U)
                {
                    var planePose = new XrPosef();
                    planePose.Position = new XrVector3f(-0.217916F, 1.101805F, 0.234703F);
                    planePose.Orientation = new XrQuaternionf(0.005009F, 0.004032F, -0.001896F, 0.999978F);
                    XrVector2f[] points =
                    {
                        new(-0.580673F, 0.241279F), // 1
                        new(-0.319212F, 0.348721F), // 2
                        new(-0.081772F, 0.368967F), // 3
                        new(-0.001394F, 0.359472F), // 4
                        new(0.424329F, 0.273498F), // 5
                        new(0.424502F, 0.273441F), // 6
                        new(0.498435F, 0.230969F), // 7
                        new(0.580673F, 0.068462F), // 8
                        new(0.570155F, -0.156726F), // 9
                        new(0.512433F, -0.333680F), // 10
                        new(0.174797F, -0.368967F), // 11
                        new(-0.406196F, -0.368967F), // 12
                        new(-0.444917F, -0.283550F), // 13
                        new(-0.516077F, -0.119362F), // 14
                    };
                    plane->type = PlanesNativeTypes.XrStructureType.XR_TYPE_TRACKABLE_PLANE_ANDROID;
                    plane->next = null;
                    plane->trackingState = PlanesNativeTypes.XrTrackingStateANDROID.XR_TRACKING_STATE_TRACKING_ANDROID;
                    plane->centerPose = planePose;
                    plane->extents = new XrExtent2Df(1.161346F, 0.737934F);
                    plane->planeType = PlanesNativeTypes.XrPlaneTypeANDROID.XR_PLANE_TYPE_HORIZONTAL_UPWARD_FACING_ANDROID;
                    plane->planeLabel = PlanesNativeTypes.XrPlaneLabelANDROID.XR_PLANE_LABEL_TABLE_ANDROID;
                    plane->subsumedByPlane = 0U;
                    plane->lastUpdatedTime = 1353693611U;
                    *(plane->vertexCountOutput) = (uint)points.Length;
                    for (uint p = 0; p < *(plane->vertexCountOutput); p++)
                    {
                        plane->vertices[p] = points[p];
                    }
                }
            }
            if (planeId == 3904125272065U) // Used in Added/Updated/Removed for Added
            {
                if (CurrentTime == 7088140323811U)
                {
                    var planePose = new XrPosef();
                    planePose.Position = new XrVector3f(-0.615766F, 1.224761F, 0.386192F);
                    planePose.Orientation = new XrQuaternionf(0.004501F, -0.205594F, -0.002903F, 0.978623F);
                    XrVector2f[] points =
                    {
                        new(-0.229609F, 0.378904F), // 1
                        new(-0.147964F, 0.295251F), // 2
                        new(0.229425F, -0.171880F), // 3
                        new(0.229869F, -0.378904F), // 4
                        new(-0.229869F, -0.330894F), // 5
                        new(-0.229869F, 0.256764F), // 6
                    };
                    plane->type = PlanesNativeTypes.XrStructureType.XR_TYPE_TRACKABLE_PLANE_ANDROID;
                    plane->next = null;
                    plane->trackingState = PlanesNativeTypes.XrTrackingStateANDROID.XR_TRACKING_STATE_TRACKING_ANDROID;
                    plane->centerPose = planePose;
                    plane->extents = new XrExtent2Df(0.459739F, 0.757809F);
                    plane->planeType = PlanesNativeTypes.XrPlaneTypeANDROID.XR_PLANE_TYPE_HORIZONTAL_UPWARD_FACING_ANDROID;
                    plane->planeLabel = PlanesNativeTypes.XrPlaneLabelANDROID.XR_PLANE_LABEL_TABLE_ANDROID;
                    plane->subsumedByPlane = 0U;
                    plane->lastUpdatedTime = 1353693611U;
                    *(plane->vertexCountOutput) = (uint)points.Length;
                    for (uint p = 0; p < *(plane->vertexCountOutput); p++)
                    {
                        plane->vertices[p] = points[p];
                    }
                }
            }
        }

        internal static unsafe uint GetVertexCount(
            XrTrackableANDROID planeId)
        {
            if (planeId == 644245094401U) // Used in Two Planes and Added/Updated/Removed
            {
                if (CurrentTime == 7088126434811U) // Two Planes
                {
                    return 15U;
                }
                if (CurrentTime == 7088140323811U) // Added/Updated/Removed for Updated
                {
                    return 15U;
                }
            }
            // Plane that has 0 vertices
            if (planeId == 661424963585U)
            {
                if (CurrentTime == 7088126434811U)
                {
                    return 0U;
                }
            }
            if (planeId == 25769803777U) // Used in Two Planes
            {
                if (CurrentTime == 7088126434811U)
                {
                    return 20U;
                }
            }
            if (planeId == 3478923509761U) // Used in Added/Updated/Removed for Added
            {
                if (CurrentTime == 7088140323811U)
                {
                    return 14U;
                }
            }
            if (planeId == 3904125272065U) // Used in Added/Updated/Removed for Added
            {
                if (CurrentTime == 7088140323811U)
                {
                    return 6U;
                }
            }
            return 0U;
        }
    }
}
