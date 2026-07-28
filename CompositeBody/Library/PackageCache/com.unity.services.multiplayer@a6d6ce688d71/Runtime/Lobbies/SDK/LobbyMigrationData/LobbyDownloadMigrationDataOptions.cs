using System;

namespace Unity.Services.Lobbies.Models
{
    /// <summary>
    /// Parameter class for lobby migration data download requests.
    /// </summary>
    public class LobbyDownloadMigrationDataOptions
    {
        /// <summary>
        /// The default timeout for a download request.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(50d);

        /// <summary>
        /// The download request timeout.
        /// </summary>
        public TimeSpan Timeout { get; set; } = DefaultTimeout;
    }
}
