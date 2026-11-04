using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using XRMultiplayer;

namespace CompositeBody.Interaction
{
    /// <summary>
    /// A networked object either player can pick up, with ownership the SERVER hands out.
    ///
    /// WHY NOT THE TEMPLATE'S NetworkBaseInteractable. That one asks the current OWNER to call
    /// <see cref="NetworkObject.ChangeOwnership"/>. In this project's client-server topology only
    /// the server may change ownership, so once a client has held an object the host can never
    /// take it again -- the host's grab moves the object on the host's screen only. Here every
    /// grab is a request to the server, which grants it (holder = sender, ownership = sender) or
    /// refuses it, and the replicated <see cref="holder"/> is the one fact both machines agree on.
    ///
    /// FIRST GRAB WINS. A grab is only allowed through the XRI select filter while nobody holds
    /// the object (or this machine already does). Two players closing on it in the same moment
    /// both pass their local filter; the server grants whichever request arrives first and sends
    /// the other a <see cref="DenyHoldRpc"/>, and the loser's hand lets go. XRI re-evaluates
    /// select filters on held objects every frame, so even without the deny the loser's
    /// selection ends as soon as the replicated holder names somebody else.
    ///
    /// THE TRANSFORM IS OWNER-AUTHORITATIVE (<see cref="ClientNetworkTransform"/>). Between a
    /// non-owner's local grab and the ownership arriving, incoming poses would drag the object out
    /// of the hand, so the network transform is switched off on that machine until the grant (the
    /// template's NetworkPhysicsInteractable does the same).
    ///
    /// RELEASES ARE DECIDED IN LateUpdate, not at select-exit: passing an object from one hand to
    /// the other exits and re-enters in the same frame, and reporting that exit would bounce
    /// ownership through the server for nothing.
    ///
    /// Subclasses decide the rules (<see cref="ServerCanGrant"/>, <see cref="LocalCanSelect"/>)
    /// and what a release means (<see cref="ServerOnReleased"/>, <see cref="reclaimOnRelease"/>).
    ///
    /// Required on the same object (see InteractionBuildUtil.ConfigureGrabbable):
    /// NetworkObject with DontDestroyWithOwner = true and AutoObjectParentSync = false,
    /// XRGrabInteractable with unparentTransformOnGrab = false and predictedVisualsTransform =
    /// null, a kinematic Rigidbody, and a ClientNetworkTransform.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public abstract class NetGrabbable : NetworkBehaviour, IXRSelectFilter, IXRHoverFilter
    {
        /// <summary>The value of <see cref="holder"/> when nobody holds it. Not 0: 0 is the host.</summary>
        public const ulong NoHolder = ulong.MaxValue;

        [Header("Grab")]
        [SerializeField, Tooltip("Found on this object if left empty.")]
        protected XRGrabInteractable m_Interactable;

        [SerializeField, Tooltip("Found on this object if left empty.")]
        protected ClientNetworkTransform m_NetworkTransform;

        [SerializeField, Min(0.1f), Tooltip("How long a grab waits for the server's answer before " +
                                            "letting go by itself.")]
        float m_GrantTimeout = 2f;

        [SerializeField, Tooltip("Log refused grabs. On by default: 'my hand just let go' is " +
                                 "otherwise invisible in the venue.")]
        protected bool m_LogRefusals = true;

        readonly NetworkVariable<ulong> m_Holder =
            new(NoHolder, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        bool m_LocalHolding;
        bool m_AwaitingGrant;
        float m_RequestedAt;
        bool m_ReleasePending;
        Coroutine m_Reclaim;

        /// <summary>Client id of whoever holds it, or <see cref="NoHolder"/>.</summary>
        public ulong holder => m_Holder.Value;

        /// <summary>True while somebody (anybody) holds it, as the server sees it.</summary>
        public bool isHeld => m_Holder.Value != NoHolder;

        /// <summary>True while a hand on THIS machine is closed on it.</summary>
        public bool heldLocally => m_Interactable != null && m_Interactable.isSelected;

        /// <summary>True between a local grab and the server's answer.</summary>
        public bool awaitingGrant => m_AwaitingGrant;

        public XRGrabInteractable interactable => m_Interactable;

        /// <summary>This machine's client id, or <see cref="NoHolder"/> with no session.</summary>
        protected ulong localClientId =>
            NetworkManager != null && NetworkManager.IsListening ? NetworkManager.LocalClientId : NoHolder;

        /// <summary>
        /// Whether the server takes ownership back when the holder lets go. True suits objects the
        /// server keeps deciding about (Home's fragments); false keeps ownership with the last
        /// holder, who then drives it (ObjectAssembly's follow-the-player halves).
        /// </summary>
        protected virtual bool reclaimOnRelease => true;

        // ---- rules, for subclasses -------------------------------------------------------------

        /// <summary>Server: may <paramref name="sender"/> take hold now? Holder is already known to be free.</summary>
        protected abstract bool ServerCanGrant(ulong sender);

        /// <summary>Server: <paramref name="sender"/> now holds it and owns it.</summary>
        protected virtual void ServerOnGranted(ulong sender) { }

        /// <summary>Server: the holder let go at this pose. Holder is already cleared.</summary>
        protected virtual void ServerOnReleased(ulong sender, Vector3 position, Quaternion rotation) { }

        /// <summary>Server: the holder disconnected while holding. Holder is already cleared.</summary>
        protected virtual void ServerOnHolderLost(ulong lostClient) { }

        /// <summary>Local gate in addition to the holder rule (e.g. "only while Free").</summary>
        protected virtual bool LocalCanSelect() => true;

        /// <summary>A hand on this machine closed on it (also with no session).</summary>
        protected virtual void OnLocalGrabStarted() { }

        /// <summary>This machine's hand is off it -- let go, refused, or cancelled.</summary>
        protected virtual void OnLocalGrabEnded() { }

        /// <summary>Every machine: the replicated holder changed.</summary>
        protected virtual void OnHolderChanged(ulong previous, ulong current) { }

        // ---- lifecycle -------------------------------------------------------------------------

        protected virtual void Reset()
        {
            m_Interactable = GetComponent<XRGrabInteractable>();
            m_NetworkTransform = GetComponent<ClientNetworkTransform>();
        }

        protected virtual void Awake()
        {
            if (m_Interactable == null) m_Interactable = GetComponent<XRGrabInteractable>();
            if (m_NetworkTransform == null) m_NetworkTransform = GetComponent<ClientNetworkTransform>();

            if (m_Interactable == null)
            {
                Debug.LogWarning($"[NetGrabbable] '{name}' has no XRGrabInteractable; it cannot be grabbed.", this);
                return;
            }

            m_Interactable.selectFilters.Add(this);
            m_Interactable.hoverFilters.Add(this);
        }

        protected virtual void OnEnable()
        {
            if (m_Interactable == null) return;
            m_Interactable.selectEntered.AddListener(HandleSelectEntered);
            m_Interactable.selectExited.AddListener(HandleSelectExited);
        }

        protected virtual void OnDisable()
        {
            if (m_Interactable == null) return;
            m_Interactable.selectEntered.RemoveListener(HandleSelectEntered);
            m_Interactable.selectExited.RemoveListener(HandleSelectExited);
        }

        public override void OnDestroy()
        {
            if (m_Interactable != null)
            {
                m_Interactable.selectFilters.Remove(this);
                m_Interactable.hoverFilters.Remove(this);
            }
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Belt and braces for the builder's setting: without it a client that disconnects
            // while holding takes the object with it -- NGO destroys everything a leaving client
            // owns.
            NetworkObject.DontDestroyWithOwner = true;

            m_Holder.OnValueChanged += HandleHolderChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            m_Holder.OnValueChanged -= HandleHolderChanged;
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            base.OnNetworkDespawn();
        }

        // ---- filters ---------------------------------------------------------------------------

        // ALWAYS TRUE. XRI skips a filter whose canProcess is false, so tying it to
        // isActiveAndEnabled (as the template does) would make a disabled component allow
        // every grab.
        public bool canProcess => true;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) => Allowed();

        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable) => Allowed();

