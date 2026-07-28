#if ENTITIES_NETCODE_AVAILABLE && !UNITY_WEBGL
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport.Relay;

namespace Unity.Services.Multiplayer
{
    class EntitiesDriverConstructor : INetworkStreamDriverConstructor
    {
        public NetworkConfiguration Configuration;
        public const int InvalidDriverId = 0;

        public int ClientIpcDriverId { get; private set; } = InvalidDriverId;
        public int ClientUdpDriverId { get; private set; } = InvalidDriverId;
        public int ClientWebSocketDriverId { get; private set; } = InvalidDriverId;

        public int ServerIpcDriverId { get; private set; } = InvalidDriverId;
        public int ServerUdpDriverId { get; private set; } = InvalidDriverId;
        public int ServerWebSocketDriverId { get; private set; } = InvalidDriverId;

        public void CreateClientDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
        {
            var ipcSettings = DefaultDriverBuilder.GetNetworkSettings();

            var driverId = 1;

            if (Configuration.Role == NetworkRole.Host || Configuration.Role == NetworkRole.Server)
            {
                Logger.LogVerbose($"Registering Client Ipc Driver ({driverId})");
                DefaultDriverBuilder.RegisterClientIpcDriver(world, ref driverStore, netDebug, ipcSettings);
                ClientIpcDriverId = driverId;
            }
            else if (Configuration.Role == NetworkRole.Client)
            {
                var udpSettings = DefaultDriverBuilder.GetNetworkSettings();

                if (Configuration.Type == NetworkType.Relay)
                {
                    var relayClientData = Configuration.RelayClientData;
                    udpSettings.WithRelayParameters(ref relayClientData);
                }

                Logger.LogVerbose($"Registering Client Udp Driver ({driverId})");
                DefaultDriverBuilder.RegisterClientUdpDriver(world, ref driverStore, netDebug, udpSettings);
                ClientUdpDriverId = driverId;
            }
        }

        public void CreateServerDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
        {
            var ipcSettings = DefaultDriverBuilder.GetNetworkSettings();

            var driverId = 1;

            if (Configuration.Role == NetworkRole.Host)
            {
                Logger.LogVerbose($"Registering Server Ipc Driver ({driverId})");
                DefaultDriverBuilder.RegisterServerIpcDriver(world, ref driverStore, netDebug, ipcSettings);
                ServerIpcDriverId = driverId;
                driverId++;
            }

            var udpSettings = DefaultDriverBuilder.GetNetworkSettings();

            if (Configuration.Type == NetworkType.Relay)
            {
                var relayServerData = Configuration.RelayServerData;
                udpSettings.WithRelayParameters(ref relayServerData);
            }

            Logger.LogVerbose($"Registering Server Udp Driver ({driverId})");
            DefaultDriverBuilder.RegisterServerUdpDriver(world, ref driverStore, netDebug, udpSettings);
            ServerUdpDriverId = driverId;
        }
    }
}
#endif
