using System;
using Unity.Services.DistributedAuthority.Http;
using Unity.Services.DistributedAuthority.Models;
using Unity.Services.Core;

namespace Unity.Services.DistributedAuthority.Exceptions
{
    /// <summary>
    /// Represents an exception that occurs when communicating with the Unity Distributed Authority Service.
    /// </summary>
    internal class DistributedAuthorityServiceException : RequestFailedException
    {
        /// <summary>
        /// The reason of the exception.
        /// </summary>
        public DistributedAuthorityExceptionReason Reason { get; private set; }

        /// <summary>
        /// If applicable, the specific details of the API error that caused the exception.
        /// </summary>
        public ErrorResponseBody ApiError
        {
            get
            {
                HttpException<ErrorResponseBody> apiException = InnerException as HttpException<ErrorResponseBody>;
                if (apiException?.ActualError == null)
                {
                    return default;
                }

                return apiException.ActualError;
            }
        }

        /// <summary>
        /// Creates a DistributedAuthorityServiceException.
        /// </summary>
        /// <param name="reason">The error code or the HTTP Status returned by the service.</param>
        /// <param name="message">The description of the exception.</param>
        /// <param name="innerException">The exception raised by the service, if any.</param>
        public DistributedAuthorityServiceException(DistributedAuthorityExceptionReason reason, string message, Exception innerException) : base((int)reason, message, innerException)
        {
            Reason = reason;
        }

        /// <summary>
        /// Creates a DistributedAuthorityServiceException.
        /// </summary>
        /// <param name="reason">The error code or the HTTP Status returned by the service.</param>
        /// <param name="message">The description of the exception.</param>
        public DistributedAuthorityServiceException(DistributedAuthorityExceptionReason reason, string message) : base((int)reason, message)
        {
            Reason = reason;
        }

        /// <summary>
        /// Creates a DistributedAuthorityServiceException.
        /// </summary>
        /// <param name="errorCode">The error code or the HTTP Status returned by the service.</param>
        /// <param name="message">The description of the exception.</param>
        public DistributedAuthorityServiceException(long errorCode, string message) : base((int)errorCode, message)
        {
            if (Enum.IsDefined(typeof(DistributedAuthorityExceptionReason), errorCode))
            {
                Reason = (DistributedAuthorityExceptionReason)errorCode;
            }
            else
            {
                Reason = DistributedAuthorityExceptionReason.Unknown;
            }
        }

        /// <summary>
        /// Creates a DistributedAuthorityServiceException.
        /// </summary>
        /// <param name="innerException">The exception raised by the service, if any.</param>
        public DistributedAuthorityServiceException(Exception innerException) : base((int)DistributedAuthorityExceptionReason.Unknown, "Unknown Distributed Authority Service Exception", innerException)
        {
        }
    }
}
