using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.DistributedAuthority.Apis.DistributedAuthority;
using Unity.Services.DistributedAuthority.DistributedAuthority;
using Unity.Services.DistributedAuthority.ErrorMitigation;
using Unity.Services.DistributedAuthority.Exceptions;
using Unity.Services.DistributedAuthority.Http;
using Unity.Services.DistributedAuthority.Internal;
using Unity.Services.DistributedAuthority.Models;
using Unity.Services.DistributedAuthority.SDK;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Multiplayer;
using Unity.Services.Qos.Internal;
using Unity.Services.Relay;
using Session = Unity.Services.DistributedAuthority.Models.Session;

namespace Unity.Services.DistributedAuthority
{
    /// <summary>
    /// This class allows a game client to interact with the Unity Distributed Authority Service.
    /// </summary>
    class WrappedDistributedAuthorityService : IDistributedAuthoritySDKConfiguration, IDistributedAuthorityService
    {
        private const string QosRelayServiceName = "relay";

        Configuration Configuration { get; }

        readonly IRetryPolicyProvider m_RetryPolicyProvider;
        readonly IClock m_Clock;
        readonly IInternalDaLobbyService m_LobbyService;
        readonly IRelayService m_RelayService;
        readonly IQosResults m_QosResults;

        readonly IDistributedAuthorityApiClient m_ApiClient;

        internal WrappedDistributedAuthorityService(
            IDistributedAuthorityApiClient apiClient,
            IRetryPolicyProvider retryPolicyProvider,
            IClock clock,
            Configuration configuration,
            IInternalDaLobbyService internalLobbyService,
            IRelayService relayService,
            IQosResults qosResults)
        {
            m_ApiClient = apiClient;
            m_RetryPolicyProvider = retryPolicyProvider;
            m_Clock = clock;
            Configuration = configuration;
            m_LobbyService = internalLobbyService;
            m_RelayService = relayService;
            m_QosResults = qosResults;
        }

        public void SetBasePath(string basePath)
        {
            Configuration.BasePath = basePath;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">Returned when the lobby ID is not valid.</exception>
        public async Task<Session> CreateSessionForLobbyIdAsync(string lobbyId, string region = null)
        {
            if (string.IsNullOrEmpty(lobbyId))
            {
                throw new ArgumentException("Lobby ID cannot be null or empty.", nameof(lobbyId));
            }

            if (string.IsNullOrEmpty(region) && m_QosResults != null && m_RelayService != null)
            {
                try
                {
                    var regions = (await m_RelayService.ListRegionsAsync()).Select(r => r.Id).ToList();
                    var qosResults =
                        await m_QosResults.GetSortedQosResultsAsync(QosRelayServiceName, regions);
                    // pick first region in the sorted list (best latency + packet loss)
                    if (qosResults.Any())
                    {
                        region = qosResults[0].Region;
                        Logger.LogVerbose($"best region is {region}");
                    }
                    else
                    {
                        Logger.LogWarning($"No Qos region selected. Will use default.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Could not do Qos region selection. Will use default.{Environment.NewLine}" +
                        $"QoS failed due to [{ex.GetType().Name}]. Reason: {ex.Message}");
                }
            }

            var request = new CreateSessionRequest(new Session(lobbyId, region));

            try
            {
                var response = await m_ApiClient.CreateSessionAsync(request);
                return response.Result;
            }
            catch (HttpException<ErrorResponseBody> e)
            {
                throw new DistributedAuthorityServiceException(e.ActualError.GetExceptionReason(), e.ActualError.GetExceptionMessage(), e);
            }
            catch (HttpException e)
            {
                if (e.Response.IsHttpError)
                {
                    throw new DistributedAuthorityServiceException(e.Response.GetExceptionReason(), e.Response.ErrorMessage, e);
                }

                if (e.Response.IsNetworkError)
                {
                    throw new DistributedAuthorityServiceException(DistributedAuthorityExceptionReason.NetworkError, e.Response.ErrorMessage);
                }

                throw new RequestFailedException((int)DistributedAuthorityExceptionReason.Unknown, "Something went wrong.", e);
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">Returned when the lobby ID is not valid and when the timeout is not a positive number.</exception>
        public async Task<string> JoinSessionForLobbyIdAsync(string lobbyId, int timeoutSeconds = 120)
        {
            if (string.IsNullOrEmpty(lobbyId))
            {
                throw new ArgumentException("Lobby Id cannot be null or empty.", nameof(lobbyId));
            }

            if (timeoutSeconds <= 0)
            {
                throw new ArgumentException("Timeout must be greater than 0.", nameof(timeoutSeconds));
            }

            try
            {
                var joinCode = await PollLobbyForJoinCodeAsync(lobbyId, timeoutSeconds);
                if (!string.IsNullOrEmpty(joinCode))
                {
                    return joinCode;
                }

                throw new DistributedAuthorityServiceException(DistributedAuthorityExceptionReason.Unknown, "No join code returned from lobby.");
            }
            catch (HttpException<ErrorResponseBody> e)
            {
                throw new DistributedAuthorityServiceException(e.ActualError.GetExceptionReason(), e.ActualError.GetExceptionMessage(), e);
            }
            catch (HttpException e)
            {
                if (e.Response.IsHttpError)
                {
                    throw new DistributedAuthorityServiceException(e.Response.GetExceptionReason(), e.Response.ErrorMessage, e);
                }

                if (e.Response.IsNetworkError)
                {
                    throw new DistributedAuthorityServiceException(DistributedAuthorityExceptionReason.NetworkError, e.Response.ErrorMessage);
                }

                throw new RequestFailedException((int)DistributedAuthorityExceptionReason.Unknown, "Something went wrong.", e);
            }
        }

        async Task<string> PollLobbyForJoinCodeAsync(string lobbyId, int timeoutSeconds)
        {
            var lobby = await m_LobbyService.JoinLobbyByIdAsync(lobbyId);
            if (TryGetJoinCode(lobby, out var joinCode))
            {
                return joinCode;
            }

            var start = m_Clock.UtcNow();
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            lobby = await m_RetryPolicyProvider
                .ForOperation(async() => await m_LobbyService.GetLobbyAsync(lobbyId))
                    .WithRetryCondition(l => Task.FromResult(ShouldRetry(l, start, timeout)))
                    .WithJitterMagnitude(0.5f)
                    .WithMaxDelayTime(2.0f)
                    .HandleException<LobbyServiceException>(ex => ex.Reason == LobbyExceptionReason.RateLimited)
                    .RunAsync();

            TryGetJoinCode(lobby, out joinCode);
            return joinCode;
        }

        bool ShouldRetry(Lobby lobby, DateTimeOffset start, TimeSpan timeout)
        {
            if (TryGetJoinCode(lobby, out var joinCode))
            {
                if (!string.IsNullOrEmpty(joinCode))
                {
                    return false;
                }

                var now = m_Clock.UtcNow();
                return now.Subtract(start) < timeout;
            }

            return true;
        }

        bool TryGetJoinCode(Lobby lobby, out string joinCode)
        {
            DataObject data = null;
            joinCode = string.Empty;
            lobby.Data?.TryGetValue(NetworkModule.SessionPropertyKey, out data);

            if (string.IsNullOrEmpty(data?.Value))
            {
                return false;
            }

            var networkMetadata = JsonConvert.DeserializeObject<NetworkMetadata>(data.Value);

            if (string.IsNullOrEmpty(networkMetadata.RelayJoinCode))
            {
                return false;
            }

            joinCode = networkMetadata.RelayJoinCode;
            return true;
        }
    }
}
