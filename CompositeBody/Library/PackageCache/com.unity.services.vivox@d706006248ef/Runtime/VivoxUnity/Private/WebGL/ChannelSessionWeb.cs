#if UNITY_WEBGL && !UNITY_EDITOR

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AOT;
using UnityEngine;

namespace Unity.Services.Vivox
{
    internal class ChannelSessionWeb : IChannelSession
    {
        #region Webextern

        private delegate void PointerCallback(IntPtr ptr);
        private delegate void IntCallback(int newValue);
        private delegate void ChannelParticipantCallback(JSONModel.JSONParticipant participant);
        private delegate void ChannelMessageCallback(JSONChannelMessage message);

        [DllImport("__Internal")]
        private static extern void vx_setupLoginChannelSessionCallbacks(IntCallback onSessionEstablished,
            IntCallback onChannelSessionRemoved,
            IntCallback onRemoteStream, PointerCallback onParticipantAdded, PointerCallback onParticipantUpdated,
            PointerCallback onParticipantRemoved, PointerCallback onChannelTextMessage,
            IntCallback onSessionTerminated);

        [DllImport("__Internal")]
        private static extern int vx_createChannelSession(string channelName, int channelType, int isAudio, int isText,
            string token);

        [DllImport("__Internal")]
        private static extern int vx_sendChannelTextMessage(string message, string language,
            string applicationStanzaNamespace, string applicationStanzaBody);

        [DllImport("__Internal")]
        private static extern void vx_terminateChannelSession();

        [DllImport("__Internal")]
        private static extern void vx_setLocalCapture(int isTransmitting);

        private static event IntCallback OnStateChange;
        private static event ChannelParticipantCallback OnParticipantAdded;
        private static event ChannelParticipantCallback OnParticipantUpdated;
        private static event ChannelParticipantCallback OnParticipantRemoved;
        private static event ChannelMessageCallback OnChannelTextMessage;

        #endregion

        #region Member Variables

        private readonly LoginSessionWeb _loginSession;
        private readonly string _sessionHandle;
        private bool _typing;

        private readonly ReadWriteDictionary<string, IParticipant, ChannelParticipant> _participants =
            new ReadWriteDictionary<string, IParticipant, ChannelParticipant>();

        private readonly ReadWriteQueue<IChannelTextMessage> _messageLog = new ReadWriteQueue<IChannelTextMessage>();

        private readonly ReadWriteQueue<ISessionArchiveMessage> _sessionArchive =
            new ReadWriteQueue<ISessionArchiveMessage>();

        private ArchiveQueryResult _sessionArchiveResult = new ArchiveQueryResult();

        private TaskCompletionSource<bool> _disconnectTaskCompletionSource;
        private TaskCompletionSource<bool> _channelSessionConnectTaskCompletionSource;

        private ConnectionState _channelState;
        private ConnectionState _audioState;
        private ConnectionState _textState;
        private bool _deleted;

        #endregion

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        public string SessionHandle => _sessionHandle;

        public ChannelId Channel => Key;
        public ILoginSession Parent => _loginSession;
        public string GroupId { get; }
        public ChannelId Key { get; }


        public IReadOnlyQueue<IChannelTextMessage> MessageLog => _messageLog;
        public event EventHandler<VivoxMessage> MessageEdited;
        public event EventHandler<VivoxMessage> MessageDeleted;

        public IReadOnlyQueue<ISessionArchiveMessage> SessionArchive => _sessionArchive;

        public IArchiveQueryResult SessionArchiveResult => _sessionArchiveResult;

        public bool IsTransmitting
        {
            get
            {
                return _loginSession.TransmittingChannels.Contains(Key);
            }
        }

        private void SetTransmitting(bool isTransmitting)
        {
            var val = isTransmitting ? 1 : 0;
            vx_setLocalCapture(val);
        }

