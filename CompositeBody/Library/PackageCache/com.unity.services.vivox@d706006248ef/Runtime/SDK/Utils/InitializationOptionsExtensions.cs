using Unity.Services.Core;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Utilities to simplify setting options related to this SDK through code.
    /// </summary>
    public static class InitializationOptionsExtensions
    {
        /// <summary>
        /// An extension to set the credentials for the Vivox SDK.
        /// </summary>
        /// <param name="tokenKey">
        /// This is optional because a developer could be leveraging Unity Authentication tokens or vending tokens server-side.
        /// If a Vivox Key is not supplied and proper server-side Vivox Access Token generation is not setup, the Vivox package will not function properly.
        /// </param>
        /// <returns>
        /// Return <paramref name="self"/>.
        /// Fluent interface pattern to make it easier to chain set options operations.
        /// </returns>
        /// <param name="self">The initialization options that are being extended.</param>
        /// <param name="server">The Vivox Server value to set the credentials for.</param>
        /// <param name="domain">The Vivox Domain value to set the credentials for.</param>
        /// <param name="issuer">The Vivox Token Issuer to set the credentials for.</param>
        public static InitializationOptions SetVivoxCredentials(this InitializationOptions self, string server, string domain, string issuer, string tokenKey = "")
        {
            self.SetOption(VivoxServiceInternal.k_ServerKey, server);
            self.SetOption(VivoxServiceInternal.k_DomainKey, domain);
            self.SetOption(VivoxServiceInternal.k_IssuerKey, issuer);
            self.SetOption(VivoxServiceInternal.k_TokenKey, tokenKey);
            self.SetOption(VivoxServiceInternal.k_EnvironmentCustomKey, true);
            return self;
        }
    }
}
