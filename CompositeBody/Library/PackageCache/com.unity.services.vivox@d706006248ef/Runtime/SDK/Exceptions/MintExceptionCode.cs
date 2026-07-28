using System;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Simple mapping of HTTP error codes for easier reading.
    /// </summary>
    /// <remarks>
    /// This enum is deprecated as the Mint service has been removed.
    /// It is retained for binary compatibility but its values will not be used by current Vivox services.
    /// </remarks>
    [Obsolete("The Mint service and its associated exception codes are being deprecated. " +
        "MintExceptionCode will be removed in a future release. " +
        "Please update your code to no longer rely on this enum type. ", false)]
    public enum MintExceptionCode
    {
        /// <summary>
        /// Indicates that the source of the exception is a service that has been deprecated,
        /// specifically the Vivox Mint service. This code serves as the default value
        /// for the deprecated <see cref="MintException"/> and signifies that original specific error
        /// details are no longer applicable or available due to the service's removal.
        /// </summary>
        ServiceDeprecated = -1,

        /// <summary>
        /// Returned when Player is unable to perform the target operation due to sanctions applied by the Moderation SDK or free-tier allowance exhaustion
        /// (This code is specific to the deprecated Mint service.)
        /// </summary>
        Unauthorized = 403,

        /// <summary>
        /// Returned when using an invalid token
        /// (This code is specific to the deprecated Mint service.)
        /// </summary>
        FailedToDecodeToken = 406
    }
}
