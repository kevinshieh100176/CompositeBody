namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Indicates the state of a session.
    /// </summary>
    public enum SessionState
    {
        /// <summary>
        /// Session unavailable.
        /// </summary>
        None,
        /// <summary>
        /// Connected to the session.
        /// </summary>
        Connected,
        /// <summary>
        /// Disconnected from the session.
        /// </summary>
        Disconnected,
        /// <summary>
        /// Session has been deleted.
        /// </summary>
        Deleted
    }
}
