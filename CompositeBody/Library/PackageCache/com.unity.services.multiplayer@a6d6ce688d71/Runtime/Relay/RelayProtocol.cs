namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Specifies which network communication protocol relay uses.
    /// </summary>
    public enum RelayProtocol : byte
    {
        /// <summary>
        /// Protocol is not specified or malformed.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// User Datagram Protocol.
        /// </summary>
        UDP = 1,

        /// <summary>
        /// Datagram Transport Layer Security.
        /// </summary>
        DTLS = 2,

        /// <summary>
        /// Secure websocket.
        /// </summary>
        WSS = 3,

        /// <summary>
        /// The suggested default protocol.
        /// <br/>
        /// Uses <see cref="WSS"/> for WebGL
        /// builds, otherwise <see cref="DTLS"/>.
        /// </summary>
#if UNITY_WEBGL
        Default = WSS,
#else
        Default = DTLS,
#endif
    }
}
