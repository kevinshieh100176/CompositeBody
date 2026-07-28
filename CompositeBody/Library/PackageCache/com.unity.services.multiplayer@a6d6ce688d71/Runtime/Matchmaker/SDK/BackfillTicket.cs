using System.Collections.Generic;

namespace Unity.Services.Matchmaker.Models
{
    /// <summary>
    /// User-facing BackfillTicket model.
    /// </summary>
    public class BackfillTicket
    {
        /// <summary>
        /// Creates an instance of BackfillTicket.
        /// </summary>
        /// <param name="id">Backfill Ticket ID</param>
        /// <param name="connection">Connection address (IPv4 string format, no port)</param>
        /// <param name="attributes">Matchmaker ticket attributes</param>
        /// <param name="properties">Matchmaker properties (match info, players etc.)</param>
        public BackfillTicket(string id = default, string connection = default, Dictionary<string, object> attributes = default, BackfillTicketProperties properties = default)
        {
            Id = id;
            Connection = connection;
            Attributes = attributes;
            Properties = properties;
        }

        /// <summary>
        /// Backfill Ticket ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Connection address (IPv4 string format, no port)
        /// </summary>
        public string Connection { get; set; }

        /// <summary>
        /// Matchmaker ticket attributes
        /// </summary>
        public Dictionary<string, object> Attributes { get; set;  }

        /// <summary>
        /// Matchmaker properties (match info, players etc.)
        /// </summary>
        public BackfillTicketProperties Properties { get; set; }
    }
}
