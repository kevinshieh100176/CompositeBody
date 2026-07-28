namespace Unity.Services.Vivox
{
    /// <summary>
    /// Profiles used for configuring how the SDK should handle Bluetooth audio.
    /// Note: These profile configurations currently only affect mobile devices.
    /// </summary>
    public enum BluetoothProfile
    {
        /// <summary>
        /// The Advanced Audio Distribution Profile (A2DP) profile defines how high-quality audio can be streamed from one device to another over a Bluetooth connection.
        /// It defines how multimedia audio can be streamed from one device to another over a Bluetooth connection (it is also called Bluetooth Audio Streaming).
        /// For example, music can be streamed from a mobile phone to a wireless headset, hearing aid/cochlear implant streamer, or car audio.
        /// A2DP is uni-directional.
        /// When using this profile, audio input will be captured by the mobile device's built-in microphone and audio output will be rendered to the connected Bluetooth device.
        /// </summary>
        A2DP = vx_bluetooth_profile.vx_bluetooth_profile_a2dp,
        /// <summary>
        /// The Bluetooth Hands-Free Profile (HFP) allows the Bluetooth device to make and receive voice calls via a connected handset.
        /// Synchronous Connection-Oriented (SCO) is the type of radio link used for voice data.
        /// When using this profile, audio will be captured by and rendered to the connected Bluetooth device.
        /// </summary>
        HFP = vx_bluetooth_profile.vx_bluetooth_profile_hfp,
    }
}
