namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// An interface that can be used by a client to provide
    /// custom migration data generation and apply.
    /// </summary>
    public interface IMigrationDataHandler
    {
        /// <summary>
        /// Generate the migration data from a host player.
        /// </summary>
        /// <returns>An array of bytes representing the session
        /// state.</returns>
        /// <remarks>This method will be called periodically and
        /// the data will be stored in case of a host migration
        /// is triggered. The data generated should be acceptable
        /// to the <see cref="Apply">Apply</see> on any player
        /// selected as host during migration.</remarks>
        public byte[] Generate();

        /// <summary>
        /// Applies the migration data to a host player.
        /// </summary>
        /// <param name="migrationData">The bytes representing
        /// the session state.</param>
        /// <remarks>This method will be called during host
        /// migration on the player selected as the new host.
        /// The data might have been<see cref="Generate">
        /// Generated</see> on any player that was previously
        /// a host, including players that have already
        /// left.</remarks>
        public void Apply(byte[] migrationData);
    }
}
