namespace Unity.Services.Vivox
{
    /// <summary>
    /// Define the policy for where microphone and injected audio are broadcast to.
    /// </summary>
    public enum TransmissionMode
    {
        /// <summary>
        /// Adopts a policy of transmission into no channels.
        /// </summary>
        None,
        /// <summary>
        /// Adopts a policy of transmission into one channel at a time.
        /// </summary>
        Single,
        /// <summary>
        /// Adopts a policy of transmission into all channels simultaneously.
        /// </summary>
        All
    }
}
