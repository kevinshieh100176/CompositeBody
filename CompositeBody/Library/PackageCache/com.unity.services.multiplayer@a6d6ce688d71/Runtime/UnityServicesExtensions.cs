using Unity.Services.Lobbies;
using Unity.Services.Matchmaker;
using Unity.Services.Multiplayer;
using Unity.Services.Relay;

namespace Unity.Services.Core
{
    /// <summary>
    /// Unity services extensions
    /// </summary>
    public static class UnityServicesExtensions
    {
        /// <summary>
        /// Retrieve the multiplayer service from the core service registry
        /// </summary>
        /// <param name="unityServices">The core services instance</param>
        /// <returns>The multiplayer service instance</returns>
        public static IMultiplayerService GetMultiplayerService(this IUnityServices unityServices)
        {
            return unityServices.GetService<IMultiplayerService>();
        }

        /// <summary>
        /// Retrieve the lobby service from the core service registry
        /// </summary>
        /// <param name="unityServices">The core services instance</param>
        /// <returns>The lobby service instance</returns>
        public static ILobbyService GetLobbyService(this IUnityServices unityServices)
        {
            return unityServices.GetService<ILobbyService>();
        }

        /// <summary>
        /// Retrieve the relay service from the core service registry
        /// </summary>
        /// <param name="unityServices">The core services instance</param>
        /// <returns>The relay service instance</returns>
        public static IRelayService GetRelayService(this IUnityServices unityServices)
        {
            return unityServices.GetService<IRelayService>();
        }

        /// <summary>
        /// Retrieve the matchmaker service from the core service registry
        /// </summary>
        /// <param name="unityServices">The core services instance</param>
        /// <returns>The matchmaker service instance</returns>
        public static IMatchmakerService GetMatchmakerService(this IUnityServices unityServices)
        {
            return unityServices.GetService<IMatchmakerService>();
        }
    }
}
