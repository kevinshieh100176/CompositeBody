using System;

namespace Unity.Services.Matchmaker
{
    /// <summary>
    /// The entry class of the Matchmaker Ticketing Service enables clients to connect to matchmaking queues and resolve to target online server instances.
    /// </summary>
    public static class MatchmakerService
    {
        private static IMatchmakerService m_Service;
        private static readonly Configuration m_Configuration;

        static MatchmakerService()
        {
            m_Configuration = new Configuration("https://matchmaker.services.api.unity.com", 10, 4, null);
        }

        /// <summary>
        /// A static instance of the Matchmaker Ticketing Client.
        /// </summary>
        public static IMatchmakerService Instance
        {
            get
            {
                if (m_Service == null)
                {
                    throw new InvalidOperationException("Attempting to call Matchmaker Services requires initializing Core Registry. Call 'UnityServices.InitializeAsync' first!");
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
