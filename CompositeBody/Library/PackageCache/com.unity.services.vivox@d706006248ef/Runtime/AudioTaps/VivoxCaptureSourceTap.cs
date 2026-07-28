using System;
using UnityEngine;

namespace Unity.Services.Vivox.AudioTaps
{
    /// <summary>
    /// An Audio Tap which provides the local player's microphone audio, captured by Vivox, while actively in a voice channel.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Audio/Vivox Capture Source Tap")]
    #if UNITY_2021_2_OR_NEWER
    [Icon("Packages/com.unity.services.vivox/Runtime/AudioTaps/icons/capture_source.png")]
    #endif
    public class VivoxCaptureSourceTap : VivoxAudioTap
    {
        private const string identifier = "VivoxCaptureSourceTap";

#if UNITY_EDITOR
        internal override float MeterMaxData => MaxDataOut;
#endif

        internal override string Identifier => identifier;

        VivoxCaptureSourceTap()
        {
            ChannelCount = 1;
        }

        internal override int RegisterTap(string channelUri)
        {
            return AudioTapBridge.RegisterTapForCaptureSource(80000, channelUri);
        }

        internal override int DoAudioFilterRead(int tapId, float[] data, int numFrames, int numChannels, int sampleRate)
        {
            return AudioTapBridge.GetCaptureSourceAudio(tapId, data, numFrames, numChannels, sampleRate);
        }
    }
}
