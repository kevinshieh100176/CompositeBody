using System.Threading.Tasks;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Provides extension methods for configuring and managing backfilling
    /// in sessions created through matchmaking and the Matchmaker service.
    /// </summary>
    public static class MatchmakerServerExtensions
    {
        /// <summary>
        /// Allows configuring the Backfilling of a session that was
        /// created through matchmaking and the Matchmaker service.
        /// </summary>
        /// <param name="options">
        /// The <see cref="SessionOptions"/> this extension method applies to.
        /// </param>
        /// <param name="enable">
        /// Enables or disables the backfilling for the session that
        /// will be created with the <see cref="SessionOptions"/>.
        /// </param>
        /// <param name="automaticallyRemovePlayers">
        /// Automatically remove the player from the state of
        /// the match or not. Setting this to <c>true</c> will
        /// enable automatically requesting players to backfill
        /// once a player leaves. Suggested value: <c>true</c>.
        /// </param>
        /// <param name="autoStart">
        /// Automatically starts or not the backfilling process at the creation
        /// of the sessions if it is not full. Suggested value: <c>true</c>.
        /// </param>
        /// <param name="playerConnectionTimeout">
        /// The time in seconds allowed for the player to connect before
        /// being removed from the backfill. Suggested value: <c>30s</c>.
        /// </param>
        /// <param name="backfillingLoopInterval">
        /// The interval in seconds between each approval of
        /// a backfilling ticket. Suggested value: <c>1s</c>.
        /// </param>
        /// <typeparam name="T">
        /// The <see cref="SessionOptions">options</see>' type.
        /// </typeparam>
        /// <returns>The <see cref="SessionOptions"/>.</returns>
        public static T WithBackfillingConfiguration<T>(this T options, bool enable, bool automaticallyRemovePlayers,
            bool autoStart,
            int playerConnectionTimeout, int backfillingLoopInterval) where T : SessionOptions
        {
            return options.WithOption(new MatchmakerBackfillOption(
                BackfillingConfiguration.WithBackfillingConfiguration(
                    enable,
                    automaticallyRemovePlayers,
                    autoStart,
                    playerConnectionTimeout,
                    backfillingLoopInterval)));
        }

        /// <summary>
        /// Starts the backfilling process of a session that was
        /// created through matchmaking and the Matchmaker service.
        /// </summary>
        /// <param name="session">
        /// The session to start the backfilling process on.
        /// </param>
        /// <returns>
        /// A <see cref="Task"/> representing the asynchronous operation.
        /// </returns>
        public static Task StartBackfillingAsync(this ISession session)
        {
            return ((SessionHandler)session).GetModule<MatchmakerModule>().StartBackfillingAsync();
        }

        /// <summary>
        /// Stops the backfilling process of a session that was
        /// created through matchmaking and the Matchmaker service.
        /// </summary>
        /// <param name="session">
        /// The session to stop the backfilling process on.
        /// </param>
        /// <returns>
        /// A <see cref="Task"/> representing the asynchronous operation.
        /// </returns>
        public static Task StopBackfillingAsync(this ISession session)
        {
            return ((SessionHandler)session).GetModule<MatchmakerModule>().StopBackfillingAsync();
        }
    }
}
