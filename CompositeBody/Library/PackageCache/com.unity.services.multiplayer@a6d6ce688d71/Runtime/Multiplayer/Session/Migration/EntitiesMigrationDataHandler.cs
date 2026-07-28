#if ENTITIES_NETCODE_HOST_MIGRATION_AVAILABLE || PACKAGE_DOCS_GENERATION
using Unity.Collections;
using Unity.NetCode;
using Unity.NetCode.HostMigration;

namespace Unity.Services.Multiplayer
{
    class EntitiesMigrationDataHandler : IMigrationDataHandler
    {
        NativeList<byte> m_MigrationDataBlob = new NativeList<byte>(100_000, Allocator.Persistent);

        public byte[] Generate()
        {
            HostMigrationData.Get(ClientServerBootstrap.ServerWorld, ref m_MigrationDataBlob);
            return m_MigrationDataBlob.AsArray().ToArray();
        }

        public void Apply(byte[] migrationData)
        {
            if (ClientServerBootstrap.ServerWorld == null)
            {
                Logger.LogVerbose("Creating server world & applying migration data.");
                ClientServerBootstrap.CreateServerWorld("ServerWorld");
            }

            m_MigrationDataBlob.ResizeUninitialized(migrationData.Length);

            var arrayData = m_MigrationDataBlob.AsArray();
            var slice = new NativeSlice<byte>(arrayData, 0, migrationData.Length);
            slice.CopyFrom(migrationData);
            HostMigrationData.Set(in arrayData, ClientServerBootstrap.ServerWorld);
        }
    }
}

#endif
