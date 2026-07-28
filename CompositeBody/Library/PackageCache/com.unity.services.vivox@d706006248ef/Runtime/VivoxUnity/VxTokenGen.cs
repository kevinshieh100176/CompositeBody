using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Unity.Services.Vivox
{
    internal class VxTokenGen
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string vx_debugGenerateToken(string issuer, int duration, string vxa, string subject, string from_uri, string to_uri, string key);
#endif
        public const int k_defaultTokenExpirationInSeconds = 90;
        static string key = "";
        private static readonly DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static ulong serialNumber = 0;

        public string IssuerKey { get => key; set => key = value; }

        public string GetLoginToken(string fromUserUri, TimeSpan? expiration = null)
        {
            string issuer = new AccountId(fromUserUri).Issuer;
            return GetToken(issuer, expiration, null, "login", null, null, fromUserUri);
        }

        public string GetLoginToken(string issuer, string fromUserUri, TimeSpan expiration, string tokenSigningKey)
        {
            return GetToken(issuer, expiration, null, "login", tokenSigningKey, null, fromUserUri);
        }

        public string GetJoinToken(string fromUserUri, string conferenceUri, TimeSpan? expiration = null)
        {
            string issuer = new AccountId(fromUserUri).Issuer;
            return GetToken(issuer, expiration, null, "join", null, conferenceUri, fromUserUri);
        }

        public string GetJoinToken(string issuer, string fromUserUri, string conferenceUri, TimeSpan expiration, string tokenSigningKey)
        {
            return GetToken(issuer, expiration, null, "join", tokenSigningKey, conferenceUri, fromUserUri);
        }

        public string GetMuteForAllToken(string fromUserUri, string userUri, string conferenceUri, TimeSpan? expiration = null)
        {
            string issuer = new AccountId(fromUserUri).Issuer;
            return GetToken(issuer, expiration, userUri, "mute", null, conferenceUri, fromUserUri);
        }

        public string GetMuteForAllToken(string issuer, string fromUserUri, string userUri, string conferenceUri, TimeSpan expiration, string tokenSigningKey)
        {
            return GetToken(issuer, expiration, userUri, "mute", tokenSigningKey, conferenceUri, fromUserUri);
        }

        public string GetTranscriptionToken(string fromUserUri, string conferenceUri, TimeSpan? expiration = null)
        {
            string issuer = new AccountId(fromUserUri).Issuer;
            return GetToken(issuer, expiration, null, "trxn", null, conferenceUri, fromUserUri);
        }

        public string GetTranscriptionToken(string issuer, string fromUserUri, string conferenceUri, TimeSpan expiration, string tokenSigningKey)
        {
            return GetToken(issuer, expiration, null, "trxn", tokenSigningKey, conferenceUri, fromUserUri);
        }

        public virtual string GetToken(string issuer = null, TimeSpan? expiration = null, string targetUserUri = null, string action = null, string tokenKey = null, string channelUri = null, string fromUserUri = null)
        {
            CheckInitialized();
            string signingKey = key;
            if (tokenKey != null)
                signingKey = tokenKey;

            if (string.IsNullOrEmpty(signingKey))
            {
                VivoxLogger.LogError($"Unable to generate a Vivox Access Token locally because the token signing key was not found.");
                return null;
            }

            if (expiration == null)
            {
                expiration = TimeSpan.FromSeconds(k_defaultTokenExpirationInSeconds);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return vx_debugGenerateToken(issuer, (int)expiration.Value.TotalSeconds, action, targetUserUri, fromUserUri, channelUri, signingKey);
#else
            return VivoxCoreInstance.vx_debug_generate_token(issuer, (int)Helper.TimeSinceUnixEpochPlusDuration(expiration.Value).TotalSeconds, action, serialNumber++, targetUserUri, fromUserUri, channelUri, signingKey);
#endif
        }

        public virtual Task<string> GetTokenAsync(string issuer = null, TimeSpan? expiration = null, string targetUserUri = null, string action = null, string tokenKey = null, string channelUri = null, string fromUserUri = null)
        {
            return Task.FromResult(GetToken(issuer, expiration, targetUserUri, action, tokenKey, channelUri, fromUserUri));
        }

        private static void CheckInitialized()
        {
            if (!VxClient.Instance.Started)
            {
                throw new NotSupportedException("Method can not be called before Vivox SDK is initialized.");
            }
        }

        private static int SecondsSinceUnixEpochPlusDuration(TimeSpan? duration = null)
        {
            TimeSpan timestamp = DateTime.UtcNow.Subtract(unixEpoch);
            if (duration.HasValue)
            {
                timestamp = timestamp.Add(duration.Value);
            }

            return (int)timestamp.TotalSeconds;
        }

        private static TimeSpan CheckExpiration(TimeSpan? expiration = null)
        {
            if (expiration == null)
            {
                expiration = TimeSpan.FromSeconds(k_defaultTokenExpirationInSeconds);
            }
            return (TimeSpan)expiration;
        }
    }
}
