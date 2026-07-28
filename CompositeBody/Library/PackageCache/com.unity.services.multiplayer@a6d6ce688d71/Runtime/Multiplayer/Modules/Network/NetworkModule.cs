using System;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.DistributedAuthority;

namespace Unity.Services.Multiplayer
{
    class NetworkModule : IHostSessionNetwork, IClientSessionNetwork, IModule
    {
        public const string SessionPropertyKey = "_session_network";
        const string k_EnclosingTypeName = nameof(NetworkModule);

        static readonly string[] AvailableMethodNames =
        {
            nameof(StartDirectNetworkAsync),
            nameof(StartRelayNetworkAsync),
#if GAMEOBJECTS_NETCODE_2_AVAILABLE
            nameof(StartDistributedAuthorityNetworkAsync)
#endif
        };

        static string Enumerate(in ReadOnlySpan<string> names)
        {
            var result = string.Join(", ", names[..^ 1].ToArray());
            return string.Concat(result, " or ", names[^ 1]);
        }

        public event Action<NetworkState> StateChanged;
        public event Action<SessionError> StartFailed;
        public event Action<SessionError> StopFailed;
        public event Action<SessionError> MigrationFailed;

        NetworkInfo IHostSessionNetwork.NetworkInfo => NetworkInfo;
        internal NetworkInfo NetworkInfo { get; set; }
        internal NetworkMetadata NetworkMetadata => m_NetworkMetadata;

        /// <summary>
        /// Used to provide network options overrides for host and client.
        /// </summary>
        internal NetworkOptions NetworkOptions { get; set; }


        readonly IDaBuilder m_DaBuilder;
        readonly INetworkBuilder m_NetworkBuilder;
        readonly IRelayBuilder m_RelayBuilder;
        readonly ISession m_Session;

        internal IDaHandler DaHandler => m_DaHandler;
        internal IRelayHandler RelayHandler => m_RelayHandler;

        IDaHandler m_DaHandler;
        IRelayHandler m_RelayHandler;
        INetworkHandler m_NetworkHandler;
        internal IHostMigrationHandler HostMigrationHandler { get; set; }
        SessionProperty m_Property;

        NetworkMetadata m_NetworkMetadata;
        NetworkState m_State = NetworkState.Stopped;
        bool m_IsMigrating;

        public IClientSessionNetwork ClientNetwork => this;
        public IHostSessionNetwork HostNetwork => this;

        public NetworkState State
        {
            get => m_State;
            private set
            {
                if (m_State == value)
                {
                    return;
                }

                if (!m_IsMigrating)
                {
                    m_State = value;
                    Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, $"Updated network state: {m_State}");
                    StateChanged?.Invoke(value);
                }
            }
        }

        public INetworkHandler NetworkHandler
        {
            get => m_NetworkHandler;
            set
            {
                if (State != NetworkState.Stopped)
                {
                    throw new SessionException(
                        "Trying to set the network handler when the network is being setup or has been setup.",
                        SessionError.InvalidOperation);
                }

                m_NetworkHandler = value;
            }
        }

        internal NetworkModule(ISession session,
                               INetworkBuilder networkBuilder, IDaBuilder daBuilder,
                               IRelayBuilder relayBuilder)
        {
            m_Session = session;
            m_NetworkBuilder = networkBuilder;
            m_DaBuilder = daBuilder;
            m_RelayBuilder = relayBuilder;
            m_Session.Changed += OnSessionChanged;
            m_Session.SessionPropertiesChanged += OnSessionChanged;
            m_Session.SessionHostChanged += OnSessionHostChanged;
            HostMigrationHandler = null;
        }

