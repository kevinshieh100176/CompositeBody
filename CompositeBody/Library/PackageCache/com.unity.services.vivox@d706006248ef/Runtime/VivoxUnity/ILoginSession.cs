using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;


namespace Unity.Services.Vivox
{
    /// <summary>
    /// A session for an account.
    /// </summary>
    internal interface ILoginSession : IKeyedItemNotifyPropertyChanged<AccountId>
    {
        #region Properties
        /// <summary>
        /// The list of channel sessions associated with this login session.
        /// </summary>
        IReadOnlyDictionary<ChannelId, IChannelSession> ChannelSessions { get; }

        #region Presence

        /// <summary>
        /// The list of presence subscriptions associated with this login session.
        /// </summary>
        /// <remarks>This typically corresponds to a list of "friends".</remarks>
        IReadOnlyDictionary<AccountId, IPresenceSubscription> PresenceSubscriptions { get; }
        /// <summary>
        /// The list of accounts blocked from seeing this account's online status.
        /// </summary>
        IReadOnlyHashSet<AccountId> BlockedSubscriptions { get; }
        /// <summary>
        /// The list of accounts allowed to see this account's online status.
        /// </summary>
        IReadOnlyHashSet<AccountId> AllowedSubscriptions { get; }
        /// <summary>
        /// The list of incoming subscription requests.
        /// </summary>
        IReadOnlyQueue<AccountId> IncomingSubscriptionRequests { get; }

        #endregion

        /// <summary>
        /// The list of accounts with cross muted communications to this LoginSession
        /// </summary>
        IReadOnlyHashSet<AccountId> CrossMutedCommunications { get; }

        /// <summary>
        /// The list of incoming user to user messages.
        /// </summary>
        IReadOnlyQueue<IDirectedTextMessage> DirectedMessages { get; }
        /// <summary>
        /// The list of failed user to user messages.
        /// </summary>
        IReadOnlyQueue<IFailedDirectedTextMessage> FailedDirectedMessages { get; }

        /// <summary>
        /// An event indicating which message has been edited from a given user.
        /// </summary>
        /// <remarks>Use VivoxMessage events to get notifications of edited text messages.</remarks>
        event EventHandler<VivoxMessage> DirectedMessageEdited;
        /// <summary>
        /// An event indicating which message has been deleted from a given user.
        /// </summary>
        /// <remarks>Use VivoxMessage events to get notifications of deleted text messages.</remarks>
        event EventHandler<VivoxMessage> DirectedMessageDeleted;
        /// <summary>
        /// The list of account archive messages returned by a BeginAccountArchiveQuery.
        /// </summary>
        /// <remarks>Use the IReadOnlyQueue events to get notifications of incoming messages from a account archive query.  This is not automatically cleared when starting a new BeginAccountArchiveQuery.</remarks>
        [Obsolete("This feature is being deprecated in favor of the limited beta release of Chat History. Please contact Unity Support for feature enablement.")]
        IReadOnlyQueue<IAccountArchiveMessage> AccountArchive { get; }
        /// <summary>
        /// The result set when all the messages have been returned from a BeginAccountArchiveQuery.
        /// </summary>
        /// <remarks>Use the PropertyChanged event to get notified when a account archive query has started or completed.</remarks>
        [Obsolete("This feature is being deprecated in favor of the limited beta release of Chat History. Please contact Unity Support for feature enablement.")]
        IArchiveQueryResult AccountArchiveResult { get; }
        /// <summary>
        /// The result set when a user to user message has been sent.
        /// </summary>
        /// <remarks>Use the PropertyChanged event to get notified when a directed message has been sent.</remarks>
        IDirectedMessageResult DirectedMessageResult { get; }
        /// <summary>
        /// The current state of this login session.
        /// </summary>
        LoginState State { get; }
        /// <summary>
        /// Get the transmission policy set for the player.
        /// <returns>The active TransmissionMode</returns>
        /// <see cref="SetTransmissionMode" />
        /// </summary>
        TransmissionMode TransmissionType { get; }
        /// <summary>
        /// Get all channels transmitting channels.
        /// <returns>A readonly collection of the currently transmitting channels</returns>
        /// </summary>
        ReadOnlyCollection<ChannelId> TransmittingChannels { get; }
        /// <summary>
        /// The current status of injected audio.
        /// </summary>
        bool IsInjectingAudio { get; }
        /// <summary>
        /// The online status that is sent to those accounts subscribing to the presence of this account.
        /// </summary>
        Presence Presence { get; set; }
        /// <summary>
        /// The unique identifier for this login session.
        /// </summary>
        AccountId LoginSessionId { get; }
        /// <summary>
        /// Specifies how often the SDK will send participant property events while in a channel.
        /// </summary>
        /// <remarks>
        /// <para>Only use this property to set the update frequency before login.  After login, the <see cref="BeginAccountSetLoginProperties" /> call must be used.</para>
        /// <para>Participant property events by default are only sent on participant state change (starts talking, stops talking, is muted, is unmuted). If set to a per second rate, messages will be sent at that rate if there has been a change since the last update message. This is always true unless the participant is muted through the SDK, causing no audio energy and no state changes.</para>
        /// <para>WARNING: Setting this value a non-default value will increase user and server traffic. It should only be done if a real-time visual representation of audio values are needed (e.g., graphic VAD indicator). For a static VAD indicator, the default setting is correct.</para>
        /// </remarks>
        ParticipantPropertyUpdateFrequency ParticipantPropertyFrequency { get; set; }
        /// <summary>
        /// The text-to-speech subsystem instance that is associated with this Login session.
        /// </summary>
        ITextToSpeech TTS { get; }
        /// <summary>
        /// The current state of the connection recovery process.
        /// </summary>
        ConnectionRecoveryState RecoveryState { get; }
        #endregion
        #region Methods

