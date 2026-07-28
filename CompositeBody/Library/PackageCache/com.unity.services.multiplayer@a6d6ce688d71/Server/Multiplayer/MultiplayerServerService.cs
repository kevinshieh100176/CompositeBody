using System;
using System.Threading.Tasks;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Facade for session browsing, session management and matchmaking.
    /// </summary>
    public interface IMultiplayerServerService
    {
        /// <summary>
        /// Creates a server session.
        /// </summary>
        /// <param name="sessionOptions">The options for the resulting session</param>
        /// <returns>The created server session</returns>
        /// <exception cref="ArgumentNullException">If a parameter is null.</exception>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<IServerSession> CreateSessionAsync(SessionOptions sessionOptions);

        /// <summary>
        /// Creates a server session handle with a custom session id.
        /// </summary>
        /// <param name="sessionId">The session id</param>
        /// <param name="sessionOptions">The session options to be used for creation</param>
        /// <returns>The server session</returns>
        /// <exception cref="ArgumentNullException">If a parameter is null.</exception>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        Task<IServerSession> CreateSessionAsync(string sessionId, SessionOptions sessionOptions);

        /// <summary>
        /// Creates a server session handle with a matchmaker match id. The id must be valid and the matchmaker results will be fetched.
        /// </summary>
        /// <param name="matchId">The matchmaker match id</param>
        /// <param name="sessionOptions">The session options to be used for creation</param>
        /// <returns>The server session</returns>
        /// <exception cref="ArgumentNullException">If a parameter is null.</exception>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        public Task<IServerSession> CreateMatchSessionAsync(string matchId, SessionOptions sessionOptions);

        /// <summary>
        /// Retrieves an existing session
        /// </summary>
        /// <param name="sessionId">The unique session id</param>
        /// <returns>The server session</returns>
        /// <exception cref="ArgumentNullException">If a parameter is null.</exception>
        /// <exception cref="SessionException">Provides a specific session error type and error message.</exception>
        internal Task<IServerSession> GetSessionAsync(string sessionId);
    }

    /// <summary>
    /// The entry class of the Multiplayer SDK and session system.
    /// </summary>
    public static class MultiplayerServerService
    {
        /// <summary>
        /// A static instance of the Multiplayer service and session system.
        /// </summary>
        public static IMultiplayerServerService Instance { get; internal set; }
    }
}
