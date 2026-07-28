using System.Threading.Tasks;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// A handler that configures the network as a session start and stops.
    /// </summary>
    /// <remarks>
    /// Implement this interface and specify it using <see cref="SessionOptionsExtensions.WithNetworkHandler{T}(T, INetworkHandler)"/>
    /// to use sessions with a custom netcode solution, or to disable the
    /// default integration provided for Netcode for GameObjects and Netcode for
    /// Entities.
    /// </remarks>
    public interface INetworkHandler
    {
        /// <summary>
        /// Configure and start networking as a server, host, or client.
        /// </summary>
        /// <remarks>
        /// <para>Use <see cref="NetworkConfiguration.Role"/> to determine the role along with all networking details.
        /// After the returned task completes, the game should be either listening and accepting connections (host or
        /// server) or connected (client).</para>
        /// <para>For direct networking, the handler is allowed to modify the publish port <see
        /// cref="NetworkConfiguration.UpdatePublishPort"/> to handle the case of port 0.</para>
        /// </remarks>
        /// <param name="configuration">The network configuration to be used.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public Task StartAsync(NetworkConfiguration configuration);

        /// <summary>
        /// Stop networking.
        /// </summary>
        /// <remarks>
        /// Use the <see cref="NetworkConfiguration.Role"/> obtained in <see cref="StartAsync"/> to determine the role
        /// along with all networking details. After the returned task completes, the game should no longer be connected
        /// or accepting connections.
        /// </remarks>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public Task StopAsync();
    }
}
