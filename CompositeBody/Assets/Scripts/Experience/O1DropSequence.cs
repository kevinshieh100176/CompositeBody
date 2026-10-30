using System;
using System.Collections.Generic;
using UnityEngine;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// O-1 鬼的控制, end to end: each player puts their objects into their box, one line of
    /// speech answers each one, and the beat is over when both of them have finished AND the
    /// last line has stopped sounding.
    ///
    /// THE GATE MOVED UP HERE FROM THE BOX. <see cref="O1DropBox"/> used to report
    /// beat.O1_Control the moment something landed in it, which was right while there was one
    /// object each. With several per player, and with a line of speech owed to every one of them,
    /// "this player is done" is a count and a silent speaker -- neither of which a single box can
    /// see. So the boxes report to this, and this reports to the show.
    ///
    /// ONE SPEAKER, ONE QUEUE, NEVER TWO AT ONCE. 「如果前一個音檔還沒播完下一個物件就被放入 音檔不會
    /// 同時撥放 而是等候前一個音檔撥放完後接著撥放」. Two people putting things away at the same time
    /// is the normal case, not the edge case, so the clips are queued rather than fired: a drop
    /// that lands mid-line adds its line to the back and the player hears it when the room is
    /// quiet again. The order is the order the clips are LISTED in, not the order the objects
    /// were dropped -- these are sentences, and they only make sense in the order they were
    /// written.
    ///
    /// LOCAL, LIKE THE REST OF THE BEAT. Neither player can see the other's objects, so neither
    /// can hear the other's lines; each machine runs the side it owns and reports its own half of
    /// the gate, which <c>StoryProgressManager</c> attributes to whoever sent it. With no role
    /// assigned -- a desk test -- both sides run here, through the one queue, which is the only
    /// way to walk the beat alone on one machine.
    ///
    /// THE DISSOLVE IS NOT HERE. The objects and the boxes arriving and leaving are authored on
    /// O-1's own timeline as <see cref="PropPresence"/> curves; the show holds a few seconds
    /// before the end of the beat, so the dissolve-out is simply the tail of the clip that plays
    /// once this gate clears.
    /// </summary>
    [DisallowMultipleComponent]
    public class O1DropSequence : MonoBehaviour, IDebugInteraction
    {
        /// <summary>One player's objects and the lines they are owed.</summary>
        [Serializable]
        public class Side
        {
            [Tooltip("Whose objects these are.")]
            public PlayerRole role = PlayerRole.Player1;

            [Tooltip("Every box this player has to fill. The beat waits for all of them.")]
            public O1DropBox[] boxes = new O1DropBox[0];

            [Tooltip("What is said, IN ORDER, as the objects go in. One per object; a shorter " +
                     "list simply runs out and the later drops are silent.")]
            public AudioClip[] clips = new AudioClip[0];

            [HideInInspector] public int filled;
            [HideInInspector] public int spoken;

            /// <summary>Boxes that are actually wired, so a hole in the list is not a hostage.</summary>
            public int wired
            {
                get
                {
                    int n = 0;
                    foreach (O1DropBox box in boxes)
                    {
                        if (box != null) n++;
                    }
                    return n;
                }
            }

            public bool done => filled >= wired;
        }

        [SerializeField, Tooltip("One entry per player.")]
        Side[] m_Sides = new Side[0];

        [SerializeField, Tooltip("Plays the lines. Made on this object if left empty. 2D: these " +
                                 "are the room speaking, not a thing in it.")]
        AudioSource m_Source;

        [SerializeField, Min(0f), Tooltip("Silence between one line and the next. A queue that " +
                                          "runs the clips end to end sounds like one long file.")]
        float m_Gap = 0.6f;

        [SerializeField, Tooltip("Task reported once this player has filled every box and the " +
                                 "last line has finished. The show's hold near the end of O-1 " +
                                 "waits on it, from both players.")]
        string m_TaskId = "beat.O1_Control";

        [SerializeField, Min(0f), Tooltip("Seconds the show may sit on this gate before this " +
                                          "player's half is reported anyway. 0 disables it. The " +
                                          "production timeline asks for a fallback on every " +
                                          "gate so one player cannot strand the other.")]
        float m_Timeout = 180f;

        readonly Queue<AudioClip> m_Queue = new();

        bool m_Reported;
        bool m_TimedOut;
        float m_ReportedAt = -1f;
        float m_WaitingSince = -1f;
        float m_QuietUntil;

        /// <summary>Whether this machine's player has finished and been reported.</summary>
        public bool reported => m_Reported;

        /// <summary>Lines still waiting to be heard, plus the one sounding now.</summary>
        public int queued => m_Queue.Count + (speaking ? 1 : 0);

        /// <summary>Whether a line is sounding right now.</summary>
        public bool speaking => m_Source != null && m_Source.isPlaying;

        void Awake()
        {
            Resolve();

            foreach (Side side in m_Sides)
            {
                if (side == null) continue;
                foreach (O1DropBox box in side.boxes)
                {
                    if (box != null) box.onDeposited += HandleDeposited;
                }
            }

            // Awake, not OnEnable, so a beat that is switched off still has a debug row rather
            // than looking like an interaction nobody built.
            InteractionDebug.Register(this);

            if (string.IsNullOrWhiteSpace(m_TaskId)) return;

            TaskGateInteraction gate = InteractionDebug.RegisterTaskGate(
                m_TaskId, "O-1 -- both players fill every box, and the lines finish");

            if (gate != null) gate.timeoutSource = () => secondsUntilTimeout;
        }

        void OnDestroy()
        {
            foreach (Side side in m_Sides)
            {
                if (side == null) continue;
                foreach (O1DropBox box in side.boxes)
                {
                    if (box != null) box.onDeposited -= HandleDeposited;
                }
            }

            InteractionDebug.Unregister(this);
        }

        void Resolve()
        {
            if (m_Source == null) m_Source = GetComponent<AudioSource>();
            if (m_Source == null)
            {
                m_Source = gameObject.AddComponent<AudioSource>();
                m_Source.playOnAwake = false;
                m_Source.loop = false;
                m_Source.spatialBlend = 0f;
            }

            if (m_Sides.Length == 0)
                Debug.LogWarning($"[O1Sequence] '{name}' has no sides wired, so nothing can ever " +
                                 "fill it and only its fallback can clear the gate.", this);
        }

        /// <summary>
        /// Seconds before this player's half is reported whether they finished or not, or
        /// negative when no clock is running. Counted from the show ARRIVING AT THIS GATE, not
        /// from the beat starting: the players cannot be late for something that has not asked
        /// them for anything yet, and O-1 is ninety seconds long.
        /// </summary>
        public float secondsUntilTimeout
        {
            get
            {
                if (m_Timeout <= 0f || m_Reported || m_WaitingSince < 0f) return -1f;
                return Mathf.Max(m_Timeout - (ExperienceClock.now - m_WaitingSince), 0f);
            }
        }

        /// <summary>
        /// The sides this machine is responsible for. With a role assigned that is one of them;
        /// on a desk test with no session it is all of them, so the beat can be walked alone.
        /// </summary>
        bool Mine(Side side)
        {
            PlayerRole local = InteractionDebug.LocalRole();
            return local == PlayerRole.Unassigned || local == side.role;
        }

        void HandleDeposited(O1DropBox box)
        {
            foreach (Side side in m_Sides)
            {
                if (side == null || !Owns(side, box)) continue;

                side.filled++;

                // 「按順序」: the next line in the list, not the one belonging to this box. They
                // are sentences, and the order they were written in is the only one that reads.
                if (side.spoken < side.clips.Length)
                {
                    AudioClip clip = side.clips[side.spoken++];
                    if (clip != null) m_Queue.Enqueue(clip);
                }

                Debug.Log($"[O1Sequence] {side.role}: {side.filled}/{side.wired} in the box, " +
                          $"{queued} line(s) to hear.", this);
                return;
            }
        }

        static bool Owns(Side side, O1DropBox box)
        {
            foreach (O1DropBox candidate in side.boxes)
            {
                if (candidate == box) return true;
            }
            return false;
        }

        void Update()
        {
            DriveQueue();

            if (m_Reported) return;

            if (Finished()) Report(false);
            else if (m_Timeout > 0f) CountDown();
        }

        /// <summary>
        /// One line at a time. The gap is measured from the moment the speaker fell silent, so a
        /// drop that lands during a line is heard a beat after it rather than on its last frame.
        /// </summary>
        void DriveQueue()
        {
            if (m_Source == null || m_Source.isPlaying)
            {
                if (m_Source != null && m_Source.isPlaying)
                    m_QuietUntil = ExperienceClock.now + m_Gap;
                return;
            }

            if (m_Queue.Count == 0) return;
            if (ExperienceClock.now < m_QuietUntil) return;

            AudioClip next = m_Queue.Dequeue();
            m_Source.clip = next;
            m_Source.Play();
            m_QuietUntil = ExperienceClock.now + m_Gap;

            Debug.Log($"[O1Sequence] playing '{next.name}' ({next.length:F1}s); " +
                      $"{m_Queue.Count} still queued.", this);
        }

        /// <summary>
        /// 「當兩個玩家都把所有該擺放的物件都放完 音檔也都撥放完畢後」 -- but only this machine's half of
        /// it. The other player's half is reported from their own machine, and the show's hold
        /// is what waits for both.
        /// </summary>
        bool Finished()
        {
            bool any = false;

            foreach (Side side in m_Sides)
            {
                if (side == null || !Mine(side)) continue;
                any = true;
                if (!side.done) return false;
            }

            if (!any) return false;
            return m_Queue.Count == 0 && !speaking;
        }

        void CountDown()
        {
            ShowTimelineDriver driver = ShowTimelineDriver.Instance;
            bool waiting = driver != null && driver.waitingOn == m_TaskId;
            if (!waiting)
            {
                m_WaitingSince = -1f;
                return;
            }

            if (m_WaitingSince < 0f) m_WaitingSince = ExperienceClock.now;
            if (ExperienceClock.now - m_WaitingSince < m_Timeout) return;

            Debug.LogWarning($"[O1Sequence] still unfinished {m_Timeout:F0}s after the show " +
                             "reached this gate; reporting anyway so the other player is not " +
                             "left waiting.", this);
            Report(true);
        }

        void Report(bool timedOut)
        {
            if (m_Reported) return;
            m_Reported = true;
            m_TimedOut = timedOut;
            m_ReportedAt = ExperienceClock.now;

            if (string.IsNullOrWhiteSpace(m_TaskId)) return;

            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress == null)
            {
                Debug.LogWarning($"[O1Sequence] no StoryProgressManager; '{m_TaskId}' not " +
                                 "reported, so the show will not come off its hold.", this);
                return;
            }

            progress.ReportTaskCompleteRpc(m_TaskId);
            Debug.Log($"[O1Sequence] reported '{m_TaskId}' at {m_ReportedAt:0.00}s" +
                      (timedOut ? " BY FALLBACK" : "") + ".", this);
        }

        #region Debug panel

        // One row for the whole beat, on this machine. The boxes keep their own rows -- which
        // object is still on the floor is a different question from whether the beat is over.
        public string debugId => "o1.sequence";

        // ASCII, because the panel's font has no CJK glyphs; see AvatarFormationSync.debugLabel.
        public string debugLabel => "O-1 -- fill the boxes, hear the lines";

        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        public string debugFireLabel => "Finish";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = m_Reported;
            state.secondsToTimeout = secondsUntilTimeout;

            if (m_Reported)
            {
                state.detail = m_TimedOut
                    ? $"UNFINISHED -- reported by the fallback at {m_ReportedAt:0.0}s"
                    : $"finished at {m_ReportedAt:0.0}s";
                return state;
            }

            if (!isActiveAndEnabled)
            {
                state.detail = "inactive -- its beat is not running";
                return state;
            }

            // Counted over the sides this machine actually owns, so a row reading "0/2" on a
            // machine that can only ever fill one of them does not look like a fault.
            int filled = 0, wired = 0;
            foreach (Side side in m_Sides)
            {
                if (side == null || !Mine(side)) continue;
                filled += side.filled;
                wired += side.wired;
            }

            string lines = speaking
                ? $", speaking ({m_Queue.Count} queued)"
                : m_Queue.Count > 0 ? $", {m_Queue.Count} line(s) queued" : "";

            state.detail = wired == 0 ? "no boxes wired" : $"{filled}/{wired} in the box{lines}";
            return state;
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            if (m_Reported) return false;

            if (target == DebugFireTarget.Local) return isActiveAndEnabled;
            if (target == DebugFireTarget.Both) return InteractionDebugRelay.canForce;

            PlayerRole role = target == DebugFireTarget.Player1
                ? PlayerRole.Player1
                : PlayerRole.Player2;

            return InteractionDebugRelay.canForce &&
                   InteractionDebugRelay.TryGetClientForRole(role, out _);
        }

        /// <summary>
        /// Report this machine's half without waiting for the objects or the lines. Staff rescue
        /// -- NOT a shortcut that fakes the drops: the boxes keep their own buttons for that, and
        /// firing one of those plays the line it owes, which this deliberately does not.
        /// </summary>
        public void DebugFire(DebugFireTarget target)
        {
            if (!DebugCanFire(target)) return;

            if (target == DebugFireTarget.Local)
            {
                Report(false);
                return;
            }

            if (target != DebugFireTarget.Player2) FireForRole(PlayerRole.Player1);
            if (target != DebugFireTarget.Player1) FireForRole(PlayerRole.Player2);
        }

        static void FireForRole(PlayerRole role)
        {
            if (InteractionDebugRelay.TryGetClientForRole(role, out ulong clientId))
                InteractionDebugRelay.Instance?.FireOnClient(clientId, "o1.sequence");
        }

        /// <summary>
        /// Put the beat back so it can be rehearsed again: the counts, the queue and the
        /// speaker. Where the objects are lying is each box's own row to reset, and the gate flag
        /// it reported belongs to the task.
        /// </summary>
        public bool DebugCanReset() => m_Reported || queued > 0;

        public void DebugReset()
        {
            m_Reported = false;
            m_TimedOut = false;
            m_ReportedAt = -1f;
            m_WaitingSince = -1f;
            m_QuietUntil = 0f;
            m_Queue.Clear();

            if (m_Source != null && m_Source.isPlaying) m_Source.Stop();

            foreach (Side side in m_Sides)
            {
                if (side == null) continue;
                side.filled = 0;
                side.spoken = 0;
            }
        }

        #endregion
    }
}
