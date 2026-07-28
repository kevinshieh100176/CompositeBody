namespace Unity.Services.Lobbies.Models
{
    /// <summary>
    /// Abstract class to define migration data model.
    /// </summary>
    public abstract class MigrationData
    {
        /// <summary>
        /// The migration data bytes.
        /// </summary>
        public abstract byte[] Data {get;}
    }

    /// <summary>
    /// Class for lobby migration data model.
    /// </summary>
    public class LobbyMigrationData : MigrationData
    {
        /// <summary>
        /// The migration data bytes.
        /// </summary>
        public override byte[] Data {get;}
        /// <summary>
        /// Creates a LobbyMigrationData object.
        /// </summary>
        /// <param name="data">The migration data bytes.</param>
        public LobbyMigrationData(byte[] data) { Data = data; }
    }
}
