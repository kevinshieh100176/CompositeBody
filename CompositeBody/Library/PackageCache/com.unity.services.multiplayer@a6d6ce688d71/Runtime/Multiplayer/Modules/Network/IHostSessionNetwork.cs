using System;
using System.Threading.Tasks;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// A handle to the network handler used by the Host's Session.
    /// </summary>
    public interface IHostSessionNetwork
    {
        /// <summary>
        /// State changed events will be fired when the internal state changes.
        /// </summary>
        event Action<NetworkState> StateChanged;

        /// <summary>
        /// Raised when the operation to start the network fails
        /// </summary>
        event Action<SessionError> StartFailed;

        /// <summary>
        /// Raised when the operation to stop the network fails
        /// </summary>
        event Action<SessionError> StopFailed;

        /// <summary>
        /// Raised when the operation to migrate the network fails
        /// </summary>
        event Action<SessionError> MigrationFailed;

        /// <summary>
        /// State of the underlying network handler.
        /// </summary>
        NetworkState State { get; }

        /// <summary>
        /// A handler that configures the network as a session start and stops.
        /// </summary>
        INetworkHandler NetworkHandler { get; set; }

        /// <summary>
        /// Starts the session associated network as a direct network using
        /// the provided options.
        /// </summary>
        /// <param name="networkOptions">
        /// The options used to configure the direct network,
        /// including port settings and other network parameters.
        /// </param>
        /// <returns>
        /// A task that will be completed when the network is started.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="networkOptions"/> is <c>null</c>.
        /// </exception>
        Task StartDirectNetworkAsync(DirectNetworkOptions networkOptions);

        /// <summary>
        /// Starts the session associated network as a relay network.
        /// </summary>
        /// <param name="networkOptions">
        /// The options used to configure the relay network.
        /// </param>
        /// <returns>
        /// A task that will be completed when the network is started.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="networkOptions"/> is <c>null</c>.
        /// </exception>
        Task StartRelayNetworkAsync(RelayNetworkOptions networkOptions);

#if GAMEOBJECTS_NETCODE_2_AVAILABLE || PACKAGE_DOCS_GENERATION
        /// <summary>
        /// Starts the session associated network as a distributed authority
        /// network.
        /// </summary>
        /// <param name="networkOptions">
        /// The options used to configure the relay network.
        /// </param>
        /// <returns>
        /// A task that will be completed when the network is started.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="networkOptions"/> is <c>null</c>.
        /// </exception>
        Task StartDistributedAuthorityNetworkAsync(RelayNetworkOptions networkOptions);
#endif

        /// <summary>
        /// Stops the network.
        /// </summary>
        /// <returns>
        /// A task that will be completed after the network has been shut down.
        /// </returns>
        Task StopNetworkAsync();

        /// <summary>
        /// The current network info
        /// </summary>
        internal NetworkInfo NetworkInfo { get; }
    }
}
