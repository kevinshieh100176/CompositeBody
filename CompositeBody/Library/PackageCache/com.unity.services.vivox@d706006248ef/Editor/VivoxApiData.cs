using Newtonsoft.Json;

namespace Unity.Services.Vivox.Editor
{
    class GetVivoxEnvironmentResponse
    {
        [JsonProperty("domain")]
        public string Domain { get; set; }

        [JsonProperty("serverUri")]
        public string ServerUri { get; set; }
    }

    class GetVivoxInternalsResponse
    {
        [JsonProperty("environment")]
        public GetVivoxEnvironmentResponse Environment { get; set; }

        [JsonProperty("issuer")]
        public string Issuer { get; set; }

        [JsonProperty("key")]
        public string Key { get; set; }
    }

    class GetVivoxCredentialsResponse
    {
        [JsonProperty("credentials")]
        public GetVivoxInternalsResponse Credentials { get; set; }
    }

    class TokenExchangeRequest
    {
        [JsonProperty("token")]
        public string Token { get; set; }
    }


    class TokenExchangeResponse
    {
        [JsonProperty("token")]
        public string Token { get; set; }
    }

    internal enum EnvironmentType
    {
        Automatic = 0,
        Custom = 1,
    }
}
