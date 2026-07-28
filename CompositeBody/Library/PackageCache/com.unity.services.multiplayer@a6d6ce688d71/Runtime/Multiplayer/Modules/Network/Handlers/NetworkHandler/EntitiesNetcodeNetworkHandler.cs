#if ENTITIES_NETCODE_AVAILABLE && !UNITY_WEBGL
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.NetCode;
using Unity.Services.Core.Scheduler.Internal;

namespace Unity.Services.Multiplayer
{
    class EntitiesNetcodeNetworkHandler : INetworkHandler
    {
        NetworkConfiguration m_Configuration;
        World m_ClientWorld;
        World m_ServerWorld;

        NetworkStreamDriver m_ClientDriver;
        NetworkStreamDriver m_ServerDriver;
        Entity m_ConnectionEntity;

        INetworkStreamDriverConstructor m_OldDriverConstructor;
        readonly EntitiesDriverConstructor m_DriverConstructor = new EntitiesDriverConstructor();
        readonly IActionScheduler m_ActionScheduler;


        public EntitiesNetcodeNetworkHandler(IActionScheduler actionScheduler)
        {
            m_ActionScheduler = actionScheduler;
        }

        public async Task StartAsync(NetworkConfiguration configuration)
        {
            m_Configuration = configuration;

            SetupWorlds();
            ValidateWorlds();
            SetupDriverConstructor();

            switch (m_Configuration.Role)
            {
                case NetworkRole.Client:
                    await ConnectAsync();
                    break;
                case NetworkRole.Host:
                    Listen();
                    await SelfConnectAsync();
                    break;
                case NetworkRole.Server:
                    Listen();
                    break;
            }
        }

        public async Task StopAsync()
        {
            CleanupServer();
            await CleanupClientAsync();
            NetworkStreamReceiveSystem.DriverConstructor = m_OldDriverConstructor;
        }

        void SetupWorlds()
        {
            m_ClientWorld = ClientServerBootstrap.ClientWorld;
            m_ServerWorld = ClientServerBootstrap.ServerWorld;

            if ((m_Configuration.Role is NetworkRole.Client or NetworkRole.Host) && m_ClientWorld == null)
            {
                Logger.LogVerbose("No client world available. Creating one.");
                m_ClientWorld = ClientServerBootstrap.CreateClientWorld("ClientWorld");
            }

            if ((m_Configuration.Role is NetworkRole.Host or NetworkRole.Server) && m_ServerWorld == null)
            {
                Logger.LogVerbose("No server world available. Creating one.");
                m_ServerWorld = ClientServerBootstrap.CreateServerWorld("ServerWorld");
            }
        }

        void SetupDriverConstructor()
        {
            m_DriverConstructor.Configuration = m_Configuration;
            m_OldDriverConstructor = NetworkStreamReceiveSystem.DriverConstructor;
            NetworkStreamReceiveSystem.DriverConstructor = m_DriverConstructor;
        }

        void SetupClientDriver()
        {
            using var drvQuery = m_ClientWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            m_ClientDriver = drvQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW;

            using (var debugQuery = m_ClientWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetDebug>()))
            {
                var netDebug = debugQuery.GetSingleton<NetDebug>();
                var driverStore = new NetworkDriverStore();
                NetworkStreamReceiveSystem.DriverConstructor.CreateClientDriver(m_ClientWorld, ref driverStore, netDebug);
                m_ClientDriver.ResetDriverStore(m_ClientWorld.Unmanaged, ref driverStore);
            }
        }

