using System;
using System.Threading.Tasks;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// A handle to the network handler used by the Client's Session.
    /// </summary>
    public interface IClientSessionNetwork
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
        /// Starts the network using the server provided network metadata.
        /// </summary>
        /// <returns>
        /// A task that will be completed after the network has been shut down.
        /// </returns>
        internal Task StartNetworkAsync();

        /// <summary>
        /// Stops the network.
        /// </summary>
        /// <returns>
        /// A task that will be completed after the network has been shut down.
        /// </returns>
        internal Task StopNetworkAsync();
    }
}
