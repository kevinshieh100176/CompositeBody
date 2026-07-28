using System;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// The exception for session operations.
    /// </summary>
    public class SessionException : Exception
    {
        /// <summary>
        /// Gets the error type.
        /// </summary>
        public SessionError Error { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionException"/>
        /// class with a specified error message, error type, and a reference
        /// to the inner exception that is the cause of this exception if any.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="error">The session error type.</param>
        /// <param name="innerException">
        /// The exception that is the cause of the current exception,
        /// or <c>null</c> if no inner exception is specified.
        /// </param>
        public SessionException(string message, SessionError error, Exception innerException) : base(message, innerException)
        {
            Error = error;
        }

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="SessionException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="error">The exception type.</param>
        internal SessionException(string message, SessionError error) : this(message, error, null)
        {
        }

        /// <summary>
        /// Returns a string representation of the
        /// <see cref="SessionException"/>instance.
        /// </summary>
        /// <returns>
        /// A string that represents the current exception with its <see
        /// cref="SessionError"/> and its <see cref="Exception.Message"/>
        /// .</returns>
        public override string ToString()
        {
            if (InnerException != null)
            {
                return $"SessionException: [Error: {Error}] [Message: {Message}] caused by {InnerException}";
            }

            return $"SessionException: [Error: {Error}] [Message: {Message}]";
        }
    }
}
