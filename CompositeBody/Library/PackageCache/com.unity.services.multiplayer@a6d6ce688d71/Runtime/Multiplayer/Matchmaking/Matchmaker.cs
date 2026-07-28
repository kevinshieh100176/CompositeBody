using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Services.Authentication.Internal;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using UnityEngine;

namespace Unity.Services.Multiplayer
{
    interface ISessionMatchmaking
    {
        public event Action<MatchmakerState> StateChanged;
        public event Action MatchFound;
        public event Action MatchFailed;
        public event Action<ISession> MatchJoined;
        public event Action MatchJoinFailed;

        public MatchmakerState State { get; }

        public string TicketId { get; }

        public Task CancelAsync();
        public Task<ISession> JoinAsync();
    }

    class Matchmaker : ISessionMatchmaking
    {
        const string k_EnclosingTypeName = nameof(Matchmaker);

        const int k_PollingDelaySeconds = 1;
        const int k_MatchmakingResultsRetryCount = 3;

        const string k_AssignmentTimeoutMessage = "Matchmaking took longer than the timeout value configured for the pool.";
        const string k_AssignmentFailedMessage = "Unknown failure while matchmaking.";

        public event Action<MatchmakerState> StateChanged;
        public event Action MatchFound;
        public event Action MatchFailed;
        public event Action<ISession> MatchJoined;
        public event Action MatchJoinFailed;

        public MatchmakerState State { get; internal set; }
        public MatchmakerAssignmentType AssignmentType { get; internal set; }
        public string TicketId { get; internal set; }

        bool IsAuthorized => m_AccessToken.AccessToken != null;
        CustomAssignment m_CustomAssignment;
        IpPortAssignment m_IpPortAssignment;
        MatchIdAssignment m_MatchIdAssignment;
        MultiplayAssignment m_MultiplayAssignment;
        readonly SessionOptions m_SessionOptions;

        long? m_PollingActionId;

        readonly ISessionManager m_SessionManager;
        readonly IActionScheduler m_ActionScheduler;
        readonly IMatchmakerService m_MatchmakerService;
        readonly IPlayerId m_PlayerId;
        readonly IAccessToken m_AccessToken;
        readonly IAccessTokenObserver m_AccessTokenObserver;
        readonly TaskCompletionSource<ISession> m_SessionCompletionSource;

        const int k_MultiplaySessionJoinTimeoutSeconds = 10;

        internal Matchmaker(
            string ticketId,
            SessionOptions sessionOptions,
            ISessionManager sessionManager,
            IActionScheduler actionScheduler,
            IMatchmakerService matchmaker,
            IPlayerId playerId,
            IAccessToken accessToken,
            IAccessTokenObserver accessTokenObserver,
            TaskCompletionSource<ISession> completionSource)
        {
            TicketId = ticketId;
            m_SessionOptions = sessionOptions;

            m_SessionManager = sessionManager;
            m_ActionScheduler = actionScheduler;
            m_MatchmakerService = matchmaker;
            m_PlayerId = playerId;
            m_AccessToken = accessToken;
            m_AccessTokenObserver = accessTokenObserver;
            m_SessionCompletionSource = completionSource;

            m_PlayerId.PlayerIdChanged += OnPlayerIdChanged;
            m_AccessTokenObserver.AccessTokenChanged += OnAccessTokenChanged;

            SetState(MatchmakerState.InProgress);
            SchedulePolling(0);

            Application.exitCancellationToken.Register(Cleanup);
        }

        void Cleanup()
        {
            if (State != MatchmakerState.InProgress)
            {
                return;
            }

            Logger.Log("Cleanup matchmaker - cancel polling, delete ticket.");
            try
            {
                CancelPolling();
                _ = CancelAsync();
            }
            catch (Exception e)
            {
                Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, $"Exception was thrown: {e.Message}");
            }
        }

