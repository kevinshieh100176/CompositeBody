using Unity.Services.Authentication.Internal;
using Unity.Services.Core.Internal;
using Unity.Services.Core.Telemetry.Internal;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Http;
using Unity.Services.Lobbies.Internal;
using Unity.Services.Vivox.Internal;
using Unity.Services.Wire.Internal;

namespace Unity.Services.Multiplayer
{
    partial class MultiplayerInitializer : IInitializablePackageV2
    {
        /// <param name="playerId">Use null for UNITY_SERVER</param>
        /// <param name="serviceID">Use null for !UNITY_SERVER</param>
        internal static WrappedLobbyService InitializeLobbyService(
            IAccessToken accessToken,
            IEnvironmentId environmentId,
            IMetricsFactory metricsFactory,
            IPlayerId playerId,
            IServiceID serviceID,
            IVivox vivox,
            IWire wire,
            string cloudEnvironment,
            IUserAgentProvider userAgentProvider)
        {
            var httpClient = new HttpClient();

            if (wire == null)
            {
                Logger.LogWarning($"The {nameof(IWire)} component is not available. LobbyEvents functionality unavailable.");
            }

            var metrics = metricsFactory.Create(k_PackageName);

            var internalService = new InternalLobbyService(httpClient, accessToken, wire, metrics, cloudEnvironment, userAgentProvider);
            var service = new WrappedLobbyService(internalService, playerId, serviceID);

#if UGS_LOBBY_VIVOX
            if (vivox == null)
            {
                Logger.LogWarning($"Version define UGS_LOBBY_VIVOX is defined, but the {nameof(IVivox)} component is not available. This means you do not have the Vivox package in your project!");
            }
            else
            {
                vivox.RegisterTokenProvider(new LobbyVivoxTokenProvider(service, environmentId));
            }
#endif

            return service;
        }
    }
}
