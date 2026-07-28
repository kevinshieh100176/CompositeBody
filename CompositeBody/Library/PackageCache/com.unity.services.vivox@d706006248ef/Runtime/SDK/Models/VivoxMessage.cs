using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// A Message for Vivox Text - either sent in a channel or directed,
    /// and either sent while this login was active, or requested from the Session or Account archives.
    /// </summary>
    public sealed class VivoxMessage
    {
        readonly IVivoxServiceInternal m_ParentVivoxServiceInstance;

        internal VivoxMessage(IVivoxServiceInternal vivoxServiceInstance, IChannelTextMessage message)
        {
            m_ParentVivoxServiceInstance = vivoxServiceInstance;
            SenderURI = message.Sender.ToString();
            SenderPlayerId = message.Sender.Name;
            SenderDisplayName = message.SenderDisplayName;
            ChannelURI = message.ChannelSession.Channel.ToString();
            ChannelName = message.ChannelSession.Channel.Name;
            MessageId = message.Key;
            MessageText = message.Message;
            FromSelf = message.FromSelf;
            ReceivedTime = message.ReceivedTime;
            Language = message.Language;
            Metadata = message.ApplicationStanzaBody;
        }

        internal VivoxMessage(IVivoxServiceInternal vivoxServiceInstance, IDirectedTextMessage message)
        {
            m_ParentVivoxServiceInstance = vivoxServiceInstance;
            SenderURI = message.Sender.ToString();
            SenderPlayerId = message.Sender.Name;
            SenderDisplayName = message.SenderDisplayName;
            ChannelName = null;
            MessageId = message.Key;
            MessageText = message.Message;
            FromSelf = message.FromSelf;
            ReceivedTime = message.ReceivedTime;
            Language = message.Language;
            Metadata = message.ApplicationStanzaBody;
        }

        internal VivoxMessage(IVivoxServiceInternal vivoxServiceInstance, ISessionArchiveMessage message)
        {
            m_ParentVivoxServiceInstance = vivoxServiceInstance;
            SenderURI = message.Sender.ToString();
            SenderPlayerId = message.Sender.Name;
            SenderDisplayName = message.SenderDisplayName;
            ChannelURI = message.ChannelSession.Channel.ToString();
            ChannelName = message.ChannelSession.Channel.Name;
            MessageId = message.Key;
            MessageText = message.Message;
            FromSelf = message.FromSelf;
            ReceivedTime = message.ReceivedTime;
            Language = message.Language;
        }

        internal VivoxMessage(IVivoxServiceInternal vivoxServiceInstance, IAccountArchiveMessage message)
        {
            m_ParentVivoxServiceInstance = vivoxServiceInstance;
            SenderURI = message.Sender.ToString();
            SenderPlayerId = message.Sender.Name;
            SenderDisplayName = message.SenderDisplayName;
            ChannelName = null;
            MessageId = message.Key;
            MessageText = message.Message;
            FromSelf = !message.Inbound;
            ReceivedTime = message.ReceivedTime;
            Language = message.Language;
            RecipientPlayerId = message.RemoteParticipant.Name;
        }

        internal VivoxMessage(IVivoxServiceInternal vivoxServiceInstance, ITranscribedMessage message)
        {
            m_ParentVivoxServiceInstance = vivoxServiceInstance;
            SenderURI = message.Sender.ToString();
            SenderPlayerId = message.Sender.Name;
            SenderDisplayName = message.SenderDisplayName;
            ChannelURI = message.ChannelSession.Channel.ToString();
            ChannelName = message.ChannelSession.Channel.Name;
            MessageId = message.Key;
            MessageText = message.Message;
            FromSelf = message.FromSelf;
            ReceivedTime = message.ReceivedTime;
            Language = message.Language;
            IsTranscribedMessage = true;
        }

        /// <summary>
        /// The account URI of the sender of the message for internal use.
        /// </summary>
        internal readonly string SenderURI;

        /// <summary>
        /// The URI of the channel a message is related to for internal use.
        /// </summary>
        internal readonly string ChannelURI;

        /// <summary>
        /// The PlayerId of the sender of the message.
        /// </summary>
        public string SenderPlayerId { get; internal set; }

        /// <summary>
        /// The DisplayName of the sender of the message.
        /// </summary>
        public string SenderDisplayName { get; internal set; }

        /// <summary>
        /// The PlayerId of the recipient of the message.
        /// This will only be populated in VivoxMessages provided by a <see cref="IVivoxService.GetDirectTextMessageHistoryAsync(string, int, ChatHistoryQueryOptions) query."/>
        /// </summary>
        public string RecipientPlayerId { get; internal set; }

        /// <summary>
        /// The ChannelName of the channel the message was sent in.
        /// IMPORTANT: null if the message was a DirectedMessage.
        /// </summary>
        public string ChannelName { get; internal set; }

        /// <summary>
        /// The text body of the message that was sent
        /// </summary>
        public string MessageText { get; internal set; }

        /// <summary>
        /// Whether or not the message was sent from the user to the channel.
        /// </summary>
        public bool FromSelf { get; internal set; }

        /// <summary>
        /// At what time the message was received.
        /// </summary>
        public DateTime ReceivedTime { get; internal set; }

        /// <summary>
        /// The language preference of the user that sent the message.
        /// </summary>
        public string Language { get; internal set; }

        /// <summary>
        /// Unique message id of the text message.
        /// </summary>
        public string MessageId { get; internal set; }

        /// <summary>
        /// Denotes if this message has been read/seen or not.
        /// </summary>
        public bool IsRead { get; internal set; }

        /// <summary>
        /// Denotes if this message was created as a result of a Speech-to-Text transcription.
        /// </summary>
        public bool IsTranscribedMessage { get; internal set; }

        /// <summary>
        /// A string to store metadata associated with this message
        /// </summary>
        public string Metadata { get; internal set; }

        /// <summary>
        /// Marks a particular message as read/seen.
        /// </summary>
        /// <param name="seenAt">The date and time the message was seen at.</param>
        /// <returns>A Task for the operation.</returns>
        public async Task SetMessageAsReadAsync(DateTime? seenAt = null)
        {
            await m_ParentVivoxServiceInstance.SetMessageAsReadAsync(this, seenAt);
        }
    }
}
