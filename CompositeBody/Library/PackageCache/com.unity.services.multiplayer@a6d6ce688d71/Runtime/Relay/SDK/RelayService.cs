using System;

namespace Unity.Services.Relay
{
    /// <summary>
    /// The entry class of the Relay Allocations Service enables clients to connect to relay servers. Once connected, they are able to communicate with each other, via the relay servers, using the bespoke relay binary protocol.
    /// </summary>
    public static class RelayService
    {
        private static IRelayService m_Service;

        /// <summary>
        /// A static instance of the Relay Allocation Client.
        /// </summary>
        public static IRelayService Instance
        {
            get
            {
                if (m_Service == null)
                {
                    throw new InvalidOperationException("Attempting to call Relay Services requires initializing Core Registry. Call 'UnityServices.InitializeAsync' first!");
                }

                return m_Service;
            }
            internal set
            {
                m_Service = value;
            }
        }
    }
}
