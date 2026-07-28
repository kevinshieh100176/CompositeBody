namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Networking role.
    /// </summary>
    public enum NetworkRole
    {
        /// <summary>
        /// A client.
        /// </summary>
        Client,

        /// <summary>
        /// A server that does not render the game (headless).
        /// </summary>
        /// <remarks>
        /// Also known as a dedicated server.
        /// </remarks>
        Server,

        /// <summary>
        /// A client that is also a server.
        /// </summary>
        /// <remarks>
        /// Also known as a listen server.
        /// </remarks>
        Host,
    }
}