        /// <summary>
        /// Handles logging in this session when presence (subscriptions) are desired.
        /// </summary>
        /// <param name="subscriptionMode">how to handle incoming subscriptions.</param>
        /// <param name="presenceSubscriptions">A list of accounts for which this user wishes to monitor online status.</param>
        /// <param name="blockedPresenceSubscriptions">A list of accounts that are not allwed to see this user's online status.</param>
        /// <param name="allowedPresenceSubscriptions">A list of accounts that are allowed to see this user's online status.</param>
        /// <param name="accessToken">an access token provided by your game server that enables this login.</param>
        /// <param name="callback">a delegate to call when this operation completes.</param>
        /// <returns>Task will return when the you are fully logged in</returns>
        Task LoginAsync(
            SubscriptionMode subscriptionMode = SubscriptionMode.Accept,
            IReadOnlyHashSet<AccountId> presenceSubscriptions = null,
            IReadOnlyHashSet<AccountId> blockedPresenceSubscriptions = null,
            IReadOnlyHashSet<AccountId> allowedPresenceSubscriptions = null,
            string accessToken = null,
            AsyncCallback callback = null
        );

        /// <summary>
        /// Send a message to the specific account
        /// </summary>
        /// <param name="accountId">The intended recipient of the message</param>
        /// <param name="message">The body of the message to be sent</param>
        /// <param name="options">An optional parameter for adding metadata to the message</param>
        /// <returns></returns>
        Task SendDirectedMessageAsync(AccountId accountId, string message, MessageOptions options);

