using UnityEngine;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// A red elastic cord strung between two players. It hangs slack when they are close and
    /// pulls straight as they separate, and it lights up along its centre once it is stretched
    /// past its rest length.
    ///
    /// The shape is derived from nothing but the two anchor positions, so every client computes
    /// the same curve from state it already replicates. Nothing about the cord needs to go over
    /// the network -- a simulated rope would either desync or cost a stream of position updates
    /// for something purely cosmetic.
    /// </summary>
    [ExecuteAlways] // so the cord can be dialled in, and previewed by tooling, without Play mode
    [RequireComponent(typeof(LineRenderer))]
    public class PlayerTether : MonoBehaviour
    {
        [Header("Anchors")]
        [SerializeField] Transform m_AnchorA;
        [SerializeField] Transform m_AnchorB;

        [Header("Cord")]
        [SerializeField, Min(0.01f), Tooltip("Unstretched length of the cord. Closer than this it sags; further and it goes taut and starts stretching.")]
        float m_RestLength = 3f;
        [SerializeField, Range(4, 128), Tooltip("Points along the curve. Below about 16 the sag visibly kinks.")]
        int m_Segments = 44;
        [SerializeField, Min(0.001f)] float m_SlackWidth = 0.052f;
        [SerializeField, Min(0.001f), Tooltip("Cord thins as it is stretched, the way a loaded elastic does.")]
        float m_TautWidth = 0.028f;

        [Header("Elasticity")]
        [SerializeField, Min(0f), Tooltip("How hard the sag chases its target. Higher snaps faster.")]
        float m_Stiffness = 55f;
        [SerializeField, Min(0f), Tooltip("Bleeds the bounce off. Low values wobble for longer.")]
        float m_Damping = 7f;
        [SerializeField, Min(0.01f), Tooltip("How far past rest length the cord stretches before tension reads as full.")]
        float m_MaxStretch = 0.6f;

        [Header("Ground")]
        [SerializeField, Tooltip("Stop the cord dropping through the floor. A cord with more slack than headroom should pool along the ground, not sink into it.")]
        bool m_ClampToGround = true;
        [SerializeField, Tooltip("Floor height the cord rests on.")]
        float m_GroundY = 0.02f;

        [Header("Sway")]
        [SerializeField, Tooltip("Sideways drift of a slack cord, in metres.")]
        float m_SwayAmplitude = 0.05f;
        [SerializeField] float m_SwaySpeed = 0.85f;

        static readonly int s_TensionId = Shader.PropertyToID("_Tension");

        LineRenderer m_Line;
        MaterialPropertyBlock m_Block;
        Vector3[] m_Points;

        // Sag is integrated rather than assigned, which is the whole reason the cord feels
        // elastic instead of geometric.
        float m_Sag;
        float m_SagVelocity;

        /// <summary>0 while the cord still has slack, ramping to 1 as it is stretched past rest.</summary>
        public float tension { get; private set; }

        void OnEnable()
        {
            EnsureInitialised();
            m_SagVelocity = 0f;
        }

        void EnsureInitialised()
        {
            if (m_Line == null) m_Line = GetComponent<LineRenderer>();
            m_Block ??= new MaterialPropertyBlock();
            if (m_Points == null || m_Points.Length != m_Segments + 1)
                m_Points = new Vector3[m_Segments + 1];
        }

        void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>
        /// Advances the cord by <paramref name="dt"/> seconds. Called every frame, and callable
        /// directly by tooling that moves the anchors itself and needs the spring stepped in
        /// lockstep with its own simulation rather than on the editor tick.
        /// </summary>
        public void Tick(float dt)
        {
            EnsureInitialised();

            if (m_AnchorA == null || m_AnchorB == null)
            {
                m_Line.positionCount = 0;
                return;
            }

            Vector3 a = m_AnchorA.position;
            Vector3 b = m_AnchorB.position;
            Vector3 chord = b - a;
            float distance = chord.magnitude;

            // Sag depth as a circular-arc approximation of a hanging cord. Correct at both
            // limits -- zero when the anchors are a full cord length apart, half the cord when
            // they meet -- and monotonic between them. Closed form on purpose: the exact
            // catenary needs a Newton solve for its shape parameter, and that is not something
            // worth having fail to converge inside a per-frame loop over every pair of players.
            float targetSag = 0.5f * Mathf.Sqrt(Mathf.Max(0f, m_RestLength * m_RestLength - distance * distance));

            // Damped spring toward that target. Overshoot on the way down gives the droop a
            // bounce when players close on each other, and a little rebound when they snap apart.
            if (dt > 0f)
            {
                float accel = (targetSag - m_Sag) * m_Stiffness - m_SagVelocity * m_Damping;
                m_SagVelocity += accel * dt;
                m_Sag = Mathf.Max(0f, m_Sag + m_SagVelocity * dt);
            }
            else
            {
                m_Sag = targetSag;
            }

            tension = Mathf.Clamp01((distance - m_RestLength) / m_MaxStretch);

            BuildCurve(a, b, chord, distance);
            ApplyMaterial();
        }

        void BuildCurve(Vector3 a, Vector3 b, Vector3 chord, float distance)
        {
            // A cord with the anchors on top of each other has no chord direction to work from;
            // hang it straight down rather than dividing by zero.
            Vector3 direction = distance > 1e-4f ? chord / distance : Vector3.down;

            // Sway runs perpendicular to the cord. Crossing with up degenerates when the cord
            // is itself vertical, so fall back to another axis in that case.
            Vector3 side = Vector3.Cross(direction, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(direction, Vector3.forward);
            side.Normalize();

            // A taut cord does not wander, so the sway is scaled by how much slack is left.
            float slack = Mathf.Clamp01(m_Sag / Mathf.Max(m_RestLength * 0.5f, 1e-4f));
            float phase = (Application.isPlaying ? Time.time : (float)UnityEditor_TimeShim()) * m_SwaySpeed;

            if (m_Points.Length != m_Segments + 1) m_Points = new Vector3[m_Segments + 1];

            for (int i = 0; i <= m_Segments; i++)
            {
                float t = (float)i / m_Segments;

                // Peaks at the middle and vanishes at both anchors, so the cord stays pinned to
                // the players however deep it hangs.
                float droop = 4f * t * (1f - t);

                Vector3 p = Vector3.Lerp(a, b, t);
                p += Vector3.down * (m_Sag * droop);
                p += side * (Mathf.Sin(phase + t * 3.1f) * m_SwayAmplitude * droop * slack);

                // Whatever the sag maths says, the cord cannot be under the floor. Clamping
                // per-point rather than capping the sag is deliberate: it flattens only the
                // stretch that would have gone below, so the cord reads as pooling along the
                // ground instead of the whole curve being squashed.
                if (m_ClampToGround) p.y = Mathf.Max(p.y, m_GroundY);

                m_Points[i] = p;
            }

            m_Line.useWorldSpace = true;
            m_Line.positionCount = m_Points.Length;
            m_Line.SetPositions(m_Points);
            m_Line.widthMultiplier = Mathf.Lerp(m_SlackWidth, m_TautWidth, tension);
        }

        void ApplyMaterial()
        {
            // Property block rather than the material asset, so several tethers can share one
            // material without overwriting each other's tension.
            m_Line.GetPropertyBlock(m_Block);
            m_Block.SetFloat(s_TensionId, tension);
            m_Line.SetPropertyBlock(m_Block);
        }

        /// <summary>
        /// Time.time does not advance in edit mode, which would freeze the sway. Uses the
        /// editor's own clock there and the game clock in play mode.
        /// </summary>
        static double UnityEditor_TimeShim()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.timeAsDouble;
#endif
        }

        /// <summary>
        /// Applies the LineRenderer settings the cord expects. Exposed so the same setup runs
        /// from tooling and from the inspector.
        /// </summary>
        [ContextMenu("Configure Line Renderer")]
        public void ConfigureLine()
        {
            EnsureInitialised();
            m_Line.useWorldSpace = true;
            m_Line.alignment = LineAlignment.View;
            m_Line.textureMode = LineTextureMode.Stretch;
            m_Line.numCapVertices = 4;
            m_Line.numCornerVertices = 2;
            m_Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Line.receiveShadows = false;
            m_Line.widthMultiplier = m_SlackWidth;
        }

        public void SetAnchors(Transform a, Transform b)
        {
            m_AnchorA = a;
            m_AnchorB = b;
        }
    }
}
