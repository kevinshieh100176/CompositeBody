namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Represents the state of a matchmaking process.
    /// </summary>
    public enum MatchmakerState
    {
        /// <summary>
        /// No matchmaker state.
        /// </summary>
        None,
        /// <summary>
        /// Matchmaking is in progress.
        /// </summary>
        InProgress,
        /// <summary>
        /// Matchmaking has been canceled.
        /// </summary>
        Canceled,
        /// <summary>
        /// Matchmaking has failed.
        /// </summary>
        MatchFailed,
        /// <summary>
        /// Match has been found.
        /// </summary>
        MatchFound,
        /// <summary>
        /// Joining a match has failed.
        /// </summary>
        JoinFailed,
        /// <summary>
        /// Match has been joined.
        /// </summary>
        Joined,
    }
}
