using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Unity.Services.Vivox.AudioTaps;
using UnityEngine;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// The interface used to interact with the VivoxService
    /// </summary>
    public interface IVivoxService
    {
        /// <summary>
        /// Event triggered when the Vivox service has been successfully initialized.
        /// This indicates that the service is ready for use.
        /// </summary>
        event Action Initialized;

        /// <summary>
        /// Event triggered when the Vivox service fails to initialize.
        /// Provides an exception detailing the reason for the failure.
        /// </summary>
        event Action<Exception> InitializationFailed;

        /// <summary>
        /// An action that will trigger when an Account is successfully LoggedIn to the Vivox Service.
        /// </summary>
        event Action LoggedIn;

        /// <summary>
        /// An action that will trigger when an Account is successfully LoggedOut to the Vivox Service.
        /// </summary>
        event Action LoggedOut;

        /// <summary>
        /// An action triggered when the input device list in refreshed/updated.
        /// An example of when this will fire is when an input device is disconnected from the primary device.
        /// </summary>
        event Action AvailableInputDevicesChanged;
        /// <summary>
        /// An action triggered when the effective input device changes.
        /// If the active input device is a virtual device such as `default system device` and the underlying device set in the system settings changes this event will be triggered.
        /// </summary>
        event Action EffectiveInputDeviceChanged;

        /// <summary>
        /// An action triggered when the output device list in refreshed/updated.
        /// An example of when this will fire is when an output device is disconnected from the primary device.
        /// </summary>
        event Action AvailableOutputDevicesChanged;
        /// <summary>
        /// An action triggered when the effective output device changes.
        /// If the active output device is a virtual device such as `default system device` and the underlying device set in the system settings changes this event will be triggered.
        /// </summary>
        event Action EffectiveOutputDeviceChanged;

        /// <summary>
        /// An Action that will fire when the network connection for the logged-in device is interrupted.
        /// Vivox will attempt to re-establish connection for 30 seconds, firing ConnectionRecovered if the connection is recovered, or ConnectionFailedToRecover if there is a failure to reconnect.
        /// </summary>
        event Action ConnectionRecovering;

        /// <summary>
        /// An action that will fire when the network connection has been successfully recovered.
        /// </summary>
        event Action ConnectionRecovered;

        /// <summary>
        /// An Action that will fire when the network connection has been interrupted for over 30 seconds, and Vivox has halted attempts to reconnect.
        /// </summary>
        event Action ConnectionFailedToRecover;

        /// <summary>
        /// An action that will trigger when a Channel has been successfully joined by the currently logged in user.
        /// Once this event fires, the user will be in the selected text/audio state based on the ChatCapabilities of the channel, and will be able to do all channel actions.
        /// Provides the ChannelName of the channel successfully joined.
        /// </summary>
        event Action<string> ChannelJoined;

        /// <summary>
        /// An action that will trigger when a Channel has been successfully left by the currently logged in user.
        /// Once this event fires, the user will no longer be in the text/audio state for this channel, and will no longer be able to do any channel operations.
        /// Provides the ChannelName of the channel successfully left.
        /// </summary>
        event Action<string> ChannelLeft;

        /// <summary>
        /// An Action that will trigger when a new Participant has been added to any channel the user is in.
        /// Provides a Participant object, which contains the Channel the participant is in, along with their PlayerId, DisplayName, whether speech has been detected, more specific audio energy changes and Muted status.
        /// </summary>
        event Action<VivoxParticipant> ParticipantAddedToChannel;

        /// <summary>
        /// An Action that will trigger when a Participant has been removed from a channel the user is in.
        /// Provides a Participant object, which contains the Channel the participant is in, along with their PlayerId, DisplayName, whether speech has been detected, more specific audio energy changes and Muted status.
        /// </summary>
        event Action<VivoxParticipant> ParticipantRemovedFromChannel;

        /// <summary>
        /// An Action that will trigger when a channel message has been received in any channel the user is in. The VivoxMessage itself will contain ChannelName of the channel it was sent in, and the PlayerId and DisplayName of the Sender
        /// </summary>
        event Action<VivoxMessage> ChannelMessageReceived;

        /// <summary>
        /// An Action that will trigger when a channel message has been edited in any channel the user is in. The VivoxMessage itself will contain the ChannelName of the channel it was sent in, the PlayerId of the Sender, and the MessageId that was edited.
        /// </summary>
        event Action<VivoxMessage> ChannelMessageEdited;

        /// <summary>
        /// An Action that will trigger when a channel message has been deleted in any channel the user is in. The VivoxMessage itself will contain the ChannelName of the channel it was sent in, the PlayerId of the Sender, and the MessageId that was deleted.
        /// The MessageText will be null.
        /// </summary>
        event Action<VivoxMessage> ChannelMessageDeleted;

        /// <summary>
        /// An Action that will trigger when a directed message has been received by the currently logged in user. The VivoxMessage itself will have the PlayerId and DisplayName of the Player, and the ChannelName will be set to null.
        /// </summary>
        event Action<VivoxMessage> DirectedMessageReceived;

        /// <summary>
        ///  An Action that will trigger when a direct message has been deleted in any channel the user is in. The VivoxMessage itself will have the PlayerId of the Player and the MessageId that was edited.
        /// The ChannelName and Message will be set to null.
        /// </summary>
        event Action<VivoxMessage> DirectedMessageDeleted;

        /// <summary>
        /// An Action that will trigger when a direct message has been deleted in any channel the user is in. The VivoxMessage itself will have the PlayerId of the Player and the MessageId that was edited.
        /// The ChannelName will be set to null.
        /// </summary>
        event Action<VivoxMessage> DirectedMessageEdited;

        /// <summary>
        /// Represents the current initialization state of the Vivox service.
        /// Possible values include:
        /// - Uninitialized: The service has not been initialized.
        /// - Initializing: The service is in the process of initializing.
        /// - Initialized: The service has been successfully initialized.
        /// - Failed: The service failed to initialize.
        /// </summary>
        VivoxInitializationState InitializationState { get; }

        /// <summary>
        /// Provides real-time control over Vivox voice processing settings including noise suppression, echo cancellation, automatic gain control, and other audio quality features.
        /// </summary>
        IVivoxGlobalAudioSettings VivoxGlobalAudioSettings { get; }

        /// <summary>
        /// Checks whether the user is logged in.
        /// </summary>
        bool IsLoggedIn { get; }

        /// <summary>
        /// The unique ID of the locally signed in user. This will be empty if a user has not successfully signed into Vivox.
        /// </summary>
        string SignedInPlayerId { get; }

        /// <summary>
        /// An action that will trigger when a transcribed message is added.
        /// Provides the added transcribed message.
        /// </summary>
        event Action<VivoxMessage> SpeechToTextMessageReceived;

        /// <summary>
        /// A collection of all available voice profiles that can be used with text-to-speech.
        /// These values within can be used to set the current voice profile when provided to <see cref="TextToSpeechSetVoice(string)"/>.
        /// </summary>
        ReadOnlyCollection<string> TextToSpeechAvailableVoices { get; }

        /// <summary>
        /// The name of the current voice being used when playback of a message occurs.
        /// </summary>
        string TextToSpeechCurrentVoice { get; }
        /// <summary>
        /// A Dictionary of channels the user is currently connected to and a list of the VivoxParticipants in each the channel.
        /// The key is the channel name used to connect with.
        /// The value is a maintained list of each <see cref="VivoxParticipant"/> in the channel.
        /// </summary>
        ReadOnlyDictionary<string, ReadOnlyCollection<VivoxParticipant>> ActiveChannels { get; }

        /// <summary>
        /// A collection of all channels that are transmitting.
        /// If the current transmission mode is set to <see cref="TransmissionMode.Single"/>, this will only show a single transmitting channel.
        /// </summary>
        ReadOnlyCollection<string> TransmittingChannels { get; }

        /// <summary>
        /// A collection of all available input devices for the local user.
        /// </summary>
        ReadOnlyCollection<VivoxInputDevice> AvailableInputDevices { get; }
        /// <summary>
        /// A collection of all available output devices for the local user.
        /// </summary>
        ReadOnlyCollection<VivoxOutputDevice> AvailableOutputDevices { get; }

        /// <summary>
        /// The active input device being used by the local user.
        /// </summary>
        VivoxInputDevice ActiveInputDevice { get; }
        /// <summary>
        /// The real active input device being used by the local user.
        /// If the active input device that's in-use is a specific device, the value provided will match the <see cref="ActiveInputDevice"/>
        /// If the active input device is a virtual device such as `default system device` then this will provide the name of the underlying input device being used.
        /// </summary>
        VivoxInputDevice EffectiveInputDevice { get; }
        /// <summary>
        /// The active output device being used by the local user.
        /// </summary>
        VivoxOutputDevice ActiveOutputDevice { get; }
        /// <summary>
        /// The real active output device being used by the local user.
        /// If the active output device that's in-use is a specific device, the value provided will match the <see cref="ActiveOutputDevice"/>
        /// If the active output device is a virtual device such as `default system device` then this will provide the name of the underlying output device being used.
        /// </summary>
        VivoxOutputDevice EffectiveOutputDevice { get; }

        /// <summary>
        /// The volume of your input.
        /// </summary>
        int InputDeviceVolume { get; }
        /// <summary>
        /// The volume of your output.
        /// </summary>
        int OutputDeviceVolume { get; }
        /// <summary>
        /// Indicates if input devices are muted.
        /// </summary>
        bool IsInputDeviceMuted { get; }
        /// <summary>
        /// Indicates if output devices are muted.
        /// </summary>
        bool IsOutputDeviceMuted { get; }

        /// <summary>
        /// Indicates whether Vivox's software echo cancellation feature is enabled.
        /// Note: This is completely independent of any hardware-provided acoustic echo cancellation that might be available for a device.
        /// </summary>
        bool IsAudioEchoCancellationEnabled { get; }

        /// <summary>
        /// Indicates whether audio is being injected or not.
        /// </summary>
        bool IsInjectingAudio { get; }

        /// <summary>
        /// The current value of the Voice Processing IO mode for iOS.
        /// On error, returns the default <see cref="IosVoiceProcessingIOModes.Always"/>.
        /// </summary>
        IosVoiceProcessingIOModes IosVoiceProcessingIOMode { get; }

        /// <summary>
        /// Initializes the Client object which will enable a user to login and check/manage audio devices.
        /// </summary>
        /// <param name="options"> The VivoxConfigurationOptions used to initialize the Vivox Service, controlling advanced audio settings and other controls like logging </param>
        /// <returns> a Task associated with the Vivox Initialization </returns>
        Task InitializeAsync(VivoxConfigurationOptions options = null);

        /// <summary>
        /// Logs into the Vivox Service, enabling Vivox to perform actions like joining channels or sending directed messages.
        /// </summary>
        /// <param name="options"> The LoginOptions to be used to create an account with a display name, and set more complex behaviour like always transmitting to new channels or allowing Text to Speech</param>
        /// <returns>Task for the operation</returns>
        Task LoginAsync(LoginOptions options = null);

        /// <summary>
        /// Logs out of the Vivox Service.
        /// </summary>
        /// <returns> a Task associated with the Vivox Logout </returns>
        Task LogoutAsync();

        /// <summary>
        /// Puts the user in a 2-D group channel using channelName as the unique identifier.
        /// This channel type is similar to a traditional non-positional voice call.
        /// If you are not logged into the Vivox service when this is called, you will be automatically logged in.
        /// </summary>
        /// <param name="channelName">A unique identifier for the channel.</param>
        /// <param name="chatCapability">Used to indicate what type of capabilities the user wants when joining a channel.</param>
        /// <param name="channelOptions">General channel configuration options to be used when joining the channel.</param>
        /// <returns>Task for the operation</returns>
        Task JoinGroupChannelAsync(string channelName, ChatCapability chatCapability, ChannelOptions channelOptions = null);

        /// <summary>
        /// Puts the user in a 3-D/positional group channel using channelName as the unique identifier.
        /// This channel type is similar to having a conversation in-person involving nuances such as audio attenutation and falloff.
        /// The features of a positional channel can be configured by using the <see cref="Channel3DProperties"/> class. See this class for an in-depth explanation of these features.
        /// If you are not logged into the Vivox service when this is called, you will be automatically logged in.
        /// </summary>
        /// <param name="channelName">A unique identifier for the channel.</param>
        /// <param name="chatCapability">Used to indicate what type of capabilities the user wants when joining a channel.</param>
        /// <param name="positionalChannelProperties">Configuration properties for a 3-D/positional channel.</param>
        /// <param name="channelOptions">General channel configuration options to be used when joining the channel.</param>
        /// <returns>Task for the operation</returns>
        Task JoinPositionalChannelAsync(string channelName, ChatCapability chatCapability, Channel3DProperties positionalChannelProperties, ChannelOptions channelOptions = null);

        /// <summary>
        /// Puts the user in an echo channel using channelName as the unique identifier.
        /// In this channel you will hear only your own voice played back at you.
        /// If you are not logged into the Vivox service when this is called, you will be automatically logged in.
        /// </summary>
        /// <param name="channelName">A unique identifier for the channel.</param>
        /// <param name="chatCapability">Used to indicate what type of capabilities the user wants when joining a channel.</param>
        /// <param name="channelOptions">General channel configuration options to be used when joining the channel.</param>
        /// <returns>Task for the operation</returns>
        Task JoinEchoChannelAsync(string channelName, ChatCapability chatCapability, ChannelOptions channelOptions = null);

        /// <summary>
        /// Leaves all channels the currently logged in user is connected to, removing the user from text and voice for those channels.
        /// </summary>
        /// <returns>Task for the operation</returns>
        Task LeaveAllChannelsAsync();

        /// <summary>
        /// Leaves a specific channel, removing the user from text and voice for that channel.
        /// </summary>
        /// <param name="channelName">The unique identifier of the channel that should be left</param>
        /// <returns>Task for the operation</returns>
        Task LeaveChannelAsync(string channelName);

        /// <summary>
        /// Enables Vivox's acoustic echo cancellation feature.
        /// </summary>
        void EnableAcousticEchoCancellation();

        /// <summary>
        /// Disables Vivox's acoustic echo cancellation feature.
        /// </summary>
        void DisableAcousticEchoCancellation();

        /// <summary>
        /// Set the value of the Voice Processing IO mode for iOS.
        /// </summary>
        /// <param name="IOSVoiceProcessingIOMode">The desired iOS voice processing mode.</param>
        void SetIosVoiceProcessingIOMode(IosVoiceProcessingIOModes IOSVoiceProcessingIOMode);

        /// <summary>
        /// Sets the specified input device as the active input device for the local user.
        /// </summary>
        /// <param name="device">The device to make the active input device.</param>
        /// <returns>Task for the operation</returns>
        Task SetActiveInputDeviceAsync(VivoxInputDevice device);

        /// <summary>
        /// Sets the specified output device as the active output device for the local user.
        /// </summary>
        /// <param name="device">The device to make the active output device.</param>
        /// <returns>Task for the operation</returns>
        Task SetActiveOutputDeviceAsync(VivoxOutputDevice device);

        /// <summary>
        /// Sets the input device volume for the local user.
        /// This applies to all active audio sessions.
        /// Volume value is clamped between -50 and 50 with a default of 0.
        /// </summary>
        /// <param name="value">Volume value to be used - clamped between -50 and 50 with a default value of 0</param>
        void SetInputDeviceVolume(int value);

        /// <summary>
        /// Sets the output device volume for the local user.
        /// This applies to all active audio sessions.
        /// Volume value is clamped between -50 and 50 with a default of 0.
        /// </summary>
        /// <param name="value">Volume value to be used - clamped between -50 and 50 with a default value of 0</param>
        void SetOutputDeviceVolume(int value);

        /// <summary>
        /// Sets the volume of an entire channel for the local user.
        /// This will adjust the volume of player audio for all players in a channel for the local user.
        /// Volume value is clamped between -50 and 50 with a default of 0.
        /// </summary>
        /// <param name="channelName">The name of the channel to set the volume value for</param>
        /// <param name="value">Volume value to be used - clamped between -50 and 50 with a default value of 0</param>
        /// <returns>Task for the operation</returns>
        Task SetChannelVolumeAsync(string channelName, int value);

        /// <summary>
        /// Enables automatic configuration of voice activity detection properties by the SDK.
        /// If Automatic Voice Activity Detection is enabled, the properties set in <see cref="SetVoiceActivityDetectionPropertiesAsync"/> will have no effect.
        /// Must be logged in to perform this action.
        /// </summary>
        /// <returns>Task for the operation</returns>
        Task EnableAutoVoiceActivityDetectionAsync();

        /// <summary>
        /// Disables automatic voice activity detection.
        /// Must be logged in to perform this action.
        /// </summary>
        /// <returns>Task for the operation</returns>
        Task DisableAutoVoiceActivityDetectionAsync();

        /// <summary>
        /// Sets voice activity detection parameters.
        /// Parameters will be defaulted to their original default value if not provided.
        /// Call this with no parameters provided if you wish to reset the VAD settings.
        /// It is recommended to cache the settings and apply all of them each time this method is called.
        /// Must be logged in to perform this action.
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
        /// in the next voice session or only allow changing the noiseFloor when the client is not in a channel.
        /// </param>
        /// <param name="sensitivity">
        /// The sensitivity is a dimensionless value between 0 and 100 that indicates the sensitivity of the VAD.
        /// Increasing this value corresponds to decreasing the sensitivity of the VAD (0 is the most sensitive, and 100 is the least sensitive).
        /// Higher values of sensitivity require louder audio to trigger the VAD. The default value is 43.
        /// </param>
        /// <returns>Task for the operation</returns>
        Task SetVoiceActivityDetectionPropertiesAsync(int hangover = 2000, int noiseFloor = 576, int sensitivity = 43);

        /// <summary>
        /// Sets the local user's consent to Vivox Safe Voice recordings
        /// </summary>
        /// <param name="consentGiven">The desired consent status to set for the local user</param>
        /// <returns>A task for the operation that will contain the consent status of the user when complete</returns>
        Task<bool> SetSafeVoiceConsentStatus(bool consentGiven);

        /// <summary>
        /// Gets the local user's consent to Vivox Safe Voice recordings
        /// </summary>
        /// <returns>A task for the operation that will contain the consent status of the user when complete</returns>
        Task<bool> GetSafeVoiceConsentStatus();

        /// <summary>
        /// Mutes the local user's input devices preventing the transmission of audio from any local input device.
        /// </summary>
        void MuteInputDevice();

        /// <summary>
        /// Unmutes the local user's input devices allowing the transmission of audio.
        /// </summary>
        void UnmuteInputDevice();

        /// <summary>
        /// Mutes the local user's output devices preventing them from hearing incoming audio from any local output device.
        /// </summary>
        void MuteOutputDevice();

        /// <summary>
        /// Unmutes the local user's output devices allowing them to hear incoming audio.
        /// </summary>
        void UnmuteOutputDevice();

        /// <summary>
        /// "Block" a player, bidirectionally muting audio/text between that player and the local user.
        /// </summary>
        /// <param name="playerId">The PlayerId to bidirectionally mute or unmute</param>
        /// <returns>Task for the operation</returns>
        Task BlockPlayerAsync(string playerId);

        /// <summary>
        /// "Unblock" a player, bidirectionally unmuting audio/text between that player and the local user.
        /// </summary>
        /// <param name="playerId">The PlayerId to bidirectionally mute or unmute</param>
        /// <returns>Task for the operation</returns>
        Task UnblockPlayerAsync(string playerId);

        /// <summary>
        /// Sends a text message to a channel the user is connected to.
        /// </summary>
        /// <param name="channelName">The name of the channel the message will be sent to.</param>
        /// <param name="message">The text of the message to be sent.</param>
        /// <param name="options">An optional parameter for adding metadata to the message</param>
        /// <returns>Task for the operation</returns>
        Task SendChannelTextMessageAsync(string channelName, string message, MessageOptions options = null);

        /// <summary>
        /// Edits an already sent message in a Channel.
        /// </summary>
        /// <param name="channelName">The name of the channel for the message to be edited.</param>
        /// <param name="messageId">The messageId of the message sent that you would like to edit.</param>
        /// <param name="newMessage">The new text of the message to be edited.</param>
        /// <returns>Task for the operation</returns>
        Task EditChannelTextMessageAsync(string channelName, string messageId, string newMessage);

        /// <summary>
        /// Deletes an already sent message in a Channel.
        /// </summary>
        /// <param name="channelName">The name of the channel for the message to be deleted.</param>
        /// <param name="messageId">The messageId of the message sent that you would like to delete.</param>
        /// <returns>Task for the operation</returns>
        Task DeleteChannelTextMessageAsync(string channelName, string messageId);

        /// <summary>
        /// Sends a text message to another logged in user.
        /// </summary>
        /// <param name="playerId">The playerId of the logged in user the message will be sent to.</param>
        /// <param name="message">The text of the message to be sent.</param>
        /// <param name="options">An optional parameter for adding metadata to the message</param>
        /// <returns>Task for the operation</returns>
        Task SendDirectTextMessageAsync(string playerId, string message, MessageOptions options = null);

        /// <summary>
        /// Edits an already sent message in a Direct Message.
        /// </summary>
        /// <param name="messageId">The messageId of the message sent that you would like to edit.</param>
        /// <param name="newMessage">The new text of the message to be edited.</param>
        /// <returns>Task for the operation</returns>
        Task EditDirectTextMessageAsync(string messageId, string newMessage);

        /// <summary>
        /// Deletes an already sent message in a Direct Message.
        /// </summary>
        /// <param name="messageId">The messageId of the message sent that you would like to delete.</param>
        /// <returns>Task for the operation</returns>
        Task DeleteDirectTextMessageAsync(string messageId);

        /// <summary>
        /// This function allows you to start audio injection. The audio file to use for audio injection should be of type WAV and MUST be single channel, 16-bit PCM, with the same sample rate as the negotiated audio codec.
        /// Injected audio is played only into the channels you're transmitting into.
        /// </summary>
        /// <param name="audioFilePath">The full pathname for the WAV file to use for audio injection (MUST be single channel, 16-bit PCM, with the same sample rate as the negotiated audio codec) required for start</param>
        void StartAudioInjection(string audioFilePath);

        /// <summary>
        /// This function allows you to stop audio injection
        /// </summary>
        void StopAudioInjection();

        /// <summary>
        /// Enables the speech-to-text audio transcription inside a channel.
        /// </summary>
        /// <param name="channelName">The name of the channel whose transcription will be enabled.</param>
        /// <returns>Task for the operation</returns>
        Task SpeechToTextEnableTranscription(string channelName);

        /// <summary>
        /// Disables the speech-to-text audio transcription inside a channel.
        /// </summary>
        /// <param name="channelName">The name of the channel whose transcription will be disabled.</param>
        /// <returns>Task for the operation</returns>
        Task SpeechToTextDisableTranscription(string channelName);

        /// <summary>
        /// Indicates if Speech-to-Text transcription is enabled for a specific channel.
        /// </summary>
        /// <param name="channelName">The name of the channel used to check wether the transcription is enabled.</param>
        /// <returns>Whether or not Speech-to-Text transcription is enabled for the specified channel.</returns>
        bool IsSpeechToTextEnabled(string channelName);

        /// <summary>
        /// Sends a text-to-speech message to the channel currently being transmitted into based on the <see cref="TextToSpeechMessageType"/> passed in.
        /// This method can also be used for local playback of text/messages exclusively.
        /// </summary>
        /// <param name="message">The message that will be sent.</param>
        /// <param name="messageType">Configuration for how the message should be sent.</param>
        void TextToSpeechSendMessage(string message, TextToSpeechMessageType messageType);

        /// <summary>
        /// Cancel all text-to-speech messages (ongoing and enqueued).
        /// </summary>
        void TextToSpeechCancelAllMessages();

        /// <summary>
        /// Cancels all text-to-speech messages of a particular <see cref="TextToSpeechMessageType"/> type (ongoing and enqueued).
        /// </summary>
        /// <param name="messageType">The type of messages that you'd like to cancel or have removed from the text-to-speech system's queue.</param>
        void TextToSpeechCancelMessages(TextToSpeechMessageType messageType);

        /// <summary>
        /// Sets the current voice used when playback of a message occurs.
        /// <see cref="TextToSpeechAvailableVoices"/> contains a list of all available voice names that can be passed into this method when logged in.
        /// </summary>
        /// <param name="voiceName">The name of the voice profile you want to use</param>
        void TextToSpeechSetVoice(string voiceName);

        /// <summary>
        /// Fetch Channel Text Messages for a given channel.  Use <see cref="ChatHistoryQueryOptions"/> chatHistoryQueryOptions to filter what is returned.
        /// </summary>
        /// <param name="channelName">The name of the channel the history will be fetched from.</param>
        /// <param name="requestSize">The maximum number of messages to return.  The larger this value is the longer the query will take to complete.  Default is 10, keeping this number low is ideal.</param>
        /// <param name="chatHistoryQueryOptions"><see cref="ChatHistoryQueryOptions"/> is used to customize the history results returned.</param>
        /// <returns>Task with the ReadOnlyCollection of <see cref="VivoxMessage"/></returns>
        Task<ReadOnlyCollection<VivoxMessage>> GetChannelTextMessageHistoryAsync(string channelName, int requestSize = 10, ChatHistoryQueryOptions chatHistoryQueryOptions = null);

        /// <summary>
        /// Fetch Direct Text Messages at an account level.  Use <see cref="ChatHistoryQueryOptions"/>chatHistoryQueryOptions to filter what is returned.
        /// </summary>
        /// <param name="playerId">The playerId of the logged in user you would like to search the chat history of.  If this value is set then it takes priority over the <see cref="ChatHistoryQueryOptions"/>chatHistoryQueryOptions value for PlayerId and it will be ignored.
        /// Otherwise that PlayerId will be used in the <see cref="ChatHistoryQueryOptions"/>chatHistoryQueryOptions PlayerId is used.</param>
        /// <param name="requestSize">The maximum number of messages to return.  The larger this value is the longer the query will take to complete.  Default is 10, keeping this number low is ideal.</param>
        /// <param name="chatHistoryQueryOptions"><see cref="ChatHistoryQueryOptions"/> is used to customize the history results returned.</param>
        /// <returns>Task with the ReadOnlyCollection of <see cref="VivoxMessage"/></returns>
        Task<ReadOnlyCollection<VivoxMessage>> GetDirectTextMessageHistoryAsync(string playerId = null, int requestSize = 10, ChatHistoryQueryOptions chatHistoryQueryOptions = null);

        /// <summary>
        /// Sets all messages of a conversation, up to the specified <see cref="VivoxMessage.MessageId"/>, as read.
        /// </summary>
        /// <param name="message">The message with an ID you'd like to set as the read/seen checkpoint of a conversation.</param>
        /// <param name="seenAt">Optional time to set the read/seen checkpoint. If not provided, the current UTC time will be used.</param>
        /// <returns>Task for the operation.</returns>
        Task SetMessageAsReadAsync(VivoxMessage message, DateTime? seenAt = null);

        /// <summary>
        /// Provides a collection of <see cref="VivoxConversation"/> objects representing conversations that the locally signed-in user has participated in.
        /// This can return a combination of channel conversations and directed message conversations.
        /// The <see cref="VivoxConversation"/> objects provided can be used to distinguish between the different <see cref="ConversationType"/> types.
        /// </summary>
        /// <param name="options">Configuration used to tailor the results of the query.</param>
        /// <returns>Task with the ReadOnlyCollection of <see cref="VivoxConversation"/>s</returns>
        Task<ReadOnlyCollection<VivoxConversation>> GetConversationsAsync(ConversationQueryOptions options = null);

        /// <summary>
        /// Set the location of the local user for a specific positonail channel.
        /// Note: This version of Set3DPosition will use the same position for the user's "ears" and "mouth", making a slightly less accurate version of positional audio.
        /// </summary>
        /// <param name="participantObject">The position of the GameObject meant to represent the player</param>
        /// <param name="channelName">The name of the specific positional channel to update the position in. Must be a positional channel. </param>
        /// <param name="allowPanning">Manages audio panning (audio shifting between left and right ears). Panning is enabled by default but will be disabled if set to false</param>
        void Set3DPosition(GameObject participantObject, string channelName, bool allowPanning = true);

        /// <summary>
        /// Set the location of the local user for a specific positional channel.
        /// </summary>
        /// <param name="speakerPos">The position of the virtual "mouth."</param>
        /// <param name="listenerPos">The position of the virtual "ear."</param>
        /// <param name="listenerAtOrient">A unit vector that represents the forward (Z) direction, or heading, of the listener.</param>
        /// <param name="listenerUpOrient">A unit vector that represents the up (Y) direction of the listener. Use Vector3(0, 1, 0) for a "global" up in the world space.</param>
        /// <param name="channelName">The name of the specific positional channel to update the position in. Must be a positional channel. </param>
        /// <param name="allowPanning">Manages audio panning (audio shifting between left and right ears). Panning is enabled by default but will be disabled if set to false</param>
        void Set3DPosition(Vector3 speakerPos, Vector3 listenerPos, Vector3 listenerAtOrient, Vector3 listenerUpOrient, string channelName, bool allowPanning = true);

        /// <summary>
        /// Changes the channel the local user is currently transmitting into.
        /// Options of transmission are all channels, no channels, or a single channel.
        /// If you are specifying a single channel, the channelName parameter is mandatory.
        /// </summary>
        /// <param name="transmissionMode">The type of transmission policy to use.</param>
        /// <param name="channelName">Name of the individual channel to transmit to. Mandatory when opting to transmit to a single channel.</param>
        /// <returns>Task for the operation</returns>
        Task SetChannelTransmissionModeAsync(TransmissionMode transmissionMode, string channelName = null);

        /// <summary>
        /// Registers an IVivoxTokenProvider implementation that will be used to vend tokens for Vivox actions.
        /// Must be called before initializing the Vivox Service.
        /// </summary>
        /// <param name="tokenProvider"> Token Provider to override default Token Provider with </param>
        void SetTokenProvider(IVivoxTokenProvider tokenProvider);
    }
}
