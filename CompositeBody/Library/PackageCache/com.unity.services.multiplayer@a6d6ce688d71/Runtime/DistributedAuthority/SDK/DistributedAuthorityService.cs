using System.Threading.Tasks;
using Unity.Services.DistributedAuthority;
using Unity.Services.DistributedAuthority.Models;
using Unity.Services.Core;

namespace Unity.Services.DistributedAuthority
{
    /// <summary>
    /// Definition of the Distributed Authority service.
    /// </summary>
    internal interface IDistributedAuthorityService
    {
        /// <summary>
        /// Async Operation.
        /// Creates a session for associated to the provided lobby.
        /// The region provided will be used to allocate a relay, otherwise the default region is used.
        /// </summary>
        /// <param name="lobbyId">Identifier of the lobby to associate with the session.</param>
        /// <param name="region">Preferred relay region for the session (optional).</param>
        /// <returns>Session information.</returns>
        /// <exception cref="System.ArgumentException">Represents an error that occur when an argument is incorrectly setup.</exception>
        /// <exception cref="Unity.Services.DistributedAuthority.Exceptions.DistributedAuthorityServiceException">An exception containing the HttpClientResponse with headers, response code, and string of error.</exception>
        Task<Session> CreateSessionForLobbyIdAsync(string lobbyId, string region = null);

        /// <summary>
        /// Async Operation.
        /// Obtain the Relay Join Code from the lobby associated to the session.
        /// The join code can later be used by the Relay SDK in order to join the
        /// allocation and configure the client's network transport.
        /// </summary>
        /// <param name="lobbyId">Identifier of the lobby associated with the session.</param>
        /// <param name="timeoutSeconds">Number of seconds before timing out the join process (optional).</param>
        /// <returns>Relay Join Code needed to connect to session.</returns>
        /// <exception cref="System.ArgumentException">Represents an error that occurs when an argument is incorrectly set up.</exception>
        /// <exception cref="Unity.Services.DistributedAuthority.Exceptions.DistributedAuthorityServiceException">An exception containing the HttpClientResponse with headers, response code, and string of error.</exception>
        Task<string> JoinSessionForLobbyIdAsync(string lobbyId, int timeoutSeconds = 120);
    }

    /// <summary>
    /// The entry point of the Distributed Authority package. Once initialized, you can use the Instance singleton.
    /// features.
    /// </summary>
    internal static class DistributedAuthorityService
    {
        const string k_InitializationErrorMessage =
            "Singleton is not initialized. Please call UnityServices.InitializeAsync() to initialize.";

        static IDistributedAuthorityService s_Instance;

        /// <summary>
        /// The default singleton instance to access the Distributed Authority service.
        /// </summary>
        /// <exception cref="ServicesInitializationException">
        /// This exception is thrown if the <code>UnityServices.InitializeAsync()</code>
        /// has not finished before accessing the singleton.
        /// </exception>
        public static IDistributedAuthorityService Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    throw new ServicesInitializationException(k_InitializationErrorMessage);
                }

                return s_Instance;
            }

            internal set => s_Instance = value;
        }
    }
}
