using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace CompositeBody.Experience
{
    /// <summary>
    /// 「玩家沒有明確的身體…和真人觸碰後，才出現自己的雙手，只能隱約看見自己的雙手」.
    ///
    /// One hand, drawn as a drift of particles sitting on the tracked joints rather than as a
    /// hand model. That is the brief read literally: the player has no definite body, and what
    /// arrives is 「從粒子與膜的流動裡出現」 -- so a mesh of a hand would be answering a different
    /// question. It is also the honest one, because hand tracking jitters, and jitter on a
    /// recognisable hand silhouette reads as broken where jitter in a cloud reads as a cloud.
    ///
    /// The particles form OUTWARD FROM A POINT, which is set to wherever the player touched the
    /// 真人. That is what makes them 「像從真人身上剝落出來的延伸」 -- an extension flaking off the
    /// scanned body -- instead of a hand that simply fades up. Without the ordering it is a
    /// cross-fade; with it, the hand arrives from the place the player put it.
    ///
    /// Driven through a ParticleSystem the component writes directly, not through emission. The
    /// positions come from the hand every frame and nothing is simulated, so a spawner would only
    /// be something else to keep in step.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class HandCloud : MonoBehaviour
    {
        [SerializeField] Handedness m_Hand = Handedness.Right;

        [SerializeField, Range(1, 24), Tooltip("Particles per tracked joint. The hand has 26, so " +
                                               "this times 26 is the whole cloud.")]
        int m_PerJoint = 8;

        [SerializeField, Range(0.002f, 0.05f), Tooltip("Metres the particles scatter around each " +
                                                       "joint. Wide enough to read as a drift, " +
                                                       "tight enough to still be a hand.")]
        float m_Spread = 0.014f;

        [SerializeField, Range(0.001f, 0.03f)] float m_Size = 0.006f;

        [Header("Look")]
        [SerializeField] Color m_Tint = new(0.78f, 0.80f, 0.92f, 1f);

        [SerializeField, Range(0f, 1f), Tooltip("「只能隱約看見」. Low on purpose -- the hands are " +
                                                "meant to be barely there, not a UI.")]
        float m_Opacity = 0.34f;

        [Header("Forming")]
        [SerializeField, Range(0f, 1f), Tooltip("0 is nothing, 1 is the whole hand.")]
        float m_Reveal;

        [SerializeField, Range(0.05f, 2f), Tooltip("Metres from the forming origin to the last " +
                                                   "particle to arrive.")]
        float m_RevealRadius = 0.7f;

        [SerializeField, Range(0.02f, 1f), Tooltip("How sharp the forming front is. Soft is a " +
                                                   "wash; hard is a wave crossing the hand.")]
        float m_FrontWidth = 0.35f;

        ParticleSystem m_System;
        ParticleSystem.Particle[] m_Particles;
        Vector3[] m_Jitter;
        XROrigin m_Origin;
        XRHandSubsystem m_Subsystem;
        Vector3 m_RevealFrom;
        bool m_HasOrigin;

        static readonly System.Collections.Generic.List<XRHandSubsystem> s_Subsystems = new();

        /// <summary>0 is nothing, 1 is the whole hand.</summary>
        public float reveal
        {
            get => m_Reveal;
            set => m_Reveal = Mathf.Clamp01(value);
        }

        /// <summary>Where the hand starts forming from, in world space. The touch point.</summary>
        public void FormFrom(Vector3 worldPoint)
        {
            m_RevealFrom = worldPoint;
            m_HasOrigin = true;
        }

        void Awake()
        {
            m_System = GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = m_System.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = 32 * 24;
            ParticleSystem.EmissionModule emission = m_System.emission;
            emission.enabled = false;

            BuildJitter();
        }

        /// <summary>
        /// Fixed offsets, generated once. Re-randomising per frame would make the cloud boil, and
        /// at this density boiling is the only thing anyone would see.
        /// </summary>
        void BuildJitter()
        {
            int count = 32 * m_PerJoint;
            m_Jitter = new Vector3[count];
            var random = new System.Random(20261110);
            for (int i = 0; i < count; i++)
            {
                // Rejection-free: a normalised vector scaled by the cube root of a uniform, which
                // fills the ball evenly instead of clustering at the centre like a raw lerp.
                var dir = new Vector3((float)random.NextDouble() * 2f - 1f,
                                      (float)random.NextDouble() * 2f - 1f,
                                      (float)random.NextDouble() * 2f - 1f);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
                float t = Mathf.Pow((float)random.NextDouble(), 1f / 3f);
                m_Jitter[i] = dir.normalized * t;
            }
            m_Particles = new ParticleSystem.Particle[count];
        }

        void LateUpdate()
        {
            if (m_System == null) return;

            if (m_Reveal <= 0.001f || !TryGetSubsystem() || !TryGetOrigin())
            {
                if (m_System.particleCount > 0) m_System.Clear();
                return;
            }

            XRHand hand = m_Hand == Handedness.Left ? m_Subsystem.leftHand : m_Subsystem.rightHand;
            if (!hand.isTracked)
            {
                if (m_System.particleCount > 0) m_System.Clear();
                return;
            }

            Transform space = m_Origin.CameraFloorOffsetObject != null
                ? m_Origin.CameraFloorOffsetObject.transform
                : m_Origin.transform;

            // The front travels a little past the radius so the last particles do arrive, rather
            // than the hand finishing at 95% forever.
            float front = m_Reveal * (1f + m_FrontWidth);
            int n = 0;

            for (int i = XRHandJointID.BeginMarker.ToIndex(); i < XRHandJointID.EndMarker.ToIndex(); i++)
            {
                XRHandJoint joint = hand.GetJoint(XRHandJointIDUtility.FromIndex(i));
                if (!joint.TryGetPose(out Pose pose)) continue;

                Vector3 world = space.TransformPoint(pose.position);

                // How far through the forming this joint is. Without a touch point the hand just
                // fades up uniformly, which is the right fallback for a desktop test.
                float order = m_HasOrigin
                    ? Mathf.Clamp01(Vector3.Distance(world, m_RevealFrom) / m_RevealRadius)
                    : 0f;
                float alpha = Mathf.Clamp01((front - order) / m_FrontWidth);
                if (alpha <= 0.002f) continue;

                for (int k = 0; k < m_PerJoint && n < m_Particles.Length; k++, n++)
                {
                    Vector3 offset = m_Jitter[(i * m_PerJoint + k) % m_Jitter.Length] * m_Spread;
                    m_Particles[n].position = world + offset;
                    m_Particles[n].startSize = m_Size;
                    // Lifetime is irrelevant -- the array is rewritten every frame -- but a
                    // particle at zero remaining lifetime is culled before it draws.
                    m_Particles[n].remainingLifetime = 1f;
                    m_Particles[n].startLifetime = 1f;
                    m_Particles[n].velocity = Vector3.zero;
                    var c = m_Tint;
                    c.a = alpha * m_Opacity;
                    m_Particles[n].startColor = c;
                }
            }

            m_System.SetParticles(m_Particles, n);
        }

        bool TryGetSubsystem()
        {
            if (m_Subsystem != null && m_Subsystem.running) return true;

            SubsystemManager.GetSubsystems(s_Subsystems);
            foreach (var subsystem in s_Subsystems)
            {
                if (subsystem == null || !subsystem.running) continue;
                m_Subsystem = subsystem;
                return true;
            }
            m_Subsystem = null;
            return false;
        }

        bool TryGetOrigin()
        {
            if (m_Origin != null) return true;
            m_Origin = FindFirstObjectByType<XROrigin>();
            return m_Origin != null;
        }
    }
}
