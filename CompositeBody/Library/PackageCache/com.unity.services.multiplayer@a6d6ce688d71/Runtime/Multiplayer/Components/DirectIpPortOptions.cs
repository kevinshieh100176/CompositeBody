using System;
using UnityEngine;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Options for direct IP/port connections.
    /// </summary>
    [Serializable]
    internal sealed class DirectIPPortOptions
    {
        [Tooltip("Address that clients should use when connecting.")]
        [SerializeField]
        private string m_Ip = "127.0.0.1";

        [Tooltip("Port to listen on (0 = random available port).")]
        [SerializeField]
        private int m_Port;

        [Tooltip("Listen for incoming connections at this address (e.g. 127.0.0.1, or 0.0.0.0 for all interfaces).")]
        [SerializeField]
        private string m_ListenIp = "127.0.0.1";

        /// <summary>
        /// Address clients use to connect.
        /// </summary>
        public string Ip { get => m_Ip; set => m_Ip = value ?? "127.0.0.1"; }

        /// <summary>
        /// Port to listen on. Use 0 for a random port.
        /// </summary>
        public int Port
        {
            get => m_Port;
            set => m_Port = value < 0 ? 0 : (value > 65535 ? 65535 : value);
        }

        /// <summary>
        /// Address to listen on for incoming connections.
        /// </summary>
        public string ListenIpAddress { get => m_ListenIp; set => m_ListenIp = value ?? "127.0.0.1"; }

        /// <summary>
        /// Listen address used at runtime.
        /// </summary>
        public string ListenIp => string.IsNullOrWhiteSpace(m_ListenIp) ? "127.0.0.1" : m_ListenIp.Trim();

        /// <summary>
        /// Address for clients to connect to.
        /// </summary>
        public string PublishIp => string.IsNullOrWhiteSpace(m_Ip) ? "127.0.0.1" : m_Ip.Trim();
    }
}
