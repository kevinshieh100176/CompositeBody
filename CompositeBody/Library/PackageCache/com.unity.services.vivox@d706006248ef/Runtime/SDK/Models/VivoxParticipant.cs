using System.ComponentModel;
using System;
using UnityEngine;
using System.Runtime.CompilerServices;
using Unity.Services.Vivox.AudioTaps;

[assembly: InternalsVisibleTo("Unity.Services.Vivox.AudioTaps")]
namespace Unity.Services.Vivox
{
    /// <summary>
    /// Representation of a player that is in a Vivox voice and/or text channel.
    /// </summary>
    public sealed class VivoxParticipant
    {
        readonly IVivoxServiceInternal m_ParentVivoxServiceInstance;
        IParticipant m_ParentParticipant;
        GameObject m_ParticipantTapGameObject;

        internal VivoxParticipant(IVivoxServiceInternal vivoxServiceInstance, IParticipant participant)
        {
            m_ParentVivoxServiceInstance = vivoxServiceInstance;
            m_ParentParticipant = participant;

            m_ParentParticipant.PropertyChanged += OnPropertyChanged;
            m_ParentParticipant.ParentChannelSession.Participants.BeforeKeyRemoved += OnParticipantRemoved;
            m_ParentVivoxServiceInstance.LoggedOut += DestroyTap;

            if (m_ParentParticipant.IsSelf)
            {
                m_ParentVivoxServiceInstance.UserInputDeviceMuteStateChanged += OnUserInputDeviceMuteStateChanged;
            }
        }

        void OnParticipantRemoved(object sender, KeyEventArg<string> keyEventArg)
        {
            var source = (IReadOnlyDictionary<string, IParticipant>)sender;
            IParticipant participantBeingRemoved = source[keyEventArg.Key];
            // If the unique ID of the participant being removed matches this participant object's, unbind events and cleanup.
            if (participantBeingRemoved.Account.Name == PlayerId)
            {
                Cleanup();
            }
        }

        void DestroyTap()
        {
            DestroyVivoxParticipantTap();
            m_ParentVivoxServiceInstance.LoggedOut -= DestroyTap;
        }

        internal void Cleanup()
        {
            if (m_ParentParticipant == null)
            {
                VivoxLogger.LogVerbose($"VivoxParticipant {PlayerId} already cleaned up.");
                return;
            }

            VivoxLogger.LogVerbose($"Cleaning up VivoxParticipant for PlayerID: {PlayerId} in channel {ChannelName}.");

            if (m_ParentVivoxServiceInstance != null)
            {
                m_ParentVivoxServiceInstance.UserInputDeviceMuteStateChanged -= OnUserInputDeviceMuteStateChanged;
                m_ParentVivoxServiceInstance.LoggedOut -= DestroyTap;
            }
            if (m_ParentParticipant != null)
            {
                m_ParentParticipant.PropertyChanged -= OnPropertyChanged;
                if (m_ParentParticipant.ParentChannelSession?.Participants != null)
                {
                    m_ParentParticipant.ParentChannelSession.Participants.BeforeKeyRemoved -= OnParticipantRemoved;
                }
            }

            ParticipantMuteStateChanged = null;
            ParticipantSpeechDetected = null;
            ParticipantAudioEnergyChanged = null;
            ParticipantAudioStateChanged = null;
            ParticipantTextStateChanged = null;
            DestroyVivoxParticipantTap();
            m_ParentParticipant = null;
        }

        void OnUserInputDeviceMuteStateChanged(bool isMuted)
        {
            ParticipantMuteStateChanged?.Invoke();
        }

        void OnPropertyChanged(object obj, PropertyChangedEventArgs args)
        {
            switch (args.PropertyName)
            {
                case "SpeechDetected":
                    ParticipantSpeechDetected?.Invoke();
                    break;
                case "AudioEnergy":
                    ParticipantAudioEnergyChanged?.Invoke();
                    break;
                case "LocalMute":
                    if (!IsSelf)
                    {
                        ParticipantMuteStateChanged?.Invoke();
                    }
                    break;
                case "IsMutedForAll":
                    ParticipantMuteStateChanged?.Invoke();
                    break;
                case "InAudio":
                    ParticipantAudioStateChanged?.Invoke();
                    break;
                case "InText":
                    ParticipantTextStateChanged?.Invoke();
                    break;
            }
        }

