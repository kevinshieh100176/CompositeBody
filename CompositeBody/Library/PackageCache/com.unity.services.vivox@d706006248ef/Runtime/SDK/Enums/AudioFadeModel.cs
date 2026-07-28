namespace Unity.Services.Vivox
{
    /// <summary>
    /// The distance model for a positional channel, which determines the algorithm to use when computing attenuation.
    /// </summary>
    public enum AudioFadeModel
    {
        /// <summary>
        /// Fades voice quickly at first, buts slows down as you get further from conversational distance.
        /// </summary>
        InverseByDistance = 1,
        /// <summary>
        /// Fades voice slowly at first, but speeds up as you get further from conversational distance.
        /// </summary>
        LinearByDistance = 2,
        /// <summary>
        /// Voice within conversational distance is louder, but fades quickly beyond it.
        /// </summary>
        ExponentialByDistance = 3
    }
}
