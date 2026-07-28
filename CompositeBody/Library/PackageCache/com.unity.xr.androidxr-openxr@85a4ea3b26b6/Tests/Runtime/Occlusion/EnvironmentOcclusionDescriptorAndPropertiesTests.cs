#if UNITY_EDITOR && UNITY_ANDROID
using System;
using NUnit.Framework;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Occlusion
{
    /// <summary>
    /// Tests for static properties, descriptor support flags, and property setters of the Android Environment Occlusion subsystem.
    /// </summary>
    public class EnvironmentOcclusionDescriptorAndPropertiesTests : EnvironmentOcclusionTestBase
    {
        /// <summary>
        /// Verifies that the feature ID for AROcclusionFeature is set to the expected value.
        /// </summary>
        [Test]
        public void FeatureId_WhenQueried_IsCorrect()
        {
            Assert.AreEqual(AROcclusionFeature.featureId, "com.unity.openxr.feature.arfoundation-androidxr-occlusion");
        }

        /// <summary>
        /// Verifies that the environment depth confidence image is reported as supported by the occlusion subsystem descriptor.
        /// </summary>
        [Test]
        public void EnvironmentDepthConfidenceImageSupported_WhenQueried_IsSupported()
        {
            Assert.AreEqual(m_occlusionSubsystem.subsystemDescriptor.environmentDepthConfidenceImageSupported, Supported.Supported);
        }

        /// <summary>
        /// Verifies that the environment depth image is reported as supported by the occlusion subsystem descriptor.
        /// </summary>
        [Test]
        public void EnvironmentDepthImageSupported_WhenQueried_IsSupported()
        {
            Assert.AreEqual(m_occlusionSubsystem.subsystemDescriptor.environmentDepthImageSupported, Supported.Supported);
        }

        /// <summary>
        /// Verifies that environment depth temporal smoothing is reported as supported by the occlusion subsystem descriptor.
        /// </summary>
        [Test]
        public void EnvironmentDepthTemporalSmoothingSupported_WhenQueried_IsSupported()
        {
            Assert.AreEqual(m_occlusionSubsystem.subsystemDescriptor.environmentDepthTemporalSmoothingSupported, Supported.Supported);
        }

        /// <summary>
        /// Verifies that human segmentation depth image is reported as unsupported by the occlusion subsystem descriptor.
        /// </summary>
        [Test]
        public void HumanSegmentationDepthImageSupported_WhenQueried_IsUnsupported()
        {
            Assert.AreEqual(m_occlusionSubsystem.subsystemDescriptor.humanSegmentationDepthImageSupported, Supported.Unsupported);
        }

        /// <summary>
        /// Verifies that human segmentation stencil image is reported as unsupported by the occlusion subsystem descriptor.
        /// </summary>
        [Test]
        public void HumanSegmentationStencilImageSupported_WhenQueried_IsUnsupported()
        {
            Assert.AreEqual(m_occlusionSubsystem.subsystemDescriptor.humanSegmentationStencilImageSupported, Supported.Unsupported);
        }

        /// <summary>
        /// Verifies that the occlusion subsystem descriptor ID is set to "Android-Occlusion".
        /// </summary>
        [Test]
        public void SubsystemDescriptorId_WhenQueried_IsCorrect()
        {
            Assert.AreEqual(m_occlusionSubsystem.subsystemDescriptor.id, "Android-Occlusion");
        }

        /// <summary>
        /// Verifies that setting the requested human depth mode to a supported value throws a NotSupportedException.
        /// </summary>
        [Test]
        public void RequestedHumanDepthMode_WhenSetToSupportedValue_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() => m_occlusionSubsystem.requestedHumanDepthMode = HumanSegmentationDepthMode.Fastest);
        }

        /// <summary>
        /// Verifies that setting the requested human stencil mode to a supported value throws a NotSupportedException.
        /// </summary>
        [Test]
        public void RequestedHumanStencilMode_WhenSetToSupportedValue_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() => m_occlusionSubsystem.requestedHumanStencilMode = HumanSegmentationStencilMode.Fastest);
        }

        /// <summary>
        /// Verifies that setting the requested occlusion preference mode updates the property as expected.
        /// </summary>
        [Test]
        public void RequestedOcclusionPreferenceMode_WhenSet_UpdatesProperty()
        {
            m_occlusionSubsystem.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            Assert.AreEqual(OcclusionPreferenceMode.PreferEnvironmentOcclusion, m_occlusionSubsystem.requestedOcclusionPreferenceMode);
        }

        /// <summary>
        /// Verifies that setting environmentDepthTemporalSmoothingRequested to true updates the property as expected.
        /// </summary>
        [Test]
        public void EnvironmentDepthTemporalSmoothingRequested_WhenSetToTrue_UpdatesProperty()
        {
            m_occlusionSubsystem.environmentDepthTemporalSmoothingRequested = true;
            Assert.IsTrue(m_occlusionSubsystem.environmentDepthTemporalSmoothingRequested);
        }
    }
}
#endif
