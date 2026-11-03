using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// S2-2 碎片：燈 -- a prop cut in half that the two players put back together, and what
    /// happens around that: 「當兩位玩家都有抓到碎片的時候會先放一段VO，當兩個碎片靠近到某個距離內的時候
    /// 兩個碎片會自己拼在一起，然後跑到 playspace 正中央」.
    ///
    /// <see cref="CompositeHalf"/> already owns the mechanic -- who may touch which half, the
    /// server's merge decision, the weld itself. This owns the BEAT around it, which is three
    /// facts the halves have no business knowing: whether the fragments are in the room at all,
    /// the line that answers both players having picked one up, and where the finished object
    /// goes.
    ///
    /// ONE COMPONENT FOR THE THREE, not three beside each other, because they are one sequence
    /// and because what staff need in the panel is ONE ROW saying where the beat has got to:
    /// dormant, waiting for a grab, speaking, waiting for the halves to meet, travelling, done.
    /// Split across three rows, that reading has to be reassembled by eye from a panel on
    /// another machine -- which is exactly when nobody has time to do it.
    ///
    /// THE FRAGMENTS ARE IN THE ROOM FROM THE FIRST FRAME, which is not a choice. They are
    /// networked objects, and Netcode spawns in-scene NetworkObjects once, during
    /// synchronization, from what is ACTIVE in the scene -- a pair sitting under a beat root the
    /// show activates twenty minutes in would never be spawned at all, and the server's merge
    /// decision would have nothing to make itself on. So they live under a root that is always
    /// active and this component holds them dormant instead: renderers off, grabs off, until
    /// <see cref="m_BeatRoot"/> comes up. That root is the beat's own object, activated by the
    /// Control clip on Main.playable, so the arming is the show's own cue rather than a second
    /// timetable to keep in step with the first.
    ///
    /// THE LINE COMES OFF THE SERVER'S GATE, not off this machine's two latches, for the reason
    /// <see cref="O1GrabWaves"/> explains at length: 「都有抓過」 is one fact about two machines,
    /// and a line each headset starts when its own news arrives starts a round trip apart on the
    /// two of them. <see cref="StoryProgressManager"/> raises its unlock on every machine, so
    /// the line lands on both at once. The latches are the fallback for a desk test, where there
    /// is no server to agree and nobody to wait for.
    ///
    /// AND THE JOURNEY IS DRIVEN BY ONE MACHINE. After the weld the server owns both halves'
    /// NetworkObjects -- <see cref="CompositeHalf"/> takes ownership back precisely so it can
    /// move them -- so the server glides the anchor and the follower rides along as its child.
    /// Every other machine simply watches the replicated pose. With no session at all, this
    /// machine is the only one there is and drives it itself.
    /// </summary>
    [DisallowMultipleComponent]
    public class CompositeFragmentPair : MonoBehaviour, IDebugInteraction
    {
        [Header("The pair")]
        [SerializeField, Tooltip("The half that stays put during the weld, and the one this " +
                                 "component glides to the centre afterwards.")]
        CompositeHalf m_Anchor;

        [SerializeField, Tooltip("The other half. Only read from here -- the weld is the " +
                                 "anchor's business.")]
        CompositeHalf m_Follower;

        [SerializeField, Tooltip("The beat's own object, which the show activates for the " +
                                 "duration of the beat. The fragments are dormant -- invisible " +
                                 "and ungrabbable -- whenever it is off. Leave empty for a test " +
                                 "scene where they should simply be there.")]
        GameObject m_BeatRoot;

        [Header("The line")]
        [SerializeField, Tooltip("Played once, on both headsets, when both players have picked " +
                                 "their own half up. 「會先放一段VO」.")]
        AudioClip m_Line;

        [SerializeField, Tooltip("Plays the line. Made on this object if left empty.")]
        AudioSource m_Source;

        [Header("Going to the middle")]
        [SerializeField, Tooltip("Where the assembled object goes. Empty means the centre of the " +
                                 "playspace, which is what the beat asks for -- and which is " +
                                 "read live, so a room that is still moving carries the " +
                                 "destination with it.")]
        Transform m_Destination;

        [SerializeField, Min(0f), Tooltip("Metres above the playspace floor to arrive at. " +
                                          "Between two standing people rather than at their " +
                                          "feet: the assembled object is the thing they are " +
                                          "meant to look at, and a prop on the floor in the " +
                                          "middle of a dark room is a trip hazard as well as a " +
                                          "bad image.")]
        float m_Height = 1.2f;

        [SerializeField, Min(0f), Tooltip("A beat to let the weld register before it moves. " +
                                          "Snapping and leaving in the same frame reads as the " +
                                          "object having been taken away.")]
        float m_Delay = 0.8f;

        [SerializeField, Min(0.05f), Tooltip("How long the journey takes.")]
        float m_Travel = 4f;

        [SerializeField, Tooltip("Stand it up on the way, keeping whatever way it was facing. A " +
                                 "lamp arriving at the angle two players happened to be holding " +
                                 "it does not read as the object being presented.")]
        bool m_StandUpright = true;

        [Header("Debug row")]
        [SerializeField, Tooltip("Unique across the show. ASCII -- the panel's font has no CJK " +
                                 "glyphs.")]
        string m_DebugId = "s2_2.fragments";

        [SerializeField, Tooltip("Row heading. ASCII, for the same reason.")]
        string m_DebugLabel = "S2-2 lamp -- both grab, a line, then it assembles";

        // Everything under both halves that dormancy switches off, cached once. CACHED BECAUSE
        // DORMANCY IS RE-ASSERTED EVERY FRAME and this beat is twenty minutes into the show: a
        // GetComponentsInChildren per frame until then is two array allocations a frame for a
        // third of an hour, in a piece that budgets its frame time for a headset.
        Renderer[] m_Renderers = System.Array.Empty<Renderer>();
        XRGrabInteractable[] m_Grabs = System.Array.Empty<XRGrabInteractable>();

        bool m_Armed;
        bool m_DormancyApplied;

        bool m_Listening;
        bool m_Unlocked;
        bool m_Spoken;
        float m_LineEndsAt = -1f;

        bool m_Leaving;
        float m_LeavesAt = -1f;
        Vector3 m_LeftFrom;
        Quaternion m_TurnedFrom, m_TurnedTo;
        bool m_Arrived;

        bool m_WarnedNoPlayspace;

        /// <summary>Whether the fragments are in the room and can be picked up.</summary>
        public bool armed => m_Armed;

        /// <summary>Whether the line that answers both players' grabs has been played.</summary>
        public bool spoken => m_Spoken;

        /// <summary>Whether the assembled object has finished its journey to the middle.</summary>
        public bool arrived => m_Arrived;

        void Reset()
        {
            foreach (CompositeHalf half in GetComponentsInChildren<CompositeHalf>(true))
            {
                if (half.isAnchor) m_Anchor = half;
                else m_Follower = half;
            }
        }

        void Awake()
        {
            Resolve();

            // Awake, so a pair whose beat has not started still has a row rather than looking
            // like an interaction nobody built.
            InteractionDebug.Register(this);
        }

        void OnDestroy() => InteractionDebug.Unregister(this);

        void OnEnable()
        {
            Listen();

            // Applied outright rather than left to the first change: the fragments are authored
            // visible so they can be placed, and the first frame of the show is twenty minutes
            // before this beat.
            m_DormancyApplied = false;
            ApplyDormancy(m_BeatRoot == null || m_BeatRoot.activeInHierarchy);
        }

        void OnDisable()
        {
            if (!m_Listening) return;

            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress != null) progress.onTaskBeatUnlocked -= HandleUnlocked;
            m_Listening = false;
        }

        void Resolve()
        {
            if (m_Anchor == null || m_Follower == null) Reset();

            if (m_Source == null) m_Source = GetComponent<AudioSource>();
            if (m_Source == null)
            {
                m_Source = gameObject.AddComponent<AudioSource>();
                m_Source.playOnAwake = false;
                m_Source.loop = false;

                // Unpositioned, like every other line in the piece: speech coming from a point
                // in the room is speech the player turns their head to look for.
                m_Source.spatialBlend = 0f;
            }

            Collect();

            if (m_Anchor == null || m_Follower == null)
                Debug.LogWarning($"[Fragments] '{name}' has fewer than two halves wired, so " +
                                 "nothing here can happen. Both are needed -- the anchor is the " +
                                 "one that moves.", this);
            if (m_Line == null)
                Debug.LogWarning($"[Fragments] '{name}' has no line; the fragments still " +
                                 "assemble and still travel, silently.", this);
        }

        void Collect()
        {
            var renderers = new System.Collections.Generic.List<Renderer>();
            var grabs = new System.Collections.Generic.List<XRGrabInteractable>();

            foreach (CompositeHalf half in new[] { m_Anchor, m_Follower })
            {
                if (half == null) continue;
                renderers.AddRange(half.GetComponentsInChildren<Renderer>(true));
                grabs.AddRange(half.GetComponentsInChildren<XRGrabInteractable>(true));
            }

            m_Renderers = renderers.ToArray();
            m_Grabs = grabs.ToArray();
        }

        /// <summary>
        /// Attach to the server's unlock, AND KEEP TRYING UNTIL IT IS ACTUALLY ATTACHED. The one
        /// line that does this in OnEnable is a race whose losing side is silent -- see
        /// <see cref="O1GrabWaves"/>, where it cost a whole beat.
        /// </summary>
        void Listen()
        {
            if (m_Listening) return;

            string task = PickedUpTask();
            if (string.IsNullOrWhiteSpace(task)) return;

            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress == null) return;

            progress.onTaskBeatUnlocked += HandleUnlocked;
            m_Listening = true;

            // And what the server already knows, because an event only reaches whoever was
            // listening when it fired: staff jumping into the beat, a client arriving late, the
            // frames before the session existed.
            if (progress.IsTaskComplete(task)) Unlock("already done on the server");
        }

        /// <summary>The gate both players report by picking their own half up. Authored on the anchor.</summary>
        string PickedUpTask() => m_Anchor != null ? m_Anchor.pickedUpTaskId : null;

        void HandleUnlocked(string taskId)
        {
            if (taskId != PickedUpTask()) return;
            Unlock("both players have picked one up");
        }

        void Unlock(string why)
        {
            if (m_Unlocked) return;
            m_Unlocked = true;

            Debug.Log($"[Fragments] '{name}': {why}. The line plays as soon as the beat is up, " +
                      "and the halves weld on proximity from here.", this);
        }

        void Update()
        {
            // A state, not an event: until the subscription is on, keep asking for it.
            Listen();

            ApplyDormancy(m_BeatRoot == null || m_BeatRoot.activeInHierarchy);

            if (m_Armed && !m_Spoken && BothHavePickedOneUp()) Speak();

            WatchForTheWeld();
            Travel();
        }

        /// <summary>
        /// Both players, by whichever account there is. The server's gate when there is one --
        /// so both headsets hear the line on the same frame -- and the halves' own latches when
        /// there is not, which is a desk test or a pair with no gate authored.
        ///
        /// THE GATE IS POLLED AS WELL AS SUBSCRIBED TO. A solo run satisfies
        /// <see cref="StoryProgressManager.IsTaskComplete"/> with one role's flag and never
        /// raises the unlock, which would leave one person rehearsing the beat in silence; and a
        /// gate staff have forced is meant to speak whenever they force it.
        /// </summary>
        bool BothHavePickedOneUp()
        {
            if (m_Unlocked) return true;

            if (m_Listening)
            {
                StoryProgressManager progress = StoryProgressManager.Instance;
                if (progress == null || !progress.IsTaskComplete(PickedUpTask())) return false;

                Unlock("the server's gate is satisfied");
                return true;
            }

            return m_Anchor != null && m_Follower != null &&
                   m_Anchor.wasEverHeld && m_Follower.wasEverHeld;
        }

        void Speak()
        {
            m_Spoken = true;

            if (m_Source != null && m_Line != null)
            {
                m_Source.clip = m_Line;
                m_Source.Play();
            }

            // The clock, not AudioSource.isPlaying: a forced gate, a muted device and a machine
            // with no line for this beat all still have to show the row moving on.
            m_LineEndsAt = ExperienceClock.now + (m_Line != null ? m_Line.length : 0f);

            Debug.Log($"[Fragments] '{name}': playing " +
                      $"'{(m_Line != null ? m_Line.name : "nothing on this machine")}'.", this);
        }

        #region Dormancy

        /// <summary>
        /// In the room, or not yet. Re-asserted every frame while dormant rather than written
        /// once, because <see cref="RoleGhost"/> re-enables the interactable whenever the local
        /// role replicates -- which happens long after the show has started and would quietly
        /// hand a beat's props to the players twenty minutes early.
        /// </summary>
        void ApplyDormancy(bool armed)
        {
            bool changed = armed != m_Armed || !m_DormancyApplied;
            m_Armed = armed;
            m_DormancyApplied = true;

            if (changed)
            {
                foreach (Renderer r in m_Renderers)
                {
                    if (r != null && r.forceRenderingOff == armed) r.forceRenderingOff = !armed;
                }

                if (armed)
                {
                    Reghost(m_Anchor);
                    Reghost(m_Follower);

                    Debug.Log($"[Fragments] '{name}': the beat is up, the fragments are in the " +
                              "room.", this);
                }
            }

            if (armed) return;

            // A comparison per grab per frame off a cached array, which is the reason this is in
            // Update at all.
            foreach (XRGrabInteractable grab in m_Grabs)
            {
                if (grab != null && grab.enabled) grab.enabled = false;
            }
        }

        /// <summary>
        /// Hands grabbability back to <see cref="RoleGhost"/> rather than switching it on here:
        /// WHICH of the two halves this player may grab is its business, and a flat
        /// "enabled = true" is how both halves become grabbable by both players.
        /// </summary>
        void Reghost(CompositeHalf half)
        {
            if (half == null) return;

            var ghost = half.GetComponent<RoleGhost>();
            if (ghost != null)
            {
                ghost.SetGhosted(ghost.isGhosted);
                return;
            }

            // No ghost on this half, so nobody is claiming it is somebody else's: it is simply
            // grabbable. A pair authored without RoleGhost is a test scene, not the show.
            foreach (XRGrabInteractable grab in m_Grabs)
            {
                if (grab != null && !grab.enabled) grab.enabled = true;
            }
        }

        #endregion

        #region The journey

        void WatchForTheWeld()
        {
            if (m_Leaving || m_Anchor == null) return;
            if (!m_Anchor.isAssembled) return;

            m_Leaving = true;
            m_LeavesAt = ExperienceClock.now + m_Delay;
            m_Anchor.transform.GetPositionAndRotation(out m_LeftFrom, out m_TurnedFrom);

            // Upright, facing whatever way it ended up facing: the players chose the angle it is
            // seen from, the beat chooses that it is standing.
            m_TurnedTo = m_StandUpright
                ? Quaternion.Euler(0f, m_TurnedFrom.eulerAngles.y, 0f)
                : m_TurnedFrom;

            Debug.Log($"[Fragments] '{name}': welded. It leaves for the middle in {m_Delay:0.0}s " +
                      $"and takes {m_Travel:0.0}s over it.", this);
        }

        void Travel()
        {
            if (!m_Leaving || m_Arrived || !IDrive()) return;
            if (ExperienceClock.now < m_LeavesAt) return;

            Vector3 target = Destination();
            float t = Mathf.Clamp01((ExperienceClock.now - m_LeavesAt) / m_Travel);

            // Smoothstep, so it neither jerks out of the players' hands nor arrives at speed.
            float eased = t * t * (3f - 2f * t);

            m_Anchor.transform.SetPositionAndRotation(
                Vector3.LerpUnclamped(m_LeftFrom, target, eased),
                Quaternion.SlerpUnclamped(m_TurnedFrom, m_TurnedTo, eased));

            if (t < 1f) return;

            m_Arrived = true;
            Debug.Log($"[Fragments] '{name}': arrived at the middle of the playspace, " +
                      $"{target:F2}.", this);
        }

        /// <summary>
        /// Whether THIS machine moves it. The server does, because it owns both halves from the
        /// weld onwards and anything another machine wrote would be overwritten by the next
        /// replicated pose anyway. With no session, this machine is the only one there is.
        /// </summary>
        bool IDrive() => m_Anchor != null && (!m_Anchor.IsSpawned || m_Anchor.IsServer);

        /// <summary>
        /// 「Playspace 正中央」, read live rather than captured at the weld: the playspace is the
        /// thing the show moves the players around on, so the middle of the room is wherever it
        /// is now and not where it was when the halves met.
        /// </summary>
        Vector3 Destination()
        {
            if (m_Destination != null) return m_Destination.position;

            Playspace playspace = Playspace.Instance;
            if (playspace != null)
                return playspace.transform.TransformPoint(new Vector3(0f, m_Height, 0f));

            if (!m_WarnedNoPlayspace)
            {
                m_WarnedNoPlayspace = true;
                Debug.LogWarning($"[Fragments] '{name}' found no Playspace, so the middle of the " +
                                 "room is being taken as the world origin. Wire Destination if " +
                                 "that is not where the players are standing.", this);
            }

            return new Vector3(0f, m_Height, 0f);
        }

        #endregion

        #region Debug panel

        public string debugId => string.IsNullOrWhiteSpace(m_DebugId) ? "fragments" : m_DebugId;

        public string debugLabel =>
            string.IsNullOrWhiteSpace(m_DebugLabel) ? "Fragment pair" : m_DebugLabel;

        // The pair's two gates have rows of their own, registered by CompositeHalf. This row is
        // the SEQUENCE around them -- which half of it the beat is on -- and most of that is
        // this machine's own view.
        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        public string debugFireLabel => "Unlock";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;

            if (m_Anchor == null || m_Follower == null)
            {
                state.detail = "halves not wired";
                return state;
            }

            state.fired = m_Spoken;

            if (m_Arrived)
            {
                state.detail = "assembled and in the middle";
                return state;
            }

            if (m_Leaving)
            {
                if (!IDrive())
                {
                    state.detail = "assembled; the server is bringing it to the middle";
                    return state;
                }

                state.detail = ExperienceClock.now < m_LeavesAt
                    ? "assembled; leaving for the middle"
                    : "on its way to the middle, " +
                      $"{Mathf.Max(m_LeavesAt + m_Travel - ExperienceClock.now, 0f):0.0}s left";
                return state;
            }

            if (!m_Armed)
            {
                state.detail = m_BeatRoot != null
                    ? $"dormant -- '{m_BeatRoot.name}' is not up yet"
                    : "dormant";
                return state;
            }

            if (!m_Spoken)
            {
                // WHICH player, not how many: a row reading 1/2 on the host is the one thing
                // here that does not say what to do next. 'P2 has not' is a machine to go and
                // look at.
                state.detail = $"picked up -- P1:{(Held(PlayerRole.Player1) ? "yes" : "--")} " +
                               $"P2:{(Held(PlayerRole.Player2) ? "yes" : "--")}" +
                               (m_Listening ? "" : " (no gate -- reading the halves directly)");
                return state;
            }

            if (ExperienceClock.now < m_LineEndsAt)
            {
                state.detail = $"speaking, {m_LineEndsAt - ExperienceClock.now:0.0}s left; " +
                               $"{Apart():0.00} m apart";
                return state;
            }

            // The number a stall here is actually about: the halves are in the room, both
            // players have had theirs, and the only question left is whether they are close
            // enough. Staff reading "0.31 m apart" know to say 「再靠近一點」.
            state.detail = $"waiting for the halves to meet -- {Apart():0.00} m apart";
            return state;
        }

        bool Held(PlayerRole role)
        {
            if (m_Anchor != null && m_Anchor.ownedBy == role) return m_Anchor.wasEverHeld;
            if (m_Follower != null && m_Follower.ownedBy == role) return m_Follower.wasEverHeld;
            return false;
        }

        float Apart() =>
            m_Anchor == null || m_Follower == null
                ? 0f
                : Vector3.Distance(m_Anchor.transform.position, m_Follower.transform.position);

        public bool DebugCanFire(DebugFireTarget target)
        {
            if (m_Anchor == null || m_Follower == null) return false;
            if (m_Spoken && m_Unlocked) return false;

            if (target == DebugFireTarget.Local) return isActiveAndEnabled;

            // The tier above Local writes the gate on the server, so it needs a server and a
            // gate to write.
            return InteractionDebugRelay.canForce &&
                   StoryProgressManager.Instance != null &&
                   !string.IsNullOrWhiteSpace(PickedUpTask());
        }

        /// <summary>
        /// THE TWO TIERS ARE NOT THE SAME RESCUE. Local plays the line on this machine and
        /// nothing else: the other headset does not hear it and -- more to the point -- the
        /// server's gate is untouched, so a pair authored to weld on 「都抓過」 still will not
        /// weld. Useful on a desk, misleading in the venue.
        ///
        /// as P1 / as P2 / both write the gate instead, which is the path a real grab ends in:
        /// every machine hears the line on the same frame AND the server welds the halves when
        /// they meet. One role alone is the half-report, which is how to find out which machine
        /// is not reporting its grab.
        /// </summary>
        public void DebugFire(DebugFireTarget target)
        {
            if (!DebugCanFire(target)) return;

            if (target == DebugFireTarget.Local)
            {
                if (!m_Armed) ApplyDormancy(true);
                if (!m_Spoken) Speak();
                return;
            }

            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress == null) return;

            string task = PickedUpTask();
            if (target != DebugFireTarget.Player2)
                progress.DebugSetRoleDone(task, PlayerRole.Player1, true);
            if (target != DebugFireTarget.Player1)
                progress.DebugSetRoleDone(task, PlayerRole.Player2, true);

            Debug.Log($"[Fragments] '{name}': '{task}' forced as {target} from the panel.", this);
        }

        /// <summary>
        /// Puts the line and the journey back so the beat can be rehearsed again.
        ///
        /// IT CANNOT UNDO THE WELD. That is a replicated flag the server owns and
        /// <see cref="CompositeHalf"/> has no path back from -- the follower is reparented, both
        /// bodies are frozen, both halves are solid for everyone. Taking the pair apart again
        /// means restarting the session, and a button that claimed otherwise would be worse than
        /// no button: staff would press it, see nothing, and spend the stall looking here.
        /// </summary>
        public bool DebugCanReset() => m_Spoken || m_Unlocked || m_Leaving;

        public void DebugReset()
        {
            m_Spoken = false;
            m_Unlocked = false;
            m_LineEndsAt = -1f;
            if (m_Source != null && m_Source.isPlaying) m_Source.Stop();

            m_Leaving = false;
            m_Arrived = false;
            m_LeavesAt = -1f;

            if (m_Anchor != null && m_Anchor.isAssembled)
                Debug.LogWarning($"[Fragments] '{name}': the line and the journey are back, but " +
                                 "the halves are already welded and cannot be taken apart " +
                                 "without restarting the session.", this);
        }

        #endregion
    }
}
