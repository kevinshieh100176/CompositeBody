using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Authentication.Internal;
#if AUTH_PACKAGE_PRESENT
using Unity.Services.Authentication;
#endif
using Unity.Services.Core;
using Unity.Services.Vivox.AudioTaps;
using Unity.Services.Vivox.Internal;
using System.Runtime.CompilerServices;
using Unity.Services.Core.Internal;
using System.Collections.Concurrent;

namespace Unity.Services.Vivox
{
    class VivoxServiceInternal : IVivoxServiceInternal
    {
        public event Action Initialized;
        public event Action<Exception> InitializationFailed;
        public event Action AvailableInputDevicesChanged;
        public event Action EffectiveInputDeviceChanged;
        public event Action AvailableOutputDevicesChanged;
        public event Action EffectiveOutputDeviceChanged;
        public event Action LoggedIn;
        public event Action LoggedOut;
        public event Action ConnectionRecovering;
        public event Action ConnectionRecovered;
        public event Action ConnectionFailedToRecover;
        public event Action<string> ChannelJoined;
        public event Action<string> ChannelLeft;
        public event Action<VivoxParticipant> ParticipantAddedToChannel;
        public event Action<VivoxParticipant> ParticipantRemovedFromChannel;
        public event Action<VivoxMessage> SpeechToTextMessageReceived;
        public event Action<VivoxMessage> ChannelMessageReceived;
        public event Action<VivoxMessage> ChannelMessageEdited;
        public event Action<VivoxMessage> ChannelMessageDeleted;
        public event Action<VivoxMessage> DirectedMessageReceived;
        public event Action<VivoxMessage> DirectedMessageEdited;
        public event Action<VivoxMessage> DirectedMessageDeleted;

        public VivoxInitializationState InitializationState { get; private set; } = VivoxInitializationState.Uninitialized;
        public IVivoxGlobalAudioSettings VivoxGlobalAudioSettings
        {
            get
            {
                if (InitializationState != VivoxInitializationState.Initialized)
                {
                    return null;
                }
                return m_vivoxAudioSettings;
            }
        }
        public bool IsLoggedIn => m_LoginSession != null && m_LoginSession.State == LoginState.LoggedIn;
        public string SignedInPlayerId => IsLoggedIn ? m_LoginSession.LoginSessionId.Name : string.Empty;
        public bool IsAudioEchoCancellationEnabled => InitializationState == VivoxInitializationState.Initialized && Client.IsAudioEchoCancellationEnabled;
        public ReadOnlyDictionary<string, ReadOnlyCollection<VivoxParticipant>> ActiveChannels
            => new ReadOnlyDictionary<string, ReadOnlyCollection<VivoxParticipant>>(m_ActiveChannels.ToDictionary(pair => pair.Key, pair => pair.Value.AsReadOnly()));
        public string LastChannelJoinedUri => m_LastChannelJoinedUri;
        public ReadOnlyCollection<string> TransmittingChannels => GetTransmittingChannels();

        public ReadOnlyCollection<string> TextToSpeechAvailableVoices =>
            IsLoggedIn ? new ReadOnlyCollection<string>(m_LoginSession.TTS.AvailableVoices.Select(ttsVoice => ttsVoice.Name).ToList()) : new List<string>().AsReadOnly();
        public string TextToSpeechCurrentVoice => IsLoggedIn ? m_LoginSession.TTS.CurrentVoice.Name : string.Empty;

        public VivoxInputDevice ActiveInputDevice => InitializationState == VivoxInitializationState.Initialized ? new VivoxInputDevice(this, Client.AudioInputDevices.ActiveDevice) : null;
        public VivoxInputDevice EffectiveInputDevice => InitializationState == VivoxInitializationState.Initialized ? new VivoxInputDevice(this, Client.AudioInputDevices.EffectiveDevice) : null;
        public VivoxOutputDevice ActiveOutputDevice => InitializationState == VivoxInitializationState.Initialized ? new VivoxOutputDevice(this, Client.AudioOutputDevices.ActiveDevice) : null;
        public VivoxOutputDevice EffectiveOutputDevice => InitializationState == VivoxInitializationState.Initialized ? new VivoxOutputDevice(this, Client.AudioOutputDevices.EffectiveDevice) : null;
        public ReadOnlyCollection<VivoxInputDevice> AvailableInputDevices => GetInputDevices();
        public ReadOnlyCollection<VivoxOutputDevice> AvailableOutputDevices => GetOutputDevices();
        public int InputDeviceVolume => InitializationState == VivoxInitializationState.Initialized ? Client.AudioInputDevices.VolumeAdjustment : 0;
        public int OutputDeviceVolume => InitializationState == VivoxInitializationState.Initialized ? Client.AudioOutputDevices.VolumeAdjustment : 0;
        public bool IsInputDeviceMuted => InitializationState == VivoxInitializationState.Initialized && Client.AudioInputDevices.Muted;
        public bool IsOutputDeviceMuted => InitializationState == VivoxInitializationState.Initialized && Client.AudioOutputDevices.Muted;
        public bool IsInjectingAudio => m_LoginSession != null && m_LoginSession.IsInjectingAudio;

        public event Action<bool> UserInputDeviceMuteStateChanged;

        public IosVoiceProcessingIOModes IosVoiceProcessingIOMode => GetIosVoiceProcessingIOMode();

        /// <summary>
        /// Keys used to fetch Vivox credentials.
        /// </summary>
        public const string k_ServerKey = "com.unity.services.vivox.server";
        public const string k_DomainKey = "com.unity.services.vivox.domain";
        public const string k_IssuerKey = "com.unity.services.vivox.issuer";
        public const string k_TokenKey = "com.unity.services.vivox.token";
        public const string k_EnvironmentCustomKey = "com.unity.services.vivox.is-environment-custom";
        public const string k_TestModeKey = "com.unity.services.vivox.is-test-mode";

        public const string k_MustBeLoggedInErrorMessage = "You must be logged in to perform this operation.";

        // This is used to determine if we will attempt to generate local Vivox Access Tokens (VATs).
        // If we have a Key, Edit > Project Settings > Services > Vivox > Test Mode is true and we will generate debug Vivox Access Tokens locally.
        public bool IsTestMode { get; set; }

        public string AccessToken => AccessTokenComponent?.AccessToken;
        public string PlayerId => PlayerIdComponent?.PlayerId;
        public string EnvironmentId => EnvironmentIdComponent?.EnvironmentId;
        public bool IsAuthenticated => !string.IsNullOrEmpty(PlayerId) && !string.IsNullOrEmpty(EnvironmentId);

        public Client Client { get; set; }
        public string Server { get; set; }
        public string Domain { get; set; }
        public string Issuer { get; set; }
        public string Key { get; set; }
        public bool IsEnvironmentCustom { get; }
        public bool HaveVivoxCredentials => !(string.IsNullOrEmpty(Issuer) && string.IsNullOrEmpty(Domain) && string.IsNullOrEmpty(Server));

        public IAccessToken AccessTokenComponent { get; }
        public IPlayerId PlayerIdComponent { get; }
        public IEnvironmentId EnvironmentIdComponent { get; }
        public IVivoxTokenProviderInternal InternalTokenProvider { get; set; }
        public IVivoxTokenProvider ExternalTokenProvider { get; set; }

