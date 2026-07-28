using System.Collections.Generic;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Used for configuring optional fields related to sending channel or direct messages.
    /// </summary>
    public sealed class MessageOptions
    {
        /// <summary>
        /// A metadata tag for the language that this message is in
        /// Defaults to null
        /// </summary>
        public string Language { get; set; } = null;

        /// <summary>
        /// A string to store metadata associated with a message
        /// If set recipients of this message will also get this metadata.
        /// Often used to store information about the message such as application specific data, or other information that is relevant to the message.
        /// Defaults to null
        /// </summary>
        public string Metadata { get; set; } = null;
    }
}
