using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

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
    /// Requiring both to be held is what makes this a two-person interaction rather than a fetch
    /// quest. Merging on proximity alone would let one player carry their half to a dropped one
    /// and finish the whole thing with the other player absent.
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

        bool m_Registered;
        bool m_PresentationApplied;

        public string pairId => m_PairId;
        public PlayerRole ownedBy => m_OwnedBy;
        public bool isAnchor => m_IsAnchor;

        /// <summary>True once this half has been welded to its partner.</summary>
        public bool isAssembled => m_PresentationApplied || m_Assembled.Value;

        /// <summary>True while the owning player is holding this half. Server-decided.</summary>
        public bool isHeld => m_Held.Value;

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

            // A client that joins or reconnects after the weld receives the flag in its spawn
            // payload rather than as a change, so the presentation has to be applied here too.
            if (m_Assembled.Value) ApplyAssembledPresentation();
        }

        public override void OnNetworkDespawn()
        {
            m_Assembled.OnValueChanged -= HandleAssembledChanged;
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
        }

        void Update()
        {
            // Only the anchor runs the check, and only on the server. Both halves testing the
            // same condition would race to assemble the pair twice.
            if (!IsSpawned || !m_IsAnchor || !IsServer) return;
            if (m_Assembled.Value) return;

            var partner = FindPartner();
            if (partner == null || !partner.IsSpawned) return;
            if (!m_Held.Value || !partner.m_Held.Value) return;

            // A player who drops out mid-grab leaves their half still flagged as held. Without
            // this the pair could assemble with only one player in the room -- the exact shortcut
            // that requiring both hands exists to close -- and headsets dropping and rejoining is
            // a case this experience plans for, which is the whole reason PlayerIdentity exists.
            if (!IsRoleConnected(m_OwnedBy) || !IsRoleConnected(partner.m_OwnedBy)) return;

            Vector3 target = m_AssemblySocket != null ? m_AssemblySocket.position : transform.position;
            if ((partner.transform.position - target).sqrMagnitude > m_SnapRadius * m_SnapRadius) return;

            ServerAssemble(partner);
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
