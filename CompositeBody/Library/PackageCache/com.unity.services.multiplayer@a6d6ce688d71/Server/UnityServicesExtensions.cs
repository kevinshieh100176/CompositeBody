using Unity.Services.Multiplayer;

namespace Unity.Services.Core
{
    /// <summary>
    /// Unity services extensions
    /// </summary>
    public static class UnityServicesExtensions
    {
        /// <summary>
        /// Retrieve the multiplayer server service from the core service registry
        /// </summary>
        /// <param name="unityServices">The core services instance</param>
        /// <returns>The multiplayer server service instance</returns>
        public static IMultiplayerServerService GetMultiplayerServerService(this IUnityServices unityServices)
        {
            return unityServices.GetService<IMultiplayerServerService>();
        }
    }
}
