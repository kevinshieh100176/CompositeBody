using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// One 光圈: the column of light O-0 wraps around 真人 A and B, in that player's colour.
    ///
    /// The shader does all the work analytically, so this only has to move a handful of floats
    /// and keep the proxy mesh the right size. Phase comes from <see cref="ExperienceClock"/>,
    /// like <see cref="DistantSoundBed"/> and the point clouds, so the drift is identical on
    /// both headsets -- two players standing either side of the same column would otherwise
    /// watch it breathe out of step.
    ///
    /// Sizing is in metres here and converted to the proxy's scale, because the shader fixes
    /// the volume at radius 0.5 and y from -1 to 1 to match Unity's Cylinder primitive. Nobody
    /// should have to know that to place a light.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public class VolumetricColumn : MonoBehaviour
    {
        [Header("Size")]
        [SerializeField, Min(0.05f), Tooltip("Radius of the column in metres.")]
        float m_Radius = 0.75f;

        [SerializeField, Min(0.1f), Tooltip("Height in metres. The column stands on this object's origin.")]
        float m_Height = 2.6f;

        [Header("Colour")]
        [SerializeField, Tooltip("Take the colour from a player role instead of the field below.")]
        bool m_UseRoleColor;

        [SerializeField, Tooltip("Which role's colour, when the toggle above is on. A 真人's 光圈 is the colour of the player who faces them.")]
        PlayerRole m_Role = PlayerRole.Player1;

        [SerializeField]
        Color m_Color = new(0.62f, 0.22f, 1f, 1f);

        [SerializeField, Range(0f, 8f)]
        float m_Intensity = 1.6f;

        [SerializeField, Range(0f, 4f)]
        float m_Density = 1f;

        [Header("Occlusion")]
        [SerializeField, Tooltip("Integrate against scene depth instead of letting the depth test do it. Fixes the light vanishing between the eye and the floor when you stand in the column -- but needs the camera or the pipeline asset to produce a depth texture, which costs a prepass.")]
        bool m_SceneDepthOcclusion;

        [Header("State")]
        [SerializeField, Range(0f, 1f), Tooltip("0 is off, 1 is full. Fade it with FadeTo for a cue.")]
        float m_Reveal = 1f;

        static readonly int k_Color = Shader.PropertyToID("_Color");
        static readonly int k_Intensity = Shader.PropertyToID("_Intensity");
        static readonly int k_Density = Shader.PropertyToID("_Density");
        static readonly int k_Phase = Shader.PropertyToID("_Phase");
        static readonly int k_Reveal = Shader.PropertyToID("_Reveal");

        MeshRenderer m_Renderer;
        MaterialPropertyBlock m_Block;

        float m_FadeFrom, m_FadeTo, m_FadeStart, m_FadeSeconds;

        public float reveal
        {
            get => m_Reveal;
            set { m_Reveal = Mathf.Clamp01(value); m_FadeSeconds = 0f; Apply(); }
        }

        /// <summary>Metres. Changing this rescales the proxy, so it is safe to animate.</summary>
        public float radius
        {
            get => m_Radius;
            set { m_Radius = Mathf.Max(0.05f, value); Resize(); }
        }

        void OnEnable()
        {
            Resolve();
            Resize();
            Apply();
        }

        void OnValidate()
        {
            Resolve();
            Resize();
            if (isActiveAndEnabled) Apply();
        }

        void Resolve()
        {
            m_Block ??= new MaterialPropertyBlock();
            if (m_Renderer == null) m_Renderer = GetComponent<MeshRenderer>();
            ApplyOcclusionMode();
        }

        /// <summary>
        /// Keyword and ZTest move together, because either on its own is wrong.
        ///
        /// Without the depth clamp, ZTest LEqual is what occludes the column, and it does it
        /// for free. With the clamp, the test has to be Always -- the whole point is to shade
        /// the air in front of nearer geometry, which LEqual has already thrown away by the
        /// time the fragment runs.
        ///
        /// Set on the shared material rather than a property block: keywords are not per
        /// renderer, and ZTest is pipeline state the block cannot reach.
        /// </summary>
        void ApplyOcclusionMode()
        {
            if (m_Renderer == null) return;
            Material material = Application.isPlaying
                ? m_Renderer.material
                : m_Renderer.sharedMaterial;
            if (material == null) return;

            const string k_Keyword = "_SCENE_DEPTH_CLAMP";
            if (m_SceneDepthOcclusion)
            {
                material.EnableKeyword(k_Keyword);
                material.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            }
            else
            {
                material.DisableKeyword(k_Keyword);
                material.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            }
        }

        /// <summary>Whether the column integrates against scene depth.</summary>
        public bool sceneDepthOcclusion
        {
            get => m_SceneDepthOcclusion;
            set { m_SceneDepthOcclusion = value; ApplyOcclusionMode(); }
        }

        /// <summary>
        /// The proxy is scaled, not rebuilt. The shader reads a fixed object-space volume, so
        /// the only thing that has to match the authored metres is this transform.
        /// </summary>
        void Resize()
        {
            // The proxy is a unit cube, so the scale is just the column's real size. The shader
            // solves the cylinder from the ray, so the proxy only has to generate fragments --
            // and it has to be a cube, because a cylinder's end caps cull from the inside and
            // tear wedges out of the light the moment the camera steps into it.
            // Divided by 0.9 because the shader insets the volume to 45% of the proxy. The
            // authored metres describe the light, not the box around it.
            const float k_Inset = 0.9f;
            transform.localScale = new Vector3(m_Radius * 2f / k_Inset,
                                               m_Height / k_Inset,
                                               m_Radius * 2f / k_Inset);

            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name != "Cube")
            {
                Debug.LogWarning($"[VolumetricColumn] '{name}' has a {filter.sharedMesh.name} " +
                                 "proxy; the shader expects a Cube. A cylinder leaves holes in " +
                                 "the light when seen from inside.", this);
            }
        }

        void LateUpdate()
        {
            if (m_FadeSeconds > 0f)
            {
                float t = Mathf.Clamp01((ExperienceClock.now - m_FadeStart) / m_FadeSeconds);
                m_Reveal = Mathf.Lerp(m_FadeFrom, m_FadeTo, t * t * (3f - 2f * t));
                if (t >= 1f) m_FadeSeconds = 0f;
            }
            Apply();
        }

        /// <summary>Bring the column up or take it down over <paramref name="seconds"/>.</summary>
        public void FadeTo(float target, float seconds)
        {
            m_FadeFrom = m_Reveal;
            m_FadeTo = Mathf.Clamp01(target);
            m_FadeStart = ExperienceClock.now;
            m_FadeSeconds = Mathf.Max(0.01f, seconds);
        }

        void Apply()
        {
            if (m_Renderer == null) return;
            m_Block ??= new MaterialPropertyBlock();

            Color colour = m_UseRoleColor ? PlayerRoleColors.For(m_Role) : m_Color;
            m_Block.SetColor(k_Color, colour);
            m_Block.SetFloat(k_Intensity, m_Intensity);
            m_Block.SetFloat(k_Density, m_Density);
            m_Block.SetFloat(k_Phase, ExperienceClock.now);
            m_Block.SetFloat(k_Reveal, m_Reveal);
            m_Renderer.SetPropertyBlock(m_Block);

            // A column at zero still rasterises every pixel of its proxy before discarding, and
            // these are large on screen. Switching the renderer off is the difference between a
            // faded cue and a free one.
            bool visible = m_Reveal > 0.002f;
            if (m_Renderer.enabled != visible) m_Renderer.enabled = visible;
        }
    }
}
