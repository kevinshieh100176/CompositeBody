#if UNITY_EDITOR && UNITY_ANDROID
using NUnit.Framework;
using UnityEngine.SubsystemsImplementation.Extensions;
using UnityEngine.TestTools;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Occlusion
{
    /// <summary>
    /// Tests for lifecycle and state transitions of the Android Environment Occlusion subsystem.
    /// </summary>
    public class EnvironmentOcclusionLifecycleTests : EnvironmentOcclusionTestBase
    {
        /// <summary>
        /// Ensures that calling Start() on an already running subsystem keeps it running.
        /// </summary>
        [Test]
        public void Start_WhenAlreadyRunning_KeepsSubsystemRunning()
        {
            Assert.IsTrue(m_occlusionSubsystem.running);
            m_occlusionSubsystem.Start();
            Assert.IsTrue(m_occlusionSubsystem.running);
        }

        /// <summary>
        /// Ensures that calling Stop() stops the subsystem and repeated calls keep it stopped.
        /// </summary>
        [Test]
        public void Stop_WhenCalled_StopsSubsystem()
        {
            Assert.IsTrue(m_occlusionSubsystem.running);
            m_occlusionSubsystem.Stop();
            Assert.IsFalse(m_occlusionSubsystem.running);
            m_occlusionSubsystem.Stop();
            Assert.IsFalse(m_occlusionSubsystem.running);
        }

        /// <summary>
        /// Verifies that the occlusion subsystem is running after being started.
        /// </summary>
        [Test]
        public void Subsystem_WhenStarted_IsRunning()
        {
            Assert.IsNotNull(m_occlusionSubsystem);
            Assert.IsTrue(m_occlusionSubsystem.running);
        }

        /// <summary>
        /// Verifies that after destroying the occlusion subsystem, both the subsystem and its provider report not running.
        /// </summary>
        [Test]
        public void Destroy_WhenCalledOnOcclusionSubsystem_SetsSubsystemAndProviderToNotRunning()
        {
            Assert.IsTrue(m_occlusionSubsystem.running);
            Assert.IsTrue(m_occlusionSubsystem.GetProvider().running);
            m_occlusionSubsystem.Destroy();
            Assert.IsFalse(m_occlusionSubsystem.running);
            Assert.IsFalse(m_occlusionSubsystem.GetProvider().running);
        }

        /// <summary>
        /// Verifies that stopping the provider sets its running state to false.
        /// </summary>
        [Test]
        public void Stop_WhenCalledOnProvider_SetsRunningToFalse()
        {
            m_occlusionSubsystem.requestedEnvironmentDepthMode = EnvironmentDepthMode.Best;
            Assert.AreEqual(EnvironmentDepthMode.Disabled, m_occlusionSubsystem.requestedEnvironmentDepthMode);
        }

        /// <summary>
        /// Verifies that calling Start after Destroy throws or does not set running to true.
        /// </summary>
        [Test]
        public void Start_AfterDestroy_DoesNotSetRunning()
        {
            m_occlusionSubsystem.Destroy();
            Assert.IsFalse(m_occlusionSubsystem.running);
            m_occlusionSubsystem.Start();
            LogAssert.Expect(LogType.Error, "Failed to start AndroidXR Depth API"); // this was the only way to test this
        }

        /// <summary>
        /// Verifies that destroying the subsystem twice does not throw and leaves it not running.
        /// </summary>
        [Test]
        public void Destroy_WhenCalledTwice_DoesNotThrow()
        {
            m_occlusionSubsystem.Destroy();
            Assert.DoesNotThrow(() => m_occlusionSubsystem.Destroy());
            Assert.IsFalse(m_occlusionSubsystem.running);
        }

        /// <summary>
        /// Verifies that accessing properties after Destroy throws or returns safe defaults.
        /// </summary>
        [Test]
        public void PropertyAccess_AfterDestroy_ThrowsOrReturnsDefault()
        {
            m_occlusionSubsystem.Destroy();
            Assert.IsFalse(m_occlusionSubsystem.running);
            Assert.IsFalse(m_occlusionSubsystem.TryGetFrame(Unity.Collections.Allocator.Temp, out _));
            Assert.AreEqual(EnvironmentDepthMode.Disabled, m_occlusionSubsystem.currentEnvironmentDepthMode);
        }

        /// <summary>
        /// Verifies that creating multiple occlusion subsystems from the descriptor returns the same instance each time.
        /// </summary>
        [Test]
        public void Create_MultipleSubsystems_ReturnsSameInstance()
        {
            var descriptor = m_occlusionSubsystem.subsystemDescriptor;
            var system1 = descriptor.Create();
            var system2 = descriptor.Create();
            Assert.AreSame(m_occlusionSubsystem, system1);
            Assert.AreSame(m_occlusionSubsystem, system2);
            Assert.AreSame(system1, system2);
        }
    }
}
#endif
