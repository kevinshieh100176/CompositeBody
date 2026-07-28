using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Networking.Transport.Relay;
using Unity.Services.Multiplayer;

namespace Unity.Services.Relay.Models
{
    /// <summary>
    /// Utility methods for relay allocations
    /// </summary>
    public static class AllocationUtils
    {
        // @formatter:off
        const string UdpProtocol             = "udp";
        const string DtlsProtocol            = "dtls";
        const string SecureWebSocketProtocol = "wss";

        static readonly IReadOnlyDictionary<string, RelayProtocol> k_StringToEnumMap =
            new Dictionary<string, RelayProtocol>()
        {
            [UdpProtocol]             = RelayProtocol.UDP,
            [DtlsProtocol]            = RelayProtocol.DTLS,
            [SecureWebSocketProtocol] = RelayProtocol.WSS
        };
        static readonly IReadOnlyDictionary<RelayProtocol, string> k_EnumToStringMap =
            k_StringToEnumMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
        // @formatter:on

        /// <summary>
        /// Convert an allocation to Transport's <see cref="RelayServerData"/>
        /// model.
        /// </summary>
        /// <param name="allocation">
        /// Allocation from which to create the server data.
        /// </param>
        /// <param name="connectionType">
        /// Type of connection to use (<c>"udp"</c>, <c>"dtls"</c> or <c>"wss"</c>).
        /// </param>
        /// <returns>Relay server data model for Transport.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if allocation is <c>null</c>, if the connection type is
        /// invalid or if no endpoint match the connection type.
        /// </exception>
        public static RelayServerData ToRelayServerData(this Allocation allocation, string connectionType)
        {
            return ToRelayServerData(allocation, ToRelayProtocol(connectionType));
        }

        /// <summary>
        /// Convert an allocation to Transport's <see cref="RelayServerData"/>
        /// model.
        /// </summary>
        /// <param name="allocation">
        /// Allocation from which to create the server data.
        /// </param>
        /// <param name="connectionType">
        /// Type of connection to use (<c>"udp"</c>, <c>"dtls"</c> or <c>"wss"</c>).
        /// </param>
        /// <returns>Relay server data model for Transport.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if allocation is <c>null</c>, if the connection type is
        /// invalid or if no endpoint match the connection type.
        /// </exception>
        public static RelayServerData ToRelayServerData(this JoinAllocation allocation, string connectionType)
        {
            return ToRelayServerData(allocation, ToRelayProtocol(connectionType));
        }

        /// <summary>
        /// Convert an allocation to Transport's <see cref="RelayServerData"/>
        /// model.
        /// </summary>
        /// <param name="allocation">
        /// Allocation from which to create the server data.
        /// </param>
        /// <param name="connectionType">
        /// Type of connection to use (<see cref="RelayProtocol.UDP"/>, <see cref="RelayProtocol.DTLS"/>
        /// or <see cref="RelayProtocol.WSS"/>).
        /// </param>
        /// <returns>Relay server data model for Transport.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if allocation is <c>null</c>, if the connection type is
        /// invalid or if no endpoint match the connection type.
        /// </exception>
        public static RelayServerData ToRelayServerData(this Allocation allocation, RelayProtocol connectionType)
        {
            if (allocation == null)
            {
                throw new ArgumentException("Invalid allocation.");
            }

            ValidateRelayConnectionType(connectionType);

            var isWebSocket = connectionType is RelayProtocol.WSS;
            var endpoint = GetEndpoint(allocation.ServerEndpoints, connectionType);

            return new RelayServerData(
                host: endpoint.Host,
                port: (ushort)endpoint.Port,
                allocationId: allocation.AllocationIdBytes,
                connectionData: allocation.ConnectionData,
                hostConnectionData: allocation.ConnectionData,
                key: allocation.Key,
                isSecure: endpoint.Secure,
                isWebSocket: isWebSocket);
        }

        /// <summary>
        /// Convert an allocation to Transport's <see cref="RelayServerData"/>
        /// model.
        /// </summary>
        /// <param name="allocation">
        /// Allocation from which to create the server data.
        /// </param>
        /// <param name="connectionType">
        /// Type of connection to use (<see cref="RelayProtocol.UDP"/>, <see
        /// cref="RelayProtocol.DTLS"/> or <see cref="RelayProtocol.WSS"/>).
        /// </param>
        /// <returns>Relay server data model for Transport.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if allocation is <c>null</c>, if the connection type is
        /// invalid or if no endpoint match the connection type.
        /// </exception>
        public static RelayServerData ToRelayServerData(this JoinAllocation allocation, RelayProtocol connectionType)
        {
            if (allocation == null)
            {
                throw new ArgumentException("Invalid allocation.");
            }

            ValidateRelayConnectionType(connectionType);

            var isWebSocket = connectionType is RelayProtocol.WSS;
            var endpoint = GetEndpoint(allocation.ServerEndpoints, connectionType);

            return new RelayServerData(
                host: endpoint.Host,
                port: (ushort)endpoint.Port,
                allocationId: allocation.AllocationIdBytes,
                connectionData: allocation.ConnectionData,
                hostConnectionData: allocation.HostConnectionData,
                key: allocation.Key,
                isSecure: endpoint.Secure,
                isWebSocket: isWebSocket);
        }