        readonly IVivoxGlobalAudioSettings m_vivoxAudioSettings;
        readonly CoreRegistry m_coreRegistry;
#if AUTH_PACKAGE_PRESENT
        IAuthenticationService m_authenticationService;
#endif
        bool m_AuthCallbacksConnected = false;
        Dictionary<ChannelId, ChatCapability> m_ChannelsPendingLogin = new Dictionary<ChannelId, ChatCapability>();
        ILoginSession m_LoginSession;
        Dictionary<string, List<VivoxParticipant>> m_ActiveChannels = new Dictionary<string, List<VivoxParticipant>>();
        string m_LastChannelJoinedUri = string.Empty;

        private Task m_initializationTask;
        private Task m_loginTask;

        private readonly object m_stateLock = new object();
        private readonly object m_loginLock = new object();

        private readonly ConcurrentQueue<Action> m_postInitializeActions = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<Action> m_postLoginActions = new ConcurrentQueue<Action>();

        public VivoxServiceInternal(
            string server,
            string domain,
            string issuer,
            string token,
            bool isEnvironmentCustom,
            bool isTestMode,
            IAccessToken accessTokenComponent,
            IPlayerId playerIdComponent,
            IEnvironmentId environmentIdComponent,
            CoreRegistry registry)
        {
            if (playerIdComponent != null)
            {
                PlayerIdComponent = playerIdComponent;
            }
            if (accessTokenComponent != null)
            {
                AccessTokenComponent = accessTokenComponent;
            }
            if (environmentIdComponent != null)
            {
                EnvironmentIdComponent = environmentIdComponent;
            }
            if (string.IsNullOrEmpty(server))
            {
                VivoxLogger.LogException(new ArgumentException($"'{nameof(server)}' is null or empty", nameof(server)));
                return;
            }
            if (string.IsNullOrEmpty(domain))
            {
                VivoxLogger.LogException(new ArgumentException($"'{nameof(domain)}' is null or empty", nameof(domain)));
            }
            if (string.IsNullOrEmpty(issuer))
            {
                VivoxLogger.LogException(new ArgumentException($"'{nameof(issuer)}' is null or empty", nameof(issuer)));
            }

            Server = server;
            Domain = domain;
            Issuer = issuer;
            Key = token;
            IsEnvironmentCustom = isEnvironmentCustom;
            IsTestMode = isTestMode;

            m_coreRegistry = registry;
#if UNITY_WEBGL && !UNITY_EDITOR
            m_vivoxAudioSettings = new VivoxGlobalAudioSettingsWeb();
#else
            m_vivoxAudioSettings = new VivoxGlobalAudioSettings();
#endif
        }

        // Helper to update the initialization state and notify listeners.
        void SetInitializationState(VivoxInitializationState newState, Exception ex = null)
        {
            var shouldFireInitialized = false;
            var shouldFireFailed = false;

            lock (m_stateLock)
            {
                if (InitializationState == newState)
                {
                    if (newState == VivoxInitializationState.Failed && ex != null)
                    {
                        VivoxLogger.LogException(new Exception("Vivox initialization failed", ex));
                    }
                    return;
                }

                InitializationState = newState;
                if (newState == VivoxInitializationState.Initialized)
                {
                    shouldFireInitialized = true;
                }
                else if (newState == VivoxInitializationState.Failed)
                {
                    shouldFireFailed = true;
                }
            }

            // Log transitions
            switch (newState)
            {
                case VivoxInitializationState.Initializing:
                    VivoxLogger.LogVerbose("Vivox initialization started.");
                    break;
                case VivoxInitializationState.Initialized:
                    VivoxLogger.LogVerbose("Vivox initialization completed successfully.");
                    break;
                case VivoxInitializationState.Failed:
                    if (ex != null)
                    {
                        VivoxLogger.LogException(new Exception("Vivox initialization failed.", ex));
                    }
                    else
                    {
                        VivoxLogger.LogError("Vivox initialization failed.");
                    }
                    break;
                case VivoxInitializationState.Uninitialized:
                default:
                    break;
            }

            // Invoke events outside lock to avoid deadlocks
            if (shouldFireInitialized)
            {
                try
                {
                    Initialized?.Invoke();
                }
                catch (Exception invokeEx)
                {
                    VivoxLogger.LogException(new Exception("Exception thrown by InitializationStateChanged handler", invokeEx));
                }
            }
            else if (shouldFireFailed)
            {
                try
                {
                    InitializationFailed?.Invoke(ex);
                }
                catch (Exception invokeEx)
                {
                    VivoxLogger.LogException(new Exception("Exception thrown by InitializationFailed handler", invokeEx));
                }
            }
        }

        public async Task InitializeAsync(VivoxConfigurationOptions options = null)
        {
            Task initializationTask;
            lock (m_stateLock)
            {
                if (InitializationState == VivoxInitializationState.Initialized)
                {
                    return;
                }

                if (InitializationState == VivoxInitializationState.Initializing)
                {
                    initializationTask = m_initializationTask;
                }
                else
                {
                    SetInitializationState(VivoxInitializationState.Initializing);
                    m_initializationTask = InitializeInternalAsync(options);
                    initializationTask = m_initializationTask;
                }
            }

            await initializationTask;
        }

        private async Task InitializeInternalAsync(VivoxConfigurationOptions options)
        {
#if AUTH_PACKAGE_PRESENT
            if (m_coreRegistry.GetService<IAuthenticationService>() != null)
            {
                m_authenticationService = m_coreRegistry.GetService<IAuthenticationService>();
            }
#endif

            // Capture the current initialization task to detect if a retry starts.
            var currentInitializationTask = m_initializationTask;

            try
            {
                string uriString = Server;

                // If custom credentials are in use, do not modify the Server Uri.
                if (!IsEnvironmentCustom)
                {
                    // If endpoint pulled in from udash is an /appconfig URI, append the Environment ID fragment.
                    // Leave the URI alone if it's an /api2 endpoint.
                    if (!uriString.EndsWith("/api2"))
                    {
                        string environmentFragment = $"/{EnvironmentId}";
                        uriString += environmentFragment;
                    }
                }

                // If this is the first service instance, let's initialize Core.
                Client = Client.Instance;
                await Client.InitializeAsync(uriString, options);

                Client.AudioInputDevices.PropertyChanged += OnInputDevicesChanged;
                Client.AudioOutputDevices.PropertyChanged += OnOutputDevicesChanged;

                // Update state and notify listeners that initialization finished successfully.
                SetInitializationState(VivoxInitializationState.Initialized);

                VivoxLogger.LogVerbose($"Initialization complete. Processing {m_postInitializeActions.Count} queued actions.");
                while (m_postInitializeActions.TryDequeue(out var action))
                {
                    action.Invoke();
                }
            }
            catch (Exception e)
            {
                // Transition to failed and notify listeners.
                SetInitializationState(VivoxInitializationState.Failed, e);

                lock (m_stateLock)
                {
                    // Only clear actions and reset the task if no retry has started.
                    if (m_initializationTask == currentInitializationTask)
                    {
                        VivoxLogger.LogVerbose("Clearing queued post-initialize actions due to initialization failure.");
                        while (m_postInitializeActions.TryDequeue(out _)) {}

                        m_initializationTask = null;
                    }
                    else
                    {
                        VivoxLogger.LogVerbose("A retry has started; preserving queued actions and the new initialization task.");
                    }
                }

                throw;
            }
        }

