namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// The type of network used for a session.
    /// </summary>
    public enum NetworkType
    {
        /// <summary>
        /// Direct networking.
        /// </summary>
        /// <remarks>
        /// Using direct networking in client-hosted games reveals the IP address of players to the host.
        /// For client-hosted games, using Relay or Distributed Authority is recommended to handle NAT, firewalls and
        /// protect player privacy.
        /// </remarks>
        Direct,

        /// <summary>
        /// Relay networking.
        /// </summary>
        Relay,

        /// <summary>
        /// Distributed authority networking.
        /// </summary>
        /// <remarks>
        /// Required to use distributed authority options in Netcode for GameObjects.
        /// </remarks>
        DistributedAuthority,
    }
}
