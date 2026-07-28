#if GAMEOBJECTS_NETCODE_2_AVAILABLE
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.Services.DistributedAuthority
{
    class DistributedAuthorityTransport : UnityTransport
    {
        internal const string DAWarning = "Detected DAHost mode. This is an unsupported configuration. Falling back to ClientServer. Please start Distributed Authority mode as client.";

        [System.NonSerialized]
        public NativeArray<byte> ConnectPayload = new(0, Allocator.Temp);

        protected override NetworkConnection Connect(NetworkEndpoint serverEndpoint)
        {
            return m_Driver.Connect(serverEndpoint, ConnectPayload);
        }

        protected override NetworkTopologyTypes OnCurrentTopology()
        {
            if (m_NetworkManager.DAHost || (m_NetworkManager.DistributedAuthorityMode && !m_NetworkManager.CMBServiceConnection))
            {
                Multiplayer.Logger.LogWarning(DAWarning);
                return NetworkTopologyTypes.ClientServer;
            }

            return base.OnCurrentTopology();
        }
    }
}
#endif
