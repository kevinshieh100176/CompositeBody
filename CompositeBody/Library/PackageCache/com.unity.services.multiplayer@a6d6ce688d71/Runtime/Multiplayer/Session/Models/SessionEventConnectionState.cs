namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// An enum describing the current state of a Session Event subscription's connection status.
    /// </summary>
    public enum SessionEventConnectionState
    {
        /// <summary>
        /// The session event subscription has reached an unknown state.
        /// </summary>
        Unknown,

        /// <summary>
        /// The session event subscription is currently unsubscribed.
        /// </summary>
        Unsubscribed,

        /// <summary>
        /// The session event subscription is currently trying to connect to the service.
        /// </summary>
        Subscribing,

        /// <summary>
        /// The session event subscription is currently connected, and ready to receive notifications.
        /// </summary>
        Subscribed,

        /// <summary>
        /// The session event subscription is currently connected, but for some reason is having trouble receiving notifications.
        /// </summary>
        Unsynced,

        /// <summary>
        /// The session event subscription is currently in an error state, and won't recover on its own.
        /// </summary>
        Error,
    }
}