        Task IModule.InitializeAsync()
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);
            State = NetworkState.Stopped;

            if (NetworkInfo != null && m_Session.IsHost)
            {
                return StartNetworkWithOptionsAsync();
            }

            return ValidateNetworkPropertyAsync();
        }

        async Task IModule.LeaveAsync()
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);
            await ResetAsync();
        }

        async void OnSessionChanged()
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);

            if (State == NetworkState.Started && !m_Session.IsMember && !m_Session.IsServer)
            {
                await ResetAsync();
                return;
            }

            if (!m_Session.IsHost)
            {
                // Validate if network metadata changed
                await ValidateNetworkPropertyAsync();
            }
        }

        async void OnSessionHostChanged(string newHostId)
        {
            Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, $"New host: {newHostId}");
            await m_Session.ReconnectAsync();

            if (m_NetworkMetadata == null)
            {
                Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, "No network set up. No migration required.");
                return;
            }

            if (HostMigrationHandler == null)
            {
                Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, "Host migration is disabled");
                return;
            }

            if (m_Session.IsHost)
            {
                await MigrateHostNetworkAsync();
            }
        }

        async Task MigrateHostNetworkAsync()
        {
            // Check if host migration is enabled & supported
            if (HostMigrationHandler == null || m_NetworkMetadata == null || m_NetworkMetadata.Network == NetworkType.DistributedAuthority)
            {
                return;
            }

            try
            {
                StartMigration();
                var previousMetadata = m_NetworkMetadata; // Cache value as this will be reset

                switch (previousMetadata.Network)
                {
                    case NetworkType.Direct:
                        Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, "Migrating direct network.");
                        await ResetAsync();
                        await ApplyMigrationAsync();
                        await StartDirectNetworkAsync(new DirectNetworkOptions(ListenIPAddress.LoopbackIpv4, new PublishIPAddress(previousMetadata.Endpoint), 0));
                        break;
                    case NetworkType.Relay:
                        Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, "Migrating relay network.");
                        await ResetAsync();
                        await ApplyMigrationAsync();
#pragma warning disable CS0618 //RelayProtocol option in RelayNetworkOptions for backward compatibility.
                        await StartRelayNetworkAsync(new RelayNetworkOptions(GetRelayProtocol(), previousMetadata.RelayRegion, !string.IsNullOrEmpty(previousMetadata.RelayRegion)));
#pragma warning restore CS0618
                        break;
                }

                await m_Session.AsHost().SavePropertiesAsync();
                CompleteMigration();
            }
            catch (SessionException e)
            {
                FailMigration(e.Error, e.Message);
            }
            catch (Exception e)
            {
                FailMigration(SessionError.NetworkSetupFailed, e.Message);
            }
        }

        async Task MigrateClientNetworkAsync(NetworkMetadata metadata)
        {
            StartMigration();

            try
            {
                await ResetAsync();
                await JoinNetworkAsync(metadata);
                CompleteMigration();
            }
            catch (SessionException e)
            {
                FailMigration(e.Error, e.Message);
            }
            catch (Exception e)
            {
                FailMigration(SessionError.NetworkSetupFailed, e.Message);
            }
        }

        async Task StartNetworkWithOptionsAsync()
        {
            try
            {
                ValidateStartNetwork();
                State = NetworkState.Starting;

                string allocationId = null;

                var networkRole = m_Session.CurrentPlayer != null
                    ? NetworkRole.Host
                    : NetworkRole.Server;

                Logger.LogVerbose($"StartNetworkWithOptionsAsync NetworkRole: {networkRole}");

                switch (NetworkInfo.Network)
                {
                    case NetworkType.DistributedAuthority:
                    {
                        Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, "Setting up distributed authority network.");
                        m_DaHandler ??= m_DaBuilder.Build();
                        await m_DaHandler.CreateAndJoinSessionAsync(m_Session.Id, NetworkInfo.RelayOptions.Region);
                        allocationId = m_DaHandler.AllocationId.ToString();

                        var connectPayload = m_DaHandler.GetConnectPayload();
                        await SetDistributedAuthorityConnectHash(
                            connectPayload);

                        var networkConfiguration = new NetworkConfiguration(
                            networkRole,
                            NetworkType.DistributedAuthority,
                            m_DaHandler.GetRelayServerData(GetRelayProtocol()),
                            connectPayload.SerializeToNativeArray());
                        await StartNetworkHandlerAsync(networkConfiguration);

                        m_NetworkMetadata = new NetworkMetadata
                        {
                            Network = NetworkType.DistributedAuthority,
                            RelayJoinCode = m_DaHandler.RelayJoinCode,
                            RelayRegion = NetworkInfo.RelayOptions.PreserveRegion ? m_DaHandler.Region : null,
                            HostId = null // Must be set to null for DA.
                        };
                        break;
                    }
                    case NetworkType.Relay:
                    {
                        Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, "Setting up relay network.");
                        m_RelayHandler ??= m_RelayBuilder.Build();

                        await m_RelayHandler.CreateAllocationAsync(m_Session.MaxPlayers, NetworkInfo.RelayOptions.Region);
                        await m_RelayHandler.FetchJoinCodeAsync();

                        var relayServerData = m_RelayHandler.GetRelayServerData(GetRelayProtocol());
                        RelayServerData? relayClientData = null;
#if ENTITIES_NETCODE_AVAILABLE
                        await m_RelayHandler.JoinAllocationAsync(m_RelayHandler.RelayJoinCode);
                        var clientRelayData = m_RelayHandler.GetRelayServerData(GetRelayProtocol());
                        relayClientData = clientRelayData;
#endif

                        var networkConfiguration = new NetworkConfiguration(
                            networkRole,
                            NetworkType.Relay,
                            relayServerData,
                            // ReSharper disable once ExpressionIsAlwaysNull
                            relayClientData);

                        await StartNetworkHandlerAsync(networkConfiguration);

                        m_NetworkMetadata = new NetworkMetadata
                        {
                            Network = NetworkType.Relay,
                            RelayJoinCode = m_RelayHandler.RelayJoinCode,
                            RelayRegion = NetworkInfo.RelayOptions.PreserveRegion ? m_RelayHandler.Region : null,
                            HostId = m_Session.Host
                        };

                        allocationId = m_RelayHandler.AllocationId.ToString();
                        break;
                    }
                    case NetworkType.Direct:
                    {
                        Logger.LogCallVerboseWithMessage(k_EnclosingTypeName,
                            "Setting up direct network.");
                        var networkConfiguration = new NetworkConfiguration(
                            networkRole,
                            NetworkInfo.PublishAddress,
                            NetworkInfo.ListenAddress);

                        await StartNetworkHandlerAsync(networkConfiguration);
                        if (networkConfiguration.Role != NetworkRole.Client &&
                            networkConfiguration.DirectNetworkPublishAddress
                                .Port == 0)
                        {
                            Logger.LogCallWarning(k_EnclosingTypeName,
                                $"Port 0 on publish address {networkConfiguration.DirectNetworkPublishAddress} was not updated by network handler (hint: call {nameof(NetworkConfiguration)}.{nameof(NetworkConfiguration.UpdatePublishPort)}())");
                        }

                        m_NetworkMetadata = new NetworkMetadata
                        {
                            Network = NetworkType.Direct,
                            Endpoint =
                                new NetworkEndpointAddress(networkConfiguration.DirectNetworkPublishAddress),
                            HostId = m_Session.Host
                        };
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(allocationId) &&
                    m_Session.CurrentPlayer != null)
                {
                    m_Session.CurrentPlayer.SetAllocationId(allocationId);
                }

                HostMigrationHandler?.Start();

                Logger.LogCallVerboseWithMessage(k_EnclosingTypeName,
                    "Adding network session property.");
                var sessionProperty = new SessionProperty(
                    NetworkMetadata.Serialize(m_NetworkMetadata),
                    VisibilityPropertyOptions.Member);
                m_Session.AsHost().SetProperty(SessionPropertyKey, sessionProperty);
                await m_Session.AsHost().SavePropertiesAsync();
                State = NetworkState.Started;
            }
            catch (SessionException e)
            {
                State = NetworkState.Stopped;
                StartFailed?.Invoke(e.Error);
                throw;
            }
            catch (Exception e)
            {
                var error = SessionError.NetworkSetupFailed;
                State = NetworkState.Stopped;
                StartFailed?.Invoke(error);
                throw new SessionException($"Starting network failed with unexpected exception.\n{e}", error);
            }
        }

        async Task JoinNetworkAsync(NetworkMetadata metadata)
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);

            if (State == NetworkState.Starting)
            {
                throw new SessionException(
                    "Trying to connect when already connecting.",
                    SessionError.NetworkSetupFailed);
            }

            if (State == NetworkState.Started)
            {
                throw new SessionException(
                    "Trying to connect when already connected.",
                    SessionError.NetworkSetupFailed);
            }

            try
            {
                State = NetworkState.Starting;
                m_NetworkMetadata = metadata;
                string allocationId = null;

                NetworkConfiguration networkConfiguration;
                m_RelayHandler ??= m_RelayBuilder.Build();

                switch (m_NetworkMetadata.Network)
                {
                    case NetworkType.Direct:
                    {
                        Logger.LogCallVerboseWithMessage(k_EnclosingTypeName,
                            "With direct network.");
                        networkConfiguration = new NetworkConfiguration(
                            NetworkRole.Client,
                            new PublishIPAddress(m_NetworkMetadata.Endpoint),
                            ListenIPAddress.LoopbackIpv4.WithPort(
                                m_NetworkMetadata.Endpoint.Port));
                        break;
                    }
                    case NetworkType.Relay:
                    {
                        Logger.LogCallVerboseWithMessage(k_EnclosingTypeName,
                            "With relay network.");
                        await m_RelayHandler.JoinAllocationAsync(
                            m_NetworkMetadata
                                .RelayJoinCode);
                        var relayServerData =
                            m_RelayHandler.GetRelayServerData(GetRelayProtocol());
                        RelayServerData? relayClientData = null;
#if ENTITIES_NETCODE_AVAILABLE
                        relayClientData = relayServerData;
#endif
                        networkConfiguration = new NetworkConfiguration(
                            NetworkRole.Client,
                            NetworkType.Relay,
                            relayServerData,
                            // ReSharper disable once ExpressionIsAlwaysNull
                            relayClientData);
                        allocationId = m_RelayHandler.AllocationId.ToString();
                        break;
                    }
                    case NetworkType.DistributedAuthority:
                    {
                        Logger.LogCallVerboseWithMessage(k_EnclosingTypeName,
                            "With distributed authority network.");
                        m_DaHandler ??= m_DaBuilder.Build();
                        await m_DaHandler.JoinSessionAsync(m_NetworkMetadata
                            .RelayJoinCode);
                        allocationId = m_DaHandler.AllocationId.ToString();

                        var connectPayload = m_DaHandler.GetConnectPayload();
                        await SetDistributedAuthorityConnectHash(
                            connectPayload);

                        networkConfiguration = new NetworkConfiguration(
                            NetworkRole.Client,
                            NetworkType.DistributedAuthority,
                            m_DaHandler.GetRelayServerData(GetRelayProtocol()),
                            connectPayload.SerializeToNativeArray());
                        break;
                    }
                    default:
                        throw new ArgumentException(
                            $"Invalid transport type {m_NetworkMetadata.Network}");
                }

                await StartNetworkHandlerAsync(networkConfiguration);

                if (!string.IsNullOrEmpty(allocationId))
                {
                    m_Session.CurrentPlayer.SetAllocationId(allocationId);
                }

                State = NetworkState.Started;
            }
            catch (SessionException e)
            {
                State = NetworkState.Stopped;
                StartFailed?.Invoke(e.Error);
                throw;
            }
            catch (Exception e)
            {
                var error = SessionError.NetworkSetupFailed;
                State = NetworkState.Stopped;
                StartFailed?.Invoke(error);
                throw new SessionException($"Joining network failed: {e}", error);
            }
        }

        bool IsNetworkSetup()
        {
            return m_Session.Properties != null && m_Session.Properties.ContainsKey(SessionPropertyKey);
        }

        Task ValidateNetworkPropertyAsync()
        {
            if (m_Session.IsHost)
                return Task.CompletedTask;

            if (State == NetworkState.Started && !IsNetworkSetup())
            {
                Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, "Network property was removed.");
                return StopNetworkAsync();
            }

            if (m_Session.Properties.TryGetValue(SessionPropertyKey, out var newProperty))
            {
                if (m_Property?.Value != newProperty.Value)
                {
                    m_Property = newProperty;
                    return ProcessNetworkMetadataAsync(m_Property.Value);
                }
            }

            return Task.CompletedTask;
        }

        async Task ProcessNetworkMetadataAsync(string metadata)
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);

            try
            {
                var oldMetadata = m_NetworkMetadata;
                var newMetadata = NetworkMetadata.Deserialize(metadata);

                if (State != NetworkState.Starting && State != NetworkState.Started && !m_Session.IsHost)
                {
                    if (newMetadata.HostId == null || newMetadata.HostId.Equals(m_Session.Host))
                    {
                        await JoinNetworkAsync(newMetadata);
                    }
                }
                else if (oldMetadata != null && State == NetworkState.Started && oldMetadata.HostId != newMetadata.HostId)
                {
                    Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, "Host changed and created new network, joining the new host.");

                    await MigrateClientNetworkAsync(newMetadata);
                }
            }
            catch (Exception e)
            {
                var previousState = State;
                State = NetworkState.Stopped;
                if (e is not SessionException)
                {
                    throw new SessionException($"Unexpected exception processing network metadata while in state '{previousState}': {e.Message}", SessionError.NetworkSetupFailed);
                }
                throw;
            }
        }

        Task StartNetworkHandlerAsync(NetworkConfiguration networkConfiguration)
        {
            m_NetworkHandler ??= m_NetworkBuilder.Build();
            return NetworkHandler.StartAsync(networkConfiguration);
        }

        void StartMigration()
        {
            Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, $"Network migration starting");
            State = NetworkState.Migrating;
            m_IsMigrating = true;
        }

        void FailMigration(SessionError error, string errorMessage)
        {
            Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, $"Network migration failed: {errorMessage}");
            m_IsMigrating = false;
            State = NetworkState.Stopped;
            MigrationFailed?.Invoke(error);
        }

        void CompleteMigration()
        {
            Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, $"Network migration completed");
            m_IsMigrating = false;
            State = NetworkState.Started;
            m_Session.OnSessionMigrated();
        }

        async Task ApplyMigrationAsync()
        {
            if (HostMigrationHandler != null)
            {
                await HostMigrationHandler.ApplyMigrationDataAsync();
            }
        }

        async Task SetDistributedAuthorityConnectHash(
            ConnectPayload connectPayload)
        {
            const string daHashKey = "_distributed_authority_connect_hash";
            m_Session.CurrentPlayer.SetProperty(daHashKey,
                new PlayerProperty(connectPayload.Base64SecretHash(),
                    VisibilityPropertyOptions.Private));
            await m_Session.SaveCurrentPlayerDataAsync();
        }

        void ValidateStartNetwork()
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);

            if (!m_Session.IsHost)
            {
                throw new SessionException(
                    "Trying to setup the network but the player isn't the host.",
                    SessionError.NetworkSetupFailed);
            }

            ValidateNetworkStateForStartNetwork();
        }

        void ValidateNetworkStateForStartNetwork()
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);

            if (State == NetworkState.Starting)
            {
                throw new SessionException(
                    "Trying to setup the network when already in progress.",
                    SessionError.NetworkSetupFailed);
            }

            if (State == NetworkState.Started)
            {
                throw new SessionException(
                    "Trying to start the network when already started.",
                    SessionError.NetworkSetupFailed);
            }
        }

