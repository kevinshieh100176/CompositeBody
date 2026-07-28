#if UNITY_EDITOR && UNITY_ANDROID
using NUnit.Framework;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR.TestTooling;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Occlusion
{
    /// <summary>
    /// Base class for Android Environment Occlusion subsystem tests.
    /// Provides common setup and teardown for mock environment and occlusion subsystem.
    /// </summary>
    public abstract class EnvironmentOcclusionTestBase
    {
        internal MockOpenXREnvironment m_mockXrEnv;
        internal XROcclusionSubsystem m_occlusionSubsystem;

        /// <summary>
        /// Common test setup: initializes the mock OpenXR environment and occlusion subsystem.
        /// </summary>
        [SetUp]
        public virtual void SetUp()
        {
            m_mockXrEnv = MockOpenXREnvironment.CreateEnvironment();
            m_mockXrEnv.Settings.EnableFeature<AROcclusionFeature>(true);
            m_mockXrEnv.Settings.RequestUseExtension("XR_ANDROID_depth_texture");
            m_mockXrEnv.AddSupportedExtension("XR_ANDROID_depth_texture", 1);
            m_mockXrEnv.SetFunctionForInterceptor(@"xrEnumerateDepthResolutionsANDROID", MockCallbacks.EnumerateDepthResolutions);
            m_mockXrEnv.SetFunctionForInterceptor(@"xrCreateDepthSwapchainANDROID", MockCallbacks.CreateDepthSwapchain);
            m_mockXrEnv.SetFunctionForInterceptor(@"xrEnumerateDepthSwapchainImagesANDROID", MockCallbacks.EnumerateDepthSwapchainImages);
            m_mockXrEnv.SetFunctionForInterceptor(@"xrAcquireDepthSwapchainImagesANDROID", MockCallbacks.AcquireDepthSwapchainImages);
            m_mockXrEnv.Start();

            var loader = XRGeneralSettings.Instance.Manager.activeLoader;
            m_occlusionSubsystem = loader.GetLoadedSubsystem<XROcclusionSubsystem>();
            m_occlusionSubsystem.Start();
        }

        /// <summary>
        /// Common test teardown: disposes the mock environment and destroys the occlusion subsystem.
        /// </summary>
        [TearDown]
        public virtual void TearDown()
        {
            m_mockXrEnv.Dispose();
            m_occlusionSubsystem.Destroy();
        }
    }
}
#endif
