using System;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// A text message.
    /// </summary>
    internal interface ITextMessage : IKeyedItemNotifyPropertyChanged<string>
    {
        /// <summary>
        /// The time when the message was received.
        /// </summary>
        DateTime ReceivedTime { get; }
        /// <summary>
        /// The message.
        /// </summary>
        string Message { get; }
        /// <summary>
        /// The language of the message.
        /// </summary>
        string Language { get; }
        /// <summary>
        /// Ths display name of the sender.
        /// </summary>
        string SenderDisplayName { get; }
    }

    /// <summary>
    /// A text message from one user to another user.
    /// </summary>
    internal interface IDirectedTextMessage : ITextMessage
    {
        /// <summary>
        /// The LoginSession that is the target of the message.
        /// </summary>
        ILoginSession LoginSession { get; }
        /// <summary>
        /// The message sender.
        /// </summary>
        AccountId Sender { get; }
        /// <summary>
        /// Indicates whether the message is from the currently signed in user.
        /// </summary>
        bool FromSelf { get; }
        /// <summary>
        /// An optional name space for application-specific data.
        /// </summary>
        string ApplicationStanzaNamespace { get; }
        /// <summary>
        /// Optional application-specific data.
        /// </summary>
        string ApplicationStanzaBody { get; }
    }

    /// <summary>
    /// A text message from one user to another user.
    /// </summary>
    internal interface IFailedDirectedTextMessage
    {
        /// <summary>
        /// The message sender.
        /// </summary>
        AccountId Sender { get; }
        /// <summary>
        /// Indicates whether the message is from the currently signed in user.
        /// </summary>
        bool FromSelf { get; }
        /// <summary>
        /// The request ID of the failed directed message.
        /// </summary>
        string RequestId { get; }
        /// <summary>
        /// The status code of the failure.
        /// </summary>
        int StatusCode { get; }
    }

    /// <summary>
    /// A text message from a channel.
    /// </summary>
    internal interface IChannelTextMessage : ITextMessage
    {
        /// <summary>
        /// The ChannelSession that is the target of the message.
        /// </summary>
        IChannelSession ChannelSession { get; }
        /// <summary>
        /// The message sender.
        /// </summary>
        AccountId Sender { get; }
        /// <summary>
        /// Indicates whether the message is from the currently signed in user.
        /// </summary>
        bool FromSelf { get; }
        /// <summary>
        /// An optional namespace for application-specific data.
        /// </summary>
        string ApplicationStanzaNamespace { get; }
        /// <summary>
        /// Optional application-specific data.
        /// </summary>
        string ApplicationStanzaBody { get; }
    }

    /// <summary>
    /// A text message from a session archive query.
    /// </summary>
    internal interface ISessionArchiveMessage : ITextMessage
    {
        /// <summary>
        /// The ChannelSession that is the target of the message.
        /// </summary>
        IChannelSession ChannelSession { get; }
        /// <summary>
        /// The message sender.
        /// </summary>
        AccountId Sender { get; }
        /// <summary>
        /// Indicates whether the message is from the currently signed in user.
        /// </summary>
        bool FromSelf { get; }
        /// <summary>
        /// The ID of the query that requested this message.
        /// </summary>
        string QueryId { get; }
        /// <summary>
        /// The server-assigned ID of the message used for paging through the large result sets.
        /// </summary>
        string MessageId { get; }
    }

    /// <summary>
    /// A text message from an account archive query.
    /// </summary>
    internal interface IAccountArchiveMessage : ITextMessage
    {
        /// <summary>
        /// The LoginSession that is the sender or receiver of this message.
        /// </summary>
        ILoginSession LoginSession { get; }
        /// <summary>
        /// The ID of the query that requested this message.
        /// </summary>
        string QueryId { get; }
        /// <summary>
        /// The message sender.
        /// </summary>
        AccountId Sender { get; }
        /// <summary>
        /// If a directed message, the remote participant who is the sender/receiver of the message for inbound/outbound messages, respectively. If this is a channel message, then this value is null.
        /// </summary>
        AccountId RemoteParticipant { get; }
        /// <summary>
        /// The message direction: true for inbound, and false for outbound.
        /// </summary>
        bool Inbound { get; }
        /// <summary>
        /// The server-assigned ID of the message used for paging through the large result sets.
        /// </summary>
        string MessageId { get; }
    }

    /// <summary>
    /// A transcription message.
    /// </summary>
    internal interface ITranscribedMessage : ITextMessage
    {
        /// <summary>
        /// The ChannelSession that is the target of the message.
        /// </summary>
        IChannelSession ChannelSession { get; }
        /// <summary>
        /// The message sender.
        /// </summary>
        AccountId Sender { get; }
        /// <summary>
        /// Indicates whether the message is from the currently signed in user.
        /// </summary>
        bool FromSelf { get; }
    }
}
