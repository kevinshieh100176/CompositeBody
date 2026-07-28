namespace Unity.Services.Matchmaker
{
    /// <summary>
    /// Enumerates the known error causes when communicating with the Matchmaker Service.
    /// </summary>
    public enum MatchmakerExceptionReason
    {
        /// <summary>
        /// Start of the range of error codes addressable by the Matchmaker Service.
        /// </summary>
        Min = 21000,

        // Add any service specific error codes to the range 17000 - 17399 for any new errors introduced to the API (that do not originate from HTTP)

        /// <summary>
        /// The Matchmaker Service could not understand the request due to an invalid value or syntax.
        /// </summary>
        BadRequest = 21400,
        /// <summary>
        /// The Matchmaker Service could not determine the user identity.
        /// </summary>
        Unauthorized = 21401,
        /// <summary>
        /// This error code is reserved for future use.
        /// </summary>
        PaymentRequired = 21402,
        /// <summary>
        /// The user does not have permission to access the requested resource.
        /// </summary>
        Forbidden = 21403,
        /// <summary>
        /// The requested entity (server fleet, matchmaker queue or region) does not exist.
        /// </summary>
        EntityNotFound = 21404,
        /// <summary>
        /// The method specified is not allowed for the specified resource.
        /// </summary>
        MethodNotAllowed = 21405,
        /// <summary>
        /// The server cannot provide a response that matches the acceptable values for the request.
        /// </summary>
        NotAcceptable = 21406,
        /// <summary>
        /// The request requires authentication with the proxy.
        /// </summary>
        ProxyAuthenticationRequired = 21407,
        /// <summary>
        /// The request was not made within the time the server was prepared to wait.
        /// </summary>
        RequestTimeOut = 21408,
        /// <summary>
        /// The request could not be completed due to a conflict with the current state on the server.
        /// </summary>
        Conflict = 21409,
        /// <summary>
        /// The requested resource is no longer available and there is no known forwarding address.
        /// </summary>
        Gone = 21410,
        /// <summary>
        /// The server refuses to accept the request without a defined content-length.
        /// </summary>
        LengthRequired = 21411,
        /// <summary>
        /// A precondition given in the request was not met when tested on the server.
        /// </summary>
        PreconditionFailed = 21412,
        /// <summary>
        /// The request entity is larger than the server is willing or able to process.
        /// </summary>
        RequestEntityTooLarge = 21413,
        /// <summary>
        /// The request URI is longer than the server is willing to interpret.
        /// </summary>
        RequestUriTooLong = 21414,
        /// <summary>
        /// The request is in a format not supported by the requested resource for the requested method.
        /// </summary>
        UnsupportedMediaType = 21415,
        /// <summary>
        /// The requested ranges cannot be served.
        /// </summary>
        RangeNotSatisfiable = 21416,
        /// <summary>
        /// An expectation in the request cannot be met by the server.
        /// </summary>
        ExpectationFailed = 21417,
        /// <summary>
        /// The server refuses to brew coffee because it is, permanently, a teapot. Defined by the Hyper Text Coffee Pot Control Protocol defined in April Fools' jokes in 1998 and 2014.
        /// </summary>
        Teapot = 21418,
        /// <summary>
        /// The request was directed to a server that is not able to produce a response.
        /// </summary>
        Misdirected = 21421,
        /// <summary>
        /// The request is understood, but the server was unable to process its instructions.
        /// </summary>
        UnprocessableTransaction = 21422,
        /// <summary>
        /// The source or destination resource is locked.
        /// </summary>
        Locked = 21423,
        /// <summary>
        /// The method could not be performed on the resource because a dependency for the action failed.
        /// </summary>
        FailedDependency = 21424,
        /// <summary>
        /// The server is unwilling to risk processing a request that may be replayed.
        /// </summary>
        TooEarly = 21425,
        /// <summary>
        /// The server refuses to perform the request using the current protocol.
        /// </summary>
        UpgradeRequired = 21426,
        /// <summary>
        /// The server requires the request to be conditional.
        /// </summary>
        PreconditionRequired = 21428,
        /// <summary>
        /// Too many requests have been sent in a given amount of time. Please see: https://docs.unity.com/matchmaker/Content/rate-limits.htm for more details.
        /// </summary>
        RateLimited = 21429,
        /// <summary>
        /// The request has been refused because its HTTP headers are too long.
        /// </summary>
        RequestHeaderFieldsTooLarge = 21431,
        /// <summary>
        /// The requested resource is not available for legal reasons.
        /// </summary>
        UnavailableForLegalReasons = 21451,
        /// <summary>
        /// The Matchmaker Service has encountered a situation it doesn't know how to handle.
        /// </summary>
        InternalServerError = 21500,
        /// <summary>
        /// The server does not support the functionality required to fulfil the request.
        /// </summary>
        NotImplemented = 21501,
        /// <summary>
        /// The server, while acting as a gateway or proxy, received an invalid response from the upstream server.
        /// </summary>
        BadGateway = 21502,
        /// <summary>
        /// The Matchmaker Service is not ready to handle the request. Common causes are a server that is down for maintenance or that is overloaded. Try again later.
        /// </summary>
        ServiceUnavailable = 21503,
        /// <summary>
        /// The server, while acting as a gateway or proxy, did not get a response in time from the upstream server that it needed in order to complete the request.
        /// </summary>
        GatewayTimeout = 21504,
        /// <summary>
        /// The server does not support the HTTP protocol that was used in the request.
        /// </summary>
        HttpVersionNotSupported = 21505,
        /// <summary>
        /// The server has an internal configuration error: the chosen variant resource is configured to engage in transparent content negotiation itself, and is therefore not a proper end point in the negotiation process.
        /// </summary>
        VariantAlsoNegotiates = 21506,
        /// <summary>
        /// The server has insufficient storage space to complete the request.
        /// </summary>
        InsufficientStorage = 21507,
        /// <summary>
        /// The server terminated the request because it encountered an infinite loop.
        /// </summary>
        LoopDetected = 21508,
        /// <summary>
        /// The policy for accessing the resource has not been met in the request.
        /// </summary>
        NotExtended = 21510,
        /// <summary>
        /// The request requires authentication for network access.
        /// </summary>
        NetworkAuthenticationRequired = 21511,
        /// <summary>
        /// NetworkError is returned when the client is unable to connect to the service due to a network error like when TLS Negotiation fails.
        /// </summary>
        NetworkError = 21998,
        /// <summary>
        /// Unknown is returned when a unrecognized error code is returned by the service. Check the inner exception to get more information.
        /// </summary>
        Unknown = 21999,
        /// <summary>
        /// End of the range of error codes addressable by the Matchmaker Service.
        /// </summary>
        Max = 21999
    }
}