        public async Task LoginAsync(LoginOptions loginOptions = null)
        {
            await EnsureInitializedAsync();

            if (!EnsureAccessTokenIsValid() || !EnsureIsLoggedOut())
            {
                return;
            }

            loginOptions = loginOptions ?? new LoginOptions();

            // If Auth is in use, use Auth's PlayerId.
            // If not, use a custom PlayerId override, if specified, or default to generating a GUID as a user's PlayerId.
            var playerId = IsAuthenticated
                ? PlayerId
                : string.IsNullOrEmpty(loginOptions.PlayerId) ? Guid.NewGuid().ToString() : loginOptions.PlayerId;

            m_LoginSession = Client.GetLoginSession(
                new AccountId(
                    Issuer,
                    playerId,
                    Domain,
                    loginOptions.DisplayName,
                    loginOptions.SpeechToTextLanguages.ToArray(),
                    string.IsNullOrEmpty(EnvironmentId) ? string.Empty : EnvironmentId),
                this);
            m_LoginSession.ParticipantPropertyFrequency = loginOptions.ParticipantUpdateFrequency;
            m_LoginSession.PropertyChanged += OnLoginSessionPropertyChanged;
            m_LoginSession.DirectedMessages.AfterItemAdded += OnDirectedMessageReceived;
            m_LoginSession.DirectedMessageEdited += OnDirectedMessageEdited;
            m_LoginSession.DirectedMessageDeleted += OnDirectedMessageDeleted;
#if AUTH_PACKAGE_PRESENT
            if (IsAuthenticated && m_authenticationService != null)
            {
                m_authenticationService.SignedOut += OnAuthenticationLost;
                m_authenticationService.Expired += OnAuthenticationLost;
                m_AuthCallbacksConnected = true;
            }
#endif
            await m_LoginSession.LoginAsync();
            await SetChannelTransmissionModeAsync(TransmissionMode.All);
            if (loginOptions.BlockedUserList.Count > 0)
            {
                foreach (string PlayerId in loginOptions.BlockedUserList)
                {
                    await BlockPlayerAsync(PlayerId);
                }
            }
        }

        public async Task LogoutAsync()
        {
            if (!IsLoggedIn)
            {
                return;
            }

            await LeaveAllChannelsAsync();
            await m_LoginSession.LogoutAsync();
        }

        public async Task SetChannelTransmissionModeAsync(TransmissionMode transmissionMode, string channelName = null)
        {
            await EnsureLoggedInAsync();

            if (transmissionMode == TransmissionMode.Single)
            {
                if (!EnsureIsInChannel(channelName))
                {
                    return;
                }

                var channelSession = m_LoginSession.ChannelSessions.FirstOrDefault(c => c.Key.Name == channelName);
                await m_LoginSession.SetTransmissionModeAsync(TransmissionMode.Single, channelSession.Key);
            }
            else
            {
                await m_LoginSession.SetTransmissionModeAsync(transmissionMode);
            }
        }

        public void StartAudioInjection(string audioFilePath)
        {
            if (IsLoggedIn)
            {
                m_LoginSession.StartAudioInjection(audioFilePath);
                return;
            }

            VivoxLogger.LogVerbose("Service not logged in. Queuing StartAudioInjection action.");
            m_postLoginActions.Enqueue(() => StartAudioInjection(audioFilePath));

            TaskUtils.FireAndForgetSafe(EnsureLoggedInAsync);
        }

        public void StopAudioInjection()
        {
            if (IsLoggedIn)
            {
                m_LoginSession.StopAudioInjection();
                return;
            }

            VivoxLogger.LogVerbose("Service not logged in. Queuing StopAudioInjection action.");
            m_postLoginActions.Enqueue(() => StopAudioInjection());

            TaskUtils.FireAndForgetSafe(EnsureLoggedInAsync);
        }

        public async Task SpeechToTextEnableTranscription(string channelName)
        {
            await EnsureLoggedInAsync();

            if (!EnsureIsInChannel(channelName))
            {
                return;
            }

            IChannelSession channelSession = m_LoginSession.ChannelSessions.First(channel => channel.Channel.Name == channelName);
            await channelSession.SpeechToTextEnableTranscription(true);
        }

        public async Task SpeechToTextDisableTranscription(string channelName)
        {
            await EnsureLoggedInAsync();

            if (!EnsureIsInChannel(channelName))
            {
                return;
            }

            IChannelSession channelSession = m_LoginSession.ChannelSessions.First(channel => channel.Channel.Name == channelName);
            await channelSession.SpeechToTextEnableTranscription(false);
        }

        public bool IsSpeechToTextEnabled(string channelName)
        {
            if (!ActiveChannels.Keys.Contains(channelName))
            {
                return false;
            }

            IChannelSession channelSession = m_LoginSession.ChannelSessions.First(channel => channel.Channel.Name == channelName);
            return channelSession.IsSessionBeingTranscribed;
        }

        public void TextToSpeechSendMessage(string message, TextToSpeechMessageType messageType)
        {
            if (IsLoggedIn)
            {
                var ttsMessage = new TTSMessage(message, messageType);
                m_LoginSession.TTS.Speak(ttsMessage);
                return;
            }

            VivoxLogger.LogVerbose("Service not logged in. Queuing TextToSpeechSendMessage action.");
            m_postLoginActions.Enqueue(() => TextToSpeechSendMessage(message, messageType));

            TaskUtils.FireAndForgetSafe(EnsureLoggedInAsync);
        }

        public void TextToSpeechCancelAllMessages()
        {
            if (IsLoggedIn)
            {
                m_LoginSession.TTS.CancelAll();
                return;
            }

            VivoxLogger.LogVerbose("Service not logged in. Queuing TextToSpeechCancelAllMessages action.");
            m_postLoginActions.Enqueue(() => TextToSpeechCancelAllMessages());

            TaskUtils.FireAndForgetSafe(EnsureLoggedInAsync);
        }

        public void TextToSpeechCancelMessages(TextToSpeechMessageType messageType)
        {
            if (IsLoggedIn)
            {
                m_LoginSession.TTS.CancelDestination(messageType);
                return;
            }

            VivoxLogger.LogVerbose("Service not logged in. Queuing TextToSpeechCancelMessages action.");
            m_postLoginActions.Enqueue(() => TextToSpeechCancelMessages(messageType));

            TaskUtils.FireAndForgetSafe(EnsureLoggedInAsync);
        }

        public async Task SendDirectTextMessageAsync(string playerId, string message, MessageOptions options)
        {
            await EnsureLoggedInAsync();

            AccountId recipient = new AccountId(Issuer, playerId, Domain, null, null, EnvironmentId);
            await m_LoginSession.SendDirectedMessageAsync(recipient, message, options);
        }

        public async Task EditDirectTextMessageAsync(string messageId, string newMessage)
        {
            await EnsureLoggedInAsync();

            await m_LoginSession.EditDirectTextMessageAsync(messageId, newMessage);
        }

        public async Task DeleteDirectTextMessageAsync(string messageId)
        {
            await EnsureLoggedInAsync();

            await m_LoginSession.DeleteDirectTextMessageAsync(messageId);
        }

