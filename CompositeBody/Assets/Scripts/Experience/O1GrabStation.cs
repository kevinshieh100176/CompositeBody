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

        bool m_Grabbed;
        float m_GrabbedAt = -1f;

        /// <summary>
        /// Raised the first time this object is picked up, on the machine it happened on.
        /// <see cref="O1GrabWaves"/> turns that into this player's half of a "both of them are
        /// holding one" task.
        /// </summary>
        public event System.Action<O1GrabStation> onGrabbed;

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
                Debug.LogWarning($"[O1Grab] '{name}' has no cue clip; the object can still be " +
                                 "picked up and put in the box, silently.", this);
        }

        void HandleSelectEntered(SelectEnterEventArgs _) => Grab();

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
                state.detail = $"grabbed at {m_GrabbedAt:0.0}s, cue " +
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
        /// Puts the cue back so the beat can be rehearsed again. Clears the latch only -- where
        /// the object is lying is <see cref="O1DropBox"/>'s row to reset, and the gate flag is
        /// the task's own.
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
