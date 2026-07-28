namespace Unity.Services.Vivox
{
    /// <summary>
    /// Determine how often the SDK sends participant property events while in a channel.
    /// </summary>
    /// <remarks>
    /// By default, participant property events send on participant state change (for example, when a user starts talking, stops talking, is muted, or is unmuted). If set to a per second rate, messages send at that rate if there has been a change since the last update message.
    /// This is always true unless the participant is muted through the SDK, which causes no audio energy or state changes.
    ///
    /// Caution: Setting this to a non-default value increases user and server traffic.
    /// This should only be done if a real-time visual representation of audio values are needed (for example, a graphic VAD indicator).
    /// For a static VAD indicator, the default setting is correct.
    /// </remarks>
    public enum ParticipantPropertyUpdateFrequency
    {
        /// <summary>
        /// On participant state change (the default setting).
        /// </summary>
        StateChange = 100,
        /// <summary>
        /// Never update.
        /// </summary>
        Never = 0,
        /// <summary>
        /// 1 update per second.
        /// </summary>
        OnePerSecond = 50,
        /// <summary>
        /// 5 updated per second.
        /// </summary>
        FivePerSecond = 10,
        /// <summary>
        /// 10 updates per second.
        /// </summary>
        TenPerSecond = 5
    }
}
