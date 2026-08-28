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

        bool m_Applied;

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
            if (!GameSessionManager.Instance.TryGetLocalRole(out PlayerRole role)) return;

            Transform target = role switch
            {
                PlayerRole.Player1 => m_Player1Spawn,
                PlayerRole.Player2 => m_Player2Spawn,
                _ => null
            };
            if (target == null) return;

            var origin = FindFirstObjectByType<XROrigin>();
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