        /// <summary>
        /// Begin logging in this session when presence (subscriptions) are desired.
        /// </summary>
        /// <param name="accessToken">an access token provided by your game server that enables this login.</param>
        /// <param name="subscriptionMode">how to handle incoming subscriptions.</param>
        /// <param name="presenceSubscriptions">A list of accounts for which this user wishes to monitor online status.</param>
        /// <param name="blockedPresenceSubscriptions">A list of accounts that are not allwed to see this user's online status.</param>
        /// <param name="allowedPresenceSubscriptions">A list of accounts that are allowed to see this user's online status.</param>
        /// <param name="callback">a delegate to call when this operation completes.</param>
        /// <returns>IAsyncResult</returns>
        /// <remarks>
        /// This version of BeginLogin does not require a Uri server argument. It is intended to be used with Unity Game Services.
        /// If you are manually initializing Vivox, please continue to use BeginLogin signature that requires the Uri server argument.
        /// Developer of games that do not have secure communications requirements can use <see cref="GetLoginToken" /> to generate the required access token.
        /// </remarks>
        IAsyncResult BeginLogin(
            string accessToken,
            SubscriptionMode subscriptionMode,
            IReadOnlyHashSet<AccountId> presenceSubscriptions,
            IReadOnlyHashSet<AccountId> blockedPresenceSubscriptions,
            IReadOnlyHashSet<AccountId> allowedPresenceSubscriptions,
            AsyncCallback callback = null);

        /// <summary>
        /// Begin logging in this session when presence (subscriptions) are desired.
        /// </summary>
        /// <param name="server">The URI of the Vivox instance assigned to you.</param>
        /// <param name="accessToken">an access token provided by your game server that enables this login.</param>
        /// <param name="subscriptionMode">how to handle incoming subscriptions.</param>
        /// <param name="presenceSubscriptions">A list of accounts for which this user wishes to monitor online status.</param>
        /// <param name="blockedPresenceSubscriptions">A list of accounts that are not allwed to see this user's online status.</param>
        /// <param name="allowedPresenceSubscriptions">A list of accounts that are allowed to see this user's online status.</param>
        /// <param name="callback">a delegate to call when this operation completes.</param>
        /// <returns>IAsyncResult</returns>
        /// <remarks>
        /// Developer of games that do not have secure communications requirements can use <see cref="GetLoginToken" /> to generate the required access token.
        /// </remarks>
        IAsyncResult BeginLogin(
            Uri server,
            string accessToken,
            SubscriptionMode subscriptionMode,
            IReadOnlyHashSet<AccountId> presenceSubscriptions,
            IReadOnlyHashSet<AccountId> blockedPresenceSubscriptions,
            IReadOnlyHashSet<AccountId> allowedPresenceSubscriptions,
            AsyncCallback callback = null);

        /// <summary>
        /// Begin logging in this session for text and voice only (no subscriptions possible).
        /// </summary>
        /// <param name="server">The URI of the Vivox instance assigned to you</param>
        /// <param name="accessToken">an access token provided by your game server that enables this login</param>
        /// <param name="callback">a delegate to call when this operation completes</param>
        /// <returns>IAsyncResult</returns>
        /// <remarks>
        /// Developer of games that do not have secure communications requirements can use <see cref="GetLoginToken" /> to generate the required access token.
        /// </remarks>
        IAsyncResult BeginLogin(
            Uri server,
            string accessToken,
            AsyncCallback callback = null);

        /// <summary>
        /// To be called by the consumer of this class when BeginLogin() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginLogin() or provided to the callback delegate</param>
        void EndLogin(IAsyncResult result);

        /// <summary>
        /// Called to change login properties when already logged in
        /// </summary>
        /// <param name="participantPropertyFrequency">How often the SDK will send participant property events while in a channel</param>
        /// <param name="callback">A delegate to call when this operation completes</param>
        /// <returns>IAsyncResult</returns>
        /// <remarks>
        /// <para>Only use this property to set the update frequency after login.  Before login, the <see cref="ParticipantPropertyFrequency" /> property must be used.</para>
        /// <para>Participant property events by default are only sent on participant state change (starts talking, stops talking, is muted, is unmuted). If set to a per second rate, messages will be sent at that rate if there has been a change since the last update message. This is always true unless the participant is muted through the SDK, causing no audio energy and no state changes.</para>
        /// <para>WARNING: Setting this value a non-default value will increase user and server traffic. It should only be done if a real-time visual representation of audio values are needed (e.g., graphic VAD indicator). For a static VAD indicator, the default setting is correct.</para>
        /// </remarks>
        IAsyncResult BeginAccountSetLoginProperties(ParticipantPropertyUpdateFrequency participantPropertyFrequency, AsyncCallback callback);
        /// <summary>
        /// To be called by the consumer of this class when BeginAccountSetLoginProperties() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginAccountSetLoginProperties() or provided to the callback delegate</param>
        void EndAccountSetLoginProperties(IAsyncResult result);

