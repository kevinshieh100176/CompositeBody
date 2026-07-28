using Unity.Services.Core.Telemetry.Internal;
using Unity.Services.Lobbies.Apis.Lobby;
using Unity.Services.Wire.Internal;

namespace Unity.Services.Lobbies
{
    /// <summary> Interface for LobbyService</summary>
    internal interface ILobbyServiceSdk
    {
        /// <summary> Accessor for LobbyApi methods.</summary>
        ILobbyApiClient LobbyApi { get; }

        /// <summary> Configuration properties for the service.</summary>
        Configuration Configuration { get; }

        IWire Wire { get; set; }

        /// <summary> Metrics/telemetry for the service.</summary>
        IMetrics Metrics { get; }
    }
}
