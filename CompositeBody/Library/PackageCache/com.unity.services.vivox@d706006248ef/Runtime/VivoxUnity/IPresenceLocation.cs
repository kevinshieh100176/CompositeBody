namespace Unity.Services.Vivox
{
    /// <summary>
    /// Presence information for a user signed in at a particular location.
    /// </summary>
    internal interface IPresenceLocation : IKeyedItemNotifyPropertyChanged<string>
    {
        /// <summary>
        /// The unique identifier for this account's specific login session. This does not change and does not raise a PropertyChangedEvent.
        /// </summary>
        string LocationId { get; }
        /// <summary>
        /// The presence for this account at this location. When changed, this raises a PropertyChangedEvent.
        /// </summary>
        Presence CurrentPresence { get; }
        /// <summary>
        /// The subscription that owns this presence location. This does not change and does not raise a PropertyChangedEvent.
        /// </summary>
        IPresenceSubscription Subscription { get; }
    }
}
