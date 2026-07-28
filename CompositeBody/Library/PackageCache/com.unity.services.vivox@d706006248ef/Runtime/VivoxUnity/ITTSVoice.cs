namespace Unity.Services.Vivox
{
    /// <summary>
    /// A voice used by the text-to-speech (TTS) subsystem to synthesize speech.
    /// </summary>
    internal interface ITTSVoice : IKeyedItemNotifyPropertyChanged<uint>
    {
        /// <summary>
        /// A user-displayable voice name.
        /// </summary>
        string Name { get; }
    }
}
