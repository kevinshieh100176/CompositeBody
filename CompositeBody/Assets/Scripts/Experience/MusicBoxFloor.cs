using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// The plate under a figure that 「似乎在旋轉，像是音樂盒」. Drives the pattern; nothing in the
    /// scene actually turns. See MusicBoxFloor.shader for why that distinction is the design and
    /// not a shortcut.
    ///
    /// Phase comes from <see cref="ExperienceClock"/> rather than Time.time, so both headsets are
    /// on the same revolution. Two people standing over one plate and seeing its teeth at
    /// different angles is worse than having no plate at all -- it is the one object in the room
    /// that both of them are looking straight down at.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public class MusicBoxFloor : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f), Tooltip("0 is still and invisible, 1 is the full plate. " +
                                                "The touch brings this up.")]
        float m_Spin;

        [SerializeField] Color m_Color = new(0.55f, 0.60f, 0.78f, 1f);
        [SerializeField, Range(0f, 4f)] float m_Intensity = 1f;

        [SerializeField, Range(0f, 20f), Tooltip("Turns per minute. Slow: this is a music box " +
                                                 "winding down, and a fast plate in the lower " +
                                                 "field is what makes people feel it.")]
        float m_TurnsPerMinute = 3.2f;

        static readonly int k_Spin = Shader.PropertyToID("_Spin");
        static readonly int k_Phase = Shader.PropertyToID("_Phase");
        static readonly int k_Color = Shader.PropertyToID("_Color");
        static readonly int k_Intensity = Shader.PropertyToID("_Intensity");
        static readonly int k_Speed = Shader.PropertyToID("_Speed");

        MeshRenderer m_Renderer;
        MaterialPropertyBlock m_Block;
        float m_From, m_To, m_Start, m_Seconds;

        /// <summary>0 is still and dark, 1 is the full plate.</summary>
        public float spin
        {
            get => m_Spin;
            set { m_Spin = Mathf.Clamp01(value); m_Seconds = 0f; Apply(); }
        }

        /// <summary>Wind the plate up, or let it stop, over <paramref name="seconds"/>.</summary>
        public void SpinTo(float target, float seconds)
        {
            m_From = m_Spin;
            m_To = Mathf.Clamp01(target);
            m_Start = ExperienceClock.now;
            m_Seconds = Mathf.Max(0.01f, seconds);
        }

        /// <summary>Push the current values now. For editor tools with no frame loop.</summary>
        public void Rebuild() => Apply();

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        void LateUpdate()
        {
            if (m_Seconds > 0f)
            {
                float t = Mathf.Clamp01((ExperienceClock.now - m_Start) / m_Seconds);
                m_Spin = Mathf.Lerp(m_From, m_To, t * t * (3f - 2f * t));
                if (t >= 1f) m_Seconds = 0f;
            }
            Apply();
        }

        void Apply()
        {
            if (m_Renderer == null) m_Renderer = GetComponent<MeshRenderer>();
            if (m_Renderer == null) return;
            m_Block ??= new MaterialPropertyBlock();

            m_Block.SetFloat(k_Spin, m_Spin);
            m_Block.SetFloat(k_Phase, ExperienceClock.now);
            m_Block.SetColor(k_Color, m_Color);
            m_Block.SetFloat(k_Intensity, m_Intensity);
            m_Block.SetFloat(k_Speed, m_TurnsPerMinute);
            m_Renderer.SetPropertyBlock(m_Block);

            bool visible = m_Spin > 0.002f;
            if (m_Renderer.enabled != visible) m_Renderer.enabled = visible;
        }
    }
}
