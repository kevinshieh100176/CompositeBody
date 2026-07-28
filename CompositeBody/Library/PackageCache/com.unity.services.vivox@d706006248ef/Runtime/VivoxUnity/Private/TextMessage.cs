using System;
using System.ComponentModel;

namespace Unity.Services.Vivox
{
    [Serializable]
    internal class JSONChannelMessage
    {
        public string Language;
        public string Message;
        public string ReceivedTime;
        public Sender Sender;
        public SenderParticipant Participant;
        public Channel Channel;
    }

    [Serializable]
    internal class Sender
    {
        public string Issuer;
        public string Name;
        public string EnvironmentId;
        public string Domain;
    }

    [Serializable]
    internal class SenderParticipant
    {
        public string InAudio;
        public string InText;
        public bool IsSelf;
        public bool LocalMute;
    }

    [Serializable]
    internal class Channel
    {
        public string domain;
        public string issuer;
        public string name;
        public string type;
    }

    [Serializable]
    internal class JSONDirectedMessage
    {
        public string Language;
        public string Message;
        public string ReceivedTime;
        public Sender Sender;
        public SenderParticipant Participant;
    }

    internal class DirectedTextMessage : IDirectedTextMessage
    {
        internal DirectedTextMessage(ILoginSession parent, JSONDirectedMessage directMessagePayload)
        {
            LoginSession = parent;
            FromSelf = directMessagePayload.Participant.IsSelf;
            Message = directMessagePayload.Message;
            ReceivedTime = Convert.ToDateTime(directMessagePayload.ReceivedTime);
            Language = directMessagePayload.Language;

            var environmentId = string.IsNullOrEmpty(directMessagePayload.Sender.EnvironmentId)
                ? ""
                : $"{directMessagePayload.Sender.EnvironmentId}."; // We want the trailing dot only if the environment id is set
            Key =
                $"sip:.{directMessagePayload.Sender.Issuer}.{directMessagePayload.Sender.Name}.{environmentId}@{directMessagePayload.Sender.Domain}";
            Sender = new AccountId(Key,
                Key); // The second value of Key is only temporary until we pass in display name, for now Key is at least unique if not user friendly

            VivoxLogger.Log($"internal ChannelParticipant  {this.Key.ToString()}");
        }

        public DirectedTextMessage()
        {
        }

        private Exception _exception;
        public event PropertyChangedEventHandler PropertyChanged;

        public Exception Exception
        {
            get { return _exception; }
            set
            {
                if (_exception != value)
                {
                    _exception = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exception)));
                }
            }
        }

        public string Key { get; set; }

        public DateTime ReceivedTime { get; set; }
        public string Message { get; set; }
        public string Language { get; set; }

        public ILoginSession LoginSession { get; set; }
        public bool FromSelf { get; set; }
        public AccountId Sender { get; set; }
        public string ApplicationStanzaNamespace { get; set; }
        public string ApplicationStanzaBody { get; set; }
        public string SenderDisplayName { get; set; }
    }

    internal class FailedDirectedTextMessage : IFailedDirectedTextMessage
    {
        private Exception _exception;
        public event PropertyChangedEventHandler PropertyChanged;

        public Exception Exception
        {
            get { return _exception; }
            set
            {
                if (_exception != value)
                {
                    _exception = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exception)));
                }
            }
        }

        public bool FromSelf { get; set; }
        public AccountId Sender { get; set; }
        public string RequestId { get; set; }
        public int StatusCode { get; set; }
    }

    internal class ChannelTextMessage : IChannelTextMessage
    {
        internal ChannelTextMessage(IChannelSession parent, JSONChannelMessage jsonChannelMessage)
        {
            ChannelSession = parent;
            FromSelf = jsonChannelMessage.Participant.IsSelf;
            Message = jsonChannelMessage.Message;
            ReceivedTime = Convert.ToDateTime(jsonChannelMessage.ReceivedTime);
            Language = jsonChannelMessage.Language;

            var environmentId = string.IsNullOrEmpty(jsonChannelMessage.Sender.EnvironmentId)
                ? ""
                : $"{jsonChannelMessage.Sender.EnvironmentId}."; // We want the trailing dot only if the environment id is set
            Key =
                $"sip:.{jsonChannelMessage.Sender.Issuer}.{jsonChannelMessage.Sender.Name}.{environmentId}@{jsonChannelMessage.Sender.Domain}";
            Sender = new AccountId(Key,
                Key); // The second value of Key is only temporary until we pass in display name, for now Key is at least unique if not user friendly

            VivoxLogger.Log($"internal ChannelParticipant  {this.Key.ToString()}");
        }

        private Exception _exception;

        public ChannelTextMessage()
        {
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Exception Exception
        {
            get { return _exception; }
            set
            {
                if (_exception != value)
                {
                    _exception = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exception)));
                }
            }
        }

        public string Key { get; set; }

        public DateTime ReceivedTime { get; set; }
        public string Message { get; set; }
        public string Language { get; set; }

        public IChannelSession ChannelSession { get; set; }
        public AccountId Sender { get; set; }
        public bool FromSelf { get; set; }
        public string ApplicationStanzaNamespace { get; set; }
        public string ApplicationStanzaBody { get; set; }
        public string SenderDisplayName { get; set; }
    }

    internal class SessionArchiveMessage : ISessionArchiveMessage
    {
        private Exception _exception;
        public event PropertyChangedEventHandler PropertyChanged;

        public Exception Exception
        {
            get { return _exception; }
            set
            {
                if (_exception != value)
                {
                    _exception = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exception)));
                }
            }
        }

        public string Key { get; set; }

        public DateTime ReceivedTime { get; set; }
        public string Message { get; set; }
        public string Language { get; set; }

        public IChannelSession ChannelSession { get; set; }
        public AccountId Sender { get; set; }
        public bool FromSelf { get; set; }

        public string QueryId { get; set; }
        public string MessageId { get; set; }
        public string SenderDisplayName { get; set; }
    }

    internal class AccountArchiveMessage : IAccountArchiveMessage
    {
        private Exception _exception;
        public event PropertyChangedEventHandler PropertyChanged;

        public Exception Exception
        {
            get { return _exception; }
            set
            {
                if (_exception != value)
                {
                    _exception = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exception)));
                }
            }
        }

        public string Key { get; set; }

        public DateTime ReceivedTime { get; set; }
        public string Message { get; set; }
        public string Language { get; set; }

        public ILoginSession LoginSession { get; set; }
        public string QueryId { get; set; }

        public AccountId Sender { get; set; }
        public AccountId RemoteParticipant { get; set; }
        public bool Inbound { get; set; }
        public string MessageId { get; set; }
        public string SenderDisplayName { get; set; }
    }

    internal class TranscribedMessage : ITranscribedMessage
    {
        private Exception _exception;

        public TranscribedMessage(AccountId sender, string message, string key, string language,
            IChannelSession channelSession,
            bool fromSelf, DateTime? receivedTime = null)
        {
            ReceivedTime = receivedTime ?? DateTime.Now;
            Sender = sender;
            Message = message;
            Key = key;
            Language = language;
            ChannelSession = channelSession;
            FromSelf = fromSelf;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Exception Exception
        {
            get { return _exception; }
            set
            {
                if (_exception != value)
                {
                    _exception = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exception)));
                }
            }
        }

        public string Key { get; set; }

        public DateTime ReceivedTime { get; private set; }
        public string Message { get; private set; }
        public string Language { get; private set; }

        public IChannelSession ChannelSession { get; private set; }
        public AccountId Sender { get; private set; }
        public bool FromSelf { get; private set; }
        public string SenderDisplayName { get; set; }
    }
}
