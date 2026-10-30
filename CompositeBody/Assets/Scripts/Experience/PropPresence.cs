using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CompositeBody.Experience
{
    /// <summary>
    /// How present a group of props is, 0 to 1, and nothing else. The coat, the mug, the two
    /// cartons and the wall of boxes behind them all arrive and leave through one of these.
    ///
    /// THE NUMBER IS AUTHORED ON A TIMELINE. <see cref="m_Presence"/> is a serialized field so an
    /// AnimationTrack can write it, which makes "when do the boxes come in" a clip you drag
    /// rather than a duration compiled into a component. Everything else here is how that one
    /// number reaches a renderer.
    ///
    /// TWO WAYS TO BE HALF THERE, because the room has two kinds of material in it and only one
    /// of them can be faded:
    ///
    /// <see cref="Mode.Fade"/> is alpha on the base colour, for the transparent props -- the coat
    /// and the mug, built that way by <c>BuildO1Grab</c>. Alpha rather than the distance dither
    /// the body uses: the dither hides whatever is NEARER than its threshold, which is right for
    /// a body you stand inside and wrong for something you pick up and bring to your face.
    ///
    /// <see cref="Mode.Clip"/> is the alpha-clip threshold, for the cardboard, which is opaque on
    /// purpose -- a transparent carton sorted against its own flaps and let the 光圈 shine
    /// through its near wall, and URP re-derives a transparent material's depth settings on every
    /// import so that cannot be patched out. Clipping keeps it solid and still lets it go: the
    /// base map carries noise in its alpha, so raising the cutoff eats the box away in patches
    /// instead of switching it off. That is the dissolve.
    ///
    /// NOTHING HERE IS NETWORKED. Each player has their own props and their own copy of this; the
    /// timeline that drives it is already on the shared clock, so both machines run the same
    /// curve without anything crossing the wire.
    /// </summary>
    [DisallowMultipleComponent]
    public class PropPresence : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>Alpha on the base colour. Transparent materials only.</summary>
            Fade,

            /// <summary>Alpha-clip cutoff. Opaque materials whose base map has noise in its alpha.</summary>
            Clip,
        }

        [SerializeField, Range(0f, 1f), Tooltip("0 absent, 1 fully here. DRIVEN BY THE TIMELINE " +
                                                "-- drag it to see any moment of the arrival " +
                                                "without pressing Play.")]
        float m_Presence;

        [SerializeField, Tooltip("How the presence reaches the material. Fade for the " +
                                 "transparent props, Clip for the cardboard.")]
        Mode m_Mode = Mode.Fade;

        [SerializeField, Tooltip("What to drive. Everything under this object if left empty.")]
        Renderer[] m_Renderers = new Renderer[0];

        [SerializeField, Tooltip("Grabbing is switched on only once the object is substantially " +
                                 "there. Reaching for something still half transparent gets a " +
                                 "grab that feels like a bug. Found under this object if empty.")]
        XRGrabInteractable[] m_Grabs = new XRGrabInteractable[0];

        [SerializeField, Range(0f, 1f), Tooltip("Presence above which the props can be grabbed.")]
        float m_GrabbableAt = 0.6f;

        [SerializeField, Tooltip("Colour property whose alpha carries the presence, in Fade mode.")]
        string m_ColorProperty = "_BaseColor";

        [SerializeField, Tooltip("Cutoff property, in Clip mode. URP's Lit calls it _Cutoff and " +
                                 "uses it in the shadow pass too, so a box that is half gone " +
                                 "casts half a shadow.")]
        string m_CutoffProperty = "_Cutoff";

        int m_ColorId, m_CutoffId;
        MaterialPropertyBlock m_Block;
        float m_Applied = -1f;

        /// <summary>Where the arrival has got to, 0 to 1.</summary>
        public float presence
        {
            get => m_Presence;
            set { m_Presence = Mathf.Clamp01(value); Apply(); }
        }

        /// <summary>Whether the props are fully here.</summary>
        public bool present => m_Presence >= 0.999f;

        void Awake()
        {
            Resolve();
            Apply();
        }

        /// <summary>
        /// Put the group at a given presence, now, without a timeline. For the still renderer and
        /// for scrubbing outside play mode, where Awake has not run.
        /// </summary>
        public void SetPresenceForEditor(float value)
        {
            Resolve();
            m_Presence = Mathf.Clamp01(value);
            m_Applied = -1f;
            Apply();
        }

        void Resolve()
        {
            if (m_ColorId == 0) m_ColorId = Shader.PropertyToID(m_ColorProperty);
            if (m_CutoffId == 0) m_CutoffId = Shader.PropertyToID(m_CutoffProperty);
            m_Block ??= new MaterialPropertyBlock();

            if (m_Renderers == null || m_Renderers.Length == 0)
                m_Renderers = GetComponentsInChildren<Renderer>(true);

            if (m_Grabs == null || m_Grabs.Length == 0)
                m_Grabs = GetComponentsInChildren<XRGrabInteractable>(true);
        }

        // LateUpdate, so the timeline has already written the field this frame. The early-out on
        // an unchanged value is what keeps this off the profiler while the props simply sit
        // there, which is most of the beat.
        void LateUpdate() => Apply();

        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            Resolve();
            m_Applied = -1f;
            Apply();
        }

        void Apply()
        {
            if (Mathf.Approximately(m_Applied, m_Presence)) return;
            m_Applied = m_Presence;

            if (m_Renderers == null) return;
            m_Block ??= new MaterialPropertyBlock();

            // Nothing to draw at zero, and these are not cheap meshes. It is also the one thing
            // that hides a prop whose material turns out not to carry the property being written
            // -- without it, a mis-set colour property would leave the coat hanging in the room
            // through the whole of O-0.
            bool hidden = m_Presence <= 0.002f;

            foreach (Renderer r in m_Renderers)
            {
                if (r == null) continue;

                if (!hidden)
                {
                    r.GetPropertyBlock(m_Block);

                    if (m_Mode == Mode.Fade)
                    {
                        Color colour = r.sharedMaterial != null && r.sharedMaterial.HasProperty(m_ColorId)
                            ? r.sharedMaterial.GetColor(m_ColorId)
                            : Color.white;
                        colour.a = m_Presence;
                        m_Block.SetColor(m_ColorId, colour);
                    }
                    else
                    {
                        // Past 1, not at it: clip() discards what is strictly below zero, so a
                        // cutoff of exactly 1 against an alpha of exactly 1 keeps the texel.
                        m_Block.SetFloat(m_CutoffId, Mathf.Lerp(1.001f, 0f, m_Presence));
                    }

                    r.SetPropertyBlock(m_Block);
                }

                if (r.forceRenderingOff != hidden) r.forceRenderingOff = hidden;
            }

            if (m_Grabs == null) return;
            bool grabbable = m_Presence >= m_GrabbableAt;
            foreach (XRGrabInteractable grab in m_Grabs)
            {
                if (grab != null && grab.enabled != grabbable) grab.enabled = grabbable;
            }
        }
    }
}
