using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication.Internal;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using Unity.Services.Qos;
using Unity.Services.Qos.V2.Models;

namespace Unity.Services.Multiplayer
{
    interface IMatchmakerManager
    {
        Task<ISession> StartAsync(MatchmakerOptions matchOptions, SessionOptions sessionOptions, CancellationToken token = default);
    }

    class MatchmakerManager : IMatchmakerManager
    {
        const string k_UnknownRegion = "unknown-region";

        readonly ISessionManager m_SessionManager;
        readonly IActionScheduler m_ActionScheduler;
        readonly IMatchmakerService m_MatchmakerService;
        readonly IPlayerId m_PlayerId;
        readonly IAccessToken m_AccessToken;
        readonly IAccessTokenObserver m_AccessTokenObserver;
        readonly QosCalculator m_QosCalculator;

        public MatchmakerManager(
            ISessionManager sessionManager,
            IActionScheduler actionScheduler,
            IQosService qosService,
            IMatchmakerService matchmakerService,
            IPlayerId playerId,
            IAccessToken accessToken,
            IAccessTokenObserver accessTokenObserver)
        {
            m_SessionManager = sessionManager;
            m_ActionScheduler = actionScheduler;
            m_MatchmakerService = matchmakerService;
            m_PlayerId = playerId;
            m_AccessToken = accessToken;
            m_AccessTokenObserver = accessTokenObserver;

            m_QosCalculator = new QosCalculator(qosService);
        }

        public async Task<ISession> StartAsync(MatchmakerOptions matchOptions, SessionOptions sessionOptions, CancellationToken token = default)
        {
            var player = await PreparePlayerAsync(
                m_PlayerId.PlayerId,
                matchOptions.PlayerProperties?.ToDictionary(x => x.Key, x => x.Value.Value),
                matchOptions.QueueName);

            try
            {
                var ticketOptions = new CreateTicketOptions(matchOptions.QueueName, matchOptions.TicketAttributes);
                var ticketResponse = await m_MatchmakerService.CreateTicketAsync(new List<Services.Matchmaker.Models.Player> { player }, ticketOptions);

                var completionSource = new TaskCompletionSource<ISession>();
                var matchmaker = new Matchmaker(ticketResponse.Id, sessionOptions, m_SessionManager, m_ActionScheduler,
                    m_MatchmakerService, m_PlayerId, m_AccessToken, m_AccessTokenObserver, completionSource);

                token.Register(CancelMatchmaking);

                return await completionSource.Task;

                async void CancelMatchmaking()
                {
                    matchmaker.CancelPolling();

                    try
                    {
                        await matchmaker.CancelAsync();
                    }
                    catch (SessionException e)
                    {
                        if (e.Error == SessionError.InvalidMatchmakerState)
                        {
                            Logger.LogVerbose("Ignoring failed cancellation while matchmaking is not in progress.");
                        }
                    }
                    catch (Exception e)
                    {
                        throw new SessionException(e.Message, SessionError.Unknown);
                    }

                    completionSource.TrySetCanceled();
                }
            }
            catch (TaskCanceledException e)
            {
                throw new SessionException(e.Message, SessionError.MatchmakerCancelled);
            }
            catch (Exception e) when (e is not SessionException)
            {
                throw new SessionException(e.Message, SessionError.Unknown);
            }
        }

        async Task<Services.Matchmaker.Models.Player> PreparePlayerAsync(string playerId, object customData, string queueName)
        {
            var qosResults = await m_QosCalculator.GetQosResultsAsync(queueName);
            return new Services.Matchmaker.Models.Player(playerId, customData, qosResults);
        }
    }
}
