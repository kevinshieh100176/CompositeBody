using Unity.Services.Vivox;

namespace Unity.Services.Core
{
    /// <summary>
    /// Vivox extension methods
    /// </summary>
    public static class VivoxServicesExtensions
    {
        /// <summary>
        /// Unity Core service extension to retrieve the Vivox service from a core service instance.
        /// </summary>
        /// <param name="unityServices">The core services instance</param>
        /// <returns>The Vivox service instance</returns>
        public static IVivoxService GetVivoxService(this IUnityServices unityServices)
        {
            return unityServices.GetService<IVivoxService>();
        }
    }
}