#pragma warning disable CS0618 //RelayProtocol option in RelayNetworkOptions for backward compatibility.
        internal RelayProtocol GetRelayProtocol() => NetworkOptions?.RelayProtocol ?? NetworkInfo?.RelayOptions?.Protocol ?? RelayProtocol.Default;
#pragma warning restore CS0618

        /// <inheritdoc />
        public async Task StartDirectNetworkAsync(DirectNetworkOptions networkOptions)
        {
            if (networkOptions == null)
            {
                throw new ArgumentNullException(nameof(networkOptions), "Network options cannot be null.");
            }
            NetworkInfo = NetworkInfo.BuildDirect(networkOptions);
            await StartNetworkWithOptionsAsync();
        }

        /// <inheritdoc />
        public async Task StartRelayNetworkAsync(RelayNetworkOptions networkOptions)
        {
            if (networkOptions == null)
            {
                throw new ArgumentNullException(nameof(networkOptions), "Network options cannot be null.");
            }
            NetworkInfo = NetworkInfo.BuildRelay(networkOptions);
            await StartNetworkWithOptionsAsync();
        }

#if GAMEOBJECTS_NETCODE_2_AVAILABLE
        /// <inheritdoc />
        public async Task StartDistributedAuthorityNetworkAsync(RelayNetworkOptions networkOptions)
        {
            if (networkOptions == null)
            {
                throw new ArgumentNullException(nameof(networkOptions), "Network options cannot be null.");
            }
            NetworkInfo = NetworkInfo.BuildDistributed(networkOptions);
            await StartNetworkWithOptionsAsync();
        }

