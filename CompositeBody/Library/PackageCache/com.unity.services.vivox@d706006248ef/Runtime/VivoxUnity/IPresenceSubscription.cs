namespace Unity.Services.Vivox
{
    /// <summary>
    /// A subscription to see another user's online status.
    /// </summary>
    internal interface IPresenceSubscription : IKeyedItemNotifyPropertyChanged<AccountId>
    {
        /// <summary>
        /// The account that this subscription pertains to.
        /// </summary>
        AccountId SubscribedAccount { get; }
        /// <summary>
        /// If the account associated with this subscription is signed in, then the Locations lists show an entry for each location and login session for that user.
        /// </summary>
        IReadOnlyDictionary<string, IPresenceLocation> Locations { get; }
    }
}
