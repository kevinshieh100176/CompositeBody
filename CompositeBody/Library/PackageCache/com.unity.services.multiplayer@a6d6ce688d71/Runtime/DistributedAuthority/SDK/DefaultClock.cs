using System;

namespace Unity.Services.DistributedAuthority.Internal
{
    internal interface IClock
    {
        DateTimeOffset UtcNow();
    }

    internal class DefaultClock : IClock
    {
        public DateTimeOffset UtcNow()
        {
            return DateTimeOffset.UtcNow;
        }
    }
}
