using System;
using System.Linq;

namespace Unity.Services.Vivox
{
    internal static class Helper
    {
        public static ulong serialNumber = 0;
        private static readonly DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static vx_message_base_t NextMessage()
        {
            vx_message_base_t msg = VivoxCoreInstance.vx_get_message();
            return msg;
        }

        private static void CheckInitialized()
        {
            if (!VxClient.Instance.Started)
            {
                throw new NotSupportedException("Method can not be called before Vivox SDK is initialized.");
            }
        }

        /// <summary>
        /// This is meant to be a shortlived helper to solve some issues we can't correct server-side in time for a release.
        /// If 'sip:' is missing at the beginning of the URI, we prepend it.
        /// If a resource tag is appended at the end of the URI, we remove it.
        /// The returned string is a true SIP URI.
        /// </summary>
        /// <param name="uri">The edit/delete event from_uri to correct.</param>
        /// <returns></returns>
        public static string FixUriFromEditAndDeleteEvents(string uri)
        {
            // Add 'sip:' if we need to.
            var correctedUri = uri.Substring(0, 4) == "sip:" ? uri : $"sip:{uri}";
            // The resource tag at the end of a URI is 35 characters long, starting with a '/'.
            // If we reverse the string and substring 35 charcaters and the final character is indeed a '/', then we'll know it's the resource and it needs to be stripped.
            var reversedString = new string(correctedUri.Reverse().ToArray());
            var stripResourceTag = reversedString.Substring(0, 35)[34] == '/';
            correctedUri = stripResourceTag ? correctedUri.Substring(0, correctedUri.Length - 35) : correctedUri;
            return correctedUri;
        }

        public static string GetRandomUserId(string prefix)
        {
            CheckInitialized();
            return VivoxCoreInstance.vx_get_random_user_id(prefix);
        }

        public static string GetRandomUserIdEx(string prefix, string issuer)
        {
            CheckInitialized();
            return VivoxCoreInstance.vx_get_random_user_id_ex(prefix, issuer);
        }

        public static string GetRandomChannelUri(string prefix, string realm)
        {
            CheckInitialized();
            return VivoxCoreInstance.vx_get_random_channel_uri(prefix, realm);
        }

        /// <summary>
        /// Obtain the time in seconds from the Unix epoch to now with the option of an added duration.
        /// </summary>
        /// <param name="duration">The timespan ahead of (DateTime.UtcNow - Unix epoch) that you want to have a timestamp for.</param>
        /// <returns>The time in seconds from the Unix epoch (January 1st, 1970, 00:00:00) to DateTime.UtcNow with an added duration.</returns>
        public static TimeSpan TimeSinceUnixEpochPlusDuration(TimeSpan duration)
        {
            TimeSpan timestamp = DateTime.UtcNow.Subtract(unixEpoch);
            TimeSpan expiration = timestamp.Add(duration);
            return expiration;
        }

        /// <summary>
        /// Converts the provided DateTime to UTC if necessary then converts the Date Time to Unix time and returns the number of seconds.
        /// </summary>
        /// <param name="dateTime">Datetime to convert to Unix time.</param>
        /// <returns>The time in seconds from the Unix epoch (January 1st, 1970, 00:00:00) to the provided DateTime in UTC.</returns>
        public static long ToUtcUnixTimeInSeconds(this DateTime dateTime)
        {
            var timeOffset = new DateTimeOffset(dateTime);
            return timeOffset.ToUnixTimeSeconds();
        }

        /// <summary>
        /// Get unix epoch time from a long in UTC
        /// </summary>
        /// <param name="unixTimeStamp">Long in ticks/microseconds since Epoch time</param>
        /// <returns>The time in seconds from the Unix epoch (January 1st, 1970, 00:00:00) to in UTC time</returns>
        public static DateTime UnixTimeStampToDateTime(long unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddTicks(unixTimeStamp);
            return dateTime;
        }

        /// <summary>
        /// Get the Number of pages needed to get all total items.  This will around up to help include partially filled pages.
        /// </summary>
        /// <param name="totalItems">Total Number of items to paginate</param>
        /// <param name="pageSize">Amount of items per page</param>
        /// <returns></returns>
        public static int NumberOfPages(int totalItems, int pageSize = 10)
        {
            int  fullyFilledPages = totalItems / pageSize;
            int partiallyFilledPages = (totalItems % pageSize == 0) ? 0 : 1;
            return fullyFilledPages + partiallyFilledPages;
        }

        public static T Clamp<T>(this T val, T min, T max) where T : IComparable<T>
        {
            if (val.CompareTo(min) < 0)
            {
                return min;
            }
            else if (val.CompareTo(max) > 0)
            {
                return max;
            }
            else
            {
                return val;
            }
        }
    }
}
