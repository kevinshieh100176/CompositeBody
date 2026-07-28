using System;
using Unity.Services.DistributedAuthority.Http;
using Unity.Services.DistributedAuthority.Models;

namespace Unity.Services.DistributedAuthority
{
    internal static class ApiErrorExtender
    {
        public static DistributedAuthorityExceptionReason GetExceptionReason(this ErrorResponseBody error)
        {
            DistributedAuthorityExceptionReason reason = DistributedAuthorityExceptionReason.Unknown;

            if (error.Code != (int)DistributedAuthorityExceptionReason.NoError)
            {
                if (Enum.IsDefined(typeof(DistributedAuthorityExceptionReason), error.Code))
                {
                    reason = (DistributedAuthorityExceptionReason)error.Code;
                }
            }
            else if (Enum.IsDefined(typeof(DistributedAuthorityExceptionReason), error.Status))
            {
                reason = (DistributedAuthorityExceptionReason)error.Status;
            }

            return reason;
        }

        public static DistributedAuthorityExceptionReason GetExceptionReason(this HttpClientResponse error)
        {
            DistributedAuthorityExceptionReason reason = DistributedAuthorityExceptionReason.Unknown;

            if (error.IsHttpError)
            {
                //As we know it's a http error (error range 0-1000), we bump it to our mapped range
                int mappedCode = (int)error.StatusCode + (int)DistributedAuthorityExceptionReason.Min;
                if (Enum.IsDefined(typeof(DistributedAuthorityExceptionReason), mappedCode))
                {
                    reason = (DistributedAuthorityExceptionReason)mappedCode;
                }
            }
            else if (error.IsNetworkError)
            {
                reason = DistributedAuthorityExceptionReason.NetworkError;
            }

            return reason;
        }

        public static string GetExceptionMessage(this ErrorResponseBody error)
        {
            string message = $"{error.Title}: {error.Detail}";
            if (error.Details == null)
            {
                return message;
            }

            foreach (var errorDetail in error.Details)
            {
                message += $"\n{errorDetail.Error}: {errorDetail.Message}";
            }

            return message;
        }
    }
}
