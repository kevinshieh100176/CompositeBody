using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Services.Vivox
{
    internal class LoginSession : ILoginSession
    {
        #region Member Variables

        public readonly IVivoxServiceInternal _parentVivoxServiceInstance;
        private readonly string _accountHandle;
        private readonly string _groupHandle;
        private readonly Client _client;
        private LoginState _state = LoginState.LoggedOut;
        private TransmissionMode _transmissionType = TransmissionMode.None;
        private bool _isInjectingAudio = false;
        private readonly ReadWriteDictionary<ChannelId, IChannelSession, ChannelSession> _channelSessions = new ReadWriteDictionary<ChannelId, IChannelSession, ChannelSession>();
        private ChannelId _transmittingChannel;
        private List<ChannelId> _channelsToDelete = new List<ChannelId>();
        private readonly ReadWriteHashSet<AccountId> _blockedSubscriptions = new ReadWriteHashSet<AccountId>();
        private readonly ReadWriteHashSet<AccountId> _allowedSubscriptions = new ReadWriteHashSet<AccountId>();
        private readonly ReadWriteDictionary<AccountId, IPresenceSubscription, PresenceSubscription> _presenceSubscriptions = new ReadWriteDictionary<AccountId, IPresenceSubscription, PresenceSubscription>();
        private Presence _presence;

        /// <summary>
        /// Local internal cache of chat history results by queryId as the key.  In the future we may want to leverage
        /// this and change the key to a Unity request id and populate with a caching and queue mechanism.
        /// </summary>
        private readonly ConcurrentDictionary<string, IList<VivoxMessage>> _internalChatHistoryResults =
            new ConcurrentDictionary<string, IList<VivoxMessage>>();
        /// <summary>
        /// Internal results and tasks created by and awaited by <see cref="GetChannelTextMessageHistoryAsync"/>
        /// </summary>
        private readonly ConcurrentDictionary<string, TaskCompletionSource<IChatHistoryQueryResult>> _chatHistoryTaskResults =
            new ConcurrentDictionary<string, TaskCompletionSource<IChatHistoryQueryResult>>();

        private TaskCompletionSource<bool> _loginSessionConnectTaskCompletionSource = new TaskCompletionSource<bool>();
        private readonly ReadWriteQueue<IDirectedTextMessage> _directedMessages = new ReadWriteQueue<IDirectedTextMessage>();
        private readonly ReadWriteQueue<IFailedDirectedTextMessage> _failedDirectedMessages = new ReadWriteQueue<IFailedDirectedTextMessage>();
        private readonly ReadWriteQueue<IAccountArchiveMessage> _accountArchive = new ReadWriteQueue<IAccountArchiveMessage>();
        //TODO: Currently there needs to be 2 seconds between the last message send and the account archive query, this should be fixed
        private DateTime lastMessageTime;
        private DirectedMessageResult _directedMessageResult = new DirectedMessageResult();
        private ArchiveQueryResult _accountArchiveResult = new ArchiveQueryResult();
        private ConnectionRecoveryState _connectionRecoveryState;

        private ReadWriteHashSet<AccountId> _crossMutedCommunications = new ReadWriteHashSet<AccountId>();
        private readonly ReadWriteQueue<AccountId> _incomingSubscriptionRequests = new ReadWriteQueue<AccountId>();
        private ParticipantPropertyUpdateFrequency _participantPropertyFrequency = ParticipantPropertyUpdateFrequency.StateChange;
        private readonly ITextToSpeech _ttsSubSystem;

        #endregion

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        internal LoginSession(Client client, AccountId accountId)
        {
            if (AccountId.IsNullOrEmpty(accountId))
            {
                throw new ArgumentNullException(nameof(accountId));
            }

            _client = client ?? throw new ArgumentNullException(nameof(client));
            Key = accountId;
            _accountHandle = accountId.ToString();
            _groupHandle = "sg_" + _accountHandle;
            _ttsSubSystem = new TextToSpeech(_client);
            _connectionRecoveryState = ConnectionRecoveryState.Disconnected;
            VxClient.Instance.EventMessageReceived += Instance_EventMessageReceived;
        }

        internal LoginSession(Client client, AccountId accountId, IVivoxServiceInternal vivoxServiceInstance) : this(client, accountId)
        {
            _parentVivoxServiceInstance = vivoxServiceInstance ?? throw new ArgumentNullException(nameof(vivoxServiceInstance));
        }

        #region Property Change Handlers

        private void Instance_EventMessageReceived(vx_evt_base_t eventMessage)
        {
            switch ((vx_event_type)eventMessage.type)
            {
                case vx_event_type.evt_account_login_state_change:
                    HandleAccountLoginStateChangeEvt(eventMessage);
                    break;
                case vx_event_type.evt_buddy_presence:
                    HandleBuddyPresenceEvt(eventMessage);
                    break;
                case vx_event_type.evt_user_to_user_message:
                    HandleUserToUserMessage(eventMessage);
                    break;
                case vx_event_type.evt_subscription:
                    HandleSubscription(eventMessage);
                    break;
                case vx_event_type.evt_account_archive_message:
                    HandleAccountArchiveMessage(eventMessage);
                    break;
                case vx_event_type.evt_account_archive_query_end:
                    HandleAccountArchiveQueryEnd(eventMessage);
                    break;
                case vx_event_type.evt_media_completion:
                    HandleMediaComplete(eventMessage);
                    break;
                case vx_event_type.evt_account_send_message_failed:
                    HandleAccountSendMessageFailed(eventMessage);
                    break;
                case vx_event_type.evt_connection_state_changed:
                    HandleDisconnectRecovery(eventMessage);
                    break;
                case vx_event_type.evt_account_edit_message:
                    HandleAccountEditMessage(eventMessage);
                    break;
                case vx_event_type.evt_account_delete_message:
                    HandleAccountDeleteMessage(eventMessage);
                    break;
            }
        }

        private void HandleAccountEditMessage(vx_evt_base_t eventMessage)
        {
            vx_evt_account_edit_message_t evt = eventMessage;
            if (evt.account_handle != _accountHandle)
            {
                return;
            }

            Debug.Assert(evt != null);

            var parsedReceivedTime = Helper.UnixTimeStampToDateTime(evt.edit_time);

            var message = new DirectedTextMessage()
            {
                Sender = new AccountId(evt.from_user),
                SenderDisplayName = evt.displayname,
                Message = evt.new_message,
                ReceivedTime = parsedReceivedTime,
                Key = evt.message_id,
                Language = evt.language,
                LoginSession = this,
                FromSelf = evt.from_user == Key.ToString()
            };

            DirectedMessageEdited?.Invoke(this, new VivoxMessage(_parentVivoxServiceInstance, message));
        }

        private void HandleAccountDeleteMessage(vx_evt_base_t eventMessage)
        {
            vx_evt_account_delete_message_t evt = eventMessage;
            if (evt.account_handle != _accountHandle) return;

            DateTime parsedReceivedTime = Helper.UnixTimeStampToDateTime(evt.delete_time);

            var message = new DirectedTextMessage()
            {
                Sender = new AccountId(evt.from_user),
                Message = null,
                ReceivedTime = parsedReceivedTime,
                Key = evt.message_id,
                Language = null,
                LoginSession = this,
                FromSelf = evt.from_user == this.Key.ToString()
            };

            DirectedMessageDeleted?.Invoke(this, new VivoxMessage(_parentVivoxServiceInstance, message));
        }

        public void HandleMediaComplete(vx_evt_base_t eventMessage)
        {
            vx_evt_media_completion_t evt = eventMessage;
            Debug.Assert(evt != null);
            switch (evt.completion_type)
            {
                case vx_media_completion_type.sessiongroup_audio_injection:
                    IsInjectingAudio = false;
                    break;
            }
        }

        private void HandleBuddyPresenceEvt(vx_evt_base_t eventMessage)
        {
            vx_evt_buddy_presence_t evt = eventMessage;
            if (evt.account_handle != _accountHandle) return;

            var buddyAccount = new AccountId(evt.buddy_uri, evt.displayname);
            if (!_presenceSubscriptions.ContainsKey(buddyAccount)) return;

            var subscription = (PresenceSubscription)_presenceSubscriptions[buddyAccount];
            subscription.UpdateLocation(evt.buddy_uri, (PresenceStatus)evt.presence,
                evt.custom_message);
        }

        private void HandleUserToUserMessage(vx_evt_base_t eventMessage)
        {
            vx_evt_user_to_user_message_t evt = eventMessage;
            if (evt.account_handle != _accountHandle) return;
            Debug.Assert(evt != null);
            _directedMessages.Enqueue(new DirectedTextMessage
            {
                ReceivedTime = DateTime.Now,
                Message = evt.message_body,
                SenderDisplayName = evt.from_displayname,
                Key = evt.message_id,
                Sender = new AccountId(evt.from_uri, evt.from_displayname),
                ApplicationStanzaBody = evt.application_stanza_body,
                ApplicationStanzaNamespace = evt.application_stanza_namespace,
                Language = evt.language,
                FromSelf = evt.from_uri == Key.ToString(),
                LoginSession = this
            });
        }

        private void HandleAccountLoginStateChangeEvt(vx_evt_base_t eventMessage)
        {
            vx_evt_account_login_state_change_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.account_handle != _accountHandle) return;
            State = (LoginState)evt.state;

            if (evt.status_code != 0)
            {
                var exception = new VivoxApiException(evt.status_code);
                if (_loginSessionConnectTaskCompletionSource.Task == null || _loginSessionConnectTaskCompletionSource.Task.IsCompleted)
                {
                    // If we get an out-of-band disconnect, handle throwing an exception in that case as well.
                    VivoxLogger.LogVxException(exception);
                }
                else
                {
                    // Throws if we encounter any errors while actively trying to login.
                    _loginSessionConnectTaskCompletionSource.TrySetException(exception);
                }
            }

            // Complete the LoginAsync task
            if (State == LoginState.LoggedIn && !_loginSessionConnectTaskCompletionSource.Task.IsCompleted)
            {
                _loginSessionConnectTaskCompletionSource.TrySetResult(true);
            }
        }

        private void HandleSubscription(vx_evt_base_t eventMessage)
        {
            vx_evt_subscription_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.account_handle != _accountHandle) return;
            _incomingSubscriptionRequests.Enqueue(new AccountId(evt.buddy_uri, evt.displayname));
        }

        private void HandleAccountArchiveMessage(vx_evt_base_t eventMessage)
        {
            vx_evt_account_archive_message_t evt = eventMessage;
            if (evt.account_handle != _accountHandle)
            {
                return;
            }

            if (!DateTime.TryParse(evt.time_stamp, out var parsedReceivedTime))
            {
                VivoxLogger.LogError($"{GetType().Name}: {eventMessage.GetType().Name} invalid message: Bad time format");
                return;
            }

            var queryId = evt.query_id;
            var isInbound = evt.is_inbound != 0;
            var message = new AccountArchiveMessage()
            {
                LoginSession = this,
                Key = evt.message_id,
                SenderDisplayName = evt.displayname,
                MessageId = evt.message_id,
                QueryId = evt.query_id,
                ReceivedTime = parsedReceivedTime,
                Message = evt.message_body,
                Inbound = isInbound,
                Language = evt.language,
                RemoteParticipant = new AccountId(evt.participant_uri),
                Sender = isInbound ? new AccountId(evt.participant_uri) : Key,
            };
            // Add messages to list as we will await other messages and then return when we are finished
            _internalChatHistoryResults.AddOrUpdate(queryId,
                new List<VivoxMessage>() { new VivoxMessage(_parentVivoxServiceInstance, message) },
                (k, l) =>
                {
                    l.Add(new VivoxMessage(_parentVivoxServiceInstance, message));
                    return l;
                });
            _accountArchive.Enqueue(message);
        }

        private void HandleAccountArchiveQueryEnd(vx_evt_base_t eventMessage)
        {
            vx_evt_account_archive_query_end_t evt = eventMessage;
            if (evt.account_handle != _accountHandle) return;


            //// Chat History Query Context
            var queryId = evt.query_id;
            var internalQueryCache = _internalChatHistoryResults.FirstOrDefault(r => r.Key == queryId);
            var taskToComplete = _chatHistoryTaskResults.FirstOrDefault(r => r.Key == queryId);
            var result = new ChatHistoryQueryResult(queryId)
            {
                VivoxMessages = internalQueryCache.Value ?? new List<VivoxMessage>()
            };
            taskToComplete.Value?.TrySetResult(result);

            // Cleanup internal query cache now that we don't need it.  If we received no messages this will be null
            if (internalQueryCache.Key != null)
            {
                _internalChatHistoryResults?.TryRemove(internalQueryCache.Key, out _);
            }
            ////

            if (_accountArchiveResult.QueryId != queryId || !_accountArchiveResult.Running) return;
            _accountArchiveResult = new ArchiveQueryResult(evt);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccountArchiveResult)));
        }

        private void HandleAccountSendMessageFailed(vx_evt_base_t eventMessage)
        {
            vx_evt_account_send_message_failed_t evt = eventMessage;
            Debug.Assert(evt != null);
            AssertLoggedIn();
            if (evt.account_handle != _accountHandle) return;
            if (_directedMessageResult.RequestId != evt.request_id) return;
            _failedDirectedMessages.Enqueue(new FailedDirectedTextMessage
            {
                Sender = new AccountId(evt.account_handle),
                FromSelf = true,
                RequestId = evt.request_id,
                StatusCode = evt.status_code
            });
        }

        private void HandleDisconnectRecovery(vx_evt_base_t eventMessage)
        {
            vx_evt_connection_state_changed_t evt = eventMessage;
            Debug.Assert(evt != null);
            _connectionRecoveryState = (ConnectionRecoveryState)evt.connection_state;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecoveryState)));
        }

        #endregion

        internal string AccountHandle => _accountHandle;

        #region ILoginSession

        public AccountId LoginSessionId => Key;
        public ConnectionRecoveryState RecoveryState => _connectionRecoveryState;
        public ITextToSpeech TTS => _ttsSubSystem;
        public ParticipantPropertyUpdateFrequency ParticipantPropertyFrequency
        {
            get { return _participantPropertyFrequency; }
            set
            {
                AssertLoggedOut();
                _participantPropertyFrequency = value;
            }
        }
        public IReadOnlyDictionary<ChannelId, IChannelSession> ChannelSessions => _channelSessions;
        public ChannelId TransmittingChannel
        {
            get { return _transmittingChannel; }
            set
            {
                if (value == null && _transmittingChannel == null)
                    return;
                if ((value == null || _transmittingChannel == null) || !value.Equals(_transmittingChannel))
                {
                    _transmittingChannel = value;
                    _transmissionType = TransmissionMode.Single;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransmittingChannel)));
                }
            }
        }
        public AccountId Key { get; }
        public LoginState State
        {
            get { return _state; }
            private set
            {
                if (value != _state)
                {
                    _state = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
                    if (value == LoginState.LoggedOut)
                    {
                        _client.RemoveLoginSession(Key);
                        Cleanup();
                    }
                }
            }
        }
        public bool IsInjectingAudio
        {
            get { return _isInjectingAudio; }
            private set
            {
                if (_isInjectingAudio != value)
                {
                    _isInjectingAudio = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInjectingAudio)));
                }
            }
        }

        public IReadOnlyQueue<IDirectedTextMessage> DirectedMessages => _directedMessages;
        public IReadOnlyQueue<IFailedDirectedTextMessage> FailedDirectedMessages => _failedDirectedMessages;

        public event EventHandler<VivoxMessage> DirectedMessageEdited;

        public event EventHandler<VivoxMessage> DirectedMessageDeleted;
        public IReadOnlyQueue<IAccountArchiveMessage> AccountArchive => _accountArchive;
        public IArchiveQueryResult AccountArchiveResult => _accountArchiveResult;
        public IDirectedMessageResult DirectedMessageResult => _directedMessageResult;

        public Presence Presence
        {
            get { return _presence; }
            set
            {
                if (VxClient.Instance.IsQuitting)
                {
                    return;
                }
                AssertLoggedIn();
                if (!Equals(_presence, value))
                {
                    AsyncNoResult ar = new AsyncNoResult(null);

                    var request = new vx_req_account_set_presence_t
                    {
                        account_handle = _accountHandle,
                        custom_message = value.Message,
                        presence = (vx_buddy_presence_state)value.Status
                    };

                    VxClient.Instance.BeginIssueRequest(request, result =>
                    {
                        try
                        {
                            VxClient.Instance.EndIssueRequest(result);
                            _presence = value;
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Presence)));
                            ar.SetComplete();
                        }
                        catch (Exception e)
                        {
                            VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                            ar.SetComplete(e);
                            throw;
                        }
                    });
                }
            }
        }
        public IReadOnlyDictionary<AccountId, IPresenceSubscription> PresenceSubscriptions => _presenceSubscriptions;
        public IReadOnlyHashSet<AccountId> BlockedSubscriptions => _blockedSubscriptions;
        public IReadOnlyHashSet<AccountId> AllowedSubscriptions => _allowedSubscriptions;
        public IReadOnlyQueue<AccountId> IncomingSubscriptionRequests => _incomingSubscriptionRequests;

        public IReadOnlyHashSet<AccountId> CrossMutedCommunications => _crossMutedCommunications;

        public TransmissionMode TransmissionType => _transmissionType;

        public ReadOnlyCollection<ChannelId> TransmittingChannels
        {
            get
            {
                List<ChannelId> channels = new List<ChannelId>();
                switch (_transmissionType)
                {
                    case TransmissionMode.Single:
                    {
                        channels.Add(_transmittingChannel);
                        break;
                    }
                    case TransmissionMode.All:
                    {
                        foreach (var channelSession in ChannelSessions)
                        {
                            channels.Add(channelSession.Key);
                        }
                        break;
                    }
                    case TransmissionMode.None:
                    default:
                        break;
                }
                return channels.AsReadOnly();
            }
        }

        public async Task LoginAsync(
            SubscriptionMode subscriptionMode = SubscriptionMode.Accept,
            IReadOnlyHashSet<AccountId> presenceSubscriptions = null,
            IReadOnlyHashSet<AccountId> blockedPresenceSubscriptions = null,
            IReadOnlyHashSet<AccountId> allowedPresenceSubscriptions = null,
            string accessToken = null,
            AsyncCallback callback = null
        )
        {
            var token = accessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                var tokenFetch = Client.tokenGen.GetTokenAsync(Key.Issuer, TimeSpan.FromSeconds(VxTokenGen.k_defaultTokenExpirationInSeconds), null, "login", null, fromUserUri: _accountHandle);
                await tokenFetch;
                token = tokenFetch.Result;
            }
            _loginSessionConnectTaskCompletionSource = new TaskCompletionSource<bool>();

            using (var ct = new CancellationTokenSource(Client.requestTimeout))
            {
                ct.Token.Register(() =>
                {
                    _loginSessionConnectTaskCompletionSource.TrySetException(exception: new TimeoutException($"[Vivox] Failed to Login user: {Key.Name}"));
                    State = LoginState.LoggedOut;
                });
                await Task.Factory.FromAsync(
                    BeginLogin(token, SubscriptionMode.Accept, presenceSubscriptions, blockedPresenceSubscriptions, allowedPresenceSubscriptions, callback),
                    ar =>
                    {
                        try
                        {
                            EndLogin(ar);
                            return Task.CompletedTask;
                        }
                        catch (Exception e)
                        {
                            // Unsubscribe from any events if we failed
                            PropertyChanged = null;
                            _loginSessionConnectTaskCompletionSource.TrySetException(e);
                            throw;
                        }
                    });
                await _loginSessionConnectTaskCompletionSource.Task;
            }
        }

        public async Task SendDirectedMessageAsync(AccountId accountId, string message, MessageOptions options)
        {
            await Task.Factory.FromAsync(
                BeginSendDirectedMessage(accountId, message, options, null),
                (ar) =>
                {
                    try
                    {
                        EndSendDirectedMessage(ar);
                        return Task.CompletedTask;
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                });
        }

        private AsyncResult<string> BeginChatHistoryQuery(AccountId recipient, int requestSize, ChatHistoryQueryOptions queryOptions = null, AsyncCallback callback = null)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            // If filtering on a player Id we will want to track the cursor based on a per participant basis
            var taskChatHistoryQueryResult = new ChatHistoryQueryResult();

            var ar = new AsyncResult<string>(callback);
            var request = new vx_req_account_chat_history_query_t()
            {
                account_handle = _accountHandle,
                max = (uint)requestSize,
                participant_uri = recipient?.ToString(),
                search_text = queryOptions?.SearchText,
                time_end = queryOptions?.TimeEnd?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? null,
                time_start = queryOptions?.TimeStart?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? null,
            };

            // Create task for the response to be completed by core events later in message received and end methods above
            var messageQueryCompletionSource = new TaskCompletionSource<IChatHistoryQueryResult>(taskChatHistoryQueryResult);
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    vx_resp_account_chat_history_query_t response = VxClient.Instance.EndIssueRequest(result);
                    _chatHistoryTaskResults.TryAdd(response.query_id, messageQueryCompletionSource);
                    ar.SetComplete(response.query_id);
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    messageQueryCompletionSource.TrySetException(e);
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        public void EndChatHistoryQuery(IAsyncResult result)
        {
            AsyncNoResult ar = result as AsyncNoResult;
            ar?.CheckForError();
        }

        public IAsyncResult BeginLogin(
            string accessToken,
            SubscriptionMode subscriptionMode,
            IReadOnlyHashSet<AccountId> presenceSubscriptions,
            IReadOnlyHashSet<AccountId> blockedPresenceSubscriptions,
            IReadOnlyHashSet<AccountId> allowedPresenceSubscriptions,
            AsyncCallback callback = null)
        {
            return BeginLogin(null, accessToken: accessToken, subscriptionMode, presenceSubscriptions, blockedPresenceSubscriptions, allowedPresenceSubscriptions, false, true, callback);
        }

        public IAsyncResult BeginLogin(
            Uri server,
            string accessToken,
            SubscriptionMode subscriptionMode,
            IReadOnlyHashSet<AccountId> presenceSubscriptions,
            IReadOnlyHashSet<AccountId> blockedPresenceSubscriptions,
            IReadOnlyHashSet<AccountId> allowedPresenceSubscriptions,
            AsyncCallback callback = null)
        {
            return BeginLogin(server, accessToken, subscriptionMode, presenceSubscriptions, blockedPresenceSubscriptions, allowedPresenceSubscriptions, true, true, callback);
        }

        public IAsyncResult BeginLogin(
            Uri server,
            string accessToken,
            AsyncCallback callback)
        {
            return BeginLogin(server, accessToken, SubscriptionMode.Block, null, null, null, true, false, callback);
        }

        internal IAsyncResult BeginLogin(
            Uri server = null,
            string accessToken = null,
            SubscriptionMode subscriptionMode = SubscriptionMode.Block,
            IReadOnlyHashSet<AccountId> presenceSubscriptions = null,
            IReadOnlyHashSet<AccountId> blockedPresenceSubscriptions = null,
            IReadOnlyHashSet<AccountId> allowedPresenceSubscriptions = null,
            bool uriServerRequired = false,
            bool presenceDesired = false,
            AsyncCallback callback = null)
        {
            if (string.IsNullOrEmpty(accessToken)) throw new ArgumentNullException(nameof(accessToken));

            AssertLoggedOut();
            AsyncNoResult result = new AsyncNoResult(callback);
            State = LoginState.LoggingIn;
            AsyncCallback callbackConnector = new AsyncCallback(ar2 =>
            {
                string connectorHandle;
                try
                {
                    connectorHandle = _client.EndGetConnectorHandle(ar2);
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"BeginGetConnectorHandle failed: {e}");
                    result.SetComplete(e);
                    throw;
                }
                Debug.Write($"connectorHandle={connectorHandle}");
                if (presenceDesired)
                {
                    Login(accessToken, connectorHandle, result, subscriptionMode);
                }
                else
                {
                    Login(accessToken, connectorHandle, result);
                }
            });
            if (uriServerRequired)
            {
                _client.BeginGetConnectorHandle(server, callbackConnector);
            }
            else
            {
                _client.BeginGetConnectorHandle(callbackConnector);
            }
            return result;
        }

        public void EndLogin(IAsyncResult result)
        {
            AsyncNoResult ar = result as AsyncNoResult;
            ar?.CheckForError();
        }

        public string GetLoginToken(TimeSpan? expiration = null)
        {
            return Client.tokenGen.GetLoginToken(this.Key.ToString(), expiration);
        }

        public string GetLoginToken(string key, TimeSpan expiration)
        {
            return Client.tokenGen.GetLoginToken(Key.Issuer, this.Key.ToString(), expiration, key);
        }

        private void Login(string accessToken, string connectorHandle, AsyncNoResult ar, SubscriptionMode? mode = null)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            vx_req_account_anonymous_login_t request = new vx_req_account_anonymous_login_t
            {
                account_handle = _accountHandle,
                connector_handle = connectorHandle,
                enable_buddies_and_presence = mode == null ? 0 : 1,
                acct_name = Key.AccountName,
                displayname = Key.DisplayName,
                languages = string.Join(",", Key.SpokenLanguages),
                access_token = accessToken,
                participant_property_frequency = (int)_participantPropertyFrequency
            };

            if (mode != null)
            {
                request.buddy_management_mode = (vx_buddy_management_mode)mode.Value;
            }
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    ar.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
        }

        public void Logout()
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            if (_state == LoginState.LoggedIn || _state == LoginState.LoggingIn)
            {
                State = LoginState.LoggingOut;
                var request = new vx_req_account_logout_t();
                request.account_handle = _accountHandle;
                VxClient.Instance.BeginIssueRequest(request, null);
            }
        }

        public async Task LogoutAsync()
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            if (_state == LoginState.LoggedIn || _state == LoginState.LoggingIn)
            {
                State = LoginState.LoggingOut;
                var request = new vx_req_account_logout_t();
                request.account_handle = _accountHandle;
                await Task.Factory.FromAsync(
                    VxClient.Instance.BeginIssueRequest(request, null),
                    ar =>
                    {
                        try
                        {
                            VxClient.Instance.EndIssueRequest(ar);
                        }
                        catch (Exception e)
                        {
                            VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                            throw;
                        }
                        return Task.CompletedTask;
                    });
            }
        }

        public IChannelSession GetChannelSession(ChannelId channelId)
        {
            if (ChannelId.IsNullOrEmpty(channelId)) throw new ArgumentNullException(nameof(channelId));
            AssertLoggedIn();
            if (_channelSessions.ContainsKey(channelId))
            {
                return _channelSessions[channelId];
            }
            var c = new ChannelSession(this, channelId, _groupHandle);
            _channelSessions[channelId] = c;
            return c;
        }

        public void DeleteChannelSession(ChannelId channelId)
        {
            if (_channelSessions.ContainsKey(channelId))
            {
                if (_channelSessions[channelId].ChannelState == ConnectionState.Disconnected)
                {
                    (_channelSessions[channelId] as ChannelSession)?.Cleanup();
                    _channelSessions.Remove(channelId);
                    return;
                }
                WaitDeleteChannelSession(channelId);
            }
        }

        public async Task DeleteChannelSessionAsync(ChannelId channelId)
        {
            if (_channelSessions.ContainsKey(channelId))
            {
                if (_channelSessions[channelId].ChannelState == ConnectionState.Connected)
                {
                    // If it's not yet disconnected, disconnect it first.
                    await WaitDeleteChannelSessionAsync(channelId);
                }
                else if (_channelSessions[channelId].ChannelState == ConnectionState.Disconnected)
                {
                    (_channelSessions[channelId] as ChannelSession)?.Cleanup();
                    _channelSessions.Remove(channelId);
                }
            }
        }

        public void StartAudioInjection(string audioFilePath)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }
            if (!ChannelSessions.Any((cs) => cs.AudioState == ConnectionState.Connected))
            {
                throw new InvalidOperationException($"{GetType().Name}: StartAudioInjection() failed for InvalidState: The channel's AudioState must be connected");
            }
            vx_req_sessiongroup_control_audio_injection_t request;

            request = new vx_req_sessiongroup_control_audio_injection_t
            {
                sessiongroup_handle = _groupHandle,
                filename = audioFilePath,
                audio_injection_control_type = vx_sessiongroup_audio_injection_control_type.vx_sessiongroup_audio_injection_control_start
            };

            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    IsInjectingAudio = true;
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    throw;
                }
            });
        }

        public void StopAudioInjection()
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }
            if (!ChannelSessions.Any((cs) => cs.AudioState == ConnectionState.Connected || cs.TextState == ConnectionState.Connected)
                || IsInjectingAudio == false)
            {
                VivoxLogger.LogWarning($"{GetType().Name}: StopAudioInjection() warning; No audio injection to stop");
                return;
            }
            vx_req_sessiongroup_control_audio_injection_t request;

            request = new vx_req_sessiongroup_control_audio_injection_t
            {
                sessiongroup_handle = _groupHandle,
                audio_injection_control_type = vx_sessiongroup_audio_injection_control_type.vx_sessiongroup_audio_injection_control_stop
            };

            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    IsInjectingAudio = false;
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    throw;
                }
            });
        }

        public IAsyncResult BeginAccountSetLoginProperties(ParticipantPropertyUpdateFrequency participantPropertyFrequency, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);

            var request = new vx_req_account_set_login_properties_t
            {
                account_handle = _accountHandle,
                participant_property_frequency = (int)participantPropertyFrequency
            };
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    _participantPropertyFrequency = participantPropertyFrequency;
                    ar.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        public void EndAccountSetLoginProperties(IAsyncResult result)
        {
            AssertLoggedIn();
            AsyncNoResult ar = result as AsyncNoResult;
            ar?.CheckForError();
        }

        public IAsyncResult BeginAddBlockedSubscription(AccountId userId, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);
            if (_blockedSubscriptions.Contains((userId)))
            {
                ar.SetCompletedSynchronously();
                return ar;
            }
            var request = new vx_req_account_create_block_rule_t
            {
                account_handle = _accountHandle,
                block_mask = userId.ToString(),
                presence_only = 0
            };

            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    _blockedSubscriptions.Add(userId);
                    ar.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        public void EndAddBlockedSubscription(IAsyncResult result)
        {
            AssertLoggedIn();
            (result as AsyncNoResult)?.CheckForError();
        }

        public IAsyncResult BeginRemoveBlockedSubscription(AccountId userId, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);
            if (!_blockedSubscriptions.Contains((userId)))
            {
                ar.SetCompletedSynchronously();
                return ar;
            }
            var request = new vx_req_account_delete_block_rule_t
            {
                account_handle = _accountHandle,
                block_mask = userId.ToString()
            };

            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    _blockedSubscriptions.Remove(userId);
                    ar.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        public void EndRemoveBlockedSubscription(IAsyncResult result)
        {
            AssertLoggedIn();
            (result as AsyncNoResult)?.CheckForError();
        }

        public IAsyncResult BeginSendDirectedMessage(AccountId userId, string message, MessageOptions options, AsyncCallback callback)
        {
            return BeginSendDirectedMessage(userId, options?.Language, message,
                options?.Metadata != null ? "userdata" : null,
                options?.Metadata, callback);
        }

        public IAsyncResult BeginSendDirectedMessage(AccountId userId, string language, string message, string applicationStanzaNamespace, string applicationStanzaBody, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            if (AccountId.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrEmpty(message)
                && string.IsNullOrEmpty(applicationStanzaBody)) throw new ArgumentNullException($"{nameof(message)} and {nameof(applicationStanzaBody)} cannot both be null");

            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);

            var request = new vx_req_account_send_message_t
            {
                account_handle = _accountHandle,
                message_body = message,
                user_uri = userId.ToString(),
                language = language,
                application_stanza_namespace = applicationStanzaNamespace,
                application_stanza_body = applicationStanzaBody
            };

            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                vx_resp_account_send_message_t response;
                try
                {
                    response = VxClient.Instance.EndIssueRequest(result);
                    _directedMessageResult = new DirectedMessageResult(response.request_id);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DirectedMessageResult)));
                    ar.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        public void EndSendDirectedMessage(IAsyncResult result)
        {
            AssertLoggedIn();
            Console.WriteLine("Finishing message: " + DateTime.UtcNow);
            lastMessageTime = DateTime.UtcNow;
            Console.WriteLine(lastMessageTime.ToString());
            (result as AsyncNoResult)?.CheckForError();
        }

        public IAsyncResult BeginAccountArchiveQuery(DateTime? timeStart, DateTime? timeEnd, string searchText,
            AccountId userId, ChannelId channel, uint max, string afterId, string beforeId, int firstMessageIndex,
            AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertLoggedIn();
            if (userId != null && channel != null)
                throw new ArgumentException($"{GetType().Name}: Parameters {nameof(userId)} and {nameof(channel)} cannot be used at the same time");
            if (afterId != null && beforeId != null)
                throw new ArgumentException($"{GetType().Name}: Parameters {nameof(afterId)} and {nameof(beforeId)} cannot be used at the same time");
            if (max > 50)
                throw new ArgumentException($"{GetType().Name}: {nameof(max)} cannot be greater than 50");

            var ar = new AsyncNoResult(callback);

            var request = new vx_req_account_archive_query_t
            {
                account_handle = _accountHandle,
                max = max,
                after_id = afterId,
                before_id = beforeId,
                first_message_index = firstMessageIndex
            };

            if (timeStart != null && timeStart != DateTime.MinValue)
            {
                request.time_start = timeStart?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            }
            if (timeEnd != null && timeEnd != DateTime.MaxValue)
            {
                request.time_end = timeEnd?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            }
            request.search_text = searchText;
            if (userId != null)
            {
                request.participant_uri = userId.ToString();
            }
            else if (channel != null)
            {
                request.participant_uri = channel.ToString();
            }

            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                vx_resp_account_archive_query_t response;
                try
                {
                    response = VxClient.Instance.EndIssueRequest(result);
                    _accountArchiveResult = new ArchiveQueryResult(response.query_id);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccountArchiveResult)));
                    ar.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        public void EndAccountArchiveQuery(IAsyncResult result)
        {
            AssertLoggedIn();
            (result as AsyncNoResult)?.CheckForError();
        }

        public async Task SetMessageAsReadAsync(VivoxMessage message, DateTime? seenAt = null)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            AssertLoggedIn();
            var request = new vx_req_account_chat_history_set_marker_t()
            {
                account_handle = _accountHandle,
                message_id = message.MessageId,
                with_uri = string.IsNullOrEmpty(message.ChannelName) ? message.SenderURI : message.ChannelURI,
                seen_at = seenAt != null ? seenAt.Value.ToUtcUnixTimeInSeconds() : DateTime.UtcNow.ToUtcUnixTimeInSeconds()
            };

            var taskResult = new TaskCompletionSource<bool>();
            try
            {
                await Task.Factory.FromAsync(VxClient.Instance.BeginIssueRequest(request, null),
                    result =>
                    {
                        try
                        {
                            vx_resp_account_chat_history_set_marker_t response = VxClient.Instance.EndIssueRequest(result);
                            message.IsRead = true;
                            taskResult.SetResult(true);
                        }
                        catch (Exception e)
                        {
                            VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                            taskResult.TrySetException(e);
                            throw;
                        }
                    });
                await taskResult.Task;
            }
            catch (Exception e)
            {
                VivoxLogger.LogVxException($"{GetType().Name} failed: {e}");
                throw;
            }
        }

        private async Task<IList<VivoxMessage>> GetDirectTextMessageHistoryPageAsync(AccountId recipient = null, int pageSize = 10, ChatHistoryQueryOptions chatHistoryQueryOptions = null, AsyncCallback callback = null)
        {
            string queryId = null;
            try
            {
                queryId = await Task.Factory.FromAsync(
                    BeginChatHistoryQuery(recipient, pageSize, chatHistoryQueryOptions, callback),
                    (ar) =>
                    {
                        try
                        {
                            EndChatHistoryQuery(ar);
                            var asyncResult = ar as AsyncResult<string>;
                            queryId = asyncResult?.Result;
                            return queryId;
                        }
                        catch (Exception e)
                        {
                            VivoxLogger.LogVxException($"{ar.GetType().Name} failed: {e}");
                            throw;
                        }
                    });
                // By this point, we should have a completion source setup up that we can await after receiving a response from Core.
                // Let's find it and wait until we receive the vx_event_type.evt_session_archive_query_end that marks the completion of the query.
                using (var cts = new CancellationTokenSource(Client.historyQueryRequestTimeout))
                {
                    var taskToComplete = _chatHistoryTaskResults.FirstOrDefault(r => r.Key == queryId).Value;
                    cts.Token.Register(() =>
                    {
                        taskToComplete.TrySetException(exception: new TimeoutException($"[Vivox] Account message history query failed to complete in a reasonable amount of time. canceling the task."));
                    });
                    await taskToComplete.Task;
                    return taskToComplete.Task.Result.VivoxMessages;
                }
            }
            catch (Exception e)
            {
                VivoxLogger.LogVxException($"{GetType().Name} failed: {e}");
                throw;
            }
            finally
            {
                // Cleanup completed task from list
                if (queryId != null)
                {
                    _chatHistoryTaskResults.TryRemove(queryId, out _);
                }
            }
        }

        public async Task<ReadOnlyCollection<VivoxMessage>> GetDirectTextMessageHistoryAsync(AccountId recipient, int requestSize, ChatHistoryQueryOptions chatHistoryQueryOptions = null, AsyncCallback callback = null)
        {
            if (requestSize <= 0)
            {
                throw new ArgumentOutOfRangeException($"{GetType().Name}: must be larger than 0", nameof(requestSize));
            }
            var pageSize = 10;
            var requestsNeeded = Helper.NumberOfPages(requestSize, pageSize);
            var pageQueue = new Queue<int>(Enumerable.Range(0, requestsNeeded));
            var remainingItemCount = requestSize;

            chatHistoryQueryOptions = chatHistoryQueryOptions ?? new ChatHistoryQueryOptions();
            var queryTimeEnd = chatHistoryQueryOptions.TimeEnd;
            var allVivoxMessages = new List<VivoxMessage>();
            try
            {
                // Below to iterate over estimated pages of query's.
                // If we receive unexpected end then the foreach will return and no longer query the backend as we reached the end of chat history
                foreach (var page in pageQueue)
                {
                    var itemCount = (remainingItemCount >= pageSize) ? pageSize : remainingItemCount;
                    chatHistoryQueryOptions.TimeEnd = queryTimeEnd;
                    var pageResults = await GetDirectTextMessageHistoryPageAsync(recipient, itemCount, chatHistoryQueryOptions, callback);
                    allVivoxMessages.InsertRange(0, pageResults);
                    queryTimeEnd = pageResults.FirstOrDefault()?.ReceivedTime;
                    remainingItemCount -= pageResults.Count;
                    // If we receive fewer items than the expected we reached the end of chat history and should return what we have
                    if (pageResults.Count != itemCount)
                    {
                        // Get out of the loop because we are at the end of chat history
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                VivoxLogger.LogVxException($"{GetType().Name} failed: {e}");
                throw;
            }

            return new ReadOnlyCollection<VivoxMessage>(allVivoxMessages);
        }

        public async Task<ReadOnlyCollection<VivoxConversation>> GetConversationsAsync(ConversationQueryOptions queryOptions = null)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }

            AssertLoggedIn();
            queryOptions = queryOptions ?? new ConversationQueryOptions();

            var request = new vx_req_account_get_conversations_t
            {
                account_handle = _accountHandle,
                request_time = queryOptions.CutoffTime?.ToUtcUnixTimeInSeconds() ?? -1,
                page_size = queryOptions.PageSize.Clamp(1, 50),
                cursor = Math.Max(queryOptions.PageCursor, 0)
            };

            var conversations = new List<VivoxConversation>();
            var taskResult = new TaskCompletionSource<ReadOnlyCollection<VivoxConversation>>();
            try
            {
                using (var cts = new CancellationTokenSource(Client.requestTimeout))
                {
                    cts.Token.Register(() =>
                    {
                        taskResult.TrySetException(exception: new TimeoutException($"[Vivox] Failed to retrieve conversations for user: {LoginSessionId}"));
                    });
                    await Task.Factory.FromAsync(VxClient.Instance.BeginIssueRequest(request, null),
                        result =>
                        {
                            try
                            {
                                vx_resp_account_get_conversations_t response = VxClient.Instance.EndIssueRequest(result);
                                for (var i = 0; i < response.conversations_size; i++)
                                {
                                    var conversation = response.GetConversation(i);
                                    var conversationType = (ConversationType)conversation.type;
                                    string conversationId = conversationType == ConversationType.ChannelConversation ? new ChannelId("sip:" + conversation.name).Name : new AccountId("sip:" + conversation.name).Name;
                                    conversations.Add(new VivoxConversation(conversationId, conversationType));
                                }
                                taskResult.TrySetResult(conversations.AsReadOnly());
                            }
                            catch (Exception e)
                            {
                                VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                                taskResult.TrySetException(e);
                                throw;
                            }
                        });
                    await taskResult.Task;
                    return taskResult.Task.Result;
                }
            }
            catch (Exception e)
            {
                VivoxLogger.LogVxException($"{GetType().Name} failed: {e}");
                throw;
            }
        }

        public IAsyncResult BeginAddAllowedSubscription(AccountId userId, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);

            if (_allowedSubscriptions.Contains((userId)))
            {
                ar.SetCompletedSynchronously();
                return ar;
            }
            if (_incomingSubscriptionRequests.Contains(userId))
            {
                var request = new vx_req_account_send_subscription_reply_t();
                request.account_handle = _accountHandle;
                request.buddy_uri = userId.ToString();
                request.rule_type = vx_rule_type.rule_allow;
                VxClient.Instance.BeginIssueRequest(request, result =>
                {
                    try
                    {
                        VxClient.Instance.EndIssueRequest(result);
                        _incomingSubscriptionRequests.RemoveAll(userId);
                        ar.SetComplete();
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                        ar.SetComplete(e);
                        throw;
                    }
                });
                return ar;
            }
            else
            {
                var request = new vx_req_account_create_auto_accept_rule_t
                {
                    account_handle = _accountHandle,
                    auto_accept_mask = userId.ToString()
                };
                VxClient.Instance.BeginIssueRequest(request, result =>
                {
                    try
                    {
                        VxClient.Instance.EndIssueRequest(result);
                        _allowedSubscriptions.Add(userId);
                        ar.SetComplete();
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                        ar.SetComplete(e);
                        throw;
                    }
                });
                return ar;
            }
        }

        public void EndAddAllowedSubscription(IAsyncResult result)
        {
            AssertLoggedIn();
            (result as AsyncNoResult)?.CheckForError();
        }

        public IAsyncResult BeginRemoveAllowedSubscription(AccountId userId, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }

            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);

            if (!_allowedSubscriptions.Contains((userId)))
            {
                ar.SetCompletedSynchronously();
                return ar;
            }
            var request = new vx_req_account_delete_auto_accept_rule_t
            {
                account_handle = _accountHandle,
                auto_accept_mask = userId.ToString()
            };
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    _allowedSubscriptions.Remove(userId);
                    ar.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        public void EndRemoveAllowedSubscription(IAsyncResult result)
        {
            AssertLoggedIn();
            (result as AsyncNoResult)?.CheckForError();
        }

        public IAsyncResult BeginAddPresenceSubscription(AccountId userId, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }

            AssertLoggedIn();
            var ar = new AsyncResult<IPresenceSubscription>(callback);

            if (_presenceSubscriptions.ContainsKey((userId)))
            {
                ar.SetCompletedSynchronously(_presenceSubscriptions[userId]);
                return ar;
            }
            var request = new vx_req_account_buddy_set_t
            {
                account_handle = _accountHandle,
                buddy_uri = userId.ToString()
            };
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    _presenceSubscriptions[userId] = new PresenceSubscription
                    {
                        Key = userId
                    };
                    ar.SetComplete(_presenceSubscriptions[userId]);
                    BeginRemoveBlockedSubscription(userId, ar2 =>
                    {
                        try
                        {
                            EndRemoveBlockedSubscription(ar2);
                        }
                        catch (Exception e)
                        {
                            VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                            ar.SetComplete(e);
                            throw;
                        }
                        BeginAddAllowedSubscription(userId, ar3 =>
                        {
                            try
                            {
                                EndAddAllowedSubscription(ar3);
                            }
                            catch (Exception e)
                            {
                                VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                                ar.SetComplete(e);
                                throw;
                            }
                        });
                    });
                }
                catch (Exception e)
                {
                    VivoxLogger.LogError($"{GetType().Name}: {request.GetType().Name} failed {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        public IPresenceSubscription EndAddPresenceSubscription(IAsyncResult result)
        {
            AssertLoggedIn();
            return (result as AsyncResult<IPresenceSubscription>)?.Result;
        }

        public IAsyncResult BeginRemovePresenceSubscription(AccountId userId, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }

            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);

            if (!_presenceSubscriptions.ContainsKey((userId)))
            {
                ar.SetCompletedSynchronously();
                return ar;
            }
            var request = new vx_req_account_buddy_delete_t();
            request.account_handle = _accountHandle;
            request.buddy_uri = userId.ToString();
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    _presenceSubscriptions.Remove(userId);
                    ar.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        public void EndRemovePresenceSubscription(IAsyncResult result)
        {
            AssertLoggedIn();
            (result as AsyncNoResult)?.CheckForError();
        }

        public async Task BlockPlayerAsync(AccountId accountId, bool blockStatus, AsyncCallback callback = null)
        {
            AssertLoggedIn();

            await Task.Factory.FromAsync(
                SetCrossMutedCommunications(accountId, blockStatus, callback), ar =>
                {
                    return Task.CompletedTask;
                });
        }

        public IAsyncResult SetCrossMutedCommunications(AccountId accountId, bool muted, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);

            var operation = muted ? vx_control_communications_operation.vx_control_communications_operation_block : vx_control_communications_operation.vx_control_communications_operation_unblock;
            SendCrossMuteOperationRequest(operation, accountId.ToString(), vx_mute_scope.mute_scope_all,
                (resp) =>
                {
                    string blockedAccount = resp.blocked_uris;
                    AccountId currentlyBlocking = new AccountId(blockedAccount);
                    if (currentlyBlocking.ToString() == accountId.ToString() && !_crossMutedCommunications.Contains(accountId) && muted)
                    {
                        _crossMutedCommunications.Add(accountId);
                    }
                    else if (currentlyBlocking.ToString() == accountId.ToString() && _crossMutedCommunications.Contains(accountId) && !muted)
                    {
                        _crossMutedCommunications.Remove(accountId);
                    }

                    ar.SetComplete();
                });

            return ar;
        }

        public IAsyncResult SetCrossMutedCommunications(List<AccountId> accountIdSet, bool muted, AsyncCallback callback)
        {
            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);

            var operation = muted ? vx_control_communications_operation.vx_control_communications_operation_block : vx_control_communications_operation.vx_control_communications_operation_unblock;
            SendCrossMuteOperationRequest(operation, accountIdSet, vx_mute_scope.mute_scope_all,
                (resp) =>
                {
                    string changedAccounts = resp.blocked_uris;
                    List<AccountId> changedAccountIds = new List<AccountId>();
                    var seperatedAccounts = changedAccounts.Split('\n');
                    foreach (var account in seperatedAccounts)
                    {
                        changedAccountIds.Add(new AccountId(account.Trim()));
                    }

                    foreach (var accountId in changedAccountIds)
                    {
                        if (!_crossMutedCommunications.Contains(accountId) && muted)
                        {
                            _crossMutedCommunications.Add(accountId);
                        }
                        else if (_crossMutedCommunications.Contains(accountId) && !muted)
                        {
                            _crossMutedCommunications.Remove(accountId);
                        }
                    }

                    ar.SetComplete();
                });

            return ar;
        }

        public IAsyncResult ClearCrossMutedCommunications(AsyncCallback callback)
        {
            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);

            SendCrossMuteOperationRequest(vx_control_communications_operation.vx_control_communications_operation_clear, "", vx_mute_scope.mute_scope_all,
                (resp) =>
                {
                    _crossMutedCommunications.Clear();

                    ar.SetComplete();
                });

            return ar;
        }

        private IAsyncResult BeginDeleteTextMessage(string messageId, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }

            if (string.IsNullOrEmpty(messageId)) throw new ArgumentNullException($"{nameof(messageId)} cannot be null");

            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);

            var request = new vx_req_account_delete_message_t()
            {
                account_handle = _accountHandle,
                message_id = messageId
            };

            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    ar.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        private IAsyncResult BeginEditTextMessage(string messageId, string newText, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }

            if (string.IsNullOrEmpty(messageId)) throw new ArgumentNullException($"{nameof(messageId)} cannot be null");

            AssertLoggedIn();
            AsyncNoResult ar = new AsyncNoResult(callback);

            var request = new vx_req_account_edit_message_t()
            {
                account_handle = _accountHandle,
                message_id = messageId,
                new_message = newText
                    //REVISIT: Add language language =
            };

            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    ar.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
                }
            });
            return ar;
        }

        public async Task EditDirectTextMessageAsync(string messageId, string newMessage)
        {
            if (string.IsNullOrEmpty(messageId)) throw new ArgumentNullException($"{nameof(messageId)} cannot be null");

            await Task.Factory.FromAsync(BeginEditTextMessage(messageId, newMessage, null), (ar) =>
            {
                try
                {
                    AsyncNoResult result = ar as AsyncNoResult;
                    result?.CheckForError();
                    return Task.CompletedTask;
                }
                catch (Exception)
                {
                    throw;
                }
            });
        }

        public async Task DeleteDirectTextMessageAsync(string messageId)
        {
            if (string.IsNullOrEmpty(messageId)) throw new ArgumentNullException($"{nameof(messageId)} cannot be null");

            await Task.Factory.FromAsync(BeginDeleteTextMessage(messageId, null), (ar) =>
            {
                try
                {
                    AsyncNoResult result = ar as AsyncNoResult;
                    result?.CheckForError();
                    return Task.CompletedTask;
                }
                catch (Exception)
                {
                    throw;
                }
            });
        }

        private void SendCrossMuteOperationRequest(vx_control_communications_operation controlOp, string userURIs, vx_mute_scope muteScope, Action<vx_resp_account_control_communications_t> callback = null)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            var request = new vx_req_account_control_communications_t
            {
                account_handle = _accountHandle,
                operation = controlOp,
                user_uris = userURIs,
                scope = muteScope
            };

            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    var response = VxClient.Instance.EndIssueRequest(result);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CrossMutedCommunications)));
                    callback?.Invoke(response);
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    throw;
                }
            });
        }

        private void SendCrossMuteOperationRequest(vx_control_communications_operation controlOp, List<AccountId> users, vx_mute_scope muteScope, Action<vx_resp_account_control_communications_t> callback = null)
        {
            System.Text.StringBuilder formattedList = new System.Text.StringBuilder();
            foreach (var accountId in users)
            {
                if (_crossMutedCommunications.Contains(accountId) && controlOp == vx_control_communications_operation.vx_control_communications_operation_block)
                {
                    continue;
                }
                else if (!_crossMutedCommunications.Contains(accountId) && controlOp == vx_control_communications_operation.vx_control_communications_operation_unblock)
                {
                    continue;
                }
                formattedList.Append(accountId.ToString() + "\n");
            }

            SendCrossMuteOperationRequest(controlOp, formattedList.ToString(), muteScope, callback);
        }

        #endregion

        #region Helpers

        void WaitDeleteChannelSession(ChannelId channelId)
        {
            _channelSessions[channelId].Disconnect();
            _channelSessions[channelId].PropertyChanged += CheckConnectionAsync;
        }

        async Task WaitDeleteChannelSessionAsync(ChannelId channelId)
        {
            _channelSessions[channelId].PropertyChanged += CheckConnectionAsync;
            await _channelSessions[channelId].DisconnectAsync();
            ClearTransmittingChannel(channelId);
        }

        async void CheckConnectionAsync(Object sender, PropertyChangedEventArgs args)
        {
            IChannelSession session = (IChannelSession)sender;

            if (_channelSessions[session.Channel].ChannelState == ConnectionState.Disconnected)
            {
                _channelSessions[session.Channel].PropertyChanged -= CheckConnectionAsync;
                await DeleteChannelSessionAsync(session.Channel);
            }
        }

        void AssertLoggedIn()
        {
            if (_state != LoginState.LoggedIn)
                throw new InvalidOperationException($"{GetType().Name}: Invalid State - must be logged in to perform this operation.");
        }

        void AssertLoggedOut()
        {
            if (_state != LoginState.LoggedOut)
                throw new InvalidOperationException($"{GetType().Name}: Invalid State - must be logged out to perform this operation.");
        }

        void AssertNotQuitting()
        {
            if (VxClient.Instance.IsQuitting)
                throw new InvalidOperationException($"{GetType().Name}: Invalid State - Vivox must not be quitting when this operation is run.");
        }

        #endregion

        internal void ClearTransmittingChannel(ChannelId channelId)
        {
            if (_transmittingChannel == null)
                return;
            if (_transmittingChannel.Equals(channelId))
            {
                _transmittingChannel = null;
                _transmissionType = TransmissionMode.None;
            }
        }

        private void Cleanup()
        {
            _channelSessions.Clear();
            _transmittingChannel = null;
            _presenceSubscriptions.Clear();
            _allowedSubscriptions.Clear();
            _blockedSubscriptions.Clear();
            _incomingSubscriptionRequests.Clear();
            _directedMessages.Clear();
            _failedDirectedMessages.Clear();
            _accountArchive.Clear();
            ClearAllCurrentTextQueries();
            VxClient.Instance.EventMessageReceived -= Instance_EventMessageReceived;
        }

        private void ClearAllCurrentTextQueries()
        {
            foreach (var textChatHistoryResult in _chatHistoryTaskResults)
            {
                textChatHistoryResult.Value?.TrySetCanceled();
            }
            _chatHistoryTaskResults.Clear();
            _internalChatHistoryResults.Clear();
        }

        public void SetTransmissionMode(TransmissionMode mode, ChannelId singleChannel = null)
        {
            if (mode == TransmissionMode.Single && singleChannel == null)
            {
                throw new ArgumentException("Setting parameter 'mode' to TransmissionsMode.Single expects a ChannelId for the 'singleChannel' parameter");
            }

            _transmissionType = mode;
            _transmittingChannel = mode == TransmissionMode.Single ? singleChannel : null;

            bool sessionGroupExists = false;
            foreach (var session in _channelSessions)
            {
                if (session.AudioState != ConnectionState.Disconnected || session.TextState != ConnectionState.Disconnected)
                {
                    sessionGroupExists = true;
                    break;
                }
            }
            if (sessionGroupExists && (_transmissionType != TransmissionMode.Single || ChannelSessions.ContainsKey(_transmittingChannel)))
            {
                SetTransmission();
            }
        }

        public async Task SetTransmissionModeAsync(TransmissionMode mode, ChannelId singleChannel = null)
        {
            if (mode == TransmissionMode.Single && singleChannel == null)
            {
                throw new ArgumentException("Setting parameter 'mode' to TransmissionsMode.Single expects a ChannelId for the 'singleChannel' parameter");
            }

            _transmissionType = mode;
            _transmittingChannel = mode == TransmissionMode.Single ? singleChannel : null;

            bool sessionGroupExists = false;
            foreach (var session in _channelSessions)
            {
                if (session.AudioState != ConnectionState.Disconnected || session.TextState != ConnectionState.Disconnected)
                {
                    sessionGroupExists = true;
                    break;
                }
            }
            if (sessionGroupExists && (_transmissionType != TransmissionMode.Single || ChannelSessions.ContainsKey(_transmittingChannel)))
            {
                await SetTransmissionAsync();
            }
        }

        public void SetTransmission()
        {
            switch (_transmissionType)
            {
                case TransmissionMode.None:
                {
                    SetNoSessionTransmitting();
                    break;
                }
                case TransmissionMode.Single:
                {
                    SetTransmitting(_transmittingChannel);
                    break;
                }
                case TransmissionMode.All:
                {
                    SetAllSessionsTransmitting();
                    break;
                }
                default:
                    break;
            }
        }

        public async Task SetTransmissionAsync()
        {
            switch (_transmissionType)
            {
                case TransmissionMode.None:
                {
                    await SetNoSessionTransmittingAsync();
                    break;
                }
                case TransmissionMode.Single:
                {
                    await SetTransmittingAsync(_transmittingChannel);
                    break;
                }
                case TransmissionMode.All:
                {
                    await SetAllSessionsTransmittingAsync();
                    break;
                }
                default:
                    break;
            }
        }

        private async Task SetTransmittingAsync(ChannelId channel)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            var request = new vx_req_sessiongroup_set_tx_session_t();
            request.session_handle = ChannelSessions[channel].SessionHandle;
            _transmittingChannel = channel;

            await Task.Factory.FromAsync(
                VxClient.Instance.BeginIssueRequest(request, null),
                ar =>
                {
                    try
                    {
                        VxClient.Instance.EndIssueRequest(ar);
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                        throw;
                    }
                });
        }

        private async Task SetNoSessionTransmittingAsync()
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            var request = new vx_req_sessiongroup_set_tx_no_session_t();
            request.sessiongroup_handle = _groupHandle;
            _transmittingChannel = null;

            await Task.Factory.FromAsync(
                VxClient.Instance.BeginIssueRequest(request, null),
                result =>
                {
                    try
                    {
                        VxClient.Instance.EndIssueRequest(result);
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                        throw;
                    }
                });
        }

        private async Task SetAllSessionsTransmittingAsync()
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            var request = new vx_req_sessiongroup_set_tx_all_sessions_t();
            request.sessiongroup_handle = _groupHandle;

            await Task.Factory.FromAsync(
                VxClient.Instance.BeginIssueRequest(request, null),
                result =>
                {
                    try
                    {
                        VxClient.Instance.EndIssueRequest(result);
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                        throw;
                    }
                });
        }

        private void SetTransmitting(ChannelId channel)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            var request = new vx_req_sessiongroup_set_tx_session_t();
            request.session_handle = ChannelSessions[channel].SessionHandle;
            _transmittingChannel = channel;
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    throw;
                }
            });
        }

        private void SetNoSessionTransmitting()
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            var request = new vx_req_sessiongroup_set_tx_no_session_t();
            request.sessiongroup_handle = _groupHandle;
            _transmittingChannel = null;
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    throw;
                }
            });
        }

        private void SetAllSessionsTransmitting()
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            var request = new vx_req_sessiongroup_set_tx_all_sessions_t();
            request.sessiongroup_handle = _groupHandle;
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    throw;
                }
            });
        }

        public async Task SetAutoVADAsync(bool enabled)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            if (_state != LoginState.LoggedIn)
                return;

            var request = new vx_req_aux_set_vad_properties_t()
            {
                account_handle = _accountHandle,
                vad_auto = Convert.ToInt32(enabled)
            };

            await Task.Factory.FromAsync(
                VxClient.Instance.BeginIssueRequest(request, null),
                ar =>
                {
                    try
                    {
                        VxClient.Instance.EndIssueRequest(ar);
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                        throw;
                    }
                });
        }

        public async Task SetVADPropertiesAsync(int hangover = 2000, int noiseFloor = 576, int sensitivity = 43)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            if (_state != LoginState.LoggedIn)
                return;

            var request = new vx_req_aux_set_vad_properties_t()
            {
                account_handle = _accountHandle,
                vad_hangover = hangover,
                vad_noise_floor = noiseFloor,
                vad_sensitivity = sensitivity
            };

            await Task.Factory.FromAsync(
                VxClient.Instance.BeginIssueRequest(request, null),
                ar =>
                {
                    try
                    {
                        VxClient.Instance.EndIssueRequest(ar);
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                        throw;
                    }
                });
        }

        public async Task<bool> SetSafeVoiceConsentStatus(string environmentId, string projectId, string playerId, string authToken, bool consentToSet)
        {
            AssertNotQuitting();
            AssertLoggedIn();

            var request = new vx_req_account_safe_voice_update_consent_t
            {
                unity_environment_id = environmentId,
                unity_project_id = projectId,
                player_id = playerId,
                unity_authentication_token = authToken,
                consent_status = consentToSet,
                account_handle = AccountHandle
            };
            var response = new vx_resp_account_safe_voice_update_consent_t();

            await Task.Factory.FromAsync(
                VxClient.Instance.BeginIssueRequest(request, null),
                ar =>
                {
                    try
                    {
                        response = VxClient.Instance.EndIssueRequest(ar);
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                        throw;
                    }
                });
            return response.consent_status;
        }

        public async Task<bool> GetSafeVoiceConsentStatus(string environmentId, string projectId, string playerId, string authToken)
        {
            AssertNotQuitting();
            AssertLoggedIn();

            var request = new vx_req_account_safe_voice_get_consent_t
            {
                unity_environment_id = environmentId,
                unity_project_id = projectId,
                player_id = playerId,
                unity_authentication_token = authToken,
                account_handle = AccountHandle
            };
            var response = new vx_resp_account_safe_voice_get_consent_t();

            await Task.Factory.FromAsync(
                VxClient.Instance.BeginIssueRequest(request, null),
                ar =>
                {
                    try
                    {
                        response = VxClient.Instance.EndIssueRequest(ar);
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                        throw;
                    }
                });
            return response.consent_status;
        }
    }
}
