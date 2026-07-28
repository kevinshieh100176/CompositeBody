using System.Collections.Generic;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// A Generic class for creating options for querying sessions.
    /// </summary>
    public class QuerySessionsOptions
    {
        /// <summary>
        /// The number of results to return.
        /// </summary>
        public int Count { get; set; } = 100;

        /// <summary>
        /// The offset to start the query from.
        /// </summary>
        public int Skip { get; set; } = 0;

        /// <summary>
        /// A list of filters which can be used to narrow down which lobbies to return.
        /// </summary>
        public List<FilterOption> FilterOptions { get; set; } = new List<FilterOption>();

        /// <summary>
        /// How the results of the session query should be sorted.
        /// </summary>
        public List<SortOption> SortOptions { get; set; } = new List<SortOption>();

        /// <summary>
        /// ContinuationToken used for pagination.
        /// </summary>
        public string ContinuationToken { get; set; }
    }
}
