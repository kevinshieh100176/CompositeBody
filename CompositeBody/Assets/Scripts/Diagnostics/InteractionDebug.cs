using System;
using System.Collections.Generic;
using UnityEngine;
using CompositeBody.Multiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>What kind of thing an interaction's state belongs to, which decides how it reads.</summary>
    public enum DebugInteractionScope
    {
        /// <summary>A StoryProgressManager task: one flag per role, server-authoritative.</summary>
        Gate = 0,

        /// <summary>Replicated state belonging to one player, e.g. their body having formed.</summary>
        Player = 1,

        /// <summary>
        /// Local to one machine and not replicated by the show itself -- the 真人 touch, the
        /// calibration pinch. The panel mirrors these over the debug relay so one screen can
        /// still show both machines.
        /// </summary>
        LocalMachine = 2,
    }

    /// <summary>Who a debug Fire is pressed on behalf of.</summary>
    public enum DebugFireTarget
    {
        /// <summary>This machine, through the same path the real interaction takes.</summary>
        Local = 0,

        Player1 = 1,
        Player2 = 2,

        /// <summary>Both roles at once, for walking a two-player gate alone.</summary>
        Both = 3,
    }

    /// <summary>
    /// Everything the panel draws for one row, fetched once per refresh. One struct rather than
    /// six properties because it is also what the relay replicates for the other machine's
    /// local-only interactions, and because a row read in one call cannot show a half-updated
    /// mixture of two frames.
    /// </summary>
    public struct DebugInteractionState
    {
        /// <summary>Has this interaction happened at all.</summary>
        public bool fired;

        /// <summary>Which roles have set it off. Both false on a LocalMachine row.</summary>
        public bool player1, player2;

        /// <summary>Seconds until a timeout fires it by itself. NEGATIVE means no timeout.</summary>
        public float secondsToTimeout;

        /// <summary>Anything else worth a line: hold progress, which station, why it is waiting.</summary>
        public string detail;

        /// <summary>Nothing has happened and there is no clock running.</summary>
        public static DebugInteractionState Idle => new() { secondsToTimeout = -1f };
    }

    /// <summary>
    /// What every interaction in the piece implements so it appears in the debug list and can be
    /// fired from it.
    ///
    /// THE POINT IS THAT A NEW INTERACTION CANNOT BE FORGOTTEN. The alternative -- a panel that
    /// knows the types it should look for -- means every interaction added after the panel is
    /// invisible until someone remembers to go back and teach it about the new one, and the ones
    /// that get forgotten are exactly the half-finished ones worth watching. Implementing this
    /// is a handful of lines and the row appears on its own.
    ///
    /// Implementations are asked for their state every refresh, so they must be cheap and must
    /// not change anything. Firing, by contrast, is allowed to do the real thing.
    /// </summary>
    public interface IDebugInteraction
    {
        /// <summary>
        /// Stable identity, unique across the show: "beat.O1_Control", "o0.touch",
        /// "o0.awakening", "calibration". Used as the relay's key, so it must not be a name
        /// built from an instance id that changes between runs or between machines.
        /// </summary>
        string debugId { get; }

        /// <summary>Row heading, for a person reading a list at speed.</summary>
        string debugLabel { get; }

        DebugInteractionScope debugScope { get; }

        /// <summary>
        /// What the local button says, because "Fire" is a lie for some of these: arming the
        /// calibrator does not calibrate anything, it allows the next pinch to.
        /// </summary>
        string debugFireLabel { get; }

        DebugInteractionState DebugState();

        bool DebugCanFire(DebugFireTarget target);

        /// <summary>Do it, as if the player had. Only called when <see cref="DebugCanFire"/> agreed.</summary>
        void DebugFire(DebugFireTarget target);

        /// <summary>Whether this can be put back to not-yet-happened.</summary>
        bool DebugCanReset();

        void DebugReset();
    }

    /// <summary>
    /// The list of everything that can be debugged, and the one switch that decides whether its
    /// buttons do anything.
    ///
    /// A STATIC REGISTRY RATHER THAN A SCENE SEARCH, because the interactions are spread across
    /// a scene, an additively loaded scene, a beat's content root and the player prefab -- so a
    /// search would have to run every refresh over everything, including inactive objects, and
    /// would still miss the task gates, which are rows without a component anywhere.
    ///
    /// Entries register on enable and drop out on disable, so the list is what is actually
    /// running rather than what the project contains.
    /// </summary>
    public static class InteractionDebug
    {
        /// <summary>
        /// Command line switch that LOCKS the Fire buttons, for a build an audience is in front
        /// of. The default is unlocked -- see <see cref="fireEnabled"/>.
        /// </summary>
        public const string LockArgument = "-lockinteractiondebug";

        /// <summary>
        /// What used to turn the buttons ON in a player build. Kept because it costs a line and
        /// because every note, shortcut and habit that says to pass it should keep working now
        /// that it is the default; passing it does nothing.
        /// </summary>
        public const string FireArgument = "-debuginteractions";

        static readonly List<IDebugInteraction> s_Entries = new();
        static readonly Dictionary<string, TaskGateInteraction> s_TaskGates = new();
        static bool? s_FireEnabled;

        /// <summary>Everything currently registered, in registration order.</summary>
        public static IReadOnlyList<IDebugInteraction> entries => s_Entries;

        /// <summary>Raised when a row appears or disappears, so a panel can rebuild itself.</summary>
        public static event Action onEntriesChanged;

        /// <summary>
        /// Whether the panel's buttons may actually do anything. LIVE EVERYWHERE unless the
        /// build was told to lock them.
        ///
        /// THIS USED TO BE THE OTHER WAY ROUND, and the reasoning was about an audience: a gate
        /// fired by a stray click during a performance must not be one misclick away, so a
        /// player build needed -debuginteractions and the editor was simply live. What that
        /// costs is paid every day and by the people least able to afford it -- both machines in
        /// this piece are rehearsal machines for months before they are venue machines, the
        /// panel is how a two-machine stall is diagnosed at all, and a build launched by
        /// double-clicking the exe (which is how anyone actually starts it) showed a full list
        /// of interactions above a row of dead buttons and a line of small print. The failure
        /// mode of the old default was a test session lost to "why is nothing happening"; the
        /// failure mode of this one is a misclick, and the lock is still there for the night it
        /// matters.
        ///
        /// THE LIST IS ALWAYS READABLE EITHER WAY. A show that stalls in front of an audience is
        /// exactly when staff need to see which half of a gate is missing, so nothing here ever
        /// strips the panel itself from a build.
        /// </summary>
        public static bool fireEnabled
        {
            get
            {
                s_FireEnabled ??= !HasArgument(LockArgument);
                return s_FireEnabled.Value;
            }
        }

        static bool HasArgument(string flag)
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Whether this exact interaction is already on the list.</summary>
        public static bool IsRegistered(IDebugInteraction interaction) =>
            interaction != null && s_Entries.Contains(interaction);

        public static void Register(IDebugInteraction interaction)
        {
            if (interaction == null || s_Entries.Contains(interaction)) return;

            // TWO ROWS UNDER ONE ID IS A HALF-BREAK, AND A SILENT ONE. Both rows appear in the
            // panel and both local buttons work, so the list looks right -- but every lookup by
            // id takes the FIRST match: DebugKeys' bindings, the panel's remote fire, the test
            // runner. The second row can be watched and never fired from a key or from the
            // other machine. O-1 shipped like that for a while, once each player had more than
            // one object to pick up and put away, so the check is here rather than in a comment.
            foreach (IDebugInteraction existing in s_Entries)
            {
                if (existing.debugId != interaction.debugId) continue;

                Debug.LogWarning($"[InteractionDebug] two interactions share the id " +
                                 $"'{interaction.debugId}' ('{existing.debugLabel}' and " +
                                 $"'{interaction.debugLabel}'). Both get a row, but a key " +
                                 "binding or a remote fire can only ever reach the first. Make " +
                                 "the id name the object, not just the player.");
                break;
            }

            s_Entries.Add(interaction);
            onEntriesChanged?.Invoke();
        }

        public static void Unregister(IDebugInteraction interaction)
        {
            if (interaction == null || !s_Entries.Remove(interaction)) return;
            onEntriesChanged?.Invoke();
        }

        /// <summary>
        /// Add a row for a StoryProgressManager task. Deduplicated by id, because the same task
        /// is legitimately declared from more than one place -- a beat definition names its gate
        /// and so does the thing that reports it -- and two rows for one task would show two
        /// sets of buttons for the same flags.
        /// </summary>
        public static TaskGateInteraction RegisterTaskGate(string taskId, string label)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return null;

            if (s_TaskGates.TryGetValue(taskId, out TaskGateInteraction existing))
            {
                // A later declaration with a real label wins over a bare id, so a task first
                // seen in the progress list gets named once something that knows it registers.
                existing.Rename(label);
                return existing;
            }

            var gate = new TaskGateInteraction(taskId, label);
            s_TaskGates.Add(taskId, gate);
            Register(gate);
            return gate;
        }

        /// <summary>
        /// Statics outlive a play-mode session when domain reload is off, and a row left over
        /// from the last run is worse than no row: it points at a destroyed component.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Entries.Clear();
            s_TaskGates.Clear();
            s_FireEnabled = null;
            onEntriesChanged = null;
        }

        /// <summary>The role this machine is playing, or Unassigned.</summary>
        public static PlayerRole LocalRole()
        {
            GameSessionManager session = GameSessionManager.Instance;
            return session != null && session.TryGetLocalRole(out PlayerRole role)
                ? role
                : PlayerRole.Unassigned;
        }
    }
}
