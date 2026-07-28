using System;
using System.Collections.Generic;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Options to configure a session when either joining or creating.
    /// Some options will only be used if a session needs to be created.
    /// Options can be customized to provide additional feature configuration.
    /// </summary>
    public class SessionOptions : BaseSessionOptions
    {
        /// <summary>
        /// Determines the name of the session if a session is created.
        /// </summary>
        /// <seealso cref="ISession.Name"/>
        public string Name { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// The max number of players (including host) allowed in the session if a session is created.
        /// Required to be over 0 if a session needs to be created.
        /// </summary>
        /// <seealso cref="ISession.MaxPlayers"/>
        public int MaxPlayers { get; set; }

        /// <summary>
        /// Determines if a session will be locked if a session is created.
        /// A locked session does not allow any more players to join.
        /// True if the session is locked, false otherwise.
        /// Only used if a session needs to be created.
        /// </summary>
        /// <seealso cref="ISession.IsLocked"/>
        public bool IsLocked { get; set; }

        /// <summary>
        /// Determines if a session will be private if a session is created.
        /// Only used if a session needs to be created.
        /// </summary>
        /// <remarks>
        /// Private sessions are not visible in queries and cannot be joined with quick-join.
        /// They can still be joined by ID or by Code.
        /// </remarks>
        /// <seealso cref="ISession.IsPrivate"/>
        public bool IsPrivate { get; set; }

        /// <summary>
        /// Determines the password required to access a session if a session is created.
        /// The session password. Null for none.
        /// Only used if a session needs to be created.
        /// </summary>
        /// <remarks>
        /// A string between 8 and 64 characters. The password must be provided when joining the session.
        /// It is not readable from sessions.
        /// </remarks>
        /// <seealso cref="IHostSession.Password"/>
        /// <seealso cref="JoinSessionOptions.Password"/>
        public string Password { get; set; }

        /// <summary>
        /// Additional user-defined session properties (e.g. 'map').
        /// </summary>
        /// <remarks>
        /// Only used if a session needs to be created.
        /// Up to 20 properties may be set per session, including those used internally by this package.
        /// The host can modify the properties through IHostSession.
        /// </remarks>
        /// <seealso cref="ISession.Properties"/>
        /// <seealso cref="IHostSession.Properties"/>
        public Dictionary<string, SessionProperty> SessionProperties { get; set; } = new Dictionary<string, SessionProperty>();

        /// <summary>
        /// Creates an instance of SessionOptions.
        /// </summary>
        public SessionOptions()
        {
        }

        internal JoinSessionOptions ToJoinOptions()
        {
            return new JoinSessionOptions()
            {
                Type = Type,
                PlayerProperties = PlayerProperties,
                Password = Password,
                Options = Options
            };
        }
    }
}
