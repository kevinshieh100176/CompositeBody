using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Base for local-only, purely presentational components that render differently depending
    /// on which <see cref="PlayerRole"/> the local machine is. This never touches the network --
    /// each client independently derives its own view from the same replicated ground truth in
    /// <see cref="GameSessionManager"/>, so asymmetric visuals cost zero network traffic and can
    /// never be spoofed into affecting server-authoritative state.
    /// </summary>
    public abstract class LocalRolePresenter : MonoBehaviour
    {
        void OnEnable()
        {
            StartCoroutine(WaitAndBind());
        }

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
            Refresh();
        }

        void HandleRolesChanged(NetworkListEvent<PlayerRoleEntry> _) => Refresh();

        void Refresh()
        {
            bool roleKnown = GameSessionManager.Instance.TryGetLocalRole(out PlayerRole role);
            ApplyForRole(role, roleKnown);
        }

        /// <param name="role">The local machine's current role, or Unassigned if not resolved yet.</param>
        /// <param name="roleKnown">False before the local role has been assigned/replicated.</param>
        protected abstract void ApplyForRole(PlayerRole role, bool roleKnown);
    }
}
