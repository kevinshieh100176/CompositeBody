using System;
using UnityEngine;

namespace Unity.Services.Vivox.AudioTaps
{
    /// <summary>
    /// An Audio Tap which provides a specific player’s audio as it is received from the network, isolated from other participant audio.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Audio/Vivox Participant Tap")]
#if UNITY_2021_2_OR_NEWER
    [Icon("Packages/com.unity.services.vivox/Runtime/AudioTaps/icons/tap_participant.png")]
#endif
    public class VivoxParticipantTap : VivoxAudioTap
    {
        // This needs to be serialized so that that the value is not lost when domain reload happens;
        // but we do not want to expose it to the user; so hide it in the inspector
        [HideInInspector]
        [SerializeField]
        private string m_ParticipantUri = string.Empty;

        [SerializeField]
        [Delayed]
        private string m_ParticipantName = string.Empty;
        private string m_LastParticipantName = string.Empty; // For detecting changes from the inspector view

        /// <summary>
        /// Account name of the participant whose audio you want to capture.
        /// </summary>
        public string ParticipantName
        {
            get => m_ParticipantName;
            set
            {
                if (m_LastParticipantName != value)
                {
                    VivoxLogger.LogVerbose($"ParticipantName setter being called {ParticipantName}, {m_ParticipantUri}, {value}");
                    m_ParticipantName = value;
                    m_LastParticipantName = value;

                    if (VivoxService.Instance != null)
                    {
                        m_ParticipantUri = ((IVivoxServiceInternal)VivoxService.Instance).GetParticipantUriByName(m_ParticipantName);
                    }
                    else
                    {
                        m_LastParticipantName = null; // allow this logic to revalidate once the service is available
                        m_ParticipantUri = null;
                    }

                    VivoxLogger.LogVerbose($"{Identifier}, Participant name and Participant URI changed to {ParticipantName} and {m_ParticipantUri}");
                    TryEnsureTapRegistration();
                }
            }
        }

        [SerializeField]
        private bool m_SilenceInChannelAudioMix = true;
        private bool m_LastSilenceInChannelAudioMix = true; // For detecting changes from the inspector view

        /// <summary>
        /// If true, the participant’s audio will be silenced in the channel audio mix output.
        /// </summary>
        public bool SilenceInChannelAudioMix
        {
            get => m_SilenceInChannelAudioMix;
            set
            {
                if (m_LastSilenceInChannelAudioMix != value)
                {
                    m_SilenceInChannelAudioMix = value;
                    m_LastSilenceInChannelAudioMix = value;
                    if (IsRunning)
                    {
                        RedoTapRegistration();
                    }
                }
            }
        }

        internal override void OnValidate()
        {
            base.OnValidate();
            ParticipantName = m_ParticipantName;
            SilenceInChannelAudioMix = m_SilenceInChannelAudioMix;
        }

        private const string identifier = "VivoxParticipantTap";

        internal override string Identifier => identifier;

#if UNITY_EDITOR
        internal override float MeterMaxData => MaxDataOut;
#endif

        VivoxParticipantTap()
        {
            ChannelCount = 1;
            AutoAcquireChannel = false;
        }

        internal override int RegisterTap(string channelUri)
        {
            if (string.IsNullOrWhiteSpace(m_ParticipantUri) && !string.IsNullOrWhiteSpace(m_ParticipantName))
            {
                VivoxLogger.LogError($"VivoxParticipantTap RegisterTap called with an unknown associated particiapnt");
                return VxapErrorAudioParticipantUnknown;
            }
            else if (string.IsNullOrWhiteSpace(m_ParticipantUri))
            {
                VivoxLogger.LogError($"VivoxParticipantTap RegisterTap called without an associated participant");
                return VxapErrorAudioParticipantUriEmpty;
            }

            var tapId = AudioTapBridge.RegisterTapForParticipantAudio(80000, m_ParticipantUri, channelUri, m_SilenceInChannelAudioMix);

            if (tapId < 0)
            {
                VivoxLogger.LogError($"Failed to register {Identifier} Tap with channel name {ChannelName}, participant name {ParticipantName}");
            }
            else
            {
                VivoxLogger.Log($"New tap id for {Identifier} tap: {tapId} with channel name {ChannelName}, participant name {ParticipantName}");
            }

            return tapId;
        }

        internal override int DoAudioFilterRead(int tapId, float[] data, int numFrames, int numChannels, int sampleRate)
        {
            return AudioTapBridge.GetParticipantAudio(tapId, data, numFrames, numChannels, sampleRate);
        }
    }
}