        bool Allowed()
        {
            if (!LocalCanSelect()) return false;
            if (!IsSpawned) return true;

            ulong me = localClientId;
            ulong h = m_Holder.Value;
            return h == NoHolder || h == me;
        }

        // ---- local grab ------------------------------------------------------------------------

        void HandleSelectEntered(SelectEnterEventArgs args)
        {
            // A socket is not a player.
            if (args.interactorObject is XRSocketInteractor) return;

            m_ReleasePending = false;
            if (m_LocalHolding) return; // the other hand

            m_LocalHolding = true;
            OnLocalGrabStarted();

            if (!IsSpawned) return;
            if (m_Holder.Value == localClientId && IsOwner) return;

            if (!IsOwner && m_NetworkTransform != null) m_NetworkTransform.enabled = false;
            m_AwaitingGrant = true;
            m_RequestedAt = Time.unscaledTime;
            RequestHoldRpc();
        }

        void HandleSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;
            m_ReleasePending = true;
        }

        protected virtual void Update()
        {
            if (m_AwaitingGrant && Time.unscaledTime - m_RequestedAt > m_GrantTimeout)
            {
                if (m_LogRefusals)
                    Debug.LogWarning($"[NetGrabbable] '{name}': no answer from the server in " +
                                     $"{m_GrantTimeout:0.#}s, letting go.", this);
                DropLocally();
            }
        }

        protected virtual void LateUpdate()
        {
            if (!m_ReleasePending) return;
            if (m_Interactable != null && m_Interactable.isSelected) return; // switched hands
            m_ReleasePending = false;

            if (!m_LocalHolding) return;
            m_LocalHolding = false;
            m_AwaitingGrant = false;
            if (m_NetworkTransform != null && !m_NetworkTransform.enabled) m_NetworkTransform.enabled = true;
            OnLocalGrabEnded();

            if (!IsSpawned) return;
            transform.GetPositionAndRotation(out Vector3 p, out Quaternion r);
            ReleaseHoldRpc(p, r);
        }

        /// <summary>
        /// Take this machine's hand off the object without telling the server anything -- for a
        /// refused grab, a lost race, or the object being taken over (placed, vanished, reset).
        /// </summary>
        protected void DropLocally()
        {
            bool wasHolding = m_LocalHolding;
            m_LocalHolding = false;
            m_AwaitingGrant = false;
            m_ReleasePending = false;

            if (m_Interactable != null && m_Interactable.isSelected && m_Interactable.interactionManager != null)
                m_Interactable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)m_Interactable);

            if (m_NetworkTransform != null && !m_NetworkTransform.enabled && NetworkTransformWanted())
                m_NetworkTransform.enabled = true;

            if (wasHolding) OnLocalGrabEnded();
        }

        /// <summary>
        /// Whether the network transform should be running on this machine when nothing grab-related
        /// is in the way. Subclasses that animate locally (a placed fragment gliding home on every
        /// machine) return false while they do.
        /// </summary>
        protected virtual bool NetworkTransformWanted() => true;

        // ---- server ----------------------------------------------------------------------------

        [Rpc(SendTo.Server)]
        void RequestHoldRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (m_Holder.Value == sender)
            {
                if (OwnerClientId != sender) NetworkObject.ChangeOwnership(sender);
                return;
            }

            if (m_Holder.Value == NoHolder && ServerCanGrant(sender))
            {
                CancelReclaim();
                m_Holder.Value = sender;
                if (OwnerClientId != sender) NetworkObject.ChangeOwnership(sender);
                ServerOnGranted(sender);
                return;
            }

            if (m_LogRefusals)
                Debug.Log($"[NetGrabbable] '{name}': refused client {sender} " +
                          $"(holder {(m_Holder.Value == NoHolder ? "none" : m_Holder.Value.ToString())}).", this);
            DenyHoldRpc(RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void DenyHoldRpc(RpcParams rpcParams)
        {
            if (m_LogRefusals) Debug.Log($"[NetGrabbable] '{name}': select refused by the server.", this);
            DropLocally();
        }

        [Rpc(SendTo.Server)]
        void ReleaseHoldRpc(Vector3 position, Quaternion rotation, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (m_Holder.Value != sender) return; // a refused or stale grab letting go

            m_Holder.Value = NoHolder;

            if (reclaimOnRelease)
            {
                if (OwnerClientId != NetworkManager.ServerClientId)
                    NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
                ServerSetPose(position, rotation);
            }

            ServerOnReleased(sender, position, rotation);
        }

        void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer || m_Holder.Value != clientId) return;
            m_Holder.Value = NoHolder;
            ServerOnHolderLost(clientId);
        }

        /// <summary>Server: clear the holder without a release (placing, vanishing, resetting).</summary>
        protected void ServerClearHolder()
        {
            if (!IsServer) return;
            m_Holder.Value = NoHolder;
        }

        /// <summary>
        /// Server: take ownership back, optionally a little later. LATER on purpose when a state
        /// change goes out in the same breath: the state must reach the holder before the
        /// ownership does, or the holder's last pose update and the change race.
        /// </summary>
        protected void ServerReclaimOwnership(float delaySeconds = 0f)
        {
            if (!IsServer) return;
            CancelReclaim();
            if (delaySeconds <= 0f) { ReclaimNow(); return; }
            m_Reclaim = StartCoroutine(ReclaimLater(delaySeconds));
        }

        IEnumerator ReclaimLater(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            m_Reclaim = null;
            ReclaimNow();
        }

        void ReclaimNow()
        {
            if (!IsSpawned) return;
            if (OwnerClientId != NetworkManager.ServerClientId)
                NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }

        void CancelReclaim()
        {
            if (m_Reclaim == null) return;
            StopCoroutine(m_Reclaim);
            m_Reclaim = null;
        }

        /// <summary>
        /// Server (or the owner): put the object at a pose. A teleport when this machine is the
        /// transform's authority, so the other machine jumps rather than sliding across the room.
        /// </summary>
        protected void ServerSetPose(Vector3 position, Quaternion rotation)
        {
            if (m_NetworkTransform != null && m_NetworkTransform.CanCommitToTransform && IsSpawned)
                m_NetworkTransform.Teleport(position, rotation, transform.localScale);
            else
                transform.SetPositionAndRotation(position, rotation);
        }

        // ---- every machine ---------------------------------------------------------------------

        void HandleHolderChanged(ulong previous, ulong current)
        {
            // Lost the race (or the object was taken over): let go here too.
            if (m_LocalHolding && current != localClientId && (current != NoHolder || !m_AwaitingGrant))
                DropLocally();

            // Granted to a machine that already owned it (the host, or a claimant picking its own
            // object back up): no ownership change will arrive to end the wait, so end it here.
            if (current == localClientId && m_AwaitingGrant && IsOwner)
            {
                m_AwaitingGrant = false;
                if (m_NetworkTransform != null && NetworkTransformWanted()) m_NetworkTransform.enabled = true;
            }

            OnHolderChanged(previous, current);
        }

        protected override void OnOwnershipChanged(ulong previous, ulong current)
        {
            base.OnOwnershipChanged(previous, current);

            if (current == localClientId && m_AwaitingGrant)
            {
                m_AwaitingGrant = false;
                if (m_NetworkTransform != null && NetworkTransformWanted()) m_NetworkTransform.enabled = true;
            }
        }
    }
}
