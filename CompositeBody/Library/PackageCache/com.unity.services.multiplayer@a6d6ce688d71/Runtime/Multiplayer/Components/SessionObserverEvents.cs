using System;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Session lifecycle events.
    /// </summary>
    [Serializable]
    public sealed class SessionLifecycleEvents
    {
        /// <summary>
        /// Occurs when a create or join operation for this session type starts.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session type string (see <see cref="MultiplayerSession.SessionType"/>).
        /// </remarks>
        [Tooltip(
            "Invoked when a create or join operation for this session type has started. Parameter is the session type.")]
        public UnityEvent<string> AddingSessionStarted = new UnityEvent<string>();

        /// <summary>
        /// Occurs when a create or join operation for this session type fails.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session type string and an error message.
        /// </remarks>
        [Tooltip(
            "Invoked when a create or join operation for this session type has failed. First parameter is the session type, second is the error message.")]
        public UnityEvent<string, string> AddingSessionFailed = new UnityEvent<string, string>();

        /// <summary>
        /// Occurs when a session of this type is added (for example after create or join succeeds).
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the added session.
        /// </remarks>
        [Tooltip(
            "Invoked when a session of this type has been added (e.g. after create or join succeeds). Parameter is the session.")]
        public UnityEvent<ISession> SessionAdded = new UnityEvent<ISession>();

        /// <summary>
        /// Occurs when the local player is removed from the session (for example kicked).
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session the local player was removed from.
        /// </remarks>
        [Tooltip(
            "Invoked when the local player has been removed from the session (e.g. kicked). Parameter is the session.")]
        public UnityEvent<ISession> RemovedFromSession = new UnityEvent<ISession>();

        /// <summary>
        /// Occurs when the session is deleted.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the deleted session.
        /// </remarks>
        [Tooltip("Invoked when the session has been deleted. Parameter is the session.")]
        public UnityEvent<ISession> Deleted = new UnityEvent<ISession>();
    }
}
