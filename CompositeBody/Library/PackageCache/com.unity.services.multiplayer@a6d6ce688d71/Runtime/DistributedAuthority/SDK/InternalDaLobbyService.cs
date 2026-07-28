using System;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace Unity.Services.DistributedAuthority
{
    /// <summary>
    /// Focused Lobby interface for the Distributed Authority service.
    /// </summary>
    internal interface IInternalDaLobbyService
    {
        Task<Lobby> JoinLobbyByIdAsync(string lobbyId, JoinLobbyByIdOptions options = null);
        Task<Lobby> GetLobbyAsync(string lobbyIy);
    }

    /// <summary>
    /// Wrapper allowing to fetch a lobby service instance at runtime (lazy loading).
    /// </summary>
    internal class InternalDaLobbyService : IInternalDaLobbyService
    {
        readonly Lazy<ILobbyService> _lazyLobbyService;

        public InternalDaLobbyService(Lazy<ILobbyService> lazyLobbyService)
        {
            _lazyLobbyService = lazyLobbyService;
        }

        public async Task<Lobby> JoinLobbyByIdAsync(string lobbyId, JoinLobbyByIdOptions options = null)
        {
            try
            {
                var(lobby, success) = await ReconnectToLobbyAsync(lobbyId);
                if (success)
                {
                    return lobby;
                }

                return await _lazyLobbyService.Value.JoinLobbyByIdAsync(lobbyId, options);
            }
            catch (LobbyServiceException lex)
            {
                if (!lex.IsAlreadyMemberError())
                {
                    throw;
                }
            }

            return await GetLobbyAsync(lobbyId);
        }

        public async Task<Lobby> GetLobbyAsync(string lobbyId)
        {
            return await _lazyLobbyService.Value.GetLobbyAsync(lobbyId);
        }

        private async Task<(Lobby, bool)> ReconnectToLobbyAsync(string lobbyId)
        {
            try
            {
                var lobby = await _lazyLobbyService.Value.ReconnectToLobbyAsync(lobbyId);
                return (lobby, true);
            }
            catch (LobbyServiceException lex)
            {
                if (lex.Reason == LobbyExceptionReason.Forbidden)
                {
                    return (null, false);
                }

                throw;
            }
        }
    }
}
