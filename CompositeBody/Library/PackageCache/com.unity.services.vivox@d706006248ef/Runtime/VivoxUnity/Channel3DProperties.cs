using System;
using System.Text.RegularExpressions;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Properties to control the 3D effects applied to audio in positional channels.
    /// </summary>
    public class Channel3DProperties
    {
        private readonly int _audibleDistance;
        private readonly int _conversationalDistance;
        private readonly float _audioFadeIntensityByDistance;
        private readonly AudioFadeModel _audioFadeModel;

        /// <summary>
        /// The maximum distance from the listener that a speaker can be heard.
        /// </summary>
        /// <remarks>
        /// Any players within this distance from you in any direction appear in the same positional
        /// voice channel as you and can be heard. When a player crosses this threshold distance from your perspective,
        /// an IChannelSession event fires: either EventParticipantAdded when a player comes within this distance,
        /// or EventParticipantLeft when a player moves beyond this distance. You stop receiving audio from participants
        /// beyond this range, even before the participant left event is called, but are guaranteed to receive the
        /// added event before receiving audio. The value of this property is measured in arbitrary “distance units,”
        /// so it can be set to any scale and does not need to conform to any real units. The default value is 32.
        /// </remarks>
        public int AudibleDistance => _audibleDistance;
        /// <summary>
        /// The distance from the listener within which a speaker’s voice is heard at its original volume, and beyond which the speaker's voice begins to fade.
        /// </summary>
        /// <remarks>
        /// This property is measured in arbitrary “distance units,” but should use the same scale as
        /// audibleDistance. Your 3D audio experience sounds the most realistic when the value of this property
        /// is set to half the height of a typical player avatar in your game. For near-human-sized entities, this
        /// means about 1 meter, 90 centimeters, or 3 feet. The default value is 1.
        /// </remarks>
        public int ConversationalDistance => _conversationalDistance;
        /// <summary>
        /// The strength of the audio fade effect as the speaker moves away from the listener past the conversational distance. For example: .5=half strength, 1=normal strength, 2=double strength.
        /// </summary>
        /// <remarks>
        /// This parameter is a scalar used in the audio fade calculations as either a constant multiplier or
        /// an exponent, depending on the audioFadeModel value. Accordingly, this scales the result of the audio
        /// attenuation at different distances, as determined by the model's formula. A value greater than 1.0 results in audio that
        /// fades quicker as you move away from the conversational distance, and a value less than 1.0 results in audio that
        /// fades slower. The default value is 1.0.
        /// </remarks>
        public float AudioFadeIntensityByDistance => _audioFadeIntensityByDistance;

        /// <summary>
        /// The model that determines how loud a voice is at different distances.
        /// </summary>
        /// <remarks>
        /// Voice heard within the conversationalDistance is at the original speaking volume, and voice from speakers
        /// past the audibleDistance is no longer transmitted. The loudness of the audio at every other distance within
        /// this range is controlled by one of three possible audio fade models. The default value is InverseByDistance, which
        /// is the most realistic.
        /// - InverseByDistance
        ///         - Fades voice quickly at first, but slows down as you get further from the conversational distance.
        ///         - The attenuation increases in inverse proportion to the distance.
        ///         - This option models real life acoustics and sounds the most natural.
        /// - LinearByDistance
        ///         - Fades voice slowly at first, but speeds up as you get further from the conversational distance.
        ///         - The attenuation increases in linear proportion to the distance.
        ///         - The audioFadeIntensityByDistance factor is the negative slope of the attenuation curve.
        ///         - This option can be thought of as a compromise between realistic acoustics and a radio channel with no distance attenuation.
        /// - ExponentialByDistance
        ///         - Fades voice extremely quickly beyond the conversational distance + 1.
        ///         - The attenuation increases in inverse proportion to the distance raised to the power of the audioFadeIntensityByDistance factor.
        ///         - This shares a curve shape similar to realistic attenuation, but allows for much steeper rolloff.
        ///         - Use this option to apply a "cocktail party effect" to the audio space; by tuning the
        ///           audioFadeIntensityByDistance, this model allows nearby participants to be understandable while mixing
        ///           farther participants’ conversation into non-intrusive chatter.
        /// </remarks>
        public AudioFadeModel AudioFadeModel => _audioFadeModel;

        /// <summary>
        /// A default constructor that sets fields to their suggested values.
        /// </summary>
        public Channel3DProperties()
        {
            _audibleDistance = 32;
            _conversationalDistance = 1;
            _audioFadeIntensityByDistance = 1.0f;
            _audioFadeModel = AudioFadeModel.InverseByDistance;
        }

        internal Channel3DProperties(string properties)
        {
            Regex regex = new Regex(@"([^-]+)-([^-]+)-([^-]+)-([^-]+)");
            var matches = regex.Matches(properties);
            _audibleDistance = int.Parse(matches[0].Groups[1].Value);
            _conversationalDistance = int.Parse(matches[0].Groups[2].Value);
            _audioFadeIntensityByDistance = float.Parse(matches[0].Groups[3].Value, new System.Globalization.CultureInfo("en-US"));
            _audioFadeModel = (AudioFadeModel)int.Parse(matches[0].Groups[4].Value);
        }

        /// <summary>
        /// A constructor that sets all 3D channel properties. For information on recommended values for different 3D scenarios, refer to the Vivox Developer Documentation.
        /// </summary>
        /// <param name="audibleDistance">The maximum distance from the listener that a speaker can be heard. Must be &gt; 0</param>
        /// <param name="conversationalDistance">The distance from the listener within which a speaker’s voice is heard at its original volume. Must be &gt;= 0 and &lt;= audibleDistance.</param>
        /// <param name="audioFadeIntensityByDistanceaudio">The strength of the audio fade effect as the speaker moves away from the listener. Must be &gt;= 0. This value is rounded to three decimal places.</param>
        /// <param name="audioFadeModel">The model used to determine voice volume at different distances.</param>
        public Channel3DProperties(int audibleDistance, int conversationalDistance, float audioFadeIntensityByDistanceaudio, AudioFadeModel audioFadeModel)
        {
            _audibleDistance = audibleDistance;
            _conversationalDistance = conversationalDistance;
            _audioFadeIntensityByDistance = audioFadeIntensityByDistanceaudio;
            _audioFadeModel = audioFadeModel;
        }

        /// <summary>
        /// Check if the current member variables have valid values.
        /// </summary>
        /// <returns>If all member variables have valid values.</returns>
        internal bool IsValid()
        {
            return _audibleDistance > 0
                && _conversationalDistance >= 0 && _conversationalDistance <= _audibleDistance
                && _audioFadeIntensityByDistance >= 0
                && Enum.IsDefined(typeof(AudioFadeModel), _audioFadeModel);
        }

        /// <summary>
        /// Create a 3D positional URI from the values of the member variables.
        /// </summary>
        /// <returns>A string embedded with member variable values that are formatted to fit the design of a positional channel's URI.</returns>
        public override String ToString()
        {
            return $"!p-{_audibleDistance}-{_conversationalDistance}-{_audioFadeIntensityByDistance.ToString("0.000", new System.Globalization.CultureInfo("en-US"))}-{(int)_audioFadeModel}";
        }
    }
}
