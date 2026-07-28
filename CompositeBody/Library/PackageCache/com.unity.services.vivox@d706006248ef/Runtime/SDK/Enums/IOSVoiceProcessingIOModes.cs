namespace Unity.Services.Vivox
{
    /// <summary>
    /// Possible values to set the iOS Voice Processing IO Mode to.
    /// Note: These configuration values are only valid on iOS
    /// </summary>
    public enum IosVoiceProcessingIOModes
    {
        /// <summary>
        /// Returned when there was an error fetching the current value of the VoiceProcessingIOMode.
        /// </summary>
        ErrorOccurred = -1,
        /// <summary>
        /// Never use the Voice Processing IO audio unit, no matter the audio configuration.
        /// </summary>
        Never = 0,
        /// <summary>
        /// Use the Voice Processing IO audio unit only while the speaker phone is in use.
        /// </summary>
        OnSpeakerPhone = 1,
        /// <summary>
        /// Default, always use the Voice Processing IO audio unit, no metter the audio configuration.
        /// </summary>
        Always = 2
    }
}
