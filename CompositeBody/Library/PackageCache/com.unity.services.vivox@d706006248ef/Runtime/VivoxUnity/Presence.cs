namespace Unity.Services.Vivox
{
    /// <summary>
    /// The presence information for a user at a location.
    /// </summary>
    internal struct Presence
    {
        /// <summary>
        /// The online status of the user.
        /// </summary>
        public readonly PresenceStatus Status;
        /// <summary>
        /// An optional message published by the user.
        /// </summary>
        public readonly string Message;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="status">The online status of the user.</param>
        /// <param name="message">An optional message.</param>
        public Presence(PresenceStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        /// <summary>
        /// Determine if two objects are equal.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True if the objects are equal.</returns>
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            if (GetType() != obj.GetType()) return false;

            return Equals((Presence)obj);
        }

        bool Equals(Presence other)
        {
            return Status == other.Status && string.Equals(Message, other.Message);
        }

        /// <summary>
        /// Get the hashcode for this object.
        /// </summary>
        /// <returns>The hashcode.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Status * 397) ^ (Message?.GetHashCode() ?? 0);
            }
        }
    }
}
