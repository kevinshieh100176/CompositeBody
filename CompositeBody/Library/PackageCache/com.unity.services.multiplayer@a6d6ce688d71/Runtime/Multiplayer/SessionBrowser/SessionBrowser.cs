using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.Lobbies;

namespace Unity.Services.Multiplayer
{
    interface ISessionQuerier
    {
        public Task<QuerySessionsResults> QueryAsync(QuerySessionsOptions options);
    }

    class SessionQuerier : ISessionQuerier
    {
        readonly IActionScheduler m_ActionScheduler;
        readonly ILobbyService m_LobbyService;

        public SessionQuerier(IActionScheduler actionScheduler, ILobbyService lobbyService)
        {
            m_ActionScheduler = actionScheduler;
            m_LobbyService = lobbyService;
        }

        public async Task<QuerySessionsResults> QueryAsync(QuerySessionsOptions options)
        {
            try
            {
                var query = LobbyConverter.ToQueryLobbiesOptions(options);
                var results = await m_LobbyService.QueryLobbiesAsync(query);
                var sessions = new List<ISessionInfo>();

                foreach (var result in results.Results)
                {
                    sessions.Add(new LobbySessionInfo(result));
                }

                return new QuerySessionsResults(sessions, results.ContinuationToken, options, this, m_ActionScheduler);
            }
            catch (LobbyServiceException exception)
            {
                throw LobbyConverter.ToSessionException(exception);
            }
            catch (System.Exception e)
            {
                throw new SessionException(e.Message, SessionError.Unknown);
            }
        }
    }
}
