using System.ComponentModel;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// The state of the Login session.
    /// </summary>
    [DefaultValue(LoggedOut)]
    internal enum LoginState
    {
        /// <summary>
        /// The Login session is signed out.
        /// </summary>
        LoggedOut = vx_login_state_change_state.login_state_logged_out,
        /// <summary>
        /// The Login session is signed in.
        /// </summary>
        LoggedIn = vx_login_state_change_state.login_state_logged_in,
        /// <summary>
        /// The Login session is in the process of signing in.
        /// </summary>
        LoggingIn = vx_login_state_change_state.login_state_logging_in,
        /// <summary>
        /// The Login session is in the process of signing out.
        /// </summary>
        LoggingOut = vx_login_state_change_state.login_state_logging_out
    }

    /// <summary>
    /// Determine how to handle incoming subscriptions.
    /// </summary>
    internal enum SubscriptionMode
    {
        /// <summary>
        /// Automatically accept all incoming subscription requests.
        /// </summary>
        Accept = vx_buddy_management_mode.mode_auto_accept,
        /// <summary>
        /// Automatically block all incoming subscription requests.
        /// </summary>
        Block = vx_buddy_management_mode.mode_block,
        /// <summary>
        /// Defer incoming subscription request handling to the application.
        /// In this scenario, the IncomingSubscriptionRequests collection raises the AfterItemAdded event.
        /// </summary>
        Defer = vx_buddy_management_mode.mode_application,
    }

    /// <summary>
    /// The online status of the user.
    /// </summary>
    internal enum PresenceStatus
    {
        /// <summary>
        /// Generally available
        /// </summary>
        Available = vx_buddy_presence_state.buddy_presence_online,
        /// <summary>
        /// Do Not Disturb
        /// </summary>
        DoNotDisturb = vx_buddy_presence_state.buddy_presence_busy,
        /// <summary>
        /// Away
        /// </summary>
        Away = vx_buddy_presence_state.buddy_presence_away,
        /// <summary>
        /// Currently in a call
        /// </summary>
        InACall = vx_buddy_presence_state.buddy_presence_onthephone,
        /// <summary>
        /// Not available (offline)
        /// </summary>
        Unavailable = vx_buddy_presence_state.buddy_presence_offline,
        /// <summary>
        /// Available to chat
        /// </summary>
        Chat = vx_buddy_presence_state.buddy_presence_chat,
        /// <summary>
        /// Away for an extended period of time
        /// </summary>
        ExtendedAway = vx_buddy_presence_state.buddy_presence_extended_away
    }

    /// <summary>
    /// The type of channel.
    /// </summary>
    internal enum ChannelType
    {
        /// <summary>
        /// A typical conferencing channel.
        /// </summary>
        NonPositional,
        /// <summary>
        /// A conferencing channel where user voices are rendered with 3D positional effects.
        /// </summary>
        Positional,
        /// <summary>
        /// A conferencing channel where the user's text and audio is echoed back to the user.
        /// </summary>
        Echo
    }

    /// <summary>
    /// The state of any resource with connection semantics (media and text state).
    /// </summary>
    internal enum ConnectionState
    {
        /// <summary>
        /// The resource is disconnected.
        /// </summary>
        Disconnected,
        /// <summary>
        /// The resource is in the process of connecting.
        /// </summary>
        Connecting,
        /// <summary>
        /// The resource is connected.
        /// </summary>
        Connected,
        /// <summary>
        /// The resource is in the process of disconnecting.
        /// </summary>
        Disconnecting
    }

    /// <summary>
    /// The state of the text-to-speech (TTS) message.
    /// </summary>
    internal enum TTSMessageState
    {
        /// <summary>
        /// The message is not yet in the TTS subsystem.
        /// </summary>
        NotEnqueued,
        /// <summary>
        /// The message is waiting to be played in its destination.
        /// </summary>
        Enqueued,
        /// <summary>
        /// The message is currently being played in its destination.
        /// </summary>
        Playing
    }

    /// <summary>
    /// The state of Vivox when an unexpected network disconnection occurs. Use this to display the status of the disconnect to the user, or if there is a recovery failure, to reset the game state or retry the overall Vivox connection.
    /// </summary>
    internal enum ConnectionRecoveryState
    {
        /// <summary>
        /// Vivox has yet to connect to the service for the first time.
        /// This is the default state.
        /// </summary>
        Disconnected = vx_connection_state.connection_state_disconnected,
        /// <summary>
        /// Vivox has successfully connected to the service for the first time.
        /// </summary>
        Connected = vx_connection_state.connection_state_connected,
        /// <summary>
        /// Vivox is in the process of recovering the connection with the service.
        /// </summary>
        Recovering = vx_connection_state.connection_state_recovering,
        /// <summary>
        /// Vivox has failed to reestablish the connection with the service.
        /// </summary>
        FailedToRecover = vx_connection_state.connection_state_failed_to_recover,
        /// <summary>
        /// Vivox has successfully recovered the connection with the service.
        /// </summary>
        Recovered = vx_connection_state.connection_state_recovered
    }
}
