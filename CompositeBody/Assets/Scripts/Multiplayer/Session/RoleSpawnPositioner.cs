using System.Collections;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using XRMultiplayer;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Moves the local XR Origin to a per-role start position once this machine's
    /// <see cref="PlayerRole"/> is known.
    ///
    /// Without this both players' rigs sit at the world origin, so on a desk test the two
    /// avatars occupy the same spot and you cannot tell whether replication is working. In the
    /// venue this is the "seat" each player starts from.
    ///
    /// Turn <see cref="m_ApplyOnRoleAssigned"/> off once players align to a physical marker via
    /// <see cref="CalibrationPoint"/>, since that establishes the shared origin instead.
    /// </summary>
    public class RoleSpawnPositioner : MonoBehaviour
    {
        [SerializeField, Tooltip("Where Player 1's rig starts.")]
        Transform m_Player1Spawn;

        [SerializeField, Tooltip("Where Player 2's rig starts.")]
        Transform m_Player2Spawn;

        [SerializeField, Tooltip("Disable when physical calibration defines the shared origin instead.")]
        bool m_ApplyOnRoleAssigned = true;

        [SerializeField, Tooltip("Skip the per-role offset whenever a headset is actually running. Leave this on: these offsets are a desktop-testing device, and applying them in the venue is what puts the two players in different places in the virtual room than they are in the real one.")]
        bool m_DesktopOnly = true;

        bool m_Applied;
        bool m_SkipReported;

        void OnEnable() => StartCoroutine(WaitAndBind());

        void OnDisable()
        {
            StopAllCoroutines();
            if (GameSessionManager.Instance != null)
                GameSessionManager.Instance.playerRoles.OnListChanged -= HandleRolesChanged;
        }

        IEnumerator WaitAndBind()
        {
            while (GameSessionManager.Instance == null) yield return null;
            GameSessionManager.Instance.playerRoles.OnListChanged += HandleRolesChanged;
            TryApply();
        }

        void HandleRolesChanged(NetworkListEvent<PlayerRoleEntry> _) => TryApply();

        void TryApply()
        {
            if (m_Applied || !m_ApplyOnRoleAssigned) return;

            // With a headset attached, the shared origin comes from each player calibrating to
            // the same physical marker. Offsetting the two rigs on top of that is what makes the
            // virtual room disagree with the real one, and makes the two players disagree with
            // each other -- each rig moved by a different amount from an origin that was already
            // correct.
            if (m_DesktopOnly && DesktopXrFallback.xrRuntimeActive)
            {
                if (!m_SkipReported)
                {
                    m_SkipReported = true;
                    Utils.Log("[RoleSpawnPositioner] XR runtime active; leaving the rig where tracking put it " +
                              "so physical calibration owns the shared origin.");
                }
                return;
            }

            if (!GameSessionManager.Instance.TryGetLocalRole(out PlayerRole role)) return;

            Transform target = role switch
            {
                PlayerRole.Player1 => m_Player1Spawn,
                PlayerRole.Player2 => m_Player2Spawn,
                _ => null
            };
            if (target == null) return;

            var origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
            {
                Utils.LogWarning("[RoleSpawnPositioner] No XROrigin in scene; cannot place the rig.");
                return;
            }

            origin.transform.SetPositionAndRotation(target.position, target.rotation);
            m_Applied = true;
            Utils.Log($"[RoleSpawnPositioner] Placed local rig at {role} spawn.");
        }
    }
}