        /// <summary>
        /// Gets the channel session for this channelId, creating one if necessary
        /// </summary>
        /// <param name="channelId">the id of the channel</param>
        /// <returns>the channel session</returns>
        IChannelSession GetChannelSession(ChannelId channelId);
        /// <summary>
        /// Deletes the channel session for this channelId, disconnecting the session if necessary
        /// </summary>
        /// <param name="channelId">the id of the channel</param>
        void DeleteChannelSession(ChannelId channelId);

        /// <summary>
        /// Deletes the channel session for this channelId asynchronously, disconnecting the session if necessary
        /// </summary>
        /// <param name="channelId">the id of the channel</param>
        Task DeleteChannelSessionAsync(ChannelId channelId);

        /// <summary>
        /// Block incoming subscription requests from the specified account
        /// </summary>
        /// <param name="accountId">the account id to block</param>
        /// <param name="callback">a delegate to call when this operation completes</param>
        /// <returns>IAsyncResult</returns>
        IAsyncResult BeginAddBlockedSubscription(AccountId accountId, AsyncCallback callback);
        /// <summary>
        /// To be called by the consumer of this class when BeginAddBlockedSubscription() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginAddBlockedSubscription() or provided to the callback delegate</param>
        void EndAddBlockedSubscription(IAsyncResult result);
        /// <summary>
        /// Unblock incoming subscription requests from the specified account. Subscription requests from the specified account will cause an event to be raised to the application.
        /// </summary>
        /// <param name="accountId">the account id to unblock</param>
        /// <param name="callback">a delegate to call when this operation completes</param>
        /// <returns>IAsyncResult</returns>
        IAsyncResult BeginRemoveBlockedSubscription(AccountId accountId, AsyncCallback callback);
        /// <summary>
        /// To be called by the consumer of this class when BeginRemoveBlockedSubscription() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginRemoveBlockedSubscription() or provided to the callback delegate</param>
        void EndRemoveBlockedSubscription(IAsyncResult result);

        /// <summary>
        /// Allow incoming subscription requests from the specified account
        /// </summary>
        /// <param name="accountId">the account id to allow</param>
        /// <param name="callback">a delegate to call when this operation completes</param>
        /// <returns>IAsyncResult</returns>
        IAsyncResult BeginAddAllowedSubscription(AccountId accountId, AsyncCallback callback);
        /// <summary>
        /// To be called by the consumer of this class when BeginAddAllowedSubscription() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginAddAllowedSubscription() or provided to the callback delegate</param>
        void EndAddAllowedSubscription(IAsyncResult result);
        /// <summary>
        /// Disallow incoming subscription requests from the specified account. Subscription requests from the specified account will cause an event to be raised to the application.
        /// </summary>
        /// <param name="accountId">the account id to disallow</param>
        /// <param name="callback">a delegate to call when this operation completes</param>
        /// <returns>IAsyncResult</returns>
        IAsyncResult BeginRemoveAllowedSubscription(AccountId accountId, AsyncCallback callback);
        /// <summary>
        /// To be called by the consumer of this class when BeginRemoveAllowedSubscription() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginRemoveAllowedSubscription() or provided to the callback delegate</param>
        void EndRemoveAllowedSubscription(IAsyncResult result);
        /// <summary>
        /// Subscribe to the specified account
        /// </summary>
        /// <param name="accountId">the account id to subscribe to</param>
        /// <param name="callback">a delegate to call when this operation completes</param>
        /// <remarks>This method will automatically allow accountId to see the subscriber's online status</remarks>
        /// <returns>IAsyncResult</returns>
        IAsyncResult BeginAddPresenceSubscription(AccountId accountId, AsyncCallback callback);
        /// <summary>
        /// To be called by the consumer of this class when BeginAddPresenceSubscription() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginAddPresenceSubscription() or provided to the callback delegate</param>
        /// <returns>The presence subscription for the account id</returns>
        IPresenceSubscription EndAddPresenceSubscription(IAsyncResult result);
        /// <summary>
        /// Unsubscribe from the specified account
        /// </summary>
        /// <param name="accountId">the account id to subscribe to</param>
        /// <param name="callback">a delegate to call when this operation completes</param>
        /// <returns>IAsyncResult</returns>
        IAsyncResult BeginRemovePresenceSubscription(AccountId accountId, AsyncCallback callback);
        /// <summary>
        /// To be called by the consumer of this class when BeginRemovePresenceSubscription() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginRemovePresenceSubscription() or provided to the callback delegate</param>
        void EndRemovePresenceSubscription(IAsyncResult result);


