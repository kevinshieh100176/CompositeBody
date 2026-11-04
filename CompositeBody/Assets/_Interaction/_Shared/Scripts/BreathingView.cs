using System;
using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.Interaction
{
    /// <summary>
    /// What a grabbable fragment looks like while it waits, trembles and lands -- all of it on the
    /// fragment's VISUAL CHILD, never on the networked root.
    ///
    /// WHY A CHILD. The root is what the hand moves and what the network transform replicates;
    /// writing a breath or a tremble there would fight both (and be sent over the wire 30 times a
    /// second for nothing). The child carries only the mesh, so its local offset is pure
    /// presentation and each machine draws its own.
    ///
    /// Everything is a function of the shared clock (<see cref="ExperienceClock"/>), phased per
    /// object from its authored position, so both headsets see the same fragment breathe the same
    /// way without any of it being replicated.
    ///
    ///   breathing  0..1  idle invitation: scale pulse + bob + emission pulse ("呼吸效果").
    ///                    Set the target; it eases there, so a grab fades the breath out.
    ///   shake01    0..1  in-hand tremble ("抖動"). Set every frame by the owner.
    ///   Wobble(t)        settle after a landing ("輕微的抖動後定住"), from a shared start time.
    ///   reveal01   0..1  appear / dissolve, written to _Reveal (CompositeBody/InteractionReveal).
    ///
    /// Emission and _Reveal go through one MaterialPropertyBlock -- the shared material is never
    /// touched. The material must have _EMISSION enabled for the pulse to show (the module
    /// builders make such a material).
    /// </summary>
    [DisallowMultipleComponent]
    public class BreathingView : MonoBehaviour
    {
        static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int k_Reveal = Shader.PropertyToID("_Reveal");

        [Header("Breathing")]
        [SerializeField, Range(0f, 0.3f), Tooltip("Scale swing, as a fraction of the authored scale.")]
        float m_BreathScale = 0.05f;

        [SerializeField, Min(0.2f), Tooltip("Seconds per breath.")]
        float m_BreathPeriod = 3.2f;

        [SerializeField, Min(0f), Tooltip("Metres of up/down float.")]
        float m_BobHeight = 0.025f;

        [SerializeField, Min(0.2f), Tooltip("Seconds per float cycle. Not the breath period, so " +
                                            "the two drift against each other and the motion never repeats exactly.")]
        float m_BobPeriod = 4.3f;

        [SerializeField, Tooltip("Emission colour at the top of a breath. HDR is off in this project, " +
                                 "so brightness tops out at 1 -- tune the colour, not an intensity.")]
        Color m_EmissionColor = new(1f, 0.82f, 0.55f, 1f);

        [SerializeField, Range(0f, 1f)] float m_EmissionMin = 0.05f;
        [SerializeField, Range(0f, 1f)] float m_EmissionMax = 0.45f;

        [SerializeField, Min(0.01f), Tooltip("Seconds for the breath to fade in or out when its target changes.")]
        float m_BreathFade = 0.4f;

        [Header("Tremble")]
        [SerializeField, Min(0f), Tooltip("Metres of tremble at shake01 = 1.")]
        float m_ShakeDistance = 0.008f;

        [SerializeField, Min(0f), Tooltip("Degrees of tremble at shake01 = 1.")]
        float m_ShakeAngle = 4f;

        [SerializeField, Min(0.1f)] float m_ShakeFrequency = 11f;

        [SerializeField, Range(0f, 1f), Tooltip("Extra emission while trembling, so the tremble reads in peripheral vision.")]
        float m_ShakeGlow = 0.35f;

        [Header("Settle")]
        [SerializeField, Min(0.05f)] float m_WobbleDuration = 0.6f;
        [SerializeField, Range(0f, 0.3f), Tooltip("Scale overshoot at the start of the settle.")]
        float m_WobbleScale = 0.06f;
        [SerializeField, Min(0f), Tooltip("Degrees of rock at the start of the settle.")]
        float m_WobbleAngle = 5f;
        [SerializeField, Min(0.5f)] float m_WobbleFrequency = 6f;

        Vector3 m_BaseLocalPosition;
        Quaternion m_BaseLocalRotation;
        Vector3 m_BaseLocalScale;
        float m_Phase;
        float m_Seed;

        float m_BreathTarget = 1f;
        float m_Breath = 1f;
        float m_Reveal = 1f;
        float m_WobbleStart = -1f;

        Renderer[] m_Renderers = Array.Empty<Renderer>();
        MaterialPropertyBlock m_Block;
        bool m_HasEmission;

        /// <summary>Target breathing weight 0..1; the drawn value eases toward it.</summary>
        public float breathing { get => m_BreathTarget; set => m_BreathTarget = Mathf.Clamp01(value); }

        /// <summary>Breathing weight as currently drawn.</summary>
        public float breathingNow => m_Breath;

        /// <summary>Tremble amount 0..1, set every frame by whoever decides it.</summary>
        public float shake01 { get; set; }

        /// <summary>0 = gone (renderers off), 1 = whole.</summary>
        public float reveal01 { get => m_Reveal; set => m_Reveal = Mathf.Clamp01(value); }

        /// <summary>Snap the breath to its target with no fade (spawn, reset).</summary>
        public void SnapBreathing() => m_Breath = m_BreathTarget;

        /// <summary>Start the settle at <paramref name="sharedTime"/> on <see cref="ExperienceClock"/>.</summary>
        public void Wobble(float sharedTime) => m_WobbleStart = sharedTime;

        public void CancelWobble() => m_WobbleStart = -1f;

        protected virtual void Awake()
        {
            transform.GetLocalPositionAndRotation(out m_BaseLocalPosition, out m_BaseLocalRotation);
            m_BaseLocalScale = transform.localScale;

            Vector3 authored = transform.parent != null ? transform.parent.position : transform.position;
            m_Phase = ExperienceClock.PhaseFor(authored);
            m_Seed = m_Phase * 97.3f + 1.7f;

            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_Block = new MaterialPropertyBlock();
            foreach (Renderer r in m_Renderers)
            {
                if (r == null || r.sharedMaterial == null) continue;
                if (r.sharedMaterial.IsKeywordEnabled("_EMISSION")) m_HasEmission = true;
            }
        }

        protected virtual void OnDisable()
        {
            // Leave the mesh where it was authored, so a disabled view is not frozen mid-breath.
            transform.SetLocalPositionAndRotation(m_BaseLocalPosition, m_BaseLocalRotation);
            transform.localScale = m_BaseLocalScale;
            foreach (Renderer r in m_Renderers)
            {
                if (r == null) continue;
                r.SetPropertyBlock(null);
                r.forceRenderingOff = false;
            }
        }

        protected virtual void LateUpdate()
        {
            float now = ExperienceClock.now;
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            m_Breath = Mathf.MoveTowards(m_Breath, m_BreathTarget, dt / m_BreathFade);

            // -- breath ----------------------------------------------------------------------
            float breathWave = Mathf.Sin((now / m_BreathPeriod + m_Phase) * 2f * Mathf.PI); // -1..1
            float bobWave = Mathf.Sin((now / m_BobPeriod + m_Phase * 0.61f) * 2f * Mathf.PI);
            float breath01 = breathWave * 0.5f + 0.5f;

            Vector3 offset = Vector3.up * (bobWave * m_BobHeight * m_Breath);
            float scale = 1f + breathWave * m_BreathScale * m_Breath;
            Quaternion turn = Quaternion.identity;

            // -- tremble ---------------------------------------------------------------------
            float shake = Mathf.Clamp01(shake01);
            if (shake > 0f)
            {
                Vector3 n = InteractionEasing.Shake(now, m_ShakeFrequency, m_Seed);
                Vector3 m = InteractionEasing.Shake(now + 17.3f, m_ShakeFrequency * 0.83f, m_Seed + 4.1f);
                offset += n * (m_ShakeDistance * shake);
                turn = Quaternion.Euler(m * (m_ShakeAngle * shake)) * turn;
            }

            // -- settle ----------------------------------------------------------------------
            if (m_WobbleStart >= 0f)
            {
                float since = now - m_WobbleStart;
                float w = InteractionEasing.Wobble(since, m_WobbleDuration, m_WobbleFrequency);
                scale += w * m_WobbleScale;
                turn = Quaternion.Euler(w * m_WobbleAngle, 0f, w * m_WobbleAngle * 0.6f) * turn;
                if (since >= m_WobbleDuration) m_WobbleStart = -1f;
            }

            Compose(now, ref offset, ref turn, ref scale);

            transform.SetLocalPositionAndRotation(m_BaseLocalPosition + offset, m_BaseLocalRotation * turn);
            transform.localScale = m_BaseLocalScale * scale;

            // -- material --------------------------------------------------------------------
            float glow = Mathf.Lerp(m_EmissionMin, m_EmissionMax, breath01) * m_Breath + shake * m_ShakeGlow;
            Color emission = m_EmissionColor * Mathf.Clamp01(glow);
            bool hidden = m_Reveal <= 0.0001f;

            foreach (Renderer r in m_Renderers)
            {
                if (r == null) continue;
                r.forceRenderingOff = hidden;
                if (hidden) continue;

                r.GetPropertyBlock(m_Block);
                m_Block.SetFloat(k_Reveal, m_Reveal);
                if (m_HasEmission) m_Block.SetColor(k_EmissionColor, emission);
                r.SetPropertyBlock(m_Block);
            }
        }

        /// <summary>Last word on the pose before it is written, for subclasses (e.g. a ramping tremble).</summary>
        protected virtual void Compose(float now, ref Vector3 offset, ref Quaternion turn, ref float scale) { }
    }
}
