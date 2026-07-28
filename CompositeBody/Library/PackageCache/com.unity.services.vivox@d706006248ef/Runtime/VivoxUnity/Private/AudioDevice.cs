using System.ComponentModel;

namespace Unity.Services.Vivox
{
    internal class AudioDevice : IAudioDevice
    {
        public string Name { get; set; }
        public string Key { get; set; }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            if (GetType() != obj.GetType()) return false;

            return Equals((AudioDevice)obj);
        }

        protected bool Equals(AudioDevice other)
        {
            return string.Equals(Name, other.Name) && string.Equals(Key, other.Key);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name?.GetHashCode() ?? 0) * 397) ^ (Key?.GetHashCode() ?? 0);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
