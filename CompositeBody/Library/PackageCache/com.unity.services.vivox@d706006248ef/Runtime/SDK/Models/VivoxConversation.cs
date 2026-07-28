namespace Unity.Services.Vivox
{
    /// <summary>
    /// Provides information about either a channel or direct message conversation.
    /// </summary>
    public sealed class VivoxConversation
    {
        /// <summary>
        /// This will be either the PlayerId of a player or the name of a channel depending on the type of conversation this instance represents.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Denotes the type of conversation this instance represents.
        /// </summary>
        public ConversationType ConversationType { get; }

        internal VivoxConversation(string name, ConversationType conversationType)
        {
            Name = name;
            ConversationType = conversationType;
        }
    }
}
