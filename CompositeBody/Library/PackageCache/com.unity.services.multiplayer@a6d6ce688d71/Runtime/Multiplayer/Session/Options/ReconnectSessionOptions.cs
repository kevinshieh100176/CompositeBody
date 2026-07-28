using System;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Options to configure a session when reconnecting.
    /// </summary>
    public class ReconnectSessionOptions
    {
        /// <summary>
        /// The type used to create or join to the session. Defaults to a GUID if not provided.
        /// See <see cref="BaseSessionOptions.Type">BaseSessionOptions.Type</see>.
        /// </summary>
        public string Type { get; set; } = Guid.NewGuid().ToString();


        internal INetworkHandler NetworkHandler { get; private set; }

        /// <summary>
        /// Configures a session to use a custom network handler.
        /// </summary>
        /// <remarks>
        /// <para>When a network handler is provided, it disables the default integration with Netcode for Game Objects
        /// and Netcode for Entities.</para>
        /// </remarks>
        /// <param name="networkHandler">The <see cref="INetworkHandler"/> to use </param>
        /// <returns>The session options</returns>
        public ReconnectSessionOptions WithNetworkHandler(INetworkHandler networkHandler)
        {
            NetworkHandler = networkHandler;
            return this;
        }
    }
}
