using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using UnityEngine.Scripting;

namespace Unity.Services.Matchmaker.PayloadProxy
{
    /// <summary>
    /// Authentication token response.
    /// </summary>
    [Preserve]
    [DataContract(Name = "TokenResponse")]
    public class TokenResponse
    {
        /// <summary>
        /// Creates a default instance of TokenResponse.
        /// </summary>
        [Preserve]
        public TokenResponse()
        {
            Token = string.Empty;
            Error = string.Empty;
        }

        /// <summary>
        /// Creates an instance of TokenResponse.
        /// </summary>
        /// <param name="token">JWT Token string associated to payload requests</param>
        /// <param name="error">Internal multiplay error occurred retrieving the JWT</param>
        [Preserve]
        public TokenResponse(string token, string error)
        {
            Token = token;
            Error = error;
        }

        /// <summary>
        /// JWT Token string associated to payload requests
        /// </summary>
        [Preserve]
        [DataMember(Name = "token", IsRequired = true, EmitDefaultValue = true)]
        public string Token { get; set; }

        /// <summary>
        /// Internal multiplay error occurred retrieving the JWT
        /// </summary>
        [Preserve]
        [DataMember(Name = "error", IsRequired = true, EmitDefaultValue = true)]
        public string Error { get; set; }
    }
}
