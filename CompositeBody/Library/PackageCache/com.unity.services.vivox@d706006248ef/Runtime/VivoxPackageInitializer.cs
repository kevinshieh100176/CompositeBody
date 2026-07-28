using System.Threading.Tasks;
using Unity.Services.Authentication.Internal;
using Unity.Services.Core.Configuration.Internal;
using Unity.Services.Core.Internal;
using Unity.Services.Vivox.Internal;
using UnityEngine;

namespace Unity.Services.Vivox
{
#if !UNITY_STANDALONE_LINUX
    class VivoxPackageInitializer : IInitializablePackageV2
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeOnLoad()
        {
            var package = new VivoxPackageInitializer();
            package.Register(CorePackageRegistry.Instance);
        }

        public void Register(CorePackageRegistry registry)
        {
            registry.Register(this)
                .ProvidesComponent<IVivox>()
                .DependsOn<IProjectConfiguration>()
                .OptionallyDependsOn<IPlayerId>()
                .OptionallyDependsOn<IAccessToken>()
                .OptionallyDependsOn<IPlayerId>()
                .OptionallyDependsOn<IEnvironmentId>();
        }

        public Task Initialize(CoreRegistry registry)
        {
            VivoxService.Instance = InitializeService(registry);
            return Task.CompletedTask;
        }

        public Task InitializeInstanceAsync(CoreRegistry registry)
        {
            InitializeService(registry);
            return Task.CompletedTask;
        }

        IVivoxService InitializeService(CoreRegistry registry)
        {
            try
            {
                var projectConfigComponent = registry.GetServiceComponent<IProjectConfiguration>();
                var playerIdComponent = registry.GetServiceComponent<IPlayerId>();
                var accessTokenComponent = registry.GetServiceComponent<IAccessToken>();
                var environmentIdComponent = registry.GetServiceComponent<IEnvironmentId>();

                var server = projectConfigComponent.GetString(VivoxServiceInternal.k_ServerKey);
                var domain = projectConfigComponent.GetString(VivoxServiceInternal.k_DomainKey);
                var issuer = projectConfigComponent.GetString(VivoxServiceInternal.k_IssuerKey);
                var token = projectConfigComponent.GetString(VivoxServiceInternal.k_TokenKey);
                var isEnvironmentCustom = projectConfigComponent.GetBool(VivoxServiceInternal.k_EnvironmentCustomKey);
                var isTestMode = projectConfigComponent.GetBool(VivoxServiceInternal.k_TestModeKey);

                var vivoxService = new VivoxServiceInternal(
                    server,
                    domain,
                    issuer,
                    token,
                    isEnvironmentCustom,
                    isTestMode,
                    accessTokenComponent,
                    playerIdComponent,
                    environmentIdComponent,
                    registry);

                registry.RegisterService<IVivoxService>(vivoxService);
                var vivoxComponent = new VivoxComponent(vivoxService);
                registry.RegisterServiceComponent<IVivox>(vivoxComponent);
                return vivoxService;
            }
            catch
            {
                VivoxLogger.LogError($"Unable to initialize Vivox. "
                    + "\nPlease ensure that a project is properly linked at \"Edit > Project Settings > Services > Vivox\" if you intend to use Unity Game Services. "
                    + "\nIf you would like to use custom credentials, you can set them by creating an InitializationOptions instance, calling SetVivoxCredentials(...) on it while providing your credentials, and passing the object into UnityServices.InitializeAsync(...)");
                throw;
            }
        }
    }
#endif
}
