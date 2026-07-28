using System;
using Unity.Networking.Transport;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Provides methods for validating and parsing IP addresses.
    /// </summary>
    public interface IIpValidator
    {
        /// <summary>
        /// Checks if the given string is a valid IP address (IPv4 or IPv6).
        /// </summary>
        /// <param name="ip">The IP address string to validate.</param>
        /// <returns>Returns <see langword="true"/> if the IP address is valid;
        /// otherwise, <see langword="false"/>.</returns>
        bool IsValidIPAddress(string ip);

        /// <summary>
        /// Attempts to parse the given string as an IP address.
        /// </summary>
        /// <param name="ip">The IP address string to parse.</param>
        /// <param name="endpoint">
        /// When this method returns, contains the parsed <see
        /// cref="NetworkEndpointAddress"/> if the parse succeeded; otherwise, the
        /// default value.
        /// </param>
        /// <returns>Returns <see langword="true"/> if the IP address was
        /// successfully parsed; otherwise, <see langword="false"/>.</returns>
        bool TryParseIPAddress(string ip, out NetworkEndpointAddress endpoint);
    }

    static class IpValidatorUtils
    {
        internal static IIpValidator GetPlatformDependentValidator()
        {
#if !(UNITY_SWITCH || UNITY_PS4 || UNITY_PS5)
            return new IpValidator(NetworkFamily.Invalid);
#else
            return new IpValidator(NetworkFamily.Ipv4);
#endif
        }

        /// <summary>
        /// Checks IPv4 or IPv6 address is well-formed.
        /// </summary>
        /// <param name="ip">The IPv4 or IPv6 address to check.</param>
        /// <param name="family"></param>
        /// <returns>
        /// Returns <see langword="true" /> if the IP address is
        /// well-formed, <see langword="false" /> otherwise.
        /// </returns>
        internal static bool IsValidIPAddress(this string ip, NetworkFamily family)
        {
            return ip.TryParseIPAddress(out _, family);
        }

        /// <summary>
        /// Attempts to parse the given string as an IP address (IPv4 or IPv6).
        /// <br/>
        /// Platform limitations may apply.
        /// </summary>
        /// <param name="ip">The IP address string to parse.</param>
        /// <param name="endpoint">
        /// The <paramref name="endpoint"/> out parameter contains the parsed
        /// <see cref="NetworkEndpointAddress" /> if the parse succeeded; otherwise, it
        /// contains the default value.
        /// </param>
        /// <param name="family">
        /// Specifies the network address family (IPv4, IPv6, or Invalid) to use
        /// when parsing the IP address. The default is <see
        /// cref="NetworkFamily.Invalid"/>, which allows the underlying <see
        /// cref="NetworkEndpointAddress"/> to automatically determine the family based
        /// on the provided address.
        /// </param>
        /// <returns>
        /// Returns <see langword="true"/> if the IP address was successfully
        /// parsed; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// This method attempts to parse the input string as an IP address,
        /// supporting both IPv4 and IPv6 formats. On platforms where IPv6 is
        /// not supported (Switch, PS4, PS5), only IPv4 parsing is attempted.
        /// </remarks>
        internal static bool TryParseIPAddress(this string ip,
            out NetworkEndpointAddress endpoint, NetworkFamily family)
        {
            const ushort port = 0;
            // The port is not used in this case, but we need to pass it to the
            // TryParse method.
            var result = NetworkEndpoint.TryParse(ip, port,
                out var endpointInternal, family);
            endpoint = new NetworkEndpointAddress(endpointInternal);
            return result;
        }
    }

    class IpValidator : IIpValidator
    {
        readonly NetworkFamily m_Family;

        public static readonly IpValidator BothIpv4AndIpv6 = new(NetworkFamily.Invalid);
        public static readonly IpValidator Ipv4Only = new(NetworkFamily.Ipv4);

        public IpValidator(NetworkFamily family)
        {
            m_Family = family;
        }

        public bool IsValidIPAddress(string ip)
        {
            return TryParseIPAddress(ip, out _);
        }

        public bool TryParseIPAddress(string ip, out NetworkEndpointAddress endpoint)
        {
            return ip.TryParseIPAddress(out endpoint, m_Family);
        }
    }

    /// <summary>
    /// Represents a network endpoint, wrapping a <see
    /// cref="Unity.Networking.Transport.NetworkEndpoint" />. Provides utility
    /// properties and constructors for working with IP addresses and ports in
    /// Unity Multiplayer.
    /// </summary>
    public struct NetworkEndpointAddress
    {
        const string InvalidIpAddressErrorMessage =
            "The IP address is not valid.";

        /// <summary>
        /// Gets an IPv4 <see cref="PublishIPAddress" /> representing the loopback
        /// address.
        /// </summary>
        public static readonly NetworkEndpointAddress LoopbackIpv4 =
            new(NetworkEndpoint.LoopbackIpv4);

        /// <summary>
        /// Gets an IPv6 <see cref="PublishIPAddress" /> representing the loopback
        /// address.
        /// </summary>
        public static readonly NetworkEndpointAddress LoopbackIpv6 =
            new(NetworkEndpoint.LoopbackIpv6);

        /// <summary>
        /// Gets an IPv4 <see cref="ListenIPAddress" /> representing any address.
        /// </summary>
        public static readonly NetworkEndpointAddress AnyIpv4 =
            new(NetworkEndpoint.AnyIpv4);

        /// <summary>
        /// Gets an IPv6 <see cref="ListenIPAddress" /> representing any address.
        /// </summary>
        public static readonly NetworkEndpointAddress AnyIpv6 =
            new(NetworkEndpoint.AnyIpv6);

        NetworkEndpoint m_UtpEndpoint;

        /// <summary>
        /// Gets the port of the endpoint.
        /// </summary>
        public ushort Port => m_UtpEndpoint.Port;

        /// <summary>
        /// Gets the address (IP and port) as a string.
        /// </summary>
        /// <remarks>For an IPv4 address the format is as follows.
        /// <br/><c>ip_address:port</c>, e.g. <c>127.0.0.1:567</c>.
        /// <br/>For an IPv6 address the format is as follows.
        /// <br/><c>[ip_address]:port</c>, e.g.
        /// <br/>,<c>[2001:0db8:85a3:0000:0000:8a2e:0370:7334]:567</c>.
        /// </remarks>
        public string Address => m_UtpEndpoint.Address;

        /// <summary>
        /// Gets the address (IP only, no port) as a string.
        /// </summary>
        public string AddressNoPort
        {
            get
            {
                if (m_UtpEndpoint.Family == NetworkFamily.Ipv4)
                {
                    return m_UtpEndpoint.ToFixedStringNoPort().ToString();
                }

                var address = m_UtpEndpoint.ToFixedStringNoPort();
                var addressToStandardString = address.ToString().Trim('\u005b', '\u005d');
                return addressToStandardString;
            }
        }

        /// <summary>
        /// Gets the underlying Unity Transport network endpoint.
        /// </summary>
        public NetworkEndpoint UtpEndpoint => m_UtpEndpoint;

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkEndpointAddress" />
        /// struct from an IP address and port.
        /// </summary>
        /// <param name="ip">The IPv4 or IPv6 address as a string.</param>
        /// <param name="port">The port number.</param>
        /// <exception cref="ArgumentException">Thrown if the IP address is not
        /// valid.</exception>
        public NetworkEndpointAddress(string ip, ushort port) : this(ip, port, IpValidatorUtils.GetPlatformDependentValidator())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkEndpointAddress" />
        /// struct from an IP address and port.
        /// </summary>
        /// <param name="ip">The IPv4 or IPv6 address as a string.</param>
        /// <param name="port">The port number.</param>
        /// <param name="ipValidator">The IP address validator used to parse and
        /// validate the provided address.</param>
        /// <exception cref="ArgumentException">Thrown if the IP address is not
        /// valid.</exception>
        internal NetworkEndpointAddress(string ip, ushort port, IIpValidator ipValidator)
        {
            if (!ipValidator.TryParseIPAddress(ip, out var endpoint))
            {
                throw new ArgumentException(InvalidIpAddressErrorMessage);
            }

            m_UtpEndpoint = endpoint.UtpEndpoint.WithPort(port);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkEndpointAddress" />
        /// struct from an existing Unity Transport endpoint.
        /// </summary>
        /// <param name="utpEndpoint">The Unity Transport network endpoint.</param>
        public NetworkEndpointAddress(NetworkEndpoint utpEndpoint)
        {
            m_UtpEndpoint = utpEndpoint;
        }

        /// <summary>
        /// Returns a copy of this <see cref="NetworkEndpointAddress" /> with the
        /// specified port.
        /// </summary>
        /// <param name="port">The port to set.</param>
        /// <returns>A new <see cref="NetworkEndpointAddress" /> with the updated
        /// port.</returns>
        public readonly NetworkEndpointAddress WithPort(ushort port)
        {
            var endpoint = this;
            endpoint.m_UtpEndpoint.Port = port;
            return endpoint;
        }
    }

    /// <summary>
    /// Represents a network address (IPv4 or IPv6).
    /// </summary>
    public readonly struct PublishIPAddress
    {
        const string InvalidAddressErrorMessage =
            "The publish address is not valid.";

        readonly NetworkEndpointAddress m_Endpoint;

        /// <summary>
        /// Initializes a new instance of the <see cref="PublishIPAddress" />
        /// struct from the specified IP address string.
        /// </summary>
        /// <param name="address">The IPv4 or IPv6 address to parse.</param>
        /// <exception cref="ArgumentException">
        /// Thrown if the provided address is not a valid IP address.
        /// </exception>
        public PublishIPAddress(string address) : this(address, IpValidatorUtils.GetPlatformDependentValidator())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PublishIPAddress" />
        /// struct from the specified IP address string.
        /// </summary>
        /// <param name="address">The IPv4 or IPv6 address to parse.</param>
        /// <param name="ipValidator">The IP address validator used to parse and
        /// validate the provided address.</param>
        /// <exception cref="ArgumentException">
        /// Thrown if the provided address is not a valid IP address.
        /// </exception>
        internal PublishIPAddress(string address, IIpValidator ipValidator)
        {
            if (!ipValidator.TryParseIPAddress(address, out var endpoint))
            {
                throw new ArgumentException(InvalidAddressErrorMessage);
            }

            m_Endpoint = endpoint;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PublishIPAddress" />
        /// struct from the specified <see cref="NetworkEndpoint" />.
        /// </summary>
        /// <param name="endpoint">
        /// The network endpoint to use for this
        /// address.
        /// </param>
        public PublishIPAddress(NetworkEndpointAddress endpoint)
        {
            m_Endpoint = endpoint;
        }

        /// <summary>
        /// Returns a new <see cref="PublishIPAddress" /> with the specified port
        /// set.
        /// </summary>
        /// <param name="port">The port to set on the address.</param>
        /// <returns>A new <see cref="PublishIPAddress" /> with the updated port.</returns>
        public PublishIPAddress WithPort(ushort port)
        {
            return new PublishIPAddress(m_Endpoint.WithPort(port));
        }

        /// <summary>
        /// Gets the underlying <see cref="NetworkEndpoint" /> for this publish
        /// address.
        /// </summary>
        public NetworkEndpointAddress NetworkEndpoint => m_Endpoint;

        /// <summary>
        /// Gets the underlying Unity Transport <see cref="NetworkEndpoint" />
        /// for this publish address.
        /// </summary>
        public NetworkEndpoint UtpNetworkEndpoint => m_Endpoint.UtpEndpoint;

        /// <summary>
        /// Gets a <see cref="PublishIPAddress" /> representing the IPv4 loopback
        /// address.
        /// </summary>
        public static PublishIPAddress LoopbackIpv4 =>
            new(NetworkEndpointAddress.LoopbackIpv4);

        /// <summary>
        /// Gets a <see cref="PublishIPAddress" /> representing the IPv6 loopback
        /// address.
        /// </summary>
        public static PublishIPAddress LoopbackIpv6 =>
            new(NetworkEndpointAddress.LoopbackIpv6);
    }

    /// <summary>
    /// Represents a network address (IPv4 or IPv6).
    /// </summary>
    public readonly struct ListenIPAddress
    {
        const string InvalidAddressErrorMessage =
            "The listen address is not valid.";

        readonly NetworkEndpointAddress m_Endpoint;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListenIPAddress" /> struct
        /// from the specified IP address string.
        /// </summary>
        /// <param name="address">The IPv4 or IPv6 address to parse.</param>
        /// <exception cref="ArgumentException"> Thrown if the provided address
        /// is not a valid IP address.
        /// </exception>
        public ListenIPAddress(string address) : this(address, IpValidatorUtils.GetPlatformDependentValidator())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ListenIPAddress" /> struct
        /// from the specified IP address string.
        /// </summary>
        /// <param name="address">The IPv4 or IPv6 address to parse.</param>
        /// <param name="ipValidator">The IP address validator used to parse and
        /// validate the provided address.</param>
        /// <exception cref="ArgumentException"> Thrown if the provided address
        /// is not a valid IP address.
        /// </exception>
        internal ListenIPAddress(string address, IIpValidator ipValidator)
        {
            if (!ipValidator.TryParseIPAddress(address, out var endpoint))
            {
                Logger.LogError($"Could not parse given ip address: {address}");
                throw new ArgumentException(InvalidAddressErrorMessage);
            }

            m_Endpoint = endpoint;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ListenIPAddress" />
        /// struct from the specified <see cref="NetworkEndpoint" />.
        /// </summary>
        /// <param name="endpoint"> The network endpoint to use for this
        /// address.</param>
        public ListenIPAddress(NetworkEndpointAddress endpoint)
        {
            m_Endpoint = endpoint;
        }

        /// <summary>
        /// Returns a new <see cref="ListenIPAddress" /> with the specified port
        /// set.
        /// </summary>
        /// <param name="port">The port to set on the address.</param>
        /// <returns>
        /// A new <see cref="ListenIPAddress" /> with the updated port.
        /// </returns>
        public ListenIPAddress WithPort(ushort port)
        {
            return new ListenIPAddress(m_Endpoint.WithPort(port));
        }

        /// <summary>
        /// Gets the underlying <see cref="NetworkEndpoint" /> for this listen
        /// address.
        /// </summary>
        public NetworkEndpointAddress NetworkEndpoint => m_Endpoint;

        /// <summary>
        /// Gets the underlying Unity Transport <see cref="NetworkEndpoint" />
        /// for this listen address.
        /// </summary>
        public NetworkEndpoint UtpNetworkEndpoint => m_Endpoint.UtpEndpoint;

        /// <summary>
        /// Gets a <see cref="ListenIPAddress" /> representing the IPv4 loopback
        /// address.
        /// </summary>
        public static ListenIPAddress LoopbackIpv4 =>
            new(NetworkEndpointAddress.LoopbackIpv4);

        /// <summary>
        /// Gets a <see cref="ListenIPAddress" /> representing the IPv6 loopback
        /// address.
        /// </summary>
        public static ListenIPAddress LoopbackIpv6 =>
            new(NetworkEndpointAddress.LoopbackIpv6);

        /// <summary>
        /// Gets a <see cref="ListenIPAddress" /> representing any IPv4 address.
        /// <br/>
        /// The actual IPv4 address is <c>0.0.0.0</c>.
        /// </summary>
        public static ListenIPAddress AnyIpv4 => new(NetworkEndpointAddress.AnyIpv4);

        /// <summary>
        /// Gets a <see cref="ListenIPAddress" /> representing any IPv6 address.
        /// <br/>
        /// The actual IPv6 address is <c>::</c> or <c>0:0:0:0:0:0:0:0</c>.
        /// </summary>
        public static ListenIPAddress AnyIpv6 => new(NetworkEndpointAddress.AnyIpv6);
    }

    class NetworkInfo
    {
        internal NetworkType Network { get; private set; }
        internal PublishIPAddress PublishAddress { get; private set; }
        internal ListenIPAddress ListenAddress { get; private set; }
        internal RelayNetworkOptions RelayOptions { get; private set; }

        NetworkInfo() {}

        public static NetworkInfo BuildDirect(DirectNetworkOptions networkOptions)
        {
            return new NetworkInfo { Network = NetworkType.Direct, PublishAddress = networkOptions.PublishIp.WithPort(networkOptions.Port), ListenAddress = networkOptions.ListenIp.WithPort(networkOptions.Port) };
        }

        public static NetworkInfo BuildRelay(RelayNetworkOptions relayOptions)
        {
            return new NetworkInfo { Network = NetworkType.Relay, RelayOptions = relayOptions };
        }

        public static NetworkInfo BuildDistributed(RelayNetworkOptions relayOptions)
        {
            return new NetworkInfo { Network = NetworkType.DistributedAuthority, RelayOptions = relayOptions };
        }

        public void OverrideDirectNetworkInfo(PublishIPAddress publishAddress,
            ushort port, ListenIPAddress listenAddress)
        {
            PublishAddress = publishAddress.WithPort(port);
            ListenAddress = listenAddress.WithPort(port);
        }
    }
}
