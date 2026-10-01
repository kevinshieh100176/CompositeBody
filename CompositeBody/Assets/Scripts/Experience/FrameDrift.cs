using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Drifts one of O-0's 「框」 -- the door and window frames that stand in for the shape of a
    /// home before the home exists -- so it hangs in the void like something floating on water
    /// rather than like a prop bolted to the air.
    ///
    /// The motion is a pure function of <see cref="ExperienceClock"/> and the frame's authored
    /// position, so both headsets compute the same pose every frame with nothing replicated and
    /// no simulation to drift out of step.
    /// </summary>
    [ExecuteAlways] // so the drift can be dialled in, and previewed by tooling, without Play mode
    public class FrameDrift : MonoBehaviour
    {
        [Header("Bob")]
        [SerializeField, Min(0f), Tooltip("Vertical travel, in metres.")]
        float m_BobHeight = 0.09f;
        [SerializeField, Min(0.01f)] float m_BobPeriod = 7.5f;

        [Header("Sway")]
        [SerializeField, Min(0f), Tooltip("Horizontal travel, in metres.")]
        float m_SwayDistance = 0.06f;
        [SerializeField, Min(0.01f)] float m_SwayPeriod = 11f;

        [Header("Turn")]
        [SerializeField, Tooltip("Degrees of slow yaw either side of the authored rotation.")]
        float m_YawAmplitude = 3.5f;
        [SerializeField, Min(0.01f)] float m_YawPeriod = 16f;
        [SerializeField, Tooltip("Degrees of tilt, so the frame is never quite level.")]
        float m_TiltAmplitude = 1.8f;
        [SerializeField, Min(0.01f)] float m_TiltPeriod = 9f;

        Vector3 m_RestPosition;
        Quaternion m_RestRotation;
        float m_Phase;
        bool m_Captured;

        void OnEnable() => Capture();

        /// <summary>
        /// Called by the scene builder after it places the frame, so the rest pose that the
        /// drift is measured from is the authored one and not wherever the last run left it.
        /// </summary>
        public void Capture()
        {
            m_RestPosition = transform.localPosition;
            m_RestRotation = transform.localRotation;
            m_Phase = ExperienceClock.PhaseFor(m_RestPosition) * Mathf.PI * 2f;
            m_Captured = true;
        }

        void Update()
        {
            if (!m_Captured) Capture();

            float t = ExperienceClock.now;

            float bob = Mathf.Sin(t * (Mathf.PI * 2f / m_BobPeriod) + m_Phase) * m_BobHeight;
            float sway = Mathf.Sin(t * (Mathf.PI * 2f / m_SwayPeriod) + m_Phase * 1.7f) * m_SwayDistance;

            transform.localPosition = m_RestPosition + new Vector3(sway, bob, sway * 0.6f);

            float yaw = Mathf.Sin(t * (Mathf.PI * 2f / m_YawPeriod) + m_Phase * 2.3f) * m_YawAmplitude;
            float tilt = Mathf.Sin(t * (Mathf.PI * 2f / m_TiltPeriod) + m_Phase * 3.1f) * m_TiltAmplitude;

            transform.localRotation = m_RestRotation * Quaternion.Euler(tilt, yaw, tilt * 0.5f);
        }
    }
}
