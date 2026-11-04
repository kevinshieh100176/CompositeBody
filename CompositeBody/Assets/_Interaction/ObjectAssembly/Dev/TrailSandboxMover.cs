using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.Interaction.ObjectAssembly.Dev
{
    /// <summary>
    /// Test driver for <see cref="OATrail"/> in Dev/TrailSandbox.unity: swings two end objects
    /// together and apart on the shared clock so the distance response (width, brightness, flow
    /// speed, turbulence) can be watched without two players in headsets.
    ///
    /// Play mode only unless <see cref="m_RunInEditMode"/> is ticked, so opening the sandbox does
    /// not leave its transforms somewhere arbitrary. <see cref="SetSeparation"/> places the ends
    /// at an exact distance for captures.
    /// </summary>
    [ExecuteAlways]
    public class TrailSandboxMover : MonoBehaviour
    {
        [SerializeField] Transform m_EndA;
        [SerializeField] Transform m_EndB;
        [SerializeField] OATrail m_Trail;

        [SerializeField, Min(0f), Tooltip("Closest separation in metres.")]
        float m_MinSeparation = 0.1f;
        [SerializeField, Min(0f), Tooltip("Widest separation in metres.")]
        float m_MaxSeparation = 1.8f;
        [SerializeField, Min(0.5f), Tooltip("Seconds for one full apart-together-apart cycle.")]
        float m_Period = 9f;
        [SerializeField, Tooltip("Small hand-like wobble on each end, metres.")]
        float m_Wobble = 0.03f;
        [SerializeField] bool m_RunInEditMode;
        [SerializeField, Range(0f, 1f), Tooltip("Visibility pushed to the trail every frame.")]
        float m_TrailVisibility = 1f;

        void Update()
        {
            if (!Application.isPlaying && !m_RunInEditMode) return;

            float t = ExperienceClock.now;
            float cycle = 0.5f + 0.5f * Mathf.Cos(t * (Mathf.PI * 2f / m_Period));
            Place(Mathf.Lerp(m_MinSeparation, m_MaxSeparation, cycle), t);
        }

        /// <summary>Put the ends exactly <paramref name="metres"/> apart, centred on this object.</summary>
        public void SetSeparation(float metres) => Place(metres, ExperienceClock.now, false);

        void Place(float separation, float t, bool wobble = true)
        {
            if (m_EndA == null || m_EndB == null) return;

            Vector3 centre = transform.position;
            Vector3 axis = transform.right * (separation * 0.5f);

            Vector3 wa = Vector3.zero, wb = Vector3.zero;
            if (wobble && m_Wobble > 0f)
            {
                wa = new Vector3(0f, Mathf.Sin(t * 1.3f), Mathf.Sin(t * 0.9f + 1f)) * m_Wobble;
                wb = new Vector3(0f, Mathf.Sin(t * 1.1f + 2f), Mathf.Sin(t * 0.7f + 3f)) * m_Wobble;
            }

            m_EndA.position = centre - axis + wa;
            m_EndB.position = centre + axis + wb;

            if (m_Trail != null) m_Trail.visibility = m_TrailVisibility;
        }
    }
}
