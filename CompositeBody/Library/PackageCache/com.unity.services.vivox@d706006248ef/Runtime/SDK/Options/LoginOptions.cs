using System.Collections.Generic;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// The options used to control Login behaviour - like enabling Text to Speech, setting a display name, or loading a blocked user list
    /// </summary>
    public sealed class LoginOptions
    {
        /// <summary>
        /// An override for a player's unique identifier that is only applied when the Authentication SDK is not in use.
        /// By default, the local user is signed into Vivox using either a generated GUID or the Authentication service's PlayerId if that SDK is being used in a project.
        /// Only a generated GUID can be overridden when using this property.
        /// </summary>
        public string PlayerId { get; set; }

        /// <summary>
        /// The DisplayName used by the LoginSession.
        /// This is not intended to be the unique ID of a user.
        /// </summary>
        public string DisplayName { get; set; }
        /// <summary>
        /// Whether or not to enable Text-to-Speech for this account - disabling it will prevent Text-to-Speech messages from being sent or received.
        /// </summary>
        public bool EnableTTS { get; set; }

        /// <summary>
        /// A list of Account identifiers to be blocked immediately upon login.
        /// </summary>
        public List<string> BlockedUserList { get; set; } = new List<string>();

        /// <summary>
        /// How frequently you would like to receieve participant updates events for all channels.
        /// </summary>
        public ParticipantPropertyUpdateFrequency ParticipantUpdateFrequency { get; set; } = ParticipantPropertyUpdateFrequency.StateChange;

        /// <summary>
        /// A list of languages used as hints for audio transcription. The default is an empty array, which implies "en".
        /// You can specify up to three spoken languages in order of preference to inform transcription of all users in transcribed channels.
        /// IETF language tag strings are not validated, but are expected to conform to <a href="https://tools.ietf.org/html/bcp47">BCP 47</a>.
        /// </summary>
        public List<string> SpeechToTextLanguages { get; set; } = new List<string>();
    }
}
