using Unity.Services.Lobbies.Models;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Class for host migration data model.
    /// </summary>
    public class SessionMigrationData : MigrationData
    {
        /// <summary>
        /// The migration data bytes.
        /// </summary>
        public override byte[] Data => m_migrationData.Data;

        private readonly LobbyMigrationData m_migrationData;

        /// <summary>
        /// Creates a MigrationData object from a LobbyMigrationData object.
        /// </summary>
        /// <param name="lobbyMigrationData">The lobby migration data object.</param>
        internal SessionMigrationData(LobbyMigrationData lobbyMigrationData)
        {
            m_migrationData = lobbyMigrationData;
        }
    }
}
