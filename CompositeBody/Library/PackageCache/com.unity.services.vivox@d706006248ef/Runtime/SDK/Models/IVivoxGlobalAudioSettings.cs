namespace Unity.Services.Vivox
{
    /// <summary>
    /// Provides control over Vivox's global audio processing features including noise suppression, echo cancellation, automatic gain control, and audio protection systems.
    /// </summary>
    public interface IVivoxGlobalAudioSettings
    {
        /// <summary>
        /// Controls the SDK's noise suppression system for audio input.
        /// When enabled, reduces background noise in captured audio.
        /// When disabled, passes through audio without noise filtering.
        /// </summary>
        bool NoiseSuppressionEnabled { get; set; }

        /// <summary>
        /// Controls platform-native acoustic echo cancellation (AEC) on mobile devices.
        /// When enabled, platform AEC is used only when necessary.
        /// When disabled, platform AEC is bypassed.
        /// Only affects iOS and Android platforms.
        /// </summary>
        bool PlatformAcousticEchoCancellationEnabled { get; set; }

        /// <summary>
        /// Controls Vivox's acoustic echo cancellation (AEC).
        /// When enabled, allows the SDK to apply AEC when necessary.
        /// </summary>
        bool VivoxAcousticEchoCancellationEnabled { get; set; }

        /// <summary>
        /// Controls automatic gain control for audio input.
        /// When enabled, allows the SDK to apply automatic gain control when necessary.
        /// </summary>
        bool AutomaticGainControlEnabled { get; set; }

        /// <summary>
        /// Controls dynamic switching of voice processing features.
        /// </summary>
        bool DynamicVoiceProcessingSwitchingEnabled { get; set; }

        /// <summary>
        /// Controls audio duplication for participants transmitting to multiple channels.
        /// When enabled, renders audio only in the channel where the participant is loudest.
        /// When disabled, allows audio duplication across 2D channels.
        /// Note: Does not affect positional (3D) channels, which always render all participants.
        /// </summary>
        bool VolumeBasedDuplicationSuppressionEnabled { get; set; }

        /// <summary>
        /// Controls volume protection for positional audio channels to prevent distortion.
        /// When enabled, reduces positional channels by 6 dB relative to non-positional channels.
        /// When disabled, maintains equal volume levels but may introduce clipping for off-center sources.
        /// </summary>
        bool PositionalChannelVolumeProtectionEnabled { get; set; }

        /// <summary>
        /// Controls the audio clipping protection system.
        /// When enabled, reduces dynamic range of near-clipping samples to prevent distortion.
        /// When disabled, allows natural audio clipping to occur.
        /// </summary>
        bool AudioClippingProtectorEnabled { get; set; }

        /// <summary>
        /// Controls parameters for the audio clipping protection system.
        /// Adjusting this changes the behavior of Vivox's audio clipping protector, which is a soft clipper.
        /// The audio clipping protector can only affect boosted audio (capture or render with volume settings greater than 0 (default)).
        /// Clipping protection is applied only above a threshold in dBFS.
        /// ThresholdDb = max(boost* ThresholdBoostSlope, MinimumThresholdDb)
        /// ex. +10 dB boost(volume 10), ThresholdBoostSlope = -0.1, MinimumThresholdDb = -6.0: ThresholdDb = max(10 * -0.1, -6.0) = -1.0.
        /// ex. (cont) Clipping protection would only be applied to amplitudes exceeding -1.0 dBFS.
        /// </summary>
        AudioClippingProtectorParameters AudioClippingProtectorParameters { get; set; }
    }
}
