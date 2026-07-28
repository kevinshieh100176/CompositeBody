using System;
using Unity.Services.Core;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// This class can be used to track events related to an <see
    /// cref="ISession"/> of a specific <see cref="ISession.Type"/>.
    /// </summary>
    /// <remarks>
    /// Use this class to get events when a session is created, pending creation
    /// or failed to be created for a specific session type.<br/> It is possible
    /// to create this class independently of the <see cref="UnityServices"/>
    /// being initialized, allowing independent system in a project to track
    /// session related events without handling the <see cref="UnityServices"/>
    /// and <see cref="MultiplayerService"/> states themselves.
    /// </remarks>
    public class SessionObserver : IDisposable
    {
        ServiceObserver<IMultiplayerService> m_ServiceObserver;
        IMultiplayerService m_MultiplayerService;

        /// <summary>
        /// The session type tracked by this observer.
        /// </summary>
        public readonly string SessionType;

        /// <summary>
        /// This event is called when a <see cref="ISession"/> is added successfully
        /// with a <see cref="ISession.Type"/> value equal to <see cref="SessionType"/>.
        /// </summary>
        public event Action<ISession> SessionAdded;

        /// <summary>
        /// This event is called when the <see cref="IMultiplayerService"/>
        /// instance is starting to create or join a <see cref="ISession"/>
        /// using a <see cref="BaseSessionOptions.Type"/> value equal to <see
        /// cref="SessionType"/>.
        /// </summary>
        public event Action<AddingSessionOptions> AddingSessionStarted;

        /// <summary>
        /// This event is called when the <see cref="IMultiplayerService"/>
        /// instance fails to create or join a <see cref="ISession"/> using a
        /// <see cref="BaseSessionOptions.Type"/> value equal to <see
        /// cref="SessionType"/>.
        /// </summary>
        public event Action<AddingSessionOptions, SessionException> AddingSessionFailed;

        /// <summary>
        /// Returns the current <see cref="ISession"/> with a matching <see
        /// cref="SessionType"/> if it exists.
        /// </summary>
        public ISession Session { get; private set; } = null;

        /// <summary>
        /// Creates a new <see cref="SessionObserver"/> instance that will track
        /// the creation of a session of the given <paramref
        /// name="sessionType"/>.
        /// </summary>
        /// <param name="sessionType">The <see cref="ISession.Type"/> to
        /// track.</param>
        /// <param name="registry">Optional parameter to specify a custom <see
        /// cref="IUnityServices"/> to register to.</param>
        /// <seealso cref="UnityServices.CreateServices()"/>
        public SessionObserver(string sessionType, IUnityServices registry)
        {
            SessionType = sessionType;
            // It is possible and valid to create an observer with a null registry when in Edit mode.
            // In this specific case, the observer will do nothing but should not throw an exception.
            if (registry == null)
                return;
            m_ServiceObserver = new ServiceObserver<IMultiplayerService>(registry);
            if (m_ServiceObserver.Service == null)
            {
                m_ServiceObserver.Initialized += OnServiceInitialized;
            }
            else
            {
                OnServiceInitialized(m_ServiceObserver.Service);
            }
        }

        /// <summary>
        /// Creates a new <see cref="SessionObserver"/> instance that will track
        /// the creation of a session of the given <paramref
        /// name="sessionType"/>.
        /// </summary>
        /// <param name="sessionType">The <see cref="ISession.Type"/> to
        /// track.</param>
        public SessionObserver(string sessionType) : this(sessionType,
            UnityServices.Instance)
        {
        }

        void OnServiceInitialized(IMultiplayerService multiplayerService)
        {
            m_MultiplayerService = multiplayerService;

            m_MultiplayerService.SessionAdded += OnSessionAdded;
            m_MultiplayerService.AddingSessionStarted += OnAddingSessionStarted;
            m_MultiplayerService.AddingSessionFailed += OnAddingSessionFailed;
            foreach (var(_, session) in m_MultiplayerService.Sessions)
            {
                OnSessionAdded(session);
            }
        }

        void OnAddingSessionStarted(AddingSessionOptions options)
        {
            if (SessionType != options.Type)
            {
                return;
            }

            AddingSessionStarted?.Invoke(options);
        }

        void OnAddingSessionFailed(AddingSessionOptions options, SessionException sessionException)
        {
            if (SessionType != options.Type)
            {
                return;
            }

            AddingSessionFailed?.Invoke(options, sessionException);
        }

        void OnSessionAdded(ISession session)
        {
            if (session.Type != SessionType)
            {
                return;
            }

            Session = session;
            Session.RemovedFromSession += CleanupSession;
            Session.Deleted += CleanupSession;
            SessionAdded?.Invoke(session);
        }

        void CleanupObserver()
        {
            m_ServiceObserver.Initialized -= OnServiceInitialized;
            m_ServiceObserver.Dispose();
            m_ServiceObserver = null;
        }

        void CleanupMultiplayerServices()
        {
            m_MultiplayerService.SessionAdded -= OnSessionAdded;
            m_MultiplayerService.AddingSessionStarted -= OnAddingSessionStarted;
            m_MultiplayerService.AddingSessionFailed -= OnAddingSessionFailed;
            m_MultiplayerService = null;
        }

        void CleanupSession()
        {
            Session.RemovedFromSession -= CleanupSession;
            Session.Deleted -= CleanupSession;
            Session = null;
        }

        /// <summary>
        /// Cleans up all currently registered events tracking the <see
        /// cref="SessionType"/> sessions.
        /// </summary>
        public void Dispose()
        {
            if (m_ServiceObserver != null)
            {
                CleanupObserver();
            }
            if (m_MultiplayerService != null)
            {
                CleanupMultiplayerServices();
            }
            if (Session != null)
            {
                CleanupSession();
            }
        }
    }
}
