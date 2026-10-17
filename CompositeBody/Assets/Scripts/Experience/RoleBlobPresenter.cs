using UnityEngine;
using CompositeBody.Multiplayer;
using XRMultiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Shows the other player as what O-0 asks for: not a person, but "一團帶有各自代表色的
    /// 模糊輪廓" -- a soft blob in their own role colour, following where their head actually is.
    ///
    /// Two things happen here. The blob is positioned on the remote player's networked head, so
    /// their location is legible and their body is not; and the template's own humanoid avatar
    /// renderers are switched off for the duration, because a visible avatar next to a blob
    /// reads as a bug rather than as a ghost. Both are local-only and fully reversible: the
    /// remote player's networked pose is untouched, and <see cref="Hide"/> puts their renderers
    /// back exactly as they were, so a later beat can show the real body.
    ///
    /// The role lookup goes through <see cref="GameSessionManager"/> rather than through
    /// anything the remote client sends, so the colour a player is shown in cannot be chosen by
    /// that player's machine.
    /// </summary>
    public class RoleBlobPresenter : MonoBehaviour
    {
        [SerializeField, Tooltip("A volumetric light to stand in for the other player. Assigned, " +
                                 "it is used instead of the blob below: before the touch the " +
                                 "other person is a light in their own 代表色, not a shape.")]
        VolumetricPoint m_PresenceLight;

        [SerializeField, Min(0.1f), Tooltip("How far that light reaches, in metres.")]
        float m_LightRange = 1.3f;

        [SerializeField, Tooltip("Give the other player their body back once they have touched a " +
                                 "真人. Off keeps them a light for the whole beat regardless.")]
        bool m_RevealOnFormed = true;

        [SerializeField, Tooltip("The blob mesh. Expects a material on the CompositeBody/GhostHalf shader.")]
        Renderer m_BlobRenderer;

        [SerializeField, Min(0.1f), Tooltip("Blob diameter at the head.")]
        float m_Size = 0.62f;

        [SerializeField, Tooltip("Metres below the head the blob centres on, so it reads as a presence rather than a floating ball.")]
        float m_VerticalOffset = -0.18f;

        [SerializeField, Min(0f), Tooltip("How quickly the blob chases the head. Lower lags behind, which blurs fast head turns.")]
        float m_Smoothing = 7f;

        [SerializeField, Tooltip("Hide the template's humanoid avatar while the blob stands in for it.")]
        bool m_HideRemoteAvatar = true;

        [SerializeField, Tooltip("Start as soon as this object is enabled, which is what BeatController does on beat entry.")]
        bool m_ShowOnEnable = true;

        static readonly int k_GhostColorId = Shader.PropertyToID("_GhostColor");

        XRINetworkPlayer m_Remote;
        Renderer[] m_HiddenRenderers;
        Material m_Material;
        bool m_Shown;

        void Awake()
        {
            if (m_BlobRenderer != null)
            {
                // Instanced so two blobs in a scene cannot fight over one shared colour.
                m_Material = m_BlobRenderer.material;
            }

            // Both stand-ins off until a remote player is actually found, so neither sits at the
            // origin for the frames between this scene loading and the other headset connecting.
            SetStandInVisible(false);
        }

        // The blob lives inside its beat's content root, so being switched on by
        // BeatController is the signal to start standing in for the other player.
        void OnEnable()
        {
            if (m_ShowOnEnable) Show();
        }

        void OnDisable() => Hide();

        /// <summary>Start standing in for the other player. Safe to call every beat entry.</summary>
        public void Show()
        {
            m_Shown = true;
        }

        /// <summary>Stop, and give the remote player their own renderers back.</summary>
        public void Hide()
        {
            m_Shown = false;

            SetStandInVisible(false);
            RestoreRemoteAvatar();
            m_Remote = null;
        }

        void LateUpdate()
        {
            if (!m_Shown) return;

            if (m_Remote == null)
            {
                // Cheap to retry: the remote player's avatar spawns some frames after the
                // session connects, and can respawn if their headset reconnects mid-show.
                m_Remote = FindRemotePlayer();
                if (m_Remote == null)
                {
                    if (m_BlobRenderer != null) m_BlobRenderer.enabled = false;
                    return;
                }

                ApplyRoleColor();
                HideRemoteAvatar();
            }

            // Once they have touched, they stop being a presence and become a person. The stand-in
            // goes away and their own renderers come back -- their body then grows in on its own,
            // driven by AvatarFormation from the same replicated stamp their machine used, so
            // both players watch the same arrival rather than two that merely look alike.
            if (m_RevealOnFormed && RemoteHasFormed())
            {
                SetStandInVisible(false);
                RestoreRemoteAvatar();
                return;
            }

            HideRemoteAvatar();

            Transform head = m_Remote.head;
            if (head == null) return;

            Vector3 target = head.position + Vector3.up * m_VerticalOffset;

            if (m_PresenceLight != null)
            {
                Transform light = m_PresenceLight.transform;
                light.position = m_Smoothing > 0f
                    ? Vector3.Lerp(light.position, target, 1f - Mathf.Exp(-m_Smoothing * Time.deltaTime))
                    : target;
                m_PresenceLight.reveal = 1f;
            }

            if (m_BlobRenderer != null)
            {
                Transform blob = m_BlobRenderer.transform;
                blob.position = m_Smoothing > 0f
                    ? Vector3.Lerp(blob.position, target, 1f - Mathf.Exp(-m_Smoothing * Time.deltaTime))
                    : target;
                blob.localScale = Vector3.one * m_Size;

                // The blob is the fallback, not a second stand-in: two of them at one head reads
                // as a bug.
                m_BlobRenderer.enabled = m_PresenceLight == null;
            }
        }

        bool RemoteHasFormed()
        {
            AvatarFormationSync sync = AvatarFormationSync.For(m_Remote);
            return sync != null && sync.formed;
        }

        void SetStandInVisible(bool visible)
        {
            if (m_PresenceLight != null) m_PresenceLight.reveal = visible ? 1f : 0f;
            if (m_BlobRenderer != null) m_BlobRenderer.enabled = visible && m_PresenceLight == null;
        }

        static XRINetworkPlayer FindRemotePlayer()
        {
            var players = Object.FindObjectsByType<XRINetworkPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var player in players)
            {
                if (player != XRINetworkPlayer.LocalPlayer) return player;
            }
            return null;
        }

        void ApplyRoleColor()
        {
            // Only the remote matters here. Guarding on the blob material as well would skip the
            // light entirely whenever the blob is the thing that is absent -- which, now that the
            // light is the stand-in, is the normal case.
            if (m_Remote == null) return;

            var session = GameSessionManager.Instance;
            PlayerRole role = PlayerRole.Unassigned;
            if (session != null) session.TryGetRoleForClient(m_Remote.OwnerClientId, out role);

            if (m_PresenceLight != null)
            {
                m_PresenceLight.SetRole(role);
                m_PresenceLight.range = m_LightRange;
            }

            if (m_Material == null) return;
            m_Material.SetColor(k_GhostColorId, PlayerRoleColors.For(role));
        }

        void HideRemoteAvatar()
        {
            if (!m_HideRemoteAvatar || m_Remote == null) return;

            // Idempotent, because LateUpdate calls this every frame now. A second pass would
            // record renderers this component had already switched off, and then Restore would
            // put back a list that no longer describes what was originally visible.
            if (m_HiddenRenderers != null) return;

            var renderers = m_Remote.GetComponentsInChildren<Renderer>(true);
            var hidden = new System.Collections.Generic.List<Renderer>(renderers.Length);

            Renderer lightRenderer = m_PresenceLight != null
                ? m_PresenceLight.GetComponent<Renderer>() : null;

            foreach (var r in renderers)
            {
                // Never touch a stand-in, in case it has been parented under the avatar.
                if (r == m_BlobRenderer || r == lightRenderer || !r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }

            m_HiddenRenderers = hidden.ToArray();
        }

        void RestoreRemoteAvatar()
        {
            if (m_HiddenRenderers == null) return;

            foreach (var r in m_HiddenRenderers)
            {
                if (r != null) r.enabled = true;
            }
            m_HiddenRenderers = null;
        }
    }
}
