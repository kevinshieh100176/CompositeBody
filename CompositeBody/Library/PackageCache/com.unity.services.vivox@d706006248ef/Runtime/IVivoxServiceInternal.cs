using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Unity.Services.Authentication.Internal;
using Unity.Services.Vivox.Internal;
using UnityEngine;

namespace Unity.Services.Vivox
{
    interface IVivoxServiceInternal : IVivoxService
    {
        string AccessToken { get; }
        IAccessToken AccessTokenComponent { get; }
        Client Client { get; set; }
        string Domain { get; set; }
        string EnvironmentId { get; }
        IEnvironmentId EnvironmentIdComponent { get; }
        IVivoxTokenProvider ExternalTokenProvider { get; set; }
        bool HaveVivoxCredentials { get; }
        IVivoxTokenProviderInternal InternalTokenProvider { get; set; }
        bool IsAuthenticated { get; }
        bool IsEnvironmentCustom { get; }
        string Issuer { get; set; }
        bool IsTestMode { get; set; }
        string Key { get; set; }
        string PlayerId { get; }
        IPlayerId PlayerIdComponent { get; }
        string Server { get; set; }

        event Action<bool> UserInputDeviceMuteStateChanged;

        string GetChannelUriByName(string channelName);

        /// <summary>
        /// Channel uri of the last channel joined by the user.
        /// </summary>
        string LastChannelJoinedUri { get; }

        ReadOnlyCollection<VivoxInputDevice> GetInputDevices();
        ReadOnlyCollection<VivoxOutputDevice> GetOutputDevices();
        string GetParticipantUriByName(string participantName);
        ReadOnlyCollection<string> GetTransmittingChannels();
        void HandleLoginStateChange(ILoginSession loginSession);
        void HandleRecoveryStateChange(ILoginSession loginSession);
        Task JoinChannelAsync(string channelName, ChatCapability chatCapability, ChannelType channelType, Channel3DProperties positionalChannelProperties = null, ChannelOptions channelOptions = null);
        void OnInputDevicesChanged(object sender, PropertyChangedEventArgs args);
        void OnOutputDevicesChanged(object sender, PropertyChangedEventArgs args);
        void OnChannelMessageReceived(object sender, QueueItemAddedEventArgs<IChannelTextMessage> textMessage);
        void OnChannelPropertyChanged(object sender, PropertyChangedEventArgs args);
        void OnDirectedMessageReceived(object sender, QueueItemAddedEventArgs<IDirectedTextMessage> textMessage);
        void OnLoginSessionPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs);
        void OnParticipantAdded(object sender, KeyEventArg<string> keyEventArg);
        void OnParticipantRemoved(object sender, KeyEventArg<string> keyEventArg);
        void SetChannelEventBindings(IChannelSession channel, bool doBind);
        IosVoiceProcessingIOModes GetIosVoiceProcessingIOMode();
    }
}
