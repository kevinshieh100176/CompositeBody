namespace Unity.Services.Vivox
{
    /// <summary>
    /// The log level for the Vivox SDK.
    /// Log level order from least to most verbose: None -> Error -> Warning -> Info -> Debug -> Trace -> All
    /// </summary>
    public enum VivoxLogLevel
    {
        /// <summary>
        /// No logging at all
        /// </summary>
        None = vx_log_level.log_none,
        /// <summary>
        /// Error logging
        /// </summary>
        Error = vx_log_level.log_error,
        /// <summary>
        /// Up to warning logging
        /// </summary>
        Warning = vx_log_level.log_warning,
        /// <summary>
        /// Up to info logging
        /// </summary>
        Info = vx_log_level.log_info,
        /// <summary>
        /// Up to debug logging
        /// </summary>
        Debug = vx_log_level.log_debug,
        /// <summary>
        /// Up to trace logging
        /// </summary>
        Trace = vx_log_level.log_trace,
        /// <summary>
        /// All logging available
        /// </summary>
        All = vx_log_level.log_all,
    }
}
