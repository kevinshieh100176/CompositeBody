using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// The result classed used internally to aggregate chat history query's
    /// </summary>
    internal interface IChatHistoryQueryResult
    {
        /// <summary>
        /// The vivox internal request ID of the chat history query.
        /// </summary>
        string RequestId { get; }

        /// <summary>
        /// The Participant we are filtering results on
        /// Optional: Default is null
        /// </summary>
        AccountId Participant { get; }

        /// <summary>
        /// A list of <see cref="VivoxMessage"/> returned from the query
        /// </summary>
        IList<VivoxMessage> VivoxMessages { get;}
    }
}
