using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Unity.Services.Vivox.AudioTaps
{

    /// <summary>
    /// Base class of all Vivox Audio Taps
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public abstract class VivoxAudioTap : MonoBehaviour
    {
        private bool m_registered = false;
        private int m_tapId = VxapTapUnRegistered;
        private bool m_RetryIsActive = false;
        private bool m_isEnabled = false;
        private readonly VivoxAudioProcessor m_AudioProcessor;
        private readonly WaitForSecondsRealtime m_cachedWaitForSecondRealtime = new WaitForSecondsRealtime(0.02f); // Caching this object avoids a GC.Alloc for every 'yield return' statement
        internal const string AutoAcquireChannelLabel = "Auto Acquire Channel";

        internal const int VxapTapUnRegistered = -1;
        internal const int VxapErrorAudioSinkInUse = -1000;
        internal const int VxapErrorAudioParticipantUriEmpty = -1050;
        internal const int VxapErrorVivoxServiceNotInitialized = -1051;
        internal const int VxapErrorChannelUriEmpty = -1052;
        internal const int VxapErrorAudioParticipantUnknown = -1053;

        private bool m_AutoAcquireChannel = true;
        private bool m_LastAutoAcquireChannel = true; // For detecting changes from the inspector view

        /// <summary>
        /// The tap ID of the registered audio tap.
        /// If negative, the tap is currently unregistered or there was an error while attempting registration.
        /// </summary>
        [HideInInspector]
        public int TapId => m_tapId;

        /// <summary>
        /// Specifies whether this VivoxAudioTap should automatically try to acquire a Voice Channel to get audio from.
        /// When set to false, the ChannelName property must be set to a valid channel name.
        /// </summary>
        public bool AutoAcquireChannel
        {
            get => m_AutoAcquireChannel;
            set
            {
                if (m_LastAutoAcquireChannel != value)
                {
                    m_AutoAcquireChannel = value;
                    m_LastAutoAcquireChannel = value;

                    TryEnsureTapRegistration();
                }
            }
        }

        // This needs to be serialized so that the value is not lost when domain reload happens
        // but we do not want to expose it to the user; so hide it in the inspector
        [HideInInspector]
        [SerializeField]
        private string m_ChannelUri = string.Empty;

        [SerializeField]
        [Delayed]
        private string m_ChannelName = string.Empty;
        private string m_LastChannelName = string.Empty; // For detecting changes from the inspector view

        /// <summary>
        /// Accepts a Channel name or Channel URI
        /// Translates the display/user friendly version to the internal channel URI
        /// Note: Setting a new channel name/Uri will cause a re-registration and disable Auto Acquire Channel.
        /// </summary>
        public string ChannelName
        {
            get => m_ChannelName;
            set
            {
                if (m_LastChannelName == value || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                m_LastChannelName = value;
                m_ChannelName = value;

                // disable Auto acquire since this is a manual channel set.
                AutoAcquireChannel = false;

                if (VivoxService.Instance != null)
                {
                    m_ChannelUri = ((IVivoxServiceInternal)VivoxService.Instance).GetChannelUriByName(value);
                }
                else
                {
                    m_LastChannelName = String.Empty; // allow this logic to revalidate once the service is available
                    m_ChannelUri = String.Empty;
                }

                VivoxLogger.LogVerbose($"Tap {m_tapId} on {Identifier}, Channel name and Channel URI changed to {ChannelName} and {m_ChannelUri}");
                TryEnsureTapRegistration();
            }
        }

        internal virtual void OnValidate()
        {
            AutoAcquireChannel = m_AutoAcquireChannel;
            ChannelName = m_ChannelName;
        }

        /// <summary>
        /// Returns true if the tap is registered and enabled.
        /// </summary>
        public bool IsRunning => m_registered && m_isEnabled;
        internal int ChannelCount { get; set; } = 0;

#if UNITY_EDITOR
        internal string Status { get; private set; } = "Not Registered";
        internal float MaxDataIn { get; private set; } = 0.0f;
        internal float MaxDataOut { get; private set; } = 0.0f;
        internal abstract float MeterMaxData { get; }
#endif // UNITY_EDITOR

        internal VivoxAudioTap()
        {
            m_AudioProcessor = new VivoxAudioProcessor(this);
        }

        internal abstract int RegisterTap(string channelUri);
        internal abstract string Identifier { get; }

        internal abstract int DoAudioFilterRead(int tapId, float[] data, int numFrames, int numChannels, int sampleRate);

        private bool IsReadyForFilterCalls()
        {
            if (!m_registered || !m_AudioProcessor.IsReady())
            {
                return false;
            }

            if (m_tapId < 0)
            {
                VivoxLogger.LogVerbose($"{Identifier} has an invalid tapID ({m_tapId})");
                return false;
            }

            return true;
        }

        private void OnChannelLeft(string channelName)
        {
            if (m_registered && CurrentChannelMatch(channelName))
            {
                VivoxLogger.LogVerbose($"Tap {m_tapId} on {Identifier}, left channel {m_ChannelName}");
                UnregisterTap();
            }
        }

        private bool CurrentChannelMatch(string channelName)
        {
            return !string.IsNullOrEmpty(m_ChannelName) && m_ChannelName.Equals(channelName);
        }

        private void OnChannelJoined(string channelName)
        {
            // On a channel join, register a tap on the last channel joined if the channel uri was automatically acquired
            if (AutoAcquireChannel)
            {
                // Reset Channel URI, to force rejoin on the latest channel joined
                m_ChannelUri = string.Empty;
                m_ChannelName = string.Empty;
                m_LastChannelName = string.Empty;
                RedoTapRegistration();
                VivoxLogger.LogVerbose($"Tap {m_tapId} on {Identifier}, auto joined new channel {m_ChannelName}");
            }
            else
            {
                if (CurrentChannelMatch(channelName))
                {
                    RedoTapRegistration();
                    VivoxLogger.LogVerbose($"Tap {m_tapId} on {Identifier}, joined channel {m_ChannelName}");
                }
                else
                {
                    VivoxLogger.LogVerbose($"{Identifier}, channel {channelName} not joined, expecting channel name: {m_ChannelName}");
                }
            }
        }

        internal void TryEnsureTapRegistration()
        {
            if (IsRunning)
            {
                RedoTapRegistration();
            }
            else if (!m_registered && m_isEnabled)
            {
                RegisterTapCore();
            }
        }

        internal void RedoTapRegistration()
        {
            VivoxLogger.LogVerbose($"Re-registering {Identifier} tap");
            UnregisterTap();
            RegisterTapCore();
        }

        private void RegisterTapCore()
        {
            if (VivoxService.Instance != null)
            {
                if (AutoAcquireChannel)
                {
                    m_ChannelUri = ((IVivoxServiceInternal)VivoxService.Instance).LastChannelJoinedUri;
                    if (!string.IsNullOrEmpty(m_ChannelUri))
                    {
                        m_ChannelName = GetChannelNameFromUri(m_ChannelUri);
                        m_LastChannelName = m_ChannelName;
                    }
                    else
                    {
                        m_tapId = VxapErrorChannelUriEmpty;
                        UpdateStatus();
                        return;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(m_ChannelName))
                    {
                        m_tapId = VxapErrorChannelUriEmpty;
                        UpdateStatus();
                        return;
                    }
                    m_ChannelUri = ((IVivoxServiceInternal)VivoxService.Instance).GetChannelUriByName(m_ChannelName);
                }

                m_tapId = RegisterTap(m_ChannelUri);

                m_AudioProcessor.InitializeResources(m_tapId);
                if (gameObject.activeInHierarchy)
                {
                    StartCoroutine(ProcessAudio());
                }
            }
            else
            {
                m_tapId = VxapErrorVivoxServiceNotInitialized;
            }

            UpdateStatus();
        }

        private void OnEnable()
        {
            m_isEnabled = true;
            RegisterTapCore();
        }

        private void Awake()
        {
            CheckVivoxService();
        }

        private void CheckVivoxService()
        {
            if (VivoxService.Instance != null)
            {
                CancelRetry();
                VivoxService.Instance.ChannelLeft += OnChannelLeft;
                VivoxService.Instance.ChannelJoined += OnChannelJoined;

                m_AudioProcessor.InitializeAudioConfiguration();
            }
            else
            {
                ActivateRetry();
                m_tapId = VxapErrorVivoxServiceNotInitialized;
                UpdateStatus();
            }
        }

        private enum StatusLevel
        {
            Verbose,
            Warning,
            Error
        }

        private void UpdateStatus()
        {
            m_registered = false;

            if (m_tapId == VxapErrorAudioSinkInUse)
            {
                LogAndSetStatus(StatusLevel.Error, $"Already in use, only one Sink is allowed at any given time");
            }
            else if (m_tapId == VxapErrorAudioParticipantUnknown)
            {
                LogAndSetStatus(StatusLevel.Error, $"Participant Name is unknown");
            }
            else if (m_tapId == VxapErrorAudioParticipantUriEmpty)
            {
                LogAndSetStatus(StatusLevel.Error, $"Participant uri is empty");
            }
            else if (m_tapId == VxapErrorVivoxServiceNotInitialized)
            {
                LogAndSetStatus(StatusLevel.Verbose, $"VivoxService is not initialized, will retry on ChannelJoined event.");
            }
            else if (m_tapId == VxapErrorChannelUriEmpty)
            {
                LogAndSetStatus(StatusLevel.Error, $"Failed to register, the channel is unknown");
            }
            else if (m_tapId == VxapTapUnRegistered)
            {
                LogAndSetStatus(StatusLevel.Verbose, $"Unregistered");
            }
            else if (m_tapId < 0)
            {
                LogAndSetStatus(StatusLevel.Error, $"Failed to register");
            }
            else
            {
                LogAndSetStatus(StatusLevel.Verbose, $"Tap ID {m_tapId} with channel name {ChannelName} registered");
                m_registered = true;
            }
        }

        private void LogAndSetStatus(StatusLevel level, string message)
        {
            string logMessage = $"{Identifier} Tap: " + message;
            switch (level)
            {
                case StatusLevel.Verbose:
                    VivoxLogger.LogVerbose(logMessage);
                    break;
                case StatusLevel.Warning:
                    VivoxLogger.LogWarning(logMessage);
                    break;
                case StatusLevel.Error:
                    VivoxLogger.LogError(logMessage);
                    break;
            }

#if UNITY_EDITOR
            string statusMessage = null;
            switch (level)
            {
                case StatusLevel.Warning:
                    statusMessage = "WARNING: " + message + $" error code: {m_tapId}";;
                    break;
                case StatusLevel.Error:
                    statusMessage = "FAILED: " + message;
                    break;
                default:
                    statusMessage = message;
                    break;
            }
            if (statusMessage != null)
            {
                Status = statusMessage;
            }
#endif // UNITY_EDITOR
        }

        /// <summary>
        /// Extracts the channel name from a channel URI
        /// Handles the following pattern:
        ///    sip:confctl-{channelType}-{issuer}.{channelName}{.optionalUnityEnvId}{!p-optional3dProperties}@realm{.something}.com
        /// For example:
        ///    sip:confctl-d-12345-tanks-11111-test.lobbyChannel.111111-2222-3333-4444-88888888888!p-60-1-1.000-1@mt1111.vivox.com
        /// </summary>
        private string GetChannelNameFromUri(string channelUriToParse)
        {
            return String.IsNullOrWhiteSpace(channelUriToParse) ? string.Empty : new ChannelId(channelUriToParse).Name;
        }

        private void UnregisterTap()
        {
            // Check for valid tap id
            if (m_tapId > VxapTapUnRegistered)
            {
                VivoxLogger.LogVerbose($"Unregistering {Identifier} tap with ID: {m_tapId}");
#if UNITY_EDITOR
                MaxDataIn = 0;
                MaxDataOut = 0;
#endif // UNITY_EDITOR
                var result = AudioTapBridge.UnregisterTap(m_tapId);

                if (result < 0)
                {
                    VivoxLogger.LogWarning($"Failed to unregister {Identifier} Tap {m_tapId}, error code: {result}");
#if UNITY_EDITOR
                    Status = $"FAILED: to unregister tap, error code: {result}";
#endif // UNITY_EDITOR
                }
#if UNITY_EDITOR
                else
                {
                    Status = $"Tap {m_tapId} unregistered";
                }
#endif // UNITY_EDITOR
            }

            m_tapId = VxapTapUnRegistered; // Tap ID can be reset to unregistered regardless of whether there was a valid Tap ID or an error in the Tap ID field

            StopCoroutine(ProcessAudio());

            m_AudioProcessor.Stop();

            UpdateStatus();
        }

        private void OnDisable()
        {
            UnregisterTap();
            m_isEnabled = false;
        }

        private void OnDestroy()
        {
            m_AudioProcessor.UninitializeAudioConfiguration();

            if (VivoxService.Instance != null)
            {
                VivoxService.Instance.ChannelLeft -= OnChannelLeft;
                VivoxService.Instance.ChannelJoined -= OnChannelJoined;
            }
        }

        internal IEnumerator ProcessAudio()
        {
            while (true)
            {
                if (!IsReadyForFilterCalls())
                {
                    yield return m_cachedWaitForSecondRealtime;
                    continue;
                }

                var maxDataOut = m_AudioProcessor.ProcessAudio();
#if UNITY_EDITOR
                MaxDataOut = maxDataOut;
#endif

                yield return m_cachedWaitForSecondRealtime;
            }
        }

        private void ActivateRetry()
        {
            if (!m_RetryIsActive)
            {
                InvokeRepeating("CheckVivoxService", 0, 1);
                m_RetryIsActive = true;
            }
        }

        private void CancelRetry()
        {
            if (m_RetryIsActive)
            {
                CancelInvoke("CheckVivoxService");
                m_RetryIsActive = false;
            }
        }
    }
}
