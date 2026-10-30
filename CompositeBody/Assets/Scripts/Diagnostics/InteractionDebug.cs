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
    /// An interaction that staff can also correct by hand, in small steps, from the panel.
    ///
    /// SEPARATE FROM <see cref="IDebugInteraction"/> BECAUSE ALMOST NOTHING WANTS IT. Firing a
    /// gate is a thing every interaction can express; being nudged four centimetres to the left
    /// is not, and putting it on the main interface would mean every implementation carrying
    /// five methods that throw. The panel draws the extra row only for the interactions that
    /// implement this, so adding it to something else later needs no panel change.
    ///
    /// A <see cref="DebugFireTarget"/> IS STILL ASKED FOR, and an implementation is free to
    /// honour only <see cref="DebugFireTarget.Local"/> -- the calibrator does, because an origin
    /// belongs to the headset in front of whoever is correcting it. The parameter stays so that
    /// the panel's code path is the same shape as its firing one, and so a future nudgeable
    /// thing that genuinely is another machine's business can say so.
    ///
    /// THE UNITS ARE THE ROOM'S, not the player's and not Unity's. A nudge is given in metres
    /// along the calibration marker's own right/up/forward and degrees about up, because the
    /// person pressing the button is thinking "the chair should be a bit further that way" while
    /// looking at the room, and a frame that turned with the player's head would make the same
    /// button mean a different direction every time they looked round.
    /// </summary>
    public interface IDebugNudgeable
    {
        /// <summary>Whether a correction can be applied to that machine right now.</summary>
        bool DebugCanNudge(DebugFireTarget target);

        /// <summary>
        /// Shift by <paramref name="roomMetres"/> in the marker's frame and turn by
        /// <paramref name="degrees"/> about up, on top of whatever calibration produced.
        /// </summary>
        void DebugNudge(DebugFireTarget target, Vector3 roomMetres, float degrees);

        /// <summary>Whether there is hand-correction to take back.</summary>
        bool DebugCanUndoNudge(DebugFireTarget target);

        /// <summary>Put it back to where calibration left it, in one step.</summary>
        void DebugUndoNudge(DebugFireTarget target);
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
        /// <summary>Command line switch that makes the Fire buttons live in a player build.</summary>
        public const string FireArgument = "-debuginteractions";

        static readonly List<IDebugInteraction> s_Entries = new();
        static readonly Dictionary<string, TaskGateInteraction> s_TaskGates = new();
        static bool? s_FireEnabled;

        /// <summary>Everything currently registered, in registration order.</summary>
        public static IReadOnlyList<IDebugInteraction> entries => s_Entries;

        /// <summary>Raised when a row appears or disappears, so a panel can rebuild itself.</summary>
        public static event Action onEntriesChanged;

        /// <summary>
        /// Whether the panel's buttons may actually do anything.
        ///
        /// THE LIST IS ALWAYS READABLE AND THE BUTTONS ARE NOT. A show that stalls in front of an
        /// audience is exactly when staff need to see which half of a gate is missing, so
        /// stripping this from the venue build would remove the tool at the moment it is worth
        /// having. Firing a gate by clicking a stray button during a performance, on the other
        /// hand, is a thing that must not be one misclick away -- so in a build the buttons need
        /// the switch above, and in the editor they are simply live.
        /// </summary>
        public static bool fireEnabled
        {
            get
            {
                s_FireEnabled ??= Application.isEditor || HasFireArgument();
                return s_FireEnabled.Value;
            }
        }

        static bool HasFireArgument()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, FireArgument, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Whether this exact interaction is already on the list.</summary>
        public static bool IsRegistered(IDebugInteraction interaction) =>
            interaction != null && s_Entries.Contains(interaction);

        public static void Register(IDebugInteraction interaction)
        {
            if (interaction == null || s_Entries.Contains(interaction)) return;
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
