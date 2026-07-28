using System.Collections.Generic;
using Unity.Services.Matchmaker.Models;

namespace Unity.Services.Matchmaker
{
    /// <summary>
    /// Parameter class for making matchmaker backfill ticket requests.
    /// </summary>
    public class CreateBackfillTicketOptions
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public CreateBackfillTicketOptions()
        {
        }

        /// <summary>
        /// Parameterized constructor.
        /// </summary>
        /// <param name="queueName">
        /// Name of the queue to target the backfill request. See <see cref="QueueName"/>.
        /// </param>
        /// <param name="connection">
        /// The IP address and port of the server creating the backfill
        /// (using the format ip:port). See <see cref="Connection"/>.
        /// </param>
        /// <param name="attributes">
        /// A dictionary of attributes (number or string), indexed by the attribute name. See <see cref="Attributes"/>.
        /// </param>
        /// <param name="properties">
        /// Properties object containing match information. See <see cref="Properties"/>.
        /// </param>
        /// <param name="poolId">
        /// The ID of the pool to create the backfill ticket in. See <see cref="PoolId"/>.
        /// </param>
        /// <param name="matchId">
        /// The ID of the match that this backfill ticket is targeting. See <see cref="MatchId"/>.
        /// </param>
        /// <remarks>
        /// <list type="bullet">
        /// <item>
        /// <description><para><paramref name="connection"/> or <see cref="ConnectionDetails"/> must be set for third-party hosting.</para></description>
        /// </item>
        /// <item>
        /// <description><para><see cref="ConnectionDetails"/> takes precedence over <paramref name="connection"/> if both are set.</para></description>
        /// </item>
        /// </list>
        /// </remarks>
        public CreateBackfillTicketOptions(
            string queueName,
            string connection = default,
            Dictionary<string, object> attributes = default,
            BackfillTicketProperties properties = default,
            string poolId = default,
            string matchId = default
        )
        {
            QueueName = queueName;
            Connection = connection;
            Attributes = attributes;
            Properties = properties;
            PoolId = poolId;
            MatchId = matchId;
        }

        /// <summary>
        /// Name of the queue to target the backfill request.
        /// </summary>
        public string QueueName { get; set; }

        /// <summary>
        /// The IP address and port of the server creating the backfill (using the format ip:port).
        /// This property is used to assign the server the matching tickets.
        /// </summary>
        public string Connection { get; set; }

        /// <summary>
        /// Connection details used for third party hosting
        /// </summary>
        internal ConnectionDetails ConnectionDetails { get; private set; }

        /// <summary>
        /// A dictionary of attributes (number or string), indexed by the attribute name. The attributes are compared
        /// against the corresponding filters defined in the matchmaking config and used to segment the ticket
        /// population into pools. Example attributes include map, mode, platform, and build number. (Optional)
        /// </summary>
        public Dictionary<string, object> Attributes { get; set; }

        /// <summary>
        /// Properties object containing match information.
        /// </summary>
        public BackfillTicketProperties Properties { get; set; }

        /// <summary>
        /// The ID of the pool to create the backfill ticket in. Cannot be used if the <c>attributes</c>
        /// field is present. The allocation payload contains the pool ID of the match it was created in.
        /// </summary>
        public string PoolId { get; set; }

        /// <summary>
        /// The ID of the match that this backfill ticket is targeting.
        /// The match ID is contained in the allocation payload.
        /// </summary>
        public string MatchId { get; set; }

        /// <summary>
        /// Configures third-party hosting connection details using an IP address and port.
        /// </summary>
        /// <param name="ip">The IPv4 address of the hosting server.</param>
        /// <param name="port">The TCP/UDP port of the hosting server.</param>
        /// <param name="data">Optional metadata to include with the connection details.</param>
        /// <returns>
        /// The current <see cref="CreateBackfillTicketOptions"/> instance for chaining.
        /// </returns>
        public CreateBackfillTicketOptions WithIpPortConnection(string ip, uint port, Dictionary<string, object> data = null)
        {
            var details = new IpPortConnectionDetails("IpPort", ip: ip, port: (int)port, data);
            ConnectionDetails = new ConnectionDetails(details, typeof(IpPortConnectionDetails));
            return this;
        }

        /// <summary>
        /// Configures custom third-party hosting connection details using arbitrary metadata.
        /// </summary>
        /// <param name="data">
        /// Optional metadata to include with the connection details.
        /// </param>
        /// <returns>
        /// The current <see cref="CreateBackfillTicketOptions"/> instance for chaining.
        /// </returns>
        public CreateBackfillTicketOptions WithCustomConnection(Dictionary<string, object> data = null)
        {
            var details = new CustomConnectionDetails("Custom", data);
            ConnectionDetails = new ConnectionDetails(details, typeof(CustomConnectionDetails));
            return this;
        }
    }
}
