namespace Unity.Services.Vivox
{
    internal class VivoxGlobalAudioSettings : IVivoxGlobalAudioSettings
    {
        /// <summary>
        /// Don't need to initialize any values because we are simply fetching/setting Core's values.
        /// </summary>
        internal VivoxGlobalAudioSettings()
        {
        }

        public bool NoiseSuppressionEnabled
        {
            get
            {
                VivoxCoreInstance.vx_get_noise_suppression_enabled(out var enabled);
                return enabled == 1;
            }
            set => VivoxCoreInstance.vx_set_noise_suppression_enabled(value ? 1 : 0);
        }

        public bool PlatformAcousticEchoCancellationEnabled
        {
            get
            {
                VivoxCoreInstance.vx_get_platform_aec_enabled(out var enabled);
                return enabled == 1;
            }
            set => VivoxCoreInstance.vx_set_platform_aec_enabled(value ? 1 : 0);
        }

        public bool VivoxAcousticEchoCancellationEnabled
        {
            get
            {
                VivoxCoreInstance.vx_get_vivox_aec_enabled(out var enabled);
                return enabled == 1;
            }
            set => VivoxCoreInstance.vx_set_vivox_aec_enabled(value ? 1 : 0);
        }

        public bool AutomaticGainControlEnabled
        {
            get
            {
                VivoxCoreInstance.vx_get_agc_enabled(out var enabled);
                return enabled == 1;
            }
            set => VivoxCoreInstance.vx_set_agc_enabled(value ? 1 : 0);
        }

        public bool DynamicVoiceProcessingSwitchingEnabled
        {
            get
            {
                VivoxCoreInstance.vx_get_dynamic_voice_processing_switching_enabled(out var enabled);
                return enabled == 1;
            }
            set => VivoxCoreInstance.vx_set_dynamic_voice_processing_switching_enabled(value ? 1 : 0);
        }

        public bool VolumeBasedDuplicationSuppressionEnabled
        {
            get
            {
                VivoxCoreInstance.vx_get_volume_based_duplication_suppression_enabled(out var enabled);
                return enabled == 1;
            }
            set => VivoxCoreInstance.vx_set_volume_based_duplication_suppression_enabled(value ? 1 : 0);
        }

        public bool PositionalChannelVolumeProtectionEnabled
        {
            get
            {
                VivoxCoreInstance.vx_get_3d_channel_volume_protection_enabled(out var enabled);
                return enabled == 1;
            }
            set => VivoxCoreInstance.vx_set_3d_channel_volume_protection_enabled(value ? 1 : 0);
        }

        public bool AudioClippingProtectorEnabled
        {
            get
            {
                VivoxCoreInstance.vx_get_audio_clipping_protector_enabled(out var enabled);
                return enabled == 1;
            }
            set => VivoxCoreInstance.vx_set_audio_clipping_protector_enabled(value ? 1 : 0);
        }

        public AudioClippingProtectorParameters AudioClippingProtectorParameters
        {
            get
            {
                VivoxCoreInstance.vx_get_audio_clipping_protector_parameters(out var minThreshold, out var boostSlope);
                return new AudioClippingProtectorParameters(minThreshold, boostSlope);
            }
            set => VivoxCoreInstance.vx_set_audio_clipping_protector_parameters(value.MinimumThresholdDb, value.ThresholdBoostSlope);
        }
    }
}
