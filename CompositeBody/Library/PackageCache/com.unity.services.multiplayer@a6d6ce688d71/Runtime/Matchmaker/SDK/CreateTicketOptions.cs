using System.Collections.Generic;

namespace Unity.Services.Matchmaker
{
    /// <summary>
    /// Parameter class for making matchmaking ticket requests.
    /// </summary>
    public class CreateTicketOptions
    {
        /// <summary>
        /// Parameterized constructor
        /// </summary>
        /// <param name="queueName">Target queue this matchmaking ticket should attempt to join. (default null)</param>
        /// <param name="attributes">Attributes / filters for determining pool to allocate. (default null)</param>
        public CreateTicketOptions(
            string queueName = default,
            Dictionary<string, object> attributes = default
        )
        {
            QueueName = queueName;
            Attributes = attributes;
        }

        /// <summary>
        /// Target queue this matchmaking ticket should attempt to join.
        /// Note: If a default queue is not defined in the matchmaking configuration, this field is required.
        /// </summary>
        public string QueueName { get; set; }

        /// <summary>
        /// Attributes / filters for determining pool to allocate.
        /// </summary>
        public Dictionary<string, object> Attributes { get; set; }
    }
}
