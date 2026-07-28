#if UNITY_WEBGL && !UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AOT;
using UnityEngine;

namespace Unity.Services.Vivox
{
    internal class LoginSessionWeb : ILoginSession
    {
#region Webextern LoginSession

        private delegate void PointerCallback(IntPtr ptr);
        private delegate void IntCallback(int newValue);
        private delegate void DirectedMessageCallback(JSONDirectedMessage message);

        [DllImport("__Internal")]
        private static extern int vx_initiateLoginSession(string userId, string realm, string issuer,
            string environmentId, int incomingSub, int isSecurePage, string loginToken);

        [DllImport("__Internal")]
        private static extern void vx_setupLoginSessionCallbacks(IntCallback OnLoginStateChange,
            PointerCallback onDirectedTextMessage);

        [DllImport("__Internal")]
        private static extern int vx_terminateLoginSession();

        [DllImport("__Internal")]
        private static extern void vx_setLocalCapture(int isTransmitting);

        [DllImport("__Internal")]
        private static extern int vx_sendDirectedTextMessage(string destination_account, string message, string language,
            string applicationStanzaNamespace, string applicationStanzaBody);

        private static event IntCallback OnStateChange;
        private static event DirectedMessageCallback OnDirectedTextMessage;

#endregion

        //Just for the time being to get web out the door
        private readonly Dictionary<int, IAsyncResult> actionResults = new Dictionary<int, IAsyncResult>();


#region Member Variables

        private readonly string _accountHandle;
        private readonly string _groupHandle;
        private LoginState _state = LoginState.LoggedOut;
        private TransmissionMode _transmissionType = TransmissionMode.None;
        private ChannelId _transmittingChannel;
        private bool _isInjectingAudio = false;

        private readonly ReadWriteDictionary<ChannelId, IChannelSession, ChannelSessionWeb> _channelSessions =
            new ReadWriteDictionary<ChannelId, IChannelSession, ChannelSessionWeb>();

        private readonly ReadWriteHashSet<AccountId> _blockedSubscriptions = new ReadWriteHashSet<AccountId>();
        private readonly ReadWriteHashSet<AccountId> _allowedSubscriptions = new ReadWriteHashSet<AccountId>();

        private readonly ReadWriteDictionary<AccountId, IPresenceSubscription, PresenceSubscription>
            _presenceSubscriptions
                = new ReadWriteDictionary<AccountId, IPresenceSubscription, PresenceSubscription>();

        private Presence _presence;
        private readonly Client _client;

        private readonly ReadWriteQueue<IDirectedTextMessage> _directedMessages =
            new ReadWriteQueue<IDirectedTextMessage>();

        private readonly ReadWriteQueue<IFailedDirectedTextMessage> _failedDirectedMessages =
            new ReadWriteQueue<IFailedDirectedTextMessage>();

        private readonly ReadWriteQueue<IAccountArchiveMessage> _accountArchive =
            new ReadWriteQueue<IAccountArchiveMessage>();

        //TODO: Currently there needs to be 2 seconds between the last message send and the account archive query, this should be fixed
        private DateTime lastMessageTime;
        private DirectedMessageResult _directedMessageResult = new DirectedMessageResult();

        private ArchiveQueryResult _accountArchiveResult = new ArchiveQueryResult();

        //
        private readonly ReadWriteQueue<AccountId> _incomingSubscriptionRequests = new ReadWriteQueue<AccountId>();
        private ChannelId _transmittingSession;

        private ParticipantPropertyUpdateFrequency _participantPropertyFrequency =
            ParticipantPropertyUpdateFrequency.StateChange;

        public IReadOnlyQueue<IDirectedTextMessage> DirectedMessages => _directedMessages;
        public IReadOnlyQueue<IFailedDirectedTextMessage> FailedDirectedMessages => _failedDirectedMessages;
        public event EventHandler<VivoxMessage> DirectedMessageEdited;
        public event EventHandler<VivoxMessage> DirectedMessageDeleted;
        public IReadOnlyQueue<IAccountArchiveMessage> AccountArchive => _accountArchive;
        public IArchiveQueryResult AccountArchiveResult => _accountArchiveResult;
        public IDirectedMessageResult DirectedMessageResult => _directedMessageResult;

        private TaskCompletionSource<bool> _disconnectTaskCompletionSource;
        private TaskCompletionSource<bool> _connectTaskCompletionSource;

        internal string AccountHandle => _accountHandle;
        public AccountId LoginSessionId => Key;

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