        public async Task SendChannelTextMessageAsync(string channelName, string message, MessageOptions options)
        {
            await EnsureLoggedInAsync();

            if (!EnsureIsInChannel(channelName))
            {
                return;
            }

            IChannelSession channelSession = m_LoginSession.ChannelSessions.First(channel => channel.Channel.Name == channelName);
            await channelSession.SendChannelMessageAsync(message, options);
        }

        public async Task EditChannelTextMessageAsync(string channelName, string messageId, string newMessage)
        {
            await EnsureLoggedInAsync();

            if (!EnsureIsInChannel(channelName))
            {
                return;
            }

            IChannelSession channelSession = m_LoginSession.ChannelSessions.First(channel => channel.Channel.Name == channelName);
            await channelSession.EditChannelTextMessageAsync(messageId, newMessage);
        }

        public async Task DeleteChannelTextMessageAsync(string channelName, string messageId)
        {
            await EnsureLoggedInAsync();

            if (!EnsureIsInChannel(channelName))
            {
                return;
            }

            IChannelSession channelSession = m_LoginSession.ChannelSessions.First(channel => channel.Channel.Name == channelName);
            await channelSession.DeleteChannelTextMessageAsync(messageId);
        }

        public async Task<ReadOnlyCollection<VivoxMessage>> GetChannelTextMessageHistoryAsync(string channelName, int requestSize, ChatHistoryQueryOptions chatHistoryQueryOptions = null)
        {
            await EnsureLoggedInAsync();

            if (!EnsureIsInChannel(channelName))
            {
                return null;
            }

            IChannelSession channelSession = m_LoginSession.ChannelSessions.First(channel => channel.Channel.Name == channelName);
            return await channelSession.GetChannelTextMessageHistoryAsync(requestSize, chatHistoryQueryOptions);
        }

        public async Task<ReadOnlyCollection<VivoxMessage>> GetDirectTextMessageHistoryAsync(string playerId = null, int requestSize = 10, ChatHistoryQueryOptions chatHistoryQueryOptions = null)
        {
            await EnsureLoggedInAsync();

            var recipient = string.IsNullOrWhiteSpace(playerId) ? null : new AccountId(Issuer, playerId, Domain, null, null, EnvironmentId);
            return await m_LoginSession.GetDirectTextMessageHistoryAsync(recipient, requestSize, chatHistoryQueryOptions);
        }

        public async Task<ReadOnlyCollection<VivoxConversation>> GetConversationsAsync(ConversationQueryOptions queryOptions = null)
        {
            await EnsureLoggedInAsync();

            return await m_LoginSession.GetConversationsAsync(queryOptions);
        }

        public async Task SetMessageAsReadAsync(VivoxMessage message, DateTime? seenAt = null)
        {
            await EnsureLoggedInAsync();

            await m_LoginSession.SetMessageAsReadAsync(message, seenAt);
        }

        /// <summary>
        /// Sets the token provider to an implementation provided by a developer.
        /// </summary>
        public void SetTokenProvider(IVivoxTokenProvider provider)
        {
            ExternalTokenProvider = provider;
        }

        public void MuteInputDevice()
        {
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                if (!Client.AudioInputDevices.Muted)
                {
                    Client.AudioInputDevices.Mute(true, m_LoginSession?.Key?.ToString());
                    UserInputDeviceMuteStateChanged?.Invoke(Client.AudioInputDevices.Muted);
                }
                return;
            }

            VivoxLogger.LogVerbose("Service not initialized. Queuing MuteInputDevice action.");
            m_postInitializeActions.Enqueue(() => MuteInputDevice());

            TaskUtils.FireAndForgetSafe(EnsureInitializedAsync);
        }

        public void UnmuteInputDevice()
        {
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                if (Client.AudioInputDevices.Muted)
                {
                    Client.AudioInputDevices.Mute(false, m_LoginSession?.Key?.ToString());
                    UserInputDeviceMuteStateChanged?.Invoke(Client.AudioInputDevices.Muted);
                }
                return;
            }

            VivoxLogger.LogVerbose("Service not initialized. Queuing UnmuteInputDevice action.");
            m_postInitializeActions.Enqueue(() => UnmuteInputDevice());

