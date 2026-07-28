using System;
using System.Collections.Generic;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Options to configure a session when joining.
    /// Some options will only be used if a session needs to be created.
    /// Options can be customized to provide additional feature configuration.
    /// </summary>
    public class JoinSessionOptions : BaseSessionOptions
    {
        /// <summary>
        /// The password used to connect to the Session
        /// </summary>
        public string Password { get; set; }
    }
}
