namespace Unity.Services.Vivox
{
    internal class DirectedMessageResult : IDirectedMessageResult
    {
        public string RequestId { get; private set; }

        /// <summary>
        /// Constructor for no directed message result.
        /// </summary>
        public DirectedMessageResult()
        {
            RequestId = "";
        }

        /// <summary>
        /// Constructor for a directed message result.
        /// </summary>
        public DirectedMessageResult(string _requestID)
        {
            RequestId = _requestID;
        }
    }
}
