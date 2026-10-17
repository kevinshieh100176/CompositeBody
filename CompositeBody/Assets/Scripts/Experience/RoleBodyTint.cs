using Unity.Netcode;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Colours the Ch36 body with its owner's 代表色: Player A purple, Player B yellow, the same
    /// two hues as the 光圈 standing over the 真人 in O-0.
    ///
    /// KEYED ON THE AVATAR'S OWNER, NOT ON WHO IS LOOKING. This is the difference between this
    /// and every <see cref="LocalRolePresenter"/> in the project, and getting it the other way
    /// round would be invisible in single-player testing and wrong in the room: each client would
    /// paint EVERY body with its own colour, so both players would see two purple bodies, or two
    /// yellow ones, and never each other's. The roles are replicated, so each client can look up
    /// the owner of each avatar and arrive at the same answer independently -- no extra traffic,
    /// and nothing to get out of step.
    ///
    /// Written through a MaterialPropertyBlock, read-modify-write. <see cref="AvatarFormation"/>
    /// drives the dither fade on these same renderers through a block of its own, and a block is
    /// set whole: writing a fresh one here would wipe the fade, and writing a fresh one there
    /// would wipe this. Both fetch the current block before changing their own property, so the
    /// two survive each other whichever order they run in.
    /// </summary>
    public class RoleBodyTint : MonoBehaviour
    {
        [SerializeField, Tooltip("The body renderers to tint. Not the membrane films, and not " +
                                 "the head and hands -- those keep their own materials.")]
        Renderer[] m_Renderers = new Renderer[0];

        [SerializeField, Tooltip("Colour property on the body material.")]
        string m_ColorProperty = "_BaseColor";

        [SerializeField, Range(0f, 1f), Tooltip("How far from the authored colour toward the " +
                                                "role colour. 1 is the full 代表色; lower keeps " +
                                                "the body dark and merely tinted.")]
        float m_Strength = 1f;

        int m_ColorId;
        MaterialPropertyBlock m_Block;
        Color[] m_Authored;
        NetworkObject m_NetworkObject;
        PlayerRole m_Applied = (PlayerRole)(-1);
        bool m_Captured;

        void Awake()
        {
            m_ColorId = Shader.PropertyToID(m_ColorProperty);
            m_Block = new MaterialPropertyBlock();
            m_NetworkObject = GetComponentInParent<NetworkObject>(true);
        }

        void OnEnable()
        {
            Apply();
            if (GameSessionManager.Instance != null)
                GameSessionManager.Instance.playerRoles.OnListChanged += HandleRolesChanged;
        }

        void OnDisable()
        {
            if (GameSessionManager.Instance != null)
                GameSessionManager.Instance.playerRoles.OnListChanged -= HandleRolesChanged;
        }

        void HandleRolesChanged(NetworkListEvent<PlayerRoleEntry> _) => Apply();

        // The session and the role assignment both arrive after the avatar does, and the list
        // event only fires for changes made after we subscribed. Polling once a second costs
        // nothing and removes a whole class of "it was the wrong colour that one time".
        float m_NextPoll;

        void Update()
        {
            if (Time.unscaledTime < m_NextPoll) return;
            m_NextPoll = Time.unscaledTime + 1f;
            Apply();
        }

        void Apply()
        {
            if (!TryGetOwnerRole(out PlayerRole role)) role = PlayerRole.Unassigned;
            if (role == m_Applied) return;
            m_Applied = role;

            CaptureAuthored();

            Color target = PlayerRoleColors.For(role);

            for (int i = 0; i < m_Renderers.Length; i++)
            {
                Renderer r = m_Renderers[i];
                if (r == null) continue;

                Color authored = i < m_Authored.Length ? m_Authored[i] : Color.black;
                Color colour = role == PlayerRole.Unassigned
                    ? authored
                    : Color.Lerp(authored, target, m_Strength);

                r.GetPropertyBlock(m_Block);
                m_Block.SetColor(m_ColorId, colour);
                r.SetPropertyBlock(m_Block);
            }

            Debug.Log($"[RoleBodyTint] '{name}' owner role {role}; " +
                      $"{m_Renderers.Length} renderer(s) set to {ColorUtility.ToHtmlStringRGB(target)}.");
        }

        /// <summary>
        /// The role of whoever owns this avatar. Falls back to the debug role view when there is
        /// no session at all, so the rehearsal scene can show both colours without a host.
        /// </summary>
        bool TryGetOwnerRole(out PlayerRole role)
        {
            role = PlayerRole.Unassigned;

            GameSessionManager session = GameSessionManager.Instance;
            if (session != null && m_NetworkObject != null && m_NetworkObject.IsSpawned)
                return session.TryGetRoleForClient(m_NetworkObject.OwnerClientId, out role);

            if (LocalRolePresenter.debugRole.HasValue)
            {
                role = LocalRolePresenter.debugRole.Value;
                return role != PlayerRole.Unassigned;
            }

            return false;
        }

        /// <summary>
        /// The authored colours, so an unassigned avatar can go back to what the material says
        /// and a partial strength has something real to interpolate from.
        ///
        /// Captured on first use rather than in Awake: the body materials are swapped during
        /// spawn, and reading them before that would remember the wrong ones.
        /// </summary>
        void CaptureAuthored()
        {
            if (m_Captured) return;
            m_Captured = true;

            m_Authored = new Color[m_Renderers.Length];
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                Renderer r = m_Renderers[i];
                m_Authored[i] = r != null && r.sharedMaterial != null &&
                                r.sharedMaterial.HasProperty(m_ColorId)
                    ? r.sharedMaterial.GetColor(m_ColorId)
                    : Color.black;
            }
        }
    }
}
