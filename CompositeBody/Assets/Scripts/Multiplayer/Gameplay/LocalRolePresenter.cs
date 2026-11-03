using System.Collections;
using System.Collections.Generic;
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
    ///
    /// THE DEBUG OVERRIDE. Because the asymmetry is local, one machine can be told to render as
    /// if it were the other player and nothing anywhere else has to agree -- no session, no second
    /// headset, no second person. <see cref="debugRole"/> is that switch, and it is deliberately
    /// on this base class rather than on any one beat: every role-dependent view in the piece
    /// answers to it at once, which is the only way the switch tells you the truth about what the
    /// other player sees rather than about one component's half of it.
    /// </summary>
    public abstract class LocalRolePresenter : MonoBehaviour
    {
        // Every live presenter, so flipping the override can reach all of them. A list rather
        // than a FindObjectsByType sweep: these get switched off by the very components in it,
        // and an object deactivated by its own presenter would never be found again.
        static readonly List<LocalRolePresenter> s_Live = new();

        static PlayerRole? s_DebugRole;

        /// <summary>
        /// Render as this role regardless of what the session says. Null -- the normal state --
        /// means the real local role is used. Setting it re-applies every presenter immediately.
        /// Debug only: it changes what is drawn here and nothing else, so the other machine is
        /// unaffected and no replicated state moves.
        /// </summary>
        public static PlayerRole? debugRole
        {
            get => s_DebugRole;
            set
            {
                if (s_DebugRole == value) return;
                s_DebugRole = value;
                RefreshAll();
                Debug.Log(value.HasValue
                              ? $"[RoleView] rendering as {value.Value} (debug override)."
                              : "[RoleView] debug override cleared; back to the real local role.");
            }
        }

        /// <summary>
        /// The role this machine is RENDERING AS -- the debug override when one is set, the real
        /// local role otherwise. False when neither is available, which is the editor with no
        /// session and the moments before the roles replicate.
        ///
        /// Exposed because the asymmetry is not only in components: a timeline clip that staggers
        /// a dissolve between the two players (see
        /// <see cref="CompositeBody.PointClouds.FigureRoleDissolveBehaviour"/>) has to answer the
        /// same question, and if it answered it by reading the session directly then the debug
        /// override would move every other role-dependent view and leave that one behind --
        /// which is precisely the failure the override exists to rule out.
        /// </summary>
        public static bool TryGetViewRole(out PlayerRole role)
        {
            if (s_DebugRole.HasValue)
            {
                role = s_DebugRole.Value;
                return role != PlayerRole.Unassigned;
            }

            GameSessionManager session = GameSessionManager.Instance;
            if (session != null) return session.TryGetLocalRole(out role);

            role = PlayerRole.Unassigned;
            return false;
        }

        /// <summary>Re-apply every live presenter. Cheap, and only called when something flips.</summary>
        public static void RefreshAll()
        {
            for (int i = s_Live.Count - 1; i >= 0; i--)
            {
                if (s_Live[i] == null) { s_Live.RemoveAt(i); continue; }
                s_Live[i].Refresh();
            }
        }

        // Statics survive entering play mode when domain reloading is off, and an override left
        // over from the last run is a debug tool lying about the build.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Live.Clear();
            s_DebugRole = null;
        }

        void OnEnable()
        {
            if (!s_Live.Contains(this)) s_Live.Add(this);

            // Applied before the session exists, not only after. Without this a presenter sits
            // in whatever state the scene was saved in until a session turns up -- and with the
            // debug override set there may never be one.
            Refresh();
            StartCoroutine(WaitAndBind());
        }

        void OnDisable()
        {
            s_Live.Remove(this);
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
            // Unassigned is a real choice for the override: it is how the operator asks for the
            // before-the-roles-arrive view, which is the one state a second headset cannot
            // easily be made to show. TryGetViewRole reports it as "not known", which is exactly
            // what that view is.
            bool roleKnown = TryGetViewRole(out PlayerRole role);
            ApplyForRole(role, roleKnown);
        }

        /// <param name="role">The local machine's current role, or Unassigned if not resolved yet.</param>
        /// <param name="roleKnown">False before the local role has been assigned/replicated.</param>
        protected abstract void ApplyForRole(PlayerRole role, bool roleKnown);
    }
}
