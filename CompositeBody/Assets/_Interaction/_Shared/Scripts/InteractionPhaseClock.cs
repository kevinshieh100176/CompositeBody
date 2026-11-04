using Unity.Netcode;
using CompositeBody.Experience;

namespace CompositeBody.Interaction
{
    /// <summary>
    /// Time stamps the server writes and every machine reads. A phase that started at a stamp is
    /// drawn as a function of "seconds since the stamp" on the shared clock, so a headset that
    /// joins late -- or hitches for a second -- lands on the same frame as the other one instead
    /// of replaying the phase from its start.
    /// </summary>
    public static class InteractionPhaseClock
    {
        /// <summary>The value of a stamp that has not been set.</summary>
        public const double Unset = -1.0;

        /// <summary>
        /// Seconds on the shared clock, in double precision when a session is running (the server
        /// time NGO keeps in sync), otherwise <see cref="ExperienceClock.now"/>'s local fallback.
        /// </summary>
        public static double now
        {
            get
            {
                var nm = NetworkManager.Singleton;
                if (nm != null && nm.IsListening) return nm.ServerTime.Time;
                return ExperienceClock.now;
            }
        }

        public static bool IsSet(double stamp) => stamp >= 0.0;

        /// <summary>Seconds since <paramref name="stamp"/>, or a negative number if it is unset or in the future.</summary>
        public static float Since(double stamp) => IsSet(stamp) ? (float)(now - stamp) : -1f;
    }
}
