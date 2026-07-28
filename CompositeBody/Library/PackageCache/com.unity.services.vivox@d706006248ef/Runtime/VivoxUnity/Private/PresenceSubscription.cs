using System.ComponentModel;

namespace Unity.Services.Vivox
{
    internal class PresenceSubscription : IPresenceSubscription
    {
        private readonly ReadWriteDictionary<string, IPresenceLocation, PresenceLocation> _locations = new ReadWriteDictionary<string, IPresenceLocation, PresenceLocation>();
#pragma warning disable 0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore 0067
        public AccountId Key { get; set; }
        public IReadOnlyDictionary<string, IPresenceLocation> Locations => _locations;

        public void UpdateLocation(string uriWithTag, PresenceStatus status, string message)
        {
            PresenceLocation item;
            if (!_locations.ContainsKey(uriWithTag))
            {
                if (status != PresenceStatus.Unavailable)
                {
                    item = new PresenceLocation(uriWithTag)
                    {
                        CurrentPresence = new Presence(status, message),
                        Subscription = this
                    };
                    _locations[item.Key] = item;
                }
            }
            else
            {
                item = (PresenceLocation)_locations[uriWithTag];
                item.CurrentPresence = new Presence(status, message);
                if (status == PresenceStatus.Unavailable)
                {
                    _locations.Remove(uriWithTag);
                }
            }
        }

        public AccountId SubscribedAccount => Key;
    }
}
