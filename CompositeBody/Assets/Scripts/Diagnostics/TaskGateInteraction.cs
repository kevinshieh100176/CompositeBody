using System;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// A debug row for one <see cref="StoryProgressManager"/> task -- the two-player gates, which
    /// are the interactions with no component of their own: a task is a pair of flags on the
    /// server, reported from two machines, read by whatever is waiting on it.
    ///
    /// NOT A MONOBEHAVIOUR for that reason. Hanging a component off some arbitrary object to
    /// represent a task would make the row's lifetime depend on that object's, which is not what
    /// a task's lifetime is: the flags live in the session, outscoping every scene the piece
    /// loads.
    ///
    /// THE THREE FIRE TIERS DIFFER IN AUTHORITY, and the row is honest about which one was used.
    /// Local goes through the same owner-attributed RPC the real interaction uses, so it proves
    /// the real path works. Player1/Player2/Both write the flags on the server directly, which
    /// no real interaction can do -- that is what lets one person walk a two-player gate alone,
    /// and also what could hide a client whose reports never arrive.
    /// </summary>
    public class TaskGateInteraction : IDebugInteraction
    {
        readonly string m_TaskId;
        string m_Label;

        /// <summary>
        /// Optional: where this gate's timeout comes from, in seconds remaining.
        ///
        /// A TASK HAS NO CLOCK OF ITS OWN. The fallback that eventually clears a gate belongs to
        /// whatever reports it -- O1GrabStation counts down while the show waits on its gate --
        /// so a gate row that wants to show a countdown is told where to read one.
        /// </summary>
        public Func<float> timeoutSource { get; set; }

        /// <summary>Set when a tier above Local was used, so the row can say the flags were faked.</summary>
        public DebugFireTarget? forcedBy { get; private set; }

        public TaskGateInteraction(string taskId, string label)
        {
            m_TaskId = taskId;
            m_Label = string.IsNullOrWhiteSpace(label) ? taskId : label;
        }

        public string taskId => m_TaskId;

        public string debugId => m_TaskId;
        public string debugLabel => m_Label;
        public DebugInteractionScope debugScope => DebugInteractionScope.Gate;
        public string debugFireLabel => "Report mine";

        /// <summary>A later, better label replaces a bare task id. Never the other way round.</summary>
        public void Rename(string label)
        {
            if (string.IsNullOrWhiteSpace(label) || label == m_TaskId) return;
            if (m_Label != m_TaskId) return;
            m_Label = label;
        }

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            StoryProgressManager progress = StoryProgressManager.Instance;

            if (progress == null)
            {
                state.detail = "no session";
                return state;
            }

            foreach (var task in progress.tasks)
            {
                if (!task.taskId.ToString().Equals(m_TaskId)) continue;
                state.player1 = task.player1Done;
                state.player2 = task.player2Done;
                break;
            }

            // IsTaskComplete rather than both flags: in a solo run the gate asks only the roles
            // that are present, and a row reading "not fired" next to a show that has moved on
            // would send someone looking for a bug in the wrong place.
            state.fired = progress.IsTaskComplete(m_TaskId);

            if (timeoutSource != null) state.secondsToTimeout = timeoutSource();

            GameSessionManager session = GameSessionManager.Instance;
            if (session != null && session.solo)
            {
                string needed = session.IsRoleOccupied(PlayerRole.Player1) ? "P1" : "";
                if (session.IsRoleOccupied(PlayerRole.Player2)) needed += needed.Length > 0 ? "+P2" : "P2";
                state.detail = $"solo run: waiting on {(needed.Length > 0 ? needed : "nobody")}";
            }

            if (forcedBy.HasValue)
            {
                string forced = $"forced server-side as {forcedBy.Value}";
                state.detail = string.IsNullOrEmpty(state.detail) ? forced : $"{state.detail}; {forced}";
            }

            return state;
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            if (StoryProgressManager.Instance == null) return false;

            if (target == DebugFireTarget.Local)
            {
                // The report is attributed to the sender's role, so a machine with no role
                // cannot report at all -- the RPC would be dropped server-side and the button
                // would look broken rather than inapplicable.
                return InteractionDebug.LocalRole() != PlayerRole.Unassigned;
            }

            return InteractionDebugRelay.canForce;
        }

        public void DebugFire(DebugFireTarget target)
        {
            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress == null) return;

            if (target == DebugFireTarget.Local)
            {
                Debug.Log($"[InteractionDebug] reporting local half of '{m_TaskId}' " +
                          $"as {InteractionDebug.LocalRole()}.");
                progress.ReportTaskCompleteRpc(m_TaskId);
                return;
            }

            if (!InteractionDebugRelay.canForce) return;

            forcedBy = target;
            if (target != DebugFireTarget.Player2) progress.DebugSetRoleDone(m_TaskId, PlayerRole.Player1, true);
            if (target != DebugFireTarget.Player1) progress.DebugSetRoleDone(m_TaskId, PlayerRole.Player2, true);
        }

        public bool DebugCanReset() => InteractionDebugRelay.canForce;

        public void DebugReset()
        {
            if (!InteractionDebugRelay.canForce) return;

            forcedBy = null;
            StoryProgressManager.Instance?.ResetTask(m_TaskId);
            Debug.Log($"[InteractionDebug] cleared both halves of '{m_TaskId}'.");
        }
    }
}
