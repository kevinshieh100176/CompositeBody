using System;
using Unity.Services.Multiplayer.Components;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Unity.Services.Multiplayer.Editor.Components
{
    [CustomEditor(typeof(MultiplayerSession))]
    class MultiplayerSessionEditor : UnityEditor.Editor
    {
        const string k_HiddenClass = "hidden";
        const string k_NotConnectedLabelClass = "not-connected-label";
        const string k_SessionViewerHeaderClass = "session-viewer-header";

        const string k_SessionIdentifier = "Session Identifier";
        const string k_SessionViewerHeader = "Session Viewer";

        const string k_SessionViewerTooltip =
            "No session connected. Create or join a session to see details.";

        [SerializeField] StyleSheet m_MultiplayerComponentsStyleSheet;

        VisualElement m_SessionViewerContainer;
        VisualElement m_NotConnectedLabel;
        UIController m_UI;
        SessionsViewer m_SessionsViewer;
        UnityAction<ISession> m_SessionAdded;
        UnityAction<ISession> m_SessionRemovedOrDeleted;
        UnityAction<ISession> m_SessionChanged;
        UnityAction<ISession, string> m_SessionHostChanged;
        UnityAction<ISession, SessionState> m_StateChanged;
        UnityAction<ISession, string> m_PlayerEvent;

        void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            var session = target as MultiplayerSession;
            if (session == null)
            {
                return;
            }

            // @formatter:off
            m_SessionAdded            = RefreshSessionSection;
            m_SessionRemovedOrDeleted = _ => RefreshSessionSection(null);
            m_SessionChanged          = _ => RefreshViewer();
            m_SessionHostChanged      = (_, _) => RefreshViewer();
            m_StateChanged            = (_, _) => RefreshViewer();
            m_PlayerEvent             = (_, _) => RefreshViewer();
            // @formatter:on

            session.SessionLifecycle.SessionAdded.AddListener(m_SessionAdded);
            session.SessionLifecycle.RemovedFromSession.AddListener(m_SessionRemovedOrDeleted);
            session.SessionLifecycle.Deleted.AddListener(m_SessionRemovedOrDeleted);
            session.SessionLifecycleEvents.Changed.AddListener(m_SessionChanged);
            session.SessionLifecycleEvents.SessionPropertiesChanged.AddListener(m_SessionChanged);
            session.SessionLifecycleEvents.SessionHostChanged.AddListener(m_SessionHostChanged);
            session.SessionLifecycleEvents.SessionMigrated.AddListener(m_SessionChanged);
            session.SessionLifecycleEvents.StateChanged.AddListener(m_StateChanged);
            session.SessionPlayerEvents.PlayerJoined.AddListener(m_PlayerEvent);
            session.SessionPlayerEvents.PlayerHasLeft.AddListener(m_PlayerEvent);
            session.SessionPlayerEvents.PlayerLeaving.AddListener(m_PlayerEvent);
            session.SessionPlayerEvents.PlayerPropertiesChanged.AddListener(m_SessionChanged);
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            var session = target as MultiplayerSession;
            if (session == null)
            {
                return;
            }

            if (m_SessionAdded != null)
            {
                session.SessionLifecycle.SessionAdded.RemoveListener(m_SessionAdded);
            }

            if (m_SessionRemovedOrDeleted != null)
            {
                session.SessionLifecycle.RemovedFromSession.RemoveListener(m_SessionRemovedOrDeleted);
                session.SessionLifecycle.Deleted.RemoveListener(m_SessionRemovedOrDeleted);
            }

            if (m_SessionChanged != null)
            {
                session.SessionLifecycleEvents.Changed.RemoveListener(m_SessionChanged);
                session.SessionLifecycleEvents.SessionPropertiesChanged.RemoveListener(m_SessionChanged);
                session.SessionLifecycleEvents.SessionMigrated.RemoveListener(m_SessionChanged);
                session.SessionPlayerEvents.PlayerPropertiesChanged.RemoveListener(m_SessionChanged);
            }

            if (m_SessionHostChanged != null)
            {
                session.SessionLifecycleEvents.SessionHostChanged.RemoveListener(m_SessionHostChanged);
            }

            if (m_StateChanged != null)
            {
                session.SessionLifecycleEvents.StateChanged.RemoveListener(m_StateChanged);
            }

            if (m_PlayerEvent != null)
            {
                session.SessionPlayerEvents.PlayerJoined.RemoveListener(m_PlayerEvent);
                session.SessionPlayerEvents.PlayerHasLeft.RemoveListener(m_PlayerEvent);
                session.SessionPlayerEvents.PlayerLeaving.RemoveListener(m_PlayerEvent);
            }
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state is PlayModeStateChange.EnteredPlayMode or PlayModeStateChange.ExitingPlayMode)
            {
                RefreshSessionSection(null);
            }
        }

        void RefreshViewer()
        {
            try
            {
                if (m_SessionViewerContainer == null || m_SessionsViewer == null)
                {
                    return;
                }

                m_SessionsViewer.RefreshSessionGUI();
                Repaint();
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message);
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            if (m_MultiplayerComponentsStyleSheet != null)
            {
                root.styleSheets.Add(m_MultiplayerComponentsStyleSheet);
            }

            var so = serializedObject;
            so.Update();

            root.Add(new PropertyField(so.FindProperty("m_SessionType")) { label = k_SessionIdentifier });
            root.Add(new PropertyField(so.FindProperty("m_SessionLifecycle")));
            root.Add(new PropertyField(so.FindProperty("m_SessionLifecycleEvents")));
            root.Add(new PropertyField(so.FindProperty("m_SessionPlayerEvents")));

            var sessionViewerHeader = new Label(k_SessionViewerHeader);
            sessionViewerHeader.AddToClassList(k_SessionViewerHeaderClass);
            root.Add(sessionViewerHeader);

            var sessionFoldout = new Foldout { text = "Session (when connected)", value = true };

            m_NotConnectedLabel = new Label(k_SessionViewerTooltip);
            m_NotConnectedLabel.AddToClassList(k_NotConnectedLabelClass);

            m_SessionViewerContainer = new VisualElement();

            sessionFoldout.Add(m_NotConnectedLabel);
            sessionFoldout.Add(m_SessionViewerContainer);
            root.Add(sessionFoldout);

            root.Bind(so);

            RefreshSessionSection((target as MultiplayerSession)?.Session);

            return root;
        }

        void RefreshSessionSection(ISession session)
        {
            try
            {
                if (m_SessionViewerContainer == null)
                {
                    return;
                }

                if (session == null)
                {
                    m_SessionViewerContainer.Clear();
                    m_SessionViewerContainer.AddToClassList(k_HiddenClass);
                    m_NotConnectedLabel.RemoveFromClassList(k_HiddenClass);
                    m_SessionsViewer = null;
                    return;
                }

                m_NotConnectedLabel.AddToClassList(k_HiddenClass);
                m_SessionViewerContainer.RemoveFromClassList(k_HiddenClass);

                if (m_SessionsViewer == null || m_SessionsViewer.Session != session)
                {
                    m_SessionViewerContainer.Clear();
                    m_UI = new UIController();
                    m_SessionsViewer = new SessionsViewer(m_UI, session);
                    using (m_UI.Scope(m_SessionViewerContainer))
                    {
                        m_SessionsViewer.CreateGUI();
                    }
                }
                else
                {
                    m_SessionsViewer.RefreshSessionGUI();
                }

                Repaint();
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message);
            }
        }
    }
}
