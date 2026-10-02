using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Draws this object as a transparent, outlined ghost on any client whose role is not in
    /// <see cref="m_SolidForRoles"/>, and unregisters it from that client's interaction manager
    /// so it cannot be hovered or grabbed there.
    ///
    /// The softer sibling of <see cref="RoleVisibility"/>. Hiding the other player's half
    /// outright would make "bring your half to mine" unplayable: neither player could see what
    /// they were converging on, and a half that vanishes reads as missing rather than as
    /// somebody else's. A ghost says both "it is there" and "it is not yours" at once.
    ///
    /// Local presentation only, like every <see cref="LocalRolePresenter"/> -- the object's
    /// networked pose and its colliders are untouched, so physics stays identical on every
    /// client and nothing here can be spoofed into affecting server-authoritative state.
    /// </summary>
    public class RoleGhost : LocalRolePresenter
    {
        [SerializeField, Tooltip("Roles that see this object normally. Everyone else sees a ghost and cannot grab it.")]
        PlayerRole[] m_SolidForRoles = { PlayerRole.Player1, PlayerRole.Player2 };

        [SerializeField, Tooltip("Material swapped in while ghosted. Expects the CompositeBody/GhostHalf shader.")]
        Material m_GhostMaterial;

        [SerializeField, Tooltip("Disabled while ghosted, which unregisters it from this client's XRInteractionManager. Optional; found on this object if left empty.")]
        XRBaseInteractable m_Interactable;

        [SerializeField, Tooltip("A ghost that still casts a shadow reads as a solid object that isn't drawn, which is worse than either.")]
        bool m_CastShadowsWhenGhosted;

        Renderer[] m_Renderers;
        Material[][] m_SolidMaterials;
        ShadowCastingMode[] m_SolidShadowModes;

        bool m_Ghosted;

        /// <summary>True while this client is seeing the ghost rather than the real object.</summary>
        public bool isGhosted => m_Ghosted;

        void Reset()
        {
            m_Interactable = GetComponent<XRBaseInteractable>();
        }

        // Awake, not OnEnable: the base class already declares OnEnable, and Unity dispatches its
        // messages to the most derived declaration only, so redeclaring it here would silently
        // stop the base ever binding to the role list.
        void Awake()
        {
            Capture();
        }

        void Capture()
        {
            if (m_Renderers != null) return;

            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_SolidMaterials = new Material[m_Renderers.Length][];
            m_SolidShadowModes = new ShadowCastingMode[m_Renderers.Length];

            for (int i = 0; i < m_Renderers.Length; i++)
            {
                m_SolidMaterials[i] = m_Renderers[i].sharedMaterials;
                m_SolidShadowModes[i] = m_Renderers[i].shadowCastingMode;
            }

            if (m_Interactable == null) m_Interactable = GetComponent<XRBaseInteractable>();
        }

        protected override void ApplyForRole(PlayerRole role, bool roleKnown)
        {
            // Ghosted until proven otherwise: before the local role has replicated, showing every
            // half as solid would invite a player to reach for one that is about to turn out to
            // be the other player's.
            bool solid = roleKnown && System.Array.IndexOf(m_SolidForRoles, role) >= 0;
            SetGhosted(!solid);
        }

        /// <summary>
        /// Swaps this object between its real materials and the ghost. Public so the assembly
        /// code can force a finished object solid for both players, and so tooling can preview
        /// either state without a live session to derive a role from.
        /// </summary>
        public void SetGhosted(bool ghosted)
        {
            Capture();
            m_Ghosted = ghosted;

            for (int i = 0; i < m_Renderers.Length; i++)
            {
                var renderer = m_Renderers[i];
                if (renderer == null) continue;

                if (ghosted && m_GhostMaterial != null)
                {
                    // One entry per submesh: a mesh with several materials would otherwise keep
                    // rendering the extra submeshes solid.
                    var ghostSlots = new Material[m_SolidMaterials[i].Length];
                    for (int slot = 0; slot < ghostSlots.Length; slot++) ghostSlots[slot] = m_GhostMaterial;
                    renderer.sharedMaterials = ghostSlots;
                }
                else
                {
                    renderer.sharedMaterials = m_SolidMaterials[i];
                }

                renderer.shadowCastingMode = ghosted && !m_CastShadowsWhenGhosted
                    ? ShadowCastingMode.Off
                    : m_SolidShadowModes[i];
            }

            // Disabling the interactable unregisters it from this client's XRInteractionManager,
            // so the wrong player cannot hover or select it. Deliberately not done by disabling
            // colliders: those are shared physics, and dropping them on one client only would
            // put that client's simulation out of step with everyone else's.
            if (m_Interactable != null) m_Interactable.enabled = !ghosted;
        }

        /// <summary>Sets which role owns this object, so the owner is the only one who sees it solid.</summary>
        public void SetSolidFor(PlayerRole role)
        {
            m_SolidForRoles = new[] { role };
        }

        /// <summary>Makes this object solid for everyone -- used once a pair has been assembled.</summary>
        public void SetSolidForEveryone()
        {
            m_SolidForRoles = new[] { PlayerRole.Player1, PlayerRole.Player2 };
            SetGhosted(false);
        }
    }
}
