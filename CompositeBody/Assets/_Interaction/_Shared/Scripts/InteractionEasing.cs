using UnityEngine;

namespace CompositeBody.Interaction
{
    /// <summary>
    /// The handful of curves the interaction modules share. Pure functions of time, so every
    /// machine evaluating them from the same replicated start time draws the same frame -- which
    /// is why nothing here is a coroutine or keeps state.
    /// </summary>
    public static class InteractionEasing
    {
        /// <summary>
        /// Slow → fast ("由慢到快"). <paramref name="curve"/> wins when it has keys; otherwise a
        /// cubic ease-in, which leaves the hand gently and arrives with intent.
        /// </summary>
        public static float EaseIn(float t, AnimationCurve curve = null)
        {
            t = Mathf.Clamp01(t);
            if (curve != null && curve.length > 0) return curve.Evaluate(t);
            return t * t * t;
        }

        /// <summary>Slow → fast → slow, for long floats where an arrival at speed would read as a crash.</summary>
        public static float EaseInOut(float t, AnimationCurve curve = null)
        {
            t = Mathf.Clamp01(t);
            if (curve != null && curve.length > 0) return curve.Evaluate(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        /// <summary>
        /// The settle after an arrival ("輕微的抖動後定住"): a damped sine that starts at full
        /// amplitude when <paramref name="sinceArrival"/> is 0 and is gone after
        /// <paramref name="duration"/>. Returns a signed scalar in [-1, 1]; callers scale it into
        /// a position or rotation offset. 0 before the arrival.
        /// </summary>
        public static float Wobble(float sinceArrival, float duration, float frequency = 7f)
        {
            if (sinceArrival < 0f || duration <= 0f || sinceArrival >= duration) return 0f;
            float k = sinceArrival / duration;
            float envelope = (1f - k) * (1f - k);
            return Mathf.Sin(sinceArrival * frequency * 2f * Mathf.PI) * envelope;
        }

        /// <summary>
        /// A smooth jitter ("抖動") in [-1, 1] per axis -- Perlin, not random, so it trembles
        /// rather than buzzes, and seeded so two objects shaking side by side are not in lockstep.
        /// </summary>
        public static Vector3 Shake(float time, float frequency, float seed)
        {
            float t = time * frequency;
            return new Vector3(
                Mathf.PerlinNoise(t, seed * 13.1f) * 2f - 1f,
                Mathf.PerlinNoise(seed * 7.7f + 3.3f, t) * 2f - 1f,
                Mathf.PerlinNoise(t + seed * 5.3f, 11.9f) * 2f - 1f);
        }

        /// <summary>
        /// Linear remap of <paramref name="value"/> from [<paramref name="from0"/>, <paramref name="from1"/>]
        /// to [0, 1], clamped. Works with from0 &gt; from1 (e.g. far → 0, near → 1).
        /// </summary>
        public static float Remap01(float value, float from0, float from1)
        {
            if (Mathf.Approximately(from0, from1)) return value >= from1 ? 1f : 0f;
            return Mathf.Clamp01((value - from0) / (from1 - from0));
        }
    }
}
