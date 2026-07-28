using System;
using System.Collections.Generic;
using Unity.Services.Core.Scheduler.Internal;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Results from a session query.
    /// </summary>
    public class QuerySessionsResults
    {
        /// <summary>
        /// ContinuationToken used for pagination.
        /// </summary>
        /// <remarks>
        /// The continuation token is an opaque string that includes all the necessary information to obtain
        /// the next page of results. It is used by <see cref="QuerySessionsOptions.ContinuationToken"/>.
        /// </remarks>
        public string ContinuationToken { get; private set; }

        /// <summary>
        /// Public information for each session returned by the query.
        /// </summary>
        /// <remarks>
        /// To join one of the sessions, use its <see cref="ISessionInfo.Id"/> and
        /// <see cref="ISessionManager.JoinByIdAsync"/>.
        /// </remarks>
        public IList<ISessionInfo> Sessions { get; private set; }
        readonly ISessionQuerier m_SessionQuerier;
        readonly QuerySessionsOptions m_QuerySessionsOptions;
        readonly IActionScheduler m_ActionScheduler;
        long? m_ActionId;

        internal QuerySessionsResults(List<ISessionInfo> sessions,
                                      string continuationToken,
                                      QuerySessionsOptions querySessionsOptions,
                                      ISessionQuerier querier,
                                      IActionScheduler actionScheduler)
        {
            ContinuationToken = continuationToken;
            m_QuerySessionsOptions = querySessionsOptions;
            m_SessionQuerier = querier;
            Sessions = sessions.AsReadOnly();
            m_ActionScheduler = actionScheduler;
        }

        async void PollOnce(int pollingDelaySeconds)
        {
            try
            {
                var result = await m_SessionQuerier.QueryAsync(m_QuerySessionsOptions);
                Sessions = result.Sessions;
            }
            catch (Exception ex)
            {
                // In the case of a transient network error, we don't want to stop polling.
                // a retry will be done at the next interval.
                Logger.LogError($"Error polling for sessions: {ex.Message}");
            }

            m_ActionId = m_ActionScheduler.ScheduleAction(() => PollOnce(pollingDelaySeconds), pollingDelaySeconds);
        }

        /// <summary>
        /// Starts background polling for new query results, re-using the same options.
        /// </summary>
        /// <remarks>
        /// Useful for live-updating UIs as session results can change frequently as they fill up or new ones get
        /// created. The polling delay should be of at least 1 second to avoid rate limiting by the service.
        /// </remarks>
        /// <param name="pollingDelaySeconds">The interval at which we should poll the query results</param>
        public void StartPolling(int pollingDelaySeconds = 5)
        {
            if (!m_ActionId.HasValue)
            {
                m_ActionScheduler.ScheduleAction(() => PollOnce(pollingDelaySeconds), pollingDelaySeconds);
            }
        }

        /// <summary>
        /// Stops background polling for new query results.
        /// </summary>
        public void StopPolling()
        {
            if (m_ActionId.HasValue)
            {
                m_ActionScheduler.CancelAction(m_ActionId.Value);
            }
        }
    }
}
