using Unity.Services.Multiplayer;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Allow to configure the backfilling behavior on the session.
    /// </summary>
    public class BackfillingConfiguration
    {
        /// <summary>
        /// Allow the matchmaker to backfill the session if it is not full, not locked, and not private.
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        /// Define if players leaving the session are automatically removed from the backfill.
        /// If set to false, players leaving the session will stay in the backfill ticket preventing them from joining again.
        /// </summary>
        public bool AutomaticallyRemovePlayers { get; private set; } = true;

        /// <summary>
        /// Define if the backfilling will start automatically when a player is missing.
        /// If set to false, it is possible to start backfilling manually using StartBackfilling() on the Session.
        /// </summary>
        public bool AutoStart { get; private set; } = true;

        /// <summary>
        /// The time between each backfill approval request.
        /// Default value is 1 second.
        /// Value is invalid if below 0
        /// </summary>
        public int BackfillingLoopInterval { get; private set; } = 1;

        /// <summary>
        /// The time in seconds that a player who was added from backfilling has to connect to a session before being automatically removed.
        /// Default value is 30 seconds.
        /// If value is set to 0, the player will never be removed.
        /// </summary>
        public int PlayerConnectionTimeout { get; set; } = 30;

        /// <summary>
        /// Create a backfilling configuration.
        /// </summary>
        /// <param name="enable">Allow the matchmaker to backfill the session if it is not full, not locked and not
        /// private.</param>
        /// <param name="automaticallyRemovePlayers">
        /// Define if players leaving the session are automatically removed from the backfill.
        /// If set to false, players leaving the session will stay in the backfill ticket preventing them from joining
        /// again.
        /// </param>
        /// <param name="autoStart">
        /// Define if the backfilling will start automatically when a player is missing.
        /// If set to false, it is possible to start backfilling manually using StartBackfilling() on the Session.
        /// </param>
        /// <param name="playerConnectionTimeout">
        /// The time in seconds that a player who was added from backfilling has to connect to a session before
        /// being automatically removed.
        /// Default value is 30 seconds.
        /// If value is set to 0, the player will never be removed.
        /// </param>
        /// <param name="backfillingLoopInterval">
        /// The time between each backfill approval request.
        /// Default value is 1 second.
        /// Value is invalid if below 0.
        /// </param>
        /// <returns>The <see cref="BackfillingConfiguration"/>.</returns>
        /// <exception cref="SessionException">
        /// <para>Thrown when <paramref name="playerConnectionTimeout"/> is less than <c>0</c> or when <paramref
        /// name="backfillingLoopInterval"/> is less than <c>1</c>.
        /// </para>
        /// </exception>
        public static BackfillingConfiguration WithBackfillingConfiguration(
            bool enable = true,
            bool automaticallyRemovePlayers = true,
            bool autoStart = true,
            int playerConnectionTimeout = 30,
            int backfillingLoopInterval = 1)
        {
            if (playerConnectionTimeout < 0)
                throw new SessionException("Player connection timeout must be greater or equal to 0",
                    SessionError.InvalidParameter);

            if (backfillingLoopInterval < 1)
                throw new SessionException("Backfilling loop interval must be greater or equal to 1",
                    SessionError.InvalidParameter);

            return new BackfillingConfiguration
            {
                Enable = enable,
                AutomaticallyRemovePlayers = automaticallyRemovePlayers,
                AutoStart = autoStart,
                BackfillingLoopInterval = backfillingLoopInterval,
                PlayerConnectionTimeout = playerConnectionTimeout,
            };
        }
    }
}
