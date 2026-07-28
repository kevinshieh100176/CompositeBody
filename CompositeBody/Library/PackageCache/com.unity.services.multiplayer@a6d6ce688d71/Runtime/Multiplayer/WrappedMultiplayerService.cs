using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_EDITOR
using Unity.Services.Core;
using UnityEditor;
#endif
using UnityEngine;

namespace Unity.Services.Multiplayer
{
    class WrappedMultiplayerService : IMultiplayerService
    {
        const string k_EnclosingType = nameof(WrappedMultiplayerService);

        public event Action<AddingSessionOptions> AddingSessionStarted;

        public event Action<AddingSessionOptions, SessionException> AddingSessionFailed;

        public event Action<ISession> SessionAdded
        {
            add => m_SessionManager.SessionAdded += value;
            remove => m_SessionManager.SessionAdded -= value;
        }

        public event Action<ISession> SessionRemoved
        {
            add => m_SessionManager.SessionRemoved += value;
            remove => m_SessionManager.SessionRemoved -= value;
        }

        IReadOnlyDictionary<string, ISession> IMultiplayerService.Sessions => m_SessionManager.Sessions;

        readonly ISessionQuerier m_SessionQuerier;
        readonly ISessionManager m_SessionManager;
        readonly IMatchmakerManager m_MatchmakerManager;
        readonly IModuleRegistry m_ModuleRegistry;

        internal WrappedMultiplayerService(
            ISessionQuerier sessionQuerier,
            ISessionManager sessionManager,
            IMatchmakerManager matchmakerManager,
            IModuleRegistry moduleRegistry)
        {
            m_SessionQuerier = sessionQuerier;
            m_SessionManager = sessionManager;
            m_MatchmakerManager = matchmakerManager;
            m_ModuleRegistry = moduleRegistry;
        }

        public async Task<IHostSession> CreateSessionAsync(SessionOptions sessionOptions)
        {
            var addingSessionOptions = new AddingSessionOptions(sessionOptions?.Type);
            AddingSessionStarted?.Invoke(addingSessionOptions);
            try
            {
                var sessionHandler = await m_SessionManager.CreateAsync(sessionOptions);
                return sessionHandler.AsHost();
            }
            catch (Exception e)
            {
                throw HandleException(addingSessionOptions, e);
            }
        }

        public async Task<ISession> CreateOrJoinSessionAsync(string sessionId, SessionOptions sessionOptions)
        {
            var addingSessionOptions = new AddingSessionOptions(sessionOptions?.Type);
            AddingSessionStarted?.Invoke(addingSessionOptions);
            try
            {
                var sessionHandler = await m_SessionManager.CreateOrJoinAsync(sessionId, sessionOptions);
                return sessionHandler;
            }
            catch (Exception e)
            {
                throw HandleException(addingSessionOptions, e);
            }
        }

        public async Task<ISession> JoinSessionByIdAsync(string sessionId, JoinSessionOptions sessionOptions)
        {
            var addingSessionOptions = new AddingSessionOptions(sessionOptions?.Type);
            AddingSessionStarted?.Invoke(addingSessionOptions);
            try
            {
                return await m_SessionManager.JoinByIdAsync(sessionId, sessionOptions);
            }
            catch (Exception e)
            {
                throw HandleException(addingSessionOptions, e);
            }
        }

        public async Task<ISession> JoinSessionByCodeAsync(string sessionCode, JoinSessionOptions sessionOptions)
        {
            var addingSessionOptions = new AddingSessionOptions(sessionOptions?.Type);
            AddingSessionStarted?.Invoke(addingSessionOptions);
            try
            {
                return await m_SessionManager.JoinByCodeAsync(sessionCode, sessionOptions);
            }
            catch (Exception e)
            {
                throw HandleException(addingSessionOptions, e);
            }
        }

        public async Task<ISession> ReconnectToSessionAsync(string sessionId, ReconnectSessionOptions options = default)
        {
            var addingSessionOptions = new AddingSessionOptions(options?.Type);
            AddingSessionStarted?.Invoke(addingSessionOptions);
            try
            {
                return await m_SessionManager.ReconnectAsync(sessionId, options);
            }
            catch (Exception e)
            {
                throw HandleException(addingSessionOptions, e);
            }
        }

        public async Task<ISession> MatchmakeSessionAsync(QuickJoinOptions quickJoinOptions, SessionOptions sessionOptions)
        {
            var addingSessionOptions = new AddingSessionOptions(sessionOptions?.Type);
            AddingSessionStarted?.Invoke(addingSessionOptions);
            try
            {
                return await m_SessionManager.QuickJoinAsync(quickJoinOptions, sessionOptions);
            }
            catch (Exception e)
            {
                throw HandleException(addingSessionOptions, e);
            }
        }

        public async Task<ISession> MatchmakeSessionAsync(MatchmakerOptions matchOptions, SessionOptions sessionOptions, CancellationToken cancellationToken = default)
        {
            var addingSessionOptions = new AddingSessionOptions(sessionOptions?.Type);
            AddingSessionStarted?.Invoke(addingSessionOptions);
            try
            {
                if (matchOptions == null)
                {
                    throw new SessionException("MatchmakerOptions cannot be null.", SessionError.InvalidMatchmakerOptions);
                }

                return await m_MatchmakerManager.StartAsync(matchOptions, sessionOptions?.WithMatchmaker(), cancellationToken);
            }
            catch (Exception e)
            {
                throw HandleException(addingSessionOptions, e);
            }
        }

        public async Task<QuerySessionsResults> QuerySessionsAsync(QuerySessionsOptions queryOptions)
        {
            try
            {
                return await m_SessionQuerier.QueryAsync(queryOptions);
            }
            catch (Exception e) when (e is not SessionException)
            {
                throw new SessionException(e.Message, SessionError.Unknown);
            }
        }

        public async Task<List<string>> GetJoinedSessionIdsAsync()
        {
            try
            {
                return await m_SessionManager.GetJoinedSessionIdsAsync();
            }
            catch (Exception e) when (e is not SessionException)
            {
                throw new SessionException(e.Message, SessionError.Unknown);
            }
        }

        SessionException HandleException(AddingSessionOptions addingSessionOptions, Exception e)
        {
            // in the case of aggregated exceptions or other exception types, we rethrow as SessionException
            if (e is SessionException sessionException)
            {
                AddingSessionFailed?.Invoke(addingSessionOptions, sessionException);
                return sessionException;
            }

            var newSessionException = new SessionException(e.Message, SessionError.Unknown);
            AddingSessionFailed?.Invoke(addingSessionOptions, newSessionException);
            return newSessionException;
        }
    }
}
