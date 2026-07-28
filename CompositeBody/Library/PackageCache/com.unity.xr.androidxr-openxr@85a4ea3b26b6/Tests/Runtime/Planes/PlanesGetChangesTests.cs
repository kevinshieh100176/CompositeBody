#if ENABLE_MOCK_RUNTIME_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;

using UnityEngine.XR.OpenXR.Features.Android;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Planes
{
    /// <summary>
    /// Tests for GetChanges for planes
    /// </summary>
    internal class PlanesGetChangesTests : PlaneTestBase
    {
        /// <summary>
        /// Checks to see that GetChanges returns correctly with no new changes
        /// </summary>
        [Test]
        public void GetChanges_NoChanges()
        {
            PlaneMockCallbacks.CurrentTime = 7088126434811U;
            m_PlaneSubsystem.GetChanges(Allocator.Temp);

            var planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);
            Assert.IsTrue(planeChanges.added.Length == 0);
            Assert.IsTrue(planeChanges.updated.Length == 0);
            Assert.IsTrue(planeChanges.removed.Length == 0);
        }

        /// <summary>
        /// Checks to see that GetChanges returns correctly with new planes added
        /// </summary>
        [Test]
        public void GetChanges_NewPlanesAdded()
        {
            PlaneMockCallbacks.CurrentTime = 7088126434811U;
            m_PlaneSubsystem.GetChanges(Allocator.Temp);

            // Change function to get data to have a plane in it
            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables_TwoPlanes);
            var planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);
            Assert.IsTrue(planeChanges.added.Length == 2);
            Assert.IsTrue(planeChanges.updated.Length == 0);
            Assert.IsTrue(planeChanges.removed.Length == 0);
        }

        /// <summary>
        /// Checks to see that GetChanges returns correctly with new planes added, planes updated, and planes removed
        /// in the same frame.
        /// </summary>
        [Test]
        public void GetChanges_PlanesAddedUpdatedAndRemoved()
        {
            PlaneMockCallbacks.CurrentTime = 7088126434811U;
            m_PlaneSubsystem.GetChanges(Allocator.Temp);

            // Change function to get data to have a plane in it
            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables_TwoPlanes);
            var planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);

            PlaneMockCallbacks.CurrentTime = 7088140323811U;
            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables_AddUpdateRemovePlanes);
            planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);
            Assert.IsTrue(planeChanges.added.Length == 2);
            Assert.IsTrue(planeChanges.updated.Length == 1);
            Assert.IsTrue(planeChanges.removed.Length == 1);
        }

        /// <summary>
        /// Checks to see that GetChanges returns correctly with planes but there are no changes this frame.
        /// </summary>
        [Test]
        public void GetChanges_PlanesExistButNoChanges()
        {
            PlaneMockCallbacks.CurrentTime = 7088126434811U;
            m_PlaneSubsystem.GetChanges(Allocator.Temp);

            // Change function to get data to have a plane in it
            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables_TwoPlanes);
            var planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);

            // Call GetChanges again without changing xrGetAllTrackablesANDROID so we see the same planes
            planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);

            Assert.IsTrue(planeChanges.added.Length == 0);
            Assert.IsTrue(planeChanges.updated.Length == 0);
            Assert.IsTrue(planeChanges.removed.Length == 0);
        }

        /// <summary>
        /// Checks to see that GetChanges returns correctly when all planes are removed.
        /// </summary>
        [Test]
        public void GetChanges_AllPlanesRemoved()
        {
            PlaneMockCallbacks.CurrentTime = 7088126434811U;
            m_PlaneSubsystem.GetChanges(Allocator.Temp);

            // Change function to get data to have a plane in it
            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables_TwoPlanes);
            var planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);

            // Call GetChanges again with no planes so they are all removed
            XrFuncTableUtils.ClearCachedFunc(TestConstants.k_XrGetAllTrackablesAndroidFuncName);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables);
            planeChanges = m_PlaneSubsystem.GetChanges(Allocator.Temp);
            Assert.IsTrue(planeChanges.added.Length == 0);
            Assert.IsTrue(planeChanges.updated.Length == 0);
            Assert.IsTrue(planeChanges.removed.Length == 2);
        }
    }
}
#endif
