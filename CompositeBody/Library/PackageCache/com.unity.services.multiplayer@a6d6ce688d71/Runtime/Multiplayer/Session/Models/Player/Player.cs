using System;
using System.Collections.Generic;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Information about a specific member creating, joining, or already in a Session.
    /// </summary>
    class Player : IPlayer
    {
        /// <summary>
        /// Information about a specific member creating, joining, or already in a Session.
        /// </summary>
        /// <param name="session">The session the player is part of.</param>
        /// <param name="id">The unique identifier for the session member.  If not provided for a create or join request, it will be set to the ID of the caller.</param>
        /// <param name="connectionInfo">Connection information for connecting to a relay with this player.</param>
        /// <param name="properties">Custom game-specific properties that apply to an individual player (e.g. &#x60;role&#x60; or &#x60;skill&#x60;).</param>
        /// <param name="allocationId">The &#x60;allocationId&#x60; from the Relay service which associates this member in this Session with a persistent connection.  When a disconnect notification is received, this value is used to identify the associated member in a Session to mark them as disconnected.</param>
        /// <param name="joined">The time at which the player joined the Session.</param>
        /// <param name="lastUpdated">The last time the metadata for this player was updated.</param>
        internal Player(ISession session, string id = default, string connectionInfo = default,
                        Dictionary<string, PlayerProperty> properties = default, string allocationId = default, DateTime joined = default,
                        DateTime lastUpdated = default)
        {
            Session = session;
            Id = id;
            ConnectionInfo = connectionInfo;
            Properties = properties;
            AllocationId = allocationId;
            Joined = joined;
            LastUpdated = lastUpdated;
        }

        /// <summary>
        /// Reference to the session the player is in.
        /// </summary>
        ISession IReadOnlyPlayer.Session => Session;

        /// <summary>
        /// Reference to the session the player is in.
        /// </summary>
        public ISession Session { get; internal set; }

        /// <summary>
        /// Whether there are pending local modifications.
        /// </summary>
        public bool Modified { get; internal set; }

        /// <summary>
        /// The unique identifier for the player.  If not provided for a create or join request, it will be set to the ID of the caller.
        /// </summary>
        public string Id { get; internal set; }

        /// <summary>
        /// Connection information for connecting to a relay with this player.
        /// </summary>
        public string ConnectionInfo { get; internal set; }

        /// <summary>
        /// The &#x60;allocationId&#x60; from the Relay service which associates this player in this Session with a persistent connection.  When a disconnect notification is received, this value is used to identify the associated member in a Session to mark them as disconnected.
        /// </summary>
        public string AllocationId { get; internal set; }

        /// <summary>
        /// The time at which the member joined the Session.
        /// </summary>
        public DateTime Joined { get; internal set; }

        /// <summary>
        /// The last time the metadata for this member was updated.
        /// </summary>
        public DateTime LastUpdated { get; internal set; }

        /// <summary>
        /// Custom game-specific properties that apply to an individual player (e.g. &#x60;role&#x60; or &#x60;skill&#x60;).
        /// </summary>
        IReadOnlyDictionary<string, PlayerProperty> IReadOnlyPlayer.Properties => Properties;

        public Dictionary<string, PlayerProperty> Properties { get; internal set; } = new();

        public void SetAllocationId(string allocationId)
        {
            AllocationId = allocationId;
            Modified = true;
        }

        public void SetProperties(Dictionary<string, PlayerProperty> properties)
        {
            Logger.LogVerbose($"Player[{Id}].SetProperties: ({properties.Count})");

            if (properties == null || properties.Count == 0)
                return;

            foreach (var property in properties)
            {
                Properties[property.Key] = property.Value;
            }

            Modified = true;
        }

        public void SetProperty(string key, PlayerProperty property)
        {
            Logger.LogVerbose($"Player[{Id}].SetProperty: {key} : {property?.Value}");
            Properties[key] = property;
            Modified = true;
        }
    }
}
