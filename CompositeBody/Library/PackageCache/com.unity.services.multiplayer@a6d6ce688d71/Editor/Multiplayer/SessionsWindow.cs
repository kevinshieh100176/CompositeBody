using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.Services.Multiplayer.Editor
{
    class SessionsWindow : EditorWindow
    {
        SessionsController SessionsController { get; set; }
        UIController UI { get; }

        UIElement SessionsContainer;

        SessionsWindow()
        {
            SessionsController = new SessionsController();
            UI = new UIController();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Services/Multiplayer/Sessions Viewer", priority = 10000)]
        public static void OpenMultiplayerSessionsViewer()
        {
            FindOrCreateWindow();
        }

        public static SessionsWindow FindOrCreateWindow()
        {
            var existingWindow = Resources.FindObjectsOfTypeAll<SessionsWindow>().FirstOrDefault();

            if (existingWindow != null)
            {
                existingWindow.Show();
                existingWindow.Focus();
                return existingWindow;
            }

            var window = CreateInstance<SessionsWindow>();
            window.minSize = new Vector2(400, 400);
            window.Show();
            window.name = "Multiplayer Sessions";
            window.titleContent.text = "Multiplayer Sessions";
            return window;
        }

        void CreateGUI()
        {
            using (UI.Scope(rootVisualElement))
            using (UI.ScrollView().Scope())
            using (UI.ScrollContent().Scope())
            {
                UI.Label("Active sessions will be displayed here during play.").SetFontStyle(FontStyle.Italic);
                UI.Separator().SetPadding(5, 0, 0, 5);
                SessionsContainer = UI.Element();
            }

            SessionsController.SessionsChanged -= RefreshGUI;
            SessionsController.SessionsChanged += RefreshGUI;

            if (Application.isPlaying)
            {
                SessionsController.Initialize();
            }
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    SessionsController.Initialize();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    SessionsController.Reset();
                    break;
            }
        }

        void RefreshGUI()
        {
            SessionsContainer.Clear();

            using (SessionsContainer.Scope())
            {
                if (SessionsController.Sessions != null)
                {
                    foreach (var sessionKVP in SessionsController.Sessions)
                    {
                        new SessionsViewer(UI, sessionKVP.Value).CreateGUI();
                        UI.Separator().SetPadding(5, 0, 0, 5);
                    }
                }
            }
        }
    }
}
