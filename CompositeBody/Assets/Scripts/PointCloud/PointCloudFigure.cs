using System;
using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.PointClouds
{
    /// <summary>
    /// Drives a point-cloud figure: idle turbulence, forming, and coming apart.
    ///
    /// Everything here is stateless per point. The shader derives a point's shimmer, its turn
    /// to leave and where it drifts to from its own object-space position and a single phase
    /// value, so this component only has to move three floats. That is what makes the effect
    /// affordable at seven hundred thousand points, and it is also what makes it identical on
    /// both headsets: the phase comes from <see cref="ExperienceClock"/>, the shared clock
    /// <see cref="DistantSoundBed"/> already uses to keep its cues from diverging. Driving it
    /// from Time.time instead would animate the figure out of step on two machines that joined
    /// seconds apart, and the 真人 are something both players stand and look at together.
    ///
    /// Written through a MaterialPropertyBlock rather than onto the material, following
    /// <see cref="CompositeBody.Avatar.Skin.MembraneReveal"/>: the material is a shared asset,
    /// and driving it directly would write the current frame's dissolve into the project every
    /// time this ran in the editor. The cost is that the renderer drops out of the SRP Batcher,
    /// which for one figure is a draw call.
    /// </summary>
    [ExecuteAlways]
    public class PointCloudFigure : MonoBehaviour
    {
        [SerializeField, Tooltip("The cloud to drive. Found on this object or a child if left empty.")]
        Renderer m_Cloud;

        [Header("Idle")]
        [SerializeField, Tooltip("Shimmer amplitude in metres. A scan reads as alive at a millimetre or two; past about a centimetre it reads as wind.")]
        float m_Turbulence = 0.003f;

        [SerializeField, Range(0.1f, 30f), Tooltip("Spatial scale of the shimmer. High values make neighbouring points disagree, low values move whole limbs together.")]
        float m_TurbulenceScale = 6f;

        [SerializeField, Range(0f, 6f)]
        float m_TurbulenceSpeed = 0.9f;

        [Header("State")]
        [SerializeField, Range(0f, 1f), Tooltip("0 unformed, 1 whole.")]
        float m_Reveal = 1f;

        [SerializeField, Range(0f, 1f), Tooltip("0 intact, 1 entirely gone.")]
        float m_Dissolve;

        [Header("Reveal shape")]
        [SerializeField, Tooltip("Where forming starts, in the cloud's local space. Leave at zero to form from the feet.")]
        Vector3 m_RevealFrom = Vector3.zero;

        [SerializeField, Min(0f), Tooltip("Metres to the last part to form. 0 forms the whole cloud at once.")]
        float m_RevealRadius;

        [Header("Glitch")]
        [SerializeField, Range(0f, 1f), Tooltip("Fraction of horizontal bands corrupted at any moment. The script's 真人 return 「有膜／Glitch的感覺」; a little goes a long way.")]
        float m_Glitch;

        [SerializeField, Range(2f, 160f), Tooltip("Bands per metre of height. High values shred, low values cut the body into a few thick slabs.")]
        float m_GlitchSlabs = 48f;

        [SerializeField, Range(0f, 0.5f), Tooltip("How far a corrupted band slides sideways.")]
        float m_GlitchShift = 0.05f;

        [SerializeField, Range(0f, 1f), Tooltip("Fraction of corrupted bands that vanish rather than move.")]
        float m_GlitchDropout = 0.25f;

        [SerializeField, Range(0f, 1f), Tooltip("How far the occasional stray point flies out of its band.")]
        float m_GlitchScatter = 0.18f;

        [SerializeField, Range(0f, 30f), Tooltip("Held frames per second. 0 freezes the glitch on the seed below, which is the static misplaced version.")]
        float m_GlitchRate = 11f;

        [SerializeField, Range(0f, 64f), Tooltip("Which frozen frame, when the rate is 0. Step it to find a still you like.")]
        float m_GlitchSeed = 3f;

        [SerializeField, Range(0f, 1f), Tooltip("Red/cyan fringing along a tear.")]
        float m_GlitchChroma = 0.5f;

        [Header("Dissolve shape")]
        [SerializeField, Tooltip("Where coming apart starts, in the cloud's local space. Chest height is where the script puts it.")]
        Vector3 m_DissolveFrom = new(0f, 1.25f, 0f);

        [SerializeField, Min(0f), Tooltip("Metres to the last part to leave. 0 dissolves everywhere at once.")]
        float m_DissolveRadius = 1.8f;

        [SerializeField, Range(0f, 1f), Tooltip("0 is a clean front sweeping outward, 1 is an even fizz with no direction.")]
        float m_DissolveScatter = 0.35f;

        [SerializeField, Min(0f), Tooltip("How far a departed point travels before it is gone.")]
        float m_DissolveDrift = 0.6f;

        [SerializeField, Tooltip("Bias direction for the drift, in local space. Up is the default; point it at the other figure for S3-3's transfer.")]
        Vector3 m_DissolveDir = Vector3.up;

        [SerializeField, Min(0f), Tooltip("Extra wander as points leave, so they lose their place in the surface.")]
        float m_DissolveSpread = 0.08f;

        static readonly int k_Phase = Shader.PropertyToID("_Phase");
        static readonly int k_Turbulence = Shader.PropertyToID("_Turbulence");
        static readonly int k_TurbulenceScale = Shader.PropertyToID("_TurbulenceScale");
        static readonly int k_TurbulenceSpeed = Shader.PropertyToID("_TurbulenceSpeed");
        static readonly int k_Reveal = Shader.PropertyToID("_Reveal");
        static readonly int k_RevealFrom = Shader.PropertyToID("_RevealFrom");
        static readonly int k_RevealRadius = Shader.PropertyToID("_RevealRadius");
        static readonly int k_Glitch = Shader.PropertyToID("_Glitch");
        static readonly int k_GlitchSlabs = Shader.PropertyToID("_GlitchSlabs");
        static readonly int k_GlitchShift = Shader.PropertyToID("_GlitchShift");
        static readonly int k_GlitchDropout = Shader.PropertyToID("_GlitchDropout");
        static readonly int k_GlitchScatter = Shader.PropertyToID("_GlitchScatter");
        static readonly int k_GlitchRate = Shader.PropertyToID("_GlitchRate");
        static readonly int k_GlitchSeed = Shader.PropertyToID("_GlitchSeed");
        static readonly int k_GlitchChroma = Shader.PropertyToID("_GlitchChroma");
        static readonly int k_Dissolve = Shader.PropertyToID("_Dissolve");
        static readonly int k_Cascade = Shader.PropertyToID("_Cascade");
        static readonly int k_DissolveFrom = Shader.PropertyToID("_DissolveFrom");
        static readonly int k_DissolveRadius = Shader.PropertyToID("_DissolveRadius");
        static readonly int k_DissolveScatter = Shader.PropertyToID("_DissolveScatter");
        static readonly int k_DissolveDrift = Shader.PropertyToID("_DissolveDrift");
        static readonly int k_DissolveDir = Shader.PropertyToID("_DissolveDir");
        static readonly int k_DissolveSpread = Shader.PropertyToID("_DissolveSpread");

        MaterialPropertyBlock m_Block;

        // Running transitions. Null when nothing is animating, which is the common case.
        Transition m_RevealTween;
        Transition m_DissolveTween;

        struct Transition
        {
            public float from, to, start, duration;
            public Action onDone;

            public bool Evaluate(float now, out float value)
            {
                if (duration <= 0f) { value = to; return true; }
                float t = Mathf.Clamp01((now - start) / duration);
                // Smoothstep, so a dissolve eases in rather than lurching on the cue frame.
                value = Mathf.Lerp(from, to, t * t * (3f - 2f * t));
                return t >= 1f;
            }
        }

        // Burst level, laid over the authored glitch rather than replacing it, so a figure
        // that is already faintly corrupted does not drop to calm when a burst ends.
        float m_GlitchBurst;
        float m_BurstStart;
        float m_BurstDuration;
        float m_BurstFrom;

        /// <summary>Steady corruption level, before any burst.</summary>
        public float glitch
        {
            get => m_Glitch;
            set { m_Glitch = Mathf.Clamp01(value); Apply(); }
        }

        /// <summary>True while the figure is frozen on one glitch frame rather than stepping.</summary>
        public bool isFrozen => m_GlitchRate <= 0f;

        /// <summary>0 unformed, 1 whole.</summary>
        public float reveal
        {
            get => m_Reveal;
            set { m_Reveal = Mathf.Clamp01(value); m_RevealTween = default; Apply(); }
        }

        /// <summary>0 intact, 1 entirely gone.</summary>
        public float dissolve
        {
            get => m_Dissolve;
            set { m_Dissolve = Mathf.Clamp01(value); m_DissolveTween = default; Apply(); }
        }

        /// <summary>True once a dissolve started by <see cref="DissolveOver"/> has finished.</summary>
        public bool isGone => m_Dissolve >= 0.999f;

        void OnEnable()
        {
            Resolve();
            Apply();
        }

        void OnValidate()
        {
            Resolve();
            if (isActiveAndEnabled) Apply();
        }

        void Resolve()
        {
            m_Block ??= new MaterialPropertyBlock();
            if (m_Cloud == null) m_Cloud = GetComponent<Renderer>();
            if (m_Cloud == null) m_Cloud = GetComponentInChildren<Renderer>();
        }

        void Update()
        {
            float now = ExperienceClock.now;
            Advance(ref m_RevealTween, now, ref m_Reveal);
            Advance(ref m_DissolveTween, now, ref m_Dissolve);

            if (m_GlitchBurst > 0f)
            {
                // Linear decay back to the authored level. Not eased: a burst that tails off
                // smoothly reads as something settling, and a fault does not settle.
                float t = (now - m_BurstStart) / m_BurstDuration;
                m_GlitchBurst = t >= 1f ? 0f : m_BurstFrom * (1f - t);
            }

            Apply();
        }

        /// <summary>
        /// Steps one transition. The callback fires after the tween is cleared, so a handler
        /// that starts another transition on the same value is not immediately overwritten by
        /// this one finishing -- which is exactly what a dissolve that hands over to the next
        /// beat does.
        /// </summary>
        static void Advance(ref Transition tween, float now, ref float value)
        {
            if (tween.duration <= 0f) return;

            bool finished = tween.Evaluate(now, out float current);
            value = current;
            if (!finished) return;

            Action done = tween.onDone;
            tween = default;
            done?.Invoke();
        }

        /// <summary>Form the cloud over <paramref name="seconds"/>. S1-1's 點雲 world, and O-0's hands.</summary>
        public void RevealOver(float seconds, Action onDone = null) =>
            m_RevealTween = new Transition
            {
                from = m_Reveal, to = 1f, start = ExperienceClock.now,
                duration = Mathf.Max(0.0001f, seconds), onDone = onDone
            };

        /// <summary>Take the cloud apart over <paramref name="seconds"/>. S3-3's 物件化膜 and S4-1's 真人消失.</summary>
        public void DissolveOver(float seconds, Action onDone = null) =>
            m_DissolveTween = new Transition
            {
                from = m_Dissolve, to = 1f, start = ExperienceClock.now,
                duration = Mathf.Max(0.0001f, seconds), onDone = onDone
            };

        /// <summary>
        /// A short spike of corruption that decays back to the authored level -- the figures
        /// stuttering as they appear in S3-1, or reacting to being touched in S3-4.
        /// </summary>
        public void GlitchBurst(float level = 0.85f, float seconds = 0.35f)
        {
            m_BurstFrom = Mathf.Clamp01(level);
            m_GlitchBurst = m_BurstFrom;
            m_BurstStart = ExperienceClock.now;
            m_BurstDuration = Mathf.Max(0.01f, seconds);
        }

        /// <summary>
        /// Freeze the glitch on one held frame, or let it step again. The frozen state is the
        /// still "misplaced" cloud; stepping is the live fault.
        /// </summary>
        public void Freeze(bool frozen, float seed = -1f)
        {
            m_GlitchRate = frozen ? 0f : Mathf.Max(1f, m_GlitchRate);
            if (seed >= 0f) m_GlitchSeed = seed;
            Apply();
        }

        /// <summary>Put it back, for a staff restart between audiences.</summary>
        public void Reset(bool formed = true)
        {
            m_RevealTween = default;
            m_DissolveTween = default;
            m_Reveal = formed ? 1f : 0f;
            m_Dissolve = 0f;
            m_GlitchBurst = 0f;
            Apply();
        }

        /// <summary>
        /// Aim the drift at a world position, for the dissolve that crosses from the object in
        /// the player's hand to the figure beside them.
        /// </summary>
        public void DriftToward(Vector3 worldTarget)
        {
            Transform t = m_Cloud != null ? m_Cloud.transform : transform;
            m_DissolveDir = t.InverseTransformDirection(
                (worldTarget - t.TransformPoint(m_DissolveFrom)).normalized);
        }

        void Apply()
        {
            if (m_Cloud == null) return;
            m_Block ??= new MaterialPropertyBlock();

            m_Block.SetFloat(k_Phase, ExperienceClock.now);
            m_Block.SetFloat(k_Turbulence, m_Turbulence);
            m_Block.SetFloat(k_TurbulenceScale, m_TurbulenceScale);
            m_Block.SetFloat(k_TurbulenceSpeed, m_TurbulenceSpeed);

            m_Block.SetFloat(k_Reveal, m_Reveal);
            m_Block.SetVector(k_RevealFrom, m_RevealFrom);
            m_Block.SetFloat(k_RevealRadius, m_RevealRadius);

            m_Block.SetFloat(k_Glitch, Mathf.Max(m_Glitch, m_GlitchBurst));
            m_Block.SetFloat(k_GlitchSlabs, m_GlitchSlabs);
            m_Block.SetFloat(k_GlitchShift, m_GlitchShift);
            m_Block.SetFloat(k_GlitchDropout, m_GlitchDropout);
            m_Block.SetFloat(k_GlitchScatter, m_GlitchScatter);
            m_Block.SetFloat(k_GlitchRate, m_GlitchRate);
            m_Block.SetFloat(k_GlitchSeed, m_GlitchSeed);
            m_Block.SetFloat(k_GlitchChroma, m_GlitchChroma);

            m_Block.SetFloat(k_Dissolve, m_Dissolve);
            // CompositeBody/PointCloudFro has no _Dissolve -- it comes apart by proximity, and
            // takes a cue through _Cascade instead. Writing both means one driver moves all
            // three looks, and a block property the material does not declare is ignored, so
            // this costs nothing on the shaders that do not want it.
            m_Block.SetFloat(k_Cascade, m_Dissolve);
            m_Block.SetVector(k_DissolveFrom, m_DissolveFrom);
            m_Block.SetFloat(k_DissolveRadius, m_DissolveRadius);
            m_Block.SetFloat(k_DissolveScatter, m_DissolveScatter);
            m_Block.SetFloat(k_DissolveDrift, m_DissolveDrift);
            m_Block.SetVector(k_DissolveDir, m_DissolveDir);
            m_Block.SetFloat(k_DissolveSpread, m_DissolveSpread);

            m_Cloud.SetPropertyBlock(m_Block);

            // A fully dissolved cloud still rasterises every point before discarding it, and
            // at this count that is not free. Switching the renderer off once nothing is left
            // is the difference between a finished effect and a permanent cost.
            bool anythingVisible = m_Reveal > 0.001f && m_Dissolve < 0.999f;
            if (m_Cloud.enabled != anythingVisible) m_Cloud.enabled = anythingVisible;
        }
    }
}
