using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// A flat card -- a title, a line of text -- that fades in and out. One float, so Timeline can
    /// animate it.
    ///
    /// The alpha goes through a MaterialPropertyBlock rather than onto the material, which is the
    /// convention the rest of the piece follows (see <see cref="VolumetricSpot"/> and the O-0
    /// slab). The alternative Timeline offers -- animating "material._BaseColor.a" directly on
    /// the renderer -- instantiates a copy of the material the first time it runs, so the asset
    /// in the project and the thing on screen stop being the same object. That is a bad trade for
    /// a piece where the same card material may later be shared across several beats.
    ///
    /// Additive, not alpha blended, and that is deliberate for text on black. An alpha-blended
    /// quad carries its own rectangle: wherever the glyphs are not, it writes black over black,
    /// which is invisible until something is behind it and then it is a floating box. Additive
    /// adds light where the glyphs are and nothing anywhere else, so the card has no edges at
    /// all. It also sidesteps transparent sort order against the beams and the point clouds.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public class CardFade : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f), Tooltip("0 is gone, 1 is full. Driven by the Timeline.")]
        float m_Alpha;

        [SerializeField, Tooltip("Multiplied into the texture. White leaves the artwork alone.")]
        Color m_Tint = Color.white;

        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

        MeshRenderer m_Renderer;
        MaterialPropertyBlock m_Block;

        /// <summary>0 is gone, 1 is full.</summary>
        public float alpha
        {
            get => m_Alpha;
            set { m_Alpha = Mathf.Clamp01(value); Apply(); }
        }

        void OnEnable() => Apply();
        void OnValidate() => Apply();
        void LateUpdate() => Apply();

        /// <summary>
        /// Push the current alpha to the renderer now.
        ///
        /// Timeline animates the serialized field directly, which bypasses the property setter --
        /// so the value is correct and nothing has sent it to the material until the next
        /// LateUpdate. At runtime that is one frame and invisible. In an editor batch render
        /// there is no next LateUpdate at all, and the card stays at whatever the material says.
        /// </summary>
        public void Rebuild() => Apply();

        void Apply()
        {
            if (m_Renderer == null) m_Renderer = GetComponent<MeshRenderer>();
            if (m_Renderer == null) return;
            m_Block ??= new MaterialPropertyBlock();

            var c = m_Tint;
            c.a = m_Alpha;
            m_Block.SetColor(k_BaseColor, c);
            m_Renderer.SetPropertyBlock(m_Block);

            // A card at zero still rasterises its whole quad before adding nothing. These are
            // large on screen and sit in front of the player for most of a minute.
            bool visible = m_Alpha > 0.002f;
            if (m_Renderer.enabled != visible) m_Renderer.enabled = visible;
        }
    }
}
