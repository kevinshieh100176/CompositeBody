using Unity.Services.Authentication.Internal;
using Unity.Services.Core.Configuration.Internal;
using Unity.Services.Core.Internal;
using Unity.Services.Qos.Internal;
using Unity.Services.Relay;
using Unity.Services.Relay.Http;

namespace Unity.Services.Multiplayer
{
    partial class MultiplayerInitializer : IInitializablePackageV2
    {
        internal static WrappedRelayService InitializeRelayService(IAccessToken accessToken, IProjectConfiguration projectConfiguration, IQosResults qosResults, IUserAgentProvider userAgentProvider)
        {
            var httpClient = new HttpClient();
            var internalService = new InternalRelayService(httpClient, projectConfiguration, accessToken, qosResults, userAgentProvider);
            var relayService = new WrappedRelayService(internalService);
            return relayService;
        }
    }
}
