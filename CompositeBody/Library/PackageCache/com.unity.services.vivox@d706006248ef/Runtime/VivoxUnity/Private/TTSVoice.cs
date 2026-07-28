using System.ComponentModel;

namespace Unity.Services.Vivox
{
    internal class TTSVoice : ITTSVoice
    {
        public string Name { get; set; }
        public uint Key { get; set; }

        internal TTSVoice(vx_tts_voice_t voice)
        {
            Name = voice.name;
            Key = voice.voice_id;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            if (GetType() != obj.GetType()) return false;

            return Equals((TTSVoice)obj);
        }

        protected bool Equals(TTSVoice other)
        {
            return string.Equals(Name, other.Name) && uint.Equals(Key, other.Key);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name?.GetHashCode() ?? 0) * 397) ^ (Key.GetHashCode());
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