#endif

        Task IClientSessionNetwork.StopNetworkAsync()
        {
            return StopNetworkAsync();
        }

        public async Task StopNetworkAsync()
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);

            if (State != NetworkState.Started)
            {
                throw new SessionException("Trying to stop the network when it is not started.", SessionError.InvalidOperation);
            }

            try
            {
                State = NetworkState.Stopping;
                await ResetAsync();

                if (m_Session.IsHost)
                {
                    var hostSession = m_Session.AsHost();
                    hostSession.SetProperty(SessionPropertyKey, null);
                    await hostSession.SavePropertiesAsync();
                }

                State = NetworkState.Stopped;
            }
            catch (Exception e)
            {
                const SessionError error = SessionError.NetworkSetupFailed;
                State = NetworkState.Started;
                StopFailed?.Invoke(error);
                throw new SessionException($"Unexpected error while stopping network: {e}", error);
            }
        }

        async Task ResetAsync()
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);
            HostMigrationHandler?.Stop();

            if (NetworkHandler != null)
            {
                await NetworkHandler.StopAsync();
            }

            State = NetworkState.Stopped;

            m_RelayHandler?.Disconnect();
            m_NetworkMetadata = null;
        }

        Task IClientSessionNetwork.StartNetworkAsync()
        {
            Logger.LogCallVerboseWithMessage(k_EnclosingTypeName,
                $"StartNetworkAsync as {(m_Session.IsHost ? "host" : "client")}");

            ValidateNetworkStateForStartNetwork();

            if (NetworkInfo != null)
            {
                return m_Session.IsHost ? StartNetworkWithOptionsAsync() : ValidateNetworkPropertyAsync();
            }

            if (m_Session.IsHost)
            {
                throw new SessionException(
                    $"Trying to start network on host session but there is no network metadata. Use {Enumerate(AvailableMethodNames)} instead.",
                    SessionError.NetworkSetupFailed);
            }

            throw new SessionException("Trying to start network as client but there is no network metadata.", SessionError.NetworkSetupFailed);
        }
    }
}
