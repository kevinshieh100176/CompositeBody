using System;
using UnityEngine;
using Unity.Services.Vivox;

namespace Unity.Services.Vivox.AudioTaps
{
    /// <summary>
    /// An Audio Tap which provides all voice activity coming from remote players in an active voice channel.
    /// </summary>
    [AddComponentMenu("Audio/Vivox Channel Audio Tap")]
    #if UNITY_2021_2_OR_NEWER
    [Icon("Packages/com.unity.services.vivox/Runtime/AudioTaps/icons/channel_audio.png")]
    #endif
    public class VivoxChannelAudioTap : VivoxAudioTap
    {
        private const string identifier = "VivoxChannelAudio";

        internal override string Identifier => identifier;

#if UNITY_EDITOR
        internal override float MeterMaxData => MaxDataOut;
#endif

        VivoxChannelAudioTap()
        {
            ChannelCount = 2;
        }

        internal override int RegisterTap(string channelUri)
        {
            return AudioTapBridge.RegisterTapForChannelAudio(80000, channelUri);
        }

        internal override int DoAudioFilterRead(int tapId, float[] data, int numFrames, int numChannels, int sampleRate)
        {
            return AudioTapBridge.GetChannelAudio(tapId, data, numFrames, numChannels, sampleRate);
        }
    }
}