        /// <summary>
        /// Send a message to the specific account
        /// </summary>
        /// <param name="accountId">the intended recipient of the message</param>
        /// <param name="language">the language of the message e.g "en". This can be null to use the default language ("en" for most systems). This must conform to RFC5646 (https://tools.ietf.org/html/rfc5646)</param>
        /// <param name="message">the body of the message</param>
        /// <param name="applicationStanzaNamespace">an optional namespace element for additional application data</param>
        /// <param name="applicationStanzaBody">the additional application data body</param>
        /// <param name="callback">a delegate to call when this operation completes</param>
        /// <returns>The AsyncResult</returns>
        IAsyncResult BeginSendDirectedMessage(AccountId accountId, string language, string message, string applicationStanzaNamespace, string applicationStanzaBody, AsyncCallback callback);

        /// <summary>
        /// Send a message to the specific account
        /// </summary>
        /// <param name="accountId">the intended recipient of the message</param>
        /// <param name="message">the body of the message</param>
        /// <param name="callback">a delegate to call when this operation completes</param>
        /// <param name="options">An optional parameter for adding metadata to the message</param>
        /// <returns>The AsyncResult</returns>
        IAsyncResult BeginSendDirectedMessage(AccountId accountId, string message, MessageOptions options, AsyncCallback callback);
        /// <summary>
        /// To be called by the consumer of this class when BeginSendDirectedMessage() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginSendDirectedMessage() or provided to the callback delegate</param>
        void EndSendDirectedMessage(IAsyncResult result);

        /// <summary>
        /// Edits a Directed Message with the new message text.
        /// </summary>
        /// <param name="messageId">The message identifier of the message to edit.</param>
        /// <param name="newMessage">The new text to replace for a given message.</param>
        /// <returns>Returns when the message has been successfully edited</returns>
        Task EditDirectTextMessageAsync(string messageId, string newMessage);
        /// <summary>
        /// Deletes a Directed Message by id.
        /// </summary>
        /// <param name="messageId">The message identifier of the message to delete</param>
        /// <returns></returns>
        Task DeleteDirectTextMessageAsync(string messageId);

        Task<ReadOnlyCollection<VivoxConversation>> GetConversationsAsync(ConversationQueryOptions options = null);

