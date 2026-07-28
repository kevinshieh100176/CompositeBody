using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Components;
using UnityEngine;

namespace Unity.Services.Multiplayer.Components
{
    /// <summary>
    /// Runs a session connector on demand or when the player signs in.
    /// </summary>
    public class SessionConnectorBehaviour : ServicesBehaviour
    {
        private IAuthenticationService m_AuthService;
        private bool m_AreServicesInitialized;

        /// <summary>
        /// When true, runs the operation automatically after sign-in.
        /// </summary>
        [Tooltip("If true, executes the session operation automatically when the player signs in.")]
        public bool ExecuteOnSignIn;

        /// <summary>
        /// The session connector to run.
        /// </summary>
        [Tooltip("The session operation to execute.")]
        public SessionConnector SessionConnector;

        /// <inheritdoc/>
        protected override void OnServicesReady() {}

        /// <inheritdoc/>
        protected override void OnServicesInitialized()
        {
            if (m_AreServicesInitialized || Services == null) return;
            m_AuthService ??= Services.GetAuthenticationService();

            if (ExecuteOnSignIn)
            {
                m_AuthService.SignedIn -= Execute;
                m_AuthService.SignedIn += Execute;
            }

            m_AreServicesInitialized = true;
        }

        /// <summary>
        /// Runs <see cref="SessionConnector"/> with <see cref="ServicesBehaviour.Services"/> when the player is signed in.
        /// </summary>
        public void Execute()
        {
            if (SessionConnector == null)
            {
                Logger.LogCallWarning(nameof(SessionConnectorBehaviour), "Session connector is not set.", gameObject);
                return;
            }

            if (SessionConnector.MultiplayerSession == null)
            {
                Logger.LogCallWarning(nameof(SessionConnectorBehaviour), "Session connector's multiplayer session is not set.", gameObject);
                return;
            }

            if (SessionConnector.MultiplayerSession == null)
            {
                Logger.LogCallWarning(nameof(SessionConnectorBehaviour), "Session connector's multiplayer session is not set.");
                return;
            }

            if (m_AuthService is { IsSignedIn : true })
            {
                SessionConnector.Execute(Services);
            }
            else
            {
                Logger.LogCallWarning(nameof(SessionConnectorBehaviour), "Player is not signed in.");
            }
        }

        /// <inheritdoc/>
        protected override void Cleanup()
        {
            if (m_AuthService == null)
            {
                return;
            }

            m_AuthService.SignedIn -= Execute;

            if (SessionConnector?.MultiplayerSession?.Session != null)
            {
                _ = SessionConnector.MultiplayerSession.Session.IsHost
                    ? SessionConnector.MultiplayerSession.Session.AsHost().DeleteAsync()
                    : SessionConnector.MultiplayerSession.Session.LeaveAsync();

                SessionConnector.MultiplayerSession.SetSession(null);
            }

            m_AuthService = null;
        }

        void OnApplicationQuit()
        {
            CleanSession ();
        }

        private void CleanSession()
        {
            var session = SessionConnector?.MultiplayerSession?.Session;
            if (session?.IsHost??false)
            {
                session.AsHost().DeleteAsync();
            }
            else
            {
                session?.LeaveAsync();
            }
        }
    }
}
