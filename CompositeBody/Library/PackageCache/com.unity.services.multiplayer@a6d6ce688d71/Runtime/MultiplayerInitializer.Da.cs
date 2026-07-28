using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Services.Authentication.Internal;
using Unity.Services.DistributedAuthority;
using Unity.Services.DistributedAuthority.Apis.DistributedAuthority;
using Unity.Services.DistributedAuthority.ErrorMitigation;
using Unity.Services.DistributedAuthority.Http;
using Unity.Services.DistributedAuthority.Internal;
using Unity.Services.Core.Configuration.Internal;
using Unity.Services.Core.Internal;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.Lobbies;
using Unity.Services.Qos.Internal;
using Unity.Services.Relay;
using Configuration = Unity.Services.DistributedAuthority.Configuration;

namespace Unity.Services.Multiplayer
{
    partial class MultiplayerInitializer : IInitializablePackageV2
    {
        internal static WrappedDistributedAuthorityService InitializeDaService(IAccessToken accessToken, IProjectConfiguration projectConfiguration, ILobbyService lobbyService, IRelayService relayService, IQosResults qosResults, IActionScheduler actionScheduler, IUserAgentProvider userAgentProvider)
        {
            var httpClient = new HttpClient();
            var configuration = new Configuration(GetDaHost(projectConfiguration), 100, 4, null);
            configuration.Headers ??= new Dictionary<string, string>();
            configuration.Headers["User-Agent"] = userAgentProvider.UserAgent;
            var apiClient = new DistributedAuthorityApiClient(httpClient, accessToken, configuration);
            return new WrappedDistributedAuthorityService(
                apiClient,
                new RetryPolicyProvider(actionScheduler),
                new DefaultClock(),
                configuration,
                new InternalDaLobbyService(new Lazy<ILobbyService>(() => lobbyService, LazyThreadSafetyMode.PublicationOnly)),
                relayService,
                qosResults);
        }

        static string GetDaHost(IProjectConfiguration projectConfiguration)
        {
            var cloudEnvironment = projectConfiguration?.GetString(k_CloudEnvironmentKey);
            switch (cloudEnvironment)
            {
                case k_StagingEnvironment:
                    return "https://cmb-stg.services.api.unity.com";
                default:
                    return "https://cmb.services.api.unity.com";
            }
        }
    }
}
