#if ENABLE_MOCK_RUNTIME_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.XR.OpenXR.NativeTypes;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Planes
{
    using XrTrackableANDROID = System.UInt64;

    /// <summary>
    /// Tests for GetBoundary for planes
    /// </summary>
    internal class PlanesGetBoundaryTests : PlaneTestBase
    {
        /// <summary>
        /// Checks to see that GetBoundary returns correctly on first plane
        /// </summary>
        [Test]
        public void GetBoundary_BoundaryOfFirstPlane()
        {
            PlaneMockCallbacks.CurrentTime = 7088126434811U;
            m_PlaneSubsystem.GetChanges(Allocator.Temp);

            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables_TwoPlanes);
            var planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);
            var planeID = planeChanges.added[0]; // first plane
            NativeArray<Vector2> boundary = new NativeArray<Vector2>();

            m_PlaneSubsystem.GetBoundary(planeID.trackableId, Allocator.TempJob, ref boundary);
            // Vertices have winding order and handedness changed
            Assert.IsTrue(boundary[14].x == -0.871075F && boundary[14].y == -0.452861F);
            Assert.IsTrue(boundary[13].x == 0.676295F && boundary[13].y == -1.443324F);
            Assert.IsTrue(boundary[12].x == 0.767670F && boundary[12].y == -1.435223F);
            Assert.IsTrue(boundary[11].x == 0.845309F && boundary[11].y == -1.364514F);
            Assert.IsTrue(boundary[10].x == 0.864124F && boundary[10].y == -1.115645F);
            Assert.IsTrue(boundary[9].x == 0.871075F && boundary[9].y == -0.737151F);
            Assert.IsTrue(boundary[8].x == 0.871075F && boundary[8].y == -0.225101F);
            Assert.IsTrue(boundary[7].x == 0.869446F && boundary[7].y == -0.017170F);
            Assert.IsTrue(boundary[6].x == 0.853406F && boundary[6].y == 0.727843F);
            Assert.IsTrue(boundary[5].x == 0.848436F && boundary[5].y == 0.832505F);
            Assert.IsTrue(boundary[4].x == 0.829442F && boundary[4].y == 1.228444F);
            Assert.IsTrue(boundary[3].x == 0.808567F && boundary[3].y == 1.398244F);
            Assert.IsTrue(boundary[2].x == 0.789651F && boundary[2].y == 1.443324F);
            Assert.IsTrue(boundary[1].x == -0.753807F && boundary[1].y == 1.225954F);
            Assert.IsTrue(boundary[0].x == -0.796433F && boundary[0].y == 1.178573F);
        }

        /// <summary>
        /// Checks to see that GetBoundary returns correctly on last plane
        /// </summary>
        [Test]
        public void GetBoundary_BoundaryOfLastPlane()
        {
            PlaneMockCallbacks.CurrentTime = 7088126434811U;
            m_PlaneSubsystem.GetChanges(Allocator.Temp);

            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables_TwoPlanes);
            var planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);
            var plane = planeChanges.added[1]; // second plane (of two, so last)
            NativeArray<Vector2> boundary = new NativeArray<Vector2>();

            m_PlaneSubsystem.GetBoundary(plane.trackableId, Allocator.TempJob, ref boundary);
            // Vertices have winding order and handedness changed
            Assert.IsTrue(boundary[19].x == -0.762518F && boundary[19].y == -1.832155F);
            Assert.IsTrue(boundary[18].x == 0.500113F && boundary[18].y == -1.519057F);
            Assert.IsTrue(boundary[17].x == 0.797556F && boundary[17].y == -1.051117F);
            Assert.IsTrue(boundary[16].x == 0.786389F && boundary[16].y == 1.032876F);
            Assert.IsTrue(boundary[15].x == 0.555857F && boundary[15].y == 1.581969F);
            Assert.IsTrue(boundary[14].x == 0.544565F && boundary[14].y == 1.600392F);
            Assert.IsTrue(boundary[13].x == 0.341389F && boundary[13].y == 1.656155F);
            Assert.IsTrue(boundary[12].x == 0.336547F && boundary[12].y == 1.657309F);
            Assert.IsTrue(boundary[11].x == 0.175344F && boundary[11].y == 1.695285F);
            Assert.IsTrue(boundary[10].x == -0.374733F&& boundary[10].y ==  1.818800F);
            Assert.IsTrue(boundary[9].x == -0.449233F && boundary[9].y == 1.832155F);
            Assert.IsTrue(boundary[8].x == -0.464513F && boundary[8].y == 1.818411F);
            Assert.IsTrue(boundary[7].x == -0.471124F && boundary[7].y == 1.812317F);
            Assert.IsTrue(boundary[6].x == -0.535297F && boundary[6].y == 1.623261F);
            Assert.IsTrue(boundary[5].x == -0.645676F && boundary[5].y == 1.063636F);
            Assert.IsTrue(boundary[4].x == -0.703946F && boundary[4].y == 0.724609F);
            Assert.IsTrue(boundary[3].x == -0.706189F && boundary[3].y == 0.710497F);
            Assert.IsTrue(boundary[2].x == -0.797556F && boundary[2].y == -0.875951F);
            Assert.IsTrue(boundary[1].x == -0.797556F && boundary[1].y == -1.223339F);
            Assert.IsTrue(boundary[0].x == -0.789830F && boundary[0].y == -1.513467F);
        }

        /// <summary>
        /// Checks to see that GetBoundary returns correctly when given incorrect ID.
        /// It should return a boundary of size 0.
        /// </summary>
        [Test]
        public void GetBoundary_NotCorrectId()
        {
            PlaneMockCallbacks.CurrentTime = 7088126434811U;
            m_PlaneSubsystem.GetChanges(Allocator.Temp);

            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables_TwoPlanes);
            m_PlaneSubsystem.GetChanges(Allocator.Temp);
            NativeArray<Vector2> boundary = new NativeArray<Vector2>();
            TrackableId planeID = new TrackableId (0U,3904125272065U); // This is the plane from added/updated/removed, not valid here
            m_PlaneSubsystem.GetBoundary(planeID, Allocator.TempJob, ref boundary);
            Assert.IsTrue(boundary.Length == 0);
        }

        /// <summary>
        /// Checks to see that GetBoundary returns correctly when the boundary length is 0.
        /// </summary>
        [Test]
        public void GetBoundary_BoundaryOfSize0()
        {
            PlaneMockCallbacks.CurrentTime = 7088126434811U;
            m_PlaneSubsystem.GetChanges(Allocator.Temp);

            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables_ZeroVertices);
            m_PlaneSubsystem.GetChanges(Allocator.Temp);
            NativeArray<Vector2> boundary = new NativeArray<Vector2>();
            TrackableId planeID = new TrackableId(0U, 661424963585U);

            m_PlaneSubsystem.GetBoundary(planeID, Allocator.TempJob, ref boundary);
            Assert.IsTrue(boundary.Length == 0);
        }
    }
}
#endif
