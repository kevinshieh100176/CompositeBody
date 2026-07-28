using System;
using UnityEngine;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// How to identify the session when joining.
    /// </summary>
    internal enum JoinSessionMode
    {
        ById,
        ByCode
    }

    /// <summary>
    /// Settings for joining a session.
    /// </summary>
    [Serializable]
    internal sealed class JoinSessionOptions
    {
        [Tooltip("Join by Session ID or by Join Code.")]
        [SerializeField]
        private JoinSessionMode m_JoinMode = JoinSessionMode.ById;

        [Tooltip("Session ID to join. Used when Join Mode is By Id.")]
        [SerializeField]
        private string m_SessionId = string.Empty;

        [Tooltip("Join code to join. Used when Join Mode is By Code.")]
        [SerializeField]
        private string m_SessionCode = string.Empty;

        [Tooltip("Optional password. Leave empty if the session has no password.")]
        [SerializeField]
        private string m_Password = string.Empty;

        /// <summary>
        /// Join by ID or by code.
        /// </summary>
        public JoinSessionMode JoinMode { get => m_JoinMode; set => m_JoinMode = value; }

        /// <summary>
        /// Session ID when joining by ID.
        /// </summary>
        public string SessionId { get => m_SessionId; set => m_SessionId = value ?? string.Empty; }

        /// <summary>
        /// Join code when joining by code.
        /// </summary>
        public string SessionCode { get => m_SessionCode; set => m_SessionCode = value ?? string.Empty; }

        /// <summary>
        /// The password. Mandatory if it was provided when creating the session.
        /// </summary>
        public string Password { get => m_Password; set => m_Password = value ?? string.Empty; }
    }
}