        public async Task<ISession> JoinAsync()
        {
            ValidateAuthorization();
            ValidateTicketId();
            ValidateValidAssignment();

            if (State != MatchmakerState.MatchFound && State != MatchmakerState.JoinFailed)
            {
                throw new SessionException("Invalid Matchmaker State to join.", SessionError.InvalidMatchmakerState);
            }

            switch (AssignmentType)
            {
                case MatchmakerAssignmentType.Custom:
                    return await JoinCustomAssignmentAsync();
                case MatchmakerAssignmentType.IpPort:
                    return await JoinIpPortAssignmentAsync();
                case MatchmakerAssignmentType.MatchId:
                    return await JoinMatchIdAssignmentAsync();
                case MatchmakerAssignmentType.Multiplay:
                    return await JoinMultiplayAssignmentAsync();
            }

            SetState(MatchmakerState.JoinFailed);
            OnMatchJoinFailed();
            throw new SessionException("Invalid assignment", SessionError.InvalidMatchmakerAssignment);
        }

        async Task<ISession> JoinCustomAssignmentAsync()
        {
            try
            {
                var matchIdSession = await m_SessionManager.JoinByIdAsync(m_CustomAssignment.MatchId,
                    new JoinSessionOptions
                    {
                        Options = m_SessionOptions.Options,
                        Password = m_SessionOptions.Password,
                        Type = m_SessionOptions.Type,
                        PlayerProperties = m_SessionOptions.PlayerProperties
                    });
                SetState(MatchmakerState.Joined);
                OnMatchJoined(matchIdSession);
                return matchIdSession;
            }
            catch (SessionException e)
            {
                SetState(MatchmakerState.JoinFailed);
                OnMatchJoinFailed();
                m_SessionCompletionSource.TrySetException(e);
                throw;
            }
            catch (Exception e)
            {
                SetState(MatchmakerState.JoinFailed);
                OnMatchJoinFailed();
                var sessionException = new SessionException(e.Message, SessionError.Unknown);
                m_SessionCompletionSource.TrySetException(sessionException);
                throw new SessionException(e.Message, SessionError.MatchmakerAssignmentFailed);
            }
        }

        async Task<ISession> JoinIpPortAssignmentAsync()
        {
            try
            {
                var matchIdSession = await m_SessionManager.JoinByIdAsync(m_IpPortAssignment.MatchId,
                    new JoinSessionOptions
                    {
                        Options = m_SessionOptions.Options,
                        Password = m_SessionOptions.Password,
                        Type = m_SessionOptions.Type,
                        PlayerProperties = m_SessionOptions.PlayerProperties
                    });
                SetState(MatchmakerState.Joined);
                OnMatchJoined(matchIdSession);
                return matchIdSession;
            }
            catch (SessionException e)
            {
                SetState(MatchmakerState.JoinFailed);
                OnMatchJoinFailed();
                m_SessionCompletionSource.TrySetException(e);
                throw;
            }
            catch (Exception e)
            {
                SetState(MatchmakerState.JoinFailed);
                OnMatchJoinFailed();
                var sessionException = new SessionException(e.Message, SessionError.Unknown);
                m_SessionCompletionSource.TrySetException(sessionException);
                throw new SessionException(e.Message, SessionError.MatchmakerAssignmentFailed);
            }
        }

        async Task<ISession> JoinMatchIdAssignmentAsync()
        {
            try
            {
                var matchIdSession = await m_SessionManager.CreateOrJoinAsync(m_MatchIdAssignment.MatchId, m_SessionOptions);
                SetState(MatchmakerState.Joined);
                OnMatchJoined(matchIdSession);
                return matchIdSession;
            }
            catch (SessionException e)
            {
                SetState(MatchmakerState.JoinFailed);
                OnMatchJoinFailed();
                m_SessionCompletionSource.TrySetException(e);
                throw;
            }
            catch (Exception e)
            {
                SetState(MatchmakerState.JoinFailed);
                OnMatchJoinFailed();
                var sessionException = new SessionException(e.Message, SessionError.Unknown);
                m_SessionCompletionSource.TrySetException(sessionException);
                throw new SessionException(e.Message, SessionError.MatchmakerAssignmentFailed);
            }
        }