        void SetupServerDriver()
        {
            using var drvQuery = m_ServerWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            m_ServerDriver = drvQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW;

            using (var debugQuery = m_ServerWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetDebug>()))
            {
                var netDebug = debugQuery.GetSingleton<NetDebug>();
                var driverStore = new NetworkDriverStore();
                NetworkStreamReceiveSystem.DriverConstructor.CreateServerDriver(m_ServerWorld, ref driverStore, netDebug);
                m_ServerDriver.ResetDriverStore(m_ServerWorld.Unmanaged, ref driverStore);
            }
        }

        void Listen()
        {
            ValidateWorld(m_ServerWorld);
            SetupServerDriver();

            Unity.Networking.Transport.NetworkEndpoint listenEndpoint = default;

            switch (m_Configuration.Type)
            {
                case NetworkType.Direct:
                    listenEndpoint = m_Configuration.DirectNetworkListenAddress;
                    break;
                case NetworkType.Relay:
                    listenEndpoint = Unity.Networking.Transport.NetworkEndpoint.AnyIpv4;
                    break;
            }

            if (!listenEndpoint.IsValid)
            {
                throw new SessionException("Invalid endpoint to listen to", SessionError.NetworkSetupFailed);
            }

            if (m_ServerDriver.Listen(listenEndpoint))
            {
                var serverUdpPort = m_ServerDriver.GetLocalEndPoint(m_DriverConstructor.ServerUdpDriverId).Port;
                Logger.LogVerbose($"ServerDriver[Udp]: {serverUdpPort}");

                if (m_Configuration.Type == NetworkType.Direct)
                {
                    m_Configuration.UpdatePublishPort(serverUdpPort);
                }
            }
            else
            {
                throw new SessionException("We assume the first driver created is IPC Network Interface! Check your `INetworkStreamDriverConstructor` implementation (hooked up via `NetworkStreamReceiveSystem.DriverConstructor`).",
                    SessionError.NetworkSetupFailed);
            }
        }

        async Task SelfConnectAsync()
        {
            ValidateWorld(m_ClientWorld);
            SetupClientDriver();

            var ipcPort = m_ServerDriver.GetLocalEndPoint(m_DriverConstructor.ServerIpcDriverId).Port;
            Logger.LogVerbose($"ServerDriver[Ipc]: {m_ServerDriver.GetLocalEndPoint(m_DriverConstructor.ServerIpcDriverId).Port}");

            var selfEndpoint = Unity.Networking.Transport.NetworkEndpoint.LoopbackIpv4.WithPort(ipcPort);
            Logger.LogVerbose($"ClientDriver SelfConnect: {selfEndpoint}");
            m_ConnectionEntity = m_ClientDriver.Connect(m_ClientWorld.EntityManager, selfEndpoint);

            await ValidateConnectionAsync();
        }

        async Task ConnectAsync()
        {
            ValidateWorld(m_ClientWorld);
            SetupClientDriver();

            Unity.Networking.Transport.NetworkEndpoint connectEndpoint = default;

            switch (m_Configuration.Type)
            {
                case NetworkType.Direct:
                {
                    connectEndpoint = m_Configuration.DirectNetworkPublishAddress;
                    break;
                }
                case NetworkType.Relay:
                {
                    connectEndpoint = m_Configuration.RelayClientData.Endpoint;
                    break;
                }
            }

            if (!connectEndpoint.IsValid)
            {
                throw new SessionException("Invalid endpoint to connect to", SessionError.NetworkSetupFailed);
            }

            Logger.LogVerbose($"ClientDriver Connect: {connectEndpoint}");
            m_ConnectionEntity = m_ClientDriver.Connect(m_ClientWorld.EntityManager, connectEndpoint);
            await ValidateConnectionAsync();
        }

        async Task ValidateConnectionAsync()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var connection = m_ClientWorld.EntityManager.GetComponentData<NetworkStreamConnection>(m_ConnectionEntity);

            while (connection.CurrentState != ConnectionState.State.Connected &&
                   connection.CurrentState != ConnectionState.State.Disconnected)
            {
                if (stopwatch.Elapsed.TotalSeconds >= MPConstants.ConnectionTimeoutSeconds)
                {
                    await CleanupClientAsync();
                    throw new Exception("Connection timeout. Failed connection");
                }

                await YieldAsync();

                if (!m_ClientWorld.EntityManager.Exists(m_ConnectionEntity))
                {
                    throw new Exception("Connect Entity no longer exists. Failed connection");
                }

                connection = m_ClientWorld.EntityManager.GetComponentData<NetworkStreamConnection>(m_ConnectionEntity);
            }
        }

        async Task CleanupClientAsync()
        {
            if (m_ClientWorld != null &&
                m_ClientWorld.IsCreated &&
                m_ClientWorld.EntityManager.Exists(m_ConnectionEntity))
            {
                m_ClientWorld.EntityManager.AddComponent<NetworkStreamRequestDisconnect>(m_ConnectionEntity);
                m_ConnectionEntity = default;

                var connectionQuery = m_ClientWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
                while (!connectionQuery.IsEmpty)
                {
                    await YieldAsync();
                    if (m_ClientWorld == null || !m_ClientWorld.IsCreated)
                    {
                        return; // if world is gone after the yield immediately return, the query will already be disposed
                    }
                }
                connectionQuery.Dispose();
            }
        }

        void CleanupServer()
        {
            if (m_ServerWorld != null)
            {
                m_ServerWorld.Dispose();
                m_ServerWorld = null;
            }
        }

        async Task YieldAsync()
        {
            var tcs = new TaskCompletionSource<object>();
            m_ActionScheduler.ScheduleAction(() => tcs.SetResult(null), 0.1f);
            await tcs.Task;
        }

        void ValidateWorlds()
        {
            if ((m_Configuration.Role is NetworkRole.Client or NetworkRole.Host) && m_ClientWorld is not { IsCreated : true })
            {
                throw new SessionException("Invalid client world. Please make sure it has been created before attempting to setup network.", SessionError.NetworkSetupFailed);
            }
            if ((m_Configuration.Role is NetworkRole.Server or NetworkRole.Host) && m_ServerWorld is not { IsCreated : true })
            {
                throw new SessionException("Invalid server world. Please make sure it has been created before attempting to setup network.", SessionError.NetworkSetupFailed);
            }
        }

        void ValidateWorld(World world)
        {
            if (world == null || !world.IsCreated)
            {
                throw new SessionException("Invalid world to setup network", SessionError.NetworkSetupFailed);
            }
        }
    }
}
#endif
