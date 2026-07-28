namespace Unity.Services.Vivox
{
    /// <summary>
    /// A unified selection of output streams and mechanisms for text-to-speech (TTS) injection.
    /// </summary>
    public enum TextToSpeechMessageType
    {
        /// <summary>
        /// Immediately send to participants in connected sessions (according to the transmission policy). Mixes new messages with any other ongoing messages.
        /// </summary>
        RemoteTransmission = vx_tts_destination.tts_dest_remote_transmission,
        /// <summary>
        /// Immediately play back locally on a render device (for example, speaker hardware). Mixes new messages with any other ongoing messages.
        /// </summary>
        LocalPlayback = vx_tts_destination.tts_dest_local_playback,
        /// <summary>
        /// Immediately play back locally on a render device and send to participants in connected sessions (according to the transmission policy). Mixes new messages with any other ongoing messages.
        /// </summary>
        RemoteTransmissionWithLocalPlayback = vx_tts_destination.tts_dest_remote_transmission_with_local_playback,
        /// <summary>
        /// Send to participants in connected sessions, or enqueue if there is already an ongoing message playing in this destination.
        /// </summary>
        QueuedRemoteTransmission = vx_tts_destination.tts_dest_queued_remote_transmission,
        /// <summary>
        /// Play back locally on a render device (for example, speaker hardware), or enqueue if there is already an ongoing message playing in this destination.
        /// </summary>
        QueuedLocalPlayback = vx_tts_destination.tts_dest_queued_local_playback,
        /// <summary>
        /// Play back locally on a render device and send to participants in connected sessions. Enqueue if there is already an ongoing message playing in this destination.
        /// </summary>
        QueuedRemoteTransmissionWithLocalPlayback = vx_tts_destination.tts_dest_queued_remote_transmission_with_local_playback,
        /// <summary>
        /// Immediately play back locally on a render device (for example, speaker hardware). Replaces the currently playing message in this destination.
        /// </summary>
        ScreenReader = vx_tts_destination.tts_dest_screen_reader
    }
}