        async Task<ISession> JoinMultiplayAssignmentAsync()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while (stopwatch.Elapsed < TimeSpan.FromSeconds(k_MultiplaySessionJoinTimeoutSeconds))
            {
                try
                {
                    var multiplaySession = await m_SessionManager.JoinByIdAsync(m_MultiplayAssignment.MatchId,
                        new JoinSessionOptions
                        {
                            Options = m_SessionOptions.Options,
                            Password = m_SessionOptions.Password,
                            Type = m_SessionOptions.Type,
                            PlayerProperties = m_SessionOptions.PlayerProperties
                        });
                    SetState(MatchmakerState.Joined);
                    OnMatchJoined(multiplaySession);
                    return multiplaySession;
                }
                catch (SessionException e)
                {
                    if (e.Error == SessionError.SessionNotFound)
                    {
                        Logger.LogVerbose("Session not found, retrying...");
                        await WaitForSeconds(1);
                    }
                    else
                    {
                        SetState(MatchmakerState.JoinFailed);
                        OnMatchJoinFailed();
                        throw;
                    }
                }
                catch (Exception e)
                {
                    SetState(MatchmakerState.JoinFailed);
                    OnMatchJoinFailed();
                    throw new SessionException(e.Message, SessionError.MatchmakerAssignmentFailed);
                }
            }

