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
    /// IT PLAYS, AND IS PULLED ONTO THE SHARED CLOCK. This used to set <c>director.time</c> and
    /// call <c>Evaluate()</c> every frame, which is exact -- and silent. Timeline's audio clips
    /// are scheduled against the audio DSP clock by a graph that is RUNNING; a director that is
    /// only ever evaluated samples its tracks without ever playing a sample, so every AudioTrack
    /// in the show was authored into a timeline that could not sound. Since the cues are the
    /// piece (the script's own first section is 「聲音先於空間」), the director now plays.
    ///
    /// What the shared clock then does is correct it. <see cref="showTime"/> is still the only
    /// authority on where the show is, and every frame the director's own time is compared with
    /// it: a disagreement under <see cref="m_Deadband"/> is ignored, a larger one is absorbed by
    /// trimming playback speed by a couple of percent -- inaudible, and enough to pull back 20ms
    /// a second -- and only a real discontinuity (a hold clearing, a staff scrub, a late joiner)
    /// is corrected by jumping, which is audible and is meant to be: those ARE discontinuities.
    ///
    /// So the two headsets are no longer frame-identical; they are within a few tens of
    /// milliseconds and cannot accumulate drift. For two people in one room that is below the
    /// threshold of noticing, and it is the price of the cues existing at all.
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

        [SerializeField, Tooltip("Hold the show at 0 until staff press start, rather than " +
                                 "running the moment a session spawns. Off only to replay the " +
                                 "running order on a desk without the lobby.")]
        bool m_WaitForStart = true;

        [Header("Audio sync")]
        [SerializeField, Min(0.02f), Tooltip("Seconds of disagreement with the shared clock that " +
                                             "are corrected by a jump rather than by trimming " +
                                             "the speed. A jump is audible, so this wants to be " +
                                             "larger than any drift the trim can absorb.")]
        float m_HardResync = 0.25f;

        [SerializeField, Range(0f, 0.2f), Tooltip("How much the playback speed may be trimmed to " +
                                                  "pull the director back onto the shared clock. " +
                                                  "0.02 is 2%, about 35 cents of pitch -- " +
                                                  "inaudible, and enough to absorb 20ms per " +
                                                  "second. Large values bend the pitch audibly.")]
        float m_SpeedTrim = 0.02f;

        [SerializeField, Min(0.001f), Tooltip("Disagreement below this is left alone, so the " +
                                              "speed is not trimmed every frame against ordinary " +
                                              "frame-time noise.")]
        float m_Deadband = 0.012f;

        PlayableDirector m_Director;
        TimelineAsset m_Timeline;

        // Authored hold points, read once off the asset and kept sorted by time.
        readonly List<(double time, string taskId)> m_Holds = new();

        // The running order itself: one entry per Control clip, read once off the asset and kept
        // sorted by start time.
        readonly List<(double start, double end, string code)> m_Scenes = new();

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

        // Latched once the three above are found to have no NetworkBehaviour; see CanWrite.
        bool m_Unbound;

        bool m_WarnedNoProgress;

        float m_Drift;

        // The speed last written to the graph's root. What is already there matters: writing the
        // same value again is free, and writing a different one is not. See SetSpeed.
        double m_Speed = 1d;

        // Latched while the trim is pulling, so the correction is not switched on and off again
        // as the error crosses the deadband.
        bool m_Trimming;

        // The steps the trim is rounded to. A trim is audible through how often it changes rather
        // than through its size, so it is allowed only a few values.
        const double k_TrimStep = 0.005d;

        /// <summary>
        /// How far the director is behind (negative: ahead of) the shared clock, in seconds. For
        /// the debug panel: this is the one number that says whether the audio sync is working,
        /// and without it a slow drift is invisible until a cue lands in the wrong place.
        /// </summary>
        public float driftSeconds => m_Drift;

        /// <summary>Whether the show's director is actually playing, which is what makes it sound.</summary>
        public bool isPlaying => m_Director != null && m_Director.state == PlayState.Playing;

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

            // UNSCALED, and not Manual any more: a manually evaluated graph never advances the
            // audio clock, so the cues would not sound (see the class remarks). Unscaled rather
            // than GameTime because audio runs on real time whatever Time.timeScale is doing,
            // and a timeline whose video half slowed down while its audio half did not would be
            // worse than either.
            //
            // playOnAwake stays off: the show starts when staff start it, and Play() is called
            // from Apply once there is a reason to.
            if (m_Director != null)
            {
                m_Director.playOnAwake = false;
                m_Director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                m_Director.extrapolationMode = DirectorWrapMode.Hold;
            }

            CollectHolds();
            CollectScenes();
            m_LocalResumedAt = Time.time;
        }

        public override void OnNetworkSpawn()
        {
            // A fresh spawn binds the variables again, so a driver that stood down in a previous
            // session is allowed to drive -- and to complain again if it happens twice.
            m_Unbound = false;

            // Carry whatever the local fallback had already run across to the shared numbers, so
            // starting a session does not snap the show back to zero.
            if (IsServer)
            {
                bool started = experienceStarted;

                m_Banked.Value = started ? m_LocalBanked : 0f;
                m_Held.Value = started ? m_LocalHeld : true;
                m_ResumedAt.Value = ServerNow;
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        /// <summary>
        /// Reads the running order off the timeline's Control tracks: one clip per scene, each
        /// track named with the script's own code and title.
        ///
        /// WHY READ IT HERE RATHER THAN KEEP A LIST IN CODE. The running order already lives in
        /// this asset -- that is the point of nesting each scene as a Control clip -- and a second
        /// copy in C# is a second thing to keep in step. That is not hypothetical: the beat list
        /// this class was written beside fell behind this timeline, and anything reading the beat
        /// list was describing a show that is no longer the one playing. Adding a scene is adding
        /// a track, and the numbering follows it.
        ///
        /// Sorted by start time, not by track order: the track list is an authoring convenience
        /// and can be dragged around without changing when anything actually plays.
        /// </summary>
        void CollectScenes()
        {
            m_Scenes.Clear();
            if (m_Timeline == null) return;

            foreach (TrackAsset track in m_Timeline.GetOutputTracks())
            {
                if (track is not ControlTrack) continue;

                foreach (TimelineClip clip in track.GetClips())
                    m_Scenes.Add((clip.start, clip.end, CodeOf(track.name)));
            }

            m_Scenes.Sort((a, b) => a.start.CompareTo(b.start));

            if (m_LogTransitions)
                Utils.Log($"[ShowTimeline] {m_Scenes.Count} scene(s) on the running order.");
        }

        /// <summary>
        /// 'S1-2 吊掛的家' becomes 'S1-2'. The track's name is the label the script uses, and the
        /// code is everything up to the first space.
        ///
        /// The code half is ASCII, which matters because it leaves the machine over OSC: the
        /// title after it is CJK and would arrive as question marks, so only the code is sent.
        /// </summary>
        static string CodeOf(string trackName)
        {
            if (string.IsNullOrWhiteSpace(trackName)) return string.Empty;

            string trimmed = trackName.Trim();
            int space = trimmed.IndexOf(' ');
            return space < 0 ? trimmed : trimmed.Substring(0, space);
        }

        /// <summary>Scenes on the running order.</summary>
        public int sceneCount => m_Scenes.Count;

        /// <summary>
        /// 1-based position of the scene playing now, or 0 when the clock is not inside any of
        /// them -- before the first one, or past the end.
        /// </summary>
        public int sceneNumber => SceneIndexAt(showTime) + 1;

        /// <summary>
        /// When a scene runs, by its code -- 'O-0', 'O-1', 'S2-3'.
        ///
        /// EXISTS SO NOBODY KEEPS A SECOND COPY OF THE RUNNING ORDER. O1PayoffTestRunner did,
        /// as four serialized seconds-into-the-show fields, and when O-1 grew from 90 seconds to
        /// 170 every one of them pointed into the wrong part of the piece: the test scrubbed to
        /// 218s looking for a hold that is now at 301s, waited a minute and reported eleven
        /// failures that were all the same stale number. The table is on the timeline asset and
        /// this is how to read it.
        /// </summary>
        public bool TryGetScene(string code, out float start, out float end)
        {
            start = 0f;
            end = 0f;
            if (string.IsNullOrWhiteSpace(code)) return false;

            foreach ((double from, double to, string id) in m_Scenes)
            {
                if (!string.Equals(id, code, System.StringComparison.OrdinalIgnoreCase)) continue;
                start = (float)from;
                end = (float)to;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Where the show stops for a gate, by task id, or false when no marker gates it. The
        /// other half of <see cref="TryGetScene"/>: a hold is authored as a marker and moves
        /// with the beat it belongs to.
        /// </summary>
        public bool TryGetHold(string taskId, out float at)
        {
            at = 0f;
            if (string.IsNullOrWhiteSpace(taskId)) return false;

            foreach ((double time, string id) in m_Holds)
            {
                if (id != taskId) continue;
                at = (float)time;
                return true;
            }
            return false;
        }

        /// <summary>The script's code for the scene playing now ('O-0', 'S2-3'), or empty.</summary>
        public string sceneCode
        {
            get
            {
                int index = SceneIndexAt(showTime);
                return index < 0 ? string.Empty : m_Scenes[index].code;
            }
        }

        /// <summary>
        /// A linear scan, which is the right shape here: the running order is a couple of dozen
        /// entries, and a scan over a sorted list that short beats a binary search's branches in
        /// practice while staying obvious.
        /// </summary>
        int SceneIndexAt(float time)
        {
            for (int i = 0; i < m_Scenes.Count; i++)
            {
                if (time >= m_Scenes[i].start && time < m_Scenes[i].end) return i;
            }
            return -1;
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
                if (IsServer && CanWrite()) ServerStep();
            }
            else
            {
                LocalStep();
            }

            double t = Mathf.Max(0f, showTime);
            if (m_Timeline != null && m_Timeline.duration > 0d)
                t = System.Math.Min(t, m_Timeline.duration);

            Apply(t, shared ? m_Held.Value : m_LocalHeld);
        }

        /// <summary>
        /// Put the director where the shared clock says, playing.
        ///
        /// HELD IS A PAUSE, NOT A FROZEN EVALUATION. Pausing stops the audio with the picture,
        /// which is what a hold is: the show is waiting for the players, and a cue that kept
        /// sounding into the wait would be telling them the show had moved on. The frame is still
        /// evaluated while paused, so the held moment stays on screen and a late joiner lands on
        /// it.
        /// </summary>
        void Apply(double target, bool held)
        {
            if (held)
            {
                if (m_Director.state == PlayState.Playing) m_Director.Pause();

                SetSpeed(1d);
                m_Director.time = target;
                m_Director.Evaluate();      // keep the held frame applied
                m_Drift = 0f;
                m_Trimming = false;
                return;
            }

            if (m_Director.state != PlayState.Playing)
            {
                // Time BEFORE Play: a director that is stopped rather than paused starts from
                // its initial time, so playing first would run the opening of the show at
                // whatever point the clock is actually at.
                m_Director.time = target;
                m_Director.Play();

                // A graph that has just been played is already running at 1, and a speed that is
                // written when it did not change costs the audio a seek. See SetSpeed.
                m_Speed = 1d;
                m_Drift = 0f;
                m_Trimming = false;
                return;
            }

            // THE DIRECTOR IS A FRAME BEHIND WHERE THIS READS IT. PlayableDirector advances in
            // PreLateUpdate, after every Update, so director.time here is the time last frame was
            // drawn at while target is the clock now. Subtracting the two as they stand reports a
            // whole frame as drift before any real drift exists -- 13.9ms on a 72Hz Link run,
            // over the deadband on its own, which left the trim below running permanently. A trim
            // that moves every frame is what shredded O-2's line (see SetSpeed). Compare against
            // the evaluation that is about to happen instead.
            double predicted = m_Director.time + Time.unscaledDeltaTime * m_Speed;
            m_Drift = (float)(target - predicted);
            float error = Mathf.Abs(m_Drift);

            if (error > m_HardResync)
            {
                // A jump. Only a real discontinuity gets this far, and the audible seam is
                // honest -- the alternative is a cue that finishes seconds late.
                if (m_LogTransitions)
                    Utils.LogWarning($"[ShowTimeline] resyncing by {m_Drift:+0.00;-0.00}s " +
                                     $"(at {m_Director.time:0.00}s, shared clock says {target:0.00}s).");

                m_Director.time = target;
                m_Director.Evaluate();
                SetSpeed(1d);
                m_Trimming = false;
                return;
            }

            // ONE FRAME OF JITTER IS NOT DRIFT. Link's frame times alternate as it reprojects,
            // and a deadband inside that noise has the trim chasing the frame clock instead of
            // the show clock, so the floor is two frames however the field is authored. Latched
            // on the way out, so the correction does not switch off and on either side of it.
            float deadband = Mathf.Max(m_Deadband, 2f * Time.unscaledDeltaTime);

            if (error <= (m_Trimming ? 0.5f * deadband : deadband))
            {
                SetSpeed(1d);
                m_Trimming = false;
                return;
            }

            m_Trimming = true;

            // Proportional, because the trim has to fall off as the error closes or the director
            // overshoots and the pitch audibly hunts. Full trim at the hard-resync threshold, and
            // rounded to steps so the speed holds one value for seconds rather than being a new
            // number on every frame.
            double trim = m_SpeedTrim * Mathf.Clamp(m_Drift / m_HardResync, -1f, 1f);
            SetSpeed(1d + System.Math.Round(trim / k_TrimStep) * k_TrimStep);
        }

        /// <summary>
        /// Set the speed of the show, and with it every sub-timeline the Control clips nest inside
        /// it.
        ///
        /// A SPEED THAT CHANGES RE-SEEKS EVERY LINE UNDER THIS GRAPH. A Control clip compares each
        /// frame's advance against deltaTime * effectiveSpeed and calls anything more than a
        /// picosecond apart a discontinuity (DirectorControlPlayable.DetectDiscontinuity), which
        /// re-evaluates the nested director -- and an Evaluate is precisely what makes a clip
        /// re-seek its voice (ScheduleRuntimeClip.EvaluateAt -> AudioClipPlayable.Seek). So a trim
        /// that is a new number every frame restarts the voice-over 72 times a second, which is
        /// what O-2's line came back from the headset sounding like. Writing the same value again
        /// is free, so only a real change is allowed through.
        /// </summary>
        void SetSpeed(double speed)
        {
            if (speed == m_Speed) return;
            if (!m_Director.playableGraph.IsValid()) return;
            if (m_Director.playableGraph.GetRootPlayableCount() == 0) return;

            Playable root = m_Director.playableGraph.GetRootPlayable(0);
            if (!root.IsValid()) return;

            root.SetSpeed(speed);
            m_Speed = speed;
        }

        /// <summary>
        /// Whether staff have started the show. With no session manager at all -- a desk test --
        /// this is true, so a single machine still runs the show through.
        /// </summary>
        bool experienceStarted
        {
            get
            {
                if (!m_WaitForStart || m_Begun) return true;

                // No session at all -- a solo editor test -- is NOT treated as started. An
                // earlier version did, which meant pressing Play in the editor still played the
                // whole running order to itself. Use Begin() for that instead, so "has the show
                // started" has one answer rather than one per code path.
                GameSessionManager session = GameSessionManager.Instance;
                return session != null && session.sessionState != SessionState.Lobby;
            }
        }

        bool m_Begun;

        /// <summary>
        /// Start the show without a session, for a desk test. The staff panel does not need this
        /// -- StartExperience moves the session out of Lobby and the driver follows that.
        /// </summary>
        [ContextMenu("Begin Show")]
        public void Begin()
        {
            m_Begun = true;
            m_LocalResumedAt = Time.time;
            if (IsSpawned && IsServer && CanWrite()) m_ResumedAt.Value = ServerNow;
            Utils.Log("[ShowTimeline] begun manually.");
        }

        /// <summary>
        /// Whether the shared numbers can actually be written to.
        ///
        /// A NetworkVariable IS NOT ALWAYS BOUND WHEN IsSpawned SAYS SO. Netcode hands each
        /// variable its NetworkBehaviour when the object spawns and takes it away again when the
        /// object goes, and there are frames either side -- a host shutting down, a spawn that
        /// did not finish -- where <see cref="shared"/> reads true and the variables have
        /// nobody. Every write in that window logs a warning AND a null reference, each with a
        /// full stack trace, EVERY FRAME. A batch run that hit it wrote 4.7 GB of log and
        /// crawled to a stop; the test it was running never finished.
        ///
        /// So the server stops driving the clock at the first frame it cannot, and says so once.
        /// Reading is left alone: an unbound variable still reports what it last held, which is
        /// exactly what every client in the session is doing anyway.
        /// </summary>
        bool CanWrite()
        {
            if (m_Banked.GetBehaviour() != null) return true;

            if (!m_Unbound)
            {
                m_Unbound = true;
                Utils.LogError("[ShowTimeline] the shared clock's NetworkVariables have lost " +
                               "their NetworkBehaviour -- the object is despawning, or never " +
                               "finished spawning. The server has stopped driving the clock; " +
                               "the show holds at whatever the numbers last read.");
            }

            return false;
        }

        void ServerStep()
        {
            double now = ServerNow;

            // THE SHOW DOES NOT BEGIN UNTIL STAFF PRESS START. Before that it is pinned at zero
            // rather than merely "held", because the hold mechanism is keyed to authored gate
            // markers: at time zero GateAt returns null, and the resume path below reads a null
            // gate as one that has been cleared. Setting m_Held alone would therefore unhold
            // itself on the very next step, which is exactly how the show came to start on its
            // own the moment the session spawned.
            if (!experienceStarted)
            {
                if (!m_Held.Value || m_Banked.Value != 0f)
                {
                    m_Banked.Value = 0f;
                    m_Held.Value = true;
                    if (m_LogTransitions)
                        Utils.Log("[ShowTimeline] waiting at 0.00s for staff to start.");
                }

                // Kept level with now, so the first frame after the start button does not credit
                // the show with all the time spent sitting in the lobby.
                m_ResumedAt.Value = now;
                return;
            }

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
            // The same pre-start hold as ServerStep. Missing here is why pressing Play in the
            // editor still ran the show: with no session `shared` is false, so ServerStep -- and
            // the gate that had been added only to it -- never ran at all.
            if (!experienceStarted)
            {
                m_LocalBanked = 0f;
                m_LocalHeld = true;
                m_LocalResumedAt = Time.time;
                return;
            }

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
