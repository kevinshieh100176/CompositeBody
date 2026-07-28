using System;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Session-level events.
    /// </summary>
    [Serializable]
    public sealed class SessionEvents
    {
        /// <summary>
        /// Occurs when the assigned session changes.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the current session.
        /// </remarks>
        [Tooltip("Invoked when the session has changed. Parameter is the session.")]
        public UnityEvent<ISession> Changed = new UnityEvent<ISession>();

        /// <summary>
        /// Occurs when the session host changes.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session and the new host player identifier.
        /// </remarks>
        [Tooltip(
            "Invoked when the session host has changed. First parameter is the session, second is the new host id.")]
        public UnityEvent<ISession, string> SessionHostChanged = new UnityEvent<ISession, string>();

        /// <summary>
        /// Occurs after host migration completes for the session.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the migrated session.
        /// </remarks>
        [Tooltip("Invoked when the session has migrated (host migration). Parameter is the session.")]
        public UnityEvent<ISession> SessionMigrated = new UnityEvent<ISession>();

        /// <summary>
        /// Occurs when session properties change.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session whose properties changed.
        /// </remarks>
        [Tooltip("Invoked when session properties have changed. Parameter is the session.")]
        public UnityEvent<ISession> SessionPropertiesChanged = new UnityEvent<ISession>();

        /// <summary>
        /// Occurs when the session state changes.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session and the new <see cref="SessionState"/>.
        /// </remarks>
        [Tooltip(
            "Invoked when the session state has changed. First parameter is the session, second is the new state.")]
        public UnityEvent<ISession, SessionState> StateChanged = new UnityEvent<ISession, SessionState>();
    }
}