            SetState(MatchmakerState.JoinFailed);
            OnMatchJoinFailed();
            throw new SessionException("Failed to join Multiplay session", SessionError.MatchmakerAssignmentFailed);
        }

        async Task WaitForSeconds(double seconds)
        {
            var tcs = new TaskCompletionSource<object>();
            m_ActionScheduler.ScheduleAction(() => tcs.SetResult(null), seconds);
            await tcs.Task;
        }

        public async Task CancelAsync()
        {
            ValidateAuthorization();
            ValidateTicketId();

            if (State != MatchmakerState.InProgress)
            {
                throw new SessionException("Invalid Matchmaker State to cancel.", SessionError.InvalidMatchmakerState);
            }

            Logger.LogVerbose($"Cancelling matchmaking ticket {TicketId}.");
            try
            {
                await m_MatchmakerService.DeleteTicketAsync(TicketId);
            }
            catch (Exception e)
            {
                Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, $"Exception was thrown: {e.Message}");
            }

            TicketId = null;
            SetState(MatchmakerState.Canceled);
        }

        public void Reset()
        {
            SetState(MatchmakerState.None);
        }

        internal void SetState(MatchmakerState state)
        {
            State = state;
            if (State is MatchmakerState.Canceled or MatchmakerState.None)
            {
                m_SessionCompletionSource?.TrySetCanceled();
            }
            StateChanged?.Invoke(State);
        }

        internal void SetCustomAssignment(CustomAssignment assignment)
        {
            AssignmentType = MatchmakerAssignmentType.Custom;
            m_CustomAssignment = assignment;
            SetState(MatchmakerState.MatchFound);
            OnMatchFound();
        }

        internal void SetIpPortAssignment(IpPortAssignment assignment)
        {
            AssignmentType = MatchmakerAssignmentType.IpPort;
            m_IpPortAssignment = assignment;
            SetState(MatchmakerState.MatchFound);
            OnMatchFound();
        }

        internal void SetMatchIdAssignment(MatchIdAssignment assignment)
        {
            AssignmentType = MatchmakerAssignmentType.MatchId;
            m_MatchIdAssignment = assignment;
            SetState(MatchmakerState.MatchFound);
            OnMatchFound();
        }

        internal void SetMultiplayAssignment(MultiplayAssignment assignment)
        {
            AssignmentType = MatchmakerAssignmentType.Multiplay;
            m_MultiplayAssignment = assignment;
            SetState(MatchmakerState.MatchFound);
            OnMatchFound();
        }

        async void OnMatchFound()
        {
            MatchFound?.Invoke();
            var session = await JoinAsync();
            m_SessionCompletionSource?.TrySetResult(session);
        }

        void OnMatchJoined(ISession session)
        {
            MatchJoined?.Invoke(session);
        }

        void OnMatchJoinFailed()
        {
            MatchJoinFailed?.Invoke();
        }

        internal void SetMatchFailure(string message, SessionError reason)
        {
            Logger.LogVerbose(message);
            SetState(MatchmakerState.MatchFailed);
            MatchFailed?.Invoke();
            throw new SessionException(message, reason);
        }

        internal void SchedulePolling(int seconds)
        {
            if (!m_PollingActionId.HasValue)
            {
                m_PollingActionId = m_ActionScheduler.ScheduleAction(RunScheduledPolling, seconds);
            }
        }

        internal void CancelPolling()
        {
            if (m_PollingActionId.HasValue)
            {
                m_ActionScheduler.CancelAction(m_PollingActionId.Value);
                m_PollingActionId = null;
            }
        }

        internal async void RunScheduledPolling()
        {
            m_PollingActionId = null;

            try
            {
                await PollTicketStatusAsync();
            }
            catch (Exception e)
            {
                _ = m_SessionCompletionSource.TrySetException(e);
            }

            if (State == MatchmakerState.InProgress)
            {
                SchedulePolling(k_PollingDelaySeconds);
            }
        }

        async Task PollTicketStatusAsync()
        {
            try
            {
                var ticketResponse = await m_MatchmakerService.GetTicketAsync(TicketId);

                if (ticketResponse.Type == typeof(NoneAssignment) &&
                    ticketResponse.Value is NoneAssignment noneAssignment)
                {
                    Logger.LogVerbose("Ticket response is being treated as a NoneAssignment");

                    switch (noneAssignment.Status)
                    {
                        case NoneAssignment.StatusOptions.Found:
                            SetMatchFailure("Assignment should have changed.", SessionError.MatchmakerAssignmentFailed);
                            return;
                        case NoneAssignment.StatusOptions.InProgress:
                            return;
                        case NoneAssignment.StatusOptions.Failed:
                            var failedMessage =
                                $"Ticket {noneAssignment.Status}: {(string.IsNullOrEmpty(noneAssignment.Message) ? k_AssignmentFailedMessage : noneAssignment.Message)}";
                            SetMatchFailure(failedMessage, SessionError.MatchmakerAssignmentFailed);
                            return;
                        case NoneAssignment.StatusOptions.Timeout:
                            var timeoutMessage =
                                $"Ticket {noneAssignment.Status}: {(string.IsNullOrEmpty(noneAssignment.Message) ? k_AssignmentTimeoutMessage : noneAssignment.Message)}";
                            SetMatchFailure(timeoutMessage, SessionError.MatchmakerAssignmentTimeout);
                            return;
                        default:
                            return;
                    }
                }

                if (ticketResponse.Type == typeof(CustomAssignment) &&
                    ticketResponse.Value is CustomAssignment customAssignment)
                {
                    Logger.LogVerbose("Ticket response is being treated as a CustomAssignment");

                    switch (customAssignment.Status)
                    {
                        case CustomAssignment.StatusOptions.Found:
                            SetCustomAssignment(customAssignment);
                            return;
                        case CustomAssignment.StatusOptions.InProgress:
                            return;
                        case CustomAssignment.StatusOptions.Failed:
                            var failedMessage =
                                $"Ticket {customAssignment.Status}: {(string.IsNullOrEmpty(customAssignment.Message) ? k_AssignmentFailedMessage : customAssignment.Message)}";
                            SetMatchFailure(failedMessage, SessionError.MatchmakerAssignmentFailed);
                            return;
                        case CustomAssignment.StatusOptions.Timeout:
                            var timeoutMessage =
                                $"Ticket {customAssignment.Status}: {(string.IsNullOrEmpty(customAssignment.Message) ? k_AssignmentTimeoutMessage : customAssignment.Message)}";
                            SetMatchFailure(timeoutMessage, SessionError.MatchmakerAssignmentTimeout);
                            return;
                        default:
                            return;
                    }
                }

                if (ticketResponse.Type == typeof(IpPortAssignment) &&
                    ticketResponse.Value is IpPortAssignment ipPortAssignment)
                {
                    Logger.LogVerbose("Ticket response is being treated as a IpPortAssignment");

                    switch (ipPortAssignment.Status)
                    {
                        case IpPortAssignment.StatusOptions.Found:
                            SetIpPortAssignment(ipPortAssignment);
                            return;
                        case IpPortAssignment.StatusOptions.InProgress:
                            return;
                        case IpPortAssignment.StatusOptions.Failed:
                            var failedMessage =
                                $"Ticket {ipPortAssignment.Status}: {(string.IsNullOrEmpty(ipPortAssignment.Message) ? k_AssignmentFailedMessage : ipPortAssignment.Message)}";
                            SetMatchFailure(failedMessage, SessionError.MatchmakerAssignmentFailed);
                            return;
                        case IpPortAssignment.StatusOptions.Timeout:
                            var timeoutMessage =
                                $"Ticket {ipPortAssignment.Status}: {(string.IsNullOrEmpty(ipPortAssignment.Message) ? k_AssignmentTimeoutMessage : ipPortAssignment.Message)}";
                            SetMatchFailure(timeoutMessage, SessionError.MatchmakerAssignmentTimeout);
                            return;
                        default:
                            return;
                    }
                }

                if (ticketResponse.Type == typeof(MultiplayAssignment) &&
                    ticketResponse.Value is MultiplayAssignment multiplayAssignment)
                {
                    Logger.LogVerbose("Ticket response is being treated as a MultiplayAssignment");

                    switch (multiplayAssignment.Status)
                    {
                        case MultiplayAssignment.StatusOptions.Found:
                            SetMultiplayAssignment(multiplayAssignment);
                            return;
                        case MultiplayAssignment.StatusOptions.InProgress:
                            return;
                        case MultiplayAssignment.StatusOptions.Failed:
                            var failedMessage =
                                $"Ticket {multiplayAssignment.Status}: {(string.IsNullOrEmpty(multiplayAssignment.Message) ? k_AssignmentFailedMessage : multiplayAssignment.Message)}";
                            SetMatchFailure(failedMessage, SessionError.MatchmakerAssignmentFailed);
                            return;
                        case MultiplayAssignment.StatusOptions.Timeout:
                            var timeoutMessage =
                                $"Ticket {multiplayAssignment.Status}: {(string.IsNullOrEmpty(multiplayAssignment.Message) ? k_AssignmentTimeoutMessage : multiplayAssignment.Message)}";
                            SetMatchFailure(timeoutMessage, SessionError.MatchmakerAssignmentTimeout);
                            return;
                        default:
                            return;
                    }
                }

                if (ticketResponse.Type == typeof(MatchIdAssignment) &&
                    ticketResponse.Value is MatchIdAssignment matchIdAssignment)
                {
                    Logger.LogVerbose("Ticket response is being treated as a MatchIdAssignment");

                    switch (matchIdAssignment.Status)
                    {
                        case MatchIdAssignment.StatusOptions.Found:
                            SetMatchIdAssignment(matchIdAssignment);
                            return;
                        case MatchIdAssignment.StatusOptions.InProgress:
                            return;
                        case MatchIdAssignment.StatusOptions.Failed:
                            var failedMessage =
                                $"Ticket {matchIdAssignment.Status}: {(string.IsNullOrEmpty(matchIdAssignment.Message) ? k_AssignmentFailedMessage : matchIdAssignment.Message)}";
                            SetMatchFailure(failedMessage, SessionError.MatchmakerAssignmentFailed);
                            return;
                        case MatchIdAssignment.StatusOptions.Timeout:
                            var timeoutMessage =
                                $"Ticket {matchIdAssignment.Status}: {(string.IsNullOrEmpty(matchIdAssignment.Message) ? k_AssignmentTimeoutMessage : matchIdAssignment.Message)}";
                            SetMatchFailure(timeoutMessage, SessionError.MatchmakerAssignmentTimeout);
                            return;
                        default:
                            return;
                    }
                }

                var message =
                    $"{nameof(m_MatchmakerService.GetTicketAsync)} returned an invalid assignment type. This operation is not supported.";
                throw new SessionException(message, SessionError.InvalidMatchmakerAssignment);
            }
            catch (MatchmakerServiceException e)
            {
                // Raise the right events & state change
                throw ConvertException(e);
            }
            catch (SessionException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new SessionException(e.Message, SessionError.MatchmakerAssignmentFailed);
            }
        }

        void ValidateAuthorization()
        {
            if (!IsAuthorized)
            {
                throw new SessionException("Player is not authorized", SessionError.NotAuthorized);
            }
        }

        void ValidateTicketId()
        {
            if (string.IsNullOrEmpty(TicketId))
            {
                throw new SessionException("Invalid matchmaker ticket", SessionError.InvalidMatchmakerTicket);
            }
        }

        void ValidateValidAssignment()
        {
            if (AssignmentType == MatchmakerAssignmentType.None)
            {
                throw new SessionException("No assignment found", SessionError.InvalidMatchmakerAssignment);
            }
        }

        private void OnPlayerIdChanged(string obj)
        {
            Reset();
        }

        private void OnAccessTokenChanged(string accessToken)
        {
            if (accessToken == null)
            {
                Reset();
            }
        }

        SessionException ConvertException(MatchmakerServiceException exception)
        {
            return new SessionException(exception.Message, SessionError.Unknown);
        }
    }
}
