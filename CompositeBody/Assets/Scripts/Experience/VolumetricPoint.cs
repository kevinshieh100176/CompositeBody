using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// The air around a point light, drawn by CompositeBody/VolumetricPoint.
    ///
    /// Sized from a real <see cref="Light"/> where one is given, so the glow and the lighting
    /// cannot drift apart: the proxy is scaled to the light's range and takes its colour, and
    /// moving the light moves both. The same arrangement VolumetricSpot has with its lamp.
    ///
    /// Written through a MaterialPropertyBlock, so two orbits can share one material and still
    /// be different colours. That breaks SRP batching for these renderers, which for two of them
    /// is two draw calls -- the same trade the rest of this project's volumetrics make.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public class VolumetricPoint : MonoBehaviour
    {
        [SerializeField, Tooltip("Take range and colour from this light. Optional; without one " +
                                 "the values below are used as authored.")]
        Light m_MatchLight;

        [SerializeField, Min(0.05f), Tooltip("How far the glow reaches, in metres. Overwritten " +
                                             "by the light's range when one is matched.")]
        float m_Range = 3f;

        [SerializeField] Color m_Color = new(0.62f, 0.22f, 1f);

        [SerializeField, Tooltip("Use the player's 代表色 instead of the colour above.")]
        bool m_UseRoleColor;

        [SerializeField] PlayerRole m_Role = PlayerRole.Player1;

        [SerializeField, Range(0f, 8f)] float m_Intensity = 1.6f;
        [SerializeField, Range(0f, 4f)] float m_Density = 1f;

        [SerializeField, Range(0f, 1f), Tooltip("0 hides it entirely. Driven by a beat.")]
        float m_Reveal = 1f;

        [SerializeField, Tooltip("Shade the air BETWEEN the eye and nearer geometry. Needs the " +
                                 "depth texture, which URP-Performant has on.")]
        bool m_SceneDepthOcclusion = true;

        static readonly int k_Color = Shader.PropertyToID("_Color");
        static readonly int k_Intensity = Shader.PropertyToID("_Intensity");
        static readonly int k_Density = Shader.PropertyToID("_Density");
        static readonly int k_Reveal = Shader.PropertyToID("_Reveal");

        MeshRenderer m_Renderer;
        MaterialPropertyBlock m_Block;

        /// <summary>The colour actually drawn, role or authored.</summary>
        public Color effectiveColor => m_UseRoleColor ? PlayerRoleColors.For(m_Role) : m_Color;

        /// <summary>0 hides the glow, 1 shows it. For a beat to drive.</summary>
        public float reveal
        {
            get => m_Reveal;
            set { m_Reveal = Mathf.Clamp01(value); Apply(); }
        }

        /// <summary>
        /// Take a player's 代表色. For a glow that stands in for someone whose role is only known
        /// once the session has assigned it, which is after this component is authored.
        /// </summary>
        public void SetRole(PlayerRole role)
        {
            m_UseRoleColor = true;
            m_Role = role;
            Apply();
        }

        /// <summary>How far the glow reaches, in metres. Resizes the proxy to match.</summary>
        public float range
        {
            get => m_Range;
            set { m_MatchLight = null; m_Range = Mathf.Max(value, 0.05f); Resize(); Apply(); }
        }

        void OnEnable() { Resolve(); Resize(); Apply(); }
        void OnValidate() { Resolve(); Resize(); Apply(); }

        void LateUpdate()
        {
            // Cheap enough to do every frame, and it has to be: these orbit, and the light's
            // range or colour can be animated by a beat at any time.
            Resize();
            Apply();
        }

        void Resolve()
        {
            m_Block ??= new MaterialPropertyBlock();
            if (m_Renderer == null) m_Renderer = GetComponent<MeshRenderer>();
            ApplyOcclusionMode();
        }

        /// <summary>
        /// Keyword and ZTest move together, because either alone is wrong -- the same pairing
        /// VolumetricSpot documents. Without the clamp, LEqual occludes the glow for free; with
        /// it, the test must be Always, because shading the air in front of nearer geometry is
        /// the whole point and LEqual has already discarded those fragments.
        ///
        /// sharedMaterial, never Renderer.material: reading .material swaps in a runtime clone,
        /// and a clone cannot be serialised -- anything that then saves the object writes a null
        /// material and the proxy returns as a magenta ball. That has happened in this project.
        /// </summary>
        void ApplyOcclusionMode()
        {
            if (m_Renderer == null) return;
            Material material = m_Renderer.sharedMaterial;
            if (material == null)
            {
                Debug.LogWarning($"[VolumetricPoint] '{name}' has no material, so it will draw " +
                                 "as a magenta ball. Assign OrbitGlow.", this);
                return;
            }

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

        /// <summary>
        /// The proxy's scale IS the range: the shader reads the radius off the transform rather
        /// than from a property, so there is no second number that can fall out of step with the
        /// first. A unit sphere is radius 0.5, hence the doubling.
        /// </summary>
        void Resize()
        {
            if (m_MatchLight != null) m_Range = m_MatchLight.range;

            float diameter = Mathf.Max(m_Range, 0.05f) * 2f;
            if ((transform.localScale - Vector3.one * diameter).sqrMagnitude > 1e-8f)
                transform.localScale = Vector3.one * diameter;
        }

        void Apply()
        {
            if (m_Renderer == null) return;
            m_Block ??= new MaterialPropertyBlock();

            Color colour = m_MatchLight != null && !m_UseRoleColor ? m_MatchLight.color : effectiveColor;

            m_Renderer.GetPropertyBlock(m_Block);
            m_Block.SetColor(k_Color, colour);
            m_Block.SetFloat(k_Intensity, m_Intensity);
            m_Block.SetFloat(k_Density, m_Density);
            m_Block.SetFloat(k_Reveal, m_Reveal);
            m_Renderer.SetPropertyBlock(m_Block);

            // Nothing to rasterise at zero, and this is a full sphere of overdraw.
            bool visible = m_Reveal > 0.002f;
            if (m_Renderer.forceRenderingOff == visible) m_Renderer.forceRenderingOff = !visible;
        }
    }
}
