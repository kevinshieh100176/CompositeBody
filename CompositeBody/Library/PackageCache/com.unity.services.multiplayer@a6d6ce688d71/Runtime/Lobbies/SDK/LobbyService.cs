using System;

namespace Unity.Services.Lobbies
{
    /// <summary>
    /// Here is the first point and call for accessing the Lobby Package's features!
    /// Use the .Instance method to get a singleton of the ILobbyServiceSDK, and from there you can make various requests to the Lobby service API.
    /// </summary>
    public static class LobbyService
    {
        private static ILobbyService service { get; set; }
        private static readonly Configuration configuration;

        static LobbyService()
        {
            configuration = new Configuration("https://lobby.services.api.unity.com/v1", 10, 4, null);
        }

        /// <summary>
        /// Provides the Lobby Service SDK interface for making service API requests.
        /// </summary>
        public static ILobbyService Instance
        {
            get
            {
                if (service == null)
                {
                    throw new InvalidOperationException($"Unable to get {nameof(ILobbyServiceSdk)} because Lobby API is not initialized. Make sure you call UnityServices.InitializeAsync().");
                }

                return service;
            }
            internal set
            {
                service = value;
            }
        }
    }
}