        public ChannelId TransmittingSession
        {
            get { return _transmittingSession; }
            set
            {
                if (value == null && _transmittingSession == null)
                    return;
                if ((value == null || _transmittingSession == null) || !value.Equals(_transmittingSession))
                {
                    _transmittingSession = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransmittingSession)));
                }
            }
        }

        public AccountId Key { get; }

        public LoginState State
        {
            get => _state;
            private set
            {
                if (_state != value)
                {
                    _state = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
                    // We never set the State property to logged out when a user is logged out correctly.
                    // When logged out unexpectedly (i.e. disconnect) we want to fire a property changed event and handle that.
                    // The only time the Vivox SDK fires an event for being logged out is when an interruption occurs so we adhere to that here too.
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


        public IReadOnlyDictionary<AccountId, IPresenceSubscription> PresenceSubscriptions => _presenceSubscriptions;
        public IReadOnlyHashSet<AccountId> BlockedSubscriptions => _blockedSubscriptions;
        public IReadOnlyHashSet<AccountId> AllowedSubscriptions => _allowedSubscriptions;
        public IReadOnlyQueue<AccountId> IncomingSubscriptionRequests => _incomingSubscriptionRequests;

        public Presence Presence
        {
            get { return _presence; }
            set
            {
                AssertLoggedIn();
                if (!Equals(_presence, value))
                {
                }
            }
        }

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

        IReadOnlyHashSet<AccountId> ILoginSession.CrossMutedCommunications => throw new NotImplementedException();

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

        ITextToSpeech ILoginSession.TTS => throw new NotImplementedException();

        ConnectionRecoveryState ILoginSession.RecoveryState => throw new NotImplementedException();

#endregion

#region Events

        public event PropertyChangedEventHandler PropertyChanged;

#endregion

#region Helpers

        void AssertLoggedIn()
        {
            if (State != LoginState.LoggedIn)
                throw new InvalidOperationException(
                    $"{GetType().Name}: Invalid State - must be logged in to perform this operation.");
        }

        void AssertLoggedOut()
        {
            if (State != LoginState.LoggedOut)
                throw new InvalidOperationException(
                    $"{GetType().Name}: Invalid State - must be logged out to perform this operation.");
        }

#endregion

        [MonoPInvokeCallback(typeof(IntCallback))]
        public static void HandleAccountLoginStateChangeEvt(int newInt)
        {
            OnStateChange?.Invoke(newInt);
        }

        [MonoPInvokeCallback(typeof(PointerCallback))]
        public static void HandleDirectedMessageEvt(IntPtr ptr)
        {
            string value = Marshal.PtrToStringAuto(ptr);
            JSONDirectedMessage jsonDirectedMessage = JsonUtility.FromJson<JSONDirectedMessage>(value);
            OnDirectedTextMessage?.Invoke(jsonDirectedMessage);
        }


        [MonoPInvokeCallback(typeof(PointerCallback))]
        public static void HandleAccountOtherChangeEvt(IntPtr ptr)
        {
            string value = Marshal.PtrToStringAuto(ptr);

            VivoxLogger.LogVerbose($"HandleAccountOtherChangeEvt is not handled but we received: {value} ");
        }

        internal LoginSessionWeb(Client client, AccountId accountId)
        {
            if (AccountId.IsNullOrEmpty(accountId)) throw new ArgumentNullException(nameof(accountId));
            if (client == null) throw new ArgumentNullException(nameof(client));
            Key = accountId;
            _accountHandle = accountId.ToString();
            _groupHandle = "sg_" + _accountHandle;
            _client = client;
            OnStateChange += HandleLoginStateChange;
            OnDirectedTextMessage += HandleDirectedMessage;

            vx_setupLoginSessionCallbacks(
                HandleAccountLoginStateChangeEvt,
                HandleDirectedMessageEvt);
        }

        private void HandleLoginStateChange(int newValue)
        {
            // Using the property, since its setter includes triggering the PropertyChanged event
            State = newValue == 1 ? LoginState.LoggedIn : LoginState.LoggedOut;
            VivoxLogger.LogVerbose($"HandleLoginStateChange -> Connect state is:{State}");
            // Complete the LoginAsync task
            if (State == LoginState.LoggedIn && _connectTaskCompletionSource != null &&
                !_connectTaskCompletionSource.Task.IsCompleted)
            {
                _connectTaskCompletionSource?.TrySetResult(true);
            }
        }

        private void HandleDirectedMessage(JSONDirectedMessage newMessage)
        {
            VivoxLogger.LogVerbose($"HandleDirectedMessage {newMessage.Message}");
            var message = new DirectedTextMessage(this, newMessage);

            Debug.Assert(message != null);
            _directedMessages.Enqueue(message);
        }

        public IAsyncResult BeginLogin(
            Uri server,
            string accessToken,
            SubscriptionMode subscriptionMode,
            IReadOnlyHashSet<AccountId> presenceSubscriptions,
            IReadOnlyHashSet<AccountId> blockedPresenceSubscriptions,
            IReadOnlyHashSet<AccountId> allowedPresenceSubscriptions,
            AsyncCallback callback)
        {
            return BeginLogin(accessToken, subscriptionMode, presenceSubscriptions, blockedPresenceSubscriptions,
                allowedPresenceSubscriptions, callback);
        }

        public IAsyncResult BeginLogin(Uri server, string accessToken, AsyncCallback callback)
        {
            return BeginLogin(server, accessToken, SubscriptionMode.Accept, null, null, null, callback);
        }

        public IAsyncResult BeginLogin(string accessToken, SubscriptionMode subscriptionMode,
            IReadOnlyHashSet<AccountId> presenceSubscriptions, IReadOnlyHashSet<AccountId> blockedPresenceSubscriptions,
            IReadOnlyHashSet<AccountId> allowedPresenceSubscriptions, AsyncCallback callback)
        {
            if (string.IsNullOrEmpty(accessToken)) throw new ArgumentNullException(nameof(accessToken));

            AssertLoggedOut();
            AsyncNoResult result = new AsyncNoResult(callback);
            State = LoginState.LoggingIn;

            var requestResult =
                vx_initiateLoginSession(Key.Name, Key.Domain, Key.Issuer, Key.EnvironmentId, 2, 1, accessToken);

            return result;
        }

        public IChannelSession GetChannelSession(ChannelId channelId)
        {
            if (ChannelId.IsNullOrEmpty(channelId)) throw new ArgumentNullException(nameof(channelId));
            AssertLoggedIn();
            if (_channelSessions.ContainsKey(channelId))
            {
                return _channelSessions[channelId];
            }

            var c = new ChannelSessionWeb(this, channelId, _groupHandle);
            _channelSessions[channelId] = c;
            return c;
        }

        public void DeleteChannelSession(ChannelId channelId)
        {
            if (_channelSessions.ContainsKey(channelId))
            {
                if (_channelSessions[channelId].ChannelState == ConnectionState.Disconnected)
                {
                    (_channelSessions[channelId] as ChannelSessionWeb)?.Disconnect();
                    _channelSessions.Remove(channelId);
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
                    await _channelSessions[channelId].DisconnectAsync();
                }

                if (_channelSessions[channelId].ChannelState == ConnectionState.Disconnected)
                {
                    (_channelSessions[channelId] as ChannelSessionWeb)?.Cleanup();
                    _channelSessions.Remove(channelId);
                }
            }
        }

        private void WaitDeleteChannelSession(ChannelId channelId)
        {
            // _channelSessions[channelId].PropertyChanged += CheckConnectionAsync;
            _channelSessions[channelId].Disconnect();
        }

        public string GetLoginToken(TimeSpan? expiration = null)
        {
            return Client.tokenGen.GetLoginToken(this.Key.ToString(), expiration);
        }

        public string GetLoginToken(string key, TimeSpan expiration)
        {
            return Client.tokenGen.GetLoginToken(Key.Issuer, this.Key.ToString(), expiration, key);
        }

        public Task SetVADPropertiesAsync(int hangover = 2000, int noiseFloor = 576, int sensitivity = 43)
        {
            throw new NotImplementedException();
        }

        public void Logout()
        {
            if (State == LoginState.LoggedIn || State == LoginState.LoggingIn)
            {
                State = LoginState.LoggingOut;
                vx_terminateLoginSession();
            }
        }

        private void Cleanup()
        {
            _channelSessions.Clear();
            _transmittingSession = null;
            _presenceSubscriptions.Clear();
            _allowedSubscriptions.Clear();
            _blockedSubscriptions.Clear();
            _incomingSubscriptionRequests.Clear();
            _directedMessages.Clear();
            _failedDirectedMessages.Clear();
            _accountArchive.Clear();
            OnStateChange -= HandleLoginStateChange;
            OnDirectedTextMessage -= HandleDirectedMessage;
            if (_disconnectTaskCompletionSource != null && !_disconnectTaskCompletionSource.Task.IsCompleted)
                _disconnectTaskCompletionSource?.TrySetResult(true);
        }

        public void EndLogin(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginAccountSetLoginProperties(
            ParticipantPropertyUpdateFrequency participantPropertyFrequency, AsyncCallback callback)
        {
            throw new NotImplementedException();
        }

        public void EndAccountSetLoginProperties(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginAddBlockedSubscription(AccountId accountId, AsyncCallback callback)
        {
            throw new NotImplementedException();
        }

        public void EndAddBlockedSubscription(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginRemoveBlockedSubscription(AccountId accountId, AsyncCallback callback)
        {
            throw new NotImplementedException();
        }

        public void EndRemoveBlockedSubscription(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginAddAllowedSubscription(AccountId accountId, AsyncCallback callback)
        {
            throw new NotImplementedException();
        }

        public void EndAddAllowedSubscription(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginRemoveAllowedSubscription(AccountId accountId, AsyncCallback callback)
        {
            throw new NotImplementedException();
        }

        public void EndRemoveAllowedSubscription(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginAddPresenceSubscription(AccountId accountId, AsyncCallback callback)
        {
            throw new NotImplementedException();
        }

        public IPresenceSubscription EndAddPresenceSubscription(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginRemovePresenceSubscription(AccountId accountId, AsyncCallback callback)
        {
            throw new NotImplementedException();
        }

        public void EndRemovePresenceSubscription(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginSendDirectedMessage(AccountId userId, string message, MessageOptions options, AsyncCallback callback = null)
        {
            return BeginSendDirectedMessage(userId, options?.Language, message, null, null, callback);
        }

        public IAsyncResult BeginSendDirectedMessage(AccountId userId, string language, string message, string applicationStanzaNamespace,
            string applicationStanzaBody, AsyncCallback callback = null)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            if (AccountId.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrEmpty(message) && string.IsNullOrEmpty(applicationStanzaBody)) throw new ArgumentNullException($"{nameof(message)} and {nameof(applicationStanzaBody)} cannot both be null");AsyncNoResult ar = new AsyncNoResult(callback);
            var results =
                vx_sendDirectedTextMessage(userId.Name, message, language, applicationStanzaNamespace, applicationStanzaBody);
            var exception = new Exception("Failed to send direct message");
            ar.SetComplete(results == 0 ? null : exception);

            if (results != 0)
                throw (exception);
            return ar;
        }

        public void EndSendDirectedMessage(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public Task EditDirectTextMessageAsync(string messageId, string newMessage)
        {
            throw new NotImplementedException();
        }

        public Task DeleteDirectTextMessageAsync(string messageId)
        {
            throw new NotImplementedException();
        }

        public Task<ReadOnlyCollection<VivoxConversation>> GetConversationsAsync(ConversationQueryOptions options =
            null)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginAccountArchiveQuery(DateTime? timeStart, DateTime? timeEnd, string searchText,
            AccountId userId, ChannelId channel, uint max, string afterId, string beforeId, int firstMessageIndex,
            AsyncCallback callback)
        {
            throw new NotImplementedException();
        }

        public void EndAccountArchiveQuery(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public Task<ReadOnlyCollection<VivoxMessage>> GetDirectTextMessageHistoryAsync(AccountId recipient =
                null, int requestSize = 10,
            ChatHistoryQueryOptions chatHistoryQueryOptions = null, AsyncCallback callback = null)
        {
            throw new NotImplementedException();
        }

        public Task SetMessageAsReadAsync(VivoxMessage message, DateTime? seenAt = null)
        {
            throw new NotImplementedException();
        }

        public Task<bool> SetSafeVoiceConsentStatus(string environmentId, string projectId, string playerId, string authToken, bool consentGiven)
        {
            throw new NotImplementedException();
        }

        public Task<bool> GetSafeVoiceConsentStatus(string environmentId, string projectId, string playerId, string authToken)
        {
            throw new NotImplementedException();
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

        private void SetTransmitting(ChannelId channel)
        {
            // Currently only support a single channel so we can just enable capture without passing a Channel
            vx_setLocalCapture(1);
        }

        private void SetNoSessionTransmitting()
        {
            vx_setLocalCapture(0);
        }

        // We Currently only support a single Channel Session in WebGL
        private void SetAllSessionsTransmitting()
        {
            // Currently only support a single channel so we can just enable capture without passing a Channel
            vx_setLocalCapture(1);
        }

        public void SetLocalCaptureMute(int isTransmitting)
        {
            vx_setLocalCapture(isTransmitting);
        }

        public void SetTransmissionMode(TransmissionMode mode, ChannelId singleChannel = null)
        {
            if (mode == TransmissionMode.Single && singleChannel == null)
            {
                throw new ArgumentException(
                    "Setting parameter 'mode' to TransmissionsMode.Single expects a ChannelId for the 'singleChannel' parameter");
            }

            _transmissionType = mode;
            _transmittingChannel = mode == TransmissionMode.Single ? singleChannel : null;

            bool sessionGroupExists = false;
            foreach (var session in _channelSessions)
            {
                if (session.AudioState != ConnectionState.Disconnected ||
                    session.TextState != ConnectionState.Disconnected)
                {
                    sessionGroupExists = true;
                    break;
                }
            }

            if (sessionGroupExists && (_transmissionType != TransmissionMode.Single ||
                                       ChannelSessions.ContainsKey(_transmittingChannel)))
            {
                SetTransmission();
            }
        }

        public Task SetTransmissionModeAsync(TransmissionMode mode, ChannelId singleChannel = null)
        {
            throw new NotImplementedException();
        }

        public Task SetAutoVADAsync(bool onOff)
        {
            throw new NotImplementedException();
        }

        public void StartAudioInjection(string audioFilePath)
        {
            throw new NotImplementedException();
        }

        public void StopAudioInjection()
        {
            throw new NotImplementedException();
        }

        public IAsyncResult SetCrossMutedCommunications(AccountId accountId, bool muted, AsyncCallback callback)
        {
            AsyncNoResult ar = new AsyncNoResult(callback);
            ar.SetCompletedSynchronously();
            return ar;
        }

        public IAsyncResult SetCrossMutedCommunications(List<AccountId> accountIdSet, bool muted,
            AsyncCallback callback)
        {
            AsyncNoResult ar = new AsyncNoResult(callback);
            ar.SetCompletedSynchronously();
            return ar;
        }

        public IAsyncResult ClearCrossMutedCommunications(AsyncCallback callback)
        {
            throw new NotImplementedException();
        }

        public async Task LoginAsync(SubscriptionMode subscriptionMode = SubscriptionMode.Accept,
            IReadOnlyHashSet<AccountId> presenceSubscriptions =
                null, IReadOnlyHashSet<AccountId> blockedPresenceSubscriptions = null,
            IReadOnlyHashSet<AccountId> allowedPresenceSubscriptions = null, string accessToken =
                null, AsyncCallback callback = null)
        {
            var token = accessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                var tokenFetch =
                    Client.tokenGen.GetTokenAsync(Key.Issuer,
                        Helper.TimeSinceUnixEpochPlusDuration(
                            TimeSpan.FromSeconds(VxTokenGen.k_defaultTokenExpirationInSeconds)), null, "login", null,
                        fromUserUri: _accountHandle);
                await tokenFetch;
                token = tokenFetch.Result;
            }

            _connectTaskCompletionSource = new TaskCompletionSource<bool>();

            await Task.Factory.FromAsync(
                BeginLogin(token, subscriptionMode, presenceSubscriptions, blockedPresenceSubscriptions,
                    allowedPresenceSubscriptions, callback),
                ar =>
                {
                    try
                    {
                        return Task.CompletedTask;
                    }
                    catch (Exception)
                    {
                        // Unsubscribe from any events if we failed
                        PropertyChanged = null;
                        throw;
                    }
                });
            await _connectTaskCompletionSource.Task;
        }

        public async Task SendDirectedMessageAsync(AccountId accountId, string message, MessageOptions options = null)
        {
            await Task.Factory.FromAsync(
                BeginSendDirectedMessage(accountId, message, options),
                (ar) =>
                {
                    try
                    {
                        return Task.CompletedTask;
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                });
        }

        public async Task LogoutAsync()
        {
            _disconnectTaskCompletionSource = new TaskCompletionSource<bool>();
            Logout();
            await _disconnectTaskCompletionSource.Task;
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

        protected virtual void OnDirectedMessageEdited(VivoxMessage e)
        {
            DirectedMessageEdited?.Invoke(this, e);
        }

        protected virtual void OnDirectedMessageDeleted(VivoxMessage e)
        {
            DirectedMessageDeleted?.Invoke(this, e);
        }
    }
}

#endif
