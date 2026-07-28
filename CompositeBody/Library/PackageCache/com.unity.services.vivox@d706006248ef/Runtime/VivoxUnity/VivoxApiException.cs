using System;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// The exception thrown when a Vivox API call fails.
    /// </summary>
    public class VivoxApiException : Exception
    {
        /// <summary>
        /// The Vivox status code returned by the failed API call.
        /// </summary>
        public int StatusCode { get; private set; }

        /// <summary>
        /// The request ID associated with the failed API call, if available.
        /// Useful for correlating failures with Vivox service logs.
        /// </summary>
        public string RequestId { get; private set; }

        internal VivoxApiException(int statusCode)
            : this(statusCode, null, null) {}

        internal VivoxApiException(int statusCode, string requestId)
            : this(statusCode, requestId, null) {}

        internal VivoxApiException(int statusCode, Exception inner)
            : this(statusCode, null, inner) {}

        internal VivoxApiException(int statusCode, string requestId, Exception inner)
            : base(BuildMessage(statusCode, requestId), inner)
        {
            StatusCode = statusCode;
            RequestId = requestId;
        }

        static string BuildMessage(int statusCode, string requestId)
        {
            var errorString = statusCode <= (int)vx_tts_status.tts_error_invalid_engine_type
                ? VivoxCoreInstance.vx_get_tts_status_string((vx_tts_status)statusCode)
                : VivoxCoreInstance.vx_get_error_string(statusCode);

            return string.IsNullOrEmpty(requestId)
                ? $"{errorString} ({statusCode})"
                : $"{errorString} ({statusCode}) [RequestId: {requestId}]";
        }
    }
}