        internal static bool IsValidProtocol(RelayProtocol protocol)
        {
            return GetValidProtocols().Contains(protocol);
        }

        static RelayServerEndpoint GetEndpoint(List<RelayServerEndpoint> endpoints, RelayProtocol connectionType)
        {
            Logger.LogVerbose($"{nameof(AllocationUtils)}.ConnectionType: {connectionType:G}");
            if (endpoints != null)
            {
                foreach (var serverEndpoint in endpoints)
                {
                    if (string.Equals(serverEndpoint.ConnectionType, connectionType.ToString("G"), StringComparison.OrdinalIgnoreCase))
                    {
                        return serverEndpoint;
                    }
                }
            }

            throw new ArgumentException($"No endpoint for connection type \"{connectionType}\" in allocation.");
        }

        /// <summary>
        /// Converts a string representing a connection type to its
        /// corresponding <see cref="RelayProtocol"/> enum value.
        /// </summary>
        /// <param name="connectionType">
        /// The connection type as a string. Supported values are <c>udp</c>,
        /// <c>dtls</c>, <c>ws</c> or <c>wss</c>.
        /// </param>
        /// <returns>
        /// The <see cref="RelayProtocol"/> enum value corresponding to the
        /// provided connection type string.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if the provided connection type string does not match any
        /// supported protocol.
        /// </exception>
        static RelayProtocol ToRelayProtocol(string connectionType)
        {
            connectionType = connectionType.ToLower();
            if (k_StringToEnumMap.TryGetValue(connectionType, out var relayProtocol))
            {
                return relayProtocol;
            }

            throw UnsupportedProtocolError(connectionType);
        }

        /// <summary>
        /// Validates that the provided <paramref
        /// name="connectionType"/> is a supported relay protocol.
        /// </summary>
        /// <param name="connectionType">The relay protocol to validate.</param>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="connectionType"/> is not a valid
        /// protocol as determined by <see cref="GetValidProtocols"/>.
        /// </exception>
        static void ValidateRelayConnectionType(RelayProtocol connectionType)
        {
            // We check against a hardcoded list of strings instead of
            // just trying to find the connection type in the endpoints
            // since it may contain things we don't support (e.g.
            // they provide a "tcp" endpoint which we don't support).
            var validProtocols = GetValidProtocols();
            if (validProtocols.Contains(connectionType))
            {
                Logger.LogVerbose($"Valid connection type was detected: \"{connectionType:G}\".");
            }
            else
            {
                throw UnsupportedProtocolError(connectionType.ToString("G"));
            }
        }

        /// <summary>
        /// Returns the list of valid <see cref="RelayProtocol"/>
        /// values supported by the current platform.<br/>
        /// <list type="table">
        /// <listheader>
        /// <term>Platform</term>
        /// <description>Supported Protocols</description>
        /// </listheader>
        /// <item>
        /// <term>WebGL</term>
        /// <description>
        /// <see cref="RelayProtocol.WSS"/>
        /// </description>
        /// </item>
        /// <item>
        /// <term>Other</term>
        /// <description>
        /// <see cref="RelayProtocol.UDP"/><br/><see
        /// cref="RelayProtocol.DTLS"/><br/><see cref="RelayProtocol.WSS"/>
        /// </description>
        /// </item>
        /// </list>
        /// </summary>
        /// <returns>
        /// An array of valid <see cref="RelayProtocol"/>
        /// values for the current platform.
        /// </returns>
        static RelayProtocol[] GetValidProtocols()
        {
#if UNITY_WEBGL
            return new[]
            {
                RelayProtocol.WSS
            };
#else
            return new[]
            {
                RelayProtocol.UDP, RelayProtocol.DTLS, RelayProtocol.WSS
            };
#endif
        }

        static ArgumentException UnsupportedProtocolError(string connectionType)
        {
            var protocols = GetValidProtocols().Select(p => string.Concat("\"", k_EnumToStringMap[p], "\"")).ToArray();
            return new ArgumentException($"Invalid connection type: \"{connectionType}\". Connection type must be one of: {EnumerateValidProtocols(protocols)}.");
        }

        static string EnumerateValidProtocols(in ReadOnlySpan<string> names)
        {
            var result = string.Join(", ", names[..^ 1].ToArray());
            return string.Concat(result, " or ", names[^ 1]);
        }
    }
}
