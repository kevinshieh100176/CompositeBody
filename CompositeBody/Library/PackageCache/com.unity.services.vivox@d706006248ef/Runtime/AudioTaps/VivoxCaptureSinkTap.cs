#if VIVOX_ENABLE_CAPTURE_SINK_TAP
using UnityEditor;
using UnityEngine;

namespace Unity.Services.Vivox.AudioTaps
{
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Audio/Vivox Capture Sink Tap")]
#if UNITY_2021_2_OR_NEWER
    [Icon("Packages/com.unity.services.vivox/Runtime/AudioTaps/icons/capture_sink.png")]
#endif
    public class VivoxCaptureSinkTap : VivoxAudioTap
    {
        private const string identifier = "VivoxCaptureSinkTap";

        internal override string Identifier => identifier;

#if UNITY_EDITOR
        public override float MeterMaxData => MaxDataIn;
#endif

        public VivoxCaptureSinkTap()
        {
            ChannelCount = 1;
        }

        public bool m_DisableAudioOut = true;

        protected override int RegisterTap(string channelUri)
        {
            return AudioTapBridge.RegisterTapForCaptureSink(80000, channelUri);
        }

        internal override int DoAudioFilterRead(int tapId, float[] data, int numFrames, int numChannels, int sampleRate)
        {
            var res = AudioTapBridge.PutCaptureSinkAudio(tapId, data, numFrames, numChannels, sampleRate);

            if (m_DisableAudioOut)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = 0;
                }
            }

            return res;
        }
    }
}
#endif
