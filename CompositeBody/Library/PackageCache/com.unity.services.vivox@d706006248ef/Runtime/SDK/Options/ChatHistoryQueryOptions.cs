using System;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Options for ChatHistoryQuery requests.
    /// Allows for things like filtering based on specific text, timestamps, or PlayerIds
    /// </summary>
    public sealed class ChatHistoryQueryOptions
    {
        /// <summary>
        /// The text to find in the query.
        /// Only messages that contain the specified text are returned.
        /// This is optional.
        /// The default setting is null.
        /// </summary>
        public string SearchText { get; set; } = null;

        /// <summary>
        /// Exclude all messages before a specified <see cref="DateTime"/> from the query results.
        /// This is optional.
        /// The default setting is null.
        /// </summary>
        public DateTime? TimeStart { get; set; } = null;

        /// <summary>
        /// Exclude all messages after a specified <see cref="DateTime"/> from the query results.
        /// The query results include messages that immediately follow the specified message in the result set.
        /// This is optional.
        /// The default setting is null.
        /// </summary>
        public DateTime? TimeEnd { get; set; } = null;

        /// <summary>
        /// If set only messages to or from the specified PlayerId are returned.
        /// Only used in channels queries and will have no affect on a Direct Message query.
        /// This is optional.
        /// The default setting is null.
        /// </summary>
        public string PlayerId { get; set; } = null;
    }
}
