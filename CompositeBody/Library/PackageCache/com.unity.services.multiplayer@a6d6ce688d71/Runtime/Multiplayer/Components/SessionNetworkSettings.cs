using System;
using Unity.Services.Core.Internal;
using UnityEngine;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Network configuration for sessions.
    /// </summary>
    [Serializable]
    internal sealed class SessionNetworkSettings
    {
        /// <summary>
        /// How the session is hosted.
        /// </summary>
        internal enum NetworkType
        {
            None = 0,
            Direct = 1,
            Relay = 2
        }

        [Tooltip("If true, a network session will be created when creating or joining a Session. " +
            "The Multiplayer Service will try to connect using the NetworkManager or the Entity Drivers depending on which network SDK is installed in your project.")]
        [SerializeField]
        private bool m_CreateNetwork = true;

        [Tooltip(
            "How the session is hosted: Direct IP/Port and Relay.")]
        [SerializeField]
        private NetworkType m_Network = NetworkType.Direct;

        [Tooltip("Direct IP/Port options. Used when Network Type is Direct.")]
        [SerializeField]
        [Visibility(nameof(m_Network), NetworkType.Direct)]
        private DirectIPPortOptions m_DirectIPPort = new DirectIPPortOptions();

        [Tooltip("Relay options. Used when Network Type is Relay.")]
        [SerializeField]
        [Visibility(nameof(m_Network), NetworkType.Relay)]
        private RelayOptions m_RelayOptions = new RelayOptions();

        /// <summary>
        /// When true, the session automatically connects to the specified network upon creation.
        /// </summary>
        internal bool CreateNetwork { get => m_CreateNetwork; set => m_CreateNetwork = value; }

        /// <summary>
        /// How the session is hosted.
        /// </summary>
        internal NetworkType Network { get => m_Network; set => m_Network = value; }

        /// <summary>
        /// Options for direct IP/port connections.
        /// </summary>
        internal DirectIPPortOptions DirectIPPort { get => m_DirectIPPort; set => m_DirectIPPort = value ?? new DirectIPPortOptions(); }

        /// <summary>
        /// Options for relay connections.
        /// </summary>
        internal RelayOptions RelayOptions { get => m_RelayOptions; set => m_RelayOptions = value ?? new RelayOptions(); }
    }
}