        /// <summary>
        /// Start a query of archived directed messages.
        /// </summary>
        /// <param name="timeStart">Results filtering: Only messages on or after the given date/time will be returned.  For no start limit, use null.</param>
        /// <param name="timeEnd">Results filtering: Only messages before the given date/time will be returned.  For no end limit, use null.</param>
        /// <param name="searchText">Results filtering: Only messages containing the specified text will be returned.  For order matching, use double-quotes around the search terms.  For no text filtering, use null.</param>
        /// <param name="userId">Results filtering: Only messages to/from the specified participant will be returned.  If this parameter is set, channel must be null.  For no participant filtering, use null.</param>
        /// <param name="channel">Results filtering: Only messages to/from the specified channel will be returned.  If this parameter is set, userId must be null.  For no channel filtering, use null.</param>
        /// <param name="max">Results paging: The maximum number of messages to return (up to 50).  If more than 50 messages are needed, multiple queries must be performed.  Use 0 to get total messages count without retrieving them.</param>
        /// <param name="afterId">Results paging: Only messages following the specified message id will be returned in the result set.  If this parameter is set, beforeId must be null.  For no lower limit, use null.</param>
        /// <param name="beforeId">Results paging: Only messages preceding the specified message id will be returned in the result set.  If this parameter is set, afterId must be null.  For no upper limit, use null.</param>
        /// <param name="firstMessageIndex">Results paging: The server side index (not message ID) of the first message to retrieve.  The first message in the result set always has an index of 0.  For no starting message, use -1.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>The AsyncResult.</returns>
        /// <exception cref="ArgumentException">Thrown when max value too large.</exception>
        /// <exception cref="ArgumentException">Thrown when afterId and beforeId are used at the same time.</exception>
        /// <exception cref="ArgumentException">Thrown when userId and channel are used at the same time.</exception>
        [Obsolete("This feature is being deprecated in favor of the limited beta release of Chat History. Please contact Unity Support for feature enablement.")]
        IAsyncResult BeginAccountArchiveQuery(DateTime? timeStart, DateTime? timeEnd, string searchText,
            AccountId userId, ChannelId channel, uint max, string afterId, string beforeId, int firstMessageIndex,
            AsyncCallback callback);
        /// <summary>
        /// To be called by the consumer of this class when BeginArchiveArchiveQuery() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginAccountArchiveQuery() or provided to the callback delegate.</param>
        [Obsolete("This feature is being deprecated in favor of the limited beta release of Chat History. Please contact Unity Support for feature enablement.")]
        void EndAccountArchiveQuery(IAsyncResult result);

        /// <summary>
        /// Fetch Direct Text Messages at an account level.  Use <see cref="ChatHistoryQueryOptions">chatHistoryQueryOptions</see> to filter what is returned.
        /// </summary>
        /// <param name="recipient">The <see cref="AccountId"/> of the logged in user you would like to search for chat history.  If this value is set then it takes priority over the <see cref="ChatHistoryQueryOptions">chatHistoryQueryOptions</see> value for PlayerId and it will be ignored.
        /// Otherwise that PlayerId will be used in the <see cref="ChatHistoryQueryOptions">chatHistoryQueryOptions</see> PlayerId is used.</param>
        /// <param name="requestSize">The maximum number of messages to return.  The larger this value is the longer the query will take to complete.  Default is 10 however keeping this number low is ideal</param>
        /// <param name="chatHistoryQueryOptions"><see cref="ChatHistoryQueryOptions"/> is used to customize the history results returned</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>Task with the ReadOnlyCollection of <see cref="VivoxMessage"/></returns>
        Task<ReadOnlyCollection<VivoxMessage>> GetDirectTextMessageHistoryAsync(AccountId recipient = null, int requestSize = 10, ChatHistoryQueryOptions chatHistoryQueryOptions = null, AsyncCallback callback = null);

        /// <summary>
        /// Sets a message as read.
        /// </summary>
        /// <param name="message">The message you'd like to set as read.</param>
        /// <param name="seenAt">Optional time to set the message as read. If not provided, the current UTC time will be used.</param>
        /// <returns></returns>
        Task SetMessageAsReadAsync(VivoxMessage message, DateTime? seenAt = null);

