using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.Interaction.Home
{
    /// <summary>
    /// A soft column of light standing on the assembly zone, so the first fragment has somewhere to
    /// go: nothing else in the room says where the mini-house is until a piece of it is there.
    ///
    /// It is O-0's <see cref="VolumetricColumn"/> (same shader, same analytic volume), driven here:
    /// hidden while the beat is dormant, fading in from the beat's start, breathing on the shared
    /// clock with the fragments' period, and fading out once the FIRST fragment is resolved --
    /// placed or taken by the timeout. After that the house itself is the marker, so it stays off
    /// for the rest of the beat; a reset (new epoch, new start stamp) brings it back.
    ///
    /// LOCAL ON EVERY MACHINE, a pure function of replicated stamps (armedAt, the first resolvedAt),
    /// so both headsets fade it in step and a late joiner sees it where it should be.
    /// </summary>
    [DisallowMultipleComponent]
    public class HomeZoneBeacon : MonoBehaviour
    {
        [SerializeField] HomeAssembly m_Assembly;

        [SerializeField, Tooltip("The light. Found on this object if left empty.")]
        VolumetricColumn m_Column;

        [SerializeField, Min(0f), Tooltip("Seconds to fade in when the beat starts.")]
        float m_FadeIn = 1.5f;

        [SerializeField, Min(0f), Tooltip("Seconds to fade out after the first fragment is resolved.")]
        float m_FadeOut = 1.5f;

        [SerializeField, Range(0f, 1f), Tooltip("Overall strength (the column's colour, intensity and " +
                                                 "density are on the VolumetricColumn).")]
        float m_Strength = 1f;

        [SerializeField, Min(0.2f), Tooltip("Seconds per breath. The fragments' BreathingView default is 3.2.")]
        float m_BreathPeriod = 3.2f;

        [SerializeField, Range(0f, 1f), Tooltip("How far a breath dips: 0 steady, 1 out to nothing.")]
        float m_BreathDepth = 0.35f;

        float m_Phase;

        /// <summary>0..1 fade (beat started, first fragment not yet resolved), without the breath.</summary>
        public float level { get; private set; }

        /// <summary>What the column is drawn at this frame (fade x breath x strength).</summary>
        public float drawn { get; private set; }

        public VolumetricColumn column => m_Column;

        void Awake()
        {
            if (m_Column == null) m_Column = GetComponent<VolumetricColumn>();
            m_Phase = ExperienceClock.PhaseFor(transform.position);
        }

        void LateUpdate()
        {
            level = Level();
            float wave = Mathf.Sin((ExperienceClock.now / m_BreathPeriod + m_Phase) * 2f * Mathf.PI) * 0.5f + 0.5f;
            drawn = m_Strength * level * Mathf.Lerp(1f - m_BreathDepth, 1f, wave);
            if (m_Column != null && !Mathf.Approximately(m_Column.reveal, drawn)) m_Column.reveal = drawn;
        }

        float Level()
        {
            if (m_Assembly == null || !m_Assembly.armed) return 0f;
            float up = Ramp(InteractionPhaseClock.Since(m_Assembly.armedAt), m_FadeIn);
            double first = m_Assembly.firstResolvedAt;
            float down = InteractionPhaseClock.IsSet(first)
                ? 1f - Ramp(InteractionPhaseClock.Since(first), m_FadeOut)
                : 1f;
            return up * down;
        }

        static float Ramp(float since, float duration)
        {
            if (since < 0f) return 0f;
            if (duration <= 0f) return 1f;
            float t = Mathf.Clamp01(since / duration);
            return t * t * (3f - 2f * t);
        }
    }
}
