using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// O-1: one player picks up one object, a cue sounds, and that player's half of the beat's
    /// gate is reported. Player A has a coat and S0-1-A, Player B a mug and S0-1-B.
    ///
    /// THE CUE IS LOCAL, like the object it belongs to. Neither player can see the other's
    /// object -- RoleVisibility switches off its renderers AND its colliders -- so a grab can
    /// only ever happen on the machine that owns it, and each player hears their own cue and not
    /// the other's. Nothing about the sound crosses the network, which is the same decision
    /// <see cref="O1GrabStation"/>'s object already embodies: there is no shared state here to
    /// disagree about.
    ///
    /// THE GATE REPORT IS THE ONE THING THAT DOES CROSS. The show holds after O-1 until both
    /// players have picked their object up, so the grab reports through
    /// <see cref="StoryProgressManager"/> -- which attributes the report to whoever sent it, so a
    /// machine cannot report the other player's half even if this component were misconfigured
    /// about which role owns it.
    ///
    /// FIRST GRAB ONLY. The object is meant to be picked up, put down and picked up again; the
    /// cue is not. A second grab replaying a five-second sound over the top of the beat's own
    /// audio is the kind of thing that reads as a bug from inside a headset.
    /// </summary>
    [DisallowMultipleComponent]
    public class O1GrabStation : MonoBehaviour, IDebugInteraction
    {
        [SerializeField, Tooltip("The role this object belongs to. Only this player can see it, " +
                                 "grab it, or hear its cue.")]
        PlayerRole m_Owner = PlayerRole.Player1;

        [SerializeField, Tooltip("The cue that plays on the first grab. S0-1-A for the coat, " +
                                 "S0-1-B for the mug.")]
        AudioClip m_Cue;

        [SerializeField, Tooltip("Plays the cue. Made on this object if left empty.")]
        AudioSource m_Source;

        [SerializeField, Tooltip("The thing that is grabbed. Found under this object if empty.")]
        XRGrabInteractable m_Grab;

        [SerializeField, Tooltip("Task reported when this player grabs. The show's hold after " +
                                 "O-1 waits on it, from both players.")]
        string m_TaskId = "beat.O1_Control";

        [SerializeField, Range(0f, 1f), Tooltip("1 is fully positioned in the room, which is " +
                                                "what a sound coming off an object you are " +
                                                "holding wants to be. The clips are mono for " +
                                                "exactly this reason.")]
        float m_Spatial = 1f;

        [SerializeField, Min(0.1f), Tooltip("Metres beyond which the cue is inaudible.")]
        float m_MaxDistance = 12f;

        [SerializeField, Min(0f), Tooltip("Seconds the show may sit on this gate before this " +
                                          "player's half is reported anyway. 0 disables it. The " +
                                          "production timeline asks for a fallback on every " +
                                          "gate so one player cannot strand the other.")]
        float m_Timeout = 180f;

        bool m_Grabbed;
        bool m_TimedOut;
        float m_GrabbedAt = -1f;
        float m_WaitingSince = -1f;

        /// <summary>Whether this object has been picked up yet, on this machine.</summary>
        public bool grabbed => m_Grabbed;

        public PlayerRole owner => m_Owner;

        /// <summary>The cue this object plays when it is picked up.</summary>
        public AudioClip cue => m_Cue;

        /// <summary>Whether that cue is sounding right now.</summary>
        public bool cuePlaying => m_Source != null && m_Source.isPlaying;

        void Awake()
        {
            Resolve();

            // Awake, not OnEnable, so a station switched off still has a debug row rather than
            // looking like an interaction nobody built.
            InteractionDebug.Register(this);

            // The gate exists whether or not anyone has reported it, so the row for it is
            // declared from here: this is the thing that knows the task is the grab's.
            TaskGateInteraction gate = InteractionDebug.RegisterTaskGate(
                m_TaskId, "O-1 grab -- both players hold an object");

            // The gate borrows this station's countdown, so the row that the show is actually
            // waiting on is the one that says how long it will wait. Whichever station registers
            // last wins, which is fine: both run the same fallback from the same moment.
            if (gate != null) gate.timeoutSource = () => secondsUntilTimeout;
        }

        void OnDestroy() => InteractionDebug.Unregister(this);

        void OnEnable()
        {
            if (m_Grab != null) m_Grab.selectEntered.AddListener(HandleSelectEntered);
        }

        void OnDisable()
        {
            if (m_Grab != null) m_Grab.selectEntered.RemoveListener(HandleSelectEntered);
        }

        void Resolve()
        {
            if (m_Grab == null) m_Grab = GetComponentInChildren<XRGrabInteractable>(true);

            if (m_Source == null) m_Source = GetComponent<AudioSource>();
            if (m_Source == null)
            {
                m_Source = gameObject.AddComponent<AudioSource>();
                m_Source.playOnAwake = false;
                m_Source.loop = false;
            }

            m_Source.clip = m_Cue;
            m_Source.spatialBlend = m_Spatial;
            m_Source.maxDistance = m_MaxDistance;
            m_Source.rolloffMode = AudioRolloffMode.Linear;

            if (m_Grab == null)
                Debug.LogWarning($"[O1Grab] '{name}' has nothing grabbable under it, so its cue " +
                                 "can never fire.", this);
            if (m_Cue == null)
                Debug.LogWarning($"[O1Grab] '{name}' has no cue clip; the grab will still report " +
                                 "the gate but will be silent.", this);
        }

        void HandleSelectEntered(SelectEnterEventArgs _) => Grab();

        /// <summary>
        /// Seconds before this player's half is reported whether they picked anything up or not,
        /// or negative when no clock is running. Counted from the show ARRIVING AT THIS GATE, not
        /// from the beat starting: the players cannot be late for something that has not asked
        /// them for anything yet, and O-1 is ninety seconds long.
        /// </summary>
        public float secondsUntilTimeout
        {
            get
            {
                if (m_Timeout <= 0f || m_Grabbed || m_WaitingSince < 0f) return -1f;
                return Mathf.Max(m_Timeout - (ExperienceClock.now - m_WaitingSince), 0f);
            }
        }

        void Update()
        {
            if (m_Grabbed || m_Timeout <= 0f) return;

            // ONLY THE OWNER'S MACHINE COUNTS DOWN. Both machines carry both stations, and a
            // report is attributed to whoever sends it -- so the copy of Player B's mug sitting
            // on Player A's machine timing out would report PLAYER A's half a second time and
            // leave Player B still missing, which is the opposite of what the fallback is for.
            PlayerRole local = InteractionDebug.LocalRole();
            if (local != PlayerRole.Unassigned && local != m_Owner) return;

            ShowTimelineDriver driver = ShowTimelineDriver.Instance;
            bool waiting = driver != null && driver.waitingOn == m_TaskId;
            if (!waiting)
            {
                m_WaitingSince = -1f;
                return;
            }

            if (m_WaitingSince < 0f) m_WaitingSince = ExperienceClock.now;
            if (ExperienceClock.now - m_WaitingSince < m_Timeout) return;

            m_TimedOut = true;
            m_Grabbed = true;       // latched, so this happens once and a later real grab is quiet
            m_GrabbedAt = ExperienceClock.now;

            // NO CUE. The sound belongs to a player picking their object up; playing it for
            // someone who did not would be telling the other player something that did not
            // happen.
            Debug.LogWarning($"[O1Grab] '{name}' not picked up {m_Timeout:F0}s after the show " +
                             "reached this gate; reporting anyway so the other player is not " +
                             "left waiting.", this);
            Report();
        }

        /// <summary>
        /// The grab itself: the cue, then the report. Idempotent -- the object can be picked up
        /// as often as the player likes and this happens once.
        /// </summary>
        public void Grab()
        {
            if (m_Grabbed) return;
            m_Grabbed = true;
            m_GrabbedAt = ExperienceClock.now;

            if (m_Source != null && m_Cue != null) m_Source.Play();

            Report();

            Debug.Log($"[O1Grab] '{name}' grabbed by {m_Owner} at {m_GrabbedAt:0.00}s; " +
                      $"cue {(m_Cue != null ? m_Cue.name : "none")}, reported '{m_TaskId}'.", this);
        }

        /// <summary>
        /// Tell the show. Sent from whichever machine the grab happened on, and attributed
        /// server-side to that machine's role -- so this does not need to be right about
        /// <see cref="m_Owner"/> for the right half to be reported.
        /// </summary>
        void Report()
        {
            if (string.IsNullOrWhiteSpace(m_TaskId)) return;

            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress == null)
            {
                Debug.LogWarning($"[O1Grab] no StoryProgressManager; '{m_TaskId}' not reported, " +
                                 "so the show will not come off its hold for this grab.", this);
                return;
            }

            progress.ReportTaskCompleteRpc(m_TaskId);
        }

        #region Debug panel

        // Per station, not per machine: both machines carry both stations, and the id has to name
        // the OBJECT so a button pressed on one screen fires the station that player actually
        // owns rather than the local copy of someone else's.
        public string debugId => $"o1.grab.{m_Owner}";

        // The object's own name, which already says whose it is -- "Coat (Player A)". Naming it
        // after its cue instead read as "Grab the S0-1-A object", which is the one thing in the
        // list a person would have to translate back.
        public string debugLabel => $"Grab the {name}";

        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        public string debugFireLabel => "Grab";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = m_Grabbed;
            state.secondsToTimeout = secondsUntilTimeout;

            if (m_Grabbed)
            {
                state.detail = m_TimedOut
                    ? $"NOT grabbed -- reported by the fallback at {m_GrabbedAt:0.0}s, no cue"
                    : $"grabbed at {m_GrabbedAt:0.0}s, cue " +
                      $"{(m_Source != null && m_Source.isPlaying ? "playing" : "done")}";
                return state;
            }

            PlayerRole local = InteractionDebug.LocalRole();

            // Says whose object it is, because a row reading "not fired" on the machine that
            // could never fire it is the one thing in this list that would mislead: there is
            // nothing wrong, it is simply not this player's coat.
            if (local != PlayerRole.Unassigned && local != m_Owner)
                state.detail = $"{m_Owner}'s object -- not grabbable on this machine";
            else if (m_Grab == null)
                state.detail = "nothing grabbable wired";
            else if (!isActiveAndEnabled)
                state.detail = "inactive -- its beat is not running";
            else
                state.detail = m_Grab.enabled ? "waiting to be picked up" : "grab switched off";

            return state;
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            if (m_Grabbed) return false;

            if (target == DebugFireTarget.Local)
            {
                if (!isActiveAndEnabled) return false;

                // With no role assigned -- a desk test -- both stations can be fired here, which
                // is the only way to walk the beat alone on one machine.
                PlayerRole local = InteractionDebug.LocalRole();
                return local == PlayerRole.Unassigned || local == m_Owner;
            }

            if (target == DebugFireTarget.Both) return false;

            DebugFireTarget mine = m_Owner == PlayerRole.Player1
                ? DebugFireTarget.Player1
                : DebugFireTarget.Player2;

            return target == mine && InteractionDebugRelay.canForce &&
                   InteractionDebugRelay.TryGetClientForRole(m_Owner, out _);
        }

        public void DebugFire(DebugFireTarget target)
        {
            if (!DebugCanFire(target)) return;

            if (target == DebugFireTarget.Local)
            {
                Grab();
                return;
            }

            // Asks the owner's machine to grab its own copy. The id names the station, so the
            // other machine fires the same object rather than its local copy of the other one.
            if (InteractionDebugRelay.TryGetClientForRole(m_Owner, out ulong clientId))
                InteractionDebugRelay.Instance?.FireOnClient(clientId, debugId);
        }

        /// <summary>
        /// Puts the cue back so the beat can be rehearsed again. Clears the latch only -- the
        /// gate flag it reported belongs to the task, and the gate's own row is where that is
        /// cleared.
        /// </summary>
        public bool DebugCanReset() => m_Grabbed;

        public void DebugReset()
        {
            m_Grabbed = false;
            m_GrabbedAt = -1f;
            if (m_Source != null && m_Source.isPlaying) m_Source.Stop();
        }

        #endregion
    }
}
