namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// The type of netcode to use
    /// </summary>
    enum NetcodeType
    {
        /// <summary>
        /// Netcode for GameObjects
        /// </summary>
        GameObjects,
        /// <summary>
        /// Netcode for Entities
        /// </summary>
        Entities,
        /// <summary>
        /// No netcode detected
        /// </summary>
        Unknown
    }
}
