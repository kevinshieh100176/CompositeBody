using System.Threading.Tasks;

namespace Unity.Services.Vivox
{
    interface IVivoxAudioDevice
    {
        /// <summary>
        /// User-friendly device name of a device.
        /// </summary>
        string DeviceName { get; }
        /// <summary>
        /// The underlying ID of the device.
        /// </summary>
        string DeviceID { get; }

        /// <summary>
        /// Sets this device as the active input or output device.
        /// </summary>
        /// <returns>Task for the operation.</returns>
        Task SetActiveDeviceAsync();
    }
}
