namespace Unity.Services.Vivox
{
    /// <summary>
    /// The result of a directed message (user-to-user).
    /// </summary>
    internal interface IDirectedMessageResult
    {
        /// <summary>
        /// The request ID of the directed message.
        /// </summary>
        string RequestId { get; }
    }
}
