using System.Threading.Tasks;

namespace Unity.Services.Multiplayer
{
    class MultiplayerServerServiceImpl : IMultiplayerServerService
    {
        readonly SessionManager m_SessionManager;

        internal MultiplayerServerServiceImpl(
            SessionManager sessionManager)
        {
            m_SessionManager = sessionManager;
        }

        public Task<IServerSession> CreateSessionAsync(SessionOptions sessionOptions)
        {
            return m_SessionManager.CreateAsync(sessionOptions).ContinueWith(t => t.Result.AsServer());
        }

        public Task<IServerSession> CreateSessionAsync(string sessionId, SessionOptions sessionOptions)
        {
            return m_SessionManager.CreateOrJoinAsync(sessionId, sessionOptions).ContinueWith(t => t.Result.AsServer());
        }

        public Task<IServerSession> CreateMatchSessionAsync(string matchId, SessionOptions sessionOptions)
        {
            return m_SessionManager.CreateOrJoinAsync(matchId, sessionOptions?.WithMatchmaker()).ContinueWith(t => t.Result.AsServer());
        }

        Task<IServerSession> IMultiplayerServerService.GetSessionAsync(string sessionId)
        {
            return m_SessionManager.GetAsync(sessionId).ContinueWith(t => t.Result.AsServer());
        }
    }
}
