namespace Unity.Services.Matchmaker
{
    /// <summary>
    /// Base class used for editing the configuration of the matchmaker service SDK.
    /// Primary usage is for testing endpoints / apis in other environments.
    /// </summary>
    public interface IMatchmakerSdkConfiguration
    {
        /// <summary>
        /// Sets the base path in configuration.
        /// </summary>
        /// <param name="basePath">The base path to set in configuration.</param>
        public void SetBasePath(string basePath);
    }
}
