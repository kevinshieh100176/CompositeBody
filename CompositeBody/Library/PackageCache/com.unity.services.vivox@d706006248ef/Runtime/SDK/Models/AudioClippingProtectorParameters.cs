using UnityEngine;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// Representation of parameters used to determine the behavior of Vivox's audio clipping protector,
    /// </summary>
    public readonly struct AudioClippingProtectorParameters
    {
        /// <summary>
        /// The lowest point in dBFS where clipping protection can be applied.
        /// Value must be between -50.0 and 0.0, inclusive.
        /// </summary>
        public readonly float MinimumThresholdDb;
        /// <summary>
        /// The ratio between boost gain and the calculated ThresholdDb.
        /// Value must be between -1.0 and -0.1, inclusive.
        /// </summary>
        public readonly float ThresholdBoostSlope;

        /// <summary>
        /// Creates a new instance of AudioClippingProtectorParameters with specified threshold and slope values.
        /// </summary>
        /// <param name="minimumThresholdDb">The minimum threshold in dBFS where clipping protection begins. Must be between -50.0 and 0.0.</param>
        /// <param name="thresholdBoostSlope">The ratio between boost gain and threshold. Must be between -1.0 and -0.1.</param>
        public AudioClippingProtectorParameters(float minimumThresholdDb, float thresholdBoostSlope)
        {
            MinimumThresholdDb = Mathf.Clamp(minimumThresholdDb, -50.0f, 0.0f);
            ThresholdBoostSlope = Mathf.Clamp(thresholdBoostSlope, -1.0f, -0.1f);
        }
    }
}
