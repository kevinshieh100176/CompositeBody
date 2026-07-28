namespace Unity.Services.Vivox
{
    internal class ArchiveQueryResult : IArchiveQueryResult
    {
        public string QueryId { get; private set; }
        public int ReturnCode { get; private set; }
        public int StatusCode { get; private set; }
        public string FirstId { get; private set; }
        public string LastId { get; private set; }
        public uint FirstIndex { get; private set; }
        public uint TotalCount { get; private set; }
        public bool Running { get; private set; }

        /// <summary>
        /// Constructor for no query.
        /// </summary>
        public ArchiveQueryResult()
        {
            QueryId = "";
            ReturnCode = -1;
            StatusCode = -1;
            FirstId = "";
            LastId = "";
            FirstIndex = 0;
            TotalCount = 0;
            Running = false;
        }

        /// <summary>
        /// Constructor for an uncompleted query.
        /// </summary>
        public ArchiveQueryResult(string queryId)
        {
            QueryId = queryId;
            ReturnCode = -1;
            StatusCode = -1;
            FirstId = "";
            LastId = "";
            FirstIndex = 0;
            TotalCount = 0;
            Running = true;
        }

        /// <summary>
        /// Constructor for a completed session archive query.
        /// </summary>
        public ArchiveQueryResult(vx_evt_session_archive_query_end_t evt)
        {
            QueryId = evt.query_id;
            ReturnCode = evt.return_code;
            StatusCode = evt.status_code;
            FirstId = evt.first_id;
            LastId = evt.last_id;
            FirstIndex = evt.first_index;
            TotalCount = evt.count;
            Running = false;
        }

        /// <summary>
        /// Constructor for a completed account archive query.
        /// </summary>
        public ArchiveQueryResult(vx_evt_account_archive_query_end_t evt)
        {
            QueryId = evt.query_id;
            ReturnCode = evt.return_code;
            StatusCode = evt.status_code;
            FirstId = evt.first_id;
            LastId = evt.last_id;
            FirstIndex = evt.first_index;
            TotalCount = evt.count;
            Running = false;
        }
    }
}
