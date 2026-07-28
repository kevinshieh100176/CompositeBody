using System.Collections.Generic;

namespace Unity.Services.Vivox
{
    internal class ChatHistoryQueryResult : IChatHistoryQueryResult
    {
        /// <summary>
        /// A unique identifier given to use from the core sdk to help aggregate the message results returned as events
        /// </summary>
        public string RequestId { get; set; }

        /// <summary>
        /// The Participant we are filtering results on
        /// Optional: Default is null
        /// </summary>
        public AccountId Participant { get; internal set; } = null;

        /// <summary>
        /// A list of <see cref="VivoxMessage"/> returned from the query
        /// </summary>
        public IList<VivoxMessage> VivoxMessages { get; internal set; } = new List<VivoxMessage>();

        /// <summary>
        /// Constructor for no history query result.
        /// </summary>
        public ChatHistoryQueryResult()
        {
            RequestId = "";
        }

        /// <summary>
        /// Constructor for a chat history query result.
        /// </summary>
        public ChatHistoryQueryResult(string requestId)
        {
            RequestId = requestId;
        }
    }
}