        public ConnectionState AudioState
        {
            get { return _audioState; }
            private set
            {
                if (value != _audioState)
                {
                    _audioState = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AudioState)));
                    if (value == ConnectionState.Connected)
                        CheckSessionConnection();
                }
            }
        }

        public ConnectionState TextState
        {
            get { return _textState; }
            set
            {
                if (value != _textState)
                {
                    _textState = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextState)));
                    if (value == ConnectionState.Connected)
                        CheckSessionConnection();
                }
            }
        }

        public IReadOnlyDictionary<string, IParticipant> Participants => _participants;

        public ConnectionState ChannelState
        {
            get { return _channelState; }
            set
            {
                if (value != _channelState)
                {
                    _channelState = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChannelState)));
                }
            }
        }

        public IReadOnlyQueue<ITranscribedMessage> TranscribedLog => new ReadWriteQueue<ITranscribedMessage>();


        public bool IsSessionBeingTranscribed => throw new NotImplementedException();

        #region Helpers

        bool AlreadyDone(bool connect, ConnectionState state)
        {
            if (connect)
            {
                return state == ConnectionState.Connected || state == ConnectionState.Connecting;
            }
            else
            {
                return state == ConnectionState.Disconnected || state == ConnectionState.Disconnecting;
            }
        }

        void AssertSessionNotDeleted()
        {
            if (_deleted)
                throw new InvalidOperationException($"{GetType().Name}: Session has been deleted");
        }

        #endregion

        [MonoPInvokeCallback(typeof(IntCallback))]
        public static void HandleChannelSessionStateChangeEvt(int newInt)
        {
            OnStateChange?.Invoke(newInt);
        }

        [MonoPInvokeCallback(typeof(PointerCallback))]
        public static void HandleChannelMessageEvt(IntPtr ptr)
        {
            string value = Marshal.PtrToStringAuto(ptr);
            JSONChannelMessage jsonChannelMessage = JsonUtility.FromJson<JSONChannelMessage>(value);
            OnChannelTextMessage?.Invoke(jsonChannelMessage);
        }

        [MonoPInvokeCallback(typeof(PointerCallback))]
        public static void HandleParticipantAddedEvt(IntPtr ptr)
        {
            string value = Marshal.PtrToStringAuto(ptr);

            try
            {
                JSONModel.JSONParticipant participant = JsonUtility.FromJson<JSONModel.JSONParticipant>(value);
                OnParticipantAdded?.Invoke(participant);
            }
            catch (Exception e)
            {
                VivoxLogger.LogException(e);
            }
        }

        [MonoPInvokeCallback(typeof(PointerCallback))]
        public static void HandleParticipantUpdatedEvt(IntPtr ptr)
        {
            string value = Marshal.PtrToStringAuto(ptr);
            try
            {
                JSONModel.JSONParticipant participant = JsonUtility.FromJson<JSONModel.JSONParticipant>(value);
                OnParticipantUpdated?.Invoke(participant);
            }
            catch (Exception e)
            {
                VivoxLogger.LogException(e);
            }
        }

        [MonoPInvokeCallback(typeof(PointerCallback))]
        public static void HandleParticipantRemovedEvt(IntPtr ptr)
        {
            VivoxLogger.LogVerbose($"HandleParticipantRemovedEvt: prt: {ptr} ");
            string value = Marshal.PtrToStringAuto(ptr);
            try
            {
                JSONModel.JSONParticipant participant = JsonUtility.FromJson<JSONModel.JSONParticipant>(value);
                OnParticipantRemoved?.Invoke(participant);
            }
            catch (Exception e)
            {
                VivoxLogger.LogException(e);
            }
        }

        public ChannelSessionWeb(LoginSessionWeb loginSession, ChannelId channelId, string groupId)
        {
            if (loginSession == null) throw new ArgumentNullException(nameof(loginSession));
            if (ChannelId.IsNullOrEmpty(channelId)) throw new ArgumentNullException(nameof(channelId));
            if (channelId.Type == ChannelType.Positional)
                throw new InvalidOperationException($"Web does not support 3d channels");

            _loginSession = loginSession;
            Key = channelId;
            GroupId = groupId;
            _sessionHandle = $"{loginSession.AccountHandle}_{channelId}";
            OnStateChange += HandleChannelStateChange;
            OnParticipantAdded += HandleParticipantAdded;
            OnParticipantUpdated += HandleParticipantUpdated;
            OnParticipantRemoved += HandleParticipantRemoved;
            OnChannelTextMessage += HandleSessionMessage;

            vx_setupLoginChannelSessionCallbacks(
                HandleChannelSessionStateChangeEvt,
                HandleChannelSessionStateChangeEvt,
                HandleChannelSessionStateChangeEvt,
                HandleParticipantAddedEvt,
                HandleParticipantUpdatedEvt,
                HandleParticipantRemovedEvt,
                HandleChannelMessageEvt,
                HandleChannelSessionStateChangeEvt);
        }

        private void HandleChannelStateChange(int newValue)
        {
            AudioState =
                ((AudioState == ConnectionState.Connecting || AudioState == ConnectionState.Connected) && newValue == 1)
                    ? ConnectionState.Connected
                    : ConnectionState.Disconnected;
            TextState = ((TextState == ConnectionState.Connecting || TextState == ConnectionState.Connected) &&
                         newValue == 1)
                ? ConnectionState.Connected
                : ConnectionState.Disconnected;
            // If not 1 then we are disconnected
            if (newValue != 1)
            {
                ClearSessionStates();
            }
            else
            {
                CheckSessionConnection();
            }
        }

        private void ClearSessionStates()
        {
            AudioState = ConnectionState.Disconnected;
            TextState = ConnectionState.Disconnected;
            ChannelState = ConnectionState.Disconnected;
            _participants.Clear();
            _messageLog.Clear();
            if (_disconnectTaskCompletionSource != null && !_disconnectTaskCompletionSource.Task.IsCompleted)
                _disconnectTaskCompletionSource?.TrySetResult(true);
        }

        private void HandleParticipantAdded(JSONModel.JSONParticipant participant)
        {
            ChannelParticipant channelParticipant = new ChannelParticipant(this, participant);
            if (!_participants.ContainsKey(channelParticipant.Account.ToString()))
            {
                VivoxLogger.LogVerbose($"HandleParticipantAdded {channelParticipant.Account.ToString()}");
                _participants[channelParticipant.Account.ToString()] = channelParticipant;
            }

            if (_participants[channelParticipant.Account.ToString()].IsSelf)
                CheckSessionConnection();
        }

        private void HandleParticipantUpdated(JSONModel.JSONParticipant participant)
        {
            ChannelParticipant channelParticipant = new ChannelParticipant(this, participant);

            if (_participants.ContainsKey(channelParticipant.Account.ToString()))
            {
                ChannelParticipant p = _participants[channelParticipant.Account.ToString()] as ChannelParticipant;

                Debug.Assert(p != null);

                p.InAudio = channelParticipant.InAudio;
                p.InText = channelParticipant.InText;
            }
        }

        private void HandleParticipantRemoved(JSONModel.JSONParticipant participant)
        {
            VivoxLogger.LogVerbose($"HandleParticipantRemoved {participant.participantId}");
            ChannelParticipant channelParticipant = new ChannelParticipant(this, participant);
            if (_participants.ContainsKey(channelParticipant.Account.ToString()))
            {
                _participants.Remove(channelParticipant.Account.ToString());
            }

            if (_participants[channelParticipant.Account.ToString()].IsSelf)
                CheckSessionConnection();
        }

        private void HandleSessionMessage(JSONChannelMessage newMessage)
        {
            VivoxLogger.LogVerbose($"HandleSessionMessage {newMessage.Message}");
            var message = new ChannelTextMessage(this, newMessage);

            Debug.Assert(message != null);
            _messageLog.Enqueue(message);
        }

        void CheckSessionConnection()
        {
            if (Participants != null && _audioState != ConnectionState.Connecting &&
                _textState != ConnectionState.Connecting &&
                Participants.ContainsKey(_loginSession.LoginSessionId.ToString()))
            {
                ChannelState = ConnectionState.Connected;
                if (_channelSessionConnectTaskCompletionSource != null &&
                    !_channelSessionConnectTaskCompletionSource.Task.IsCompleted)
                    _channelSessionConnectTaskCompletionSource?.TrySetResult(true);
            }
        }

        public async Task SendChannelMessageAsync(string message, MessageOptions options)
        {
            await Task.Factory.FromAsync(
                BeginSendText(message, options, null),
                (ar) =>
                {
                    try
                    {
                        EndSendText(ar);
                        return Task.CompletedTask;
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                });
        }

        public IAsyncResult BeginConnect(bool connectAudio, bool connectText, bool switchTransmission,
            string accessToken, AsyncCallback callback)
        {
            AssertSessionNotDeleted();
            if (string.IsNullOrEmpty(accessToken)) throw new ArgumentNullException(nameof(accessToken));
            if (!connectAudio && !connectText)
                throw new ArgumentException($"{GetType().Name}: connectAudio and connectText cannot both be false",
                    nameof(connectAudio));

            if (AudioState != ConnectionState.Disconnected || TextState != ConnectionState.Disconnected)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name}: Both AudioState and Text State must be disconnected");
            }

            AsyncNoResult ar = new AsyncNoResult(callback);

            if (connectAudio)
                AudioState = ConnectionState.Connecting;
            if (connectText)
                TextState = ConnectionState.Connecting;
            ChannelState = ConnectionState.Connecting;
            var results = 1;

            results = vx_createChannelSession(Key.Name, (int)Key.Type, (int)(connectAudio ? 1 : 0),
                (int)(connectText ? 1 : 0), accessToken);
            if (results == 0)
            {
                if (connectAudio)
                    AudioState = ConnectionState.Connected;
                if (connectText)
                    TextState = ConnectionState.Connected;
                ChannelState = ConnectionState.Connected;
            }
            else
            {
                if (connectAudio)
                    AudioState = ConnectionState.Disconnected;
                if (connectText)
                    TextState = ConnectionState.Disconnected;
                ChannelState = ConnectionState.Disconnected;
            }
            var exception =
                new Exception(
                    "Failed to Join Channel: Make sure you are logged in and have microphone permissions before trying to join a channel");
            ar.SetComplete(results == 0 ? null : exception);
            if (results != 0)
            {
                _loginSession.DeleteChannelSession(Key);
                VivoxLogger.LogVxException(exception);
                throw (exception);
            }
            return ar;
        }

        public IAsyncResult BeginSendText(string message, AsyncCallback callback)
        {
            return BeginSendText(null, message, null, null, callback);
        }

        public IAsyncResult BeginSendText(string message, MessageOptions options, AsyncCallback callback)
        {
            return BeginSendText(options?.Language, message, null, null, callback);
        }

        public IAsyncResult BeginSendText(string language, string message, string applicationStanzaNamespace,
            string applicationStanzaBody, AsyncCallback callback)
        {
            if (string.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));
            if (TextState != ConnectionState.Connected)
                throw new InvalidOperationException($"{GetType().Name}: TextState must equal ChannelState.Connected");
            var ar = new AsyncNoResult(callback);

            var results =
                vx_sendChannelTextMessage(message, language, applicationStanzaNamespace, applicationStanzaBody);
            var exception = new Exception("Failed to send message");
            ar.SetComplete(results == 0 ? null : exception);

            if (results != 0)
                throw (exception);
            return ar;
        }

        public IAsyncResult BeginSessionArchiveQuery(DateTime? timeStart, DateTime? timeEnd, string searchText,
            AccountId userId, uint max, string afterId, string beforeId, int firstMessageIndex, AsyncCallback callback)
        {
            throw new NotImplementedException();
        }


        public void EndSendText(IAsyncResult result)
        {
            return;
        }

        public void EndSessionArchiveQuery(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public Task<ReadOnlyCollection<VivoxMessage>> GetChannelTextMessageHistoryAsync(int requestSize = 10,
            ChatHistoryQueryOptions chatHistoryQueryOptions = null,
            AsyncCallback callback = null)
        {
            throw new NotImplementedException();
        }

        public void EndSetAudioConnected(IAsyncResult result)
        {
            return;
        }

        public void EndSetTextConnected(IAsyncResult result)
        {
            return;
        }

        public void Set3DPosition(Vector3 speakerPos, Vector3 listenerPos, Vector3 listenerAtOrient,
            Vector3 listenerUpOrient)
        {
            throw new NotImplementedException();
        }

        public Task SpeechToTextEnableTranscription(bool enable)
        {
            throw new NotImplementedException();
        }

        public void EndConnect(IAsyncResult ar)
        {
            AssertSessionNotDeleted();
            (ar as AsyncNoResult)?.CheckForError();
        }

        public async Task DisconnectAsync()
        {
            if (_loginSession.State != LoginState.LoggedIn)
            {
                throw new InvalidOperationException($"{GetType().Name}: You must be logged in to leave a channel.");
            }

            _disconnectTaskCompletionSource = new TaskCompletionSource<bool>();
            await Task.Factory.FromAsync(Disconnect(), ar =>
            {
                try
                {
                    return Task.CompletedTask;
                }
                catch (Exception e)
                {
                    _disconnectTaskCompletionSource.TrySetException(e);
                    throw;
                }
            });
            await _disconnectTaskCompletionSource.Task;
        }

        public IAsyncResult Disconnect(AsyncCallback callback = null)
        {
            AssertSessionNotDeleted();
            AsyncNoResult ar = new AsyncNoResult(callback);

            if (AudioState == ConnectionState.Connecting || AudioState == ConnectionState.Connected ||
                TextState == ConnectionState.Connecting || TextState == ConnectionState.Connected)
            {
                vx_terminateChannelSession();
                if (AudioState != ConnectionState.Disconnected)
                    AudioState = ConnectionState.Disconnecting;
                if (TextState != ConnectionState.Disconnected)
                    TextState = ConnectionState.Disconnecting;
            }

            ar.SetComplete();
            return ar;
        }

        public void Cleanup()
        {
            if (_channelState != ConnectionState.Disconnected)
            {
                Disconnect();
            }
            _deleted = true;
            OnStateChange -= HandleChannelStateChange;
            OnParticipantAdded -= HandleParticipantAdded;
            OnParticipantUpdated -= HandleParticipantUpdated;
            OnParticipantRemoved -= HandleParticipantRemoved;
            OnChannelTextMessage -= HandleSessionMessage;
        }

        public IAsyncResult BeginSetAudioConnected(bool value, bool switchTransmission, AsyncCallback callback)
        {
            AssertSessionNotDeleted();
            AsyncNoResult ar = new AsyncNoResult(callback);
            return ar;
        }

        public string GetConnectToken(TimeSpan? expiration = null)
        {
            AssertSessionNotDeleted();
            return Client.tokenGen.GetJoinToken(Parent.Key.ToString(), Key.ToString(), expiration);
        }

        public string GetConnectToken(string tokenSigningKey, TimeSpan expiration)
        {
            AssertSessionNotDeleted();
            return Client.tokenGen.GetJoinToken(Key.Issuer, Parent.Key.ToString(), Key.ToString(), expiration,
                tokenSigningKey);
        }

        public IAsyncResult BeginSetTextConnected(bool value, AsyncCallback callback)
        {
            AssertSessionNotDeleted();

            AsyncNoResult ar = new AsyncNoResult(callback);
            if (AlreadyDone(value, TextState))
            {
                ar.CompletedSynchronously = true;
                ar.SetComplete();
            }

            return ar;
        }

        public IAsyncResult BeginSetChannelTranscription(bool value, string accessToken, AsyncCallback callback)
        {
            throw new NotImplementedException();
        }

        public void EndSetChannelTranscription(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public string GetTranscriptionToken(TimeSpan? tokenExpirationDuration = null)
        {
            throw new NotImplementedException();
        }

        public string GetTranscriptionToken(string tokenSigningKey, TimeSpan tokenExpirationDuration)
        {
            throw new NotImplementedException();
        }

        public Task SetVolumeAsync(int value)
        {
            throw new NotImplementedException();
        }

        public Task EditChannelTextMessageAsync(string messageId, string newMessage)
        {
            throw new NotImplementedException();
        }

        public Task DeleteChannelTextMessageAsync(string messageId)
        {
            throw new NotImplementedException();
        }

        public async Task ConnectAsync(bool connectAudio, bool connectText, bool switchTransmission,
            AsyncCallback callback = null, string accessToken = null)
        {
            if (_loginSession.State != LoginState.LoggedIn)
            {
                throw new InvalidOperationException($"{GetType().Name}: You must be logged in to join a channel.");
            }

            var token = accessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                var tokenFetch = Client.tokenGen.GetTokenAsync(Key.Issuer,
                    Helper.TimeSinceUnixEpochPlusDuration(
                        TimeSpan.FromSeconds(VxTokenGen.k_defaultTokenExpirationInSeconds)), null, "join", null,
                    channelUri: Key.ToString(), fromUserUri: Parent.Key.ToString());
                await tokenFetch;
                token = tokenFetch.Result;
            }

            _channelSessionConnectTaskCompletionSource = new TaskCompletionSource<bool>();
            await Task.Factory.FromAsync(
                BeginConnect(connectAudio, connectText, switchTransmission, token, callback),
                (ar) =>
                {
                    try
                    {
                        return Task.CompletedTask;
                    }
                    catch (Exception e)
                    {
                        _channelSessionConnectTaskCompletionSource.TrySetException(e);
                        throw;
                    }
                });
            await _channelSessionConnectTaskCompletionSource.Task;
        }

        public async Task SendChannelMessageAsync(string message)
        {
            await Task.Factory.FromAsync(
                BeginSendText(message, null),
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

        protected virtual void OnMessageEdited(VivoxMessage e)
        {
            MessageEdited?.Invoke(this, e);
        }

        protected virtual void OnMessageDeleted(VivoxMessage e)
        {
            MessageDeleted?.Invoke(this, e);
        }
    }
}

#endif
