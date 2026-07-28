using Unity.Services.Authentication.Internal;
using Unity.Services.Authentication.Server.Internal;
using Unity.Services.Core.Configuration.Internal;
using Unity.Services.Core.Device.Internal;
using Unity.Services.Core.Internal;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Http;

namespace Unity.Services.Multiplayer
{
    partial class MultiplayerInitializer : IInitializablePackageV2
    {
        internal static WrappedMatchmakerService InitializeMatchmakerService(
            IAccessToken accessToken,
            IServerAccessToken serverAccessToken,
            ICloudProjectId cloudProjectId,
            IEnvironmentId environmentId,
            IInstallationId installationId,
            IProjectConfiguration projectConfiguration,
            string cloudEnvironment,
            IUserAgentProvider userAgentProvider = null)
        {
            var httpClient = new HttpClient();

            var internalService = new InternalMatchmakerServiceSdk(
                httpClient,
                cloudEnvironment,
                accessToken,
                serverAccessToken,
                userAgentProvider);

            var matchmakerService = new WrappedMatchmakerService(cloudProjectId, projectConfiguration, installationId, environmentId, internalService);

            return matchmakerService;
        }
    }
}
