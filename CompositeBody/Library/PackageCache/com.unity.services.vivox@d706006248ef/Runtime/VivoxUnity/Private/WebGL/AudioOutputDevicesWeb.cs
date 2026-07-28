#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Services.Vivox;

namespace Unity.Services.Vivox
{
    internal class AudioOutputDevicesWeb : IAudioDevices
    {
#region Member Variables
        private readonly string DefaultSystemDevice = "Default System Device";
        private readonly string DefaultCommunicationDevice = "Default Communication Device";
        private AudioDevice _defaultSystemDevice;
        private AudioDevice _defaultCommunicationDevice;
        private AudioDevice _currentSystemDevice;
        private AudioDevice _currentCommunicationDevice;
        private AudioDevice _activeDevice;
        private AudioDevice _effectiveDevice;
        private int _volumeAdjustment;
        private bool _muted;
        private double _deviceEnergy;

        private readonly VxClient _client;
        private readonly ReadWriteDictionary<string, IAudioDevice, AudioDevice> _devices = new ReadWriteDictionary<string, IAudioDevice, AudioDevice>();
        public double DeviceEnergy => _deviceEnergy;

#endregion

        [DllImport("__Internal")]
        private static extern void vx_setLocalRender(int isMuted);

        public AudioOutputDevicesWeb(VxClient client)
        {
            _client = client;
            _defaultSystemDevice = new AudioDevice { Key = DefaultSystemDevice, Name = DefaultSystemDevice };
            _defaultCommunicationDevice = new AudioDevice { Key = DefaultCommunicationDevice, Name = DefaultCommunicationDevice };
            _currentSystemDevice = new AudioDevice { Key = DefaultSystemDevice, Name = DefaultSystemDevice };
            _currentCommunicationDevice = new AudioDevice { Key = DefaultCommunicationDevice, Name = DefaultCommunicationDevice };
            _activeDevice = _defaultSystemDevice;

            VxClient.Instance.EventMessageReceived += OnEventMessageReceived;
        }

        ~AudioOutputDevicesWeb()
        {
            VxClient.Instance.EventMessageReceived -= OnEventMessageReceived;
        }

        public IAudioDevice SystemDevice => throw new NotImplementedException();

        public IReadOnlyDictionary<string, IAudioDevice> AvailableDevices => throw new NotImplementedException();

        public IAudioDevice ActiveDevice => throw new NotImplementedException();

        public IAudioDevice EffectiveDevice => throw new NotImplementedException();

        public int VolumeAdjustment { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool Muted
        {
            get { return _muted; }
            set
            {
                if (value == _muted) return;
                _muted = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Muted)));

                vx_setLocalRender((value ? 0 : 1));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public IAsyncResult BeginRefresh(AsyncCallback cb)
        {
            AsyncNoResult ar = new AsyncNoResult(cb);
            // In the future we can update the devices above by calling the browsers enumerateDevices();
            // https://developer.mozilla.org/en-US/docs/Web/API/MediaDevices/enumerateDevices
            ar.SetComplete();
            return ar;
        }

        public async Task RefreshDevicesAsync(AsyncCallback cb = null)
        {
            await Task.Factory.FromAsync(
                BeginRefresh(cb),
                ar =>
                {
                    try
                    {
                        EndRefresh(ar);
                        return Task.CompletedTask;
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                });        }

        public Task SetActiveDeviceAsync(IAudioDevice device, AsyncCallback callback = null, string accountHandle = null)
        {
            return null;
        }

        public IAsyncResult BeginSetActiveDevice(IAudioDevice device, AsyncCallback callback, string accountHandle = null)
        {
            AsyncNoResult ar = new AsyncNoResult(callback);
            // In the future we can set the devices above by calling the browsers selectAudioOutput()
            // https://developer.mozilla.org/en-US/docs/Web/API/MediaDevices/selectAudioOutput
            ar.SetComplete();
            return ar;
        }

        public void EndRefresh(IAsyncResult result)
        {
            return;
        }

        public void EndSetActiveDevice(IAsyncResult result)
        {
            return;
        }

        private void OnEventMessageReceived(vx_evt_base_t eventMessage)
        {
            VivoxLogger.Log("Output device - hot swap event received");
        }

        public void Mute(bool doMute, string accountHandle)
        {
            return;
        }

        public void AdjustVolume(int volumeLevel, string accountHandle)
        {
            return;
        }
    }
}
#endif
