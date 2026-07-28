using System;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Used during initialization to set up the SDK with a custom configuration.
    /// </summary>
    public sealed class VivoxConfigurationOptions
    {
        /// <summary>
        /// Used for mapping adjustments made to the properties in this class back to the SWIG generated internal vx_sdk_config_t type.
        /// </summary>
        internal vx_sdk_config_t InternalVivoxConfig { get; set; } = new vx_sdk_config_t();

        /// <summary>
        /// The render source maximum queue depth.
        /// </summary>
        public int RenderSourceQueueDepthMax
        {
            get { return InternalVivoxConfig.render_source_queue_depth_max; }
            set { InternalVivoxConfig.render_source_queue_depth_max = value; }
        }

        /// <summary>
        /// The render source initial buffer count.
        /// </summary>
        public int RenderSourceInitialBufferCount
        {
            get { return InternalVivoxConfig.render_source_initial_buffer_count; }
            set { InternalVivoxConfig.render_source_initial_buffer_count = value; }
        }

        /// <summary>
        /// The upstream jitter frame count.
        /// </summary>
        public int UpstreamJitterFrameCount
        {
            get { return InternalVivoxConfig.upstream_jitter_frame_count; }
            set { InternalVivoxConfig.upstream_jitter_frame_count = value; }
        }

        /// <summary>
        /// The maximum number of logins per user.
        /// </summary>
        public int MaxLoginsPerUser
        {
            get { return InternalVivoxConfig.max_logins_per_user; }
            set { InternalVivoxConfig.max_logins_per_user = value; }
        }

        /// <summary>
        /// The log verbosity of the Vivox SDK.
        /// Log level order from least to most verbose: None -> Error -> Warning -> Info -> Debug -> Trace -> All
        /// </summary>
        public VivoxLogLevel LogLevel
        {
            get { return (VivoxLogLevel)InternalVivoxConfig.initial_log_level; }
            set { InternalVivoxConfig.initial_log_level = (vx_log_level)value; }
        }

        /// <summary>
        /// Disable audio device polling by using a timer.
        /// </summary>
        public bool DisableDevicePolling
        {
            get { return Convert.ToBoolean(InternalVivoxConfig.disable_device_polling); }
            set { InternalVivoxConfig.disable_device_polling = Convert.ToInt32(value); }
        }

        /// <summary>
        /// Used for diagnostic purposes only.
        /// </summary>
        public bool ForceCaptureSilence
        {
            get { return Convert.ToBoolean(InternalVivoxConfig.force_capture_silence); }
            set { InternalVivoxConfig.force_capture_silence = Convert.ToInt32(value); }
        }

        /// <summary>
        /// Enable advanced automatic settings for audio levels.
        /// </summary>
        public bool EnableAdvancedAutoLevels
        {
            get { return Convert.ToBoolean(InternalVivoxConfig.enable_advanced_auto_levels); }
            set { InternalVivoxConfig.enable_advanced_auto_levels = Convert.ToInt32(value); }
        }

        /// <summary>
        /// The number of 20 millisecond buffers for the capture device.
        /// </summary>
        public int CaptureDeviceBufferSizeIntervals
        {
            get { return InternalVivoxConfig.capture_device_buffer_size_intervals; }
            set { InternalVivoxConfig.capture_device_buffer_size_intervals = value; }
        }

        /// <summary>
        /// The number of 20 millisecond buffers for the render device.
        /// </summary>
        public int RenderDeviceBufferSizeIntervals
        {
            get { return InternalVivoxConfig.render_device_buffer_size_intervals; }
            set { InternalVivoxConfig.render_device_buffer_size_intervals = value; }
        }

        /// <summary>
        /// Disable audio ducking.
        /// </summary>
        public bool DisableAudioDucking
        {
            get { return Convert.ToBoolean(InternalVivoxConfig.disable_audio_ducking); }
            set { InternalVivoxConfig.disable_audio_ducking = Convert.ToInt32(value); }
        }

        /// <summary>
        /// Default of 1 for most platforms.
        /// Caution: Changes to this value must be coordinated with Vivox.
        /// </summary>
        public bool EnableDtx
        {
            get { return Convert.ToBoolean(InternalVivoxConfig.enable_dtx); }
            set { InternalVivoxConfig.enable_dtx = Convert.ToInt32(value); }
        }

        /// <summary>
        /// Enable fast network change detection. By default, this is disabled.
        /// </summary>
        public bool EnableFastNetworkChangeDetection
        {
            get { return Convert.ToBoolean(InternalVivoxConfig.enable_fast_network_change_detection); }
            set { InternalVivoxConfig.enable_fast_network_change_detection = Convert.ToInt32(value); }
        }

        /// <summary>
        /// Use the operating system-configured proxy settings.
        /// The default value is 0.
        /// The value is 1 if the environment variable "VIVOX_USE_OS_PROXY_SETTINGS" is set.
        /// Note: Only applicable to the Windows platform.
        /// </summary>
        public int UseOsProxySettings
        {
            get { return InternalVivoxConfig.use_os_proxy_settings; }
            set { InternalVivoxConfig.use_os_proxy_settings = value; }
        }

        /// <summary>
        /// Enable dynamic voice processing switching. The default value is true.
        /// If enabled, the SDK automatically switches between hardware and software AECs.
        /// The default value is 1.
        /// To disable this capability, set the value to 0.
        /// </summary>
        public bool DynamicVoiceProcessingSwitching
        {
            get { return Convert.ToBoolean(InternalVivoxConfig.dynamic_voice_processing_switching); }
            set { InternalVivoxConfig.dynamic_voice_processing_switching = Convert.ToInt32(value); }
        }

        /// <summary>
        /// Enable mobile recording conflicts avoidance.
        /// If Vivox detects that there is more than one recorder, Vivox disables its recorder to allow for others to record.
        /// Real call dialer app, Voip apps, Voice recognition apps are all prioritized to use the recorder over Vivox if this field is set to true.
        /// The default value is true.
        /// To disable this capability, set the value to false.
        /// Note: This feature currently only affects Android mobile devices.
        /// </summary>
        public bool MobileRecordingConflictAvoidance
        {
            get { return Convert.ToBoolean(InternalVivoxConfig.mobile_recording_conflicts_avoidance); }
            set { InternalVivoxConfig.mobile_recording_conflicts_avoidance = Convert.ToInt32(value); }
        }

        /// <summary>
        /// The number of millseconds to wait before disconnecting audio due to RTP timeout at the initial call time.
        /// A zero or negative value turns off the guard, which is not recommended.
        /// </summary>
        public int NeverRtpTimeoutMs
        {
            get { return InternalVivoxConfig.never_rtp_timeout_ms; }
            set { InternalVivoxConfig.never_rtp_timeout_ms = value; }
        }

        /// <summary>
        /// The number of millseconds to wait before disconnecting audio due to RTP timeout after the call has been established.
        /// A zero or negative value turns off the guard, which is not recommended.
        /// </summary>
        public int LostRtpTimeoutMs
        {
            get { return InternalVivoxConfig.lost_rtp_timeout_ms; }
            set { InternalVivoxConfig.lost_rtp_timeout_ms = value; }
        }

        /// <summary>
        /// Allows for the configuration of which Bluetooth profile Vivox will use.
        /// Note: This  setting currently only affects mobile devices.
        /// </summary>
        public BluetoothProfile BluetoothProfile
        {
            get => (BluetoothProfile)InternalVivoxConfig.bluetooth_profile;
            set => InternalVivoxConfig.bluetooth_profile = (vx_bluetooth_profile)value;
        }

        /// <summary>
        /// Controls the behavior of which audio unit is used on different scenarios on iOS.
        /// 2 (default): Will always use the Voice Processing Audio Unit, no matter the audio setup.
        /// 1: Will only use the Voice Processing Audio Unit when the speaker phone is in use.
        /// 0: Will never use the Voice Processing Audio Unit.
        /// </summary>
        public IosVoiceProcessingIOModes IosVoiceProcessingMode
        {
            get => (IosVoiceProcessingIOModes)InternalVivoxConfig.ios_voice_processing_io_mode;
            set => InternalVivoxConfig.ios_voice_processing_io_mode = (int)value;
        }

        /// <summary>
        /// For iOS, set this to true to control the iOS PlayAndRecord category.
        /// If set to false, Vivox sets the proper iOS PlayAndRecord category.
        /// Note: You must set the PlayAndRecord category for simultaneous input/output.
        /// An improper PlayAndRecord category can result in loss of voice communication.
        /// Defaulting to a speaker plays from speaker hardware instead of the receiver (ear speaker) when headphones are not used.
        /// </summary>
        public bool SkipPrepareForVivox { get; set; }
    }
}
