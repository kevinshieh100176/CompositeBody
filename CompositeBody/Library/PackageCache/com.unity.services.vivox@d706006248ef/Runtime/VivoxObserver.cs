using System;
using Unity.Services.Core;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// A helper class for subscribing to <see cref="IVivoxService"/> events without needing
    /// to manage service initialization state.
    /// </summary>
    /// <remarks>
    /// <see cref="VivoxObserver"/> can be constructed at any time — even before
    /// <see cref="UnityServices.InitializeAsync"/> has completed — and will automatically
    /// subscribe to the underlying <see cref="IVivoxService"/> once it becomes available.<br/><br/>
    /// Use <see cref="VivoxObserverType"/> flags to scope subscriptions to only the events
    /// your system cares about. Combine flags with the bitwise OR operator:
    /// <c>new VivoxObserver(VivoxObserverType.Login | VivoxObserverType.Channel)</c>
    /// Use <see cref="VivoxObserverType.All"/> to subscribe to all events.<br/><br/>
    /// Always call <see cref="Dispose"/> when done to unsubscribe from all events.<br/><br/>
    /// Events are not emitted retroactively. If the service is already initialized or the user
    /// is already logged in when the observer is constructed, use <see cref="IsServiceInitialized"/>
    /// and <see cref="IsLoggedIn"/> to check current state rather than relying solely on
    /// <see cref="ServiceInitialized"/> or <see cref="LoggedIn"/>.
    /// </remarks>
    public sealed class VivoxObserver : IDisposable
    {
        ServiceObserver<IVivoxService> m_ServiceObserver;

        /// <summary>
        /// The combination of <see cref="VivoxObserverType"/> flags this observer was created with.
        /// </summary>
        public readonly VivoxObserverType ObserverType;

        /// <summary>
        /// The target channel name if this observer is filtering events for a specific channel.
        /// </summary>
        public readonly string TargetChannelName;

        #region Service Events

        /// <summary>
        /// This event is called when the Vivox service is successfully initialized.
        /// </summary>
        public event Action ServiceInitialized;

        /// <summary>
        /// This event is called when the Vivox service fails to initialize.
        /// </summary>
        public event Action<Exception> ServiceInitializationFailed;

        /// <summary>
        /// This event is called when a user successfully logs into Vivox.
        /// Requires <see cref="VivoxObserverType.Login"/>.
        /// </summary>
        public event Action LoggedIn;

        /// <summary>
        /// This event is called when a user logs out of Vivox.
        /// Requires <see cref="VivoxObserverType.Login"/>.
        /// </summary>
        public event Action LoggedOut;

        /// <summary>
        /// This event is called when a channel is joined successfully.
        /// Requires <see cref="VivoxObserverType.Channel"/>.
        /// Filtered by <see cref="TargetChannelName"/> if specified.
        /// </summary>
        public event Action<string> ChannelJoined;

        /// <summary>
        /// This event is called when a channel is left.
        /// Requires <see cref="VivoxObserverType.Channel"/>.
        /// Filtered by <see cref="TargetChannelName"/> if specified.
        /// </summary>
        public event Action<string> ChannelLeft;

        /// <summary>
        /// This event is called when a participant joins a channel.
        /// Requires <see cref="VivoxObserverType.Channel"/>.
        /// Filtered by <see cref="TargetChannelName"/> if specified.
        /// </summary>
        public event Action<VivoxParticipant> ParticipantJoined;

        /// <summary>
        /// This event is called when a participant leaves a channel.
        /// Requires <see cref="VivoxObserverType.Channel"/>.
        /// Filtered by <see cref="TargetChannelName"/> if specified.
        /// </summary>
        public event Action<VivoxParticipant> ParticipantLeft;

        /// <summary>
        /// This event is called when a channel text message is received.
        /// Requires <see cref="VivoxObserverType.ChannelMessages"/>.
        /// Filtered by <see cref="TargetChannelName"/> if specified.
        /// </summary>
        public event Action<VivoxMessage> ChannelMessageReceived;

        /// <summary>
        /// This event is called when a channel text message is edited.
        /// Requires <see cref="VivoxObserverType.ChannelMessages"/>.
        /// Filtered by <see cref="TargetChannelName"/> if specified.
        /// </summary>
        public event Action<VivoxMessage> ChannelMessageEdited;

        /// <summary>
        /// This event is called when a channel text message is deleted.
        /// Requires <see cref="VivoxObserverType.ChannelMessages"/>.
        /// Filtered by <see cref="TargetChannelName"/> if specified.
        /// </summary>
        public event Action<VivoxMessage> ChannelMessageDeleted;

        /// <summary>
        /// This event is called when a direct message is received.
        /// Requires <see cref="VivoxObserverType.DirectMessages"/>.
        /// </summary>
        public event Action<VivoxMessage> DirectMessageReceived;

        /// <summary>
        /// This event is called when a direct message is edited.
        /// Requires <see cref="VivoxObserverType.DirectMessages"/>.
        /// </summary>
        public event Action<VivoxMessage> DirectMessageEdited;

        /// <summary>
        /// This event is called when a direct message is deleted.
        /// Requires <see cref="VivoxObserverType.DirectMessages"/>.
        /// </summary>
        public event Action<VivoxMessage> DirectMessageDeleted;

        /// <summary>
        /// This event is called when the list of available input devices changes.
        /// Requires <see cref="VivoxObserverType.AudioDevices"/>.
        /// </summary>
        public event Action AvailableInputDevicesChanged;

        /// <summary>
        /// This event is called when the effective input device changes.
        /// Requires <see cref="VivoxObserverType.AudioDevices"/>.
        /// </summary>
        public event Action EffectiveInputDeviceChanged;

        /// <summary>
        /// This event is called when the list of available output devices changes.
        /// Requires <see cref="VivoxObserverType.AudioDevices"/>.
        /// </summary>
        public event Action AvailableOutputDevicesChanged;

        /// <summary>
        /// This event is called when the effective output device changes.
        /// Requires <see cref="VivoxObserverType.AudioDevices"/>.
        /// </summary>
        public event Action EffectiveOutputDeviceChanged;

        /// <summary>
        /// Returns true if the Vivox service is initialized and ready.
        /// </summary>
        public bool IsServiceInitialized => VivoxService?.InitializationState == VivoxInitializationState.Initialized;

        /// <summary>
        /// Returns true if the user is currently logged into Vivox.
        /// </summary>
        public bool IsLoggedIn => VivoxService?.IsLoggedIn ?? false;

        /// <summary>
        /// Returns true if the user is in the target channel (if specified).
        /// Defaults to false if a target channel was not specified.
        /// </summary>
        public bool IsInTargetChannel => !string.IsNullOrEmpty(TargetChannelName) &&
                                        (VivoxService?.ActiveChannels?.ContainsKey(TargetChannelName) ?? false);

        /// <summary>
        /// Returns the current Vivox service instance if available.
        /// </summary>
        public IVivoxService VivoxService { get; private set; }

        /// <summary>
        /// Creates a new <see cref="VivoxObserver"/> that subscribes to the specified
        /// <paramref name="observerType"/> events, optionally filtered to a single channel.
        /// </summary>
        /// <param name="observerType">
        /// A combination of <see cref="VivoxObserverType"/> flags specifying which events to subscribe to.
        /// </param>
        /// <param name="targetChannelName">
        /// Optional channel name to filter channel and participant events to a single channel.
        /// </param>
        /// <param name="registry">
        /// Optional custom <see cref="IUnityServices"/> registry. Defaults to <see cref="UnityServices.Instance"/>.
        /// Pass the result of <see cref="UnityServices.CreateServices()"/> here when using multiple service instances.
        /// </param>
        public VivoxObserver(VivoxObserverType observerType, string targetChannelName, IUnityServices registry)
        {
            ObserverType = observerType;
            TargetChannelName = targetChannelName;

            // It is possible and valid to create an observer with a null registry when in Edit mode.
            // In this specific case, the observer will do nothing but should not throw an exception.
            if (registry == null)
            {
                return;
            }

            m_ServiceObserver = new ServiceObserver<IVivoxService>(registry);
            if (m_ServiceObserver.Service == null)
            {
                m_ServiceObserver.Initialized += OnServiceInitialized;
            }
            else
            {
                OnServiceInitialized(m_ServiceObserver.Service);
            }
        }

        /// <summary>
        /// Creates a new <see cref="VivoxObserver"/> that subscribes to the specified
        /// <paramref name="observerType"/> events, optionally filtered to a single channel.
        /// Uses <see cref="UnityServices.Instance"/> as the registry.
        /// </summary>
        /// <param name="observerType">
        /// A combination of <see cref="VivoxObserverType"/> flags specifying which events to subscribe to.
        /// </param>
        /// <param name="targetChannelName">
        /// Optional channel name to filter channel and participant events to a single channel.
        /// </param>
        public VivoxObserver(VivoxObserverType observerType, string targetChannelName = null)
            : this(observerType, targetChannelName, UnityServices.Instance)
        {
        }

        #endregion

        void OnServiceInitialized(IVivoxService vivoxService)
        {
            CleanupObserver();
            VivoxService = vivoxService;
            SetupEventSubscriptions();
        }

        void SetupEventSubscriptions()
        {
            if (VivoxService == null)
            {
                return;
            }

            // Always subscribe to service initialization events
            VivoxService.Initialized += OnVivoxInitialized;
            VivoxService.InitializationFailed += OnVivoxInitializationFailed;

            if (ShouldObserveLoginEvents())
            {
                VivoxService.LoggedIn += OnLoggedIn;
                VivoxService.LoggedOut += OnLoggedOut;
            }

            // Subscribe to channel and participant events based on observer type
            if (ShouldObserveChannelEvents())
            {
                VivoxService.ChannelJoined += OnChannelJoined;
                VivoxService.ChannelLeft += OnChannelLeft;
                VivoxService.ParticipantAddedToChannel += OnParticipantAdded;
                VivoxService.ParticipantRemovedFromChannel += OnParticipantRemoved;
            }

            // Subscribe to message events
            if (ShouldObserveMessageEvents())
            {
                VivoxService.ChannelMessageReceived += OnChannelMessageReceived;
                VivoxService.ChannelMessageEdited += OnChannelMessageEdited;
                VivoxService.ChannelMessageDeleted += OnChannelMessageDeleted;
            }

            if (ShouldObserveDirectMessageEvents())
            {
                VivoxService.DirectedMessageReceived += OnDirectMessageReceived;
                VivoxService.DirectedMessageEdited += OnDirectMessageEdited;
                VivoxService.DirectedMessageDeleted += OnDirectMessageDeleted;
            }

            // Subscribe to audio device events if needed
            if (ShouldObserveAudioDeviceEvents())
            {
                VivoxService.AvailableInputDevicesChanged += OnAvailableInputDevicesChanged;
                VivoxService.EffectiveInputDeviceChanged += OnEffectiveInputDeviceChanged;
                VivoxService.AvailableOutputDevicesChanged += OnAvailableOutputDevicesChanged;
                VivoxService.EffectiveOutputDeviceChanged += OnEffectiveOutputDeviceChanged;
            }
        }

        #region Event Type Checks

        bool ShouldObserveLoginEvents() => (ObserverType & VivoxObserverType.Login) != 0;
        bool ShouldObserveChannelEvents() => (ObserverType & VivoxObserverType.Channel) != 0;
        bool ShouldObserveMessageEvents() => (ObserverType & VivoxObserverType.ChannelMessages) != 0;
        bool ShouldObserveDirectMessageEvents() => (ObserverType & VivoxObserverType.DirectMessages) != 0;
        bool ShouldObserveAudioDeviceEvents() => (ObserverType & VivoxObserverType.AudioDevices) != 0;

        void OnVivoxInitialized()
        {
            // Fire the initialization event
            ServiceInitialized?.Invoke();
        }

        void OnVivoxInitializationFailed(Exception ex)
        {
            ServiceInitializationFailed?.Invoke(ex);
        }

        void OnLoggedIn()
        {
            LoggedIn?.Invoke();
        }

        void OnLoggedOut()
        {
            LoggedOut?.Invoke();
        }

        void OnChannelJoined(string channelName)
        {
            if (channelName == null || ShouldFilterByChannel(channelName))
            {
                return;
            }
            ChannelJoined?.Invoke(channelName);
        }

        void OnChannelLeft(string channelName)
        {
            if (channelName == null || ShouldFilterByChannel(channelName))
            {
                return;
            }
            ChannelLeft?.Invoke(channelName);
        }

        void OnParticipantAdded(VivoxParticipant participant)
        {
            if (participant == null || ShouldFilterByChannel(participant.ChannelName))
            {
                return;
            }
            ParticipantJoined?.Invoke(participant);
        }

        void OnParticipantRemoved(VivoxParticipant participant)
        {
            if (participant == null || ShouldFilterByChannel(participant.ChannelName))
            {
                return;
            }
            ParticipantLeft?.Invoke(participant);
        }

        void OnChannelMessageReceived(VivoxMessage message)
        {
            if (message == null || ShouldFilterByChannel(message.ChannelName))
            {
                return;
            }
            ChannelMessageReceived?.Invoke(message);
        }

        void OnChannelMessageEdited(VivoxMessage message)
        {
            if (message == null || ShouldFilterByChannel(message.ChannelName))
            {
                return;
            }

            ChannelMessageEdited?.Invoke(message);
        }

        void OnChannelMessageDeleted(VivoxMessage message)
        {
            if (message == null || ShouldFilterByChannel(message.ChannelName))
            {
                return;
            }
            ChannelMessageDeleted?.Invoke(message);
        }

        void OnDirectMessageReceived(VivoxMessage message)
        {
            if (message == null)
            {
                return;
            }
            DirectMessageReceived?.Invoke(message);
        }

        void OnDirectMessageEdited(VivoxMessage message)
        {
            if (message == null)
            {
                return;
            }
            DirectMessageEdited?.Invoke(message);
        }

        void OnDirectMessageDeleted(VivoxMessage message)
        {
            if (message == null)
            {
                return;
            }
            DirectMessageDeleted?.Invoke(message);
        }

        void OnAvailableInputDevicesChanged()
        {
            AvailableInputDevicesChanged?.Invoke();
        }

        void OnEffectiveInputDeviceChanged()
        {
            EffectiveInputDeviceChanged?.Invoke();
        }

        void OnAvailableOutputDevicesChanged()
        {
            AvailableOutputDevicesChanged?.Invoke();
        }

        void OnEffectiveOutputDeviceChanged()
        {
            EffectiveOutputDeviceChanged?.Invoke();
        }

        #endregion

        bool ShouldFilterByChannel(string channelName)
        {
            return !string.IsNullOrEmpty(TargetChannelName) && channelName != TargetChannelName;
        }

        void CleanupObserver()
        {
            if (m_ServiceObserver != null)
            {
                m_ServiceObserver.Initialized -= OnServiceInitialized;
                m_ServiceObserver.Dispose();
                m_ServiceObserver = null;
            }
        }

        void CleanupVivoxService()
        {
            if (VivoxService == null)
            {
                return;
            }

            VivoxService.Initialized -= OnVivoxInitialized;
            VivoxService.InitializationFailed -= OnVivoxInitializationFailed;

            if (ShouldObserveLoginEvents())
            {
                VivoxService.LoggedIn -= OnLoggedIn;
                VivoxService.LoggedOut -= OnLoggedOut;
            }

            if (ShouldObserveChannelEvents())
            {
                VivoxService.ChannelJoined -= OnChannelJoined;
                VivoxService.ChannelLeft -= OnChannelLeft;
                VivoxService.ParticipantAddedToChannel -= OnParticipantAdded;
                VivoxService.ParticipantRemovedFromChannel -= OnParticipantRemoved;
            }

            if (ShouldObserveMessageEvents())
            {
                VivoxService.ChannelMessageReceived -= OnChannelMessageReceived;
                VivoxService.ChannelMessageEdited -= OnChannelMessageEdited;
                VivoxService.ChannelMessageDeleted -= OnChannelMessageDeleted;
            }

            if (ShouldObserveDirectMessageEvents())
            {
                VivoxService.DirectedMessageReceived -= OnDirectMessageReceived;
                VivoxService.DirectedMessageEdited -= OnDirectMessageEdited;
                VivoxService.DirectedMessageDeleted -= OnDirectMessageDeleted;
            }

            if (ShouldObserveAudioDeviceEvents())
            {
                VivoxService.AvailableInputDevicesChanged -= OnAvailableInputDevicesChanged;
                VivoxService.EffectiveInputDeviceChanged -= OnEffectiveInputDeviceChanged;
                VivoxService.AvailableOutputDevicesChanged -= OnAvailableOutputDevicesChanged;
                VivoxService.EffectiveOutputDeviceChanged -= OnEffectiveOutputDeviceChanged;
            }

            VivoxService = null;
        }

        /// <summary>
        /// Cleans up all currently registered events and disposes of the observer.
        /// </summary>
        public void Dispose()
        {
            CleanupObserver();
            CleanupVivoxService();
        }
    }

    /// <summary>
    /// Flags that control which <see cref="IVivoxService"/> events a <see cref="VivoxObserver"/> subscribes to.
    /// Combine values with the bitwise OR operator to subscribe to multiple categories.
    /// </summary>
    [Flags]
    public enum VivoxObserverType
    {
        /// <summary>
        /// No events. The observer will still track service initialization state and expose
        /// <see cref="VivoxObserver.IsServiceInitialized"/>, <see cref="VivoxObserver.IsLoggedIn"/>,
        /// and <see cref="VivoxObserver.IsInTargetChannel"/> for polling, but will not fire any events.
        /// </summary>
        None = 0,
        /// <summary>Login and logout events: <see cref="VivoxObserver.LoggedIn"/>, <see cref="VivoxObserver.LoggedOut"/>.</summary>
        Login = 1 << 0,
        /// <summary>Channel join/leave and participant events: <see cref="VivoxObserver.ChannelJoined"/>, <see cref="VivoxObserver.ChannelLeft"/>, <see cref="VivoxObserver.ParticipantJoined"/>, <see cref="VivoxObserver.ParticipantLeft"/>.</summary>
        Channel = 1 << 1,
        /// <summary>Channel text message events: <see cref="VivoxObserver.ChannelMessageReceived"/>, <see cref="VivoxObserver.ChannelMessageEdited"/>, <see cref="VivoxObserver.ChannelMessageDeleted"/>.</summary>
        ChannelMessages = 1 << 2,
        /// <summary>Direct message events: <see cref="VivoxObserver.DirectMessageReceived"/>, <see cref="VivoxObserver.DirectMessageEdited"/>, <see cref="VivoxObserver.DirectMessageDeleted"/>.</summary>
        DirectMessages = 1 << 3,
        /// <summary>Audio device events: <see cref="VivoxObserver.AvailableInputDevicesChanged"/>, <see cref="VivoxObserver.EffectiveInputDeviceChanged"/>, <see cref="VivoxObserver.AvailableOutputDevicesChanged"/>, <see cref="VivoxObserver.EffectiveOutputDeviceChanged"/>.</summary>
        AudioDevices = 1 << 4,
        /// <summary>All event categories.</summary>
        All = Login | Channel | ChannelMessages | DirectMessages | AudioDevices
    }
}
