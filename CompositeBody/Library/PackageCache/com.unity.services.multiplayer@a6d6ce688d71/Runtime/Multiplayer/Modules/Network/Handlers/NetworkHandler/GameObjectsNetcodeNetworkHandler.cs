#if GAMEOBJECTS_NETCODE_AVAILABLE
using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.DistributedAuthority;
using UnityEngine;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// An <see cref="INetworkHandler"/> for Netcode for GameObjects.
    /// </summary>
    class GameObjectsNetcodeNetworkHandler : INetworkHandler
    {
        readonly IActionScheduler m_ActionScheduler;
        const string k_EnclosingType = nameof(GameObjectsNetcodeNetworkHandler);

        NetworkManagerSession m_CurrentSession;

        public GameObjectsNetcodeNetworkHandler(IActionScheduler actionScheduler)
        {
            m_ActionScheduler = actionScheduler;
        }

        /// <summary>
        /// Starts the Netcode for GameObjects session.
        /// </summary>
        /// <param name="configuration">
        /// The <see cref="NetworkConfiguration"/>
        /// used to configure this session.
        /// </param>
        /// <returns>
        /// A <see cref="Task"/> that can be awaited. Resolves once
        /// the <see cref="NetworkManager"/> has finished connecting.
        /// </returns>
        /// <exception cref="SessionException">
        /// Thrown when the <see cref="NetworkManager"/> fails to start.
        /// </exception>
        public async Task StartAsync(NetworkConfiguration configuration)
        {
            // Using NetworkManager.Singleton directly makes
            // it clearer where the NetworkManager comes from.
            var networkManager = NetworkManager.Singleton;

            if (networkManager == null)
            {
                throw new SessionException($"Cannot start when {nameof(NetworkManager.Singleton)} is not set.",
                    SessionError.NetworkManagerNotInitialized);
            }

            // If we have a current session, nothing needs to be started.
            if (m_CurrentSession != null)
            {
                Logger.LogCallWarning(k_EnclosingType, "Session already started.");
                return;
            }

            var newSession = new NetworkManagerSession(m_ActionScheduler, networkManager, configuration.Role);

            switch (configuration.Type)
            {
                case NetworkType.Direct:
                    SetupDirect(newSession, configuration);
                    break;
                case NetworkType.Relay:
                    SetupRelay(newSession, configuration);
                    break;
                case NetworkType.DistributedAuthority:
                    SetupDistributedAuthority(newSession, configuration);
                    break;
            }

            try
            {
                await newSession.StartAsync(Application.exitCancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            // Direct connection can bind to port 0.
            // When binding to port 0 a random available port is
            // chosen by the OS. Ensure when using direct connection
            // that we update our configuration with the chosen port.
            if (configuration.Type == NetworkType.Direct)
            {
                UpdateDirectConnectPortBinding(newSession, configuration);
            }

            // If the session started successfully,
            // set it as the current session.
            m_CurrentSession = newSession;
        }

        /// <summary>
        /// Stops the Netcode for GameObjects session.
        /// </summary>
        /// <returns>
        /// A <see cref="Task"/> that can be awaited. Resolves once the
        /// <see cref="NetworkManager"/> has finished shutting down.
        /// </returns>
        public Task StopAsync()
        {
            Logger.LogCallVerbose(k_EnclosingType);

            if (m_CurrentSession == null)
            {
                Logger.LogCallWarning(k_EnclosingType, "Failed to stop session: session was never started.");
                return Task.CompletedTask;
            }

            var stoppingSession = m_CurrentSession;

            // Clear the current session as we want to forget
            // about it regardless of the result of StopAsync.
            m_CurrentSession = null;

            return stoppingSession.StopAsync(Application.exitCancellationToken);
        }

        static void SetupDirect(NetworkManagerSession session, NetworkConfiguration configuration)
        {
            var transport = session.GetUnityTransport();

            if (transport == null)
            {
                throw new SessionException($"{nameof(NetworkManager)} must have a {nameof(UnityTransport)} component.",
                    SessionError.TransportComponentMissing);
            }

            Logger.LogCallVerboseWithMessage(k_EnclosingType,
                $"Publish Address: {configuration.DirectNetworkPublishAddress} - Listen Address: {configuration.DirectNetworkListenAddress}");

#if GAMEOBJECTS_NETCODE_2_8_AVAILABLE
            transport.SetConnectionData(
                true,
                configuration.DirectNetworkPublishAddress.ToFixedStringNoPort().ToString(),
                configuration.DirectNetworkPublishAddress.Port,
                configuration.DirectNetworkListenAddress.ToFixedStringNoPort().ToString());
#else
            transport.SetConnectionData(configuration.DirectNetworkPublishAddress, configuration.DirectNetworkListenAddress);
#endif
        }

        static void UpdateDirectConnectPortBinding(NetworkManagerSession session, NetworkConfiguration configuration)
        {
            if (configuration.Role != NetworkRole.Client && configuration.DirectNetworkListenAddress.Port == 0)
            {
#if GAMEOBJECTS_NETCODE_2_AVAILABLE
                // No need to null check here as the transport
                // will be checked inside SetupDirect.
                var transport = session.GetUnityTransport();
                var localEndpoint = transport.GetLocalEndpoint();

                Logger.LogCallVerboseWithMessage(k_EnclosingType, $"LocalEndpoint {localEndpoint}.");

                configuration.UpdatePublishPort(localEndpoint.Port);
#else
                throw new SessionException("Listening port 0 requires Netcode for GameObjects 2.0.0; " +
                    "change the port to a non-zero value or upgrade netcode package to 2.0.0 or newer.", SessionError.Unknown);
#endif
            }
        }

        static void SetupRelay(NetworkManagerSession session, NetworkConfiguration configuration)
        {
            var role = configuration.Role;

            // Always start Relay sessions with the user as a Host.
            // TODO: This seems like a bug - Dedicated game servers
            if (role == NetworkRole.Server)
            {
                session.SetNetworkRole(NetworkRole.Host);
            }

            var transport = session.GetUnityTransport();

            if (transport == null)
            {
                throw new SessionException($"{nameof(NetworkManager)} must have a {nameof(UnityTransport)} component.",
                    SessionError.TransportComponentMissing);
            }

            // TODO: make UTP set the connection protocol and secure settings returned from the relay server data
            transport.SetRelayServerData(configuration.RelayServerData);
        }

        static void SetupDistributedAuthority(NetworkManagerSession session, NetworkConfiguration configuration)
        {
#if GAMEOBJECTS_NETCODE_2_AVAILABLE
            // It is only valid to be a client in a DA session.
            session.SetNetworkRole(NetworkRole.Client);

            // Since you can have more than one NetworkTransport
            // component attached to a NetworkManager, we want to get the
            // NetworkTransport currently assigned to the NetworkManager.
            var transport = session.GetDistributedAuthorityTransport();

            // If the currently assigned transport is not the DaTransport.
            if (!transport)
            {
                // Get the session's networkManager to do the following changes.
                var networkManager = session.NetworkManager;

                // Check and see if the NetworkManager's GameObject
                // has the DistributedAuthorityTransport component.
                transport = networkManager.GetComponent<DistributedAuthorityTransport>();
                if (!transport)
                {
                    // If it does not, then add the
                    // DistributedAuthorityTransport
                    // component to the NetworkTransport.
                    transport = networkManager.gameObject.AddComponent<DistributedAuthorityTransport>();
                }

                session.SetTransport(transport);
            }

            transport.ConnectPayload = configuration.DistributedAuthorityConnectionPayload;
            transport.SetRelayServerData(configuration.RelayServerData);

            session.ConfigureForDistributedAuthority();
#else
            throw new SessionException("Distributed Authority requires Netcode For GameObjects 2.0", SessionError.NetworkManagerStartFailed);
#endif
        }
    }
}

#endif
