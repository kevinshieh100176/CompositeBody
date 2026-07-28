using System;

namespace Unity.Services.Lobbies.Models
{
    /// <summary>
    /// Parameter class for lobby migration data upload requests.
    /// </summary>
    public class LobbyUploadMigrationDataOptions
    {
        /// <summary>
        /// The default timeout for an upload request.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(50d);

        /// <summary>
        /// The timeout for an upload request.
        /// </summary>
        public TimeSpan Timeout { get; set; } = DefaultTimeout;
    }
}
