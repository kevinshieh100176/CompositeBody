using System;

namespace CompositeBody.Multiplayer
{
    public enum SessionState
    {
        Lobby = 0,
        InProgress = 1
    }

    /// <summary>
    /// Small, versioned payload broadcast by <see cref="LanSessionBroadcaster"/> and parsed by
    /// <see cref="LanSessionDiscovery"/>. Kept as a plain pipe-delimited string to avoid pulling
    /// in a serializer for a handful of fields.
    /// </summary>
    [Serializable]
    public struct LanSessionInfo
    {
        const string k_ProtocolTag = "CBLAN1";

        public string sessionName;
        public string hostAddress;
        public int port;
        public int currentPlayers;
        public int maxPlayers;
        public SessionState state;

        public static string Encode(LanSessionInfo info)
        {
            // hostAddress is filled in by the receiver from the UDP packet's source IP,
            // so it is not part of the wire payload.
            string safeName = info.sessionName.Replace("|", " ");
            return string.Join("|", k_ProtocolTag, safeName, info.port, info.currentPlayers, info.maxPlayers, (int)info.state);
        }

        public static bool TryDecode(string payload, string sourceAddress, out LanSessionInfo info)
        {
            info = default;
            if (string.IsNullOrEmpty(payload)) return false;

            string[] parts = payload.Split('|');
            if (parts.Length != 6 || parts[0] != k_ProtocolTag) return false;

            if (!int.TryParse(parts[2], out int port)) return false;
            if (!int.TryParse(parts[3], out int currentPlayers)) return false;
            if (!int.TryParse(parts[4], out int maxPlayers)) return false;
            if (!int.TryParse(parts[5], out int stateValue)) return false;

            info = new LanSessionInfo
            {
                sessionName = parts[1],
                hostAddress = sourceAddress,
                port = port,
                currentPlayers = currentPlayers,
                maxPlayers = maxPlayers,
                state = (SessionState)stateValue
            };
            return true;
        }
    }
}
