using System.Threading.Tasks;

namespace Unity.Services.Vivox
{
    ///<summary>
    /// An Output AudioDevice on the device connecting to Vivox.
    /// Can be a physical audio device, like a set of headphones,
    /// or an abstraction, like the device's default communication audio device.
    ///</summary>
    public sealed class VivoxOutputDevice : IVivoxAudioDevice
    {
        readonly IVivoxServiceInternal m_ParentVivoxServiceInstance;
        internal IAudioDevice m_parentDevice;

        /// <summary>
        /// The name of the device
        /// </summary>
        public string DeviceName => m_parentDevice.Name;
        /// <summary>
        /// The ID of the device
        /// </summary>
        public string DeviceID => m_parentDevice.Key;

        internal VivoxOutputDevice(IVivoxServiceInternal vivoxServiceInstance, IAudioDevice parentDevice)
        {
            m_ParentVivoxServiceInstance = vivoxServiceInstance;
            m_parentDevice = parentDevice;
        }

        ///<summary>
        /// Set this Input Device to be the active Vivox Input Device
        ///</summary>
        /// <returns> A task for the operation </returns>
        public async Task SetActiveDeviceAsync()
        {
            await m_ParentVivoxServiceInstance.SetActiveOutputDeviceAsync(this);
        }
    }
}
