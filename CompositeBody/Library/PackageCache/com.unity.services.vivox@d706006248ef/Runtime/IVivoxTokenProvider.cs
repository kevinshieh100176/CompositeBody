using System;
using System.Threading.Tasks;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Must be implemented by the <see cref="IVivoxService.SetTokenProvider(IVivoxTokenProvider)"/> caller.
    /// This object's responsibility is to provide an overridable implementation that will generate tokens for Vivox actions.
    /// </summary>
    public interface IVivoxTokenProvider
    {
        /// <summary>
        /// This async method should implement the necessary steps to providing a valid Vivox Access Token (VAT).
        /// After registration, this method will automatically be called whenever a token needs to
        /// be generated for a particular action. (e.g. login, channel join, mute).
        /// </summary>
        /// <param name="issuer">
        /// Id of a title.
        /// Provided as part of the credentials delivered upon creating a project in the Unity Dashboard
        /// and enabling Vivox.
        /// </param>
        /// <param name="expiration">
        /// When the token should expire.
        /// By default, a 90 second expiration is used.
        /// When entering this into the expiration field of the token payload, use '(int)expiration.Value.TotalSeconds' to get the integer value in seconds.
        /// You may provide a custom expiration to your token payload but it must be relative to Unix epoch and the current time plus the desired expiration duration.
        /// </param>
        /// <param name="targetUserUri">
        /// Id of the target for actions such as muting and blocking.
        /// </param>
        /// <param name="action">
        /// The action for which a token is requested.
        /// e.g.: "login", "join", ...
        /// </param>
        /// <param name="channelUri">
        /// Id of the channel requesting the token.
        /// </param>
        /// <param name="fromUserUri">
        /// Id of the user requesting the token.
        /// </param>
        /// <param name="realm">
        /// Domain for which the token should be created.
        /// </param>
        /// <returns>
        /// A Vivox token string.
        /// </returns>
        Task<string> GetTokenAsync(string issuer = null, TimeSpan? expiration = null, string targetUserUri = null, string action = null, string channelUri = null, string fromUserUri = null, string realm = null);
    }
}
