using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lobbies.SDK.LobbyCacher;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace Unity.Services.Lobbies.Internal
{
    internal interface ILobbyServiceInternal : ILobbyService
    {
        Task<Dictionary<string, Models.TokenData>> RequestTokensAsync(string lobbyId, params TokenRequest.TokenTypeOptions[] tokenOptions);

        LobbyCacher GetLobbyCacher();

        public ILobbyEvents SetCacherLobbyCallbacks(string lobbyId, LobbyEventCallbacks lobbyEventCallbacks);

        Task DeleteLobbyAsync(string lobbyId, bool applyIfMatch);

        Task RemovePlayerAsync(string lobbyId, string playerId, bool applyIfMatch);

        Task<Models.Lobby> UpdateLobbyAsync(string lobbyId, UpdateLobbyOptions options, bool applyIfMatch);

        Task<Models.Lobby> UpdatePlayerAsync(string lobbyId, string playerId, UpdatePlayerOptions options, bool applyIfMatch);
    }
}
