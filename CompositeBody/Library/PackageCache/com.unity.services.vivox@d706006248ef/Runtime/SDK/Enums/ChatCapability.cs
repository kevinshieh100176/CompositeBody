namespace Unity.Services.Vivox
{
    /// <summary>
    /// Used to indicate what type of channel a user wants to join.
    /// </summary>
    public enum ChatCapability
    {
        ///<summary>
        ///Channel will only have access to Vivox Text
        ///</summary>
        TextOnly,
        ///<summary>
        ///Channel will only have access to Vivox Audio
        ///</summary>
        AudioOnly,
        ///<summary>
        ///Channel will have access to Vivox Text and Audio
        ///</summary>
        TextAndAudio
    };
}
