using System;
using System.Collections;
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
    internal class ChannelSession : IChannelSession
    {
        #region Member Variables

        private readonly LoginSession _loginSession;
        private readonly string _sessionHandle;
        private bool _isSessionBeingTranscribed;

        private readonly ReadWriteDictionary<string, IParticipant, ChannelParticipant> _participants =
            new ReadWriteDictionary<string, IParticipant, ChannelParticipant>();

        private readonly ReadWriteQueue<IChannelTextMessage> _messageLog = new ReadWriteQueue<IChannelTextMessage>();
        private readonly ReadWriteQueue<ISessionArchiveMessage> _sessionArchive = new ReadWriteQueue<ISessionArchiveMessage>();
        private readonly ReadWriteQueue<ITranscribedMessage> _transcribedLog = new ReadWriteQueue<ITranscribedMessage>();

        private ArchiveQueryResult _sessionArchiveResult = new ArchiveQueryResult();

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

        private TaskCompletionSource<bool> _channelSessionConnectTaskCompletionSource = new TaskCompletionSource<bool>();
        private TaskCompletionSource<bool> _channelSessionDisconnectTaskCompletionSource = new TaskCompletionSource<bool>();

        private ConnectionState _audioState;
        private ConnectionState _textState;
        private ConnectionState _channelState;
        private int _nextTranscriptionId = 0;
        private bool _deleted;

        #endregion

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

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

        void CheckSessionConnection()
        {
            if (_audioState != ConnectionState.Connecting && _textState != ConnectionState.Connecting && Participants.ContainsKey(_loginSession.LoginSessionId.ToString()))
            {
                ChannelState = ConnectionState.Connected;

                if (_channelSessionConnectTaskCompletionSource != null && !_channelSessionConnectTaskCompletionSource.Task.IsCompleted)
                {
                    _channelSessionConnectTaskCompletionSource.TrySetResult(true);
                }
            }
        }

        #endregion

        public ChannelSession(LoginSession loginSession, ChannelId channelId, string groupId)
        {
            if (loginSession == null) throw new ArgumentNullException(nameof(loginSession));
            if (ChannelId.IsNullOrEmpty(channelId)) throw new ArgumentNullException(nameof(channelId));
            _loginSession = loginSession;
            Key = channelId;
            GroupId = groupId;
            _sessionHandle = $"{loginSession.AccountHandle}_{channelId}";
            VxClient.Instance.EventMessageReceived += InstanceOnEventMessageReceived;
            _loginSession.PropertyChanged += InstanceOnLoginSessionPropertyChanged;
        }

        #region Handle Events Messages

        private void InstanceOnLoginSessionPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(_loginSession.TransmittingChannel))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTransmitting)));
            }
        }

        private void InstanceOnEventMessageReceived(vx_evt_base_t eventMessage)
        {
            switch ((vx_event_type)eventMessage.type)
            {
                case vx_event_type.evt_participant_added:
                    HandleParticipantAdded(eventMessage);
                    break;
                case vx_event_type.evt_participant_removed:
                    HandleParticipantRemoved(eventMessage);
                    break;
                case vx_event_type.evt_participant_updated:
                    HandleParticipantUpdated(eventMessage);
                    break;
                case vx_event_type.evt_media_stream_updated:
                    HandleMediaStreamUpdated(eventMessage);
                    break;
                case vx_event_type.evt_text_stream_updated:
                    HandleTextStreamUpdated(eventMessage);
                    break;
                case vx_event_type.evt_session_removed:
                    HandleSessionRemoved(eventMessage);
                    break;
                case vx_event_type.evt_account_login_state_change:
                    HandleLoginStateChange(eventMessage);
                    break;
                case vx_event_type.evt_message:
                    HandleSessionMessage(eventMessage);
                    break;
                case vx_event_type.evt_session_archive_message:
                    HandleSessionArchiveMessage(eventMessage);
                    break;
                case vx_event_type.evt_session_archive_query_end:
                    HandleSessionArchiveQueryEnd(eventMessage);
                    break;
                case vx_event_type.evt_transcribed_message:
                    HandleSessionTranscribedMessage(eventMessage);
                    break;
                case vx_event_type.evt_session_delete_message:
                    HandleSessionDeleteMessage(eventMessage);
                    break;
                case vx_event_type.evt_session_edit_message:
                    HandleSessionEditMessage(eventMessage);
                    break;
            }
        }

        private void HandleSessionDeleteMessage(vx_evt_base_t eventMessage)
        {
            vx_evt_session_delete_message_t evt = eventMessage;
            if (evt.session_handle != _sessionHandle) return;
            DateTime parsedReceivedTime = Helper.UnixTimeStampToDateTime(evt.delete_time);

            var fromUri = Helper.FixUriFromEditAndDeleteEvents(evt.from_uri);
            var message = new ChannelTextMessage()
            {
                Sender = _participants.ContainsKey(fromUri) ?
                    _participants[fromUri].Account
                    : new AccountId(fromUri),
                Message = null,
                ReceivedTime = parsedReceivedTime,
                Key = evt.message_id,
                Language = null,
                ChannelSession = this,
                FromSelf = fromUri == this._loginSession.Key.ToString()
            };

            MessageDeleted?.Invoke(this, new VivoxMessage(_loginSession._parentVivoxServiceInstance, message));
        }

        private void HandleSessionEditMessage(vx_evt_base_t eventMessage)
        {
            vx_evt_session_edit_message_t evt = eventMessage;
            if (evt.session_handle != _sessionHandle) return;
            Debug.Assert(evt != null);

            DateTime parsedReceivedTime = Helper.UnixTimeStampToDateTime(evt.edit_time);

            var fromUri = Helper.FixUriFromEditAndDeleteEvents(evt.from_uri);
            var message = new ChannelTextMessage()
            {
                Sender = _participants.ContainsKey(fromUri) ?
                    _participants[fromUri].Account
                    : new AccountId(fromUri),
                Message = evt.new_message,
                SenderDisplayName = evt.displayname,
                ReceivedTime = parsedReceivedTime,
                Key = evt.message_id,
                Language = evt.language,
                ChannelSession = this,
                FromSelf = fromUri == _loginSession.Key.ToString()
            };

            MessageEdited?.Invoke(this, new VivoxMessage(_loginSession._parentVivoxServiceInstance, message));
        }

        private void HandleParticipantAdded(vx_evt_base_t eventMessage)
        {
            vx_evt_participant_added_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.session_handle != _sessionHandle) return;
            _participants[evt.participant_uri] = new ChannelParticipant(this, evt);
            if (_participants[evt.participant_uri].IsSelf)
                CheckSessionConnection();
        }

        private void HandleParticipantRemoved(vx_evt_base_t eventMessage)
        {
            vx_evt_participant_removed_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.session_handle != _sessionHandle) return;
            _participants.Remove(evt.participant_uri);
        }

        private void HandleParticipantUpdated(vx_evt_base_t eventMessage)
        {
            vx_evt_participant_updated_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.session_handle != _sessionHandle) return;
            if (_participants.ContainsKey(evt.participant_uri))
            {
                ChannelParticipant p = _participants[evt.participant_uri] as ChannelParticipant;
                Debug.Assert(p != null);
                p.IsMutedForAll = evt.is_moderator_muted != 0;
                p.SpeechDetected = evt.is_speaking != 0;
                p.InAudio = (evt.active_media & 0x1) == 0x1;
                p.InText = (evt.active_media & 0x2) == 0x2;
                p.AudioEnergy = evt.energy;
                p._internalVolumeAdjustment = evt.volume;
                p._internalMute = evt.is_muted_for_me != 0;
                p.UnavailableCaptureDevice = evt.has_unavailable_capture_device != 0;
                p.UnavailableRenderDevice = evt.has_unavailable_render_device != 0;
            }
        }

        private void HandleMediaStreamUpdated(vx_evt_base_t eventMessage)
        {
            vx_evt_media_stream_updated_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.session_handle != _sessionHandle) return;
            if (evt.state == vx_session_media_state.session_media_connected)
            {
                AudioState = ConnectionState.Connected;
            }
            else if (evt.state == vx_session_media_state.session_media_disconnected)
            {
                AudioState = ConnectionState.Disconnected;
            }

            // If there is a non-zero status code, we will receive it in session_media_disconnecting and session_media_disconnected events but we only need to process it once.
            // Let's process it as soon as we get it in the session_media_disconnecting event.
            if (evt.status_code != 0 && evt.state == vx_session_media_state.session_media_disconnecting)
            {
                var exception = new VivoxApiException(evt.status_code);
                ThrowUnexpectedChannelDisconnectException(exception);
            }
        }

        private void HandleTextStreamUpdated(vx_evt_base_t eventMessage)
        {
            vx_evt_text_stream_updated_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.session_handle != _sessionHandle) return;
            if (evt.state == vx_session_text_state.session_text_connected)
            {
                TextState = ConnectionState.Connected;
            }
            else if (evt.state == vx_session_text_state.session_text_disconnected)
            {
                TextState = ConnectionState.Disconnected;
            }

            // If there is a non-zero status code, we will receive it in session_text_disconnecting and session_text_disconnected events but we only need to process it once.
            // Let's process it as soon as we get it in the session_text_disconnecting event.
            if (evt.status_code != 0 && evt.state == vx_session_text_state.session_text_disconnecting)
            {
                var exception = new VivoxApiException(evt.status_code);
                ThrowUnexpectedChannelDisconnectException(exception);
            }
        }

        private void ThrowUnexpectedChannelDisconnectException(VivoxApiException exception)
        {
            if (_channelSessionConnectTaskCompletionSource.Task == null || _channelSessionConnectTaskCompletionSource.Task.IsCompleted)
            {
                // If we get an out-of-band disconnect, handle throwing an exception in that case as well.
                VivoxLogger.LogVxException(exception);
            }
            else
            {
                // Throws if we encounter any errors while actively trying to connect to a channel.
                _channelSessionConnectTaskCompletionSource.TrySetException(exception);
            }
        }

        private void HandleSessionRemoved(vx_evt_base_t eventMessage)
        {
            vx_evt_session_removed_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.session_handle != _sessionHandle) return;
            ClearSessionStates();
        }

        private void HandleLoginStateChange(vx_evt_base_t eventMessage)
        {
            vx_evt_account_login_state_change_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.account_handle != _loginSession.AccountHandle) return;
            if (evt.state == vx_login_state_change_state.login_state_logging_out || evt.state == vx_login_state_change_state.login_state_logged_out)
            {
                // The session has been removed or is being removed already due to the logout process
                ClearSessionStates();
            }
        }

        private void ClearSessionStates()
        {
            AudioState = ConnectionState.Disconnected;
            TextState = ConnectionState.Disconnected;
            ChannelState = ConnectionState.Disconnected;
            _participants.Clear();
            _messageLog.Clear();
            _transcribedLog.Clear();
            ClearAllCurrentTextQueries();
            _channelSessionDisconnectTaskCompletionSource.TrySetResult(true);
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

        private void HandleSessionMessage(vx_evt_base_t eventMessage)
        {
            vx_evt_message_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.session_handle != _sessionHandle) return;

            var message = new ChannelTextMessage()
            {
                Sender = _participants.ContainsKey(evt.participant_uri) ?
                    _participants[evt.participant_uri].Account
                    : new AccountId(evt.participant_uri, evt.participant_displayname),
                Message = evt.message_body,
                SenderDisplayName = evt.participant_displayname,
                ReceivedTime = DateTime.Now,
                Key = evt.message_id,
                ApplicationStanzaBody = evt.application_stanza_body,
                ApplicationStanzaNamespace = evt.application_stanza_namespace,
                Language = evt.language,
                ChannelSession = this,
                FromSelf = evt.is_current_user != 0
            };
            _messageLog.Enqueue(message);
        }

        private void HandleSessionArchiveMessage(vx_evt_base_t eventMessage)
        {
            vx_evt_session_archive_message_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.session_handle != _sessionHandle)
            {
                return;
            }

            if (!DateTime.TryParse(evt.time_stamp, out var parsedReceivedTime))
            {
                VivoxLogger.LogError($"{GetType().Name}: {eventMessage.GetType().Name} invalid message: Bad time format");
                return;
            }

            var queryId = evt.query_id;
            var message = new SessionArchiveMessage()
            {
                ChannelSession = this,
                Key = evt.message_id,
                MessageId = evt.message_id,
                SenderDisplayName = evt.displayname,
                QueryId = evt.query_id,
                ReceivedTime = parsedReceivedTime,
                // Check if user is currently in the room otherwise we wont have a display name
                Sender = _participants.ContainsKey(evt.participant_uri) ?
                    _participants[evt.participant_uri].Account
                    : new AccountId(evt.participant_uri),
                Message = evt.message_body,
                FromSelf = evt.is_current_user != 0,
                Language = evt.language
            };

            // Add messages to list as we will await other messages and then return when we are finished
            _internalChatHistoryResults.AddOrUpdate(queryId,
                new List<VivoxMessage>() { new VivoxMessage(_loginSession._parentVivoxServiceInstance, message) },
                (k, l) =>
                {
                    l.Add(new VivoxMessage(_loginSession._parentVivoxServiceInstance, message));
                    return l;
                });
            _sessionArchive.Enqueue(message);
        }

        private void HandleSessionArchiveQueryEnd(vx_evt_base_t eventMessage)
        {
            vx_evt_session_archive_query_end_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.session_handle != _sessionHandle) return;

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

            if (_sessionArchiveResult.QueryId != queryId || !_sessionArchiveResult.Running) return;
            _sessionArchiveResult = new ArchiveQueryResult(evt);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionArchiveResult)));
        }

        private void HandleSessionTranscribedMessage(vx_evt_base_t eventMessage)
        {
            vx_evt_transcribed_message_t evt = eventMessage;
            Debug.Assert(evt != null);
            if (evt.session_handle != _sessionHandle) return;

            var message = new TranscribedMessage(
                _participants.ContainsKey(evt.participant_uri) ? _participants[evt.participant_uri].Account : new AccountId(evt.participant_uri, evt.participant_displayname),
                evt.text,
                _nextTranscriptionId++.ToString("D8"),
                evt.language,
                this,
                evt.is_current_user != 0
            )
            {
                SenderDisplayName = evt.participant_displayname
            };
            _transcribedLog.Enqueue(message);
            _transcribedLog.Dequeue();
        }

        #endregion

        #region IChannelSession Implementation


        public async Task SetVolumeAsync(int value)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            if (_loginSession.State != LoginState.LoggedIn)
            {
                throw new InvalidOperationException($"{GetType().Name}: You must be logged in to set the volume of a channel.");
            }

            var request = new vx_req_session_set_local_render_volume_t
            {
                volume = value + 50,
                session_handle = _sessionHandle
            };

            await Task.Factory.FromAsync(
                VxClient.Instance.BeginIssueRequest(request, null),
                result =>
                {
                    try
                    {
                        VxClient.Instance.EndIssueRequest(result);
                        return Task.CompletedTask;
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                        throw;
                    }
                });
        }

        public async Task ConnectAsync(bool connectAudio, bool connectText, bool switchTransmission, AsyncCallback callback = null, string accessToken = null)
        {
            if (_loginSession.State != LoginState.LoggedIn)
            {
                throw new InvalidOperationException($"{GetType().Name}: You must be logged in to join a channel.");
            }
            var token = accessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                var tokenFetch = Client.tokenGen.GetTokenAsync(Key.Issuer, TimeSpan.FromSeconds(VxTokenGen.k_defaultTokenExpirationInSeconds), null, "join", null, channelUri: Key.ToString(), fromUserUri: Parent.Key.ToString());
                await tokenFetch;
                token = tokenFetch.Result;
            }

            _channelSessionConnectTaskCompletionSource = new TaskCompletionSource<bool>();
            using (var ct = new CancellationTokenSource(Client.requestTimeout))
            {
                ct.Token.Register(async() =>
                {
                    _channelSessionConnectTaskCompletionSource.TrySetException(exception: new TimeoutException($"[Vivox] Failed to connect to channel: {Channel}"));
                    await DisconnectAsync();
                    ClearSessionStates();
                });
                await Task.Factory.FromAsync(
                    BeginConnect(connectAudio, connectText, switchTransmission, token, callback),
                    (ar) =>
                    {
                        try
                        {
                            EndConnect(ar);
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

        public IAsyncResult BeginConnect(bool connectAudio, bool connectText, bool switchTransmission, string accessToken, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }

            AssertSessionNotDeleted();
            if (string.IsNullOrEmpty(accessToken)) throw new ArgumentNullException(nameof(accessToken));
            if (!connectAudio && !connectText)
                throw new ArgumentException($"{GetType().Name}: connectAudio and connectText cannot both be false", nameof(connectAudio));
            if (AudioState != ConnectionState.Disconnected || TextState != ConnectionState.Disconnected)
            {
                throw new InvalidOperationException($"{GetType().Name}: Both AudioState and Text State must be disconnected");
            }

            AsyncNoResult ar = new AsyncNoResult(callback);
            var request = new vx_req_sessiongroup_add_session_t
            {
                account_handle = _loginSession.AccountHandle,
                uri = Key.ToString(),
                session_handle = _sessionHandle,
                sessiongroup_handle = GroupId,
                connect_audio = connectAudio ? 1 : 0,
                connect_text = connectText ? 1 : 0,
                access_token = accessToken
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
                    AudioState = ConnectionState.Disconnected;
                    TextState = ConnectionState.Disconnected;
                    ar.SetComplete(e);
                    throw;
                }
            });

            if (connectAudio)
                AudioState = ConnectionState.Connecting;
            if (connectText)
                TextState = ConnectionState.Connecting;
            ChannelState = ConnectionState.Connecting;

            if (switchTransmission)
            {
                if (!connectAudio)
                {
                    VivoxLogger.Log("Switching audio transmission exclusively to text-only channel -- this is allowed but unusual.");
                }
                _loginSession.SetTransmissionMode(TransmissionMode.Single, Key);
            }
            else
            {
                _loginSession.SetTransmission();
            }

            return ar;
        }

        public void EndConnect(IAsyncResult ar)
        {
            AsyncNoResult parentAr = ar as AsyncNoResult;
            parentAr?.CheckForError();
        }

        public async Task DisconnectAsync()
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }

            if (_loginSession.State != LoginState.LoggedIn)
            {
                throw new InvalidOperationException($"{GetType().Name}: You must be logged in to leave a channel.");
            }
            _channelSessionDisconnectTaskCompletionSource = new TaskCompletionSource<bool>();
            using (var ct = new CancellationTokenSource(Client.requestTimeout))
            {
                ct.Token.Register(() =>
                {
                    _channelSessionDisconnectTaskCompletionSource.TrySetException(exception: new TimeoutException($"[Vivox] Failed to disconnect from channel: {Channel}"));
                });
                await Task.Factory.FromAsync(
                    Disconnect(),
                    (ar) =>
                    {
                        try
                        {
                            EndConnect(ar);
                            return Task.CompletedTask;
                        }
                        catch (Exception e)
                        {
                            _channelSessionDisconnectTaskCompletionSource.TrySetException(e);
                            throw;
                        }
                    });
                await _channelSessionDisconnectTaskCompletionSource.Task;
            }
        }

        public IAsyncResult Disconnect(AsyncCallback callback = null)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }

            AssertSessionNotDeleted();
            AsyncNoResult ar = new AsyncNoResult(callback);

            if (AudioState == ConnectionState.Connecting || AudioState == ConnectionState.Connected ||
                TextState == ConnectionState.Connecting || TextState == ConnectionState.Connected)
            {
                var request = new vx_req_sessiongroup_remove_session_t
                {
                    session_handle = _sessionHandle,
                    sessiongroup_handle = GroupId
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
                    // Don't set the media and text state since that needs to occur from
                    // events or the client will not reflect what's happening on the server.
                });

                if (AudioState != ConnectionState.Disconnected)
                    AudioState = ConnectionState.Disconnecting;
                if (TextState != ConnectionState.Disconnected)
                    TextState = ConnectionState.Disconnecting;
                if (ChannelState != ConnectionState.Disconnected)
                    ChannelState = ConnectionState.Disconnecting;
            }
            return ar;
        }

        public IAsyncResult BeginSetAudioConnected(bool value, bool switchTransmission, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertSessionNotDeleted();

            AsyncNoResult ar = new AsyncNoResult(callback);
            if (value && switchTransmission)
            {
                _loginSession.SetTransmissionMode(TransmissionMode.Single, Key);
            }
            if (AlreadyDone(value, AudioState))
            {
                ar.CompletedSynchronously = true;
                ar.SetComplete();
                return ar;
            }
            if (value)
            {
                var request = new vx_req_session_media_connect_t();
                request.session_handle = _sessionHandle;
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
                AudioState = ConnectionState.Connecting;
                return ar;
            }
            else
            {
                _loginSession.ClearTransmittingChannel(Channel);

                var request = new vx_req_session_media_disconnect_t();
                request.session_handle = _sessionHandle;
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
                AudioState = ConnectionState.Disconnecting;
                return ar;
            }
        }

        public void EndSetAudioConnected(IAsyncResult result)
        {
            AssertSessionNotDeleted();

            AsyncNoResult parentAr = result as AsyncNoResult;
            parentAr?.CheckForError();
        }

        public IAsyncResult BeginSetTextConnected(bool value, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertSessionNotDeleted();

            AsyncNoResult ar = new AsyncNoResult(callback);
            if (AlreadyDone(value, TextState))
            {
                ar.CompletedSynchronously = true;
                ar.SetComplete();
            }
            if (value)
            {
                var request = new vx_req_session_text_connect_t
                {
                    session_handle = _sessionHandle
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
                TextState = ConnectionState.Connecting;
                return ar;
            }
            else
            {
                var request = new vx_req_session_text_disconnect_t();
                request.session_handle = _sessionHandle;
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
                TextState = ConnectionState.Disconnecting;
                return ar;
            }
        }

        public void EndSetTextConnected(IAsyncResult result)
        {
            AssertSessionNotDeleted();

            AsyncNoResult parentAr = result as AsyncNoResult;
            parentAr?.CheckForError();
        }

        public string SessionHandle => _sessionHandle;

        public ILoginSession Parent => _loginSession;
        public string GroupId { get; }
        public ChannelId Key { get; }

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
        public IReadOnlyDictionary<string, IParticipant> Participants => _participants;

        public IAsyncResult BeginSendText(string message, MessageOptions options, AsyncCallback callback)
        {
            AssertSessionNotDeleted();

            return BeginSendText(options?.Language, message,
                options?.Metadata != null ? "userdata" : null,
                options?.Metadata, callback);
        }

        public IAsyncResult BeginSendText(string language, string message, string applicationStanzaNamespace,
            string applicationStanzaBody, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertSessionNotDeleted();

            if (string.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));
            if (TextState != ConnectionState.Connected)
                throw new InvalidOperationException($"{GetType().Name}: TextState must equal ChannelState.Connected");
            var ar = new AsyncNoResult(callback);
            var request = new vx_req_session_send_message_t
            {
                session_handle = _sessionHandle,
                message_body = message,
                application_stanza_body = applicationStanzaBody,
                language = language,
                application_stanza_namespace = applicationStanzaNamespace
            };
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    VxClient.Instance.EndIssueRequest(result);
                    ar.SetComplete();
                }
                catch (VivoxApiException e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    ar.SetComplete(e);
                    throw;
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

        public void EndSendText(IAsyncResult result)
        {
            AssertSessionNotDeleted();
            (result as AsyncNoResult)?.CheckForError();
        }

        public async Task DeleteChannelTextMessageAsync(string messageId)
        {
            if (string.IsNullOrEmpty(messageId)) throw new ArgumentNullException($"{nameof(messageId)} cannot be null");
            await Task.Factory.FromAsync(BeginDeleteTextMessage(messageId, null), (ar) =>
            {
                try
                {
                    EndDeleteTextMessage(ar);
                    return Task.CompletedTask;
                }
                catch (Exception)
                {
                    throw;
                }
            });
        }

        public async Task EditChannelTextMessageAsync(string messageId, string newText)
        {
            if (string.IsNullOrEmpty(messageId)) throw new ArgumentNullException($"{nameof(messageId)} cannot be null");
            if (string.IsNullOrEmpty(newText)) throw new ArgumentNullException($"{nameof(newText)} cannot be null");
            await Task.Factory.FromAsync(BeginEditTextMessage(messageId, newText, null), (ar) =>
            {
                try
                {
                    EndEditTextMessage(ar);
                    return Task.CompletedTask;
                }
                catch (Exception)
                {
                    throw;
                }
            });
        }

        private IAsyncResult BeginDeleteTextMessage(string messageId, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AsyncNoResult ar = new AsyncNoResult(callback);

            var request = new vx_req_session_delete_message_t
            {
                session_handle = _sessionHandle,
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

        public void EndDeleteTextMessage(IAsyncResult result)
        {
            AsyncNoResult ar = result as AsyncNoResult;
            ar?.CheckForError();
        }

        private IAsyncResult BeginEditTextMessage(string messageId, string newText, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AsyncNoResult ar = new AsyncNoResult(callback);

            var request = new vx_req_session_edit_message_t
            {
                session_handle = _sessionHandle,
                message_id = messageId,
                new_message = newText
                    //REVISIT: add language
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

        public void EndEditTextMessage(IAsyncResult result)
        {
            AsyncNoResult ar = result as AsyncNoResult;
            ar?.CheckForError();
        }

        public IReadOnlyQueue<IChannelTextMessage> MessageLog => _messageLog;

        public event EventHandler<VivoxMessage> MessageEdited;

        public event EventHandler<VivoxMessage> MessageDeleted;

        public IReadOnlyQueue<ITranscribedMessage> TranscribedLog => _transcribedLog;

        public IAsyncResult BeginSessionArchiveQuery(DateTime? timeStart, DateTime? timeEnd, string searchText,
            AccountId userId, uint max, string afterId, string beforeId, int firstMessageIndex,
            AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertSessionNotDeleted();
            if (TextState != ConnectionState.Connected)
                throw new InvalidOperationException($"{GetType().Name}: {nameof(TextState)} must equal ChannelState.Connected");
            if (afterId != null && beforeId != null)
                throw new ArgumentException($"{GetType().Name}: Parameters {nameof(afterId)} and {nameof(beforeId)} cannot be used at the same time");
            if (max > 50)
                throw new ArgumentException($"{GetType().Name}: {nameof(max)} cannot be greater than 50");


            var ar = new AsyncNoResult(callback);

            var request = new vx_req_session_archive_query_t
            {
                session_handle = _sessionHandle,
                max = max,
                after_id = afterId,
                before_id = beforeId,
                first_message_index = firstMessageIndex,
                search_text = searchText
            };
            if (timeStart != null && timeStart != DateTime.MinValue)
            {
                request.time_start = (timeStart?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
            }
            if (timeEnd != null && timeEnd != DateTime.MaxValue)
            {
                request.time_end = (timeEnd?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
            }

            if (!AccountId.IsNullOrEmpty(userId))
            {
                request.participant_uri = userId.ToString();
            }

            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                vx_resp_session_archive_query_t response;
                try
                {
                    response = VxClient.Instance.EndIssueRequest(result);
                    _sessionArchiveResult = new ArchiveQueryResult(response.query_id);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionArchiveResult)));
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

        public void EndSessionArchiveQuery(IAsyncResult result)
        {
            AssertSessionNotDeleted();
            (result as AsyncNoResult)?.CheckForError();
        }

        public IReadOnlyQueue<ISessionArchiveMessage> SessionArchive => _sessionArchive;

        public IArchiveQueryResult SessionArchiveResult => _sessionArchiveResult;

        public string GetConnectToken(TimeSpan? expiration = null)
        {
            AssertSessionNotDeleted();
            return Client.tokenGen.GetJoinToken(Parent.Key.ToString(), Key.ToString(), expiration);
        }

        public string GetConnectToken(string tokenSigningKey, TimeSpan expiration)
        {
            AssertSessionNotDeleted();
            return Client.tokenGen.GetJoinToken(Key.Issuer, Parent.Key.ToString(), Key.ToString(), expiration, tokenSigningKey);
        }

        public bool IsTransmitting
        {
            get
            {
                return _loginSession.TransmittingChannels.Contains(Key);
            }
        }

        public bool IsSessionBeingTranscribed => _isSessionBeingTranscribed;

        public async Task SpeechToTextEnableTranscription(bool enable)
        {
            if (ChannelState != ConnectionState.Connected)
            {
                throw new InvalidOperationException($"{GetType().Name}: You must be connected to a channel to enable transcription.");
            }

            if (enable == IsSessionBeingTranscribed)
            {
                return;
            }


            var tokenFetch = Client.tokenGen.GetTokenAsync(Key.Issuer, TimeSpan.FromSeconds(VxTokenGen.k_defaultTokenExpirationInSeconds), null, "trxn", null, channelUri: Key.ToString(), fromUserUri: Parent.Key.ToString());
            await tokenFetch;
            string token = tokenFetch.Result;

            await Task.Factory.FromAsync(
                BeginSetChannelTranscription(enable, token, null),
                (ar) =>
                {
                    try
                    {
                        EndSetChannelTranscription(ar);
                        return Task.CompletedTask;
                    }
                    catch (Exception e)
                    {
                        VivoxLogger.LogVxException($"{ar.GetType().Name} failed: {e}");
                        throw;
                    }
                });
        }

        public IAsyncResult BeginSetChannelTranscription(bool value, string accessToken, AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            AssertSessionNotDeleted();
            AsyncNoResult ar = new AsyncNoResult(callback);

            // No need to issue the request if the current value is already what was passed in.
            if (_isSessionBeingTranscribed == value)
            {
                VivoxLogger.Log($"IsSessionBeingTranscribed is already {value.ToString()}. Returning.");
                ar.SetComplete();
                return ar;
            }
            var request = new vx_req_session_transcription_control_t
            {
                session_handle = this._sessionHandle,
                enable = value ? 1 : 0,
                access_token = accessToken
            };
            VxClient.Instance.BeginIssueRequest(request, result =>
            {
                try
                {
                    var response = VxClient.Instance.EndIssueRequest(result);
                    if (response.status_code == 0)
                    {
                        _isSessionBeingTranscribed = Convert.ToBoolean(request.enable);
                    }
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"BeginSetChannelTranscription() failed {e}");
                    ar.SetComplete(e);
                    throw;
                }
                ar.SetComplete();
            });
            return ar;
        }

        public void EndSetChannelTranscription(IAsyncResult result)
        {
            (result as AsyncNoResult)?.CheckForError();
        }

        public string GetTranscriptionToken(TimeSpan? tokenExpirationDuration = null)
        {
            return Client.tokenGen.GetTranscriptionToken(Parent.Key.ToString(), Key.ToString(), tokenExpirationDuration);
        }

        public string GetTranscriptionToken(string tokenSigningKey, TimeSpan tokenExpirationDuration)
        {
            return Client.tokenGen.GetTranscriptionToken(Key.Issuer, Parent.Key.ToString(), Key.ToString(), tokenExpirationDuration, tokenSigningKey);
        }

        public AsyncResult<string> BeginChatHistoryQuery(int requestSize, ChatHistoryQueryOptions queryOptions = null, AsyncCallback callback = null)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            // If filtering on a player Id we will want to track the cursor based on a per participant basis
            var taskChatHistoryQueryResult = new ChatHistoryQueryResult()
            {
                Participant = !string.IsNullOrWhiteSpace(queryOptions?.PlayerId)
                    ? new AccountId(queryOptions?.PlayerId)
                    : null
            };

            var ar = new AsyncResult<string>(callback);
            var participantFilter = !string.IsNullOrWhiteSpace(queryOptions?.PlayerId)
                ? new AccountId(queryOptions?.PlayerId).ToString()
                : null;

            var request = new vx_req_session_chat_history_query_t
            {
                session_handle = _sessionHandle,
                max = (uint)requestSize,
                participant_uri = participantFilter,
                search_text = queryOptions?.SearchText,
                time_end = queryOptions?.TimeEnd?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? null,
                time_start = queryOptions?.TimeStart?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? null,
            };

            var messageQueryCompletionSource = new TaskCompletionSource<IChatHistoryQueryResult>(taskChatHistoryQueryResult);
            using (var ct = new CancellationTokenSource(Client.historyQueryRequestTimeout))
            {
                ct.Token.Register(() =>
                {
                    messageQueryCompletionSource.TrySetException(exception: new TimeoutException($"[Vivox] Channel message history query failed to complete in a reasonable amount of time. Canceling the task."));
                });
                VxClient.Instance.BeginIssueRequest(request, result =>
                {
                    try
                    {
                        vx_resp_session_chat_history_query_t response = VxClient.Instance.EndIssueRequest(result);
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
        }

        public void EndChatHistoryQuery(IAsyncResult result)
        {
            AsyncNoResult ar = result as AsyncNoResult;
            ar?.CheckForError();
        }

        private async Task<IList<VivoxMessage>> GetChannelTextMessageHistoryPageAsync(int pageSize, ChatHistoryQueryOptions chatHistoryQueryOptions = null, AsyncCallback callback = null)
        {
            string queryId = null;
            try
            {
                queryId = await Task.Factory.FromAsync(
                    BeginChatHistoryQuery(pageSize, chatHistoryQueryOptions, callback), (ar) =>
                    {
                        try
                        {
                            var asyncResult = ar as AsyncResult<string>;
                            queryId = asyncResult?.Result;
                            EndChatHistoryQuery(ar);
                            return queryId;
                        }
                        catch (Exception e)
                        {
                            VivoxLogger.LogVxException($"{ar.GetType().Name} failed: {e}");
                            throw;
                        }
                    });
                // By this point, we should have a completion source setup up that we can await after receiving a response from Core.
                // Let's find it and wait until we receive the vx_event_type.evt_session_archive_query_end event which marks the completion of the query.
                var taskToComplete = _chatHistoryTaskResults.FirstOrDefault(r => r.Key == queryId).Value;
                await taskToComplete.Task;
                return taskToComplete.Task.Result.VivoxMessages;
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

        public async Task<ReadOnlyCollection<VivoxMessage>> GetChannelTextMessageHistoryAsync(int requestSize, ChatHistoryQueryOptions chatHistoryQueryOptions = null, AsyncCallback callback = null)
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
                    var pageResults = await GetChannelTextMessageHistoryPageAsync(itemCount, chatHistoryQueryOptions, callback);
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

        public ChannelId Channel => Key;

        public void Set3DPosition(UnityEngine.Vector3 speakerPos, UnityEngine.Vector3 listenerPos, UnityEngine.Vector3 listenerAtOrient, UnityEngine.Vector3 listenerUpOrient)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return;
            }
            if (Channel.Type != ChannelType.Positional)
            {
                throw new InvalidOperationException($"{GetType().Name}: Set3DPosition() failed for InvalidState: Channel must be Positional.");
            }
            if (!(VxClient.Instance.Started))
            {
                throw new InvalidOperationException($"{GetType().Name}: Set3DPosition() failed for InvalidClient: The Client must be Started");
            }
            if (!((AudioState == ConnectionState.Connected && (TextState == ConnectionState.Connected || TextState == ConnectionState.Disconnected)) || (TextState == ConnectionState.Connected && AudioState == ConnectionState.Disconnected)))
            {
                throw new InvalidOperationException($"{GetType().Name}: Set3DPosition() failed for InvalidState: The channel's AudioState must be connected");
            }
            var request = new vx_req_session_set_3d_position_t();
            request.session_handle = _sessionHandle;
            request.Set3DPosition(
                new float[3] { speakerPos.x, speakerPos.y, speakerPos.z },
                new float[3] { listenerPos.x, listenerPos.y, listenerPos.z },
                new float[3] { listenerAtOrient.x, listenerAtOrient.y, listenerAtOrient.z },
                new float[3] { listenerUpOrient.x, listenerUpOrient.y, listenerUpOrient.z }
            );
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

        #endregion

        public void Cleanup()
        {
            _deleted = true;
            VxClient.Instance.EventMessageReceived -= InstanceOnEventMessageReceived;
            _loginSession.PropertyChanged -= InstanceOnLoginSessionPropertyChanged;
        }
    }
}
