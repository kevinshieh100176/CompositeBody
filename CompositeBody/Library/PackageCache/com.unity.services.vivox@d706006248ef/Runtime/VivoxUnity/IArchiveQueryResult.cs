namespace Unity.Services.Vivox
{
    /// <summary>
    /// The result of a session or account archive query.
    /// </summary>
    internal interface IArchiveQueryResult
    {
        /// <summary>
        /// The ID of a successfully started query.
        /// </summary>
        string QueryId { get; }
        /// <summary>
        /// The query result code.
        /// </summary>
        int ReturnCode { get; }
        /// <summary>
        /// The query status code.
        /// </summary>
        int StatusCode { get; }
        /// <summary>
        /// The first returned message ID.
        /// </summary>
        string FirstId { get; }
        /// <summary>
        /// The last returned message ID.
        /// </summary>
        string LastId { get; }
        /// <summary>
        /// The index of the first matching message.
        /// </summary>
        uint FirstIndex { get; }
        /// <summary>
        /// The total number of messages that match the criteria specified in the request.
        /// </summary>
        uint TotalCount { get; }
        /// <summary>
        /// Indicates whether the archive query is complete.
        /// </summary>
        bool Running { get; }
    }
}
