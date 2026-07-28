using System.Threading.Tasks;
using Unity.Services.Authentication.Internal;
using Unity.Services.Authentication.Server.Internal;
using Unity.Services.Core.Configuration.Internal;
using Unity.Services.Core.Device.Internal;
using Unity.Services.Core.Internal;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.Core.Telemetry.Internal;
using Unity.Services.Qos.Internal;
using Unity.Services.Vivox.Internal;
using Unity.Services.Wire.Internal;
using UnityEngine;

namespace Unity.Services.Multiplayer
{
    partial class MultiplayerServerInitializer : IInitializablePackageV2
    {
        const string k_CloudEnvironmentKey = "com.unity.services.core.cloud-environment";
        const string k_PackageName = "com.unity.services.multiplayer";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            var package = new MultiplayerServerInitializer();
            package.Register(CorePackageRegistry.Instance);
        }

        public void Register(CorePackageRegistry registry)
        {
            registry.Register(this)
                .DependsOn<IActionScheduler>()
                .DependsOn<ICloudProjectId>()
                .DependsOn<IInstallationId>()
                .DependsOn<IMetricsFactory>()
                .DependsOn<IPlayerId>()
                .DependsOn<IProjectConfiguration>()
                .DependsOn<IQosResults>()
                .DependsOn<IServerAccessToken>()
                .DependsOn<IServerEnvironmentId>()
                .OptionallyDependsOn<IVivox>()
                .OptionallyDependsOn<IWire>();
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
            var serverAccessToken = registry.GetServiceComponent<IServerAccessToken>();
            var environmentId = registry.GetServiceComponent<IServerEnvironmentId>();
            var metricsFactory = registry.GetServiceComponent<IMetricsFactory>();
            var projectConfiguration = registry.GetServiceComponent<IProjectConfiguration>();
            var installationId = registry.GetServiceComponent<IInstallationId>();
            var qosResults = registry.GetServiceComponent<IQosResults>();
            registry.TryGetServiceComponent<IWire>(out var wire);
            registry.TryGetServiceComponent<IVivox>(out var vivox);
            var cloudEnvironment = projectConfiguration.GetString(k_CloudEnvironmentKey);
            var userAgentProvider = new UserAgentProvider(projectConfiguration);
            var serviceId = new InternalServiceID(serverAccessToken);
            var lobbyService = MultiplayerInitializer.InitializeLobbyService(serverAccessToken, environmentId, metricsFactory, null, serviceId, vivox, wire, cloudEnvironment, userAgentProvider);
            var lobbyBuilder = new LobbyBuilder(actionScheduler, lobbyService, null, serviceId, serverAccessToken, serverAccessToken, true);
            var relayService = MultiplayerInitializer.InitializeRelayService(serverAccessToken, projectConfiguration, qosResults, userAgentProvider);
            relayService.EnableQos = false;
            var relayBuilder = new RelayBuilder(relayService);
            var matchmakerService = InitializeMatchmakerService(serverAccessToken, cloudProjectId, environmentId, installationId, projectConfiguration, cloudEnvironment, userAgentProvider);
            var networkBuilder = new NetworkBuilder(actionScheduler);
            var networkProvider = new NetworkProvider(networkBuilder, null, relayBuilder);
            var matchmakerProvider = new MatchmakerProvider(actionScheduler, matchmakerService);

            var moduleRegistry = new ModuleRegistry();
            moduleRegistry.RegisterModuleProvider(networkProvider);
            moduleRegistry.RegisterModuleProvider(matchmakerProvider);

            var sessionManager = new SessionManager(actionScheduler, moduleRegistry, lobbyBuilder, null, null, serverAccessToken);

            var multiplayerServerService = new MultiplayerServerServiceImpl(sessionManager);
            registry.RegisterService<IMultiplayerServerService>(multiplayerServerService);

            if (globalRegistry)
            {
                MultiplayerServerService.Instance = multiplayerServerService;
            }
        }
    }
}
