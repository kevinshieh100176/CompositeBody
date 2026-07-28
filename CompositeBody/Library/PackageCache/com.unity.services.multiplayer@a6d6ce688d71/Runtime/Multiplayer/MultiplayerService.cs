using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Facade for session browsing, session management and matchmaking.
    /// </summary>
    public interface IMultiplayerService
    {
        /// <summary>
        /// Raised when a new session creation or joining request is initiated.
        /// </summary>
        public event Action<AddingSessionOptions> AddingSessionStarted;

        /// <summary>
        /// Raised when a new session creation or joining request fails.
        /// </summary>
        public event Action<AddingSessionOptions, SessionException> AddingSessionFailed;

        /// <summary>
        /// Raised when a new session is added to <see cref="Sessions"/>.
        /// </summary>
        public event Action<ISession> SessionAdded;

        /// <summary>
        /// Raised when an active session is removed from <see cref="Sessions"/>.
        /// </summary>
        public event Action<ISession> SessionRemoved;

        /// <summary>
        /// The list of active sessions
        /// </summary>
        public IReadOnlyDictionary<string, ISession> Sessions { get; }

        /// <summary>
        /// Creates a Session.
        /// </summary>
        /// <param name="sessionOptions">The options for the resulting session</param>
        /// <returns>The created session</returns>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<IHostSession> CreateSessionAsync(SessionOptions sessionOptions);

        /// <summary>
        /// Tries to join a Session, creates it if no Session associated to the provided ID exists.
        /// </summary>
        /// <param name="sessionId">The Session ID</param>
        /// <param name="sessionOptions">The options for the resulting session</param>
        /// <returns>The created or joined session.</returns>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<ISession> CreateOrJoinSessionAsync(string sessionId, SessionOptions sessionOptions);

        /// <summary>
        /// Joins a Session by the session ID.
        /// </summary>
        /// <param name="sessionId">The ID for the session</param>
        /// <param name="sessionOptions">The options for the resulting session</param>
        /// <returns>The joined session.</returns>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<ISession> JoinSessionByIdAsync(string sessionId, JoinSessionOptions sessionOptions = default);

        /// <summary>
        /// Joins a Session through a join code.
        /// </summary>
        /// <param name="sessionCode">The join code for the session</param>
        /// <param name="sessionOptions">The options for the resulting session</param>
        /// <returns>The joined session.</returns>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<ISession> JoinSessionByCodeAsync(string sessionCode, JoinSessionOptions sessionOptions = default);

        /// <summary>
        /// Attempts to reconnect to an existing already joined session following a disconnect.
        /// </summary>
        /// <param name="sessionId">The ID for the session.</param>
        /// <param name="options">The reconnection options.</param>
        /// <returns>The reconnected session.</returns>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<ISession> ReconnectToSessionAsync(string sessionId, ReconnectSessionOptions options = default);

        /// <summary>
        /// Find and join a Session with Unity matchmaker.
        /// </summary>
        /// <param name="matchOptions">The matchmaking queue options to join the Session</param>
        /// <param name="sessionOptions">The options for the resulting session</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request</param>
        /// <returns>The created or joined session.</returns>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<ISession> MatchmakeSessionAsync(MatchmakerOptions matchOptions, SessionOptions sessionOptions, CancellationToken cancellationToken = default);

        /// <summary>
        /// Find and join a Session using session filters.
        /// This operation will be retried at an interval up to a timeout specified in the options.
        /// Optionally creates a session if none is found after a timeout.
        /// </summary>
        /// <param name="quickJoinOptions">The quick join options used to find, join or create session</param>
        /// <param name="sessionOptions">The options for the resulting session</param>
        /// <returns>The matchmade session.</returns>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<ISession> MatchmakeSessionAsync(QuickJoinOptions quickJoinOptions, SessionOptions sessionOptions);

        /// <summary>
        /// Query available sessions
        /// </summary>
        /// <param name="queryOptions">The query options for the search</param>
        /// <returns>The result of the query with a list of sessions</returns>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<QuerySessionsResults> QuerySessionsAsync(QuerySessionsOptions queryOptions);


        /// <summary>
        /// Get a list of session IDs that the current player is part of.
        /// </summary>
        /// <returns>A list of sessions.</returns>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<List<string>> GetJoinedSessionIdsAsync();
    }

    /// <summary>
    /// The entry class of the Multiplayer SDK and session system.
    /// </summary>
    public static class MultiplayerService
    {
        /// <summary>
        /// A static instance of the Multiplayer service and session system.
        /// </summary>
        public static IMultiplayerService Instance { get; set; }
    }
}
