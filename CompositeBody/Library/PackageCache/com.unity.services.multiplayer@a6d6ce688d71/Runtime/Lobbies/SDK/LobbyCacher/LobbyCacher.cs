using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace Lobbies.SDK.LobbyCacher
{
    class LobbyCacher : ILobbyEvents
    {
        public LobbyEventCallbacks Callbacks { get; private set; }

        bool m_IsSubscribed;

        readonly Dictionary<string, Lobby> m_LobbyCacher;
        readonly string m_PlayerID;

        public LobbyCacher(string playerID = "")
        {
            m_LobbyCacher = new Dictionary<string, Lobby>();
            m_PlayerID = playerID;
        }

        public LobbyCacher WithEventSubscription(LobbyEventCallbacks lobbyCallbacks)
        {
            Callbacks = lobbyCallbacks;
            m_IsSubscribed = true;
            return this;
        }

        public Task SubscribeAsync()
        {
            if (Callbacks == null)
                throw new InvalidOperationException(
                    "No callbacks set for lobby events. Set callbacks using WithEventSubscription before subscribing.");

            m_IsSubscribed = true;
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync()
        {
            m_IsSubscribed = false;
            return Task.CompletedTask;
        }

        public bool TryGetLobbyCache(string lobbyId, out Lobby cachedLobby)
        {
            return m_LobbyCacher.TryGetValue(lobbyId, out cachedLobby);
        }

        public string GetIfMatchTag(string lobbyId, bool applyIfMatch = true)
        {
            return applyIfMatch ? GetLobbyCacheVersion(lobbyId) : default;
        }

        public string GetLobbyCacheVersion(string lobbyId)
        {
            return TryGetLobbyCache(lobbyId, out var cachedLobby) ? cachedLobby.Version.ToString() : null;
        }

        public bool RemoveLobbyCache(string lobbyId)
        {
            return m_LobbyCacher.Remove(lobbyId);
        }

        public bool UpdateLobbyCache(string lobbyId, Lobby newLobby)
        {
            if (!TryGetLobbyCache(lobbyId, out var cachedLobby))
            {
                return false;
            }

            return UpdateLobbyCache(lobbyId, LobbyPatcher.GetLobbyDiff(cachedLobby, newLobby));
        }

        public bool UpdateLobbyCache(string lobbyId, ILobbyChanges changes)
        {
            if (!TryGetLobbyCache(lobbyId, out var cachedLobby))
            {
                return false;
            }

            // if the lobby was deleted, remove it from the cache and invoke the callback
            if (changes.LobbyDeleted)
            {
                var removed = RemoveLobbyCache(lobbyId);
                if (removed && m_IsSubscribed)
                {
                    Callbacks?.InvokeLobbyChanged(changes);
                }

                return removed;
            }

            // if the cached lobby version is older than the changed lobby version, update the cached lobby
            // and invoke the callback (or InvokeKickedFromLobby if the player was removed)
            if (cachedLobby.Version < changes.Version.Value)
            {
                if (WasRemovedFromLobby(changes, cachedLobby))
                {
                    Callbacks?.InvokeKickedFromLobby();
                    return true;
                }

                changes.ApplyToLobby(cachedLobby);
                Callbacks?.InvokeLobbyChanged(changes);
            }

            return true;
        }

        public bool AddLobbyCache(string lobbyId, Lobby lobby)
        {
            if (m_LobbyCacher.ContainsKey(lobbyId))
            {
                return false;
            }

            m_LobbyCacher[lobbyId] = lobby;
            return true;
        }

        bool WasRemovedFromLobby(ILobbyChanges changes, Lobby cachedLobby)
        {
            if (cachedLobby == null || !changes.PlayerLeft.Changed)
                return false;

            // If the current player left and has not joined again, it has been removed from the lobby (kicked, or left)
            if (changes.PlayerLeft.Value.Exists(playerIdx => cachedLobby.Players[playerIdx].Id == m_PlayerID)
                && (changes.PlayerJoined.Value == null ||
                    !changes.PlayerJoined.Value.Exists(joinEvt => joinEvt.Player.Id == m_PlayerID)))
            {
                RemoveLobbyCache(cachedLobby.Id);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resets a Lobby.
        /// </summary>
        /// <param name="lobbyId">The Lobby id.</param>
        public void ResetLobby(string lobbyId)
        {
            m_LobbyCacher.Remove(lobbyId);
        }
    }
}
