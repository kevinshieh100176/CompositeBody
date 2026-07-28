namespace Unity.Services.Multiplayer
{
    public static partial class SessionOptionsExtensions
    {
        /// <summary>
        /// Signals that this session id is based on a matchmaking match id.
        /// Matchmaking results will be fetched based on the session id.
        /// </summary>
        /// <typeparam name="T">
        /// The <see cref="SessionOptions">options</see>' type.
        /// </typeparam>
        /// <param name="options">The options</param>
        /// <returns>The <see cref="SessionOptions"/>.</returns>
        internal static T WithMatchmaker<T>(this T options) where T : SessionOptions
        {
            return options.WithOption(new EnableMatchmakerModuleOption());
        }

        /// <summary>
        /// Configures a session to use Relay networking.
        /// </summary>
        /// <param name="options">
        /// The SessionOptions this extension method applies to.
        /// </param>
        /// <param name="region">
        /// Force a specific Relay <a
        /// href="https://docs.unity.com/ugs/en-us/manual/relay/manual/locations-and-regions">region</a>
        /// to be used and skip auto-selection from <a
        /// href="https://docs.unity.com/ugs/en-us/manual/relay/manual/qos">QoS</a>.
        /// </param>
        /// <typeparam name="T">The options' type.</typeparam>
        /// <returns>
        /// The <see cref="SessionOptions"/> configured to automatically
        /// create and connect to a Relay server
        /// once the <see cref="ISession"/> is created.
        /// </returns>
        /// <remarks>
        /// <para>The <a
        /// href="https://docs.unity.com/ugs/en-us/manual/relay/manual/locations-and-regions">region</a>
        /// is optional; the default behavior is to perform quality of service
        /// (<a
        /// href="https://docs.unity.com/ugs/en-us/manual/relay/manual/qos">QoS</a>)
        /// measurements and pick the lowest latency region. The list of regions
        /// can be obtained from the Relay Allocations Service via <see
        /// cref="Unity.Services.Relay.IRelayService.ListRegionsAsync"/>.</para>
        /// <para>When using Netcode for Entities, the default Multiplayer
        /// Services Network handler requires that your Client and Server Worlds
        /// are created before you create or join a session. Using the default
        /// <c>ClientServerBootstrap</c>, automatically creates the client and
        /// server worlds at startup when the Netcode for Entities package is
        /// first added to your project. For more advanced use cases, use <see
        /// cref="WithNetworkHandler{T}"/> to disable the default integration
        /// with Netcode for Entities.</para>
        /// </remarks>
        public static T WithRelayNetwork<T>(this T options,
            string region = null) where T : SessionOptions
        {
            return WithRelayNetwork(options, new RelayNetworkOptions(region));
        }

        /// <summary>
        /// Configures a session to use Relay networking.
        /// </summary>
        /// <param name="options">The SessionOptions this extension method
        /// applies to.</param>
        /// <param name="relayOptions">The <see cref="RelayNetworkOptions"/></param>
        /// <typeparam name="T">The options' type.</typeparam>
        /// <returns>The <see cref="SessionOptions"/>.</returns>
        /// <remarks>
        /// <para>The region is optional; the default behavior is to perform
        /// quality of service (QoS) measurements and pick the lowest latency
        /// region. The list of regions can be obtained from the Relay
        /// Allocations Service via <see
        /// cref="Unity.Services.Relay.IRelayService.ListRegionsAsync"/>.</para>
        /// <para>When using Netcode for Entities, the default Multiplayer
        /// Services Network handler requires that your Client and Server Worlds
        /// are created before you create or join a session. Using the default
        /// <c>ClientServerBootstrap</c>, automatically creates the client and
        /// server worlds at startup when the Netcode for Entities package is
        /// first added to your project. For more advanced use cases, use <see
        /// cref="WithNetworkHandler{T}"/> to disable the default integration
        /// with Netcode for Entities.</para>
        /// </remarks>
        public static T WithRelayNetwork<T>(this T options, RelayNetworkOptions relayOptions) where T : SessionOptions
        {
            return options.WithOption(new NetworkInfoOption(NetworkInfo.BuildRelay(relayOptions)));
        }

        /// <summary>
        /// Configures a session to use direct networking and accept connections
        /// at the specified address.
        /// </summary>
        /// <param name="options">The SessionOptions this extension method
        /// applies to.</param>
        /// <param name="listenIp">Listen for incoming connection at this
        /// address (<c>0.0.0.0</c> for all interfaces).</param>
        /// <param name="publishIp">Address that clients should use when
        /// connecting</param>
        /// <param name="port">Port to listen for incoming connections and also
        /// the one to use by clients</param>
        /// <typeparam name="T">The options' type.</typeparam>
        /// <returns>The session options</returns>
        /// <remarks>
        /// <para>Using direct networking in client-hosted games reveals the IP
        /// address of players to the host. For client-hosted games, using Relay
        /// or Distributed Authority is recommended to handle NAT, firewalls and
        /// protect player privacy.</para>
        /// <para>The default values allow local connections only and use
        /// <c>127.0.0.1</c> as the <paramref name="listenIp"/> and <paramref
        /// name="publishIp"/>. To listen on all interfaces, use <c>0.0.0.0</c>
        /// as the listenIp and specify the external/public IP address that
        /// clients should use as the publishIp.</para>
        /// <para>The port number defaults to <c>0</c> which selects a randomly
        /// available port on the machine and uses the chosen value as the
        /// publish port. If a non-zero value is used, the port number applies
        /// to both listen and publish addresses.</para>
        /// <para>When using Netcode for Entities, the default Multiplayer
        /// Services Network handler requires that your Client and Server Worlds
        /// are created before you create or join a session. Using the default
        /// <c>ClientServerBootstrap</c>, automatically creates the client and
        /// server worlds at startup when the Netcode for Entities package is
        /// first added to your project. For more advanced use cases, use <see
        /// cref="WithNetworkHandler{T}"/> to disable the default integration
        /// with Netcode for Entities.</para>
        /// </remarks>
        public static T WithDirectNetwork<T>(this T options,
            string listenIp = "127.0.0.1",
            string publishIp = "127.0.0.1", int port = 0)
            where T : SessionOptions
        {
            return options.WithDirectNetwork(new DirectNetworkOptions(new ListenIPAddress(listenIp), new PublishIPAddress(publishIp), (ushort)port));
        }

        /// <summary>
        /// Same as <see cref="WithDirectNetwork{T}(T, DirectNetworkOptions)"/>
        /// but the listen address defaults to <see
        /// cref="ListenIPAddress.LoopbackIpv4"/>, the publish address defaults
        /// to <see cref="PublishIPAddress.LoopbackIpv4"/> and the port defaults
        /// to<c>0</c>.
        /// </summary>
        /// <param name="options">The SessionOptions this extension method
        /// applies to.</param>
        /// <typeparam name="T">The options' type.</typeparam>
        /// <returns>The session options.</returns>
        /// <remarks>
        /// <para>The default values allow local connections only.</para>
        /// <para>
        /// The port number defaults to <c>0</c> which selects a randomly
        /// available port on the machine and uses the chosen value as the
        /// publish port.
        /// </para>
        /// </remarks>
        /// <seealso cref="WithDirectNetwork{T}(T, DirectNetworkOptions)"/>
        public static T WithDirectNetwork<T>(this T options)
            where T : SessionOptions
        {
            return options.WithOption(new NetworkInfoOption(NetworkInfo.BuildDirect(new DirectNetworkOptions())));
        }

        /// <summary>
        /// Configures a session to use direct networking and accept connections
        /// at the specified IP address.
        /// </summary>
        /// <param name="options">The SessionOptions this extension method
        /// applies to.</param>
        /// <param name="networkOptions">
        /// The options used to configure the direct network connection,
        /// including port settings and other network parameters.
        /// </param>
        /// <typeparam name="T">The options' type.</typeparam>
        /// <returns>The session options.</returns>
        public static T WithDirectNetwork<T>(this T options,
            DirectNetworkOptions networkOptions)
            where T : SessionOptions
        {
            return options.WithOption(new NetworkInfoOption(NetworkInfo.BuildDirect(networkOptions)));
        }

        /// <summary>
        /// Configures a session to use a custom network handler.
        /// </summary>
        /// <param name="options">The SessionOptions this extension method
        /// applies to.</param>
        /// <param name="networkHandler">The <see cref="INetworkHandler"/> to
        /// use.</param>
        /// <typeparam name="T">The options' type.</typeparam>
        /// <returns>The session options.</returns>
        /// <remarks>
        /// <para>When a network handler is provided, it disables the default
        /// integration with Netcode for Game Objects and Netcode for
        /// Entities.</para>
        /// <para>Combine this option with other networking options:<br/>
        /// <see cref="WithDirectNetwork{T}(T)"/><br/>
        /// <see cref="WithDirectNetwork{T}(T, string, string, int)"/><br/>
        /// <see cref="WithDirectNetwork{T}(T, DirectNetworkOptions)"/><br/>
        /// <see cref="WithRelayNetwork{T}(T, string)"/><br/>
        /// <see cref="WithRelayNetwork{T}(T, RelayNetworkOptions)"/><br/>
        /// <see cref="WithDistributedAuthorityNetwork{T}(T, string)"/><br/>
        /// <see cref="WithDistributedAuthorityNetwork{T}(T, RelayNetworkOptions)"/><br/><br/>
        /// to obtain the appropriate data to implement a custom management of
        /// the netcode library and/or transport library.</para>
        /// <para>This option applies to all session flows and is normally set
        /// for all roles (host, server, client).</para>
        /// </remarks>
        public static T WithNetworkHandler<T>(this T options,
            INetworkHandler networkHandler)
            where T : BaseSessionOptions
        {
            return options.WithOption(new NetworkHandlerOption(networkHandler));
        }

        /// <summary>
        /// Configures a session to use a specific configuration when connecting to a network.
        /// </summary>
        /// <param name="options">The SessionOptions this extension method
        /// applies to.</param>
        /// <param name="networkOptions">The network configuration to apply to the session.</param>
        /// <typeparam name="T">The options' type.</typeparam>
        /// <returns>The session options.</returns>
        public static T WithNetworkOptions<T>(this T options, NetworkOptions networkOptions)
            where T : BaseSessionOptions
        {
            return options.WithOption(new NetworkModuleOptions(networkOptions));
        }

#if GAMEOBJECTS_NETCODE_2_AVAILABLE || PACKAGE_DOCS_GENERATION
        /// <summary>
        /// Configures a session to use the Distributed Authority networking.
        /// </summary>
        /// <param name="options">
        /// The <see cref="SessionOptions"/> this extension method applies to.
        /// </param>
        /// <param name="region">
        /// The Relay <see
        /// href="https://docs.unity.com/ugs/en-us/manual/relay/manual/locations-and-regions">region</see>
        /// where the Relay allocation used by Distributed Authority will
        /// happen. Defaults to <c>us-central1</c>.
        /// </param>
        /// <typeparam name="T">The options' type.</typeparam>
        /// <returns>
        /// The <see cref="SessionOptions"/> configured to automatically
        /// connect to a Distributed Authority server
        /// once the <see cref="ISession"/> is created.
        /// </returns>
        /// <remarks>
        /// To obtain the list of available region, use the Relay Allocations
        /// Service via <see
        /// cref="Unity.Services.Relay.IRelayService.ListRegionsAsync"/>.<br/>
        /// To determine the lowest latency region, use <see
        /// cref="Unity.Services.Qos.IQosService.GetSortedRelayQosResultsAsync"/>.
        /// </remarks>
        public static T WithDistributedAuthorityNetwork<T>(this T options,
            string region = null)
            where T : SessionOptions
        {
            return options.WithDistributedAuthorityNetwork(
                new RelayNetworkOptions(region));
        }

        /// <summary>
        /// Configures a session to use the Distributed Authority networking
        /// with the specified <see cref="RelayNetworkOptions"/>.
        /// </summary>
        /// <param name="options">
        /// The <see cref="SessionOptions"/> this extension method applies to.
        /// </param>
        /// <param name="relayOptions">
        /// The <see cref="RelayNetworkOptions"/> specifying the relay <see
        /// cref="RelayProtocol"/> and <see
        /// href="https://docs.unity.com/ugs/en-us/manual/relay/manual/locations-and-regions">region</see>
        /// to use for Distributed Authority networking.
        /// </param>
        /// <typeparam name="T">The options' type.</typeparam>
        /// <returns>
        /// The <see cref="SessionOptions"/> configured to use the provided
        /// <paramref name="relayOptions"/>.
        /// </returns>
        /// <remarks>
        /// To obtain the list of available regions, use the Relay Allocations
        /// Service via <see
        /// cref="Unity.Services.Relay.IRelayService.ListRegionsAsync"/>.<br/>
        /// To determine the lowest latency region, use <see
        /// cref="Unity.Services.Qos.IQosService.GetSortedRelayQosResultsAsync"/>.
        /// </remarks>
        public static T WithDistributedAuthorityNetwork<T>(this T options, RelayNetworkOptions relayOptions)
            where T : SessionOptions
        {
            return options.WithOption(new NetworkInfoOption(NetworkInfo.BuildDistributed(relayOptions)));
        }

#endif
    }
}
