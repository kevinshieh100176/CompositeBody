using System.ComponentModel;

namespace Unity.Services.Vivox
{
    internal class PresenceLocation : IPresenceLocation
    {
        private Presence _currentPresence;
        private string _location;
        public event PropertyChangedEventHandler PropertyChanged;

        public PresenceLocation(string key)
        {
            Key = key;
            _currentPresence = new Presence();
        }

        public string Key { get; }

        public Presence CurrentPresence
        {
            get { return _currentPresence; }
            set
            {
                if (!_currentPresence.Equals(value))
                {
                    _currentPresence = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPresence)));
                }
            }
        }

        public string Location
        {
            get { return _location; }
            set
            {
                if (_location != value)
                {
                    _location = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
                }
            }
        }

        public IPresenceSubscription Subscription { get; set; }
        public string LocationId => Key;
    }
}
