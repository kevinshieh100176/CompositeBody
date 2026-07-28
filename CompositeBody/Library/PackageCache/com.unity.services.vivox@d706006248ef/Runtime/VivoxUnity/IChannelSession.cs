using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// A connection to a channel.
    /// </summary>
    internal interface IChannelSession : IKeyedItemNotifyPropertyChanged<ChannelId>
    {
        /// <summary>
        /// A handle for the channel session.
        /// </summary>
        string SessionHandle { get; }

        /// <summary>
        /// The login session that owns this channel session.
        /// </summary>
        ILoginSession Parent { get; }

        /// <summary>
        /// The state of the audio portion of this channel session.
        /// </summary>
        /// <remarks>Changes to this value can occur at any time due to network or moderator events.</remarks>
        ConnectionState AudioState { get; }

        /// <summary>
        /// The state of the text portion of this channel session.
        /// </summary>
        /// <remarks>Changes to this value can occur at any time due to network or moderator events.</remarks>
        ConnectionState TextState { get; }

        /// <summary>
        /// The state of the overall connection to this channel session.
        /// </summary>
        /// <remarks>Changes to this value can occur at any time due to network or moderator events.</remarks>
        ConnectionState ChannelState { get; }

        /// <summary>
        /// The list of participants in this channel, including the current user.
        /// </summary>
        /// <remarks>Use IReadOnlyDictionary events to get participant notifications, such as when participants are joining, leaving, or speaking.</remarks>
        IReadOnlyDictionary<string, IParticipant> Participants { get; }

        /// <summary>
        /// The list of incoming messages.
        /// </summary>
        /// <remarks>Use IReadOnlyQueue events to get notifications of incoming text messages.</remarks>
        IReadOnlyQueue<IChannelTextMessage> MessageLog { get; }

        /// <summary>
        /// An event indicating which message has been edited for a given channel.
        /// </summary>
        /// <remarks>Use VivoxMessage events to get notifications of edited text messages.</remarks>
        event EventHandler<VivoxMessage> MessageEdited;

        /// <summary>
        /// An event indicating which message has been deleted for a given channel.
        /// </summary>
        /// <remarks>Use VivoxMessage events to get notifications of deleted text messages.</remarks>
        event EventHandler<VivoxMessage> MessageDeleted;

        /// <summary>
        /// The list of session archive messages returned by a BeginSessionArchiveQuery.
        /// </summary>
        /// <remarks>Use IReadOnlyQueue events to get notifications of incoming messages from a session archive query. Note that this is not automatically cleared when starting a new BeginSessionArchiveQuery.</remarks>
        [Obsolete("This feature is being deprecated in favor of the limited beta release of Chat History. Please contact Unity Support for feature enablement.")]
        IReadOnlyQueue<ISessionArchiveMessage> SessionArchive { get; }

        /// <summary>
        /// The result set when all messages have been returned from a BeginSessionArchiveQuery.
        /// </summary>
        /// <remarks>Use the PropertyChanged event to get notified when a session archive query has started or completed.</remarks>
        [Obsolete("This feature is being deprecated in favor of the limited beta release of Chat History. Please contact Unity Support for feature enablement.")]
        IArchiveQueryResult SessionArchiveResult { get; }

        /// <summary>
        /// The list of incoming transcribed messages.
        /// </summary>
        /// <remarks>Use IReadOnlyQueue events to get notifications of incoming transcribed messages.</remarks>
        IReadOnlyQueue<ITranscribedMessage> TranscribedLog { get; }

        /// <summary>
        /// Indicates that microphone audio and injected audio will transmit to this session.
        /// </summary>
        /// <remarks>Setting this value to true clears this value from all other sessions.</remarks>
        bool IsTransmitting { get; }

        /// <summary>
        /// Indicates that this session is being transcribed.
        /// </summary>
        /// <remarks>To set this value, use <see cref="BeginSetChannelTranscription"/>.</remarks>
        bool IsSessionBeingTranscribed { get; }

        /// <summary>
        /// The channel ID of this session.
        /// </summary>
        ChannelId Channel { get; }

        /// <summary>
        /// Perform the initial connection to the channel.
        /// </summary>
        /// <param name="connectAudio">True to connect audio.</param>
        /// <param name="connectText">True to connect text.</param>
        /// <param name="switchTransmission">Whether to transmit in this channel. Transmitting in one channel stops transmitting in other channels.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <param name="accessToken">The access token that grants the user access to the channel. If not provided, a debug token will automatically be created for the developer.</param>
        /// <returns></returns>
        Task ConnectAsync(bool connectAudio, bool connectText, bool switchTransmission, AsyncCallback callback = null, string accessToken = null);

        /// <summary>
        /// Send a message to this channel.
        /// </summary>
        /// <param name="message">The message to be sent to this channel</param>
        /// <param name="options">An optional parameter for adding metadata to the message</param>
        /// <returns></returns>
        Task SendChannelMessageAsync(string message, MessageOptions options);

        /// <summary>
        /// Perform the initial connection to the channel.
        /// </summary>
        /// <param name="connectAudio">True to connect audio.</param>
        /// <param name="connectText">True to connect text.</param>
        /// <param name="switchTransmission">Whether to transmit in this channel. Transmitting in one channel stops transmitting in other channels.</param>
        /// <param name="accessToken">The access token that grants the user access to the channel.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>The AsyncResult.</returns>
        /// <remarks>
        /// Developers of games that do not have secure communications requirements can use <see cref="GetConnectToken" /> to generate the required access token.
        /// </remarks>
        IAsyncResult BeginConnect(bool connectAudio, bool connectText, bool switchTransmission, string accessToken, AsyncCallback callback);

        /// <summary>
        /// The consumer of this class should call this when BeginConnect() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginConnect() or provided to the callback delegate.</param>
        void EndConnect(IAsyncResult result);

        /// <summary>
        /// Disconnect the user from the channel as an async Task.
        /// </summary>
        /// <returns>A task for the operation</returns>
        Task DisconnectAsync();

        /// <summary>
        /// Disconnect the user from this channel.
        /// </summary>
        /// <remarks>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>The AsyncResult.</returns>
        /// The AudioState and TextState properties are not set to ConnectionState.Disconnected until it is OK to rejoin this channel. The Application must monitor property changes for these properties to determine when it is OK to rejoin the channel. This object remains in the ILoginSession.ChannelSessions list. Use ILoginSession.DeleteChannelSession to remove it from the list.
        /// </remarks>
        IAsyncResult Disconnect(AsyncCallback callback = null);

        /// <summary>
        /// Add or remove audio from the channel session.
        /// </summary>
        /// <param name="value">True to add audio, false to remove audio.</param>
        /// <param name="transmit">Whether to transmit in this channel. Transmitting in one channel stops transmitting in other channels.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>The AsyncResult.</returns>
        IAsyncResult BeginSetAudioConnected(bool value, bool switchTransmission, AsyncCallback callback);

        /// <summary>
        /// The consumer of this class should call this when BeginSetAudioConnected() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginSetAudioConnected() or provided to the callback delegate.</param>
        void EndSetAudioConnected(IAsyncResult result);

        /// <summary>
        /// Add or remove text from the channel session.
        /// </summary>
        /// <param name="value">True to add text, false to remove text.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>The AsyncResult.</returns>
        IAsyncResult BeginSetTextConnected(bool value, AsyncCallback callback);

        /// <summary>
        /// The consumer of this class should call this when BeginSetTextConnected() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginSetTextConnected() or provided to the callback delegate.</param>
        void EndSetTextConnected(IAsyncResult result);

        /// <summary>
        /// Send a message to this channel.
        /// </summary>
        /// <param name="message">The body of the message.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <param name="options">An optional parameter for adding metadata to the message</param>
        /// <returns>The AsyncResult.</returns>
        IAsyncResult BeginSendText(string message, MessageOptions options, AsyncCallback callback);

        /// <summary>
        /// Send a message to this channel.
        /// </summary>
        /// <param name="language">The language of the message, for example, "en". This can be null to use the default language ("en" for most systems). This must conform to <a href="https://tools.ietf.org/html/rfc5646">RFC 5646</a>.</param>
        /// <param name="message">The body of the message.</param>
        /// <param name="applicationStanzaNamespace">An optional namespace element for additional application data.</param>
        /// <param name="applicationStanzaBody">The additional application data body.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>The AsyncResult.</returns>
        IAsyncResult BeginSendText(string language, string message, string applicationStanzaNamespace, string applicationStanzaBody, AsyncCallback callback);

        /// <summary>
        /// The consumer of this class should call this when BeginSendText() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginSendText() or provided to the callback delegate.</param>
        void EndSendText(IAsyncResult result);

        /// <summary>
        /// Start a query of archived channel messages.
        /// </summary>
        /// <param name="timeStart">Results filtering: Only messages on or after the given date/time are returned. For no start limit, use null.</param>
        /// <param name="timeEnd">Results filtering: Only messages before the given date/time are returned. For no end limit, use null.</param>
        /// <param name="searchText">Results filtering: Only messages that contain the specified text are returned. For order matching, use double-quotes around the search terms. For no text filtering, use null.</param>
        /// <param name="userId">Results filtering: Only messages to/from the specified participant are returned. For no participant filtering, use null.</param>
        /// <param name="max">Results paging: The maximum number of messages to return (up to 50). If more than 50 messages are needed, then you must perform multiple queries. Use 0 to get the total message count without retrieving them.</param>
        /// <param name="afterId">Results paging: Only messages following the specified message ID are returned in the result set. If this parameter is set, beforeId must be null. For no lower limit, use null.</param>
        /// <param name="beforeId">Results paging: Only messages preceding the specified message ID are returned in the result set. If this parameter is set, afterId must be null. For no upper limit, use null.</param>
        /// <param name="firstMessageIndex">Results paging: The server-side index (not message ID) of the first message to retrieve. The first message in the result set always has an index of 0. For no starting message, use -1.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>The AsyncResult.</returns>
        /// <exception cref="ArgumentException">Thrown when the maximum value is too large.</exception>
        /// <exception cref="ArgumentException">Thrown when afterId and beforeId are used at the same time.</exception>
        [Obsolete("This feature is being deprecated in favor of the limited beta release of Chat History. Please contact Unity Support for feature enablement.")]
        IAsyncResult BeginSessionArchiveQuery(DateTime? timeStart, DateTime? timeEnd, string searchText,
            AccountId userId, uint max, string afterId, string beforeId, int firstMessageIndex,
            AsyncCallback callback);

        /// <summary>
        /// The consumer of this class should call this when BeginSessionArchiveQuery() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginSessionArchiveQuery() or provided to the callback delegate.</param>
        [Obsolete("This feature is being deprecated in favor of the limited beta release of Chat History. Please contact Unity Support for feature enablement.")]
        void EndSessionArchiveQuery(IAsyncResult result);

        /// <summary>
        /// Fetch Channel Text Messages for a given session you are actively connected too.  Use <see cref="ChatHistoryQueryOptions">chatHistoryQueryOptions</see> to filter what is returned.
        /// </summary>
        /// <param name="requestSize">The maximum number of messages to return.  The larger this value is the longer the query will take to complete.  Default is 10 however keeping this number low is ideal</param>
        /// <param name="chatHistoryQueryOptions"><see cref="ChatHistoryQueryOptions"/> is used to customize the history results returned</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        /// <returns>Task<ReadOnlyCollection<VivoxMessage>> this will return a <see cref="ReadOnlyCollection{VivoxMessage}"/> of the completed message history results.</returns>
        Task<ReadOnlyCollection<VivoxMessage>> GetChannelTextMessageHistoryAsync(int requestSize = 10, ChatHistoryQueryOptions chatHistoryQueryOptions = null, AsyncCallback callback = null);

        /// <summary>
        /// Get a token that can be used to connect to this channel.
        /// </summary>
        /// <param name="tokenExpirationDuration">The length of time the token is valid for.</param>
        /// <returns>A token that can be used to join this channel.</returns>
        /// <remarks>To be used only by applications without secure communications requirements.</remarks>
        string GetConnectToken(TimeSpan? tokenExpirationDuration = null);

        /// <summary>
        /// Get a token that can be used to connect to this channel.
        /// </summary>
        /// <param name="tokenSigningKey">The key corresponding to the issuer for this account that is used to sign the token.</param>
        /// <param name="tokenExpirationDuration">The length of time the token is valid for.</param>
        /// <returns>A token that can be used to join this channel.</returns>
        /// <remarks>To be used only by applications without secure communications requirements.</remarks>
        string GetConnectToken(string tokenSigningKey, TimeSpan tokenExpirationDuration);

        /// <summary>
        /// Issue a request to set the listening and speaking positions of a user in a positional channel.
        /// </summary>
        /// <param name="speakerPos">The position of the virtual "mouth."</param>
        /// <param name="listenerPos">The position of the virtual "ear."</param>
        /// <param name="listenerAtOrient">A unit vector that represents the forward (Z) direction, or heading, of the listener.</param>
        /// <param name="listenerUpOrient">A unit vector that represents the up (Y) direction of the listener. Use Vector3(0, 1, 0) for a "global" up in the world space.</param>
        void Set3DPosition(Vector3 speakerPos, Vector3 listenerPos, Vector3 listenerAtOrient, Vector3 listenerUpOrient);

        /// <summary>
        /// Enables the speech-to-text audio transcription inside the channel the user is currently joined to.
        /// </summary>
        /// <param name="enable">True to enable transcription, false to disable transcription.</param>
        /// <returns>A task for the operation</returns>
        Task SpeechToTextEnableTranscription(bool enable);

        /// <summary>
        /// Issue a request to set transcription for this channel session.
        /// </summary>
        /// <param name="value">True to enable transcription, false to disable transcription.</param>
        /// <param name="accessToken">The access token that grants the user access to set transcription on the channel. For testing purposes, use <see cref="GetTranscriptionToken"/>.</param>
        /// <param name="callback">A delegate to call when this operation completes.</param>
        IAsyncResult BeginSetChannelTranscription(bool value, string accessToken, AsyncCallback callback);

        /// <summary>
        /// The consumer of this class should call this when BeginSetChannelTranscription() completes.
        /// </summary>
        /// <param name="result">The IAsyncResult object returned from BeginSetChannelTranscription() or provided to the callback delegate.</param>
        void EndSetChannelTranscription(IAsyncResult result);

        /// <summary>
        /// Get a token that can be used to set transcriptions in this channel.
        /// </summary>
        /// <param name="tokenSigningKey">The key that corresponds to the issuer for this account that is used to sign the token.</param>
        /// <param name="tokenExpirationDuration">The length of time the token is valid for.</param>
        /// <returns>A token that can be used to set channel transcription.</returns>
        /// <remarks>To be used only by applications without secure communications requirements.</remarks>
        string GetTranscriptionToken(TimeSpan? tokenExpirationDuration = null);

        /// <summary>
        /// Get a token that can be used to set transcriptions in this channel.
        /// </summary>
        /// <param name="tokenSigningKey">The key that corresponds to the issuer for this account that is used to sign the token.</param>
        /// <param name="tokenExpirationDuration">The length of time the token is valid for.</param>
        /// <returns>A token that can be used to set channel transcription.</returns>
        /// <remarks>To be used only by applications without secure communications requirements.</remarks>
        string GetTranscriptionToken(string tokenSigningKey, TimeSpan tokenExpirationDuration);

        /// <summary>
        /// Sets the volume of an entire channel.
        /// Volume value is clamped between -50 and 50 with a default of 0.
        /// </summary>
        Task SetVolumeAsync(int value);

        /// <summary>
        /// Edits a Directed Message with the new message text.
        /// </summary>
        /// <param name="messageId">The message identifier of the message to edit.</param>
        /// <param name="newMessage">The new text to replace for a given message.</param>
        /// <returns>Returns when the message has been successfully edited</returns>
        Task EditChannelTextMessageAsync(string messageId, string newMessage);

        /// <summary>
        /// Deletes a Directed Message by id.
        /// </summary>
        /// <param name="messageId">The message identifier of the message to delete</param>
        /// <returns></returns>
        Task DeleteChannelTextMessageAsync(string messageId);
    }
}
