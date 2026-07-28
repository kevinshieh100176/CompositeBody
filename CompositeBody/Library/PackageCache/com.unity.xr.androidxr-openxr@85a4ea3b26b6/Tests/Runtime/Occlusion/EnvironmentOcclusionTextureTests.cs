#if UNITY_EDITOR && UNITY_ANDROID
using System;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Occlusion
{
    /// <summary>
    /// Tests related to texture descriptors, their properties, and resource management for the Android Environment Occlusion feature.
    /// </summary>
    public class EnvironmentOcclusionTextureTests : EnvironmentOcclusionTestBase
    {
        /// <summary>
        /// Verifies that exactly two texture descriptors are returned.
        /// </summary>
        [Test]
        public void GetTextureDescriptors_WhenCalled_ReturnsTwoDescriptors()
        {
            var descriptors = m_occlusionSubsystem.GetTextureDescriptors(Allocator.Temp);
            Assert.IsNotNull(descriptors);
            Assert.AreEqual(descriptors.Length, 2);
        }

        /// <summary>
        /// Ensures that the depth texture descriptor has correct properties and is valid.
        /// </summary>
        [Test]
        public void GetTextureDescriptors_WhenCalled_DepthTextureDescriptorIsValid()
        {
            var descriptors = m_occlusionSubsystem.GetTextureDescriptors(Allocator.Temp);
            var depthDesc = descriptors[0];
            Assert.AreEqual(depthDesc.width, MockCallbacks.DepthWidth);
            Assert.AreEqual(depthDesc.height, MockCallbacks.DepthHeight);
            Assert.AreEqual(depthDesc.depth, 2);
            Assert.AreEqual(depthDesc.propertyNameId, Shader.PropertyToID("_EnvironmentDepthTexture"));
            Assert.IsTrue(depthDesc.valid);
            Assert.AreEqual(depthDesc.textureType, XRTextureType.ColorRenderTextureRef);
            Assert.AreEqual(depthDesc.mipmapCount, 0);
            Assert.AreEqual(depthDesc.format, TextureFormat.RFloat);
        }

        /// <summary>
        /// Ensures that the depth confidence texture descriptor has correct properties and is valid.
        /// </summary>
        [Test]
        public void GetTextureDescriptors_WhenCalled_DepthConfidenceTextureDescriptorIsValid()
        {
            var descriptors = m_occlusionSubsystem.GetTextureDescriptors(Allocator.Temp);
            var depthDesc = descriptors[1];
            Assert.AreEqual(depthDesc.width, MockCallbacks.DepthWidth);
            Assert.AreEqual(depthDesc.height, MockCallbacks.DepthHeight);
            Assert.AreEqual(depthDesc.depth, 2);
            Assert.AreEqual(depthDesc.propertyNameId, Shader.PropertyToID("_EnvironmentConfidenceTexture"));
            Assert.IsTrue(depthDesc.valid);
            Assert.AreEqual(depthDesc.textureType, XRTextureType.ColorRenderTextureRef);
            Assert.AreEqual(depthDesc.mipmapCount, 0);
            Assert.AreEqual(depthDesc.format, TextureFormat.R8);
        }

        /// <summary>
        /// Verifies that disposing a NativeArray returned from GetTextureDescriptors twice throws ObjectDisposedException.
        /// </summary>
        [Test]
        public void GetTextureDescriptors_WhenDisposedTwice_ThrowsObjectDisposedException()
        {
            var descriptors = m_occlusionSubsystem.GetTextureDescriptors(Allocator.Temp);
            descriptors.Dispose();
            Assert.Throws<ObjectDisposedException>(() => descriptors.Dispose());
        }
    }
}
#endif
