using UnityEngine.XR.OpenXR.NativeTypes;
using XrSession = System.UInt64;

namespace UnityEngine.XR.OpenXR.Features.Android
{
    /// <summary>
    /// Dimming level changed event. The runtime must queue this event upon a successful call to the
    /// xrBeginSession function, so that the application can be in sync on the state when a session
    /// begins running.
    /// </summary>
    public readonly unsafe struct XrEventDataGlobalDimmingLevelChangedANDROID
    {
        /// <summary>
        /// The `XrStructureType` of this struct.
        /// </summary>
        public XrStructureType type { get; }

        /// <summary>
        /// `null` or a pointer to the next structure in a structure chain.
        /// </summary>
        public void* next { get; }

        /// <summary>
        /// Handle of the xrSession
        /// </summary>
        public XrSession session { get; }
    }
}