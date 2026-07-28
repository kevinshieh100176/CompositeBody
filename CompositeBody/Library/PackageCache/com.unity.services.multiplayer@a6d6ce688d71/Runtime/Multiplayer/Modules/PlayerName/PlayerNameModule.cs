using System;
using System.Threading.Tasks;
using Unity.Services.Authentication.Internal;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Ensures synchronization of the player name throughout the session lifecycle.
    /// </summary>
    class PlayerNameModule : IModule
    {
        public const string PropertyKey = "_player_name";

        readonly ISession m_Session;
        readonly IPlayerNameComponent m_PlayerName;

        internal const string InvalidNameWarning = "Attempting to set player name in session to an invalid value.";
        internal const string SyncFailureError = "Failed to synchronize player name in session.";

        PlayerNameSessionOption m_PlayerNameOption;

        internal PlayerNameModule(IPlayerNameComponent playerName, ISession session)
        {
            m_PlayerName = playerName;
            m_Session = session;
        }

        internal void Enable(PlayerNameSessionOption option)
        {
            m_PlayerNameOption = option;
            m_PlayerName.PlayerNameChanged -= OnPlayerNameChanged;
            m_PlayerName.PlayerNameChanged += OnPlayerNameChanged;
        }

        Task IModule.InitializeAsync()
        {
            return Task.CompletedTask;
        }

        internal async void OnPlayerNameChanged(string playerName)
        {
            try
            {
                await SyncPlayerNameAsync(playerName);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }

        internal async Task SyncPlayerNameAsync(string playerName)
        {
            if (m_Session.State != SessionState.Connected || m_PlayerNameOption == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(playerName))
            {
                Logger.LogWarning(InvalidNameWarning);
                return;
            }

            Logger.LogVerbose($"Synchronizing player name in session to '{playerName}'");
            m_Session.CurrentPlayer.SetProperty(PropertyKey, new PlayerProperty(playerName, m_PlayerNameOption.Visibility));

            try
            {
                await m_Session.SaveCurrentPlayerDataAsync();
            }
            catch
            {
                throw new SessionException(SyncFailureError, SessionError.PlayerNameSynchronizationFailed);
            }
        }

        public Task LeaveAsync()
        {
            return Task.CompletedTask;
        }
    }
}
