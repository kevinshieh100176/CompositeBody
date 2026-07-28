using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication.Internal;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// An interface that can be used by a server to manage a session and access
    /// more privileged operations.
    /// </summary>
    public interface IServerSession : IHostSession {}

    /// <summary>
    /// A host's mutable handle on a session.
    /// </summary>
    /// <seealso cref="ISession.AsHost"/>
    public interface IHostSession : ISession
    {
        /// <summary>
        /// The Name of the session.
        /// </summary>
        public new string Name { get; set; }

        /// <summary>
        /// Whether the session is private
        /// </summary>
        public new bool IsPrivate { get; set; }

        /// <summary>
        /// Whether the session is locked
        /// </summary>
        public new bool IsLocked { get; set; }

        /// <summary>
        /// Session host player ID.
        /// </summary>
        /// <remarks>
        /// Can be changed to the ID of any player
        /// that is already a member of the session.
        /// </remarks>
        public new string Host { get; set; }

        /// <summary>
        /// The password used to connect to the Session.
        /// </summary>
        public string Password { set; }

        /// <summary>
        /// Retrieve the host network handle
        /// </summary>
        public new IHostSessionNetwork Network { get; }

        /// <summary>
        /// Removes a player from the session.
        /// </summary>
        /// <param name="playerId">Identifier for the player to remove.</param>
        /// <returns>A task for the operation.</returns>
        /// <seealso cref="ISession.ConcurrencyControlEnabled"/>
        Task RemovePlayerAsync(string playerId);

        /// <summary>
        /// Get the host migration data from the session.
        /// </summary>
        /// <param name="timeout">The timeout for the request.</param>
        /// <returns> Task for <see cref="SessionMigrationData"/>. <see cref="SessionMigrationData"/> null if not present./></returns>
        /// <exception cref="SessionException">
        /// Thrown when a session related error occurs.
        /// The <see cref="SessionException.Error"/> property reflects the error. Possible errors include: <br/>
        /// <see cref="SessionError.MigrationDataRequestTimeout"/><br/>
        /// <see cref="SessionError.NotAuthorized"/> <br/>
        /// <see cref="SessionError.NotInLobby"/> <br/>
        /// <see cref="SessionError.RateLimitExceeded"/> <br/>
        /// <see cref="SessionError.SessionNotFound"/> <br/>
        /// <see cref="SessionError.Unknown"/>
        /// </exception>
        /// <remarks>
        /// This task is not cancellable; use timeout parameter to limit execution time.
        /// </remarks>
        Task<SessionMigrationData> GetHostMigrationDataAsync(TimeSpan timeout);

        /// <summary>
        /// Set the host migration data on the session.
        /// </summary>
        /// <param name="data">The host migration data byte array to set.</param>
        /// <param name="timeout">The timeout for the request.</param>
        /// <returns>A task for the operation.</returns>
        /// <exception cref="SessionException">
        /// Thrown when a session related error occurs.
        /// The <see cref="SessionException.Error"/> property reflect the error. Possible errors include: <br/>
        /// <see cref="SessionError.MigrationDataRequestTimeout"/> <br/>
        /// <see cref="SessionError.NotAuthorized"/> <br/>
        /// <see cref="SessionError.NotInLobby"/> <br/>
        /// <see cref="SessionError.RateLimitExceeded"/> <br/>
        /// <see cref="SessionError.SessionNotFound"/> <br/>
        /// <see cref="SessionError.Unknown"/>
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the <paramref name="data"/> to set exceeds the maximum migration data size limit returned by the server.
        /// </exception>
        /// <remarks>
        /// This task is not cancellable; use timeout parameter to limit execution time.
        /// </remarks>
        Task SetHostMigrationDataAsync(byte[] data, TimeSpan timeout);

        /// <summary>
        /// Set <see cref="SessionProperty">properties</see>.
        /// </summary>
        /// <param name="properties">The <see
        /// cref="SessionProperty">properties</see> to be set on the
        /// session.</param>
        /// <seealso cref="SetProperty"/>
        void SetProperties(Dictionary<string, SessionProperty> properties);

        /// <summary>
        /// Set a <see cref="SessionProperty"/>.
        /// </summary>
        /// <param name="key">The <see cref="SessionProperty">property</see>'s key to bet set on the session.</param>
        /// <param name="property">The <see cref="SessionProperty">property</see>'s value.</param>
        /// <remarks>
        /// To remove an existing property, pass <see langword="null"/> to the
        /// <paramref name="property"/> argument.
        /// <br/>
        /// To set the value of the property to <see langword="null"/>, pass a
        /// <see cref="SessionProperty"/> with its <see
        /// cref="SessionProperty.Value"/> set to <see langword="null"/>.
        /// </remarks>
        /// <example>
        /// <para>To add a <c>colour</c> property</para>
        /// <code>var redColourProperty = new SessionProperty("red", VisibilityOptions.Public);
        /// myHostSession.SetProperty("colour", redColourProperty);
        /// await myHostSession.SavePropertiesAsync();</code>
        /// <para>To update the <c>colour</c> property to <see langword="null"/></para>
        /// <code>var nullSessionProperty = new SessionProperty(null, VisibilityOptions.Public);
        /// myHostSession.SetProperty("colour", nullSessionProperty);
        /// await myHostSession.SavePropertiesAsync();</code>
        /// <para>To remove the <c>colour</c> property</para>
        /// <code>myHostSession.SetProperty("colour", null);
        /// await myHostSession.SavePropertiesAsync();</code>
        /// </example>
        void SetProperty(string key, SessionProperty property);

        /// <summary>
        /// The list of players in the session
        /// </summary>
        new IReadOnlyList<IPlayer> Players { get; }

        /// <summary>
        /// Saves the <see cref="SessionProperty">properties</see> of the
        /// session.
        /// </summary>
        /// <returns>A <see cref="Task"/> for the operation.</returns>
        /// <seealso cref="SetProperty"/>
        /// <seealso cref="SetProperties"/>
        /// <seealso cref="ISession.ConcurrencyControlEnabled"/>
        Task SavePropertiesAsync();

        /// <summary>
        /// Save the <see cref="PlayerProperty">properties</see>
        /// of a <see cref="IPlayer">player</see>.
        /// </summary>
        /// <param name="playerId">
        /// The ID of the <see cref="IPlayer">player</see>
        /// whose data will be saved.
        /// </param>
        /// <returns>
        /// A <see cref="Task"/> representing the asynchronous operation.
        /// </returns>
        /// <exception cref="SessionException">
        /// Thrown when the player is not found. The <see
        /// cref="SessionException.Error"/> property will be
        /// set to <see cref="SessionError.InvalidOperation"/>.
        /// </exception>
        /// <seealso cref="IPlayer.SetProperty"/>
        /// <seealso cref="IPlayer.SetProperties"/>
        /// <seealso cref="IHostSession.ConcurrencyControlEnabled"/>
        Task SavePlayerDataAsync(string playerId);

        /// <summary>
        /// Delete the session.
        /// </summary>
        /// <returns>
        /// A <see cref="Task"/> representing the asynchronous operation.
        /// </returns>
        /// <seealso cref="ISession.ConcurrencyControlEnabled"/>
        public Task DeleteAsync();

        /// <summary>
        /// Retrieves information about a session member player by their
        /// <paramref name="playerId"/>.
        /// </summary>
        /// <param name="playerId">The ID of the player to retrieve.</param>
        /// <returns>
        /// The <see cref="IPlayer"/> instance if found; otherwise, <c>null</c>.
        /// </returns>
        /// <seealso cref="ISession.GetPlayer(string)"/>
        public new IPlayer GetPlayer(string playerId);
    }

    /// <summary>
    /// An interface that can be used by a client to access the information of a
    /// session.
    /// </summary>
    public interface ISession
    {
        /// <summary>
        /// Event that is invoked when the session changes.
        /// </summary>
        /// <remarks>
        /// This includes any type of changes to the session (properties, players, etc.).
        /// </remarks>
        public event Action Changed;

        /// <summary>
        /// Event that is invoked when the session state changes.
        /// </summary>
        /// <remarks>
        /// The <see cref="SessionState"/> parameter represents the new state of the session.
        /// </remarks>
        public event Action<SessionState> StateChanged;

        /// <summary>
        /// Event that is invoked when a player has joined the session.
        /// </summary>
        /// <remarks>
        /// The <see cref="string"/> parameter represents the ID of the player who has joined.
        /// This event is called right after the session gets updated.
        /// </remarks>
        public event Action<string> PlayerJoined;

        /// <summary>
        /// Event that is invoked when a player is leaving the session.
        /// </summary>
        /// <remarks>
        /// The <see cref="string"/> parameter represents the ID of the player who is leaving.
        /// This event is called right before the session gets updated.
        /// </remarks>
        [Obsolete("PlayerLeft has been deprecated. Use PlayerLeaving instead (UnityUpgradable) -> PlayerLeaving")]
        public event Action<string> PlayerLeft;

        /// <summary>
        /// Event that is invoked when a player is leaving the session.
        /// </summary>
        /// <remarks>
        /// The <see cref="string"/> parameter represents the ID of the player who is leaving.
        /// This event is called right before the session gets updated.
        /// </remarks>
        public event Action<string> PlayerLeaving;

        /// <summary>
        /// Event that is invoked when a player has left the session.
        /// </summary>
        /// <remarks>
        /// The <see cref="string"/> parameter represents the ID of the player who has left.
        /// This event is called right after the session gets updated.
        /// </remarks>
        public event Action<string> PlayerHasLeft;

        /// <summary>
        /// Event that is invoked when session properties are changed.
        /// </summary>
        /// <remarks>
        /// If the properties are already up to date locally, this event will not trigger.
        /// Example: This will not trigger for the host after it updates the properties,
        /// as the changes are already reflected in the host's local session.
        /// </remarks>
        public event Action SessionPropertiesChanged;

        /// <summary>
        /// Event that is invoked when player properties are changed.
        /// </summary>
        /// <remarks>
        /// If the properties are already up to date locally, this event will not trigger.
        /// Example: This will not trigger for the host after it updates the properties,
        /// as the changes are already reflected in the host's local session.
        /// </remarks>
        public event Action PlayerPropertiesChanged;

        /// <summary>
        /// Event that is invoked when the current player is removed from the session.
        /// </summary>
        public event Action RemovedFromSession;

        /// <summary>
        /// Event that is invoked when the session is deleted.
        /// </summary>
        public event Action Deleted;

        /// <summary>
        /// Event that is invoked when the session host changed.
        /// </summary>
        /// <remarks>
        /// The <see cref="string"/> parameter represents the ID of the new session host.
        /// </remarks>
        public event Action<string> SessionHostChanged;

        /// <summary>
        /// Event that is invoked when the session has finished migrating hosts.
        /// </summary>
        public event Action SessionMigrated;

        /// <summary>
        /// The type is a client-side key used to uniquely identify a session.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// The Name of the session.
        /// </summary>
        /// <remarks>
        /// Does not have to be unique.
        /// Sessions can be filtered by name in a query.
        /// </remarks>
        /// <seealso cref="SessionOptions.Name"/>
        /// <seealso cref="QuerySessionsOptions"/>
        public string Name { get; }

        /// <summary>
        /// The ID of the session.
        /// </summary>
        /// <remarks>
        /// Unique within a project and environment.
        /// Sessions can be joined by Id.
        /// </remarks>
        /// <seealso cref="ISessionManager.JoinByIdAsync"/>
        public string Id { get; }

        /// <summary>
        /// The join code of the session.
        /// </summary>
        /// <remarks>
        /// Prefer this to ID when player-visible strings are needed.
        /// Unlike IDs, join codes are much shorter to type and avoid visually ambiguous characters.
        /// </remarks>
        public string Code { get; }

        /// <summary>
        /// Whether the current player is the host of the session.
        /// </summary>
        public bool IsHost { get; }

        /// <summary>
        /// Whether the local owner of the session handle is a server managing the session.
        /// </summary>
        public bool IsServer { get; }

        /// <summary>
        /// Whether the session is private.
        /// </summary>
        /// <remarks>
        /// Private sessions or not visible in queries and cannot be joined with quick-join.
        /// They can still be joined by ID or by Code.
        /// </remarks>
        /// <seealso cref="SessionOptions.IsPrivate"/>
        public bool IsPrivate { get; }

        /// <summary>
        /// Whether the session is locked.
        /// </summary>
        /// <remarks>
        /// Locked sessions cannot be joined by anyone.
        /// </remarks>
        /// <seealso cref="SessionOptions.IsLocked"/>
        public bool IsLocked { get; }

        /// <summary>
        /// True if the session has a password, false otherwise
        /// </summary>
        public bool HasPassword { get; }

        /// <summary>
        /// Available slots in the session
        /// </summary>
        public int AvailableSlots { get; }

        /// <summary>
        /// The total number of players allowed in the session, including the host.
        /// </summary>
        /// <seealso cref="SessionOptions.MaxPlayers"/>
        public int MaxPlayers { get; }

        /// <summary>
        /// The player count of the session
        /// </summary>
        public int PlayerCount { get; }

        /// <summary>
        /// The list of players in the session
        /// </summary>
        public IReadOnlyList<IReadOnlyPlayer> Players { get; }

        /// <summary>
        /// Retrieve the client network handle
        /// </summary>
        public IClientSessionNetwork Network { get; }

        internal NetworkModule NetworkModule { get; }

        /// <summary>
        /// The properties of the session.
        /// </summary>
        /// <remarks>You can modify session properties via an <see
        /// cref="IHostSession"/>.
        /// <br/>
        /// To obtain an <see cref="IHostSession"/> from an <see
        /// cref="ISession"/> call <see cref="AsHost"/>.</remarks>
        /// <seealso cref="AsHost"/>
        /// <seealso cref="IHostSession"/>
        public IReadOnlyDictionary<string, SessionProperty> Properties { get; }

        /// <summary>
        /// PlayerID of the session host.
        /// </summary>
        public string Host { get; }

        /// <summary>
        /// The current state of the session.
        /// </summary>
        public SessionState State { get; }

        /// <summary>
        /// The current player in the session.
        /// </summary>
        public IPlayer CurrentPlayer { get; }

        /// <summary>
        /// Returns whether the player is a member of the session
        /// </summary>
        bool IsMember { get; }

        /// <summary>
        /// Applies concurrency control by adding an <a
        /// href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/If-Match">If-match
        /// header</a> with the latest version of the lobby to applicable lobby operations.
        /// <br/>
        /// When enabled, operations are only allowed if the latest
        /// version matches the current version on the server.
        /// <br/>
        /// Applies to the following operations:
        /// <br/>
        /// • <see cref="IHostSession.DeleteAsync">The host deleting a session</see>
        /// <br/>
        /// • <see cref="IHostSession.RemovePlayerAsync(string)">The
        /// host removing a player from a session</see>
        /// <br/>
        /// • <see cref="IHostSession.SavePropertiesAsync">The
        /// host updating a session's properties</see>
        /// <br/>
        /// • <see cref="ISession.SaveCurrentPlayerDataAsync">A
        /// player updating its properties</see>
        /// <br/>
        /// • <see cref="IHostSession.SavePlayerDataAsync(string)">The
        /// host updating its properties</see>
        /// </summary>
        public bool ConcurrencyControlEnabled { get; set; }

        /// <summary>
        /// Save changes to the current player.
        /// </summary>
        /// <returns>
        /// A <see cref="Task"/> representing the asynchronous operation.
        /// </returns>
        /// <seealso cref="ConcurrencyControlEnabled"/>
        public Task SaveCurrentPlayerDataAsync();

        /// <summary>
        /// Leaves the session.
        /// </summary>
        /// <returns>A task for the operation.</returns>
        /// <exception cref="SessionException">
        /// Thrown when called by a non-session-member. The <see cref="SessionException.Error"/> property will be
        /// set to <see cref="SessionError.InvalidOperation"/>.
        /// </exception>
        /// <remarks>This method will be called automatically when the <see cref="Application.quitting">application is
        /// quitting</see>.</remarks>
        public Task LeaveAsync();

        /// <summary>
        /// Refreshes the session data.
        /// </summary>
        /// <returns>A task for the operation.</returns>
        /// <exception cref="SessionException">Throws a SessionDeleted error when the session is already deleted.</exception>
        public Task RefreshAsync();

        /// <summary>
        /// Reconnects to the session.
        /// </summary>
        /// <remarks>
        /// Reconnecting is necessary when a player has momentarily disconnected from the network but didn't gracefully
        /// leave the session.
        /// </remarks>
        /// <returns>A task for the operation.</returns>
        /// <exception cref="SessionException">Throws a SessionDeleted error when the session is already deleted.</exception>
        public Task ReconnectAsync();

        /// <summary>
        /// Returns a read-write handle to the session with host privileges.
        /// </summary>
        /// <exception cref="SessionException">Throws a Forbidden error when the caller is not the host.</exception>
        /// <returns>A <see cref="IHostSession">session</see> with higher privileges.</returns>
        public IHostSession AsHost();

        /// <summary>
        /// Returns a read-write handle to the session with server privileges.
        /// </summary>
        /// <exception cref="SessionException">Throws a Forbidden error when the caller is not the server.</exception>
        /// <returns>A <see cref="IServerSession">session</see> with higher privileges.</returns>
        public IServerSession AsServer();

        /// <summary>
        /// Checks if a player with the given <paramref name="playerId"/> is in
        /// the session.
        /// </summary>
        /// <param name="playerId">
        /// The player ID to check for session membership.
        /// </param>
        /// <returns><c>true</c> if the player is a member of the session;
        /// otherwise, <c>false</c>.</returns>
        public bool HasPlayer(string playerId);

        /// <summary>
        /// Retrieves information about a session member player by their
        /// <paramref name="playerId"/>.
        /// </summary>
        /// <param name="playerId">The ID of the player to retrieve.</param>
        /// <returns>
        /// The <see cref="IReadOnlyPlayer"/> instance if found; otherwise,
        /// <c>null</c>.
        /// </returns>
        /// <seealso cref="IHostSession.GetPlayer(string)"/>
        public IReadOnlyPlayer GetPlayer(string playerId);

        internal T GetModule<T>() where T : class, IModule;
        internal void OnSessionMigrated();
    }

    class SessionHandler : IServerSession
    {
        // Events
        public event Action Changed;
        public event Action<SessionState> StateChanged;
        public event Action<string> PlayerJoined;
        public event Action<string> PlayerLeft;
        public event Action<string> PlayerLeaving;
        public event Action<string> PlayerHasLeft;
        public event Action SessionPropertiesChanged;
        public event Action PlayerPropertiesChanged;
        public event Action Deleted;
        public event Action RemovedFromSession;
        public event Action<string> SessionHostChanged;
        public event Action SessionMigrated;

        // Properties
        public SessionState State { get; internal set; }
        public bool IsHost { get; set; }
        public bool IsMember => LobbyHandler.IsMember;
        public bool IsServer => LobbyHandler.IsServer;

        public bool IsPrivate
        {
            get => _isPrivate;
            set
            {
                if (_isPrivate == value)
                {
                    return;
                }

                _isPrivate = value;
                Modified = true;
            }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked == value)
                {
                    return;
                }

                _isLocked = value;
                Modified = true;
            }
        }

        public string Type { get; set; }

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                {
                    return;
                }

                _name = value;
                Modified = true;
            }
        }

        public string Id { get; set; }
        public string Code { get; set; }

        public string Host
        {
            get => _host;
            set
            {
                if (_host == value)
                {
                    return;
                }

                _host = value;
                Modified = true;
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (_password == value)
                {
                    return;
                }

                _password = value;
                Modified = true;
            }
        }

        public bool HasPassword { get; private set; }

        public int AvailableSlots { get; private set; }
        public int MaxPlayers { get; set; }
        public int PlayerCount => Players.Count;
        public readonly Dictionary<string, SessionProperty> Properties = new();
        public readonly List<Player> Players = new();

        IReadOnlyList<IPlayer> IHostSession.Players => Players;
        IReadOnlyList<IReadOnlyPlayer> ISession.Players => Players;
        IPlayer ISession.CurrentPlayer => CurrentPlayer;
        IReadOnlyDictionary<string, SessionProperty> ISession.Properties => Properties;

        internal Lobby Lobby => LobbyHandler.Lobby;
        internal Player CurrentPlayer => GetPlayer(PlayerId?.PlayerId);
        public bool ConcurrencyControlEnabled { get => LobbyHandler.ConcurrencyControlEnabled; set => LobbyHandler.ConcurrencyControlEnabled = value; }

        IHostSessionNetwork IHostSession.Network => NetworkModule.HostNetwork;
        IClientSessionNetwork ISession.Network => NetworkModule.ClientNetwork;

        NetworkModule ISession.NetworkModule => NetworkModule;
        NetworkModule NetworkModule => GetModule<NetworkModule>();

        internal bool Modified;
        internal readonly IActionScheduler ActionScheduler;
        internal readonly ILobbyHandler LobbyHandler;
        internal readonly IPlayerId PlayerId;
        internal readonly IAccessTokenObserver AccessTokenObserver;
        internal readonly Dictionary<Type, IModule> Modules = new Dictionary<Type, IModule>();

        bool _isHost;
        string _name;
        bool _isPrivate;
        bool _isLocked;
        string _password;
        string _host;

        public void RegisterModule<T>(T module) where T : class, IModule
        {
            RegisterModule(typeof(T), module);
        }

        public void RegisterModule(Type type, IModule module)
        {
            if (Modules.ContainsKey(type))
            {
                Logger.LogError($"Module of type '{type}' already registered");
                return;
            }

            Modules.Add(type, module);
        }

        T ISession.GetModule<T>()
        {
            return GetModule<T>();
        }

        /// <summary>
        /// Retrieves a registered module of the specified type.
        /// </summary>
        /// <typeparam name="T">The type of the module to retrieve. The type
        /// must implement <see cref="IModule"/>.</typeparam>
        /// <returns>The module of the specified type <typeparamref name="T"/>
        /// if found; otherwise, <see langword="null"/>.</returns>
        public T GetModule<T>() where T : class, IModule
        {
            var type = typeof(T);

            if (Modules.TryGetValue(type, out var module))
            {
                return module as T;
            }

            return null;
        }

        public SessionHandler(
            string type,
            IActionScheduler actionScheduler,
            ILobbyHandler lobbyHandler,
            IAccessTokenObserver accessTokenObserver,
            IPlayerId playerId)
        {
            Type = type;
            ActionScheduler = actionScheduler;
            LobbyHandler = lobbyHandler;
            PlayerId = playerId;
            AccessTokenObserver = accessTokenObserver;

            if (AccessTokenObserver != null)
            {
                AccessTokenObserver.AccessTokenChanged += OnAuthenticationTokenChanged;
            }

            // make an initial call to sync derived properties on session creation, then refresh on future lobby changes.
            if (Lobby != null)
            {
                UpdateDerivedProperties();
                Modified = false;
            }

            SetState(SessionState.Connected);

            // setup lobby based event handlers
            LobbyHandler.LobbyHostChanged += OnLobbyHostChanged;
            LobbyHandler.LobbyChanged += OnLobbyChanged;
            LobbyHandler.PlayerJoined += OnPlayerJoined;
            LobbyHandler.PlayerLeaving += OnPlayerLeaving;
            LobbyHandler.PlayerHasLeft += OnPlayerHasLeft;
            LobbyHandler.DataChanged += OnSessionPropertiesChanged;
            LobbyHandler.PlayerDataChanged += OnPlayerPropertiesChanged;
            LobbyHandler.KickedFromLobby += OnRemovedFromSession;
            LobbyHandler.LobbyDeleted += OnDeleted;
            LobbyHandler.LobbyExit += OnLobbyExit;
            Application.quitting += OnQuitting;
        }

        public async Task InitializeModulesAsync()
        {
            foreach (var module in Modules)
            {
                await module.Value.InitializeAsync();
            }
        }

        public async Task LeaveModulesAsync()
        {
            foreach (var module in Modules)
            {
                try
                {
                    await module.Value.LeaveAsync();
                }
                catch (Exception e)
                {
                    Logger.LogError($"LeaveModule {module.Key.Name} failed: {e.Message}");
                }
            }
        }

        public async Task LeaveAsync()
        {
            Logger.LogVerbose($"Session LeaveAsync");

            if (IsServer)
            {
                await DeleteAsync();
                return;
            }

            if (!LobbyHandler.IsMember)
            {
                throw new SessionException("Only session members can leave a session", SessionError.InvalidOperation);
            }

            if (State == SessionState.Deleted)
            {
                return;
            }

            Logger.LogVerbose("Removing subscriptions!");
            LobbyHandler.LobbyHostChanged -= OnLobbyHostChanged;
            LobbyHandler.LobbyChanged -= OnLobbyChanged;
            LobbyHandler.PlayerJoined -= OnPlayerJoined;
            LobbyHandler.PlayerLeaving -= OnPlayerLeaving;
            LobbyHandler.PlayerHasLeft -= OnPlayerHasLeft;
            LobbyHandler.DataChanged -= OnSessionPropertiesChanged;
            LobbyHandler.PlayerDataChanged -= OnPlayerPropertiesChanged;
            LobbyHandler.KickedFromLobby -= OnRemovedFromSession;
            LobbyHandler.LobbyDeleted -= OnDeleted;
            LobbyHandler.LobbyExit -= OnLobbyExit;
            Application.quitting -= OnQuitting;

            await LeaveModulesAsync();
            await LobbyHandler.RemovePlayerAsync(PlayerId.PlayerId);

            await LobbyHandler.ResetAsync();
            SetState(SessionState.Disconnected);
            RemovedFromSession?.Invoke();
        }

        internal async Task CleanupAsync()
        {
            Logger.LogVerbose($"Session CleanupAsync");
            if (!LobbyHandler.IsMember)
            {
                throw new SessionException("Only session members can leave a session", SessionError.InvalidOperation);
            }

            if (State == SessionState.Deleted)
            {
                return;
            }

            await LeaveModulesAsync();

            await LobbyHandler.ResetAsync();

            SetState(SessionState.Disconnected);
        }

        public Task RefreshAsync()
        {
            if (State == SessionState.Deleted)
            {
                throw new SessionException("cannot refresh a deleted session", SessionError.SessionDeleted);
            }

            return LobbyHandler.RefreshLobbyAsync();
        }

        public async Task ReconnectAsync()
        {
            if (State == SessionState.Connected)
            {
                return;
            }

            if (State == SessionState.Deleted)
            {
                throw new SessionException("cannot reconnect to a deleted session", SessionError.SessionDeleted);
            }

            Logger.LogVerbose("ReconnectToLobbyAsync");
            await LobbyHandler.ReconnectToLobbyAsync();
            SetState(SessionState.Connected);
        }

        public IHostSession AsHost()
        {
            if (!IsHost)
            {
                throw new SessionException("Only the host can perform this operation.", SessionError.Forbidden);
            }

            return this;
        }

        public IServerSession AsServer()
        {
            if (!IsServer)
            {
                throw new SessionException("Only a server host can perform this operation.", SessionError.Forbidden);
            }

            return this;
        }

        public async Task DeleteAsync()
        {
            Logger.LogVerbose($"Session DeleteAsync");
            if (State == SessionState.Deleted)
            {
                return;
            }

            await LeaveModulesAsync();
            SetState(SessionState.Disconnected);
            await LobbyHandler.DeleteLobbyAsync();
            SetState(SessionState.Deleted);
            Deleted?.Invoke();
        }

        void SetState(SessionState state)
        {
            if (State != state)
            {
                State = state;
                StateChanged?.Invoke(State);
                Changed?.Invoke();
            }
        }

        public Task SavePlayerDataAsync(string playerId)
        {
            var player = Players.FirstOrDefault(p => p.Id == playerId);

            if (player == null)
            {
                throw new SessionException($"Cannot save player data. Player not found.",
                    SessionError.InvalidOperation);
            }

            return SavePlayerDataAsync(player);
        }

        public Task SaveCurrentPlayerDataAsync()
        {
            if (CurrentPlayer != null)
            {
                return SavePlayerDataAsync(CurrentPlayer);
            }

            Logger.LogWarning("Cannot save current player whilst not a player in the session");
            return Task.CompletedTask;
        }

        async Task SavePlayerDataAsync(Player player)
        {
            Logger.LogVerbose($"Session.SaveCurrentPlayerDataAsync: IsModified = {player.Modified}");

            if (!player.Modified)
                return;

            var updatePlayerOptions = new UpdatePlayerOptions { Data = new Dictionary<string, PlayerDataObject>() };

            // convert the player data to lobby player data
            foreach (var playerDataItem in player.Properties)
            {
                var playerProperty = playerDataItem.Value;
                var playerDataObject = playerProperty != null
                    ? new PlayerDataObject((PlayerDataObject.VisibilityOptions)playerProperty.Visibility,
                    playerProperty.Value)
                    : null;
                updatePlayerOptions.Data.Add(playerDataItem.Key, playerDataObject);
            }

            await LobbyHandler.UpdatePlayerAsync(player.Id, updatePlayerOptions);
            player.Modified = false;
        }

        public Task RemovePlayerAsync(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                throw new ArgumentException("PlayerId cannot be null or empty", nameof(playerId));
            }

            return LobbyHandler.RemovePlayerAsync(playerId);
        }

        IPlayer IHostSession.GetPlayer(string playerId)
        {
            return GetPlayer(playerId);
        }

        IReadOnlyPlayer ISession.GetPlayer(string playerId)
        {
            return GetPlayer(playerId);
        }

        public Player GetPlayer(string playerId)
        {
            return Players.Find(player => player.Id == playerId);
        }

        public bool HasPlayer(string playerId)
        {
            return Players.Exists(player => player.Id == playerId);
        }

        public async Task<SessionMigrationData> GetHostMigrationDataAsync(TimeSpan timeout)
        {
            return new SessionMigrationData(await LobbyHandler.DownloadMigrationDataAsync(timeout));
        }

        public Task SetHostMigrationDataAsync(byte[] data, TimeSpan timeout)
        {
            return LobbyHandler.UploadMigrationDataAsync(data, timeout);
        }

        public void SetProperties(Dictionary<string, SessionProperty> properties)
        {
            if (!IsHost || properties == null || properties.Count == 0)
                return;

            foreach (var property in properties)
            {
                Properties[property.Key] = property.Value;
            }

            Modified = true;
        }

        public void SetProperty(string key, SessionProperty property)
        {
            Properties[key] = property;
            Modified = true;
        }

        public async Task SavePropertiesAsync()
        {
            if (!IsHost || !Modified)
            {
                return;
            }

            Logger.LogVerbose($"Session.SaveProperties: IsModified = {Modified}");
            var updateLobbyOptions = new UpdateLobbyOptions
            {
                Name = Name,
                IsPrivate = IsPrivate,
                IsLocked = IsLocked,
                Password = Password,
                Data = new Dictionary<string, DataObject>(),
            };

            if (Properties?.Count != 0)
            {
                var lobbyProperties = SessionToLobbyProperties();

                Logger.LogVerbose($"SavePropertiesAsync: {lobbyProperties.Count}");
                updateLobbyOptions.Data = lobbyProperties;
            }

            // note: we should only add the host to update if the host is different from the current host,
            // as the host migration flow is not supported on GSH server builds.
            if (Lobby?.HostId != Host)
                updateLobbyOptions.HostId = Host;

            await LobbyHandler.UpdateLobbyAsync(updateLobbyOptions);
            Modified = false;
        }

        /// <summary>
        /// Update the watchable properties of the session, ensuring that any subscriptions are triggered.
        /// </summary>
        void UpdateDerivedProperties()
        {
            Logger.LogVerbose($"UpdateDerivedProperties");
            Id = Lobby.Id;
            Name = Lobby.Name;
            IsHost = LobbyHandler.IsHost;
            Code = Lobby.LobbyCode;
            Host = Lobby.HostId;
            MaxPlayers = Lobby.MaxPlayers;
            IsLocked = Lobby.IsLocked;
            IsPrivate = Lobby.IsPrivate;
            AvailableSlots = Lobby.AvailableSlots;
            HasPassword = Lobby.HasPassword;

            // Handle removed players.
            Players?.RemoveAll(player => Lobby.Players.All(lobbyPlayer => lobbyPlayer?.Id != player?.Id));

            // Handle added or updated players.
            foreach (var player in Lobby?.Players ?? Enumerable.Empty<Lobbies.Models.Player>())
            {
                if (player == null)
                    continue;

                var convertedPlayerProperties = new Dictionary<string, PlayerProperty>();
                foreach (var property in player?.Data ??
                         Enumerable.Empty<KeyValuePair<string, Lobbies.Models.PlayerDataObject>>())
                {
                    convertedPlayerProperties.Add(property.Key,
                        new PlayerProperty(property.Value.Value, (VisibilityPropertyOptions)property.Value.Visibility));
                }

                var convertedPlayer = new Player(
                    this,
                    player?.Id,
                    player?.ConnectionInfo,
                    convertedPlayerProperties,
                    player?.AllocationId,
                    player?.Joined ?? default(DateTime),
                    player?.LastUpdated ?? default(DateTime)
                );

                var existingPlayerIndex = Players.FindIndex(p => p.Id == convertedPlayer.Id);
                if (existingPlayerIndex != -1)
                {
                    Players[existingPlayerIndex] = convertedPlayer;
                }
                else
                {
                    Players.Add(convertedPlayer);
                }
            }

            if (Properties != null && Lobby?.Data != null)
            {
                // Handle removed session properties.
                for (int i = Properties.Count - 1; i >= 0; i--)
                {
                    var key = Properties.Keys.ElementAt(i);

                    if (!Lobby.Data.ContainsKey(key))
                    {
                        Logger.LogVerbose($"UpdateDerivedProperties:: removing {key}");
                        Properties.Remove(key);
                    }
                }

                // Handle added or updated session properties.
                foreach (var key in Lobby.Data.Keys)
                {
                    if (!Properties.TryGetValue(key, out var property) || property.Value != Lobby.Data[key].Value)
                    {
                        Logger.LogVerbose($"UpdateDerivedProperties:: updating {key}");
                        Properties[key] = new SessionProperty(Lobby.Data[key].Value,
                            (VisibilityPropertyOptions)Lobby.Data[key].Visibility,
                            (PropertyIndex)Lobby.Data[key].Index);
                    }
                }
            }
        }

        private void OnLobbyExit()
        {
            LobbyHandler.LobbyChanged -= UpdateDerivedProperties;
        }

        void OnAuthenticationTokenChanged(string accessToken)
        {
            Logger.LogVerbose($"Session.OnAuthenticationTokenChanged");

            if (accessToken != null)
            {
                return;
            }

            SetState(SessionState.None);
        }

        void OnPlayerJoined(string playerId)
        {
            Logger.LogVerbose($"Session.OnPlayerJoined");
            PlayerJoined?.Invoke(playerId);
        }

        void OnPlayerLeaving(string playerId)
        {
            Logger.LogVerbose($"Session.OnPlayerLeaving");
            PlayerLeaving?.Invoke(playerId);
            PlayerLeft?.Invoke(playerId);
        }

        void OnPlayerHasLeft(string playerId)
        {
            Logger.LogVerbose($"Session.OnPlayerHasLeft");
            PlayerHasLeft?.Invoke(playerId);
        }

        void OnSessionPropertiesChanged()
        {
            Logger.LogVerbose($"Session.OnSessionPropertiesChanged");
            SessionPropertiesChanged?.Invoke();
        }

        void OnPlayerPropertiesChanged()
        {
            Logger.LogVerbose($"Session.OnPlayerPropertiesChanged");
            PlayerPropertiesChanged?.Invoke();
        }

        void OnDeleted()
        {
            Logger.LogVerbose($"Session.OnDeleted");
            Deleted?.Invoke();
        }

        void OnRemovedFromSession()
        {
            Logger.LogVerbose($"Session.OnRemovedFromSession");
            RemovedFromSession?.Invoke();
        }

        internal void OnLobbyHostChanged(string newHostId)
        {
            Logger.LogVerbose("Raising SessionHostChanged");
            SessionHostChanged?.Invoke(newHostId);
        }

        internal void OnLobbyChanged()
        {
            Logger.LogVerbose($"Session.OnLobbyChanged");

            // if the update is triggered by a lobby reset, remove the LobbyChanged event subscription.
            if (Lobby == null)
            {
                LobbyHandler.LobbyChanged -= OnLobbyChanged;
                LobbyHandler.LobbyHostChanged -= OnLobbyHostChanged;
                return;
            }

            UpdateDerivedProperties();
            Changed?.Invoke();
        }

        private async void OnQuitting()
        {
            Logger.LogVerbose("Session.OnQuitting");
            if (State != SessionState.Connected)
            {
                return;
            }

            try
            {
                await LeaveAsync();
            }
            catch (Exception e)
            {
                // ignored
                Logger.LogVerbose($"Exception was thrown! {e}");
            }
        }

        internal Dictionary<string, DataObject> SessionToLobbyProperties()
        {
            return Properties.ToDictionary(
                kvp => kvp.Key,
                kvp => LobbyConverter.ToSessionDataObject(kvp.Value));
        }

        void ISession.OnSessionMigrated()
        {
            SessionMigrated?.Invoke();
        }
    }
}
