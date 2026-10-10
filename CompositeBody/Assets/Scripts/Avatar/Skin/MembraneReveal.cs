using UnityEngine;

namespace CompositeBody.Avatar.Skin
{
    /// <summary>
    /// Drives a membrane film's formation: fading it in and out, or growing it outward from a
    /// point so the sheet creeps across whatever it wraps.
    ///
    /// Both are the one <c>_Reveal</c> parameter on CompositeBody/VacuumMembraneAnimated. Which
    /// of the two you get is decided by <see cref="m_GrowRadius"/>: at zero the whole sheet fades
    /// together, and above zero it forms in order of distance from <see cref="m_GrowFrom"/>. They
    /// are not two modes with two code paths -- a plain fade is just a growth with no spatial
    /// order to it.
    ///
    /// Written through a MaterialPropertyBlock rather than onto the material. The film material
    /// is a shared asset used by more than one scene, and driving it directly would write the
    /// current frame's reveal into the project every time this ran in the editor. The cost is
    /// that this renderer drops out of the SRP Batcher, which for one film is a draw call.
    ///
    /// At zero the renderer is switched off outright. A fully transparent film still rasterises,
    /// and this one is a couple of hundred thousand double-sided triangles, so a room sitting at
    /// reveal 0 waiting for its cue would otherwise cost the same as one in full view.
    /// </summary>
    [ExecuteAlways]
    public class MembraneReveal : MonoBehaviour
    {
        [SerializeField, Tooltip("The film to drive. Found on this object if left empty.")]
        Renderer m_Film;

        [SerializeField, Range(0f, 1f), Tooltip("0 is unformed, 1 is the finished sheet.")]
        float m_Reveal = 1f;

        [Header("Growth")]
        [SerializeField, Tooltip("Where the sheet starts forming. Leave empty on a skinned film and use the point below instead.")]
        Transform m_GrowFrom;

        [SerializeField, Tooltip("Growth origin in the film's own mesh space, used when no transform is set. This is what a skinned film wants.")]
        Vector3 m_GrowFromLocal;

        [SerializeField, Tooltip("Metres from the origin to the last part to form. Zero fades the whole sheet at once.")]
        float m_GrowRadius;

        [SerializeField, Tooltip("Measure the radius off the film's bounds instead, so it always covers the whole sheet.")]
        bool m_AutoRadius = true;

        [Header("Timing")]
        [SerializeField] float m_FadeInSeconds = 6f;
        [SerializeField] float m_FadeOutSeconds = 4f;

        [SerializeField, Tooltip("Shapes the ramp. Flat ends stop the front jerking into motion and stopping dead.")]
        AnimationCurve m_Shape = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField, Tooltip("Start unformed and grow in as soon as this is enabled.")]
        bool m_GrowOnEnable;

        static readonly int k_Reveal = Shader.PropertyToID("_Reveal");
        static readonly int k_GrowOrigin = Shader.PropertyToID("_GrowOrigin");
        static readonly int k_GrowRadius = Shader.PropertyToID("_GrowRadius");

        MaterialPropertyBlock m_Block;
        float m_From, m_To, m_Elapsed, m_Duration;
        bool m_Running;

        /// <summary>Where the formation currently stands, 0 unformed to 1 whole.</summary>
        public float reveal => m_Reveal;

        /// <summary>True while a fade or growth is still playing.</summary>
        public bool running => m_Running;

        void OnEnable()
        {
            if (m_Film == null) m_Film = GetComponent<Renderer>();

            if (m_GrowOnEnable && Application.isPlaying)
            {
                SetRevealImmediate(0f);
                GrowIn();
            }
            else
            {
                Apply();
            }
        }

        void OnValidate()
        {
            if (m_Film == null) m_Film = GetComponent<Renderer>();
            m_FadeInSeconds = Mathf.Max(0f, m_FadeInSeconds);
            m_FadeOutSeconds = Mathf.Max(0f, m_FadeOutSeconds);
            m_GrowRadius = Mathf.Max(0f, m_GrowRadius);
            Apply();
        }

        void Update()
        {
            if (!m_Running) return;

            m_Elapsed += Application.isPlaying ? Time.deltaTime : 0f;

            if (m_Duration <= 0f || m_Elapsed >= m_Duration)
            {
                m_Reveal = m_To;
                m_Running = false;
            }
            else
            {
                float k = m_Shape.Evaluate(m_Elapsed / m_Duration);
                m_Reveal = Mathf.Lerp(m_From, m_To, k);
            }

            Apply();
        }

        /// <summary>Grows the sheet in over the configured fade-in time.</summary>
        public void GrowIn() => RampTo(1f, m_FadeInSeconds);

