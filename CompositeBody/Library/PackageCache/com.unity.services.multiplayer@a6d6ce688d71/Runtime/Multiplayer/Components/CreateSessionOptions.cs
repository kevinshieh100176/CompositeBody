using System;
using UnityEngine;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Settings for creating a session (capacity, name, privacy, lock, password).
    /// </summary>
    /// <seealso cref="SessionConnector.SessionOptions"/>
    [Serializable]
    internal class CreateSessionOptions
    {
        private const int k_DefaultMaxPlayers = 4;

        [Tooltip("Maximum number of players that can join the session. Use 0 or less for default (4).")]
        [SerializeField]
        private int m_MaxPlayers = k_DefaultMaxPlayers;

        [Tooltip("Display name for the session. Leave empty to use a generated ID.")]
        [SerializeField]
        private string m_SessionName = string.Empty;

        [Tooltip("If true, the session is hidden from public listings (invite-only).")]
        [SerializeField]
        private bool m_IsPrivate;

        [Tooltip("If true, the session does not accept new players.")]
        [SerializeField]
        private bool m_IsLocked;

        [Tooltip("Optional password. Leave empty for no password.")]
        [SerializeField]
        private string m_Password = string.Empty;

        /// <summary>
        /// The maximum number of players that can join the session.
        /// </summary>
        public int MaxPlayers { get => m_MaxPlayers; set => m_MaxPlayers = value; }

        /// <summary>
        /// The display name for the session.
        /// </summary>
        public string SessionName { get => m_SessionName; set => m_SessionName = value ?? string.Empty; }

        /// <summary>
        /// Whether the session is private (invite-only, hidden from public listings).
        /// </summary>
        public bool IsPrivate { get => m_IsPrivate; set => m_IsPrivate = value; }

        /// <summary>
        /// Whether the session is locked (no new players can join).
        /// </summary>
        public bool IsLocked { get => m_IsLocked; set => m_IsLocked = value; }

        /// <summary>
        /// The optional password for the session. Empty or null means no password.
        /// </summary>
        public string Password { get => m_Password; set => m_Password = value ?? string.Empty; }
    }
}
