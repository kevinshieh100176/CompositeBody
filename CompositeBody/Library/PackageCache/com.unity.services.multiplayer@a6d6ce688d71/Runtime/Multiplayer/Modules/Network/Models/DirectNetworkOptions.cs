namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Represents configuration options for direct network connections.
    /// </summary>
    /// <remarks>
    /// <para>
    /// To listen on all interfaces, use <see cref="ListenIPAddress.AnyIpv4"/>
    /// or <see cref="ListenIPAddress.AnyIpv6"/> as the <see cref="ListenIp"/>
    /// and specify the external/public IP address that clients should use as
    /// the <see cref="PublishIp"/>.
    /// </para>
    /// <para>
    /// Using direct networking in client-hosted games reveals the IP address of
    /// players to the host. For client-hosted games, using Relay or Distributed
    /// Authority is recommended to handle NAT, firewalls and protect player
    /// privacy.
    /// </para>
    /// <para>
    /// If a non-zero value is used, the <see cref="Port"/> number applies to
    /// both listen and publish addresses.
    /// </para>
    /// <para>
    /// When using Netcode for Entities, the default Multiplayer Services
    /// Network handler requires that your Client and Server Worlds are created
    /// before you create or join a session. Using the default
    /// <c>ClientServerBootstrap</c>, automatically creates the client and
    /// server worlds at startup when the Netcode for Entities package is first
    /// added to your project. For more advanced use cases, use <see
    /// cref="SessionOptionsExtensions.WithNetworkHandler{T}"/> to disable the
    /// default integration with Netcode for Entities.
    /// </para>
    /// </remarks>
    public class DirectNetworkOptions
    {
        /// <summary>
        /// Gets the IP address to listen on for incoming connections.
        /// </summary>
        public ListenIPAddress ListenIp { get; }

        /// <summary>
        /// Gets the IP address to publish for other clients to connect to.
        /// </summary>
        public PublishIPAddress PublishIp { get; }

        /// <summary>
        /// Gets the port number for the network connection.
        /// </summary>
        public ushort Port { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DirectNetworkOptions"/>
        /// class with default values (port <c>0</c>, IPv4 loopback addresses).
        /// </summary>
        /// <remarks>
        /// This constructor is incompatible with Netcode for GameObjects 1.x as
        /// providing a specific a port is required.
        /// </remarks>
        public DirectNetworkOptions() : this(ListenIPAddress.LoopbackIpv4, PublishIPAddress.LoopbackIpv4, 0)
        {}

        /// <summary>
        /// Initializes a new instance of the <see cref="DirectNetworkOptions"/>
        /// class with the specified port and default IPv4 loopback addresses
        /// for both listening and publishing.
        /// </summary>
        /// <param name="port">
        /// The port number to use for the connection.
        /// </param>
        public DirectNetworkOptions(ushort port) : this(ListenIPAddress.LoopbackIpv4, PublishIPAddress.LoopbackIpv4, port)
        {}

        /// <summary>
        /// Initializes a new instance of the <see cref="DirectNetworkOptions"/>
        /// class with the specified network configuration.
        /// </summary>
        /// <param name="listenIp">
        /// The IP address to listen on for incoming connections.
        /// </param>
        /// <param name="publishIp">
        /// The IP address to publish for other clients to connect to.
        /// </param>
        /// <param name="port">
        /// The port number to use for the connection.
        /// </param>
        public DirectNetworkOptions(ListenIPAddress listenIp, PublishIPAddress publishIp, ushort port)
        {
            ListenIp = listenIp;
            PublishIp = publishIp;
            Port = port;
        }
    }
}
