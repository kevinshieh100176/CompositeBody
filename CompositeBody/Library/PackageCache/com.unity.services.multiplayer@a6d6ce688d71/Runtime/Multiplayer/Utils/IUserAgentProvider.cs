namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Provides a User-Agent string for HTTP requests to backend services.
    /// </summary>
    internal interface IUserAgentProvider
    {
        /// <summary>
        /// The User-Agent header value to include on outgoing HTTP requests.
        /// </summary>
        string UserAgent { get; }
    }
}
