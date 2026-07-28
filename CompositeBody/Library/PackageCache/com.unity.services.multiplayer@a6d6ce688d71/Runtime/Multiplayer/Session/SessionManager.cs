using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication.Internal;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.Lobbies;

namespace Unity.Services.Multiplayer
{
    interface ISessionManager
    {
        public event Action<ISession> SessionAdded;
        public event Action<ISession> SessionRemoved;

        Dictionary<string, ISession> Sessions { get; }
        Task<SessionHandler> GetAsync(string sessionId, SessionOptions sessionOptions = null);
        Task<SessionHandler> CreateAsync(SessionOptions sessionOptions);
        Task<SessionHandler> CreateOrJoinAsync(string sessionId, SessionOptions sessionOptions);
        Task<SessionHandler> JoinByCodeAsync(string sessionCode, JoinSessionOptions sessionOptions);
        Task<SessionHandler> JoinByIdAsync(string sessionCode, JoinSessionOptions sessionOptions);
        Task<SessionHandler> QuickJoinAsync(QuickJoinOptions quickJoinOptions, SessionOptions sessionOptions);
        Task<SessionHandler> ReconnectAsync(string sessionId, ReconnectSessionOptions options = default);
        Task<List<string>> GetJoinedSessionIdsAsync();
    }

    class SessionManager : ISessionManager
    {
        public event Action<ISession> SessionAdded;
        public event Action<ISession> SessionRemoved;

        public Dictionary<string, ISession> Sessions { get; } = new Dictionary<string, ISession>();

        readonly IPlayerId m_PlayerId;
        readonly IPlayerNameComponent m_PlayerName;

        readonly IActionScheduler m_ActionScheduler;
        readonly IModuleRegistry m_ModuleRegistry;
        readonly ILobbyBuilder m_LobbyBuilder;
        readonly IAccessTokenObserver m_AccessTokenObserver;

        public SessionManager(
            IActionScheduler actionScheduler,
            IModuleRegistry moduleRegistry,
            ILobbyBuilder lobbyBuilder,
            IPlayerId playerId,
            IPlayerNameComponent playerName,
            IAccessTokenObserver accessTokenObserver)
        {
            m_ActionScheduler = actionScheduler;
            m_ModuleRegistry = moduleRegistry;
            m_LobbyBuilder = lobbyBuilder;
            m_PlayerId = playerId;
            m_PlayerName = playerName;
            m_AccessTokenObserver = accessTokenObserver;
        }

        public async Task<SessionHandler> GetAsync(string sessionId, SessionOptions sessionOptions = null)
        {
            var lobbyHandler = m_LobbyBuilder.Build();

            await lobbyHandler.GetLobbyAsync(sessionId);

            return await SetupSessionAsync(lobbyHandler, sessionOptions ?? new SessionOptions());
        }

        public async Task<SessionHandler> CreateAsync(SessionOptions sessionOptions)
        {
            Logger.LogVerbose("SessionHandler.CreateAsync");
            ValidateOptions(sessionOptions);
            ValidateCreationOptions(sessionOptions);
            PreprocessOptions(sessionOptions);

            var lobbyHandler = m_LobbyBuilder.Build();

            await lobbyHandler.CreateLobbyAsync(sessionOptions.Name, sessionOptions.MaxPlayers, new CreateLobbyOptions()
            {
                IsPrivate = sessionOptions.IsPrivate,
                Password = sessionOptions.Password,
                IsLocked = sessionOptions.IsLocked,
                Data = sessionOptions.SessionProperties?.ToDictionary(propertyData => propertyData.Key,
                    propertyData => LobbyConverter.ToSessionDataObject(propertyData.Value)),
                Player = LobbyConverter.ToLobbyPlayer(m_PlayerId, sessionOptions.PlayerProperties)
            });

            return await SetupSessionAsync(lobbyHandler, sessionOptions, sessionOptions.SessionProperties);
        }

        public async Task<SessionHandler> CreateOrJoinAsync(string sessionId, SessionOptions sessionOptions)
        {
            Logger.LogVerbose($"SessionManager.CreateOrJoinAsync: {sessionId}");
            ValidateOptions(sessionOptions);
            ValidateCreationOptions(sessionOptions);
            PreprocessOptions(sessionOptions);

            var lobbyHandler = m_LobbyBuilder.Build();

            await lobbyHandler.CreateOrJoinLobbyAsync(sessionId, sessionOptions.Name, sessionOptions.MaxPlayers,
                new CreateLobbyOptions()
                {
                    IsPrivate = sessionOptions.IsPrivate,
                    Password = sessionOptions.Password,
                    IsLocked = sessionOptions.IsLocked,
                    Data = sessionOptions.SessionProperties?.ToDictionary(propertyData => propertyData.Key,
                        propertyData => LobbyConverter.ToSessionDataObject(propertyData.Value)),
                    Player = LobbyConverter.ToLobbyPlayer(m_PlayerId, sessionOptions.PlayerProperties)
                });

            return await SetupSessionAsync(lobbyHandler, sessionOptions, sessionOptions.SessionProperties);
        }

