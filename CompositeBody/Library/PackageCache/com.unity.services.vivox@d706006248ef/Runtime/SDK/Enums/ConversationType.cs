namespace Unity.Services.Vivox
{
    /// <summary>
    /// Used to denote different types of Vivox conversations.
    /// </summary>
    public enum ConversationType
    {
        /// <summary>
        /// The conversation is from a channel.
        /// </summary>
        ChannelConversation = 0,
        /// <summary>
        /// The conversation is from a directed message with another person.
        /// </summary>
        DirectedMessageConversation = 1
    }
}
