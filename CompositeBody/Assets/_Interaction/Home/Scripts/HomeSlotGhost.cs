using UnityEngine;

namespace CompositeBody.Interaction.Home
{
    /// <summary>
    /// A blue outline of a fragment standing in its slot in the mini-house, shown only while
    /// somebody is carrying that fragment: "this goes here". It brightens as the fragment comes
    /// closer and is at full strength once the fragment is inside the assembly zone, so the last
    /// half-metre reads as getting warmer rather than as guessing.
    ///
    /// Shown on BOTH machines -- the person watching sees where their partner is heading -- because
    /// it reads only the replicated holder and the fragment's pose (which the network transform
    /// already carries). Gone the instant the fragment is resolved; fades out shortly after a
    /// fragment is let go anywhere else.
    ///
    /// Alpha and the proximity boost go through one MaterialPropertyBlock (CompositeBody/HomeSlotGhost
    /// reads _Alpha and _Boost); the renderer is switched off outright while invisible.
    /// </summary>
    [DisallowMultipleComponent]
    public class HomeSlotGhost : MonoBehaviour
    {
        static readonly int k_Alpha = Shader.PropertyToID("_Alpha");
        static readonly int k_Boost = Shader.PropertyToID("_Boost");

        [SerializeField] HomeFragment m_Fragment;

        [SerializeField, Tooltip("Found on this object if left empty.")]
        Renderer m_Renderer;

        [SerializeField, Range(0f, 1f), Tooltip("Strength the moment the fragment is picked up, far away.")]
        float m_BaseAlpha = 0.4f;

        [SerializeField, Range(0f, 1f), Tooltip("Strength once the fragment is inside the assembly zone.")]
        float m_MaxAlpha = 1f;

        [SerializeField, Min(0.05f), Tooltip("Metres (fragment centre to slot centre) at which it starts to brighten.")]
        float m_ProximityRange = 1.6f;

        [SerializeField, Min(0f), Tooltip("Metres at which it is fully bright (inside zone B it always is).")]
        float m_FullAt = 0.2f;

        [SerializeField, Min(0.01f), Tooltip("Seconds to fade in to the base strength.")]
        float m_FadeIn = 0.25f;

        [SerializeField, Min(0.01f), Tooltip("Seconds to fade out after a let-go outside the zone.")]
        float m_FadeOut = 0.2f;

        MaterialPropertyBlock m_Block;
        float m_Alpha;

        public HomeFragment fragment => m_Fragment;
        public Renderer ghostRenderer => m_Renderer;

        /// <summary>Strength drawn this frame (0 = renderer off).</summary>
        public float alpha => m_Alpha;

        /// <summary>0..1 how close the carried fragment is to its slot (1 inside zone B).</summary>
        public float proximity01 { get; private set; }

        public bool shown => m_Renderer != null && m_Renderer.enabled;

        void Awake()
        {
            if (m_Renderer == null) m_Renderer = GetComponent<Renderer>();
            m_Block = new MaterialPropertyBlock();
            Apply();
        }

        void LateUpdate()
        {
            if (m_Fragment == null) return;

            HomeFragmentState s = m_Fragment.state;
            if (s is HomeFragmentState.Placed or HomeFragmentState.Vanished or HomeFragmentState.Dormant)
            {
                m_Alpha = 0f;
                proximity01 = 0f;
                Apply();
                return;
            }

            // The replicated holder, so the other machine's carry shows here too. With no session
            // there is no holder; the local state says it instead.
            bool held = m_Fragment.IsSpawned ? m_Fragment.isHeld : s == HomeFragmentState.Held;
            proximity01 = held ? Proximity() : 0f;

            float target = held ? Mathf.Lerp(m_BaseAlpha, m_MaxAlpha, proximity01) : 0f;
            // Linear toward the target: up at a rate that reaches the base strength in m_FadeIn, down
            // at one that empties a full-strength ghost in m_FadeOut.
            float perSecond = target > m_Alpha
                ? Mathf.Max(m_BaseAlpha, 0.05f) / m_FadeIn
                : Mathf.Max(m_MaxAlpha, 0.05f) / m_FadeOut;
            m_Alpha = Mathf.MoveTowards(m_Alpha, target, perSecond * Time.deltaTime);
            Apply();
        }

        float Proximity()
        {
            HomeZone zone = m_Fragment.assembly != null ? m_Fragment.assembly.zoneB : null;
            Vector3 centre = m_Fragment.worldCenter;
            if (zone != null && zone.Contains(centre)) return 1f;
            Vector3 slotCentre = transform.TransformPoint(m_Fragment.localCenter);
            return InteractionEasing.Remap01(Vector3.Distance(centre, slotCentre), m_ProximityRange, m_FullAt);
        }

        void Apply()
        {
            if (m_Renderer == null) return;
            bool on = m_Alpha > 0.002f;
            if (m_Renderer.enabled != on) m_Renderer.enabled = on;
            if (!on) return;
            m_Block ??= new MaterialPropertyBlock();
            m_Block.SetFloat(k_Alpha, m_Alpha);
            m_Block.SetFloat(k_Boost, proximity01);
            m_Renderer.SetPropertyBlock(m_Block);
        }
    }
}
