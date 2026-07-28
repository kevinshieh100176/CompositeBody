namespace Unity.Services.DistributedAuthority
{
    /// <summary>
    /// Enumerates the known error causes when communicating with the Distributed Authority Service.
    /// </summary>
    internal enum DistributedAuthorityExceptionReason
    {
        /// <summary>
        /// Start of the range of error codes addressable by the Distributed Authority Service.
        /// </summary>
        Min = 45000,
        /// <summary>
        /// Default value of the enum. No error detected.
        /// </summary>
        NoError = 45000,

        /// <summary>
        /// The Distributed Authority Service could not understand the request due to an invalid value or syntax.
        /// </summary>
        InvalidArgument = 45400,
        /// <summary>
        /// The Distributed Authority Service could not determine the user identity.
        /// </summary>
        Unauthorized = 45401,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        PaymentRequired = 45402,
        /// <summary>
        /// The user does not have permission to access the requested resource.
        /// </summary>
        Forbidden = 45403,
        /// <summary>
        /// The requested entity (allocation, join code or region) does not exist.
        /// </summary>
        EntityNotFound = 45404,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        MethodNotAllowed = 45405,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        NotAcceptable = 45406,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        ProxyAuthenticationRequired = 45407,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        RequestTimeOut = 45408,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        Conflict = 45409,
        /// <summary>
        /// The requested resource has been permenantly deleted from the server.
        /// </summary>
        Gone = 45410,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        LengthRequired = 45411,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        PreconditionFailed = 45412,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        RequestEntityTooLarge = 45413,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        RequestUriTooLong = 45414,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        UnsupportedMediaType = 45445,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        RangeNotSatisfiable = 45416,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        ExpectationFailed = 45417,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        Teapot = 45418,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        Misdirected = 45421,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        UnprocessableTransaction = 45422,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        Locked = 45423,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        FailedDependency = 45424,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        TooEarly = 45425,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        UpgradeRequired = 45426,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        PreconditionRequired = 45428,
        /// <summary>
        /// The user has sent too many requests in a given amount of time and is now rate limited.
        /// </summary>
        RateLimited = 45429,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        RequestHeaderFieldsTooLarge = 45431,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        UnavailableForLegalReasons = 45451,
        /// <summary>
        /// The Distributed Authority Service has encountered a situation it doesn't know how to handle.
        /// </summary>
        InternalServerError = 45500,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        NotImplemented = 45501,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        BadGateway = 45502,
        /// <summary>
        /// The Distributed Authority Service is not ready to handle the request. Common causes are a server that is down for maintenance or that is overloaded. Try again later.
        /// </summary>
        ServiceUnavailable = 45503,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        GatewayTimeout = 45504,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        HttpVersionNotSupported = 45505,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        VariantAlsoNegotiates = 45506,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        InsufficientStorage = 45507,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        LoopDetected = 45508,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        NotExtended = 45510,
        /// <summary>
        /// Same as the HTTP Status.
        /// </summary>
        NetworkAuthenticationRequired = 45511,

        /// <summary>
        /// NetworkError is returned when the Distributed Authority is unable to connect to the service due to a network error like when TLS Negotiation fails.
        /// </summary>
        NetworkError = 45998,
        /// <summary>
        /// Unknown is returned when a unrecognized error code is returned by the service. Check the inner exception to get more information.
        /// </summary>
        Unknown = 45999,
        /// <summary>
        /// End of the range of error codes addressable by the Distributed Authority Service.
        /// </summary>
        Max = 45999
    }
}
