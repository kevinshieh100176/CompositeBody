using Unity.Services.Matchmaker.Models;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Extension methods of an ISession that can be used when using the Matchmaker service.
    /// </summary>
    public static class MatchmakerExtensions
    {
        /// <summary>
        /// Returns the matchmaking results of the session. The session has to be created through matchmaking and the
        /// Matchmaker service.
        /// </summary>
        /// <param name="session">The session to fetch the matchmaking results from.</param>
        /// <returns>The matchmaking results.</returns>
        public static StoredMatchmakingResults GetMatchmakingResults(this ISession session)
        {
            return ((SessionHandler)session).GetModule<MatchmakerModule>().MatchmakingResults;
        }
    }
}
