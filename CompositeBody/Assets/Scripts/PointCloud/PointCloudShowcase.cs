using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.PointClouds
{
    /// <summary>
    /// Walks the test scene's figures through their states on a loop, and dollies the camera so
    /// the proximity-driven variant shows what it does.
    ///
    /// Exists because three of the four things worth looking at cannot be seen in a still. The
    /// turbulence only reads as motion, the glitch only reads as something that steps between
    /// held frames, and <see cref="CompositeBody.PointClouds"/>' Fro variant is driven entirely
    /// by how far away the viewer is -- so a scene you have to drive by hand to see any of that
    /// is a scene nobody checks. Press play and the whole set demonstrates itself.
    ///
    /// Timed from <see cref="ExperienceClock"/> like everything else here, so the cycle is the
    /// same on two machines and a screenshot taken at a given moment is reproducible.
    /// </summary>
    public class PointCloudShowcase : MonoBehaviour
    {
        [SerializeField, Tooltip("Figures to drive. Filled in by the scene builder.")]
        PointCloudFigure[] m_Figures;

        [SerializeField, Min(2f), Tooltip("Seconds for one full pass: hold, glitch, dissolve, re-form.")]
        float m_CycleSeconds = 24f;

        [SerializeField, Tooltip("Stagger each figure through the cycle so they are never all doing the same thing.")]
        bool m_Stagger = true;

        [Header("Camera")]
        [SerializeField, Tooltip("Dollied toward and away from the target below. Leave empty to leave the camera alone.")]
        Transform m_Camera;

        [SerializeField, Tooltip("What the dolly approaches -- point it at the Fro station, whose effect is distance.")]
        Transform m_DollyTarget;

        [SerializeField, Min(0.2f)] float m_DollyNear = 0.6f;
        [SerializeField, Min(0.3f)] float m_DollyFar = 4.2f;
        [SerializeField, Min(2f)] float m_DollySeconds = 16f;

        [Header("Debug")]
        [SerializeField, Tooltip("Log each figure's state change, for a headless capture run.")]
        bool m_LogStates;

        // Phase each figure was last seen in, so a change can be acted on once rather than
        // every frame -- DissolveOver restarted per frame would never advance.
        int[] m_LastPhase;
        Vector3 m_DollyBase;
        bool m_HasBase;

        void OnEnable()
        {
            if (m_Figures == null) return;
            m_LastPhase = new int[m_Figures.Length];
            for (int i = 0; i < m_LastPhase.Length; i++) m_LastPhase[i] = -1;
        }

        void Update()
        {
            float now = ExperienceClock.now;
            DriveFigures(now);
            DriveCamera(now);
        }

        void DriveFigures(float now)
        {
            if (m_Figures == null) return;

            for (int i = 0; i < m_Figures.Length; i++)
            {
                PointCloudFigure figure = m_Figures[i];
                if (figure == null) continue;

                float offset = m_Stagger ? m_CycleSeconds * i / Mathf.Max(1, m_Figures.Length) : 0f;
                float t = Mathf.Repeat(now + offset, m_CycleSeconds) / m_CycleSeconds;

                // Four quarters: hold whole, stutter, come apart, form again. The glitch burst
                // lands inside the second quarter rather than at its edge so it is not masked
                // by the dissolve starting.
                int phase = t < 0.30f ? 0 : t < 0.50f ? 1 : t < 0.78f ? 2 : 3;
                if (phase == m_LastPhase[i]) continue;
                m_LastPhase[i] = phase;

                switch (phase)
                {
                    case 0:
                        figure.Reset();
                        break;
                    case 1:
                        figure.GlitchBurst(0.8f, m_CycleSeconds * 0.16f);
                        break;
                    case 2:
                        figure.DissolveOver(m_CycleSeconds * 0.22f);
                        break;
                    case 3:
                        figure.dissolve = 0f;
                        figure.reveal = 0f;
                        figure.RevealOver(m_CycleSeconds * 0.18f);
                        break;
                }

                if (m_LogStates)
                    Debug.Log($"[Showcase] {figure.name} -> phase {phase} at {now:0.0}s");
            }
        }

        void DriveCamera(float now)
        {
            if (m_Camera == null || m_DollyTarget == null) return;

            if (!m_HasBase)
            {
                m_DollyBase = m_Camera.position;
                m_HasBase = true;
            }

            // Ping-pong on a cosine rather than Mathf.PingPong, so the camera eases at both
            // ends instead of reversing with a visible jerk at the near point -- which is
            // exactly where the approach effect is most interesting to look at.
            float u = 0.5f - 0.5f * Mathf.Cos(now / Mathf.Max(0.1f, m_DollySeconds) * Mathf.PI * 2f);
            float distance = Mathf.Lerp(m_DollyFar, m_DollyNear, u);

            Vector3 target = m_DollyTarget.position;
            Vector3 fromBase = m_DollyBase - target;
            fromBase.y = 0f;
            if (fromBase.sqrMagnitude < 0.0001f) fromBase = Vector3.back;

            Vector3 flat = fromBase.normalized * distance;
            m_Camera.position = new Vector3(target.x + flat.x, m_DollyBase.y, target.z + flat.z);
            m_Camera.LookAt(target);
        }
    }
}
