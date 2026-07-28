using System;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// A participant in a channel.
    /// <remarks>
    /// Note: The key for this interface is not the account ID; it is a unique identifier of that participant's session in that channel.
    /// </remarks>
    /// </summary>
    internal interface IParticipant : IKeyedItemNotifyPropertyChanged<string>, IParticipantProperties
    {
        /// <summary>
        /// The unique identifier for this participant.
        /// <remarks>This is not the same as <see cref="Account"/>.</remarks>
        /// </summary>
        string ParticipantId { get; }

        /// <summary>
        /// The ChannelSession that owns this participant.
        /// </summary>
        IChannelSession ParentChannelSession { get; }
        /// <summary>
        /// The account ID of this participant.
        /// </summary>
        AccountId Account { get; }
        /// <summary>
        /// Mute a given user for all other users in a channel.
        /// </summary>
        /// <param name="accountHandle">The account handle of the user you are muting.</param>
        /// <param name="setMuted">True to mute, false to unmute.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>The AsyncResult.</returns>
        [Obsolete("Use SetIsMuteForAll without the 'accountHandle' parameter instead")]
        IAsyncResult SetIsMuteForAll(string accountHandle, bool setMuted, AsyncCallback callback);
        /// <summary>
        /// Mute or unmute a given user for all other users in a channel.
        /// </summary>
        /// <param name="setMuted">True to mute, false to unmute.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns></returns>
        IAsyncResult SetIsMuteForAll(bool setMuted, AsyncCallback callback);
        /// <summary>
        /// Mute a given user for all other users in a channel.
        /// Not suggested for most uses - use the version that doesn't require an accessToken instead.
        /// </summary>
        /// <param name="accountHandle">The account handle of the user you are muting.</param>
        /// <param name="setMuted">True to mute, false to unmute.</param>
        /// <param name="accessToken">The access token that grants the user permission to mute this participant in the channel.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>The AsyncResult.</returns>
        [Obsolete("Use SetIsMuteForAll without the 'accountHandle' parameter instead")]
        IAsyncResult SetIsMuteForAll(string accountHandle, bool setMuted, string accessToken, AsyncCallback callback);
        /// <summary>
        /// Mute or unmute a given user for all other users in a channel.
        /// Not suggested for most uses - use the version that doesn't require an accessToken instead.
        /// </summary>
        /// <param name="setMuted">True to mute, false to unmute.</param>
        /// <param name="accessToken">The access token that grants the user permission to mute this participant in the channel.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>The AsyncResult.</returns>
        IAsyncResult SetIsMuteForAll(bool setMuted, string accessToken, AsyncCallback callback);
    }
}