        /// <summary>
        /// Sets the volume of a participant for the local user only in the channel associated with this participant.
        /// In order to adjust the volume of this player across multiple channels you will need to iterate over <see cref="IVivoxService.ActiveChannels"/>.
        /// Similar participants will have a matching <see cref="VivoxParticipant.PlayerId"/> as the current <see cref="VivoxParticipant"/>.
        /// This only affects the audio that is heard locally, and does not change the audio that is heard by any of the other participants in a channel.
        /// Volume value is clamped between -50 and 50 with a default of 0.
        /// </summary>
        /// <param name="volume"> The value to set the participant to for the local user - clamped between -50 and 50 with a default of 0</param>
        public void SetLocalVolume(int volume)
        {
            if (!ValidateParticipant())
            {
                return;
            }

            if (!IsSelf)
            {
                m_ParentParticipant.LocalVolumeAdjustment = Mathf.Clamp(volume, -50, 50);
            }
        }

        /// <summary>
        /// Sets this participant as muted for the local user only in the channel associated with this participant.
        /// In order to mute this player across multiple channels you will need to iterate over <see cref="IVivoxService.ActiveChannels"/>
        /// and leverage this method on the additional <see cref="VivoxParticipant"/> objects that match this instance's <see cref="VivoxParticipant.PlayerId"/>.
        /// </summary>
        public void MutePlayerLocally()
        {
            if (!ValidateParticipant())
            {
                return;
            }

            SetLocalMuteState(true);
        }

        /// <summary>
        /// Sets this participant as unmuted for the local user only in the channel associated with this participant.
        /// In order to unmute this player across multiple channels you will need to iterate over <see cref="IVivoxService.ActiveChannels"/>
        /// and leverage this method on the additional <see cref="VivoxParticipant"/> objects that match this instance's <see cref="VivoxParticipant.PlayerId"/>.
        /// </summary>
        public void UnmutePlayerLocally()
        {
            if (!ValidateParticipant())
            {
                return;
            }

            SetLocalMuteState(false);
        }

        /// <summary>
        /// Wrapper for locally muting or unmuting a player.
        /// doMute = true locally mutes a player, doMute = false locally unmutes a player.
        /// </summary>
        internal void SetLocalMuteState(bool doMute)
        {
            if (IsSelf)
            {
                if (doMute)
                {
                    m_ParentVivoxServiceInstance.MuteInputDevice();
                }
                else
                {
                    m_ParentVivoxServiceInstance.UnmuteInputDevice();
                }
                return;
            }
            m_ParentParticipant.LocalMute = doMute;
        }

        bool ValidateParticipant([CallerMemberName] string memberName = "")
        {
            if (m_ParentParticipant == null)
            {
                VivoxLogger.LogException(
                    new InvalidOperationException(
                        $"Unable to call {nameof(VivoxParticipant)}.{memberName} because this participant has been removed from the channel. " +
                        $"If this is a cached reference, please update the reference when receiving an update from our IVivoxService.{nameof(IVivoxService.ParticipantRemovedFromChannel)} event."
                    )
                );

                return false;
            }
            return true;
        }

        /// <summary>
        /// Creates a GameObject containing the VivoxParticipantTap and AudioSource for this VivoxParticipant
        /// </summary>
        /// <param name="gameObjectName">Optional name to give to the created GameObject</param>
        /// <param name="silenceInChannelAudioMix">Whether to mute this Participant in the Channel Audio Mix output (defaults to true)</param>
        /// <returns>The built GameObject with the <see cref="VivoxParticipantTap"/>. This will return null if the creation fails for any reason.</returns>
        public GameObject CreateVivoxParticipantTap(string gameObjectName = "", bool silenceInChannelAudioMix = true)
        {
            if (!ValidateParticipant())
            {
                return null;
            }

            if (IsSelf)
            {
                VivoxLogger.LogWarning("Creating a VivoxParticipantTap for the local user is not supported.");
                return null;
            }

            if (m_ParticipantTapGameObject != null)
            {
                VivoxLogger.LogWarning($"Participant \"{DisplayName}-{PlayerId}\" in channel \"{ChannelName}\" already has an active Audio Tap.");
                return m_ParticipantTapGameObject;
            }

            var tapObjectName = string.IsNullOrEmpty(gameObjectName)
                ? $"VivoxParticipantAudioTap: {DisplayName}-{PlayerId}"
                : gameObjectName;
            m_ParticipantTapGameObject = new GameObject(tapObjectName);
            m_ParticipantTapGameObject.SetActive(false);

            var tap = m_ParticipantTapGameObject.AddComponent<VivoxParticipantTap>();
            tap.AutoAcquireChannel = false;
            tap.ChannelName = ChannelName;
            tap.ParticipantName = PlayerId;
            tap.SilenceInChannelAudioMix = silenceInChannelAudioMix;
            ParticipantTapAudioSource = tap.GetComponent<AudioSource>();
            m_ParticipantTapGameObject.SetActive(true);

            return m_ParticipantTapGameObject;
        }

