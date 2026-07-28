namespace Unity.Services.Vivox
{
    /// <summary>
    /// Represents the current initialization state of the Vivox SDK.
    /// Use this value to determine whether Vivox has been initialized and is ready for use.
    /// </summary>
    public enum VivoxInitializationState
    {
        /// <summary>
        /// Initialization has not been started. Calling APIs that require initialization
        /// may queue work depending on the API's behavior.
        /// </summary>
        Uninitialized,

        /// <summary>
        /// Initialization is in progress. Consumers can listen for final state via
        /// <see cref="VivoxServiceInternal.Initialized"/> (for success) or
        /// <see cref="VivoxServiceInternal.InitializationFailed"/> (for failure).
        /// </summary>
        Initializing,

        /// <summary>
        /// Initialization completed successfully and the Vivox SDK is operational.
        /// At this point callers can safely use APIs that require an initialized SDK.
        /// </summary>
        Initialized,

        /// <summary>
        /// Initialization failed. See <see cref="VivoxServiceInternal.InitializationFailed"/>
        /// for exception details.
        /// </summary>
        Failed
    }
}
