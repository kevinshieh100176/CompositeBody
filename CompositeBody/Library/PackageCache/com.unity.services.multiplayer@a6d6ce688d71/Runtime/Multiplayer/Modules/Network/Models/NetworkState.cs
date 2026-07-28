namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Represents the states of the underlying network.
    /// </summary>
    public enum NetworkState
    {
        /// <summary>
        /// The network is not active.
        /// </summary>
        Stopped,

        /// <summary>
        /// The network is in the process of starting.
        /// </summary>
        Starting,

        /// <summary>
        /// The network has started and is active.
        /// </summary>
        Started,

        /// <summary>
        /// The network is in the process of stopping.
        /// </summary>
        Stopping,

        /// <summary>
        /// The network is in the process of migrating.
        /// </summary>
        Migrating,
    }
}
