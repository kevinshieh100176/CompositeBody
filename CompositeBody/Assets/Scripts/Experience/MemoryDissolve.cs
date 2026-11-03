using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// How much of a group of renderers has blown away, 0 (all there) to 1 (gone).
    ///
    /// THE NUMBER IS AUTHORED OR DRIVEN, NEVER TIMED IN HERE. <see cref="m_Progress"/> is a
    /// serialized field, so an AnimationTrack can write it and so it can be dragged in the
    /// inspector to see any moment of the dissolve without pressing Play -- the same arrangement
    /// <see cref="PropPresence"/> and <see cref="ScanHandover"/> use, and for the same reason:
    /// "how long does the wall take to go" is a clip somebody drags, not a duration compiled
    /// into a component.
    ///
    /// ONE PROPERTY, AND THE MATERIAL IS SOMEBODY ELSE'S JOB. The renderers are expected to
    /// already carry a material whose shader has <c>_Progress</c> -- CompositeBody/MemoryFragments
    /// or CompositeBody/MemoryShards. Swapping a Lit material out for a dissolving one as the
    /// dissolve starts and back again as it ends is the obvious alternative and it is wrong: the
    /// swap pops on one frame, and for a dissolve-IN that frame is the last one, which is the
    /// frame everybody in the room is looking at. <c>BuildS2Dollhouse</c> assigns the materials
    /// once; this only moves the number.
    ///
    /// WRITTEN THROUGH A MaterialPropertyBlock, so ten renderers sharing one wall material
    /// dissolve together without instancing ten materials, and so nothing here dirties an asset
    /// while somebody scrubs the slider in the editor.
    ///
    /// NOTHING HERE IS NETWORKED. Every machine runs the same curve off the same replicated
    /// fact -- see <see cref="DollhouseAssembly"/>, which decides when -- so the dissolve itself
    /// never crosses the wire.
    /// </summary>
    [DisallowMultipleComponent]
    public class MemoryDissolve : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f), Tooltip("0 all there, 1 completely blown away. DRAG IT " +
                                                "-- the whole dissolve is visible in the scene " +
                                                "view without pressing Play.")]
        float m_Progress;

        [SerializeField, Tooltip("What to dissolve. Everything under this object if left empty.")]
        Renderer[] m_Renderers = new Renderer[0];

        [SerializeField, Tooltip("The shader property carrying the dissolve. MemoryFragments and " +
                                 "MemoryShards both call it _Progress.")]
        string m_ProgressProperty = "_Progress";

        [SerializeField, Range(0.9f, 1f), Tooltip("Past this the renderers are switched off " +
                                                  "outright. The last few per cent of a dissolve " +
                                                  "is a handful of dust nobody can see, and these " +
                                                  "are not cheap meshes to keep drawing for it.")]
        float m_HideAt = 0.995f;

        int m_ProgressId;
        MaterialPropertyBlock m_Block;
        float m_Applied = -1f;

        /// <summary>How far gone the group is, 0 to 1. Setting it applies immediately.</summary>
        public float progress
        {
            get => m_Progress;
            set { m_Progress = Mathf.Clamp01(value); Apply(); }
        }

        /// <summary>True once the group is entirely gone.</summary>
        public bool gone => m_Progress >= m_HideAt;

        /// <summary>True while the group is wholly there.</summary>
        public bool whole => m_Progress <= 0.0001f;

        void Awake()
        {
            Resolve();
            Apply();
        }

        /// <summary>
        /// Put the group at a given amount of dissolve, now, with no timeline and without Awake
        /// having run. For the scene builder and for the still renderer, the same reason
        /// <see cref="PropPresence.SetPresenceForEditor"/> exists.
        /// </summary>
        public void SetProgressForEditor(float value)
        {
            Resolve();
            m_Progress = Mathf.Clamp01(value);
            m_Applied = -1f;
            Apply();
        }

        void Resolve()
        {
            if (m_ProgressId == 0)
                m_ProgressId = Shader.PropertyToID(string.IsNullOrWhiteSpace(m_ProgressProperty)
                                                       ? "_Progress"
                                                       : m_ProgressProperty);
            m_Block ??= new MaterialPropertyBlock();

            if (m_Renderers == null || m_Renderers.Length == 0)
                m_Renderers = GetComponentsInChildren<Renderer>(true);
        }

        // LateUpdate, so a timeline has already written the field this frame. The early-out on an
        // unchanged value is what keeps this off the profiler while the wall simply stands there,
        // which is all of the show but about nine seconds of it.
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
            // Resolved here as well as in Awake, because the beat sets the group's progress from
            // its own Arm() -- which can land before this component has woken, and a property id of
            // 0 writes nothing and reports no error.
            if (m_ProgressId == 0 || m_Renderers == null || m_Renderers.Length == 0) Resolve();

            if (Mathf.Approximately(m_Applied, m_Progress)) return;
            m_Applied = m_Progress;

            if (m_Renderers == null) return;
            m_Block ??= new MaterialPropertyBlock();

            bool hidden = m_Progress >= m_HideAt;

            foreach (Renderer r in m_Renderers)
            {
                if (r == null) continue;

                if (!hidden)
                {
                    r.GetPropertyBlock(m_Block);
                    m_Block.SetFloat(m_ProgressId, m_Progress);
                    r.SetPropertyBlock(m_Block);
                }

                if (r.forceRenderingOff != hidden) r.forceRenderingOff = hidden;
            }
        }
    }
}
