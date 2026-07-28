#if UNITY_EDITOR && UNITY_ANDROID
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Occlusion
{
    /// <summary>
    /// Tests for frame retrieval, shader keywords, API support, and Try* methods in the Android Environment Occlusion subsystem.
    /// </summary>
    public class EnvironmentOcclusionFrameAndAPIBehaviorTests : EnvironmentOcclusionTestBase
    {
        /// <summary>
        /// Checks that the enabled shader keywords include "XR_LINEAR_DEPTH".
        /// </summary>
        [Test]
        public void GetShaderKeywords2_WhenCalled_ContainsLinearDepthKeywordEnabled()
        {
            var enabledKeywords = new List<string>(m_occlusionSubsystem.GetShaderKeywords2().enabledKeywords);
            Assert.IsNotNull(m_occlusionSubsystem.GetShaderKeywords2().enabledKeywords);
            Assert.Contains("XR_LINEAR_DEPTH", enabledKeywords);
        }

        /// <summary>
        /// Verifies that there are no disabled shader keywords.
        /// </summary>
        [Test]
        public void GetShaderKeywords2_WhenCalled_DoesNotContainDisabledKeywords()
        {
            Assert.IsNull(m_occlusionSubsystem.GetShaderKeywords2().disabledKeywords);
        }

        /// <summary>
        /// Confirms that TryGetFrame returns true, indicating a frame is available.
        /// </summary>
        [Test]
        public void TryGetFrame_WhenCalled_ReturnsTrue()
        {
            var gotFrame = m_occlusionSubsystem.TryGetFrame(Allocator.Temp, out _);
            Assert.IsTrue(gotFrame);
        }

        /// <summary>
        /// Checks that FOVs and poses arrays returned from a frame each have a length of two.
        /// </summary>
        [Test]
        public void TryGetFrame_WhenCalled_FovsAndPosesArraysHaveLengthTwo()
        {
            var gotFrame = m_occlusionSubsystem.TryGetFrame(Allocator.Temp, out var frame);
            Assert.IsTrue(gotFrame);

            var gotFovs = frame.TryGetFovs(out var fovs);
            var gotPoses = frame.TryGetPoses(out var poses);
            var gotNearFarPlanes = frame.TryGetNearFarPlanes(out _);

            Assert.IsTrue(gotFovs);
            Assert.IsTrue(gotPoses);
            Assert.IsTrue(gotNearFarPlanes);
            Assert.AreEqual(fovs.Length, 2);
            Assert.AreEqual(poses.Length, 2);
        }

        /// <summary>
        /// Verifies that TryGetFrame returns false when the subsystem is stopped.
        /// </summary>
        [Test]
        public void TryGetFrame_WhenStopped_ReturnsFalse()
        {
            m_occlusionSubsystem.Stop();
            Assert.IsFalse(m_occlusionSubsystem.TryGetFrame(Allocator.Temp, out _));
        }

        /// <summary>
        /// Verifies that TryGetFrame returns true while running and false after stop.
        /// </summary>
        [Test]
        public void TryGetFrame_WhileRunningAndAfterStop_BehavesConsistently()
        {
            Assert.IsTrue(m_occlusionSubsystem.running);
            Assert.IsTrue(m_occlusionSubsystem.TryGetFrame(Allocator.Temp, out _));

            m_occlusionSubsystem.Stop();
            Assert.IsFalse(m_occlusionSubsystem.TryGetFrame(Allocator.Temp, out _));
        }

        /// <summary>
        /// Verifies that TryGetFrame after subsystem is disposed does not throw and returns false.
        /// </summary>
        [Test]
        public void TryGetFrame_AfterDispose_ReturnsFalse()
        {
            m_mockXrEnv.Dispose();
            m_occlusionSubsystem.Destroy();
            Assert.IsFalse(m_occlusionSubsystem.TryGetFrame(Allocator.Temp, out _));
        }

        /// <summary>
        /// Asserts that requesting environment depth throws NotSupportedException.
        /// </summary>
        [Test]
        public void TryGetEnvironmentDepth_WhenCalled_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() => m_occlusionSubsystem.TryGetEnvironmentDepth(out _));
        }

        /// <summary>
        /// Confirms that acquiring the environment depth confidence CPU image is not supported and returns false.
        /// </summary>
        [Test]
        public void TryAcquireEnvironmentDepthConfidenceCpuImage_WhenCalled_ReturnsFalse()
        {
            Assert.IsFalse(m_occlusionSubsystem.TryAcquireEnvironmentDepthConfidenceCpuImage(out _));
        }

        /// <summary>
        /// Confirms that acquiring the environment depth CPU image is not supported and returns false.
        /// </summary>
        [Test]
        public void TryAcquireEnvironmentDepthCpuImage_WhenCalled_ReturnsFalse()
        {
            Assert.IsFalse(m_occlusionSubsystem.TryAcquireEnvironmentDepthCpuImage(out _));
        }

        /// <summary>
        /// Confirms that acquiring the human depth CPU image is not supported and returns false.
        /// </summary>
        [Test]
        public void TryAcquireHumanDepthCpuImage_WhenCalled_ReturnsFalse()
        {
            Assert.IsFalse(m_occlusionSubsystem.TryAcquireHumanDepthCpuImage(out _));
        }

        /// <summary>
        /// Confirms that acquiring the human stencil CPU image is not supported and returns false.
        /// </summary>
        [Test]
        public void TryAcquireHumanStencilCpuImage_WhenCalled_ReturnsFalse()
        {
            Assert.IsFalse(m_occlusionSubsystem.TryAcquireHumanStencilCpuImage(out _));
        }

        /// <summary>
        /// Confirms that acquiring the raw environment depth CPU image is not supported and returns false.
        /// </summary>
        [Test]
        public void TryAcquireRawEnvironmentDepthCpuImage_WhenCalled_ReturnsFalse()
        {
            Assert.IsFalse(m_occlusionSubsystem.TryAcquireRawEnvironmentDepthCpuImage(out _));
        }

        /// <summary>
        /// Confirms that acquiring the smoothed environment depth CPU image is not supported and returns false.
        /// </summary>
        [Test]
        public void TryAcquireSmoothedEnvironmentDepthCpuImage_WhenCalled_ReturnsFalse()
        {
            Assert.IsFalse(m_occlusionSubsystem.TryAcquireSmoothedEnvironmentDepthCpuImage(out _));
        }

        /// <summary>
        /// Asserts that requesting human depth throws NotSupportedException.
        /// </summary>
        [Test]
        public void TryGetHumanDepth_WhenCalled_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() => m_occlusionSubsystem.TryGetHumanDepth(out _));
        }

        /// <summary>
        /// Asserts that requesting human stencil throws NotSupportedException.
        /// </summary>
        [Test]
        public void TryGetHumanStencil_WhenCalled_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() => m_occlusionSubsystem.TryGetHumanStencil(out _));
        }

        /// <summary>
        /// Confirms that getting swapchain texture descriptors is not supported and returns false.
        /// </summary>
        [Test]
        public void TryGetSwapchainTextureDescriptors_WhenCalled_ReturnsFalse()
        {
            Assert.IsFalse(m_occlusionSubsystem.TryGetSwapchainTextureDescriptors(out _));
        }
    }
}
#endif
