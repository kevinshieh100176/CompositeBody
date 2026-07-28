using System;

namespace Unity.Services.Multiplayer
{
    class HostMigrationOption : IModuleOption
    {
        public Type Type => typeof(NetworkModule);
        public void Process(SessionHandler session)
        {
            session.GetModule<NetworkModule>().HostMigrationHandler = new HostMigrationHandler(session, DataHandler, DataUploadInterval, DataHandlingTimeout);
        }

        public IMigrationDataHandler DataHandler { get; set; }
        public TimeSpan DataUploadInterval { get; set; }
        public TimeSpan DataHandlingTimeout { get; set; }
    }

    public static partial class SessionOptionsExtensions
    {
        /// <summary>
        /// The default interval, <c>5s</c>, at which migration data is uploaded
        /// during host migration.
        /// </summary>
        static readonly TimeSpan DefaultDataUploadInterval =
            TimeSpan.FromSeconds(5);

        /// <summary>
        /// The default timeout duration, <c>3s</c>, for handling migration data
        /// during host migration.
        /// </summary>
        static readonly TimeSpan DefaultDataHandlingTimeout =
            TimeSpan.FromSeconds(3);

#if ENTITIES_NETCODE_HOST_MIGRATION_AVAILABLE || PACKAGE_DOCS_GENERATION
        /// <summary>
        /// Enables host migration for the session using the default migration
        /// data handler (<see cref="EntitiesMigrationDataHandler"/>) and
        /// default timing options.
        /// </summary>
        /// <typeparam name="T">The session option type derived from <see
        /// cref="BaseSessionOptions"/>.</typeparam>
        /// <param name="options">The session options to configure.</param>
        /// <returns>The session options with host migration enabled.</returns>
        public static T WithHostMigration<T>(this T options)
            where T : BaseSessionOptions
        {
            return options.WithHostMigration(new EntitiesMigrationDataHandler(),
                DefaultDataUploadInterval, DefaultDataHandlingTimeout);
        }

#endif

        /// <summary>
        /// Enables host migration for the session with the specified migration
        /// data handler, using default timing options <see
        /// cref="DefaultDataUploadInterval"/> and <see
        /// cref="DefaultDataHandlingTimeout"/>.
        /// </summary>
        /// <typeparam name="T">The session option type derived from <see
        /// cref="BaseSessionOptions"/>.</typeparam>
        /// <param name="options">The session options to configure.</param>
        /// <param name="migrationDataHandler">The handler responsible for
        /// migration data processing.</param>
        /// <returns>The session options with host migration enabled.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref
        /// name="migrationDataHandler"/> is null.</exception>
        public static T WithHostMigration<T>(this T options, IMigrationDataHandler migrationDataHandler) where T : BaseSessionOptions
        {
            return options.WithHostMigration(migrationDataHandler,
                DefaultDataUploadInterval, DefaultDataHandlingTimeout);
        }

        /// <summary>
        /// Enables host migration for the session with the specified migration
        /// data handler and timing options.
        /// </summary>
        /// <typeparam name="T">The session option type derived from <see
        /// cref="BaseSessionOptions"/>.</typeparam>
        /// <param name="options">The session options to configure.</param>
        /// <param name="migrationDataHandler">The handler responsible for
        /// migration data processing.</param>
        /// <param name="dataUploadInterval">The interval at which migration
        /// data is uploaded. The lowest interval is 1 second.</param>
        /// <param name="dataHandlingTimeout">The timeout for handling migration
        /// data.</param>
        /// <returns>The session options with host migration enabled.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref
        /// name="migrationDataHandler"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref
        /// name="dataUploadInterval"/> is lower than 1 second.</exception>
        public static T WithHostMigration<T>(this T options, IMigrationDataHandler migrationDataHandler, TimeSpan dataUploadInterval, TimeSpan dataHandlingTimeout) where T : BaseSessionOptions
        {
            if (migrationDataHandler == null)
            {
                throw new ArgumentNullException(nameof(migrationDataHandler), $"{nameof(migrationDataHandler)} is a required property for WithHostMigration and cannot be null.");
            }

            if (dataUploadInterval < TimeSpan.FromSeconds(1))
            {
                throw new ArgumentOutOfRangeException(nameof(dataUploadInterval), $"{nameof(dataUploadInterval)} cannot be lower than 1 second.");
            }

            return options.WithOption(new HostMigrationOption
            {
                DataHandler = migrationDataHandler,
                DataUploadInterval = dataUploadInterval,
                DataHandlingTimeout = dataHandlingTimeout
            });
        }
    }
}
