using UnityEngine;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Hides this object (renderers, optionally colliders) on any client whose local role
    /// isn't in <see cref="m_VisibleToRoles"/>. E.g. "only Player1 can see object A".
    /// The object's real, server-synced state is untouched -- this only affects what this
    /// one client renders/can interact with.
    /// </summary>
    public class RoleVisibility : LocalRolePresenter
    {
        [SerializeField] PlayerRole[] m_VisibleToRoles = { PlayerRole.Player1, PlayerRole.Player2 };
        [SerializeField] bool m_AlsoDisableColliders = false;

        Renderer[] m_Renderers;
        Collider[] m_Colliders;

        void Awake()
        {
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            if (m_AlsoDisableColliders)
                m_Colliders = GetComponentsInChildren<Collider>(true);
        }

        protected override void ApplyForRole(PlayerRole role, bool roleKnown)
        {
            bool visible = roleKnown && System.Array.IndexOf(m_VisibleToRoles, role) >= 0;

            foreach (var r in m_Renderers) r.enabled = visible;

            if (m_AlsoDisableColliders)
            {
                foreach (var c in m_Colliders) c.enabled = visible;
            }
        }
    }
}
