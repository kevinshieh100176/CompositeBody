using System;
using Unity.Services.Core;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// An Exception relating to the Vivox Mint Authentication service, which checks Vivox tokens against the Unity Game Gateway
    /// </summary>
    /// <remarks>
    /// This exception type is deprecated as the Mint service has been removed.
    /// It is retained for binary compatibility but will not be thrown by current Vivox services.
    /// </remarks>
    [Serializable]
    [Obsolete("The Mint service is being deprecated. MintException will be removed in a future release. " +
        "Please update your code to no longer rely on this exception type. ", false)]
    public class MintException : RequestFailedException
    {
        /// <summary>
        /// The Exception details received from the server if present. Null otherwise.
        /// (This property is now a stub as the Mint service is deprecated.)
        /// </summary>
        public string Detail => throw new NotSupportedException("The Mint service has been deprecated and this property is no longer populated.");

        /// <summary>
        /// Date and time of the end of the ban if applicable. Null otherwise.
        /// (This property is now a stub as the Mint service is deprecated.)
        /// </summary>
        public DateTime? ExpiresAt => throw new NotSupportedException("The Mint service has been deprecated and this property is no longer populated.");

        /// <summary>
        /// The Exception error code enum value
        /// (This property is now a stub as the Mint service is deprecated.)
        /// </summary>
        public MintExceptionCode ExceptionCode => MintExceptionCode.ServiceDeprecated;

        /// <summary>
        /// The exception ctor
        /// (This constructor is retained for binary compatibility but will not be called by current Vivox services.)
        /// </summary>
        /// <param name="innerException"> The exception that the Mint exception is being created from </param>
        internal MintException(Exception innerException) : base(
            (int)MintExceptionCode.ServiceDeprecated,
            "Mint service is deprecated and no longer available.",
            innerException)
        {
        }
    }
}
