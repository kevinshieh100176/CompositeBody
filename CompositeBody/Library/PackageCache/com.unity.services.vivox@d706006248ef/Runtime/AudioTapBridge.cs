
namespace Unity.Services.Vivox
{
    internal class AudioTapBridge
    {
        public static int RegisterTapForChannelAudio(uint bufferDuration, string channelUri)
        {
            return VivoxCoreInstance.vxunity_register_for_channel_audio(bufferDuration, channelUri);
        }

        public static int RegisterTapForCaptureSource(uint bufferDuration, string channelUri)
        {
            return VivoxCoreInstance.vxunity_register_for_capture_source(bufferDuration, channelUri);
        }

        public static int RegisterTapForCaptureSink(uint bufferDuration, string channelUri)
        {
            return VivoxCoreInstance.vxunity_register_for_capture_sink(bufferDuration, channelUri);
        }

        public static int RegisterTapForParticipantAudio(uint bufferDuration, string participantUri, string channelUri, bool silenceInFinalMix)
        {
            return VivoxCoreInstance.vxunity_register_for_participant_audio(bufferDuration, participantUri, channelUri, silenceInFinalMix);
        }

        public static int UnregisterTap(int tapId)
        {
            return VivoxCoreInstance.vxunity_unregister_tap(tapId);
        }

        public static int GetChannelAudio(int tapId, float[] data, int numFrames, int numChannels, int sampleRate)
        {
            return VivoxCoreInstance.vxunity_get_channel_audio_for_id(tapId, data, numFrames, numChannels, sampleRate);
        }

        public static int GetCaptureSourceAudio(int tapId, float[] data, int numFrames, int numChannels, int sampleRate)
        {
            return VivoxCoreInstance.vxunity_get_capture_audio_for_id(tapId, data, numFrames, numChannels, sampleRate);
        }

        public static int PutCaptureSinkAudio(int tapId, float[] data, int numFrames, int numChannels, int sampleRate)
        {
            return VivoxCoreInstance.vxunity_put_capture_audio_for_id(tapId, data, numFrames, numChannels, sampleRate);
        }

        public static int GetParticipantAudio(int tapId, float[] data, int numFrames, int numChannels, int sampleRate)
        {
            return VivoxCoreInstance.vxunity_get_participant_audio_for_id(tapId, data, numFrames, numChannels, sampleRate);
        }
    }
}