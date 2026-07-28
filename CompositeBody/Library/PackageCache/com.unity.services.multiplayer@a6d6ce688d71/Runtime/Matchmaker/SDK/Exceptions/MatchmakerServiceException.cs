using System;
using Unity.Services.Core;

namespace Unity.Services.Matchmaker
{
    /// <summary>
    /// Represents an exception that occurs in the usage of the Unity Matchmaker Service.
    /// </summary>
    public class MatchmakerServiceException : RequestFailedException
    {
        /// <summary>
        /// Reason for service failure.
        /// </summary>
        public MatchmakerExceptionReason Reason { get; private set; }

        /// <summary>
        /// Creates a MatchmakerServiceException.
        /// </summary>
        /// <param name="reason">Reason for service failure.</param>
        /// <param name="message">The description of the error.</param>
        /// <param name="innerException">The exception raised internally by the service, if any.</param>
        public MatchmakerServiceException(MatchmakerExceptionReason reason, string message, Exception innerException = null) : base((int)reason, message, innerException)
        {
            Reason = reason;
        }

        /// <summary>
        /// Creates a MatchmakerServiceException (unknown reason as default).
        /// </summary>
        /// <param name="innerException">The exception raised internally by the service, if any.</param>
        public MatchmakerServiceException(Exception innerException) : base((int)MatchmakerExceptionReason.Unknown, "Unknown Matchmaker Service Exception", innerException)
        {
        }
    }
}