        /// <summary>Takes the sheet back off over the configured fade-out time.</summary>
        public void FadeOut() => RampTo(0f, m_FadeOutSeconds);

        public void GrowIn(float seconds) => RampTo(1f, seconds);

        public void FadeOut(float seconds) => RampTo(0f, seconds);

        /// <summary>Ramps to any point in the formation, for a beat that only half forms the room.</summary>
        public void RampTo(float target, float seconds)
        {
            m_From = m_Reveal;
            m_To = Mathf.Clamp01(target);
            m_Duration = Mathf.Max(0f, seconds);
            m_Elapsed = 0f;
            m_Running = true;

            // A zero-length ramp still has to land this frame rather than next, or a beat that
            // snaps the room on would show one frame of the old state.
            if (m_Duration <= 0f)
            {
                m_Reveal = m_To;
                m_Running = false;
                Apply();
            }
        }

        /// <summary>Jumps straight there and cancels anything in flight.</summary>
        public void SetRevealImmediate(float value)
        {
            m_Running = false;
            m_Reveal = Mathf.Clamp01(value);
            Apply();
        }

        void Apply()
        {
            if (m_Film == null) return;

            // Nothing formed is nothing to draw, and this is a lot of triangles to rasterise
            // only to multiply them all by zero.
            //
            // Through forceRenderingOff rather than through enabled, because enabled is not ours
            // to write: every film carries a MembraneFilmLink that mirrors its source renderer's
            // enabled flag in LateUpdate, so a reveal that switched the renderer off would be
            // switched back on a few milliseconds later, every frame, forever. forceRenderingOff
            // is a separate flag that nothing else in the project touches.
            bool hidden = m_Reveal <= 0.0005f;
            if (m_Film.forceRenderingOff != hidden) m_Film.forceRenderingOff = hidden;
            if (hidden) return;

            m_Block ??= new MaterialPropertyBlock();
            m_Film.GetPropertyBlock(m_Block);

            m_Block.SetFloat(k_Reveal, m_Reveal);
            m_Block.SetVector(k_GrowOrigin, GrowOriginLocal());
            m_Block.SetFloat(k_GrowRadius, ResolveRadius());

            m_Film.SetPropertyBlock(m_Block);
        }

        /// <summary>
        /// The growth origin in the film's own mesh space, which is the space the shader's rest
        /// positions are in.
        ///
        /// A Transform is the right answer for a rigid film -- a room, a prop -- where the origin
        /// can be a door or a lamp and can be moved between cues without re-authoring anything.
        ///
        /// It is the wrong answer for a skinned one. A skinned film's rest positions are its bind
        /// pose, which does not move, while a Transform in the scene does; feeding one into the
        /// other means the growth origin wanders through the body every time the body moves, and
        /// the front crawls around on its own. So a skinned film sets the point directly in mesh
        /// space instead, and it stays put on the body wherever the body goes.
        /// </summary>
        Vector4 GrowOriginLocal()
        {
            Vector3 local = m_GrowFrom != null
                ? m_Film.transform.InverseTransformPoint(m_GrowFrom.position)
                : m_GrowFromLocal;

            return new Vector4(local.x, local.y, local.z, 0f);
        }

        /// <summary>
        /// Far enough that the last corner of the sheet still finishes forming. Measured to the
        /// furthest bounds corner rather than to the bounds centre: half the sheet would never
        /// reach reveal 1, and a fade-in that leaves the far wall permanently half formed looks
        /// like a bug rather than a choice.
        /// </summary>
        float ResolveRadius()
        {
            if (!m_AutoRadius) return m_GrowRadius;

            Mesh mesh = FilmMesh();
            if (mesh == null) return m_GrowRadius;

            Bounds b = mesh.bounds;
            Vector3 origin = GrowOriginLocal();

            Vector3 far = new Vector3(
                Mathf.Max(Mathf.Abs(b.min.x - origin.x), Mathf.Abs(b.max.x - origin.x)),
                Mathf.Max(Mathf.Abs(b.min.y - origin.y), Mathf.Abs(b.max.y - origin.y)),
                Mathf.Max(Mathf.Abs(b.min.z - origin.z), Mathf.Abs(b.max.z - origin.z)));

            return far.magnitude;
        }

        /// <summary>
        /// The film's mesh, from either kind of renderer. A prop or a room film hangs off a
        /// MeshFilter; the avatar's hangs off the SkinnedMeshRenderer itself, which has no
        /// MeshFilter at all -- so looking only for one leaves every skinned film silently
        /// falling back to a radius of zero, which is a fade rather than the growth that was
        /// asked for.
        /// </summary>
        Mesh FilmMesh()
        {
            if (m_Film is SkinnedMeshRenderer skinned) return skinned.sharedMesh;

            var filter = m_Film.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