        public async Task<SessionHandler> JoinByCodeAsync(string sessionCode, JoinSessionOptions sessionOptions)
        {
            Logger.LogVerbose("SessionManager.JoinByCode");
            sessionOptions ??= new JoinSessionOptions();
            ValidateOptions(sessionOptions);
            sessionCode = sessionCode.Trim();
            PreprocessOptions(sessionOptions);

            var lobbyHandler = m_LobbyBuilder.Build();

            await lobbyHandler.JoinLobbyByCodeAsync(sessionCode,
                new JoinLobbyByCodeOptions()
                {
                    Player = LobbyConverter.ToLobbyPlayer(m_PlayerId, sessionOptions?.PlayerProperties),
                    Password = sessionOptions.Password
                });

            return await SetupSessionAsync(lobbyHandler, sessionOptions);
        }

        public async Task<SessionHandler> JoinByIdAsync(string sessionId, JoinSessionOptions sessionOptions)
        {
            Logger.LogVerbose("SessionManager.JoinById");
            sessionOptions ??= new JoinSessionOptions();
            ValidateOptions(sessionOptions);
            PreprocessOptions(sessionOptions);
            sessionId = sessionId.Trim();

            var lobbyHandler = m_LobbyBuilder.Build();

            await lobbyHandler.JoinLobbyByIdAsync(sessionId,
                new JoinLobbyByIdOptions()
                {
                    Player = LobbyConverter.ToLobbyPlayer(m_PlayerId, sessionOptions?.PlayerProperties),
                    Password = sessionOptions.Password
                });

            return await SetupSessionAsync(lobbyHandler, sessionOptions);
        }

