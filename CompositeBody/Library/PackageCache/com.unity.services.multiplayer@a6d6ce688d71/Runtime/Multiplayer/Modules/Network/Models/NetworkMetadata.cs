using System;
using Newtonsoft.Json;

namespace Unity.Services.Multiplayer
{
    class NetworkMetadata
    {
        const string k_EnclosingTypeName = nameof(NetworkMetadata);

        public NetworkType Network { get; set; }
        [JsonProperty]
        string Ip { get; set; }
        [JsonProperty]
        ushort Port { get; set; }
        public string RelayJoinCode { get; set; }
        public string RelayRegion { get; set; }
        public string HostId { get; set; }

        [JsonIgnore]
        public NetworkEndpointAddress Endpoint
        {
            get => new(Ip, Port);
            set
            {
                var ip = value.AddressNoPort;
                var port = value.Port;
                Ip = ip;
                Port = port;
            }
        }

        internal static NetworkMetadata Deserialize(string content)
        {
            try
            {
                Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, content);
                var networkMetadata = JsonConvert.DeserializeObject<NetworkMetadata>(content);

                if (networkMetadata == null)
                {
                    throw new SessionException("Invalid network metadata.", SessionError.InvalidSessionMetadata);
                }

                return networkMetadata;
            }
            catch (Exception e)
            {
                throw new SessionException($"Missing network metadata: {e}", SessionError.InvalidSessionMetadata);
            }
        }

        internal static string Serialize(NetworkMetadata networkMetadata)
        {
            try
            {
                var metadata = JsonConvert.SerializeObject(networkMetadata);
                Logger.LogCallVerboseWithMessage(k_EnclosingTypeName, metadata);
                return metadata;
            }
            catch (Exception e)
            {
                throw new SessionException(
                    $"Failed to serialize network metadata: {e}",
                    SessionError.InvalidSessionMetadata);
            }
        }
    }
}
