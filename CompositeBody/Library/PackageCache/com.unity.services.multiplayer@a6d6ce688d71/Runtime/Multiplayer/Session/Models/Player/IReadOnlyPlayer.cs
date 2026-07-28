using System;
using System.Collections.Generic;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// A read only interface representing a player in the session.
    /// </summary>
    public interface IReadOnlyPlayer
    {
        /// <summary>
        /// The unique identifier for the player.  If not provided for a create or join request, it will be set to the ID of the caller.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The allocation id
        /// </summary>
        public string AllocationId { get; }

        /// <summary>
        /// Custom game-specific properties that apply to an individual player (e.g. &#x60;role&#x60; or &#x60;skill&#x60;).
        /// </summary>
        public IReadOnlyDictionary<string, PlayerProperty> Properties { get; }

        /// <summary>
        /// The time at which the member joined the Session.
        /// </summary>
        public DateTime Joined { get; }

        /// <summary>
        /// The last time the metadata for this member was updated.
        /// </summary>
        public DateTime LastUpdated { get; }

        /// <summary>
        /// Reference to the session the player is in.
        /// </summary>
        internal ISession Session { get; }
    }
}
