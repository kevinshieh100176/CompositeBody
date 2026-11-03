using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using CompositeBody.Diagnostics;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// One half of a two-part object that only exists once both players put it together.
    ///
    /// Two halves sharing a <see cref="m_PairId"/> form a pair. Each is owned by one
    /// <see cref="PlayerRole"/>: its owner sees it normally and can pick it up, while the other
    /// player sees it ghosted through <see cref="RoleGhost"/> and cannot grab it at all. Once
    /// both halves are held -- each by the player who owns it -- and brought within
    /// <see cref="m_SnapRadius"/>, the server welds them into a single assembled object.
    ///
    /// Requiring both players is what makes this a two-person interaction rather than a fetch
    /// quest. Merging on proximity alone would let one player carry their half to a dropped one
    /// and finish the whole thing with the other player absent. <see cref="SnapCondition"/>
    /// decides how strictly that is read: both hands on it at the moment they meet, or both
    /// players having picked their own half up at some point. Either way the pair cannot be
    /// finished by one person, because a half's hold can only be claimed by the role that owns
    /// it -- and the latched reading is also what the show's 「兩個人都抓過了」 lines wait on, which
    /// <see cref="onFirstHeld"/> and <see cref="m_PickedUpTaskId"/> carry.
    ///
    /// The merge decision is server-only and validated against the sender's role, so a client
    /// cannot assemble a pair by claiming to hold a half it does not own. Everything downstream
    /// of the decision -- freezing, un-ghosting, cancelling the grab -- runs on every client off
    /// the replicated <see cref="m_Assembled"/> flag, which also means a headset that reconnects
    /// mid-session finds the pair already assembled instead of having missed the event.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class CompositeHalf : NetworkBehaviour
    {
        /// <summary>What the server insists on before it will weld a pair.</summary>
        public enum SnapCondition
        {
            /// <summary>
            /// Both halves in both players' hands at the moment they meet. The strictest
            /// reading, and the one the chair shipped with.
            /// </summary>
            BothHandsOnIt = 0,

            /// <summary>
            /// Both players have picked their own half up at least once; after that, proximity
            /// alone welds it.
            ///
            /// STILL A TWO-PERSON INTERACTION -- the latch is per role and each player can only
            /// ever set their own -- but it lets one of them put their half down on something
            /// and the other bring theirs to it, which is what people do with two hands and an
            /// object that needs lining up. Under <see cref="BothHandsOnIt"/> that reads as the
            /// mechanic being broken: the halves touch, nothing happens, and the reason is
            /// invisible from inside a headset.
            /// </summary>
            BothHavePickedItUp = 1,
        }

        [Header("Pairing")]
        [SerializeField, Tooltip("Shared by exactly two halves. Anything unique per pair works, e.g. chair_01.")]
        string m_PairId;

        [SerializeField, Tooltip("The only role that sees this half solid and can pick it up.")]
        PlayerRole m_OwnedBy = PlayerRole.Player1;

        [SerializeField, Tooltip("The half that stays put during the weld. Exactly one of the two must be the anchor.")]
        bool m_IsAnchor;

        [Header("Assembly (anchor only)")]
        [SerializeField, Tooltip("Where the other half lands. Its origin is snapped here, so put this wherever the other half's pivot belongs.")]
        Transform m_AssemblySocket;

        [SerializeField, Min(0.01f), Tooltip("How close the other half's origin must get to the socket before the two snap together.")]
        float m_SnapRadius = 0.25f;

        [SerializeField, Tooltip("Reported to StoryProgressManager by both players once assembled. Leave empty for a pair that gates nothing.")]
        string m_CompletedTaskId;

        [Header("Picking up (anchor only)")]
        [SerializeField, Tooltip("What the pair waits for before it will weld. Both Hands On It " +
                                 "is the strict reading; Both Have Picked It Up latches on the " +
                                 "first grab, so a half can be set down and the other brought to it.")]
        SnapCondition m_SnapNeeds = SnapCondition.BothHandsOnIt;

        [SerializeField, Tooltip("Reported by each player the first time they pick their own half " +
                                 "up, so the show can answer 「兩個人都抓過了」 -- a line, a cue, a " +
                                 "gate. Leave empty for a pair nothing waits on.")]
        string m_PickedUpTaskId;

        [Header("References")]
        [SerializeField, Tooltip("Found on this object if left empty.")]
        XRBaseInteractable m_Interactable;
        [SerializeField, Tooltip("Found on this object if left empty.")]
        Rigidbody m_Rigidbody;
        [SerializeField, Tooltip("Found on this object if left empty. Optional -- without one the half is simply always solid.")]
        RoleGhost m_Ghost;

        // Keyed by pair id so a half can find its partner without a scene-level manager to wire
        // up. Halves are hand-placed, and a manager object to place beside them would be one more
        // thing to forget when authoring a new pair.
        static readonly Dictionary<string, List<CompositeHalf>> s_ByPairId = new();

        readonly NetworkVariable<bool> m_Held =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<bool> m_Assembled =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // LATCHED, AND REPLICATED SEPARATELY FROM m_Held, which is the whole reason it exists:
        // 「都有抓過」 is a different question from 「現在拿著」, it is asked on both machines at once,
        // and the half that answers it may have been put down since. A client that joins late
        // gets it in the spawn payload, so a line waiting on both players does not wait forever
        // on a headset that dropped out after its grab.
        readonly NetworkVariable<bool> m_EverHeld =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        bool m_Registered;
        bool m_PresentationApplied;

        // Set the moment a hand closes on this machine, before the server has agreed. ON A DESK
        // WITH NO SESSION that is the only answer there will ever be -- ReportHeldRpc needs a
        // role to validate and there is nobody to give it one -- so a pair dialled in alone
        // still latches, still speaks and still welds.
        bool m_EverHeldLocally;

        // So the first-held news raises its event and sends its report exactly once, whichever
        // of the two paths above brought it.
        bool m_FirstHeldRaised;

        public string pairId => m_PairId;
        public PlayerRole ownedBy => m_OwnedBy;
        public bool isAnchor => m_IsAnchor;

        /// <summary>What this pair waits for before welding. Authored on the anchor.</summary>
        public SnapCondition snapNeeds => m_SnapNeeds;

        /// <summary>The task each player reports on first picking their half up, or empty.</summary>
        public string pickedUpTaskId => m_PickedUpTaskId;

        /// <summary>True once this half has been welded to its partner.</summary>
        public bool isAssembled => m_PresentationApplied || m_Assembled.Value;

        /// <summary>True while the owning player is holding this half. Server-decided.</summary>
        public bool isHeld => m_Held.Value;

        /// <summary>
        /// True once the owning player has picked this half up at least once, and never false
        /// again. Not the same as <see cref="isHeld"/>: this one is what 「都有抓過」 means.
        /// </summary>
        public bool wasEverHeld => m_EverHeldLocally || m_EverHeld.Value;

        /// <summary>
        /// Raised on EVERY machine the first time this half is picked up, off the replicated
        /// latch -- so a pair-level listener counting 「兩個人都抓過了」 sees both players' grabs
        /// wherever it is running, and not only the one that happened in front of it.
        /// </summary>
        public event System.Action<CompositeHalf> onFirstHeld;

        /// <summary>
        /// Raised on every machine once this half is part of an assembled object, after the
        /// freeze and the un-ghosting. What the pair's own content hangs off: the line, the
        /// cue, the journey the finished object makes.
        /// </summary>
        public event System.Action<CompositeHalf> onAssembled;

        void Reset()
        {
            m_Interactable = GetComponent<XRBaseInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();
            m_Ghost = GetComponent<RoleGhost>();
        }

        void Awake()
        {
            if (m_Interactable == null) m_Interactable = GetComponent<XRBaseInteractable>();
            if (m_Rigidbody == null) m_Rigidbody = GetComponent<Rigidbody>();
            if (m_Ghost == null) m_Ghost = GetComponent<RoleGhost>();

            // Single source of truth for who owns the half: the ghost takes its role from here
            // rather than being authored separately, which would be two fields to keep in step
            // and a silent failure -- a half solid for nobody -- once they drift apart.
            // In Awake, so it lands before the base presenter binds to the role list in OnEnable.
            if (m_Ghost != null) m_Ghost.SetSolidFor(m_OwnedBy);
        }

        void OnEnable()
        {
            Register();

            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.AddListener(HandleSelectEntered);
                m_Interactable.selectExited.AddListener(HandleSelectExited);
            }
        }

        void OnDisable()
        {
            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.RemoveListener(HandleSelectEntered);
                m_Interactable.selectExited.RemoveListener(HandleSelectExited);
            }

            Unregister();
        }

        public override void OnNetworkSpawn()
        {
            m_Assembled.OnValueChanged += HandleAssembledChanged;
            m_EverHeld.OnValueChanged += HandleEverHeldChanged;

            // The pair's gate gets a debug row as soon as a half exists. Declared from here
            // rather than from a list somewhere because the task id is authored on the prop:
            // a pair added to a scene later brings its own row with it, and the row is
            // deduplicated by task id so the second half does not add another.
            InteractionDebug.RegisterTaskGate(m_CompletedTaskId,
                $"Assemble '{(string.IsNullOrEmpty(m_PairId) ? "pair" : m_PairId)}' (both halves)");

            // The same, for the gate that only says they have both HELD one. Named from the
            // anchor so the two halves do not register it under two labels, and registered even
            // on the follower -- whichever half spawns first brings the row in.
            CompositeHalf namer = m_IsAnchor ? this : FindPartner();
            string pickedUp = namer != null ? namer.m_PickedUpTaskId : m_PickedUpTaskId;
            InteractionDebug.RegisterTaskGate(pickedUp,
                $"Pick up both halves of '{(string.IsNullOrEmpty(m_PairId) ? "pair" : m_PairId)}'");

            // A client that joins or reconnects after the weld receives the flag in its spawn
            // payload rather than as a change, so the presentation has to be applied here too.
            if (m_EverHeld.Value) HandleEverHeldChanged(false, true);
            if (m_Assembled.Value) ApplyAssembledPresentation();
        }

        public override void OnNetworkDespawn()
        {
            m_Assembled.OnValueChanged -= HandleAssembledChanged;
            m_EverHeld.OnValueChanged -= HandleEverHeldChanged;
        }

        void Register()
        {
            if (m_Registered || string.IsNullOrEmpty(m_PairId)) return;

            if (!s_ByPairId.TryGetValue(m_PairId, out var halves))
            {
                halves = new List<CompositeHalf>();
                s_ByPairId[m_PairId] = halves;
            }
            halves.Add(this);
            m_Registered = true;
        }

        void Unregister()
        {
            if (!m_Registered) return;

            if (s_ByPairId.TryGetValue(m_PairId, out var halves))
            {
                halves.Remove(this);
                if (halves.Count == 0) s_ByPairId.Remove(m_PairId);
            }
            m_Registered = false;
        }

        /// <summary>The other half sharing this one's pair id, or null while it is not in the scene.</summary>
        public CompositeHalf FindPartner()
        {
            if (string.IsNullOrEmpty(m_PairId)) return null;
            if (!s_ByPairId.TryGetValue(m_PairId, out var halves)) return null;

            foreach (var half in halves)
            {
                if (half != null && half != this) return half;
            }
            return null;
        }

        void HandleSelectEntered(SelectEnterEventArgs args)
        {
            // A socket holding the half is not a player holding it, and letting one count would
            // mean a pair could assemble itself out of two racks with nobody in the room.
            if (args.interactorObject is XRSocketInteractor) return;
            ReportHeld(true);

            // Locally too, and not only through the server: this is the machine the hand is on,
            // the latch is the thing a line waits for, and a desk test has no server to agree.
            // The replicated flag arrives a trip later and finds the latch already down.
            LatchEverHeld();
        }

        void HandleSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;

            // Another interactor may still have it -- a two-handed grab handing off between hands
            // should not read as a release.
            if (m_Interactable != null && m_Interactable.isSelected) return;
            ReportHeld(false);
        }

        void ReportHeld(bool held)
        {
            if (!IsSpawned) return;
            ReportHeldRpc(held);
        }

        /// <summary>
        /// The local player says it has picked up or let go of this half. The claim is checked
        /// against the sender's role before it is believed, so a client reporting a hold on the
        /// other player's half -- whether through a mirrored interaction event or deliberately --
        /// cannot move the pair any closer to assembling.
        /// </summary>
        [Rpc(SendTo.Server)]
        void ReportHeldRpc(bool held, RpcParams rpcParams = default)
        {
            if (GameSessionManager.Instance == null) return;
            if (!GameSessionManager.Instance.TryGetRoleForClient(rpcParams.Receive.SenderClientId, out PlayerRole role)) return;
            if (role != m_OwnedBy) return;

            m_Held.Value = held;

            // One way only. The pair's own content asks 「都有抓過嗎」 and a half put back down
            // has still been picked up -- clearing this on release would retract a line that
            // has already been spoken.
            if (held) m_EverHeld.Value = true;
        }

        /// <summary>
        /// Raises <see cref="onFirstHeld"/> once and reports this player's half of the pick-up
        /// gate, however the news arrived -- the hand on this machine or the server's flag.
        /// </summary>
        void LatchEverHeld()
        {
            m_EverHeldLocally = true;
            if (m_FirstHeldRaised) return;
            m_FirstHeldRaised = true;

            ReportPickedUpIfMine();

            // Last, for the same reason Grab() raises its event last: a listener that throws
            // must not leave the latch half down and send the report again on the next grab.
            onFirstHeld?.Invoke(this);
        }

        void HandleEverHeldChanged(bool previous, bool current)
        {
            if (current) LatchEverHeld();
        }

        /// <summary>
        /// 「我抓過我的那半了」, reported by the player who owns this half and nobody else, so the
        /// server's gate is two separate claims from two machines exactly as it expects.
        /// </summary>
        void ReportPickedUpIfMine()
        {
            CompositeHalf anchor = m_IsAnchor ? this : FindPartner();
            string taskId = anchor != null ? anchor.m_PickedUpTaskId : null;
            if (string.IsNullOrEmpty(taskId)) return;

            if (StoryProgressManager.Instance == null || GameSessionManager.Instance == null) return;
            if (!GameSessionManager.Instance.TryGetLocalRole(out PlayerRole role) || role != m_OwnedBy) return;

            StoryProgressManager.Instance.ReportTaskCompleteRpc(taskId);
        }

        void Update()
        {
            // Only the anchor runs the check, and only on the server. Both halves testing the
            // same condition would race to assemble the pair twice.
            if (!IsSpawned || !m_IsAnchor || !IsServer) return;
            if (m_Assembled.Value) return;

            var partner = FindPartner();
            if (partner == null || !partner.IsSpawned) return;
            if (!BothPlayersHaveDoneTheirPart(partner)) return;

            // A player who drops out mid-grab leaves their half still flagged as held. Without
            // this the pair could assemble with only one player in the room -- the exact shortcut
            // that requiring both hands exists to close -- and headsets dropping and rejoining is
            // a case this experience plans for, which is the whole reason PlayerIdentity exists.
            if (!IsRoleConnected(m_OwnedBy) || !IsRoleConnected(partner.m_OwnedBy)) return;

            Vector3 target = m_AssemblySocket != null ? m_AssemblySocket.position : transform.position;
            if ((partner.transform.position - target).sqrMagnitude > m_SnapRadius * m_SnapRadius) return;

            ServerAssemble(partner);
        }

        /// <summary>
        /// Whether both players have done whatever this pair asks of them, which is the only
        /// part of the merge decision the two conditions disagree about.
        /// </summary>
        bool BothPlayersHaveDoneTheirPart(CompositeHalf partner)
        {
            if (m_SnapNeeds == SnapCondition.BothHandsOnIt)
                return m_Held.Value && partner.m_Held.Value;

            if (m_EverHeld.Value && partner.m_EverHeld.Value) return true;

            // AND WHAT STAFF HAVE FORCED, because the latch is exactly what a stuck pair is
            // stuck on: a grab that never reported leaves two halves that touch and do nothing,
            // and the panel's gate row is the rescue. Reading the gate here means forcing it
            // releases the weld as well as the line, rather than only half the beat.
            return !string.IsNullOrEmpty(m_PickedUpTaskId) &&
                   StoryProgressManager.Instance != null &&
                   StoryProgressManager.Instance.IsTaskComplete(m_PickedUpTaskId);
        }

        static bool IsRoleConnected(PlayerRole role)
        {
            if (GameSessionManager.Instance == null) return false;

            foreach (var entry in GameSessionManager.Instance.playerRoles)
            {
                if (entry.role == role) return entry.isConnected;
            }
            return false;
        }

        void ServerAssemble(CompositeHalf follower)
        {
            // Hand both halves back to the server before moving anything. The grabbing clients
            // own them at this point, and with a client-authoritative transform anything the
            // server writes to the pose is overwritten by the owner's next update.
            ulong serverId = NetworkManager.ServerClientId;
            if (NetworkObject.OwnerClientId != serverId) NetworkObject.ChangeOwnership(serverId);
            if (follower.NetworkObject.OwnerClientId != serverId) follower.NetworkObject.ChangeOwnership(serverId);

            // Parent to the anchor's root, not to the socket: TrySetParent looks for a
            // NetworkObject on the transform it is handed and refuses anything without one, and
            // the socket is a plain child transform. The socket still decides the pose below.
            bool parented = follower.NetworkObject.TrySetParent(transform, false);
            if (!parented)
            {
                Debug.LogWarning($"[CompositeHalf] Pair {m_PairId} could not be parented; falling " +
                                 "back to a world-space snap. Both halves need a NetworkObject.");
            }

            SnapToSocket(follower, parented);

            m_Assembled.Value = true;
            follower.m_Assembled.Value = true;
        }

        void SnapToSocket(CompositeHalf follower, bool parented)
        {
            Transform socket = m_AssemblySocket != null ? m_AssemblySocket : transform;

            if (parented)
            {
                // Parented with worldPositionStays false, so the local pose is whatever it was
                // under the old parent and has to be set outright. Derived from the socket's pose
                // in the anchor's space rather than read off localPosition, so the socket can sit
                // any number of levels deep in the anchor's hierarchy.
                follower.transform.SetLocalPositionAndRotation(
                    transform.InverseTransformPoint(socket.position),
                    Quaternion.Inverse(transform.rotation) * socket.rotation);
            }
            else
            {
                follower.transform.SetPositionAndRotation(socket.position, socket.rotation);
            }
        }

        void HandleAssembledChanged(bool previous, bool current)
        {
            if (current) ApplyAssembledPresentation();
        }

        /// <summary>
        /// Everything a client does once its half is part of an assembled object. Runs on every
        /// client including the host, because XR interaction and rendering are local concerns --
        /// the server freezing its own copy would leave the other player still holding theirs.
        /// </summary>
        void ApplyAssembledPresentation()
        {
            if (m_PresentationApplied) return;
            m_PresentationApplied = true;

            // Take it out of the player's hand first. An interactor that is still selecting keeps
            // writing the transform, and would drag the freshly welded half straight back out.
            if (m_Interactable != null && m_Interactable.isSelected && m_Interactable.interactionManager != null)
                m_Interactable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)m_Interactable);

            // Both halves stay separate kinematic bodies rather than being consolidated into one.
            // Removing the follower's Rigidbody is what "welded into a single body" would mean
            // literally, but XRGrabInteractable requires one, so Unity refuses to remove it while
            // the component is there -- and with the assembly frozen as scenery, two frozen bodies
            // and one behave identically anyway.
            if (m_Rigidbody != null) m_Rigidbody.isKinematic = true;

            // Un-ghost before disabling the interactable, not after: RoleGhost re-enables the
            // interactable whenever it goes solid, so the reverse order would quietly hand the
            // finished object back to the players.
            if (m_Ghost != null) m_Ghost.SetSolidForEveryone();

            if (m_Interactable != null) m_Interactable.enabled = false;

            ReportTaskCompleteIfMine();

            // Last, after the object is frozen, solid for everyone and out of both hands: a
            // listener that moves the finished assembly is entitled to assume nothing is still
            // holding on to it.
            onAssembled?.Invoke(this);
        }

        /// <summary>
        /// Each player reports their own half. StoryProgressManager unlocks a beat only once both
        /// roles have reported it, and an assembled pair is by construction the work of both --
        /// so the two halves reporting separately is exactly the signal it expects.
        /// </summary>
        void ReportTaskCompleteIfMine()
        {
            var anchor = m_IsAnchor ? this : FindPartner();
            string taskId = anchor != null ? anchor.m_CompletedTaskId : null;
            if (string.IsNullOrEmpty(taskId)) return;

            if (StoryProgressManager.Instance == null || GameSessionManager.Instance == null) return;
            if (!GameSessionManager.Instance.TryGetLocalRole(out PlayerRole role) || role != m_OwnedBy) return;

            StoryProgressManager.Instance.ReportTaskCompleteRpc(taskId);
        }

        /// <summary>
        /// Welds the pair with no session running. For the editor scene builder, and for dialling
        /// a pair in without standing up a host and two headsets -- the same reason
        /// <see cref="PlayerTether.Tick"/> is public.
        /// </summary>
        [ContextMenu("Assemble Locally")]
        public void AssembleLocally() => AssembleLocally(FindPartner());

        /// <summary>
        /// As <see cref="AssembleLocally()"/>, but told which half to weld to rather than looking
        /// it up. The registry is filled by OnEnable, which does not run in edit mode, so the
        /// scene builder has to hand over the partner it just created.
        /// </summary>
        public void AssembleLocally(CompositeHalf partner)
        {
            if (partner == null)
            {
                Debug.LogWarning($"[CompositeHalf] No partner for pair {m_PairId}.");
                return;
            }

            CompositeHalf anchor = m_IsAnchor ? this : partner;
            CompositeHalf follower = m_IsAnchor ? partner : this;

            if (!anchor.m_IsAnchor)
            {
                Debug.LogWarning($"[CompositeHalf] Neither half of pair {m_PairId} is marked as the anchor.");
                return;
            }

            follower.transform.SetParent(anchor.transform, false);
            anchor.SnapToSocket(follower, true);

            anchor.ApplyAssembledPresentation();
            follower.ApplyAssembledPresentation();
        }

        void OnValidate()
        {
            // An anchor with no socket would snap the other half onto its own pivot, which is
            // rarely where it belongs but is at least a visible, adjustable starting point.
            if (m_IsAnchor && m_AssemblySocket == null) m_AssemblySocket = transform;
        }

        void OnDrawGizmosSelected()
        {
            if (!m_IsAnchor) return;

            Transform socket = m_AssemblySocket != null ? m_AssemblySocket : transform;
            Gizmos.color = new Color(0.42f, 0.72f, 1f, 0.9f);
            Gizmos.DrawWireSphere(socket.position, m_SnapRadius);
        }
    }
}
