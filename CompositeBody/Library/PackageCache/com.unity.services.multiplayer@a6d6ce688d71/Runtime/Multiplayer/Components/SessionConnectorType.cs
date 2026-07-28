namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Type of session connector that a <see cref="SessionConnector"/> can run.
    /// </summary>
    /// <seealso cref="SessionConnector"/>
    public enum SessionConnectorType
    {
        /// <summary>
        /// Create a new session or join an existing one by session ID.
        /// </summary>
        CreateOrJoin,

        /// <summary>
        /// Create a new session.
        /// </summary>
        Create
    }
}