        /// <summary>
        /// Set whether microphone audio and injected audio should be transmitted to no channels, all channels, or a single specific channel.
        /// </summary>
        /// <param name="mode">enum specifying a transmission policy</param>
        /// <param name="singleChannel">the specific channel to transmit to when TransmissionMode::Single is set (ignored otherwise)</param>
        /// <remarks>To be used only by applications without secure communications requirements.</remarks>
        /// Audio transmission changes take effect immediately in all channels this user is already connected to. The changes also affect future channels joined as follows:
        /// - <b>None:</b> audio will not automatically transmit to new channels nor to text-only channels when audio is added.
        /// - <b>All:</b> audio automatically transmits to new channels and to text-only channels when audio is added.
        /// - <b>Single:</b> audio will transmit into the channel specified only, and will not automatically switch to new channels even if this channel is disconnected.
        ///
        /// <b>Important:</b> You can override and change this setting by passing `true` for the switchTransmission argument in IChannelSession::BeginConnect() and IChannelSession::BeginSetAudioConnected().
        void SetTransmissionMode(TransmissionMode mode, ChannelId singleChannel = null);

        /// <summary>
        /// Set whether microphone audio and injected audio should be transmitted to no channels, all channels, or a single specific channel.
        /// </summary>
        /// <param name="mode">enum specifying a transmission policy</param>
        /// <param name="singleChannel">the specific channel to transmit to when TransmissionMode::Single is set (ignored otherwise)</param>
        /// <remarks>To be used only by applications without secure communications requirements.</remarks>
        /// Audio transmission changes take effect immediately in all channels this user is already connected to. The changes also affect future channels joined as follows:
        /// - <b>None:</b> audio will not automatically transmit to new channels nor to text-only channels when audio is added.
        /// - <b>All:</b> audio automatically transmits to new channels and to text-only channels when audio is added.
        /// - <b>Single:</b> audio will transmit into the channel specified only, and will not automatically switch to new channels even if this channel is disconnected.
        ///
        /// <b>Important:</b> You can override and change this setting by passing `true` for the switchTransmission argument in IChannelSession::BeginConnect() and IChannelSession::BeginSetAudioConnected().
        Task SetTransmissionModeAsync(TransmissionMode mode, ChannelId singleChannel = null);

        /// <summary>
        /// Enable or disable automatic voice activity detection.
        /// If Auto VAD is enabled, the properties set via <see cref="SetVADPropertiesAsync"/> will have no effect.
        /// </summary>
        /// <param name="onOff">the intended recipient of the message</param>
        Task SetAutoVADAsync(bool onOff);

        /// <summary>
        /// Sets voice activity detection parameters.
        /// </summary>
        /// <param name="hangover">
        /// The hangover time is the time (in milliseconds) it takes for the VAD to switch back from speech mode to silence after the last speech frame is detected. The default value is 2000.
        /// </param>
        /// <param name="noiseFloor">
        /// The noise floor is a dimensionless value between 0 and 20000 that controls how the VAD separates speech from background noise.
        /// Lower values assume the user is in a quieter environment where the audio is only speech.
        /// Higher values assume a noisy background environment.The default value is 576.
        ///
        /// Note: Changes to the VAD noiseFloor settings do not affect currently joined channels.
        /// If the ability to change VAD settings is available to the end-user, indicate that noiseFloor changes only take effect
        /// the next time a voice channel is joined or only allow changing the noiseFloor when the client is not in a voice channel.
        /// </param>
        /// <param name="sensitivity">
        /// The sensitivity is a dimensionless value between 0 and 100 that indicates the sensitivity of the VAD.
        /// Increasing this value corresponds to decreasing the sensitivity of the VAD (0 is the most sensitive, and 100 is the least sensitive).
        /// Higher values of sensitivity require louder audio to trigger the VAD. The default value is 43.
        /// </param>
        Task SetVADPropertiesAsync(int hangover = 2000, int noiseFloor = 576, int sensitivity = 43);

        /// <summary>
        /// Sets Vivox Safe Voice consent status to a given consent
        /// </summary>
        /// <param name="environmentId">The project's environment Id</param>
        /// <param name="projectId">The project's ProjectId</param>
        /// <param name="playerId">The PlayerId associated with the AuthToken</param>
        /// <param name="authToken">A valid AuthToken for the PlayerId</param>
        /// <param name="consentToSet">The consent status to set</param>
        /// <returns>Bool delineating the current consent status of the user</returns>
        Task<bool> SetSafeVoiceConsentStatus(string environmentId, string projectId, string playerId, string authToken, bool consentToSet);

