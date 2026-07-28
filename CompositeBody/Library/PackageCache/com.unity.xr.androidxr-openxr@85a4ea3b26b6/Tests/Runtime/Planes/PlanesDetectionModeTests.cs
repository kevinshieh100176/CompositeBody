#if ENABLE_MOCK_RUNTIME_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Planes
{
    /// <summary>
    /// Tests for PlaneDetectionMode for planes
    /// </summary>
    internal class PlanesDetectionModeTests : PlaneTestBase
    {
        /// <summary>
        /// Checks to see that PlaneDetectionMode currentPlaneDetectionMode returns correctly
        /// </summary>
        [Test]
        public void DetectionMode_GetCurrent()
        {
            m_PlaneSubsystem.GetChanges(Allocator.Temp);
            PlaneDetectionMode currentMode = m_PlaneSubsystem.currentPlaneDetectionMode;
            Assert.IsTrue(currentMode == (PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical));
        }

        /// <summary>
        /// Checks to see that PlaneDetectionMode requestedPlaneDetectionMode returns correctly
        /// </summary>
        [Test]
        public void DetectionMode_GetRequested()
        {
            m_PlaneSubsystem.GetChanges(Allocator.Temp);
            PlaneDetectionMode requestedMode = m_PlaneSubsystem.requestedPlaneDetectionMode;
            Assert.IsTrue(requestedMode == (PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical));
        }

        /// <summary>
        /// Checks to see that requested PlaneDetectionMode updates when it is set
        /// </summary>
        [Test]
        public void DetectionMode_GetRequestedNewMode()
        {
            m_PlaneSubsystem.GetChanges(Allocator.Temp);
            m_PlaneSubsystem.requestedPlaneDetectionMode = PlaneDetectionMode.Horizontal;
            PlaneDetectionMode requestedMode = m_PlaneSubsystem.requestedPlaneDetectionMode;
            Assert.IsTrue(requestedMode == PlaneDetectionMode.Horizontal);
        }

        /// <summary>
        /// Checks to see that current PlaneDetectionMode updates when it is set via requested mode
        /// </summary>
        [Test]
        public void DetectionMode_GetCurrentNewMode()
        {
            m_PlaneSubsystem.GetChanges(Allocator.Temp);
            m_PlaneSubsystem.requestedPlaneDetectionMode = PlaneDetectionMode.Vertical;
            PlaneDetectionMode currentMode = m_PlaneSubsystem.currentPlaneDetectionMode;
            Assert.IsTrue(currentMode == PlaneDetectionMode.Vertical);
        }

        /// <summary>
        /// Checks to see that PlaneDetectionMode filters out planes
        /// </summary>
        [Test]
        public void DetectionMode_Filtration()
        {
            PlaneMockCallbacks.CurrentTime = 7088140323811U;
            m_PlaneSubsystem.GetChanges(Allocator.Temp);
            m_PlaneSubsystem.requestedPlaneDetectionMode = PlaneDetectionMode.Horizontal;
            // AddUpdateRemovePlanes has two horizontal and one vertical plane.
            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables_AddUpdateRemovePlanes);
            var planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);
            Assert.IsTrue(planeChanges.added.Length == 2);
        }
    }
}
#endif
