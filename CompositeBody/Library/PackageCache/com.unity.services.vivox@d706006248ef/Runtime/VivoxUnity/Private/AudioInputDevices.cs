using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Unity.Services.Vivox
{
    internal class AudioInputDevices : IAudioDevices
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
        private int _audioGain;
        private bool _muted;
        private double _deviceEnergy;

        private readonly VxClient _client;
        private readonly ReadWriteDictionary<string, IAudioDevice, AudioDevice> _devices = new ReadWriteDictionary<string, IAudioDevice, AudioDevice>();

        #endregion

        #region Helpers

        int ConvertGain(int gain)
        {
            return gain + 50;
        }

        #endregion

        public AudioInputDevices(VxClient client)
        {
            _client = client;
            _defaultSystemDevice = new AudioDevice { Key = DefaultSystemDevice, Name = DefaultSystemDevice };
            _defaultCommunicationDevice = new AudioDevice { Key = DefaultCommunicationDevice, Name = DefaultCommunicationDevice };
            _currentSystemDevice = new AudioDevice { Key = DefaultSystemDevice, Name = DefaultSystemDevice };
            _currentCommunicationDevice = new AudioDevice { Key = DefaultCommunicationDevice, Name = DefaultCommunicationDevice };
            _activeDevice = _defaultSystemDevice;

            VxClient.Instance.EventMessageReceived += OnEventMessageReceived;
        }

        ~AudioInputDevices()
        {
            VxClient.Instance.EventMessageReceived -= OnEventMessageReceived;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public IAudioDevice SystemDevice => _defaultSystemDevice;
        public IAudioDevice CommunicationDevice => _defaultCommunicationDevice;
        public IAudioDevice ActiveDevice => _activeDevice;
        public IAudioDevice EffectiveDevice => _effectiveDevice;
        public IReadOnlyDictionary<string, IAudioDevice> AvailableDevices => _devices;
        public double DeviceEnergy => _deviceEnergy;

        public async Task SetActiveDeviceAsync(IAudioDevice device, AsyncCallback callback = null, string accountHandle = null)
        {
            await Task.Factory.FromAsync(
                BeginSetActiveDevice(device, callback, accountHandle),
                ar =>
                {
                    try
                    {
                        EndSetActiveDevice(ar);
                        return Task.CompletedTask;
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                });
        }

        public IAsyncResult BeginSetActiveDevice(IAudioDevice device, AsyncCallback callback, string accountHandle)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }

            if (device == null) throw new ArgumentNullException();

            AsyncNoResult result = new AsyncNoResult(callback);
            var request = new vx_req_aux_set_capture_device_t();
            request.account_handle = accountHandle;
            request.capture_device_specifier = device.Key;
            return _client.BeginIssueRequest(request, ar =>
            {
                try
                {
                    _client.EndIssueRequest(ar);

                    // When trying to set the active device to what is already the active device, return.
                    if (_activeDevice.Key == device.Key)
                    {
                        return;
                    }
                    _activeDevice = (AudioDevice)device;

                    if (_activeDevice == AvailableDevices[DefaultSystemDevice])
                    {
                        _effectiveDevice = new AudioDevice
                        {
                            Key = _currentSystemDevice.Key,
                            Name = _currentSystemDevice.Name
                        };
                    }
                    else if (_activeDevice == AvailableDevices[DefaultCommunicationDevice])
                    {
                        _effectiveDevice = new AudioDevice
                        {
                            Key = _currentCommunicationDevice.Key,
                            Name = _currentCommunicationDevice.Name
                        };
                    }
                    else
                    {
                        _effectiveDevice = new AudioDevice
                        {
                            Key = device.Key,
                            Name = device.Name
                        };
                    }
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveDevice)));

                    result.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    result.SetComplete(e);
                    throw;
                }
            });
        }

        public void EndSetActiveDevice(IAsyncResult result)
        {
            AsyncNoResult ar = result as AsyncNoResult;
            ar?.CheckForError();
        }

        public int VolumeAdjustment
        {
            get => _audioGain;
            set => AdjustVolume(value);
        }

        public bool Muted
        {
            get => _muted;
            set => Mute(value);
        }

        public IAsyncResult BeginRefresh(AsyncCallback callback)
        {
            if (VxClient.Instance.IsQuitting)
            {
                return null;
            }
            if (VxClient.PlatformNotSupported) return null;
            AsyncNoResult result = new AsyncNoResult(callback);
            var request = new vx_req_aux_get_capture_devices_t();
            _client.BeginIssueRequest(request, ar =>
            {
                vx_resp_aux_get_capture_devices_t response;
                try
                {
                    response = _client.EndIssueRequest(ar);
                    var oldDevices = new ReadWriteDictionary<string, IAudioDevice, AudioDevice>();
                    bool devicesChanged = false;
                    if (response.count != _devices.Count)
                    {
                        devicesChanged = true;
                    }
                    for (int i = 0; i < _devices.Count; i++)
                    {
                        oldDevices[_devices.Keys.ElementAt(i)] = _devices.ElementAt(i);
                    }
                    _devices.Clear();
                    for (var i = 0; i < response.count; ++i)
                    {
                        var device = VivoxCoreInstance.get_device(i, response.capture_devices);
                        var id = device.device;
                        var name = device.display_name;
                        var newDevice = new AudioDevice { Key = id, Name = name };
                        //if an id that didn't previously exist
                        if (!oldDevices.Contains(newDevice))
                        {
                            devicesChanged = true;
                        }
                        //If an id that did previously exist but the device has now changed (such as setting a new device name in system settings)
                        else if (!oldDevices[id].Equals(newDevice))
                        {
                            devicesChanged = true;
                        }
                        _devices[id] = newDevice;
                    }
                    if (devicesChanged)
                    {
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailableDevices)));
                    }
                    oldDevices.Clear();

                    var currentSystemDevice = new AudioDevice
                    {
                        Key = response.default_capture_device.device,
                        Name = response.default_capture_device.display_name
                    };
                    _currentCommunicationDevice = new AudioDevice
                    {
                        Key = response.default_communication_capture_device.device,
                        Name = response.default_communication_capture_device.display_name
                    };
                    var effectiveDevice = new AudioDevice
                    {
                        Key = response.effective_capture_device.device,
                        Name = response.effective_capture_device.display_name,
                    };
                    if (!effectiveDevice.Equals(_effectiveDevice))
                    {
                        // Only fire the event if the effective device has truly changed.
                        _effectiveDevice = effectiveDevice;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveDevice)));
                    }
                    if (!currentSystemDevice.Equals(_currentSystemDevice))
                    {
                        // Only fire the event if the system device has truly changed.
                        _currentSystemDevice = currentSystemDevice;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SystemDevice)));
                    }
                    result.SetComplete();
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    result.SetComplete(e);
                    throw;
                }
            });
            return result;
        }

        public void EndRefresh(IAsyncResult result)
        {
            (result as AsyncNoResult)?.CheckForError();
        }

        public async Task RefreshDevicesAsync(AsyncCallback cb)
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
                });
        }

        private void OnEventMessageReceived(vx_evt_base_t eventMessage)
        {
            if (eventMessage.type == vx_event_type.evt_audio_device_hot_swap)
            {
                HandleDeviceHotSwap(eventMessage);
            }
            if (eventMessage.type == vx_event_type.evt_aux_audio_properties)
            {
                HandleDeviceAudioProperties(eventMessage);
            }
        }

        private void HandleDeviceHotSwap(vx_evt_base_t eventMessage)
        {
            BeginRefresh(new AsyncCallback((IAsyncResult result) =>
            {
                try
                {
                    EndRefresh(result);
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"BeginRefresh failed: {e}");
                    throw;
                }
            }));
        }

        private void HandleDeviceAudioProperties(vx_evt_base_t eventMessage)
        {
            vx_evt_aux_audio_properties_t evt = (vx_evt_aux_audio_properties_t)eventMessage;
            _deviceEnergy = evt.mic_energy;
        }

        public void Clear()
        {
            _devices.Clear();
            _activeDevice = _defaultSystemDevice;
            _effectiveDevice = null;
            _muted = false;
            _audioGain = 0;
        }

        public void Mute(bool doMute, string accountHandle = null)
        {
            if (VxClient.Instance.IsQuitting || doMute == _muted)
            {
                return;
            }

            _muted = doMute;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Muted)));
            var request = new vx_req_connector_mute_local_mic_t
            {
                mute_level = doMute ? 1 : 0,
                account_handle = string.IsNullOrEmpty(accountHandle) ? string.Empty : accountHandle
            };
            _client.BeginIssueRequest(request, ar =>
            {
                try
                {
                    _client.EndIssueRequest(ar);
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    throw;
                }
            });
        }

        public void AdjustVolume(int volumeLevel, string accountHandle = null)
        {
            if (VxClient.Instance.IsQuitting || volumeLevel == _audioGain)
            {
                return;
            }

            if (volumeLevel < -50 || volumeLevel > 50)
            {
                throw new ArgumentOutOfRangeException();
            }

            _audioGain = volumeLevel;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VolumeAdjustment)));

            var request = new vx_req_aux_set_mic_level_t
            {
                level = ConvertGain(volumeLevel),
                account_handle = string.IsNullOrEmpty(accountHandle) ? string.Empty : accountHandle
            };
            _client.BeginIssueRequest(request, ar =>
            {
                try
                {
                    _client.EndIssueRequest(ar);
                }
                catch (Exception e)
                {
                    VivoxLogger.LogVxException($"{request.GetType().Name} failed: {e}");
                    throw;
                }
            });
        }
    }
}
