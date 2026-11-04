using UnityEngine;

namespace CompositeBody.Interaction.Home
{
    /// <summary>
    /// A Home fragment's look, on its VISUAL child (see <see cref="BreathingView"/> for why a child).
    /// Adds one thing to the shared view: the in-hand tremble that grows while the fragment is
    /// held inside the assembly zone, so the hand FEELS the dwell counting down -- a fragment that
    /// shakes harder and harder and then pulls itself out of the hand reads as the house wanting
    /// it, where a silent timer would read as the grab breaking.
    ///
    /// Everything else -- breathing while it waits, the settle after it lands, the appear and the
    /// dissolve (reveal01) -- is the base class, driven by <see cref="HomeFragment"/> from
    /// replicated state, so both headsets draw the same thing without any of it being sent.
    /// </summary>
    public class HomeFragmentView : BreathingView
    {
        [Header("Dwell tremble")]
        [SerializeField, Range(0f, 1f), Tooltip("Tremble the moment the fragment enters the zone.")]
        float m_DwellShakeStart = 0.15f;

        [SerializeField, Range(0f, 1f), Tooltip("Tremble just before it places itself.")]
        float m_DwellShakeEnd = 1f;

        [SerializeField, Min(0.1f), Tooltip("Shape of the ramp: 1 linear, >1 stays calm longer and then rushes.")]
        float m_DwellShakeExponent = 1.6f;

        [SerializeField, Range(0f, 0.2f), Tooltip("Scale it swells by as the dwell completes -- a held breath.")]
        float m_DwellSwell = 0.04f;

        float m_Dwell = -1f;

        /// <summary>
        /// 0..1 progress of the dwell while held inside the zone, or negative when not counting.
        /// Set every frame by the fragment.
        /// </summary>
        public float dwell01 { get => m_Dwell; set => m_Dwell = value; }

        protected override void LateUpdate()
        {
            shake01 = m_Dwell < 0f
                ? 0f
                : Mathf.Lerp(m_DwellShakeStart, m_DwellShakeEnd, Mathf.Pow(Mathf.Clamp01(m_Dwell), m_DwellShakeExponent));
            base.LateUpdate();
        }

        protected override void Compose(float now, ref Vector3 offset, ref Quaternion turn, ref float scale)
        {
            if (m_Dwell > 0f) scale += m_DwellSwell * Mathf.Clamp01(m_Dwell);
        }
    }
}