        public async Task<SessionHandler> QuickJoinAsync(QuickJoinOptions quickJoinOptions,
            SessionOptions sessionOptions)
        {
            Logger.LogVerbose("SessionManager.QuickMatchmakeSession");
            quickJoinOptions ??= new QuickJoinOptions();
            sessionOptions ??= new SessionOptions();
            ValidateOptions(sessionOptions);

            if (quickJoinOptions.CreateSession)
            {
                ValidateCreationOptions(sessionOptions);
            }

            PreprocessOptions(sessionOptions);

            var lobbyHandler = m_LobbyBuilder.Build();

            var quickJoinLobbyOptions = new QuickJoinLobbyOptions()
            {
                Player = LobbyConverter.ToLobbyPlayer(m_PlayerId, sessionOptions.PlayerProperties),
                Filter = LobbyConverter.ToQueryFilters(quickJoinOptions.Filters)
            };

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while (true)
            {
                try
                {
                    await lobbyHandler.QuickJoinLobbyAsync(quickJoinLobbyOptions);
                    return await SetupSessionAsync(lobbyHandler, sessionOptions);
                }
                catch (Exception)
                {
                    if (stopwatch.Elapsed < quickJoinOptions.Timeout)
                    {
                        await WaitForSeconds(1);
                    }
                    else if (quickJoinOptions.CreateSession)
                    {
                        return await CreateAsync(sessionOptions);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        public async Task<SessionHandler> ReconnectAsync(string sessionId, ReconnectSessionOptions options = default)
        {
            Logger.LogVerbose("SessionManager.ReconnectAsync");

            var lobbyHandler = m_LobbyBuilder.Build();
            var reconnectOptions = new SessionOptions();
            if (options != default)
            {
                reconnectOptions.Type = options.Type;
            }

            if (string.IsNullOrWhiteSpace(reconnectOptions.Type))
            {
                throw new SessionException($"Session type is required if ReconnectSessionOptions is provided.", SessionError.InvalidParameter);
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new SessionException($"Session Id is required.", SessionError.InvalidParameter);
            }

            await lobbyHandler.ReconnectToLobbyAsync(sessionId);

            if (options?.NetworkHandler != null)
            {
                reconnectOptions.WithNetworkHandler(options.NetworkHandler);
            }

            return await SetupSessionAsync(lobbyHandler, reconnectOptions);
        }

        public async Task<List<string>> GetJoinedSessionIdsAsync()
        {
            Logger.LogVerbose("SessionManager.GetJoinedSessionIdsAsync");

            var lobbyHandler = m_LobbyBuilder.Build();

            return await lobbyHandler.GetJoinedLobbiesAsync();
        }

        async Task<SessionHandler> SetupSessionAsync(
            ILobbyHandler lobbyHandler,
            BaseSessionOptions options,
            Dictionary<string, SessionProperty> sessionProperties = null)
        {
            var session = CreateSessionHandler(lobbyHandler, options);

            if (session.IsHost)
            {
                session.SetProperties(sessionProperties);
            }

            ProcessOptions(session, options);
            await InitializeModulesAsync(session);

            if (session.IsHost)
            {
                await session.SavePropertiesAsync();
            }

            if (session.CurrentPlayer != null)
            {
                await session.SaveCurrentPlayerDataAsync();
            }

            RegisterSession(session, options.Type);
            return session;
        }

        internal void PreprocessOptions(BaseSessionOptions options)
        {
            if (options.HasOption<PlayerNameSessionOption>())
            {
                if (string.IsNullOrEmpty(m_PlayerName.PlayerName))
                {
                    throw new SessionException("Trying to set the player name in the session but it is not set.", SessionError.InvalidPlayerName);
                }

                var playerNameOption = options.GetOption<PlayerNameSessionOption>();
                options.PlayerProperties ??= new Dictionary<string, PlayerProperty>();
                options.PlayerProperties[PlayerNameModule.PropertyKey] = new PlayerProperty(m_PlayerName.PlayerName, playerNameOption.Visibility);
            }
        }

        SessionHandler CreateSessionHandler(ILobbyHandler lobbyHandler,
            BaseSessionOptions options)
        {
            var session = new SessionHandler(options.Type, m_ActionScheduler, lobbyHandler, m_AccessTokenObserver,
                m_PlayerId);

            var moduleProviders = m_ModuleRegistry?.ModuleProviders;

            if (moduleProviders != null)
            {
                foreach (var provider in moduleProviders)
                {
                    Logger.LogVerbose($"Registering session module: '{provider.Type}'");
                    var module = provider.Build(session);
                    session.RegisterModule(provider.Type, module);
                }
            }

            session.Deleted += () => OnSessionDeleted(session);
            session.RemovedFromSession += async() => await OnRemovedFromSession(session);
            return session;
        }

        async Task InitializeModulesAsync(SessionHandler session)
        {
            try
            {
                await session.InitializeModulesAsync();
            }
            catch (SessionException)
            {
                await CleanupSessionAsync(session);
                throw;
            }
            catch (Exception e)
            {
                await CleanupSessionAsync(session);
                throw new SessionException($"Failed to initialize session modules: {e.Message}", SessionError.Unknown, e);
            }
        }

        async Task CleanupSessionAsync(SessionHandler session, bool cleanupAndLeave = true)
        {
            try
            {
                if (cleanupAndLeave)
                {
                    // Keep current behavior
                    await session.LeaveAsync();
                }
                else
                {
                    // Cleanup the session without triggering leave nor associated events
                    await session.CleanupAsync();
                }
            }
            catch (Exception e)
            {
                Logger.LogVerbose($"Exception was thrown! {e}");
            }
        }

        void ValidateOptions(BaseSessionOptions options)
        {
            Logger.LogVerbose($"ValidateOptions");

            if (options == null)
            {
                throw new SessionException($"Missing session options.", SessionError.InvalidParameter);
            }

            if (Sessions.ContainsKey(options.Type))
            {
                throw new SessionException($"A session for type: {options.Type} is already registered.",
                    SessionError.SessionTypeAlreadyExists);
            }
        }

        void ValidateCreationOptions(SessionOptions options)
        {
            Logger.LogVerbose($"ValidateCreationOptions");

            if (options == null)
            {
                throw new SessionException($"Session options are required.", SessionError.InvalidParameter);
            }

            if (options.MaxPlayers <= 0)
            {
                throw new SessionException($"A valid MaxPlayers is required for session create.",
                    SessionError.InvalidParameter);
            }
        }

        void ProcessOptions(SessionHandler session, BaseSessionOptions sessionOptions)
        {
            Logger.LogVerbose($"ProcessOptions");
            if (sessionOptions != null)
            {
                foreach (var options in sessionOptions.Options)
                {
                    Logger.LogVerbose($"Process Options: '{options.Key}'");
                    try
                    {
                        options.Value.Process(session);
                    }
                    catch (Exception e)
                    {
                        Logger.LogError($"Process Options Exception: {e.Message}");
                    }
                }
            }
        }

        void RegisterSession(ISession session, string sessionType)
        {
            Logger.LogVerbose($"RegisterSession: {sessionType}");
            Sessions[sessionType] = session;

            m_ActionScheduler.ScheduleAction(() =>
            {
                if (session.State != SessionState.Deleted)
                {
                    session.RefreshAsync();
                }
            }, 2);

            // Ensure that the event handlers are triggered outside the awaited Session Operation's scope
            m_ActionScheduler.ScheduleAction(() =>
            {
                SessionAdded?.Invoke(session);
            });
        }

        async Task WaitForSeconds(double seconds)
        {
            var tcs = new TaskCompletionSource<object>();
            m_ActionScheduler.ScheduleAction(() => tcs.SetResult(null), seconds);
            await tcs.Task;
        }

        void OnSessionDeleted(ISession session)
        {
            Sessions.Remove(session.Type);
            SessionRemoved?.Invoke(session);
        }

        async Task OnRemovedFromSession(ISession session)
        {
            // Cleanup the session we were kicked from (already left)
            await CleanupSessionAsync((SessionHandler)session, cleanupAndLeave: false);
            Sessions.Remove(session.Type);
            SessionRemoved?.Invoke(session);
        }
    }
}
