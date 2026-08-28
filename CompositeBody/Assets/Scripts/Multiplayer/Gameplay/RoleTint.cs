using System;
using UnityEngine;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Applies a different material to this renderer depending on the local client's role.
    /// E.g. the same physical object looks red to Player1 and blue to Player2. Purely local
    /// presentation -- the object's networked identity/position is unaffected.
    /// </summary>
    public class RoleTint : LocalRolePresenter
    {
        [Serializable]
        struct RoleMaterial
        {
            public PlayerRole role;
            public Material material;
        }

        [SerializeField] Renderer m_Renderer;
        [SerializeField] RoleMaterial[] m_MaterialsByRole;
        [SerializeField] Material m_DefaultMaterial;

        void Reset()
        {
            m_Renderer = GetComponent<Renderer>();
        }

        protected override void ApplyForRole(PlayerRole role, bool roleKnown)
        {
            if (m_Renderer == null) return;

            Material chosen = m_DefaultMaterial;
            if (roleKnown)
            {
                foreach (var entry in m_MaterialsByRole)
                {
                    if (entry.role == role)
                    {
                        chosen = entry.material;
                        break;
                    }
                }
            }

            if (chosen != null) m_Renderer.material = chosen;
        }
    }
}
