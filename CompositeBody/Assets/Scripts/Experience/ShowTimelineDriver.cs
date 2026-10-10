using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CompositeBody.Multiplayer;
using XRMultiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Drives the master show timeline, and holds it at a <see cref="ShowHoldMarker"/> until the
    /// players have done what that beat asks of them.
    ///
    /// THE TIMELINE IS THE RUNNING ORDER. Main.playable nests each beat's own timeline as a
    /// Control clip, so the whole show is one scrubbable surface instead of a running order spread
    /// across a beat list and four prefabs.
    ///
    /// IT IS DRIVEN, NOT PLAYED, for the same reason every other director in this project is:
    /// a PlayableDirector left to play runs on the local machine's clock, and two people standing
    /// in the same room would reach the same cue seconds apart depending on when each headset
    /// started. Time here is computed from the shared clock instead.
    ///
    /// WHICH MAKES THE HOLD THE INTERESTING PART. A monotonic shared clock cannot be paused, so
    /// "show time" is kept as two server-owned numbers -- how much show has already been banked,
    /// and the server time it last resumed at -- and every client derives the same show time from
    /// them. That the server owns the decision is the whole point: if each headset decided locally
    /// when to stop and start again, they would resume on different frames and the show would
    /// drift apart at exactly the moment it matters most, when the two players have just finished
    /// doing something together.
    ///
    /// Falls back to this machine's own clock before the object is spawned, so a desk test and the
    /// editor's own preview still run the show through.
    /// </summary>
    [RequireComponent(typeof(PlayableDirector))]
    public class ShowTimelineDriver : NetworkBehaviour
    {
        /// <summary>The driver running the show, if there is one in the scene.</summary>
        public static ShowTimelineDriver Instance { get; private set; }

        /// <summary>
        /// True when a master timeline is driving the per-beat sub-timelines.
        ///
        /// A beat that also drives its own director -- <c>O0ArrivalBeat</c> does -- has to stand
        /// down while this is true, or the two of them write <c>director.time</c> on the same
        /// frame and whichever ran second wins. See the note in O0ArrivalBeat.ApplyCue.
        /// </summary>
        public static bool ownsSubTimelines => Instance != null && Instance.m_OwnSubTimelines;

        [SerializeField, Tooltip("Take ownership of each beat's own director. Leave on: the " +
                                 "Control clips in Main.playable are what nest the sub-timelines, " +
                                 "and a beat driving its own director as well would fight them.")]
        bool m_OwnSubTimelines = true;

        [SerializeField, Tooltip("Hold at gate markers even with no session, so the waiting can " +
                                 "be seen on a desk. Off by default: with no second player no " +
                                 "gate can ever clear, and the show would stop at the first one.")]
        bool m_HoldWithoutSession;

        [SerializeField, Tooltip("Log each hold and resume. Useful while authoring the running " +
                                 "order; noisy in the venue.")]
        bool m_LogTransitions = true;

        PlayableDirector m_Director;
        TimelineAsset m_Timeline;

        // Authored hold points, read once off the asset and kept sorted by time.
        readonly List<(double time, string taskId)> m_Holds = new();

        // Server-owned show time. Banked is how much show has run; ResumedAt is the server time
        // it last started running from that point. Held freezes the pair.
        readonly NetworkVariable<float> m_Banked =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<double> m_ResumedAt =
            new(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<bool> m_Held =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // The same three, for before the object is spawned -- or when it never is.
        float m_LocalBanked;
        float m_LocalResumedAt;
        bool m_LocalHeld;

        bool m_WarnedNoProgress;

        /// <summary>
        /// Whether the networked numbers are live. Reading a NetworkVariable before the object is
        /// spawned gives a default rather than an answer, so this picks which pair to trust.
        /// </summary>
        bool shared => IsSpawned;

        /// <summary>Seconds into the show, agreed by every machine in the session.</summary>
        public float showTime
        {
            get
            {
                if (shared)
                {
                    if (m_Held.Value) return m_Banked.Value;
                    return m_Banked.Value + (float)(ServerNow - m_ResumedAt.Value);
                }

                if (m_LocalHeld) return m_LocalBanked;
                return m_LocalBanked + (Time.time - m_LocalResumedAt);
            }
        }

        /// <summary>The gate the show is currently waiting on, or null when it is running.</summary>
        public string waitingOn
        {
            get
            {
                bool held = shared ? m_Held.Value : m_LocalHeld;
                if (!held) return null;
                return GateAt(shared ? m_Banked.Value : m_LocalBanked);
            }
        }

        static double ServerNow
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening ? nm.ServerTime.Time : Time.time;
            }
        }

        void Awake()
        {
            Instance = this;
            m_Director = GetComponent<PlayableDirector>();
            m_Timeline = m_Director != null ? m_Director.playableAsset as TimelineAsset : null;

            // Manual regardless of what the asset was saved with. A director that plays itself
            // would run the show on this machine's clock, which is the thing this class exists
            // to avoid, and a stale serialized value should not be able to reintroduce it.
            if (m_Director != null)
            {
                m_Director.playOnAwake = false;
                m_Director.timeUpdateMode = DirectorUpdateMode.Manual;
                m_Director.extrapolationMode = DirectorWrapMode.Hold;
            }

            CollectHolds();
            m_LocalResumedAt = Time.time;
        }

        public override void OnNetworkSpawn()
        {
            // Carry whatever the local fallback had already run across to the shared numbers, so
            // starting a session does not snap the show back to zero.
            if (IsServer)
            {
                m_Banked.Value = m_LocalBanked;
                m_Held.Value = m_LocalHeld;
                m_ResumedAt.Value = ServerNow;
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        /// <summary>
        /// Reads the hold markers off the timeline asset.
        ///
        /// Done once rather than per frame: the asset does not change while the show is running,
        /// and walking the marker track every frame on a Quest is not free.
        /// </summary>
        void CollectHolds()
        {
            m_Holds.Clear();
            if (m_Timeline == null || m_Timeline.markerTrack == null) return;

            foreach (IMarker marker in m_Timeline.markerTrack.GetMarkers())
            {
                if (marker is ShowHoldMarker hold && hold.gates)
                    m_Holds.Add((hold.time, hold.taskId));
            }

            m_Holds.Sort((a, b) => a.time.CompareTo(b.time));

            if (m_LogTransitions)
                Utils.Log($"[ShowTimeline] {m_Holds.Count} hold(s) on the running order.");
        }

        void Update()
        {
            if (m_Director == null) return;

            // Only the server moves the numbers; everyone else just reads them.
            if (shared)
            {
                if (IsServer) ServerStep();
            }
            else
            {
                LocalStep();
            }

            double t = Mathf.Max(0f, showTime);
            if (m_Timeline != null && m_Timeline.duration > 0d)
                t = System.Math.Min(t, m_Timeline.duration);

            m_Director.time = t;
            m_Director.Evaluate();
        }

        void ServerStep()
        {
            double now = ServerNow;

            if (m_Held.Value)
            {
                string gate = GateAt(m_Banked.Value);
                if (gate == null || IsClear(gate))
                {
                    m_Held.Value = false;
                    m_ResumedAt.Value = now;
                    if (m_LogTransitions)
                        Utils.Log($"[ShowTimeline] '{gate}' cleared; resuming at {m_Banked.Value:0.00}s.");
                }
                return;
            }

            float banked = m_Banked.Value;
            float candidate = banked + (float)(now - m_ResumedAt.Value);

            if (NextHold(banked, candidate, out double holdAt, out string taskId) && !IsClear(taskId))
            {
                m_Banked.Value = (float)holdAt;
                m_Held.Value = true;
                if (m_LogTransitions)
                    Utils.LogWarning($"[ShowTimeline] Holding at {holdAt:0.00}s for '{taskId}'.");
            }
        }

        /// <summary>The same step, on one machine's own clock, so a desk test runs the show.</summary>
        void LocalStep()
        {
            if (m_LocalHeld)
            {
                string gate = GateAt(m_LocalBanked);
                if (gate == null || IsClear(gate))
                {
                    m_LocalHeld = false;
                    m_LocalResumedAt = Time.time;
                }
                return;
            }

            float candidate = m_LocalBanked + (Time.time - m_LocalResumedAt);

            if (NextHold(m_LocalBanked, candidate, out double holdAt, out string taskId) && !IsClear(taskId))
            {
                m_LocalBanked = (float)holdAt;
                m_LocalHeld = true;
                if (m_LogTransitions)
                    Utils.LogWarning($"[ShowTimeline] Holding at {holdAt:0.00}s for '{taskId}' (local).");
            }
        }

        /// <summary>
        /// The first hold strictly after <paramref name="from"/> that <paramref name="to"/> would
        /// reach or pass.
        ///
        /// Strictly after, because a resume leaves the banked time sitting exactly on the marker
        /// it just cleared -- and a search that included it would hold there again forever.
        /// </summary>
        bool NextHold(float from, float to, out double holdAt, out string taskId)
        {
            const double k_Epsilon = 1e-4;

            for (int i = 0; i < m_Holds.Count; i++)
            {
                (double time, string id) = m_Holds[i];
                if (time <= from + k_Epsilon) continue;
                if (time > to) break;

                holdAt = time;
                taskId = id;
                return true;
            }

            holdAt = 0d;
            taskId = null;
            return false;
        }

        /// <summary>The gate authored at a given show time, or null if none is.</summary>
        string GateAt(float time)
        {
            const double k_Epsilon = 1e-3;
            for (int i = 0; i < m_Holds.Count; i++)
            {
                if (System.Math.Abs(m_Holds[i].time - time) <= k_Epsilon) return m_Holds[i].taskId;
            }
            return null;
        }

        /// <summary>Whether both players have reported this task done.</summary>
        bool IsClear(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return true;

            // No session to gate against. Running the show through is the useful behaviour while
            // authoring; stopping dead at the first marker is not.
            if (!shared && !m_HoldWithoutSession) return true;

            var progress = StoryProgressManager.Instance;
            if (progress == null)
            {
                if (!m_WarnedNoProgress)
                {
                    m_WarnedNoProgress = true;
                    Utils.LogWarning("[ShowTimeline] No StoryProgressManager; treating every gate " +
                                     "as already clear so the running order plays through.");
                }
                return true;
            }

            return progress.IsTaskComplete(taskId);
        }

        /// <summary>
        /// Puts the show at a given second. Staff scrubbing, and the only supported way to move
        /// the show by hand -- writing <c>director.time</c> directly would be overwritten next frame.
        /// </summary>
        public void ScrubTo(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);

            if (shared)
            {
                if (!IsServer) return;
                m_Banked.Value = seconds;
                m_Held.Value = false;
                m_ResumedAt.Value = ServerNow;
                return;
            }

            m_LocalBanked = seconds;
            m_LocalHeld = false;
            m_LocalResumedAt = Time.time;
        }
    }
}
