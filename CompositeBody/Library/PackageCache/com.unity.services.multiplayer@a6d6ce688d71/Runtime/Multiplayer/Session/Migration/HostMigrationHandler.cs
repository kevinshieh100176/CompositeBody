using System;
using System.Threading.Tasks;
using Unity.Services.Core.Scheduler.Internal;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Used to Apply migration data to a host.
    /// </summary>
    internal interface IHostMigrationHandler
    {
        /// <summary>
        /// Retrieves and applies the migration data from the Lobby to the data handler.
        /// If no data handler is provided, or if the current player is not host of the lobby,
        /// nothing is done. Otherwise, it gets the host migration data from the Lobby, applies
        /// the fetched data to the data handler and schedules data upload.
        /// </summary>
        public Task ApplyMigrationDataAsync();

        /// <summary>
        /// Start scheduling data uploads
        /// </summary>
        public void Start();

        /// <summary>
        /// Cancel any scheduled data uploads.
        /// </summary>
        public void Stop();
    }

    internal class HostMigrationHandler : IHostMigrationHandler
    {
        const string k_EnclosingTypeName = nameof(HostMigrationHandler);
        readonly IActionScheduler m_ActionScheduler;
        readonly SessionHandler m_SessionHandler;
        internal readonly IMigrationDataHandler MigrationDataHandler;

        readonly TimeSpan m_DataUploadInterval;
        readonly TimeSpan m_DataHandlingTimeout;

        internal long? m_ScheduledMigrationId;

        internal HostMigrationHandler(SessionHandler sessionHandler,
                                      IMigrationDataHandler dataHandler,
                                      TimeSpan dataUploadInterval, TimeSpan dataHandlingTimeout)
        {
            m_SessionHandler = sessionHandler;
            m_ActionScheduler = sessionHandler.ActionScheduler;
            MigrationDataHandler = dataHandler;
            m_DataUploadInterval = dataUploadInterval;
            m_DataHandlingTimeout = dataHandlingTimeout;
        }

        public async Task ApplyMigrationDataAsync()
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);
            if (MigrationDataHandler == null)
            {
                Logger.LogWarning("No data handler provided, cannot apply migration data.");
                return;
            }

            if (!m_SessionHandler.IsHost)
            {
                Logger.LogError("Cannot apply migration data for non-host player.");
                return;
            }

            try
            {
                var migrationData = await m_SessionHandler.GetHostMigrationDataAsync(m_DataHandlingTimeout);

                if (migrationData is { Data : not null })
                {
                    MigrationDataHandler.Apply(migrationData.Data);
                }
                else
                {
                    Logger.LogVerbose("No migration data found, skipping apply.");
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Error while getting host migration data: {e.Message}");
            }

            ScheduleHostDataUpload();
        }

        public void Start()
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);
            if (!m_ScheduledMigrationId.HasValue)
            {
                if (MigrationDataHandler != null)
                {
                    ScheduleHostDataUpload();
                }
                else
                {
                    Logger.LogWarning("No data handler provided, host migration data will not run.");
                }
            }
        }

        public void Stop()
        {
            Logger.LogCallVerbose(k_EnclosingTypeName);
            if (m_ScheduledMigrationId.HasValue)
            {
                Logger.LogVerbose("Canceling scheduled host data upload");
                m_ActionScheduler.CancelAction(m_ScheduledMigrationId.Value);
                m_ScheduledMigrationId = null;
            }
        }

        void ScheduleHostDataUpload()
        {
            if (m_SessionHandler.IsHost && !m_ScheduledMigrationId.HasValue)
            {
                m_ScheduledMigrationId = m_ActionScheduler.ScheduleAction(UploadHostData, m_DataUploadInterval.TotalSeconds);
            }
        }

        async void UploadHostData()
        {
            m_ScheduledMigrationId = null;

            if (!m_SessionHandler.IsHost)
            {
                Logger.LogVerbose("Player is not host, stopping upload.");
                return;
            }

            if (m_SessionHandler.State != SessionState.Connected)
            {
                Logger.LogVerbose("Session is not connected, skipping upload.");
                ScheduleHostDataUpload();
                return;
            }

            if (m_SessionHandler.PlayerCount < 2)
            {
                Logger.LogVerbose("Player count is less than 2, skipping upload.");
                ScheduleHostDataUpload();
                return;
            }

            Logger.LogVerbose("Updating migration data.");
            var migrationData = MigrationDataHandler.Generate();
            await m_SessionHandler.SetHostMigrationDataAsync(migrationData, m_DataHandlingTimeout);
            ScheduleHostDataUpload();
        }
    }
}
