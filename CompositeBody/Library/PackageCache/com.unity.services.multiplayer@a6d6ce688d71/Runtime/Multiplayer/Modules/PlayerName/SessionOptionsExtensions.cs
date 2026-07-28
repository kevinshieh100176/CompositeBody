namespace Unity.Services.Multiplayer
{
    public static partial class SessionOptionsExtensions
    {
        /// <summary>
        /// Synchronizes the unity player name as a session player property.
        /// Requires the unity player name to be retrieved with the Authentication service.
        /// </summary>
        /// <typeparam name="T">The options' type.</typeparam>
        /// <param name="options">The SessionOptions this extension method applies to.</param>
        /// <param name="visibility">The visibility to apply to the player name property. It must be Member or Public to be readable by other players.</param>
        /// <returns>The session options</returns>
        public static T WithPlayerName<T>(this T options, VisibilityPropertyOptions visibility = VisibilityPropertyOptions.Member) where T : BaseSessionOptions
        {
            return options.WithOption(new PlayerNameSessionOption(visibility));
        }

        /// <summary>
        /// Retrieve the player name from the session player property
        /// </summary>
        /// <param name="player">The player to retrieve the name from</param>
        /// <returns>The player name</returns>
        public static string GetPlayerName(this IReadOnlyPlayer player)
        {
            if (player.Properties?.ContainsKey(PlayerNameModule.PropertyKey) ?? false)
            {
                return player.Properties[PlayerNameModule.PropertyKey].Value;
            }

            return null;
        }
    }
}