        /// <summary>
        /// Destroys the GameObject containing the VivoxParticipantTap for this VivoxParticipant and sets the AudioSource to null
        /// </summary>
        public void DestroyVivoxParticipantTap()
        {
            if (m_ParticipantTapGameObject != null)
            {
                ParticipantTapAudioSource = null;
                UnityEngine.Object.Destroy(m_ParticipantTapGameObject);
                m_ParticipantTapGameObject = null;
            }
        }

        /// <summary>
        /// The DisplayName of the participant.
        /// </summary>
        public string DisplayName => m_ParentParticipant.Account.DisplayName;
        /// <summary>
        /// The PlayerId and unique identifier of a Vivox channel participant.
        /// This will be either a Unity Authentication Service PlayerId if the Authentication package is in use, or a unique GUID assigned by Vivox during the account creation process.
        /// </summary>
        public string PlayerId => m_ParentParticipant.Account.Name;
        /// <summary>
        /// The Vivox universal resource identifier of this participant.
        /// </summary>
        public string URI => m_ParentParticipant.Account.ToString();
        /// <summary>
        /// The unique identifier of the channel that this participant is associated with.
        /// </summary>
        public string ChannelName => m_ParentParticipant.ParentChannelSession.Channel.Name;
        /// <summary>
        /// The universal resource identifier of the channel this participant is associated with.
        /// </summary>
        public string ChannelURI => m_ParentParticipant.ParentChannelSession.Key.ToString();
        /// <summary>
        /// Whether or not the AudioEnergy has surpassed the threshold to be considered speech.
        /// </summary>
        public bool SpeechDetected => m_ParentParticipant.SpeechDetected;
        /// <summary>
        /// Whether or not this participant is muted.
        /// </summary>
        public bool IsMuted => IsSelf ? m_ParentVivoxServiceInstance.Client.AudioInputDevices.Muted : (m_ParentParticipant.LocalMute || m_ParentParticipant.IsMutedForAll);
        /// <summary>
        /// Whether or not this participant is the logged in user.
        /// </summary>
        public bool IsSelf => m_ParentParticipant.IsSelf;
        /// <summary>
        /// Whether or not this participant is connected to channel audio.
        /// </summary>
        /// <remarks>
        /// This value may read as <c>false</c> immediately after a participant joins and will be
        /// corrected once the first participant update is received from the server. Subscribe to
        /// <see cref="ParticipantAudioStateChanged"/> to be notified when this value changes.
        /// </remarks>
        public bool IsInAudio => m_ParentParticipant.InAudio;
        /// <summary>
        /// Whether or not this participant is connected to channel text.
        /// </summary>
        /// <remarks>
        /// This value may read as <c>false</c> immediately after a participant joins and will be
        /// corrected once the first participant update is received from the server. Subscribe to
        /// <see cref="ParticipantTextStateChanged"/> to be notified when this value changes.
        /// </remarks>
        public bool IsInText => m_ParentParticipant.InText;
        /// <summary>
        /// The volume of a participant only for the local user in a given channel.
        /// </summary>
        public int LocalVolume => m_ParentParticipant.LocalVolumeAdjustment;
        /// <summary>
        /// The AudioEnergy of the participant.
        /// This can be used to create a voice activity meter for participants in a channel.
        /// </summary>
        public double AudioEnergy => m_ParentParticipant.AudioEnergy;
        /// <summary>
        /// The AudioSource component of a <see cref="VivoxParticipantTap"/> gameobject if one has been created.
        /// This will be null if there is no active <see cref="VivoxParticipantTap"/> associated with this participant object.
        /// </summary>
        public AudioSource ParticipantTapAudioSource { get; internal set; }

        /// <summary>
        /// An event that fires if the mute state of the participant changes.
        /// </summary>
        public event Action ParticipantMuteStateChanged;
        /// <summary>
        /// An event that fires if the Participant's speech detected status changes.
        /// </summary>
        public event Action ParticipantSpeechDetected;
        /// <summary>
        /// An event that fires if the Participants audio energy changes.
        /// </summary>
        public event Action ParticipantAudioEnergyChanged;
        /// <summary>
        /// An event that fires when this participant's audio connection state changes.
        /// </summary>
        public event Action ParticipantAudioStateChanged;
        /// <summary>
        /// An event that fires when this participant's text connection state changes.
        /// </summary>
        public event Action ParticipantTextStateChanged;
    }
}
