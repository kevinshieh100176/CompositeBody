using System.Collections.Generic;
using Unity.Services.Authentication.Internal;
using Unity.Services.Core.Telemetry.Internal;
using Unity.Services.Lobbies.Apis.Lobby;
using Unity.Services.Lobbies.Http;
using Unity.Services.Multiplayer;
using Unity.Services.Wire.Internal;

namespace Unity.Services.Lobbies
{
    /// <summary>
    /// InternalLobbyService
    /// </summary>
    internal class InternalLobbyService : ILobbyServiceSdk
    {
        /// <summary>
        /// Constructor for InternalLobbyService
        /// </summary>
        /// <param name="httpClient">The HttpClient for InternalLobbyService.</param>
        /// <param name="accessToken">The Authentication token for the service.</param>
        /// <param name="userAgentProvider">Provides the User-Agent header value for HTTP requests.</param>
        public InternalLobbyService(HttpClient httpClient, IAccessToken accessToken = null, IWire subscriptionFactory = null, IMetrics metrics = null, string cloudEnvironment = "production", IUserAgentProvider userAgentProvider = null)
        {
            LobbyApi = new LobbyApiClient(httpClient, accessToken);
            var basePath = GetBasePath(cloudEnvironment);
            Configuration = new Configuration(basePath, 10, 4, null);
            if (userAgentProvider != null)
            {
                Configuration.Headers ??= new Dictionary<string, string>();
                Configuration.Headers["User-Agent"] = userAgentProvider.UserAgent;
            }

            Wire = subscriptionFactory;

            Metrics = metrics;
        }

        /// <summary> Instance of ILobbyApiClient interface</summary>
        public ILobbyApiClient LobbyApi { get; set; }

        /// <summary> Configuration properties for the service.</summary>
        public Configuration Configuration { get; set; }

        /// <summary> Instance of the wire component for the service.</summary>
        public IWire Wire { get; set; }

        /// <summary> Instance of the metrics component for the service.</summary>
        public IMetrics Metrics { get; }

        private string GetBasePath(string cloudEnvironment)
        {
            if (cloudEnvironment == "staging")
            {
                return "https://lobby-stg.services.api.unity.com/v1";
            }

            return "https://lobby.services.api.unity.com/v1";
        }
    }
}
