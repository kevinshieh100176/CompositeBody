using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// O-1, first half: one player picks their object up off the floor and a cue sounds. Player
    /// A has a coat and S0-1-A, Player B a mug and S0-1-B.
    ///
    /// THE CUE IS LOCAL, like the object it belongs to. Neither player can see the other's
    /// object -- RoleVisibility switches off its renderers AND its colliders -- so a grab can
    /// only ever happen on the machine that owns it, and each player hears their own cue and not
    /// the other's. Nothing about the sound crosses the network, which is the same decision
    /// <see cref="O1GrabStation"/>'s object already embodies: there is no shared state here to
    /// disagree about.
    ///
    /// THE GRAB DOES NOT REPORT THE BEAT'S GATE. Picking the object up is half of what the
    /// player is asked to do -- the other half is putting it in the box -- and that gate belongs
    /// to the half that finishes it: <see cref="O1DropBox"/> reports it, and carries the
    /// fallback that eventually clears it. A grab that reported the gate would come off the hold
    /// with a lamp still swinging from someone's hand.
    ///
    /// IT DOES RAISE <see cref="onGrabbed"/>, which is a different thing. 「兩人必須都抓住物件才會
    /// 觸發VO，要不然只有音效」 -- the beat's speech waits for BOTH players to have their object in
    /// hand, and that is a fact about two machines. <see cref="O1GrabWaves"/> listens here,
    /// reports this player's half through <c>StoryProgressManager</c>, and speaks when the
    /// server says both halves are in. This component still knows nothing about the other
    /// player, which is the point.
    ///
    /// FIRST GRAB ONLY. The object is meant to be picked up, put down and picked up again; the
    /// cue is not. A second grab replaying a five-second sound over the top of the beat's own
    /// audio is the kind of thing that reads as a bug from inside a headset.
    ///
    /// AND IT HOLDS THE OBJECT STILL UNTIL SOMEBODY TAKES IT, which is the only part of this
    /// component that is not about the cue -- it lives here because this is what already knows
    /// which object belongs to the station, and the alternative was a second component on four
    /// props that a builder has to remember to add. See <see cref="FixedUpdate"/> for what
    /// floating did before and why kinematic is the fix.
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

        [SerializeField, Range(0f, 1f), Tooltip("1 is fully positioned in the room, which is " +
                                                "what a sound coming off an object you are " +
                                                "holding wants to be. The clips are mono for " +
                                                "exactly this reason.")]
        float m_Spatial = 1f;

        [SerializeField, Min(0.1f), Tooltip("Metres beyond which the cue is inaudible.")]
        float m_MaxDistance = 12f;

        [SerializeField, Tooltip("Hold the object exactly where it was put until the first time " +
                                 "a player lets go of it, so reaching for it cannot send it " +
                                 "anywhere. Off is what the beat shipped with -- see the " +
                                 "remarks on Hold().")]
        bool m_SteadyUntilReleased = true;

        [SerializeField, Min(0f), Tooltip("Linear damping once the object is physical. The " +
                                          "props are meant to be put down, not slid across the " +
                                          "room, and XRI restores this value after every grab.")]
        float m_LinearDamping = 1.5f;

        [SerializeField, Min(0f), Tooltip("Angular damping once the object is physical, so a " +
                                          "lamp knocked on the floor stops rolling.")]
        float m_AngularDamping = 1f;

        bool m_Grabbed;
        float m_GrabbedAt = -1f;

        // The body under m_Grab, and the pose it was authored at -- kept as a PARENT AND A LOCAL
        // POSE rather than a world one, because neither end of that is stable on its own. The
        // station is a child of a beat root the show is free to move, so a world pose recorded
        // in Awake would put the object back where the room used to be; and XRI unparents a
        // grabbed object (m_UnparentTransformOnGrab) and reparents it on release, so a local
        // pose read while it is in a hand is a world pose wearing the wrong name.
        Rigidbody m_Body;
        Transform m_HomeParent;
        Vector3 m_Home;
        Quaternion m_HomeRotation;

        // Whether a player has ever let go of it, which is when it stops floating and becomes an
        // ordinary object with gravity -- XRI's forceGravityOnDetach. Latched, so a station
        // switched off and on again mid-beat does not take the lamp back out of the player's
        // hands and hang it in the air.
        bool m_Released;

        /// <summary>
        /// Raised the first time this object is picked up, on the machine it happened on.
        /// <see cref="O1GrabWaves"/> turns that into this player's half of a "both of them are
        /// holding one" task.
        /// </summary>
        public event System.Action<O1GrabStation> onGrabbed;

        /// <summary>Whether this object has been picked up yet, on this machine.</summary>
        public bool grabbed => m_Grabbed;

        /// <summary>
        /// Whether it is in a hand <em>right now</em>, which is a different question from
        /// <see cref="grabbed"/>: that one latches on the first pick-up and never clears, because
        /// it exists to say the cue has played. This one goes back down when the object is put
        /// down, and it is what the TouchDesigner stream sends.
        /// </summary>
        public bool held => m_Grab != null && m_Grab.isSelected;

        /// <summary>
        /// Whether a player has let go of it at least once, which is the moment it stops
        /// floating: before that it is pinned where the builder put it, after it falls like
        /// anything else.
        /// </summary>
        public bool released => m_Released;

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
        }

        void OnDestroy() => InteractionDebug.Unregister(this);

        void OnEnable()
        {
            if (m_Grab == null) return;

            m_Grab.selectEntered.AddListener(HandleSelectEntered);
            m_Grab.selectExited.AddListener(HandleSelectExited);
        }

        void OnDisable()
        {
            if (m_Grab == null) return;

            m_Grab.selectEntered.RemoveListener(HandleSelectEntered);
            m_Grab.selectExited.RemoveListener(HandleSelectExited);
        }

        void Resolve()
        {
            if (m_Grab == null) m_Grab = GetComponentInChildren<XRGrabInteractable>(true);

            if (m_Grab != null)
            {
                m_Body = m_Grab.GetComponent<Rigidbody>();
                m_HomeParent = m_Grab.transform.parent;
                m_Grab.transform.GetLocalPositionAndRotation(out m_Home, out m_HomeRotation);
            }

            if (m_Body != null)
            {
                // Set here rather than left to the prefab, so a station rebuilt by BuildO1Grab
                // or wired by hand cannot ship with the old frictionless settings. XRI stores
                // and restores both values around every grab, so this survives being picked up.
                m_Body.linearDamping = m_LinearDamping;
                m_Body.angularDamping = m_AngularDamping;

                // From the first frame of the show, and from Awake rather than OnEnable: a
                // station whose beat has not started yet is switched off, and the object under
                // it is in the room all the same -- invisible, its grab off, and still solid
                // enough to be knocked out of the place it is going to fade in at.
                if (m_SteadyUntilReleased && !m_Released) Hold();
            }

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
                Debug.LogWarning($"[O1Grab] '{name}' has no cue clip; the object can still be " +
                                 "picked up and put in the box, silently.", this);
        }

        void HandleSelectEntered(SelectEnterEventArgs _) => Grab();

        /// <summary>
        /// The first time a hand opens, the object stops being scenery and becomes an object:
        /// XRI has already OR-ed <c>forceGravityOnDetach</c> into useGravity by this point, and
        /// restored the kinematic flag it found -- which, with the hold below, is the hold's own.
        /// So this is where that is undone, and it is latched: the lamp falls from wherever the
        /// player let go of it and is never pinned again.
        /// </summary>
        void HandleSelectExited(SelectExitEventArgs _)
        {
            if (m_Released) return;
            m_Released = true;
            Loosen();
        }

        /// <summary>
        /// WHY THE FLOAT HAS TO BE HELD. 「浮在箱子正上方」 is a non-kinematic body with gravity
        /// off, which has no forces on it and therefore stays where it was put -- and that is
        /// exactly as true of a push as it is of gravity. A hand, an elbow or a knee catching a
        /// prop on the way past gave it a velocity nothing would ever take away again: no
        /// gravity to pull it down, no drag to slow it, so it sailed off across the room at
        /// walking pace and could not be reached. It happened for the objects of the LATER waves
        /// too, which sit in the room at presence 0 -- invisible, their grab switched off, and
        /// still solid, so a player could knock a jacket they could not see out of the place it
        /// was going to fade in at.
        ///
        /// SO IT IS KINEMATIC UNTIL IT IS LET GO OF. A kinematic body cannot be pushed at all,
        /// which is the whole requirement, and it is still perfectly grabbable -- XRI's own
        /// Instantaneous movement type makes a held body kinematic anyway. What made this look
        /// unavailable is that XRI restores the kinematic flag on release, which would leave the
        /// lamp hanging in mid-air wherever the hand opened; <see cref="HandleSelectExited"/> is
        /// the answer to that, and it is two lines.
        ///
        /// IT RESISTS FORCES, NOT INTENT. The hold does not drag the object back to where it was
        /// authored -- anything that MEANS to move it still can, which is the harness placing a
        /// prop in a carton to test the trigger, and <see cref="ReturnHome"/> fetching one that
        /// has been kicked across the room. A pose this re-asserted every physics step would
        /// fight both of them, and the second of those is the staff rescue.
        ///
        /// AND IT IS SET IN <see cref="Resolve"/> TOO, not only here, because a station whose
        /// beat has not started is a DISABLED component with a live collider under it: the later
        /// waves' objects sit in the room from the first frame of the show at presence 0, and
        /// FixedUpdate does not run on a component that is switched off.
        /// </summary>
        void FixedUpdate()
        {
            if (m_Body == null || !m_SteadyUntilReleased) return;

            // In a hand: XRI owns the body, and it will hand it back on release.
            if (m_Grab != null && m_Grab.isSelected) return;

            if (m_Released)
            {
                // Belt and braces, and ORDER-INDEPENDENT: if the release event were ever missed
                // -- an interactor destroyed mid-grab, a station switched off while held -- the
                // object would otherwise be left kinematic, hanging in the air and impossible to
                // put in a box.
                Loosen();
                return;
            }

            Hold();
        }

        /// <summary>Floating and unpushable, wherever it is standing.</summary>
        void Hold()
        {
            if (m_Body == null) return;

            if (!m_Body.isKinematic)
            {
                // Before the flag, not after: velocity cannot be written to a kinematic body,
                // and a leftover drift would otherwise come back the moment it is released.
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
                m_Body.isKinematic = true;
            }

            if (m_Body.useGravity) m_Body.useGravity = false;
        }

        /// <summary>Where it was authored, in the room as the room is now.</summary>
        Vector3 HomePosition() =>
            m_HomeParent != null ? m_HomeParent.TransformPoint(m_Home) : m_Home;

        Quaternion HomeRotation() =>
            m_HomeParent != null ? m_HomeParent.rotation * m_HomeRotation : m_HomeRotation;

        /// <summary>An ordinary object again: dynamic, with gravity, and at rest.</summary>
        void Loosen()
        {
            if (m_Body == null) return;

            if (m_Body.isKinematic)
            {
                m_Body.isKinematic = false;
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }

            if (!m_Body.useGravity) m_Body.useGravity = true;
        }

        /// <summary>
        /// Put the object back where it floated and let it float again -- the staff rescue for a
        /// prop that has ended up somewhere nobody can reach, which is the one thing the hold
        /// cannot prevent once the object is physical and a player has kicked it.
        ///
        /// It does NOT clear the cue latch or anything the box has seen; those are their own
        /// rows' business.
        /// </summary>
        public void ReturnHome()
        {
            if (m_Grab == null) return;

            // Not out of a player's hand. Staff pressing reset while someone is holding the
            // lamp mean "put the beat back", not "snatch it off them" -- and XRI would drag it
            // straight back to the hand on the next frame anyway, so the teleport would only
            // make the object jump.
            if (held)
            {
                Debug.Log($"[O1Grab] '{name}' is in a hand, so it was left there.", this);
                return;
            }

            if (m_Body != null && !m_Body.isKinematic)
            {
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }

            m_Grab.transform.SetPositionAndRotation(HomePosition(), HomeRotation());

            // Floating again, which is also what makes it reachable: a prop put back at the
            // height it was authored at with gravity still on would simply fall to the floor.
            m_Released = false;
            if (m_SteadyUntilReleased && m_Body != null) Hold();

            Debug.Log($"[O1Grab] '{name}' put back where it was floating.", this);
        }

        /// <summary>Metres the object is from where it was authored, for the debug row.</summary>
        float AwayFromHome() =>
            m_Grab == null ? 0f : Vector3.Distance(m_Grab.transform.position, HomePosition());

        /// <summary>
        /// The grab itself: the cue, and nothing else. Idempotent -- the object can be picked up
        /// as often as the player likes and this happens once.
        /// </summary>
        public void Grab()
        {
            if (m_Grabbed) return;
            m_Grabbed = true;
            m_GrabbedAt = ExperienceClock.now;

            if (m_Source != null && m_Cue != null) m_Source.Play();

            Debug.Log($"[O1Grab] '{name}' grabbed by {m_Owner} at {m_GrabbedAt:0.00}s; " +
                      $"cue {(m_Cue != null ? m_Cue.name : "none")}. The gate waits on the box.",
                      this);

            // Last, so a listener that throws cannot leave this station un-latched and replay
            // the cue on the next grab.
            onGrabbed?.Invoke(this);
        }

        #region Debug panel

        // Per station, not per machine: both machines carry both stations, and the id has to name
        // the OBJECT so a button pressed on one screen fires the station that player actually
        // owns rather than the local copy of someone else's.
        //
        // AND THE OBJECT, NOT JUST ITS OWNER. This was 'o1.grab.{owner}' while each player had
        // exactly one thing to pick up. O-1 now shows them several in turn, so that id named two
        // stations at once -- and every lookup of it (DebugKeys, the panel's remote fire,
        // InteractionDebugTestRunner) takes the FIRST match, which meant the second object could
        // be seen in the list but never fired from a key or from the other machine. Half-broken
        // and silent, which is the worst way for a rescue button to be broken.
        public string debugId => $"o1.grab.{m_Owner}.{ObjectKey()}";

        /// <summary>
        /// The grabbable's own name -- "Lamp", "Jacket" -- which is what tells this player's
        /// stations apart. The builders own these names, and both machines build from the same
        /// prefab, so the id means the same thing on each of them.
        ///
        /// AND THE SAME ANSWER BEFORE Awake, which is why the fallback strips the role off this
        /// station's own name rather than using it whole. <see cref="m_Grab"/> is found in
        /// <see cref="Resolve"/> and is not serialised, so a station read in the editor would
        /// otherwise answer "Lamp (Player A)" and the same station read in play mode "Lamp" --
        /// an id that changes under you is worse than one that collides, because it looks fine
        /// in both places.
        /// </summary>
        string ObjectKey()
        {
            if (m_Grab != null) return m_Grab.name;

            int role = name.IndexOf(" (", System.StringComparison.Ordinal);
            return role > 0 ? name.Substring(0, role) : name;
        }

        // The object's own name, which already says whose it is -- "Lamp (Player A)". Naming it
        // after its cue instead read as "Grab the S0-1-A object", which is the one thing in the
        // list a person would have to translate back.
        public string debugLabel => $"Grab the {name}";

        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        public string debugFireLabel => "Grab";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = m_Grabbed;

            if (m_Grabbed)
            {
                float away = AwayFromHome();

                state.detail = $"grabbed at {m_GrabbedAt:0.0}s, cue " +
                               $"{(m_Source != null && m_Source.isPlaying ? "playing" : "done")}" +
                               (held ? ", in hand" : $", {away:0.0} m from where it floated");

                // The row that answers "they say they cannot find it". A prop a metre or two out
                // is a prop that has been put down somewhere; one across the room was kicked,
                // and reset is what brings it back.
                if (!held && away > 2f) state.detail += " -- RESET BRINGS IT BACK";
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
            else if (!m_Grab.enabled)
                state.detail = "grab switched off -- it has not faded in yet";
            else
                state.detail = m_SteadyUntilReleased
                    ? "floating where it was put, held still until it is picked up"
                    : "waiting to be picked up";

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

            // BOTH MEANS "WHEREVER THIS ONE LIVES", not "both players": an object has one owner,
            // so there is no second half of it to fire. The button was dead on these rows, which
            // on the host -- the machine with the panel in front of it -- is the one button a
            // person reaches for when they do not want to work out whose lamp this is.
            DebugFireTarget mine = m_Owner == PlayerRole.Player1
                ? DebugFireTarget.Player1
                : DebugFireTarget.Player2;

            if (target != mine && target != DebugFireTarget.Both) return false;

            return InteractionDebugRelay.canForce &&
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
            // FireOnClient routes a request addressed to this machine back to itself, so the
            // host firing its own object this way takes the same path as the real grab.
            if (InteractionDebugRelay.TryGetClientForRole(m_Owner, out ulong clientId))
                InteractionDebugRelay.Instance?.FireOnClient(clientId, debugId);
        }

        /// <summary>
        /// Puts the cue back so the beat can be rehearsed again, AND puts the object back where
        /// it was floating. The second half is the staff rescue: 「道具飛出去不好抓」 -- once a prop
        /// is physical a player can kick it under the water or out of the room, and this is the
        /// one button that fetches it. The gate flag is the task's own row to clear.
        /// </summary>
        public bool DebugCanReset() => m_Grabbed || (!held && AwayFromHome() > 0.01f);

        public void DebugReset()
        {
            m_Grabbed = false;
            m_GrabbedAt = -1f;
            if (m_Source != null && m_Source.isPlaying) m_Source.Stop();

            ReturnHome();
        }

        #endregion
    }
}
