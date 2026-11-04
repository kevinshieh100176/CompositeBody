using System;
using Unity.Netcode;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Interaction.ObjectAssembly
{
    /// <summary>
    /// One half of the ObjectAssembly lamp: grabbable, claimable, and -- once claimed -- a thing
    /// that stays with its player.
    ///
    /// CLAIM BY GRAB, FIRST GRAB WINS. The server writes <see cref="claimedBy"/> on the first grab
    /// it grants. From then on only the claimant may pick it up, and the claimant may not pick up
    /// the other half: one half per player. The same rule runs twice -- locally in
    /// <see cref="LocalCanSelect"/>, so a refused hand never closes at all, and on the server in
    /// <see cref="ServerCanGrant"/>, which is the one that settles a race.
    ///
    /// RELEASE DOES NOT GIVE IT BACK (<see cref="reclaimOnRelease"/> is false). Ownership stays
    /// with the claimant, whose machine then FOLLOWS: the half drifts to a spot beside and in front
    /// of that player's head, damped and bobbing, and the ClientNetworkTransform carries that pose
    /// to the other headset. Nothing else drives a claimed half while it is not in a hand.
    ///
    /// AFTER THE JOIN EVERY MACHINE ANIMATES IT LOCALLY. <see cref="OAController"/> calls
    /// <see cref="BeginLocalAnimation"/> on both halves on every machine and then writes their
    /// poses from replicated stamps, so the network transform is switched off everywhere and the
    /// glide and the float are identical on both headsets without being sent.
    ///
    /// A claimant who disconnects frees the half again where it is, so the other player -- or the
    /// same one rejoining -- can pick it up.
    /// </summary>
    public class OAFragment : NetGrabbable
    {
        /// <summary>
        /// <see cref="claimedBy"/> for a claim the debug panel made on behalf of a role with no
        /// machine connected (a solo rehearsal). Nobody can grab it; it just counts as claimed.
        /// </summary>
        public const ulong ForcedClaim = ulong.MaxValue - 1;

        [Header("Assembly")]
        [SerializeField] OAController m_Controller;

        [SerializeField, Tooltip("Where this half meets the other one. Both halves' join points " +
                                 "coincide in the assembled layout.")]
        Transform m_JoinPoint;

        [SerializeField, Tooltip("The breathing / tremble / reveal view on the Visual child.")]
        BreathingView m_View;

        [SerializeField, Tooltip("This root's position in the assembled layout, relative to the " +
                                 "assembly frame (origin at the join point). Written by the builder.")]
        Vector3 m_LayoutPosition;

        [SerializeField, Tooltip("This root's rotation in the assembled layout. Written by the builder.")]
        Quaternion m_LayoutRotation = Quaternion.identity;

        [Header("Follow the claimant")]
        [SerializeField, Tooltip("Where the half floats when its claimant is not holding it, in " +
                                 "the head's yaw frame: x right, y up, z forward (metres).")]
        Vector3 m_FollowOffset = new(0.24f, -0.3f, 0.42f);

        [SerializeField, Min(0.01f), Tooltip("Seconds for the follow to close most of the gap. " +
                                             "Lazy on purpose: it trails the player, it is not glued on.")]
        float m_FollowSmoothTime = 0.6f;

        [SerializeField, Min(0.01f), Tooltip("Seconds for its facing to catch up with the head's yaw.")]
        float m_FollowTurnTime = 1.2f;

        [SerializeField, Min(0f), Tooltip("Metres of vertical bob while following.")]
        float m_FollowBob = 0.018f;

        [SerializeField, Min(0.2f)] float m_FollowBobPeriod = 3.7f;

        [SerializeField, Min(0.1f), Tooltip("Speed cap while following, metres per second.")]
        float m_FollowMaxSpeed = 2.5f;

        readonly NetworkVariable<ulong> m_ClaimedBy =
            new(NoHolder, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<byte> m_ClaimedRole =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<double> m_ClaimedAt =
            new(InteractionPhaseClock.Unset, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        bool m_LocallyAnimated;
        Vector3 m_FollowVelocity;
        Camera m_Head;
        float m_BobPhase;

        // ---- public surface --------------------------------------------------------------------

        /// <summary>Client id of the claimant, <see cref="NetGrabbable.NoHolder"/>, or <see cref="ForcedClaim"/>.</summary>
        public ulong claimedBy => m_ClaimedBy.Value;

        public bool isClaimed => m_ClaimedBy.Value != NoHolder;

        /// <summary>Which role claimed it (Unassigned when unclaimed).</summary>
        public PlayerRole claimedRole => (PlayerRole)m_ClaimedRole.Value;

        /// <summary>Server time of the claim, or <see cref="InteractionPhaseClock.Unset"/>.</summary>
        public double claimedAt => m_ClaimedAt.Value;

        /// <summary>True when this machine is the claimant.</summary>
        public bool claimedLocally => IsSpawned && isClaimed && m_ClaimedBy.Value == localClientId;

        public Transform joinPoint => m_JoinPoint;
        public BreathingView view => m_View;
        public OAController controller => m_Controller;

        /// <summary>This root's pose in the assembled layout, relative to the assembly frame.</summary>
        public Pose layout => new(m_LayoutPosition, m_LayoutRotation);

        /// <summary>True while the controller is writing this half's pose (joined, floating).</summary>
        public bool locallyAnimated => m_LocallyAnimated;

        /// <summary>True while this machine is driving the follow.</summary>
        public bool following { get; private set; }

        /// <summary>Raised on every machine when the claim changes.</summary>
        public event Action<OAFragment> onClaimChanged;

        /// <summary>Builder hook.</summary>
        public void Configure(OAController controller, Transform joinPoint, BreathingView view, Pose layoutPose, Vector3 followOffset)
        {
            m_Controller = controller;
            m_JoinPoint = joinPoint;
            m_View = view;
            m_LayoutPosition = layoutPose.position;
            m_LayoutRotation = layoutPose.rotation;
            m_FollowOffset = followOffset;
        }

        // ---- rules -----------------------------------------------------------------------------

        protected override bool reclaimOnRelease => false;

        protected override bool LocalCanSelect()
        {
            if (m_LocallyAnimated) return false;
            if (m_Controller == null || !m_Controller.grabWindowOpen) return false;
            if (!IsSpawned) return true;

            ulong me = localClientId;
            ulong claimant = m_ClaimedBy.Value;
            if (claimant != NoHolder) return claimant == me;
            return !m_Controller.HasClaim(me, this);
        }

        protected override bool ServerCanGrant(ulong sender)
        {
            if (m_LocallyAnimated) return false;
            if (m_Controller == null || !m_Controller.grabWindowOpen) return false;

            ulong claimant = m_ClaimedBy.Value;
            if (claimant != NoHolder) return claimant == sender;
            return !m_Controller.HasClaim(sender, this);
        }

        protected override void ServerOnGranted(ulong sender)
        {
            if (m_ClaimedBy.Value != NoHolder) return;
            PlayerRole role = PlayerRole.Unassigned;
            GameSessionManager session = GameSessionManager.Instance;
            if (session != null) session.TryGetRoleForClient(sender, out role);
            ServerClaim(sender, role);
        }

        // ---- server ----------------------------------------------------------------------------

        /// <summary>
        /// Server: record a claim. A real client also gets ownership, so its machine drives the
        /// follow; a <see cref="ForcedClaim"/> leaves the half with the server, where it stays put.
        /// </summary>
        public void ServerClaim(ulong client, PlayerRole role)
        {
            if (!IsServer) return;
            m_ClaimedBy.Value = client;
            m_ClaimedRole.Value = (byte)role;
            m_ClaimedAt.Value = InteractionPhaseClock.now;
            if (client != ForcedClaim && client != NoHolder && OwnerClientId != client)
                NetworkObject.ChangeOwnership(client);
            Debug.Log($"[OAFragment] '{name}' claimed by " +
                      $"{(client == ForcedClaim ? "a forced claim" : "client " + client)} as {role}.", this);
        }

        /// <summary>Server: the halves have joined. Nobody holds them; the server takes them back shortly.</summary>
        public void ServerJoin(float reclaimDelay)
        {
            if (!IsServer) return;
            ServerClearHolder();
            ServerReclaimOwnership(reclaimDelay);
        }

        /// <summary>Server: unclaimed, unheld, server-owned, at <paramref name="pose"/>.</summary>
        public void ServerResetTo(Pose pose)
        {
            if (!IsServer) return;
            ServerClearHolder();
            m_ClaimedBy.Value = NoHolder;
            m_ClaimedRole.Value = 0;
            m_ClaimedAt.Value = InteractionPhaseClock.Unset;
            ServerReclaimOwnership();
            EndLocalAnimation();
            ServerSetPose(pose.position, pose.rotation);
        }

        /// <summary>Server: put it somewhere (start of the beat). Only when nobody owns it but the server.</summary>
        public void ServerPlace(Pose pose)
        {
            if (!IsServer) return;
            ServerSetPose(pose.position, pose.rotation);
        }

        protected override void ServerOnHolderLost(ulong lostClient) => ReleaseClaimOf(lostClient);

        void HandleClientDisconnected(ulong clientId) => ReleaseClaimOf(clientId);

        void ReleaseClaimOf(ulong clientId)
        {
            if (!IsServer || m_ClaimedBy.Value != clientId) return;
            Debug.Log($"[OAFragment] '{name}': claimant {clientId} left; the half is free again.", this);
            m_ClaimedBy.Value = NoHolder;
            m_ClaimedRole.Value = 0;
            m_ClaimedAt.Value = InteractionPhaseClock.Unset;
            ServerReclaimOwnership();
        }

        // ---- every machine ---------------------------------------------------------------------

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_ClaimedBy.OnValueChanged += HandleClaimChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            m_ClaimedBy.OnValueChanged -= HandleClaimChanged;
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            base.OnNetworkDespawn();
        }

        void HandleClaimChanged(ulong previous, ulong current) => onClaimChanged?.Invoke(this);

        protected override void Awake()
        {
            base.Awake();
            m_BobPhase = ExperienceClock.PhaseFor(transform.position);
        }

        protected override bool NetworkTransformWanted() => !m_LocallyAnimated;

        /// <summary>
        /// Every machine: from now on the controller writes this half's pose. Lets go of it here,
        /// switches grabbing and the network transform off.
        /// </summary>
        public void BeginLocalAnimation()
        {
            if (m_LocallyAnimated) return;
            m_LocallyAnimated = true;
            following = false;
            DropLocally();
            if (m_Interactable != null) m_Interactable.enabled = false;
            if (m_NetworkTransform != null) m_NetworkTransform.enabled = false;
        }

        /// <summary>Every machine: hand the pose back to the network transform and grabbing.</summary>
        public void EndLocalAnimation()
        {
            m_FollowVelocity = Vector3.zero;
            if (!m_LocallyAnimated) return;
            m_LocallyAnimated = false;
            if (m_Interactable != null) m_Interactable.enabled = true;
            if (m_NetworkTransform != null && !m_NetworkTransform.enabled) m_NetworkTransform.enabled = true;
        }

        /// <summary>Write the pose directly (controller-driven animation, local reset).</summary>
        public void SetLocalPose(Vector3 position, Quaternion rotation) =>
            transform.SetPositionAndRotation(position, rotation);

        protected override void Update()
        {
            base.Update();
            following = ShouldFollow();
            if (following) Follow(Time.deltaTime);
            else m_FollowVelocity = Vector3.zero;
        }

        bool ShouldFollow()
        {
            if (!IsSpawned || m_LocallyAnimated || !IsOwner) return false;
            if (m_Controller == null || !m_Controller.followActive) return false;
            if (m_ClaimedBy.Value != localClientId) return false;
            return !heldLocally && !awaitingGrant && !isHeld;
        }

        void Follow(float dt)
        {
            if (dt <= 0f) return;
            if (m_Head == null || !m_Head.isActiveAndEnabled) m_Head = Camera.main;
            if (m_Head == null) return;

            Transform head = m_Head.transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            Quaternion yaw = Quaternion.LookRotation(forward.normalized, Vector3.up);

            float bob = Mathf.Sin((ExperienceClock.now / m_FollowBobPeriod + m_BobPhase) * 2f * Mathf.PI) * m_FollowBob;
            Vector3 target = head.position + yaw * m_FollowOffset + Vector3.up * bob;

            Vector3 position = Vector3.SmoothDamp(transform.position, target, ref m_FollowVelocity,
                                                  m_FollowSmoothTime, m_FollowMaxSpeed, dt);

            // Upright, turning slowly with the player: the halves were baked standing up, so the
            // layout rotation under the head's yaw is "the way it stands", facing with them.
            Quaternion facing = yaw * m_LayoutRotation;
            Quaternion rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-dt / m_FollowTurnTime));

            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Where the follow is heading right now (for tests and gizmos); null with no head.</summary>
        public Vector3? FollowTarget()
        {
            Camera cam = m_Head != null ? m_Head : Camera.main;
            if (cam == null) return null;
            Transform head = cam.transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            return head.position + Quaternion.LookRotation(forward.normalized, Vector3.up) * m_FollowOffset;
        }

        void OnDrawGizmosSelected()
        {
            if (m_JoinPoint == null) return;
            Gizmos.color = new Color(1f, 0.75f, 0.3f);
            Gizmos.DrawWireSphere(m_JoinPoint.position, 0.012f);
        }
    }
}