            TaskUtils.FireAndForgetSafe(EnsureInitializedAsync);
        }

        public void MuteOutputDevice()
        {
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                if (!Client.AudioOutputDevices.Muted)
                {
                    Client.AudioOutputDevices.Mute(true, m_LoginSession?.Key?.ToString());
                }
                return;
            }

            VivoxLogger.LogVerbose("Service not initialized. Queuing MuteOutputDevice action.");
            m_postInitializeActions.Enqueue(() => MuteOutputDevice());

            TaskUtils.FireAndForgetSafe(EnsureInitializedAsync);
        }

        public void UnmuteOutputDevice()
        {
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                if (Client.AudioOutputDevices.Muted)
                {
                    Client.AudioOutputDevices.Mute(false, m_LoginSession?.Key?.ToString());
                }
                return;
            }

            VivoxLogger.LogVerbose("Service not initialized. Queuing UnmuteOutputDevice action.");
            m_postInitializeActions.Enqueue(() => UnmuteOutputDevice());

            TaskUtils.FireAndForgetSafe(EnsureInitializedAsync);
        }

        public async Task BlockPlayerAsync(string playerId)
        {
            await EnsureLoggedInAsync();

            var accountToBlock = new AccountId(Issuer, playerId, Domain, null, null, EnvironmentId);
            await m_LoginSession.BlockPlayerAsync(accountToBlock, true);
        }

        public async Task UnblockPlayerAsync(string playerId)
        {
            await EnsureLoggedInAsync();

            var accountToUnblock = new AccountId(Issuer, playerId, Domain, null, null, EnvironmentId);
            await m_LoginSession.BlockPlayerAsync(accountToUnblock, false);
        }

        public async Task JoinGroupChannelAsync(string channelName, ChatCapability chatCapability, ChannelOptions channelOptions = null)
        {
            await JoinChannelAsync(channelName, chatCapability, ChannelType.NonPositional, channelOptions: channelOptions);
        }

        public async Task JoinPositionalChannelAsync(string channelName, ChatCapability chatCapability, Channel3DProperties positionalChannelProperties, ChannelOptions channelOptions = null)
        {
            await JoinChannelAsync(channelName, chatCapability, ChannelType.Positional, positionalChannelProperties, channelOptions);
        }

        public async Task JoinEchoChannelAsync(string channelName, ChatCapability chatCapability, ChannelOptions channelOptions = null)
        {
            await JoinChannelAsync(channelName, chatCapability, ChannelType.Echo, channelOptions: channelOptions);
        }

        /// <summary>
        /// By default we transmit to new channels automatically.
        /// If the user specifies that they don't want that behaviour by setting <see cref="LoginOptions.DisableAutomaticChannelTransmissionSwap"/> to true when logging in we will prevent the automatic transmission swap.
        /// Developers will need to manually switch transmission to other channels if automatic transmission to new channels is disabled.
        /// </summary>
        public async Task JoinChannelAsync(string channelName, ChatCapability chatCapability, ChannelType channelType, Channel3DProperties positionalChannelProperties = null, ChannelOptions channelOptions = null)
        {
            // This single line replaces all the complex validation and manual login calls.
            await EnsureLoggedInAsync();

            if (ActiveChannels.Keys.Contains(channelName))
            {
                VivoxLogger.LogException(new InvalidOperationException($"Unable to join channel \"{channelName}\" because there is already an active channel with the same name."));
                return;
            }

            var channel = new ChannelId(
                Issuer,
                channelName,
                Domain,
                channelType,
                positionalChannelProperties,
                EnvironmentId,
                channelOptions?.IsLargeText ?? false);
            IChannelSession channelSession = m_LoginSession.GetChannelSession(channel);
            SetChannelEventBindings(channelSession, true);
            await channelSession.ConnectAsync(chatCapability != ChatCapability.TextOnly, chatCapability != ChatCapability.AudioOnly, false);
        }

        public async Task LeaveAllChannelsAsync()
        {
            await EnsureLoggedInAsync();

            if (ActiveChannels.Count == 0)
            {
                return;
            }

            foreach (IChannelSession session in m_LoginSession.ChannelSessions.ToList())
            {
                await m_LoginSession.DeleteChannelSessionAsync(session.Key);
            }
        }

        public async Task LeaveChannelAsync(string channelName)
        {
            await EnsureLoggedInAsync();

            if (!EnsureIsInChannel(channelName))
            {
                return;
            }

            await m_LoginSession.DeleteChannelSessionAsync(m_LoginSession.ChannelSessions.First(channel => channel.Channel.Name == channelName).Key);
        }

        public void TextToSpeechSetVoice(string voiceName)
        {
            if (IsLoggedIn)
            {
                ITTSVoice expectedTTSVoice = m_LoginSession.TTS.AvailableVoices.FirstOrDefault(v => v.Name == voiceName);
                if (expectedTTSVoice == null)
                {
                    VivoxLogger.LogException(new InvalidOperationException($"Unable to find a Text-To-Speech voice matching {voiceName} in the list of available voices."));
                    return;
                }

                m_LoginSession.TTS.CurrentVoice = expectedTTSVoice;
                return;
            }


            VivoxLogger.LogVerbose("Service not logged in. Queuing TextToSpeechSetVoice action.");
            m_postLoginActions.Enqueue(() => TextToSpeechSetVoice(voiceName));

            TaskUtils.FireAndForgetSafe(EnsureLoggedInAsync);
        }

        public void Set3DPosition(GameObject participantObject, string channelName, bool allowPanning = true)
        {
            Transform transform = participantObject.transform;
            Set3DPosition(transform.position, transform.position, transform.forward, transform.up, channelName, allowPanning);
        }

        public void Set3DPosition(Vector3 speakerPos, Vector3 listenerPos, Vector3 listenerAtOrient, Vector3 listenerUpOrient, string channelName, bool allowPanning = true)
        {
            if (IsLoggedIn)
            {
                if (!EnsureIsInChannel(channelName))
                {
                    return;
                }

                IChannelSession channelSession = m_LoginSession.ChannelSessions.FirstOrDefault(channel => channel.Channel.Type == ChannelType.Positional && channel.Channel.Name == channelName);
                if (channelSession == null)
                {
                    VivoxLogger.LogException(new InvalidOperationException($"A positional channel with name {channelName} could not be found."));
                }

                // If we find find the channel, we will allow updates only if it is fully connected.
                if (channelSession.ChannelState == ConnectionState.Connected)
                {
                    channelSession.Set3DPosition(speakerPos, listenerPos, allowPanning ? listenerAtOrient : listenerUpOrient, listenerUpOrient);
                }
                return;
            }

            VivoxLogger.LogVerbose("Service not logged in. Queuing Set3DPosition action.");
            m_postLoginActions.Enqueue(() => Set3DPosition(speakerPos, listenerPos, listenerAtOrient, listenerUpOrient, channelName, allowPanning));

            TaskUtils.FireAndForgetSafe(EnsureLoggedInAsync);
        }

        public void EnableAcousticEchoCancellation()
        {
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                Client.SetAudioEchoCancellation(true);
                return;
            }

            VivoxLogger.LogVerbose("Service not initialized. Queuing EnableAcousticEchoCancellation action.");
            m_postInitializeActions.Enqueue(() => EnableAcousticEchoCancellation());

            TaskUtils.FireAndForgetSafe(EnsureInitializedAsync);
        }

        public void DisableAcousticEchoCancellation()
        {
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                Client.SetAudioEchoCancellation(false);
                return;
            }

            VivoxLogger.LogVerbose("Service not initialized. Queuing DisableAcousticEchoCancellation action.");
            m_postInitializeActions.Enqueue(() => DisableAcousticEchoCancellation());

            TaskUtils.FireAndForgetSafe(EnsureInitializedAsync);
        }

        public IosVoiceProcessingIOModes GetIosVoiceProcessingIOMode()
        {
            if (InitializationState != VivoxInitializationState.Initialized)
            {
                return IosVoiceProcessingIOModes.ErrorOccurred;
            }

            return (IosVoiceProcessingIOModes)Client.IosVoiceProcessingIOMode;
        }

        public void SetIosVoiceProcessingIOMode(IosVoiceProcessingIOModes mode)
        {
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                Client.SetIosVoiceProcessingIOMode((int)mode);
                return;
            }

            VivoxLogger.LogVerbose("Service not initialized. Queuing SetIosVoiceProcessingIOMode action.");
            m_postInitializeActions.Enqueue(() => SetIosVoiceProcessingIOMode(mode));

            TaskUtils.FireAndForgetSafe(EnsureInitializedAsync);
        }

        public async Task SetActiveInputDeviceAsync(VivoxInputDevice device)
        {
            await EnsureInitializedAsync();

            await Client.AudioInputDevices.SetActiveDeviceAsync(device.m_parentDevice, accountHandle: m_LoginSession?.Key?.ToString());
        }

        public async Task SetActiveOutputDeviceAsync(VivoxOutputDevice device)
        {
            await EnsureInitializedAsync();

            await Client.AudioOutputDevices.SetActiveDeviceAsync(device.m_parentDevice, accountHandle: m_LoginSession?.Key?.ToString());
        }

        public void SetInputDeviceVolume(int value)
        {
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                Client.AudioInputDevices.AdjustVolume(Mathf.Clamp(value, -50, 50), m_LoginSession?.Key?.ToString());
                return;
            }

            VivoxLogger.LogVerbose("Service not initialized. Queuing SetInputDeviceVolume action.");
            m_postInitializeActions.Enqueue(() => SetInputDeviceVolume(value));

            TaskUtils.FireAndForgetSafe(EnsureInitializedAsync);
        }

        public void SetOutputDeviceVolume(int value)
        {
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                Client.AudioOutputDevices.AdjustVolume(Mathf.Clamp(value, -50, 50), m_LoginSession?.Key?.ToString());
                return;
            }

            VivoxLogger.LogVerbose("Service not initialized. Queuing SetOutputDeviceVolume action.");
            m_postInitializeActions.Enqueue(() => SetOutputDeviceVolume(value));

            TaskUtils.FireAndForgetSafe(EnsureInitializedAsync);
        }

        public async Task SetChannelVolumeAsync(string channelName, int value)
        {
            await EnsureLoggedInAsync();

            if (!EnsureIsInChannel(channelName))
            {
                return;
            }

            var channel = m_LoginSession.ChannelSessions.First(kv => kv.Key.Name == channelName);
            await channel.SetVolumeAsync(Mathf.Clamp(value, -50, 50));
        }

        public async Task EnableAutoVoiceActivityDetectionAsync()
        {
            await EnsureLoggedInAsync();

            await m_LoginSession.SetAutoVADAsync(true);
        }

        public async Task DisableAutoVoiceActivityDetectionAsync()
        {
            await EnsureLoggedInAsync();

            await m_LoginSession.SetAutoVADAsync(false);
        }

        public async Task SetVoiceActivityDetectionPropertiesAsync(int hangover = 2000, int noiseFloor = 576, int sensitivity = 43)
        {
            await EnsureLoggedInAsync();

            await m_LoginSession.SetVADPropertiesAsync(hangover, Mathf.Clamp(noiseFloor, 0, 20000), Mathf.Clamp(sensitivity, 0, 100));
        }

        public async Task<bool> SetSafeVoiceConsentStatus(bool consentGiven)
        {
            await EnsureLoggedInAsync();

            if (!IsAuthenticated)
            {
                VivoxLogger.LogError("The Player must be authenticated through UAS in order to consent to Vivox Safe Voice");
                return false;
            }
            if (PlayerId != m_LoginSession.LoginSessionId.Name)
            {
                VivoxLogger.LogError("The Name provided to BeginLogin must match the UAS PlayerId in order to use Vivox Safe Voice");
                return false;
            }
            return await m_LoginSession.SetSafeVoiceConsentStatus(EnvironmentId, Application.cloudProjectId, PlayerId, AccessToken, consentGiven);
        }

        public async Task<bool> GetSafeVoiceConsentStatus()
        {
            await EnsureLoggedInAsync();

            if (!IsAuthenticated)
            {
                VivoxLogger.LogError("The Player must be authenticated through UAS in order to get current consent to Vivox Safe Voice");
                return false;
            }
            if (PlayerId != m_LoginSession.LoginSessionId.Name)
            {
                VivoxLogger.LogError("The Name provided to BeginLogin must match the UAS PlayerId in order to use Vivox Safe Voice");
                return false;
            }
            return await m_LoginSession.GetSafeVoiceConsentStatus(EnvironmentId, Application.cloudProjectId, PlayerId, AccessToken);
        }

        /// <summary>
        /// Manage the event bindings of a channel for events related to participant updates and the channel itself.
        /// </summary>
        /// <param name="channel">Channel to manage events bindings for.</param>
        /// <param name="doBind">true = bind, false = unbind.</param>
        public void SetChannelEventBindings(IChannelSession channel, bool doBind)
        {
            if (doBind)
            {
                channel.Participants.AfterKeyAdded += OnParticipantAdded;
                channel.Participants.BeforeKeyRemoved += OnParticipantRemoved;
                channel.PropertyChanged += OnChannelPropertyChanged;
                channel.TranscribedLog.AfterItemAdded += OnTranscribedMessageReceived;
                channel.MessageLog.AfterItemAdded += OnChannelMessageReceived;
                channel.MessageEdited += OnChannelMessageEdited;
                channel.MessageDeleted += OnChannelMessageDeleted;
            }
            else
            {
                channel.Participants.AfterKeyAdded -= OnParticipantAdded;
                channel.Participants.BeforeKeyRemoved -= OnParticipantRemoved;
                channel.PropertyChanged -= OnChannelPropertyChanged;
                channel.TranscribedLog.AfterItemAdded -= OnTranscribedMessageReceived;
                channel.MessageLog.AfterItemAdded -= OnChannelMessageReceived;
                channel.MessageEdited -= OnChannelMessageEdited;
                channel.MessageDeleted -= OnChannelMessageDeleted;
            }
        }

        public void OnLoginSessionPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            var loginSession = (ILoginSession)sender;

            switch (propertyChangedEventArgs.PropertyName)
            {
                case "State":
                    HandleLoginStateChange(loginSession);
                    break;
                case "RecoveryState":
                    HandleRecoveryStateChange(loginSession);
                    break;
                default:
                    return;
            }
        }

        public void HandleLoginStateChange(ILoginSession loginSession)
        {
            LoginState newState = loginSession.State;
            switch (newState)
            {
                case LoginState.LoggedIn:
                {
                    lock (m_loginLock)
                    {
                        m_loginTask = null;
                    }
                    LoggedIn?.Invoke();
                    // Process any actions that were queued while we were logging in.
                    VivoxLogger.LogVerbose($"Login complete. Processing {m_postLoginActions.Count} queued actions.");
                    while (m_postLoginActions.TryDequeue(out var action))
                    {
                        action.Invoke();
                    }
                    break;
                }
                case LoginState.LoggedOut:
                {
                    m_ActiveChannels = new Dictionary<string, List<VivoxParticipant>>();
                    lock (m_loginLock)
                    {
                        m_loginTask = null;
                    }
                    LoggedOut?.Invoke();
                    loginSession.PropertyChanged -= OnLoginSessionPropertyChanged;
                    loginSession.DirectedMessages.AfterItemAdded -= OnDirectedMessageReceived;
                    loginSession.DirectedMessageEdited -= OnDirectedMessageEdited;
                    loginSession.DirectedMessageDeleted -= OnDirectedMessageDeleted;
#if AUTH_PACKAGE_PRESENT
                    if (m_AuthCallbacksConnected)
                    {
                        m_authenticationService.SignedOut -= OnAuthenticationLost;
                        m_authenticationService.Expired -= OnAuthenticationLost;
                        m_AuthCallbacksConnected = false;
                    }
#endif
                    break;
                }
                default:
                    break;
            }
        }

        public void HandleRecoveryStateChange(ILoginSession loginSession)
        {
            ConnectionRecoveryState newState = loginSession.RecoveryState;
            switch (newState)
            {
                case ConnectionRecoveryState.Recovering:
                    ConnectionRecovering?.Invoke();
                    break;
                case ConnectionRecoveryState.Recovered:
                    ConnectionRecovered?.Invoke();
                    break;
                case ConnectionRecoveryState.FailedToRecover:
                    ConnectionFailedToRecover?.Invoke();
                    break;
            }
        }

        public void OnChannelPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            var channelSession = (IChannelSession)sender;

            if (args.PropertyName == "ChannelState" && channelSession.ChannelState == ConnectionState.Disconnected)
            {
                if (m_ActiveChannels.TryGetValue(channelSession.Channel.Name, out var channelParticipants))
                {
                    foreach (var participant in channelParticipants)
                    {
                        VivoxLogger.LogVerbose($"Cleaning up residual participants: {participant.PlayerId}");
                        participant.Cleanup();
                    }
                }

                SetChannelEventBindings(channelSession, false);
                m_ActiveChannels.Remove(channelSession.Channel.Name);
                ChannelLeft?.Invoke(channelSession.Channel.Name);
            }
            else if (args.PropertyName == "ChannelState" && channelSession.ChannelState == ConnectionState.Connected)
            {
                // Only add a channel as an active channel once it's fully connected and all participants are in.
                m_ActiveChannels.Add(channelSession.Channel.Name, new List<VivoxParticipant>());
                m_LastChannelJoinedUri = channelSession.Channel.ToString();
                ChannelJoined?.Invoke(channelSession.Channel.Name);

                foreach (var participant in channelSession.Participants)
                {
                    var newParticipant = new VivoxParticipant(this, participant);
                    m_ActiveChannels[channelSession.Channel.Name].Add(newParticipant);
                    ParticipantAddedToChannel?.Invoke(newParticipant);
                }
            }
        }

        private void OnTranscribedMessageReceived(object sender, QueueItemAddedEventArgs<ITranscribedMessage> transcribedMessage)
        {
            SpeechToTextMessageReceived?.Invoke(new VivoxMessage(this, transcribedMessage.Value));
        }

        public void OnChannelMessageReceived(object sender, QueueItemAddedEventArgs<IChannelTextMessage> textMessage)
        {
            IChannelTextMessage channelTextMessage = textMessage.Value;
            var message = new VivoxMessage(this, channelTextMessage);
            ChannelMessageReceived?.Invoke(message);
        }

        private void OnChannelMessageDeleted(object sender, VivoxMessage message)
        {
            ChannelMessageDeleted?.Invoke(message);
        }

        private void OnChannelMessageEdited(object sender, VivoxMessage message)
        {
            ChannelMessageEdited?.Invoke(message);
        }

        public void OnDirectedMessageReceived(object sender, QueueItemAddedEventArgs<IDirectedTextMessage> textMessage)
        {
            IDirectedTextMessage directedTextMessage = textMessage.Value;
            VivoxMessage message = new VivoxMessage(this, directedTextMessage);
            DirectedMessageReceived?.Invoke(message);
        }

        private void OnDirectedMessageDeleted(object sender, VivoxMessage message)
        {
            DirectedMessageDeleted?.Invoke(message);
        }

        private void OnDirectedMessageEdited(object sender, VivoxMessage message)
        {
            DirectedMessageEdited?.Invoke(message);
        }

        public void OnParticipantAdded(object sender, KeyEventArg<string> keyEventArg)
        {
            var source = (IReadOnlyDictionary<string, IParticipant>)sender;
            IParticipant addedParticipant = source[keyEventArg.Key];

            // After the local user is connected, let's allow firing events for any new users that join.
            if (addedParticipant.ParentChannelSession.ChannelState == ConnectionState.Connected)
            {
                var channelName = addedParticipant.ParentChannelSession.Channel.Name;
                // Don't try creating/adding any new participants if for some reason one matching the player ID and channel name exists already.
                if (m_ActiveChannels[channelName].FirstOrDefault(p => p.PlayerId == addedParticipant.Account.Name && p.ChannelName == channelName) != null)
                {
                    return;
                }

                var vivoxParticipant = new VivoxParticipant(this, addedParticipant);
                m_ActiveChannels[vivoxParticipant.ChannelName].Add(vivoxParticipant);
                ParticipantAddedToChannel?.Invoke(vivoxParticipant);
            }
        }

        public void OnParticipantRemoved(object sender, KeyEventArg<string> keyEventArg)
        {
            var source = (IReadOnlyDictionary<string, IParticipant>)sender;
            IParticipant participant = source[keyEventArg.Key];
            var relevantChannelName = participant.ParentChannelSession.Channel.Name;
            // The local player won't have the active channel anymore if they've left so don't try to remove participants it's gone.
            if (m_ActiveChannels.Keys.Contains(relevantChannelName))
            {
                var channelWithPartToRemove = m_ActiveChannels.First(kvp => kvp.Key == relevantChannelName);
                var participantToRemove = channelWithPartToRemove.Value.First(p => p.PlayerId == participant.Account.Name);
                channelWithPartToRemove.Value.Remove(participantToRemove);
                ParticipantRemovedFromChannel?.Invoke(participantToRemove);
            }
        }

        public void OnInputDevicesChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "AvailableDevices")
            {
                AvailableInputDevicesChanged?.Invoke();
            }
            else if (args.PropertyName == "EffectiveDevice")
            {
                EffectiveInputDeviceChanged?.Invoke();
            }
        }

        public void OnOutputDevicesChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "AvailableDevices")
            {
                AvailableOutputDevicesChanged?.Invoke();
            }
            else if (args.PropertyName == "EffectiveDevice")
            {
                EffectiveOutputDeviceChanged?.Invoke();
            }
        }

        private void OnAuthenticationLost()
        {
            if (IsLoggedIn)
            {
                VivoxLogger.LogWarning("The Authentication SDK has been signed out of. As a result, Vivox is logging out.");
                m_LoginSession.LogoutAsync();
            }
        }

        public ReadOnlyCollection<string> GetTransmittingChannels()
        {
            List<string> transmittingChannels = new List<string>();
            if (IsLoggedIn && ActiveChannels.Count > 0)
            {
                foreach (var transmittingChannel in m_LoginSession.TransmittingChannels)
                {
                    transmittingChannels.Add(transmittingChannel.Name);
                }
            }
            return transmittingChannels.AsReadOnly();
        }

        public ReadOnlyCollection<VivoxInputDevice> GetInputDevices()
        {
            var inputDevices = new List<VivoxInputDevice>();
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                foreach (var inputDevice in Client.AudioInputDevices.AvailableDevices)
                {
                    inputDevices.Add(new VivoxInputDevice(this, inputDevice));
                }
            }

            return inputDevices.AsReadOnly();
        }

        public ReadOnlyCollection<VivoxOutputDevice> GetOutputDevices()
        {
            var outputDevices = new List<VivoxOutputDevice>();
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                foreach (var outputDevice in Client.AudioOutputDevices.AvailableDevices)
                {
                    outputDevices.Add(new VivoxOutputDevice(this, outputDevice));
                }
            }

            return outputDevices.AsReadOnly();
        }

        public string GetChannelUriByName(string channelName)
        {
            // Once the channelId regex can parse a uri properly (channel name with a GUID) we can remove this code
            if (string.IsNullOrEmpty(channelName) || m_LoginSession == null)
            {
                return null;
            }

            // The substring is needed until we fix the ChannelId.Name having the Unity Environment (GUID) in it.
            var channelNameToLookup = channelName;
            if (channelNameToLookup.Contains("."))
            {
                channelNameToLookup = channelName.Substring(0, channelName.LastIndexOf("."));
            }

            return m_LoginSession.ChannelSessions
                .FirstOrDefault(c =>
                    String.Equals(c.Key.Name, channelNameToLookup, StringComparison.CurrentCultureIgnoreCase))?.Channel?.ToString();
        }

        public string GetParticipantUriByName(string participantName)
        {
            if (string.IsNullOrEmpty(participantName) || m_LoginSession == null)
            {
                return null;
            }

            string participantUriFound = null;
            foreach (var channelSession in m_LoginSession.ChannelSessions)
            {
                participantUriFound = channelSession.Participants.FirstOrDefault(p =>
                    p.Account.Name.ToLower() == participantName.ToLower() ||
                    p.Account.DisplayName.ToLower() == participantName)
                    ?.Account.ToString();
                if (!string.IsNullOrWhiteSpace(participantUriFound))
                {
                    // We found the participant so lets exit this foreach
                    break;
                }
            }
            return participantUriFound;
        }

        /// <summary>
        /// Ensures the service is initialized. If not already initializing, it starts the process.
        /// This method is safe to call multiple times.
        /// </summary>
        private Task EnsureInitializedAsync()
        {
            if (InitializationState == VivoxInitializationState.Initialized)
            {
                return Task.CompletedTask;
            }

            lock (m_stateLock)
            {
                // If the task is null, or has failed, we should (re)start initialization.
                if (m_initializationTask == null || m_initializationTask.IsFaulted)
                {
                    VivoxLogger.LogVerbose("Initialization not started or has failed. Beginning initialization now.");
                    // Call the original InitializeAsync and store its task.
                    m_initializationTask = InitializeAndProcessQueueAsync();
                }
            }
            return m_initializationTask;
        }

        private async Task InitializeAndProcessQueueAsync()
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception e)
            {
                VivoxLogger.LogException(new Exception("Vivox initialization failed. Queued actions will be cleared.", e));

                //Clear the post init queue.
                while (m_postInitializeActions.TryDequeue(out _)) {}

                throw;
            }
        }

        /// <summary>
        /// Ensures the user is logged in. If not already logging in, it starts the process.
        /// This method will also ensure the service is initialized first.
        /// </summary>
        private async Task EnsureLoggedInAsync()
        {
            await EnsureInitializedAsync();

            if (IsLoggedIn)
            {
                return;
            }

            lock (m_loginLock)
            {
                // If the task is null, or has failed, we should (re)start sign-in.
                if (m_loginTask == null || m_loginTask.IsFaulted)
                {
                    VivoxLogger.LogVerbose("Sign-in not started or has failed. Beginning sign-in now.");
                    m_loginTask = LoginAndProcessQueueAsync();
                }
            }
            await m_loginTask;
        }

        private async Task LoginAndProcessQueueAsync()
        {
            try
            {
                await LoginAsync();
            }
            catch (Exception e)
            {
                VivoxLogger.LogException(new Exception("Vivox login failed. Queued actions will be cleared.", e));

                //Clear the post login queue.
                while (m_postLoginActions.TryDequeue(out _)) {}

                throw;
            }
        }

        bool EnsureIsLoggedOut([CallerMemberName] string memberName = "")
        {
            if (IsLoggedIn)
            {
                VivoxLogger.LogException(
                    new InvalidOperationException(
                        $"Unable to call {nameof(VivoxService)}.Instance.{memberName} because you are already signed into the Vivox Service. " +
                        $"{nameof(VivoxService)}.Instance.{nameof(LogoutAsync)} must be called first."));
                return false;
            }

            return true;
        }

        bool EnsureAccessTokenIsValid()
        {
            bool useLocalTokens = IsTestMode || !string.IsNullOrEmpty(Key);
            bool useServerSideTokens = ExternalTokenProvider != null || InternalTokenProvider != null;


            // TODO: vincent move this to login, but we might want to check it here if the provider is set?
            if (useLocalTokens)
            {
                VivoxLogger.LogWarning("We've detected a Vivox Secret Key being used in the project - we will generate Vivox Access Tokens locally using your Vivox Secret Key but this should only be used for testing!"
                    + "\nWhen you are successfully generating server-side Vivox Access Tokens, please remove the Vivox Key from the \"UnityServices.InitializeAsync(new InitializationOptions().SetVivoxCredentials(...))\" call."
                    + "\nIf Test Mode enabled in the Vivox configuration window (Edit > Project Settings > Services > Vivox) be sure to disable that as well.");
                Client.tokenGen.IssuerKey = Key;
            }
            else if (useServerSideTokens || IsAuthenticated)
            {
                if (!HaveVivoxCredentials)
                {
                    VivoxLogger.LogError("Vivox credentials are missing!"
                        + "\nPlease ensure a project is linked at Edit > Project Settings > Services and that you head to the Services > Vivox window to fetch your Vivox credentials."
                        + "If you wish to use credentials provided from outside of the Unity Dashboard, you can call \"UnityServices.InitializeAsync(new InitializationOptions().SetVivoxCredentials(...))\"");
                    return false;
                }

                Client.tokenGen = new VivoxJWTTokenGen(this);
            }
            else
            {
                VivoxLogger.LogError("Failed to initialize the SDK!"
                    + "\nIs a project linked at \"Edit > Project Settings > Services\"? Head to the \"Services > Vivox\" window to populate your Vivox credentials once a project is linked."
                    + "\nIf you wish to use credentials provided from outside of the Unity Dashboard, you can set them by calling \"UnityServices.InitializeAsync(new InitializationOptions().SetVivoxCredentials(...))\""
                    + "\nIf you're using the Authentication package, be sure to initialize it before initializing the Vivox SDK.");
                return false;
            }

            return true;
        }

        bool EnsureIsInChannel(string channelName = null, [CallerMemberName] string memberName = "")
        {
            if (m_ActiveChannels.Count > 0)
            {
                if (!string.IsNullOrEmpty(channelName) && !m_ActiveChannels.ContainsKey(channelName))
                {
                    VivoxLogger.LogException(
                        new InvalidOperationException(
                            $"Unable to call {nameof(VivoxService)}.Instance.{memberName} because you are not currently in the specified target channel: {channelName} " +
                            $"{nameof(VivoxService)}.Instance.{nameof(JoinGroupChannelAsync)}|{nameof(JoinEchoChannelAsync)}|{nameof(JoinPositionalChannelAsync)} must be called first."));
                    return false;
                }
            }
            else
            {
                VivoxLogger.LogException(
                    new InvalidOperationException(
                        $"Unable to call {nameof(VivoxService)}.Instance.{memberName} because you are not currently in any channels." +
                        $"{nameof(VivoxService)}.Instance.{nameof(JoinGroupChannelAsync)}|{nameof(JoinEchoChannelAsync)}|{nameof(JoinPositionalChannelAsync)} must be called first."));
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Class used to override the token we pass into any Vivox requests.
    /// </summary>
    class VivoxJWTTokenGen : VxTokenGen
    {
        readonly VivoxServiceInternal m_VivoxService;

        internal VivoxJWTTokenGen(VivoxServiceInternal vivoxService)
        {
            m_VivoxService = vivoxService;
        }

        public override string GetToken(string issuer = null, TimeSpan? expiration = null, string targetUserUri = null, string action = null, string tokenKey = null, string channelUri = null, string fromUserUri = null)
        {
            return m_VivoxService.AccessToken;
        }

        /// <summary>
        /// Prioritizes leveraging a token provider provided by an external developer even if an internal package has provided one.
        /// The expectation is that we always defer to the customer but will handle token generation for them if they have not given us a token provider and an internal package has.
        /// If the operation we are performing is "login" then we'll use the Unity Authentication Service ("UAS") token for that but "login" is the only operation it can be used for.
        /// External and internal token providers are expected to be able to handle all other operations and provide valid Vivox tokens (VATs) as a result of their token provider implementation.
        /// </summary>
        public override async Task<string> GetTokenAsync(string issuer = null, TimeSpan? expiration = null, string targetUserUri = null, string action = null, string tokenKey = null, string channelUri = null, string fromUserUri = null)
        {
            if (m_VivoxService.ExternalTokenProvider != null)
            {
                return await m_VivoxService.ExternalTokenProvider?.GetTokenAsync(issuer, Helper.TimeSinceUnixEpochPlusDuration(expiration.Value), targetUserUri, action, channelUri, fromUserUri);
            }

            if (m_VivoxService.InternalTokenProvider != null && action != "login")
            {
                return await m_VivoxService.InternalTokenProvider?.GetTokenAsync(issuer, Helper.TimeSinceUnixEpochPlusDuration(expiration.Value), targetUserUri, action, channelUri, fromUserUri);
            }

            return m_VivoxService.AccessToken;
        }
    }
}
