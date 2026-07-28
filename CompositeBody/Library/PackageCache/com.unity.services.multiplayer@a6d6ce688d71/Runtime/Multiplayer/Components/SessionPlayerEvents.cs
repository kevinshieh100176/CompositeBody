using System;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Player focused events.
    /// </summary>
    [Serializable]
    public sealed class SessionPlayerEvents
    {
        /// <summary>
        /// Occurs after a player has left the session.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session and the id of the player who left.
        /// </remarks>
        [Tooltip(
            "Invoked when a player has left the session. First parameter is the session, second is the player id.")]
        public UnityEvent<ISession, string> PlayerHasLeft = new UnityEvent<ISession, string>();

        /// <summary>
        /// Occurs when a player joins the session.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session and the id of the player who joined.
        /// </remarks>
        [Tooltip(
            "Invoked when a player has joined the session. First parameter is the session, second is the player id.")]
        public UnityEvent<ISession, string> PlayerJoined = new UnityEvent<ISession, string>();

        /// <summary>
        /// Occurs when a player is leaving the session (before removal completes).
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session and the id of the player who is leaving.
        /// </remarks>
        [Tooltip(
            "Invoked when a player is leaving the session. First parameter is the session, second is the player id.")]
        public UnityEvent<ISession, string> PlayerLeaving = new UnityEvent<ISession, string>();

        /// <summary>
        /// Occurs when a player's properties change.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session whose player data changed.
        /// </remarks>
        [Tooltip("Invoked when player properties have changed. Parameter is the session.")]
        public UnityEvent<ISession> PlayerPropertiesChanged = new UnityEvent<ISession>();
    }
}
