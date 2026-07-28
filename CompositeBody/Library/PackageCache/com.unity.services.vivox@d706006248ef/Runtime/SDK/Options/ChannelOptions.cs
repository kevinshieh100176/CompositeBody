namespace Unity.Services.Vivox
{
    /// <summary>
    /// Options to set behaviour on a channel join - like making the channel the Active channel being spoken into,
    /// or having Vivox log in automatically without a seperate LoginAsync call
    /// </summary>
    public sealed class ChannelOptions
    {
        /// <summary>
        /// Makes the current channel being joined the active channel being spoken into.
        /// </summary>
        public bool MakeActiveChannelUponJoining { get; set; }

        /// <summary>
        /// When true, the SDK will mark the channel as a "large text" channel when joining.
        /// Large text channels allow a significantly higher number of text participants (for example, up to ~2000 users
        /// instead of the default ~200) and require server-side support and entitlement. This is an enterprise-only
        /// feature; to enable it for your project, contact Vivox customer support or your Vivox representative.
        ///
        /// If enabled, the SDK will ensure the channel name used for the join includes the special "(t-largetext)"
        /// suffix (appending it if it is not already present). The SDK surface will continue to expose the
        /// human-friendly channel name (the suffix is removed for display/lookup); the large text designation is
        /// handled internally by the SDK and the Vivox service.
        /// </summary>
        public bool IsLargeText { get; set; }
    }
}
