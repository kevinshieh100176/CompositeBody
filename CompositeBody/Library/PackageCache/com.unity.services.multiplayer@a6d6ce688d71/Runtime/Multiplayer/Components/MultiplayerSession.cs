using System;
using Unity.Services.Core;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Holds session state and forwards session and player
    /// events. Use <see cref="SetSession"/> to assign a session.
    /// </summary>
    [CreateAssetMenu(fileName = "MultiplayerSession", menuName = "Services/Multiplayer/Multiplayer Session", order = 4)]
    public sealed class MultiplayerSession : ScriptableObject
    {
        const string k_DefaultSessionType = "Default";
        SessionObserver m_Observer;
        [NonSerialized] ISession m_Session;
        [NonSerialized] internal IUnityServices m_Services;

        [Tooltip(
            "Session type key used to identify and track this session (e.g. for create/join, events and for editor display).")]
        [SerializeField]
        string m_SessionType = k_DefaultSessionType;

        [Header("Events")][Tooltip("Adding started/failed, session added, removed, deleted.")][SerializeField]
        SessionLifecycleEvents m_SessionLifecycle = new();

        [Tooltip("Session changed, host changed, migrated, properties changed, state changed.")][SerializeField]
        SessionEvents m_SessionLifecycleEvents = new();

        [Tooltip("Player joined, leaving, left, properties changed.")][SerializeField]
        SessionPlayerEvents m_SessionPlayerEvents = new();

        /// <summary>
        /// The current session instance.
        /// </summary>
        public ISession Session => m_Session;

        /// <summary>
        /// Identifier for this session type.
        /// </summary>
        public string SessionType
        {
            get => m_SessionType;
            set
            {
                EnsureObserver();
                m_SessionType = value;
            }
        }

        /// <summary>Initializes the observer for lifecycle events. Safe to call multiple times.</summary>
        internal void EnsureObserver()
        {
            if (m_Observer?.SessionType == m_SessionType)
                return;

            UnregisterObserver();

            if (m_Services == null)
                return;
            m_Observer = new SessionObserver(m_SessionType, m_Services);
            m_Observer.AddingSessionStarted += AddingSessionStarted;
            m_Observer.AddingSessionFailed += AddingSessionFailed;
            m_Observer.SessionAdded += SessionAdded;
        }

        /// <summary>Lifecycle events: adding started/failed, session added, removed, deleted.</summary>
        public SessionLifecycleEvents SessionLifecycle => m_SessionLifecycle;

        /// <summary>Session events: changed, host changed, migrated, properties, state.</summary>
        public SessionEvents SessionLifecycleEvents => m_SessionLifecycleEvents;

        /// <summary>Player events: joined, leaving, left, properties changed.</summary>
        public SessionPlayerEvents SessionPlayerEvents => m_SessionPlayerEvents;

        /// <summary>Assigns the session; its events are forwarded to <see cref="SessionLifecycle"/>, <see cref="SessionLifecycleEvents"/>, and <see cref="SessionPlayerEvents"/>.</summary>
        internal void SetSession(ISession session)
        {
            UnsubscribeFromSession();
            m_Session = session;
            SubscribeToSession();
        }

        void SubscribeToSession()
        {
            if (m_Session == null)
            {
                Logger.LogVerbose(
                    $"[{nameof(MultiplayerSession)}] There is no session to subscribe to.");
                return;
            }

            // @formatter:off
            m_Session.Changed                  += Changed;
            m_Session.Deleted                  += Deleted;
            m_Session.PlayerHasLeft            += PlayerHasLeft;
            m_Session.PlayerJoined             += PlayerJoined;
            m_Session.PlayerLeaving            += PlayerLeaving;
            m_Session.PlayerPropertiesChanged  += PlayerPropertiesChanged;
            m_Session.RemovedFromSession       += RemovedFromSession;
            m_Session.SessionHostChanged       += SessionHostChanged;
            m_Session.SessionMigrated          += SessionMigrated;
            m_Session.SessionPropertiesChanged += SessionPropertiesChanged;
            m_Session.StateChanged             += StateChanged;
            // @formatter:on
        }

        void UnsubscribeFromSession()
        {
            if (m_Session == null)
            {
                Logger.LogVerbose(
                    $"[{nameof(MultiplayerSession)}] There is no session to unsubscribe from.");
                return;
            }

            var s = m_Session;
            m_Session = null;

            // @formatter:off
            s.Changed                  -= Changed;
            s.Deleted                  -= Deleted;
            s.PlayerHasLeft            -= PlayerHasLeft;
            s.PlayerJoined             -= PlayerJoined;
            s.PlayerLeaving            -= PlayerLeaving;
            s.PlayerPropertiesChanged  -= PlayerPropertiesChanged;
            s.RemovedFromSession       -= RemovedFromSession;
            s.SessionHostChanged       -= SessionHostChanged;
            s.SessionMigrated          -= SessionMigrated;
            s.SessionPropertiesChanged -= SessionPropertiesChanged;
            s.StateChanged             -= StateChanged;
            // @formatter:on
        }

        void Changed()
        {
            if (Session != null)
                m_SessionLifecycleEvents.Changed?.Invoke(Session);
        }

        void Deleted()
        {
            if (Session != null)
                m_SessionLifecycle.Deleted?.Invoke(Session);
        }

        void PlayerHasLeft(string playerId)
        {
            if (Session != null)
                m_SessionPlayerEvents.PlayerHasLeft?.Invoke(Session, playerId);
        }

        void PlayerJoined(string playerId)
        {
            if (Session != null)
                m_SessionPlayerEvents.PlayerJoined?.Invoke(Session, playerId);
        }

        void PlayerLeaving(string playerId)
        {
            if (Session != null)
                m_SessionPlayerEvents.PlayerLeaving?.Invoke(Session, playerId);
        }

        void PlayerPropertiesChanged()
        {
            if (Session != null)
                m_SessionPlayerEvents.PlayerPropertiesChanged?.Invoke(Session);
        }

        void RemovedFromSession()
        {
            var session = Session;
            if (session != null)
                m_SessionLifecycle.RemovedFromSession?.Invoke(session);
        }

        void SessionHostChanged(string hostId)
        {
            if (Session != null)
                m_SessionLifecycleEvents.SessionHostChanged?.Invoke(Session, hostId);
        }

        void SessionMigrated()
        {
            if (Session != null)
                m_SessionLifecycleEvents.SessionMigrated?.Invoke(Session);
        }

        void SessionPropertiesChanged()
        {
            if (Session != null)
                m_SessionLifecycleEvents.SessionPropertiesChanged?.Invoke(Session);
        }

        void StateChanged(SessionState state)
        {
            if (Session != null)
                m_SessionLifecycleEvents.StateChanged?.Invoke(Session, state);
        }

        void AddingSessionStarted(AddingSessionOptions options)
        {
            m_SessionLifecycle.AddingSessionStarted?.Invoke(m_SessionType);
        }

        void AddingSessionFailed(AddingSessionOptions options, SessionException sessionException)
        {
            m_SessionLifecycle.AddingSessionFailed?.Invoke(m_SessionType,
                sessionException?.Message ?? "Unknown error");
        }

        void SessionAdded(ISession session)
        {
            m_SessionLifecycle.SessionAdded?.Invoke(session);
        }

        internal void ResetServices(IUnityServices unityServices = null)
        {
            m_Services = unityServices ?? UnityServices.Instance;
            EnsureObserver();
        }

        void OnEnable()
        {
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
            UnsubscribeFromSession();
            UnregisterObserver();
        }

        void UnregisterObserver()
        {
            if (m_Observer != null)
            {
                m_Observer.AddingSessionStarted -= AddingSessionStarted;
                m_Observer.AddingSessionFailed -= AddingSessionFailed;
                m_Observer.SessionAdded -= SessionAdded;
                m_Observer.Dispose();
                m_Observer = null;
            }
        }

#if UNITY_EDITOR
        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Inspector can keep stale session UI if it stays focused when leaving Play Mode so we need to make sure to remove it.
            if (state is PlayModeStateChange.ExitingPlayMode)
            {
                UnsubscribeFromSession();
                UnregisterObserver();
            }
        }
#endif
    }
}
