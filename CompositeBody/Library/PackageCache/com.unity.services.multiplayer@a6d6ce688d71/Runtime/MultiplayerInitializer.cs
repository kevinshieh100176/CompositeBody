using System.Threading.Tasks;
using Unity.Services.Authentication.Internal;
using Unity.Services.Authentication.Server.Internal;
using Unity.Services.Core.Configuration.Internal;
using Unity.Services.Core.Device.Internal;
using Unity.Services.Core.Internal;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.Core.Telemetry.Internal;
using Unity.Services.DistributedAuthority;
using Unity.Services.Lobbies;
using Unity.Services.Matchmaker;
using Unity.Services.Qos;
using Unity.Services.Qos.Internal;
using Unity.Services.Relay;
using Unity.Services.Vivox.Internal;
using Unity.Services.Wire.Internal;
using UnityEngine;

namespace Unity.Services.Multiplayer
{
    partial class MultiplayerInitializer : IInitializablePackageV2
    {
        const string k_CloudEnvironmentKey = "com.unity.services.core.cloud-environment";
        const string k_PackageName = "com.unity.services.multiplayer";
        const string k_StagingEnvironment = "staging";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            var package = new MultiplayerInitializer();
            package.Register(CorePackageRegistry.Instance);
        }

        public void Register(CorePackageRegistry registry)
        {
            registry.Register(this)
                .DependsOn<IAccessToken>()
                .DependsOn<IActionScheduler>()
                .DependsOn<ICloudProjectId>()
                .DependsOn<IEnvironmentId>()
                .DependsOn<IInstallationId>()
                .DependsOn<IMetricsFactory>()
                .DependsOn<IPlayerId>()
                .DependsOn<IPlayerNameComponent>()
                .DependsOn<IProjectConfiguration>()
                .DependsOn<IQosResults>()
                .DependsOn<IQosServiceComponent>()
                .OptionallyDependsOn<IWire>()
                .OptionallyDependsOn<IVivox>();
        }

        public Task Initialize(CoreRegistry registry)
        {
            InitializeServices(registry, true);
            return Task.CompletedTask;
        }

        public Task InitializeInstanceAsync(CoreRegistry registry)
        {
            InitializeServices(registry, false);
            return Task.CompletedTask;
        }

        void InitializeServices(CoreRegistry registry, bool globalRegistry)
        {
            var actionScheduler = registry.GetServiceComponent<IActionScheduler>();
            var cloudProjectId = registry.GetServiceComponent<ICloudProjectId>();
            var playerId = registry.GetServiceComponent<IPlayerId>();
            var playerName = registry.GetServiceComponent<IPlayerNameComponent>();
            var accessToken = registry.GetServiceComponent<IAccessToken>();
            var accessTokenObserver = registry.GetServiceComponent<IAccessTokenObserver>();

            var environmentId = registry.GetServiceComponent<IEnvironmentId>();
            var installationId = registry.GetServiceComponent<IInstallationId>();
            var metricsFactory = registry.GetServiceComponent<IMetricsFactory>();
            var projectConfiguration = registry.GetServiceComponent<IProjectConfiguration>();
            var qosResults = registry.GetServiceComponent<IQosResults>();
            var qosServiceComponent = registry.GetServiceComponent<IQosServiceComponent>();

            registry.TryGetServiceComponent<IServerAccessToken>(out var serverAccessToken);
            registry.TryGetServiceComponent<IWire>(out var wire);
            registry.TryGetServiceComponent<IVivox>(out var vivox);

            var cloudEnvironment = projectConfiguration.GetString(k_CloudEnvironmentKey);
            var userAgentProvider = new UserAgentProvider(projectConfiguration);

            var lobbyService = InitializeLobbyService(accessToken, environmentId, metricsFactory, playerId, null, vivox, wire, cloudEnvironment, userAgentProvider);
            var lobbyBuilder = new LobbyBuilder(actionScheduler, lobbyService, playerId, null, accessToken, accessTokenObserver, false);
            registry.RegisterService<ILobbyService>(lobbyService);

            var matchmakerService = InitializeMatchmakerService(accessToken, serverAccessToken, cloudProjectId, environmentId, installationId, projectConfiguration, cloudEnvironment, userAgentProvider);
            registry.RegisterService<IMatchmakerService>(matchmakerService);

            var relayService = InitializeRelayService(accessToken, projectConfiguration, qosResults, userAgentProvider);
            var relayBuilder = new RelayBuilder(relayService);
            registry.RegisterService<IRelayService>(relayService);

            var daService = InitializeDaService(accessToken, projectConfiguration, lobbyService, relayService, qosResults, actionScheduler, userAgentProvider);
            var daBuilder = new DaBuilder(relayService, daService, playerId);
            registry.RegisterService<IDistributedAuthorityService>(daService);

            var qosService = qosServiceComponent.Service;

            var moduleRegistry = new ModuleRegistry();

            var matchmakerModuleProvider = new MatchmakerProvider(actionScheduler, matchmakerService);
            var sessionQuery = new SessionQuerier(actionScheduler, lobbyService);
            var sessionManager = new SessionManager(actionScheduler, moduleRegistry, lobbyBuilder, playerId, playerName, accessTokenObserver);
            var matchmakerManager = new MatchmakerManager(sessionManager, actionScheduler, qosService, matchmakerService, playerId, accessToken, accessTokenObserver);
            var multiplayerService = new WrappedMultiplayerService(sessionQuery, sessionManager, matchmakerManager, moduleRegistry);

            var networkBuilder = new NetworkBuilder(actionScheduler);
            var networkModuleProvider = new NetworkProvider(networkBuilder, daBuilder, relayBuilder);
            var playerNameModuleProvider = new PlayerNameModuleProvider(playerName);

            moduleRegistry.RegisterModuleProvider(networkModuleProvider);
            moduleRegistry.RegisterModuleProvider(matchmakerModuleProvider);
            moduleRegistry.RegisterModuleProvider(playerNameModuleProvider);

            registry.RegisterService<IMultiplayerService>(multiplayerService);

            if (globalRegistry)
            {
                LobbyService.Instance = lobbyService;
                MatchmakerService.Instance = matchmakerService;
                RelayService.Instance = relayService;
                MultiplayerService.Instance = multiplayerService;
                DistributedAuthorityService.Instance = daService;
            }
        }
    }
}