        /// <summary>
        /// Gets the current Vivox Safe Voice consent of a given PlayerId
        /// </summary>
        /// <param name="environmentId">The project's environment Id</param>
        /// <param name="projectId">The project's ProjectId</param>
        /// <param name="playerId">The PlayerId associated with the AuthToken</param>
        /// <param name="authToken">A valid AuthToken for the PlayerId</param>
        /// <returns>Bool delineating the current consent status of the user</returns>
        Task<bool> GetSafeVoiceConsentStatus(string environmentId, string projectId, string playerId, string authToken);

        /// <summary>
        /// Log the account out of the Vivox system.
        /// </summary>
        void Logout();

        /// <summary>
        /// Log the account out of the Vivox system asynchronously.
        /// </summary>
        Task LogoutAsync();

        /// <summary>
        /// Get a login token for this account.
        /// </summary>
        /// <param name="tokenExpirationDuration">the length of time the token is valid for - will default to 60 seconds</param>
        /// <returns>an access token that can be used to log this account in</returns>
        /// <remarks>To be used only by applications without secure communications requirements.</remarks>
        string GetLoginToken(TimeSpan? tokenExpirationDuration = null);

        /// <summary>
        /// Get a login token for this account.
        /// </summary>
        /// <param name="tokenExpirationDuration">the length of time the token is valid for - will default to 60 seconds</param>
        /// <returns>an access token that can be used to log this account in</returns>
        /// <remarks>To be used only by applications without secure communications requirements.</remarks>
        string GetLoginToken(string tokenSigningKey, TimeSpan tokenExpirationDuration);

        /// <summary>
        /// This function allows you to start audio injection
        /// </summary>
        /// <param name="audioFilePath">The full pathname for the WAV file to use for audio injection (MUST be single channel, 16-bit PCM, with the same sample rate as the negotiated audio codec) required for start</param>
        void StartAudioInjection(string audioFilePath);

        /// <summary>
        /// This function allows you to stop audio injection
        /// </summary>
        void StopAudioInjection();

        #region Cross Mute/Control Communications Operations

        /// <summary>
        /// "Block" an AccountId, bidirectionally muting audio/text between that account and this login session
        /// </summary>
        /// <param name="accountId">The AccountId to bidirectionally mute or unmute</param>
        /// <returns></returns>
        Task BlockPlayerAsync(AccountId accountId, bool blockStatus, AsyncCallback callback = null);

        /// <summary>
        /// "Cross mute" an AccountId, bidirectionally muting audio/text between that account and this login session
        /// </summary>
        /// <param name="accountId">The AccountId to bi-directionally mute or unmute</param>
        /// <param name="muted">The status to set, with true as muted and false unmuted</param>
        /// <returns>The AsyncResult.</returns>
        IAsyncResult SetCrossMutedCommunications(AccountId accountId, bool muted, AsyncCallback callback);

        /// <summary>
        /// "Cross mute" a set of AccountIds, bidirectionally muting audio/text between those accounts and this login session
        /// </summary>
        /// <param name="accountIdSet">The set of AccountIds to bi-directionally mute or unmute</param>
        /// <param name="muted">The status to set, with true as muted and false unmuted</param>
        /// <returns>The AsyncResult.</returns>
        IAsyncResult SetCrossMutedCommunications(List<AccountId> accountIdSet, bool muted, AsyncCallback callback);

        /// <summary>
        /// Clear the bi-directionally muted communications list, unmuting all AccountIds and allowing audio/text through any way not otherwise prevented
        /// </summary>
        /// <returns>The AsyncResult.</returns>
        IAsyncResult ClearCrossMutedCommunications(AsyncCallback callback);

        #endregion

        #endregion
    }
}
