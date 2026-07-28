using System;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Options configured when adding a session.
    /// </summary>
    public struct AddingSessionOptions
    {
        /// <summary>
        /// The type used to create or join to the session. This may be null.
        /// </summary>
        public readonly string Type;

        /// <summary>
        /// Options configured when adding a session.
        /// </summary>
        /// <param name="type">The session type</param>
        public AddingSessionOptions(string type)
        {
            Type = type;
        }
    }
}
