using Unity.Netcode;
using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// A clock both headsets agree on.
    ///
    /// Ambient motion and ambient sound are derived from this rather than from
    /// <see cref="Time.time"/>, so the two players see the same frame drifting the same way and
    /// hear the same distant sound from the same direction without any of it being replicated.
    /// Two machines started a minute apart would otherwise each run their own private version
    /// of the room, which is the one thing a piece about sharing a space cannot afford.
    ///
    /// Falls back to local time when there is no session, so a single-machine desk test still
    /// animates instead of freezing.
    /// </summary>
    public static class ExperienceClock
    {
        /// <summary>Seconds on the shared clock, or local time if there is no session yet.</summary>
        public static float now
        {
            get
            {
                var nm = NetworkManager.Singleton;
                if (nm != null && nm.IsListening) return (float)nm.ServerTime.Time;
                return Time.time;
            }
        }

        /// <summary>True when <see cref="now"/> is actually shared rather than local-only.</summary>
        public static bool isShared
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening;
            }
        }

        /// <summary>
        /// A stable per-object offset in [0,1), so a crowd of objects driven by one clock do not
        /// all move in lockstep. Derived from the authored position, which means it is identical
        /// on both machines and survives a scene reload.
        /// </summary>
        public static float PhaseFor(Vector3 authoredPosition)
        {
            // Quantised before hashing so floating-point noise between two builds of the same
            // scene cannot produce two different phases for the same object.
            int x = Mathf.RoundToInt(authoredPosition.x * 100f);
            int y = Mathf.RoundToInt(authoredPosition.y * 100f);
            int z = Mathf.RoundToInt(authoredPosition.z * 100f);

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                hash = hash * 31 + z;
                return Mathf.Abs(hash % 10000) / 10000f;
            }
        }
    }
}
