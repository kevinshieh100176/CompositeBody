using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// O-1, second half: the player picks their object up off the floor and puts it in a
    /// cardboard box. THE BOX IS WHAT THE SHOW WAITS FOR -- the grab only sounds the cue (see
    /// <see cref="O1GrabStation"/>); this is what counts, so the hold near the end of O-1 clears
    /// when both players have actually put something somewhere, not when they have merely closed
    /// a hand around it.
    ///
    /// IT NO LONGER REPORTS THE GATE ITSELF. With one object each it did, and that was right.
    /// With several each, and a line of speech owed to every one of them, "this player is
    /// finished" is a count and a silent speaker -- so <see cref="O1DropSequence"/> watches every
    /// box through <see cref="onDeposited"/> and reports beat.O1_Control when the last line has
    /// stopped. A box left with a task id of its own still reports, for a beat that is one object
    /// and no speech.
    ///
    /// LOCAL, LIKE EVERYTHING ELSE IN THE BEAT. Each player has their own object and their own
    /// box, and RoleVisibility hides the other player's pair -- renderers AND colliders, which
    /// is also what keeps this trigger from firing on the machine that does not own it. Nothing
    /// about the box crosses the network; only the gate report does, attributed server-side to
    /// whoever sent it.
    ///
    /// DROPPED IN, NOT HELD OVER. The object has to be inside the mouth AND let go of: an arm
    /// reaching through the box on the way somewhere else is not someone putting a coat away,
    /// and a player holding a mug over the opening while they look around is not either. The
    /// settle time is there for the other direction -- an object thrown through the box fast
    /// enough to be inside it on one frame and out of it on the next should not count.
    ///
    /// ONCE. The object can be taken out and put back as often as the player likes; the report
    /// and the cue happen the first time.
    /// </summary>
    [DisallowMultipleComponent]
    public class O1DropBox : MonoBehaviour, IDebugInteraction
    {
        [SerializeField, Tooltip("The role this box belongs to. Only this player can see it, " +
                                 "reach it, or fill it.")]
        PlayerRole m_Owner = PlayerRole.Player1;

        [SerializeField, Tooltip("The inside of the box: a trigger the object has to end up " +
                                 "within. Found under this object if left empty.")]
        Collider m_Mouth;

        [SerializeField, Tooltip("The object that belongs in it -- this player's coat or mug. " +
                                 "Taken from the station this box sits under if left empty.")]
        XRGrabInteractable m_Object;

        [SerializeField, Tooltip("Task reported when the object is in the box. LEAVE IT EMPTY " +
                                 "when an O1DropSequence is running the beat: with several " +
                                 "objects each and a line of speech owed to every one of them, " +
                                 "one box cannot tell whether the player has finished, so the " +
                                 "sequence owns the gate and the fallback and this only reports " +
                                 "to it.")]
        string m_TaskId = "";

        [SerializeField, Tooltip("Optional sound for the moment it lands. The grab already has " +
                                 "its own cue; this one is silent unless a clip is given.")]
        AudioClip m_Cue;

        [SerializeField, Tooltip("Plays the cue. Made on this object if left empty and a clip " +
                                 "is set.")]
        AudioSource m_Source;

        [SerializeField, Min(0f), Tooltip("Seconds the object has to stay inside, let go of, " +
                                          "before it counts. Short: this is only here so a " +
                                          "throw that passes through the box does not register.")]
        float m_SettleSeconds = 0.25f;

        [SerializeField, Min(0f), Tooltip("Seconds the show may sit on this gate before this " +
                                          "player's half is reported anyway. 0 disables it. The " +
                                          "production timeline asks for a fallback on every " +
                                          "gate so one player cannot strand the other.")]
        float m_Timeout = 180f;

        Rigidbody m_Body;
        bool m_Deposited;
        bool m_TimedOut;
        float m_DepositedAt = -1f;
        float m_WaitingSince = -1f;
        float m_InsideFor;

        // Where the object was before anyone touched it, so a debug reset can put it back. A
        // latch cleared with the object still lying in the box would be re-reported on the next
        // frame, which is the one way this row could lie about what it had seen.
        Vector3 m_ObjectHome;
        Quaternion m_ObjectHomeRotation;

        /// <summary>
        /// Raised the first time something is put in, on the machine it happened on.
        /// <see cref="O1DropSequence"/> counts these and queues the line each one is owed.
        /// </summary>
        public event System.Action<O1DropBox> onDeposited;

        /// <summary>Whether this player's object is in the box, on this machine.</summary>
        public bool deposited => m_Deposited;

        public PlayerRole owner => m_Owner;

        /// <summary>The object this box is waiting for.</summary>
        public XRGrabInteractable target => m_Object;

        /// <summary>The volume the object has to end up in.</summary>
        public Collider mouth => m_Mouth;

        void Awake()
        {
            Resolve();

            // Awake, not OnEnable, so a box switched off still has a debug row rather than
            // looking like an interaction nobody built.
            InteractionDebug.Register(this);

            // Only when this box still owns a gate. It usually does not any more --
            // O1DropSequence reports beat.O1_Control once every box is full AND the lines it
            // owes have been heard, and registering the same task here as well would give the
            // panel two rows for one gate, each with its own countdown.
            if (string.IsNullOrWhiteSpace(m_TaskId)) return;

            TaskGateInteraction gate = InteractionDebug.RegisterTaskGate(
                m_TaskId, "O-1 box -- both players put their object in");

            if (gate != null) gate.timeoutSource = () => secondsUntilTimeout;
        }

        void OnDestroy() => InteractionDebug.Unregister(this);

        void Resolve()
        {
            if (m_Object == null && transform.parent != null)
                m_Object = transform.parent.GetComponentInChildren<XRGrabInteractable>(true);

            if (m_Mouth == null)
            {
                foreach (Collider collider in GetComponentsInChildren<Collider>(true))
                {
                    if (!collider.isTrigger) continue;
                    m_Mouth = collider;
                    break;
                }
            }

            if (m_Object != null)
            {
                m_Body = m_Object.GetComponent<Rigidbody>();
                m_ObjectHome = m_Object.transform.position;
                m_ObjectHomeRotation = m_Object.transform.rotation;
            }

            if (m_Cue != null)
            {
                if (m_Source == null) m_Source = GetComponent<AudioSource>();
                if (m_Source == null)
                {
                    m_Source = gameObject.AddComponent<AudioSource>();
                    m_Source.playOnAwake = false;
                    m_Source.loop = false;
                    m_Source.spatialBlend = 1f;
                    m_Source.rolloffMode = AudioRolloffMode.Linear;
                    m_Source.maxDistance = 12f;
                }
                m_Source.clip = m_Cue;
            }

            if (m_Mouth == null)
                Debug.LogWarning($"[O1Box] '{name}' has no trigger inside it, so nothing can " +
                                 "ever be put in it.", this);
            if (m_Object == null)
                Debug.LogWarning($"[O1Box] '{name}' has no object to wait for; only its " +
                                 "fallback can clear this gate.", this);
        }

        /// <summary>
        /// Seconds before this player's half is reported whether they filled the box or not, or
        /// negative when no clock is running. Counted from the show ARRIVING AT THIS GATE, not
        /// from the beat starting: the players cannot be late for something that has not asked
        /// them for anything yet, and O-1 is ninety seconds long.
        /// </summary>
        public float secondsUntilTimeout
        {
            get
            {
                if (m_Timeout <= 0f || m_Deposited || m_WaitingSince < 0f) return -1f;
                return Mathf.Max(m_Timeout - (ExperienceClock.now - m_WaitingSince), 0f);
            }
        }

        /// <summary>
        /// Whether the object is within the mouth right now, held or not. ClosestPoint rather
        /// than the trigger's bounds: bounds is an axis-aligned box around the collider, so a
        /// box turned to face the player would claim a wedge of the room beside it.
        /// </summary>
        public bool objectInside
        {
            get
            {
                if (m_Mouth == null || m_Object == null) return false;

                // A COLLIDER THAT CANNOT BE QUERIED SAYS YES TO EVERYTHING. ClosestPoint returns
                // the point it was handed when it cannot answer -- a disabled collider, an
                // inactive object, a non-convex mesh -- and the test below reads "the closest
                // point is where I already am" as "I am inside". RoleVisibility switches off the
                // renderers AND the colliders of the station that does not belong to this
                // player, which includes this trigger: so on every machine, the OTHER player's
                // box reported itself full on the first frame of the show, with nothing in it.
                //
                // It cost a gate. The report is attributed server-side to whoever sent it, so
                // the phantom deposit on Player A's machine reported PLAYER A's half of
                // beat.O1_Control before either player had touched anything.
                if (!m_Mouth.enabled || !m_Mouth.gameObject.activeInHierarchy)
                {
                    if (!m_WarnedUnqueryable)
                    {
                        m_WarnedUnqueryable = true;
                        // Says what is true and not why: this fires once, and on the first
                        // frames of the show it fires for BOTH boxes, because RoleVisibility
                        // has not been told which role this machine holds yet. Claiming the
                        // box belongs to the other player would be wrong half the time.
                        Debug.Log($"[O1Box] '{m_Owner}'s box cannot be filled while its trigger " +
                                  "is off -- RoleVisibility switches off the station that does " +
                                  "not belong to this machine, and switches nothing on until " +
                                  "the local role is known.", this);
                    }
                    return false;
                }

                Vector3 point = m_Body != null
                    ? m_Body.worldCenterOfMass
                    : m_Object.transform.position;

                return (m_Mouth.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;
            }
        }

        bool m_WarnedUnqueryable;

        /// <summary>Whether a hand is on the object at this moment.</summary>
        public bool objectHeld => m_Object != null && m_Object.isSelected;

        void Update()
        {
            if (m_Deposited) return;

            Watch();

            // No gate of its own means no fallback of its own: O1DropSequence carries the one
            // that matters, and a box that latched itself "deposited" on a timeout would be
            // counted by the sequence as an object that went in when nothing did.
            if (!m_Deposited && m_Timeout > 0f && !string.IsNullOrWhiteSpace(m_TaskId)) CountDown();
        }

        /// <summary>In, and let go of, for long enough to mean it.</summary>
        void Watch()
        {
            if (!objectInside || objectHeld)
            {
                m_InsideFor = 0f;
                return;
            }

            m_InsideFor += Time.deltaTime;
            if (m_InsideFor >= m_SettleSeconds) Deposit();
        }

        void CountDown()
        {
            // ONLY THE OWNER'S MACHINE COUNTS DOWN. Both machines carry both boxes, and a report
            // is attributed to whoever sends it -- so the copy of Player B's box sitting on
            // Player A's machine timing out would report PLAYER A's half a second time and leave
            // Player B still missing, which is the opposite of what the fallback is for.
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
            m_Deposited = true;     // latched, so this happens once and a later real drop is quiet
            m_DepositedAt = ExperienceClock.now;

            // NO CUE. The sound belongs to an object landing in a box; playing it for a box that
            // is still empty would be telling the other player something that did not happen.
            Debug.LogWarning($"[O1Box] '{name}' still empty {m_Timeout:F0}s after the show " +
                             "reached this gate; reporting anyway so the other player is not " +
                             "left waiting.", this);
            Report();
        }

        /// <summary>
        /// The object is in. Idempotent -- it can be taken out and put back as often as the
        /// player likes and this happens once.
        /// </summary>
        public void Deposit()
        {
            if (m_Deposited) return;
            m_Deposited = true;
            m_DepositedAt = ExperienceClock.now;

            if (m_Source != null && m_Cue != null) m_Source.Play();

            Report();

            // After the report, so an O1DropSequence that clears the gate on this very drop does
            // it with the box already counted.
            onDeposited?.Invoke(this);

            Debug.Log($"[O1Box] '{name}' filled by {m_Owner} at {m_DepositedAt:0.00}s" +
                      (string.IsNullOrWhiteSpace(m_TaskId)
                          ? "; the sequence owns the gate."
                          : $"; reported '{m_TaskId}'."), this);
        }

        /// <summary>
        /// Tell the show. Sent from whichever machine the drop happened on, and attributed
        /// server-side to that machine's role -- so this does not need to be right about
        /// <see cref="m_Owner"/> for the right half to be reported.
        /// </summary>
        void Report()
        {
            if (string.IsNullOrWhiteSpace(m_TaskId)) return;

            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress == null)
            {
                Debug.LogWarning($"[O1Box] no StoryProgressManager; '{m_TaskId}' not reported, " +
                                 "so the show will not come off its hold for this drop.", this);
                return;
            }

            progress.ReportTaskCompleteRpc(m_TaskId);
        }

        #region Debug panel

        // Per box, not per machine: both machines carry both boxes, and the id has to name the
        // OBJECT so a button pressed on one screen fires the box that player actually owns
        // rather than the local copy of someone else's.
        //
        // AND THE OBJECT, NOT JUST ITS OWNER. This was 'o1.box.{owner}' while there was one
        // watcher per player. There is one carton now with a watcher per object under it, so
        // that id named two of them at once and every lookup -- DebugKeys, the panel's remote
        // fire, the test runner -- takes the FIRST match. The jacket's watcher could be seen in
        // the list and never fired from a key or from the other machine, which matters more
        // than it used to: putting an object away is what brings in the next one, so a drop
        // that will not register now stalls the beat rather than just delaying its gate.
        public string debugId => $"o1.box.{m_Owner}.{ObjectKey()}";

        /// <summary>
        /// The watched object's own name -- "Lamp", "Jacket" -- which is what tells this
        /// player's watchers apart. Matches <see cref="O1GrabStation.debugId"/>'s key, so the
        /// two rows for one object read as a pair. The watcher's own name ("Drop (Lamp)") is
        /// the fallback for a box wired by hand before its object was set.
        /// </summary>
        string ObjectKey() => m_Object != null ? m_Object.name : name;

        // WHOSE BOX, as well as which object. The id was not the only thing that stopped telling
        // these apart once each player had several: four rows read "Put the Lamp in the box" and
        // "Put the Jacket in the box" twice over, so the panel could not say which of them had
        // been filled. The grab rows get this for free -- their station is named "Lamp (Player
        // A)" -- and these do not, because a watcher is named after the object alone.
        public string debugLabel =>
            $"Put the {(m_Object != null ? m_Object.name : "object")} in {m_Owner}'s box";

        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        public string debugFireLabel => "Drop in";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = m_Deposited;
            state.secondsToTimeout = secondsUntilTimeout;

            if (m_Deposited)
            {
                state.detail = m_TimedOut
                    ? $"STILL EMPTY -- reported by the fallback at {m_DepositedAt:0.0}s"
                    : $"filled at {m_DepositedAt:0.0}s";
                return state;
            }

            PlayerRole local = InteractionDebug.LocalRole();

            // Says whose box it is, because a row reading "not fired" on the machine that could
            // never fire it is the one thing in this list that would mislead: there is nothing
            // wrong, it is simply not this player's box.
            if (local != PlayerRole.Unassigned && local != m_Owner)
                state.detail = $"{m_Owner}'s box -- not reachable on this machine";
            else if (m_Mouth == null)
                state.detail = "no trigger inside it";
            else if (m_Object == null)
                state.detail = "nothing wired to put in it";
            else if (!isActiveAndEnabled)
                state.detail = "inactive -- its beat is not running";
            else if (objectInside && objectHeld)
                state.detail = "held over the box -- let go to drop it in";
            else if (objectHeld)
                state.detail = "object in hand, box empty";
            else
                state.detail = "empty";

            return state;
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            if (m_Deposited) return false;

            if (target == DebugFireTarget.Local)
            {
                if (!isActiveAndEnabled) return false;

                // With no role assigned -- a desk test -- both boxes can be filled here, which
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
                Deposit();
                return;
            }

            // Asks the owner's machine to fill its own box. The id names the box, so the other
            // machine fires the same one rather than its local copy of the other player's.
            if (InteractionDebugRelay.TryGetClientForRole(m_Owner, out ulong clientId))
                InteractionDebugRelay.Instance?.FireOnClient(clientId, debugId);
        }

        /// <summary>
        /// Empties the box so the beat can be rehearsed again: the latch, and the object itself,
        /// which goes back to the floor it started on. Leaving it in the box would re-report the
        /// gate on the very next frame. The gate flag it reported belongs to the task, and the
        /// gate's own row is where that is cleared.
        /// </summary>
        public bool DebugCanReset() => m_Deposited;

        public void DebugReset()
        {
            m_Deposited = false;
            m_TimedOut = false;
            m_DepositedAt = -1f;
            m_InsideFor = 0f;

            if (m_Source != null && m_Source.isPlaying) m_Source.Stop();

            if (m_Object == null) return;

            if (m_Body != null)
            {
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }

            m_Object.transform.SetPositionAndRotation(m_ObjectHome, m_ObjectHomeRotation);
        }

        #endregion
    }
}
