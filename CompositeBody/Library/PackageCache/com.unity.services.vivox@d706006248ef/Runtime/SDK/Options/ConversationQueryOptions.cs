using System;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Options for <see cref="IVivoxService.GetConversationsAsync(ConversationQueryOptions)"/> requests.
    /// </summary>
    public sealed class ConversationQueryOptions
    {
        /// <summary>
        /// The Page number you want to retrieve, 0-indexed
        /// Returns only the first page by default.
        /// </summary>
        public int PageCursor { get; set; } = 0;

        /// <summary>
        /// This timestamp will make a <see cref="IVivoxService.GetConversationsAsync(ConversationQueryOptions)"/> query return the results as they would be at a specific point in time.
        /// Any changes in what conversations the user is a part of or updates to existing conversations that would have happened after the cutoff time will be omitted from the results of the query.
        /// </summary>
        public DateTime? CutoffTime { get; set; } = null;

        /// <summary>
        /// The number of conversations that will be returned per page.
        /// A maximum value of 50 will be respected
        /// </summary>
        public int PageSize { get; set; } = 10;
    }
}
