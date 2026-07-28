using NUnit.Framework;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR.TestTooling;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Planes
{
    /// <summary>
    /// Base class for Android XR Environment Planes subsystem tests.
    /// Provides common setup and teardown for mock planes subsystem.
    /// </summary>
    abstract class PlaneTestBase
    {
        protected MockOpenXREnvironment m_MockEnvironment;
        protected XRPlaneSubsystem m_PlaneSubsystem;

        /// <summary>
        /// Common test setup: initializes the mock OpenXR environment and planes subsystem.
        /// </summary>
        [SetUp]
        public virtual void SetUp()
        {
            m_MockEnvironment = MockOpenXREnvironment.CreateEnvironment();
            m_MockEnvironment.Settings.EnableFeature<ARPlaneFeature>(true);
            m_MockEnvironment.Settings.RequestUseExtension(TestConstants.k_XrAndroidTrackablesExtName);
            m_MockEnvironment.AddSupportedExtension(TestConstants.k_XrAndroidTrackablesExtName, 1);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrCreateTrackableTrackerAndroidFuncName, PlaneMockCallbacks.CreateTrackableTracker);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrDestroyTrackableTrackerAndroidFuncName, PlaneMockCallbacks.DestroyTrackableTracker);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetAllTrackablesAndroidFuncName, PlaneMockCallbacks.GetAllTrackables);
            m_MockEnvironment.SetFunctionForInterceptor(TestConstants.k_XrGetTrackablePlaneAndroidFuncName, PlaneMockCallbacks.GetTrackablePlane);
            m_MockEnvironment.Start();

            var loader = XRGeneralSettings.Instance.Manager.activeLoader;
            m_PlaneSubsystem = loader.GetLoadedSubsystem<XRPlaneSubsystem>();
            m_PlaneSubsystem.Start();
        }

        /// <summary>
        /// Common test teardown: disposes the mock environment and destroys the planes subsystem.
        /// </summary>
        [TearDown]
        public virtual void TearDown()
        {
            m_MockEnvironment.Dispose();
            m_PlaneSubsystem.Destroy();
        }
    }
}
