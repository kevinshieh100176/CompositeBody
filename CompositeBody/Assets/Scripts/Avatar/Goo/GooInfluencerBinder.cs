using UnityEngine;

namespace CompositeBody.Avatar.Goo
{
    /// <summary>
    /// Feeds "toucher" spheres to the goo shader each frame. Anything assigned here (the
    /// player's hands, a prop, another player's fingertip) pushes the goo surface out of its
    /// volume, denting and bulging the jelly on contact.
    ///
    /// Uses a MaterialPropertyBlock so several avatars can share one material asset without
    /// overwriting each other's deformers.
    /// </summary>
    [ExecuteAlways] // also drives the deformation in the editor, so it can be dialled in without entering Play
    [RequireComponent(typeof(Renderer))]
    public class GooInfluencerBinder : MonoBehaviour
    {
        public const int MaxInfluencers = 8; // must match GOO_MAX_INFLUENCERS in GooJelly.shader

        [System.Serializable]
        public struct Influencer
        {
            public Transform source;
            [Tooltip("Radius of the deforming sphere, in world units.")]
            public float radius;
        }

        [SerializeField] Influencer[] m_Influencers = new Influencer[0];

        static readonly int s_InfluencersId = Shader.PropertyToID("_GooInfluencers");
        static readonly int s_CountId = Shader.PropertyToID("_GooInfluencerCount");

        Renderer m_Renderer;
        MaterialPropertyBlock m_Block;
        readonly Vector4[] m_Packed = new Vector4[MaxInfluencers];

        void Awake() => EnsureInitialised();

        void EnsureInitialised()
        {
            if (m_Renderer == null) m_Renderer = GetComponent<Renderer>();
            m_Block ??= new MaterialPropertyBlock();
        }

        void LateUpdate() => Apply();

        /// <summary>
        /// Pushes the current deformer set to the renderer immediately. Called every frame, and
        /// callable directly by tooling that needs the deformation applied before it draws
        /// rather than waiting for the next editor tick.
        /// </summary>
        public void Apply()
        {
            EnsureInitialised(); // Awake does not run for edit-mode domain reloads
            int count = 0;
            for (int i = 0; i < m_Influencers.Length && count < MaxInfluencers; i++)
            {
                var influencer = m_Influencers[i];
                if (influencer.source == null || influencer.radius <= 0f) continue;

                Vector3 p = influencer.source.position;
                m_Packed[count++] = new Vector4(p.x, p.y, p.z, influencer.radius);
            }

            // Zero out the unused tail; stale entries would keep denting the goo.
            for (int i = count; i < MaxInfluencers; i++)
                m_Packed[i] = Vector4.zero;

            m_Renderer.GetPropertyBlock(m_Block);
            m_Block.SetVectorArray(s_InfluencersId, m_Packed);
            m_Block.SetInt(s_CountId, count);
            m_Renderer.SetPropertyBlock(m_Block);
        }
    }
}
