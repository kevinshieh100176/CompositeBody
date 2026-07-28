using System;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Core.Internal;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Runs a create, create-or-join, or join session connector.
    /// </summary>
    /// <seealso cref="SessionConnectorType"/>
    /// <seealso cref="MultiplayerSession"/>
    /// <seealso cref="SessionConnectorBehaviour"/>
    public sealed class SessionConnector : ScriptableObject
    {
        [Tooltip(
            "The MultiplayerSession to assign the created or joined session to.")]
        [SerializeField]
        private MultiplayerSession m_MultiplayerSession;

        [Tooltip("Whether to create a new session, create or join by id, or join an existing one.")] [SerializeField]
        private SessionConnectorType m_ConnectorType = SessionConnectorType.Create;

        [Tooltip("Session ID for Create Or Join mode. The session id to create or join.")]
        [SerializeField]
        [Visibility(nameof(m_ConnectorType), SessionConnectorType.CreateOrJoin)]
        private string m_CreateOrJoinSessionId = string.Empty;

        [Tooltip(
            "Session options for Create Session and Create Or Join modes (name, max players, privacy, password, etc.).")]
        [SerializeField]
        private CreateSessionOptions m_CreateSessionOptions = new CreateSessionOptions();

        [Tooltip("Network type and options for Create Session and Create Or Join modes.")] [SerializeField]
        private SessionNetworkSettings m_SessionNetworkSettings = new SessionNetworkSettings();

        [Tooltip("Join options for Join Session mode (session id or code, password).")] [SerializeField]
        //Visibility(nameof(m_ConnectorType), SessionConnectorType.Join)]
        private JoinSessionOptions m_JoinSessionOptions = new JoinSessionOptions();

        [Tooltip("Connector events.")] [SerializeField]
        private SessionConnectorEvents m_Events = new SessionConnectorEvents();

        private Task m_ConnectionTask;

        /// <summary>
        /// The <see cref="MultiplayerSession"/> asset that receives the created or joined session.
        /// </summary>
        public MultiplayerSession MultiplayerSession
        {
            get => m_MultiplayerSession;
            set => m_MultiplayerSession = value;
        }

        /// <summary>
        /// Type of the session connector to configure and execute.
        /// </summary>
        /// <seealso cref="SessionConnectorType"/>
        public SessionConnectorType ConnectorType { get => m_ConnectorType; set => m_ConnectorType = value; }

        /// <summary>
        /// The session ID used when <see cref="Connector"/> is <see cref="SessionConnectorType.CreateOrJoin"/>.
        /// </summary>
        public string CreateOrJoinSessionId
        {
            get => m_CreateOrJoinSessionId;
            set => m_CreateOrJoinSessionId = value ?? string.Empty;
        }

        /// <summary>
        /// The options for session creation (name, max players, privacy, password).
        /// </summary>
        /// <seealso cref="CreateSessionOptions"/>
        internal CreateSessionOptions SessionOptions
        {
            get => m_CreateSessionOptions;
            set => m_CreateSessionOptions = value ?? new CreateSessionOptions();
        }

        /// <summary>
        /// The network configuration for the session (direct IP/port or relay).
        /// </summary>
        /// <seealso cref="NetworkOptionsSection"/>
        internal SessionNetworkSettings SessionNetwork
        {
            get => m_SessionNetworkSettings;
            set => m_SessionNetworkSettings = value ?? new SessionNetworkSettings();
        }

        /// <summary>
        /// The options for joining a session by ID or join code.
        /// </summary>
        /// <seealso cref="Components.JoinSessionOptions"/>
        internal JoinSessionOptions JoinSessionOptions
        {
            get => m_JoinSessionOptions;
            set => m_JoinSessionOptions = value ?? new JoinSessionOptions();
        }

        /// <summary>
        /// The events raised during the connector lifecycle (started, success, failure).
        /// </summary>
        /// <seealso cref="SessionConnectorEvents"/>
        public SessionConnectorEvents Events
        {
            get => m_Events;
            set => m_Events = value ?? new SessionConnectorEvents();
        }

        /// <summary>
        /// Raised when <see cref="Execute"/> is called. The string parameter is the session type.
        /// </summary>
        public UnityEvent<string> ExecutionStarted => m_Events.ExecutionStarted;

        /// <summary>
        /// Raised when the connector execution completes successfully.
        /// The parameter is the created or joined <see cref="ISession"/>.
        /// </summary>
        public UnityEvent<ISession> SuccessfulExecution => m_Events.SuccessfulExecution;

        /// <summary>
        /// Raised when the connector execution fails. The string parameter is the error message.
        /// </summary>
        public UnityEvent<string> FailedExecution => m_Events.FailedExecution;

        private string SessionType => m_MultiplayerSession?.SessionType;
        private IMultiplayerService m_MultiplayerService;

        /// <summary>
        /// Runs the session connector with the current settings (create,
        /// create-or-join, or join as specified by <see cref="Connector"/>),
        /// using the multiplayer service from <see cref="UnityServices"/>.
        /// </summary>
        /// <remarks>
        /// Parameterless so this overload can be bound to a <see
        /// cref="UnityEngine.Events.UnityEvent"/> in the Inspector.
        /// Results are reported via <see cref="ExecutionStarted"/>, <see
        /// cref="SuccessfulExecution"/>, and <see cref="FailedExecution"/>.
        /// </remarks>
        /// <exception cref="SessionException">When Multiplayer Session is null.</exception>
        public void Execute()
        {
            Execute(null);
        }

        /// <summary>
        /// Runs the session connector as specified by <see cref="Connector"/>,
        /// resolving <see cref="IMultiplayerService"/> from the given Unity Services instance.
        /// </summary>
        /// <param name="servicesRegistry">
        /// The <see cref="IUnityServices"/> registry used to obtain <see cref="IMultiplayerService"/>
        /// and associated with the linked <see cref="MultiplayerSession"/>.
        /// When <c>null</c>, <see cref="IMultiplayerService"/> is resolved from <see cref="UnityServices"/>.
        /// </param>
        /// <remarks>
        /// Results are reported via <see cref="ExecutionStarted"/>, <see
        /// cref="SuccessfulExecution"/>, and <see cref="FailedExecution"/>.
        /// </remarks>
        public void Execute(IUnityServices servicesRegistry = default)
        {
            m_Events.ExecutionStarted?.Invoke(SessionType ?? string.Empty);

            servicesRegistry ??= UnityServices.Instance;
            if (!IsValid(m_MultiplayerSession) || !IsValid(servicesRegistry))
            {
                return;
            }

            m_MultiplayerSession.ResetServices(servicesRegistry);
            RunExecute(servicesRegistry);
        }

        void RunExecute(IUnityServices servicesRegistry)
        {
            try
            {
                m_MultiplayerService = servicesRegistry.GetMultiplayerService();

                if (m_ConnectionTask is { IsCompleted: false })
                {
                    Logger.LogCallWarning(nameof(SessionConnector), "Connection already in progress.", this);
                    return;
                }

                m_ConnectionTask = ExecuteAsync();
            }
            catch (Exception e)
            {
                InvokeFailed(e.Message);
            }
        }

        private Task ExecuteAsync()
        {
            if (m_MultiplayerSession == null)
            {
                InvokeFailed(
                    "A Multiplayer Session is required. Assign one in the Inspector.");
                return Task.CompletedTask;
            }

            if (m_MultiplayerService == null)
            {
                InvokeFailed(
                    "Unity Services are not initialized. Ensure the project is linked and services are started.");
                return Task.CompletedTask;
            }

            return (m_ConnectorType) switch
            {
                SessionConnectorType.Create => ExecuteCreateAsync(),
                SessionConnectorType.CreateOrJoin => ExecuteCreateOrJoinAsync(),
                _ => Task.CompletedTask
            };
        }

        private void InvokeFailed(string message)
        {
            Logger.LogCallWarning(nameof(SessionConnector), message, this);
            m_Events.FailedExecution?.Invoke(message);
        }

        private async Task ExecuteCreateAsync()
        {
            try
            {
                var options = BuildSessionOptions();
                var hostSession = await m_MultiplayerService.CreateSessionAsync(options);
                m_MultiplayerSession.SetSession(hostSession);
                m_Events.SuccessfulExecution?.Invoke(hostSession);
            }
            catch (SessionException e)
            {
                InvokeFailed(e.Message);
            }
            catch (Exception e)
            {
                InvokeFailed(e.Message);
            }
        }

        private async Task ExecuteCreateOrJoinAsync()
        {
            var sessionId = string.IsNullOrWhiteSpace(m_CreateOrJoinSessionId) ? null : m_CreateOrJoinSessionId.Trim();
            if (string.IsNullOrEmpty(sessionId))
            {
                InvokeFailed("Session ID is required for Create Or Join. Enter the session id in the Inspector.");
                return;
            }

            try
            {
                var options = BuildSessionOptions();
                var session = await m_MultiplayerService.CreateOrJoinSessionAsync(sessionId, options);
                m_MultiplayerSession.SetSession(session);
                m_Events.SuccessfulExecution?.Invoke(session);
            }
            catch (SessionException e)
            {
                InvokeFailed(e.Message);
            }
            catch (Exception e)
            {
                InvokeFailed(e.Message);
            }
        }

        private async Task ExecuteJoinAsync()
        {
            var joinOptions = BuildJoinSessionOptions();
            try
            {
                ISession session;
                if (m_JoinSessionOptions.JoinMode == JoinSessionMode.ById)
                {
                    var sessionId = string.IsNullOrWhiteSpace(m_JoinSessionOptions.SessionId)
                        ? null
                        : m_JoinSessionOptions.SessionId.Trim();
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        InvokeFailed("Session ID is required when joining by ID.");
                        return;
                    }

                    session = await m_MultiplayerService.JoinSessionByIdAsync(sessionId, joinOptions);
                }
                else
                {
                    var sessionCode = string.IsNullOrWhiteSpace(m_JoinSessionOptions.SessionCode)
                        ? null
                        : m_JoinSessionOptions.SessionCode.Trim();
                    if (string.IsNullOrEmpty(sessionCode))
                    {
                        InvokeFailed("Session code is required when joining by code.");
                        return;
                    }

                    session = await m_MultiplayerService.JoinSessionByCodeAsync(sessionCode, joinOptions);
                }

                m_MultiplayerSession.SetSession(session);
                m_Events.SuccessfulExecution?.Invoke(session);
            }
            catch (SessionException e)
            {
                InvokeFailed(e.Message);
            }
            catch (Exception e)
            {
                InvokeFailed(e.Message);
            }
        }

        private Multiplayer.JoinSessionOptions BuildJoinSessionOptions()
        {
            var joinOptions = new Multiplayer.JoinSessionOptions
            {
                Type = string.IsNullOrWhiteSpace(SessionType) ? null : SessionType,
                Password = string.IsNullOrWhiteSpace(m_JoinSessionOptions.Password)
                    ? null
                    : m_JoinSessionOptions.Password.Trim()
            };
            return joinOptions;
        }

        private SessionOptions BuildSessionOptions()
        {
            var sessionOptions = m_CreateSessionOptions;
            var options = new SessionOptions
            {
                Type = string.IsNullOrWhiteSpace(SessionType) ? Guid.NewGuid().ToString() : SessionType,
                Name =
                    string.IsNullOrWhiteSpace(sessionOptions.SessionName)
                        ? Guid.NewGuid().ToString()
                        : sessionOptions.SessionName,
                MaxPlayers = sessionOptions.MaxPlayers,
                IsPrivate = sessionOptions.IsPrivate,
                IsLocked = sessionOptions.IsLocked,
                Password = string.IsNullOrWhiteSpace(m_CreateSessionOptions.Password)
                    ? null
                    : m_CreateSessionOptions.Password.Trim()
            };

            if (!m_SessionNetworkSettings.CreateNetwork ||
                m_SessionNetworkSettings.Network == SessionNetworkSettings.NetworkType.None)
            {
                return options;
            }

            switch (m_SessionNetworkSettings.Network)
            {
                case SessionNetworkSettings.NetworkType.Direct:
                    options = options.WithDirectNetwork(m_SessionNetworkSettings.DirectIPPort.ListenIp,
                        m_SessionNetworkSettings.DirectIPPort.PublishIp,
                        m_SessionNetworkSettings.DirectIPPort.Port);
                    break;
                case SessionNetworkSettings.NetworkType.Relay:
                    options = options.WithNetworkOptions(new NetworkOptions()
                        {
                            RelayProtocol = m_SessionNetworkSettings.RelayOptions.RelayProtocol
                        })
                        .WithRelayNetwork(m_SessionNetworkSettings.RelayOptions.ToRelayNetworkOptions());
                    break;
            }

            return options;
        }

        private bool IsValid(MultiplayerSession multiplayerSession)
        {
            if (multiplayerSession is not null)
            {
                return true;
            }

            InvokeFailed("Multiplayer Session must not be null.");
            return false;
        }

        private bool IsValid(IUnityServices registry)
        {
            if (registry is not null)
            {
                return true;
            }

            InvokeFailed("Unity Services have not been initialized yet, use UnityServices.InitializeAsync() first.");
            return false;
        }
    }
}
