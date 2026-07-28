using System;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Session connector focused events.
    /// </summary>
    [Serializable]
    public sealed class SessionConnectorEvents
    {
        /// <summary>
        /// Occurs when <see cref="SessionConnector.Execute()"/> runs.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the session identifier for the operation.
        /// </remarks>
        [Tooltip("Invoked when Execute() is called. Parameter is the session identifier.")]
        public UnityEvent<string> ExecutionStarted = new UnityEvent<string>();

        /// <summary>
        /// Occurs when the connector operation completes successfully.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive the hosted or joined session.
        /// </remarks>
        [Tooltip("Invoked when the operation completes successfully. Parameter is the session (host or joined).")]
        public UnityEvent<ISession> SuccessfulExecution = new UnityEvent<ISession>();

        /// <summary>
        /// Occurs when the connector operation fails.
        /// </summary>
        /// <remarks>
        /// Listener callbacks receive a description of the failure.
        /// </remarks>
        [Tooltip("Invoked when the operation fails. Parameter is the error message.")]
        public UnityEvent<string> FailedExecution = new UnityEvent<string>();
    }
}
