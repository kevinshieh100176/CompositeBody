using System.Collections.Generic;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Options for finding a match with Unity Matchmaker
    /// </summary>
    public class MatchmakerOptions
    {
        /// <summary>
        /// Name of the Matchmaker queue to use.
        /// </summary>
        public string QueueName { get; set; }

        /// <summary>
        /// Attributes to be used for the Matchmaking ticket request.
        /// </summary>
        public Dictionary<string, object> TicketAttributes { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Custom game-specific properties that apply to an individual player (e.g. &#x60;role&#x60; or &#x60;skill&#x60;).
        /// </summary>
        public Dictionary<string, PlayerProperty> PlayerProperties { get; set; } = new Dictionary<string, PlayerProperty>();
    }
}
