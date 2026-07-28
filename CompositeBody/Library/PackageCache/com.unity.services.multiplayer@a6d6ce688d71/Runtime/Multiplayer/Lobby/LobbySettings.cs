namespace Unity.Services.Multiplayer
{
    interface ILobbySettings
    {
        public int HeartbeatSeconds { get; }
    }

    class LobbySettings : ILobbySettings
    {
        public int HeartbeatSeconds => 10;
    }
}
