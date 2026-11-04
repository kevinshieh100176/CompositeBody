using Unity.Netcode;
using UnityEngine;

namespace CompositeBody.Interaction.Home
{
    /// <summary>Where a Home fragment is in its life. Server-written, replicated.</summary>
    public enum HomeFragmentState : byte
    {
        /// <summary>Beat not started: invisible, ungrabbable.</summary>
        Dormant = 0,

        /// <summary>Floating in zone A, breathing, anyone may take it.</summary>
        Free = 1,

        /// <summary>In somebody's hand (see <see cref="NetGrabbable.holder"/>).</summary>
        Held = 2,

        /// <summary>Home in the mini-house. Final for this epoch.</summary>
        Placed = 3,

        /// <summary>Dissolved away by the timeout. Final for this epoch.</summary>
        Vanished = 4,
    }

    /// <summary>
    /// One of the five paper fragments of Home (Floor, Back, Front, Left, Right). Grabbing is the
    /// shared server-granted protocol (<see cref="NetGrabbable"/>: first grab wins); this adds the
    /// fragment's life on top of it.
    ///
    /// THE SERVER DECIDES, EVERY MACHINE DRAWS. The server writes the state and three time stamps
    /// (armed, entered the zone, resolved) and the rest pose; every machine -- the server too --
    /// turns those into what it sees, as a pure function of the shared clock. So the glide into the
    /// slot, the tremble, the appear and the dissolve run on both headsets without a message of
    /// their own, and a headset that joins late lands on the same frame instead of replaying.
    ///
    /// PLACING. Held inside zone B the fragment starts to tremble (harder as the dwell runs) and
    /// after <see cref="HomeAssembly.dwell"/> seconds the server places it; letting go inside B
    /// places it at once. Placed means: every machine cancels its own selection, switches the grab,
    /// the collider and the network transform off, and glides the fragment LOCALLY from wherever
    /// it is on that machine to the slot (ease-in, so it leaves the hand gently and lands with
    /// intent), then settles with a small wobble. Ownership goes back to the server a moment later
    /// (<see cref="HomeAssembly.reclaimDelay"/>), so the state reaches the holder before the
    /// ownership change does and the holder's last pose update cannot race the glide.
    ///
    /// LET GO OUTSIDE B and it stays where it was released (the server takes ownership back and
    /// teleports it to the release pose, which becomes its rest pose) and breathes again.
    ///
    /// NO SESSION (a desk, one machine, nothing listening): this machine is the authority and the
    /// same rules run on local mirrors of the replicated values.
    /// </summary>
    public class HomeFragment : NetGrabbable
    {
        [Header("Home")]
        [SerializeField, Tooltip("The module this fragment belongs to (timings, zone B).")]
        HomeAssembly m_Assembly;

        [SerializeField, Tooltip("Where this fragment belongs in the mini-house (world pose).")]
        Transform m_Slot;

        [SerializeField, Tooltip("The view on the Visual child.")]
        HomeFragmentView m_View;

        [SerializeField, Tooltip("Switched off whenever the fragment cannot be grabbed.")]
        Collider m_Collider;

        [SerializeField, Tooltip("Short ASCII name for logs and the debug row (Floor, Back, ...).")]
        string m_Label = "Fragment";

        [SerializeField, Tooltip("The mesh's bounds centre in this object's local space -- what zone " +
                                 "tests and the scatter use, since the pivot can sit off the paper.")]
        Vector3 m_LocalCenter;

        readonly NetworkVariable<byte> m_State =
            new((byte)HomeFragmentState.Dormant, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<double> m_ArmedAt =
            new(InteractionPhaseClock.Unset, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<double> m_ZoneEnteredAt =
            new(InteractionPhaseClock.Unset, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<double> m_ResolvedAt =
            new(InteractionPhaseClock.Unset, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<int> m_Epoch =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<Vector3> m_RestPosition =
            new(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<Quaternion> m_RestRotation =
            new(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // The same values with no session, so a desk run follows the same rules.
        HomeFragmentState m_OfflineState = HomeFragmentState.Dormant;
        double m_OfflineArmedAt = InteractionPhaseClock.Unset;
        double m_OfflineZoneEnteredAt = InteractionPhaseClock.Unset;
        double m_OfflineResolvedAt = InteractionPhaseClock.Unset;
        int m_OfflineEpoch;

        // What this machine last presented, so transitions run once.
        const byte k_NothingApplied = 255;
        byte m_Applied = k_NothingApplied;
        int m_AppliedEpoch = int.MinValue;
        bool m_AppliedOffline;

        Vector3 m_GlideFromPosition;
        Quaternion m_GlideFromRotation = Quaternion.identity;
        bool m_Landed;

        float m_LocalZoneEnter = -1f;
        double m_FinalDissolveAt = InteractionPhaseClock.Unset;

        // ---- reading ---------------------------------------------------------------------------

        /// <summary>True when nothing is listening: this machine is the only one there is.</summary>
        public static bool offline
        {
            get
            {
                NetworkManager nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening;
            }
        }

        /// <summary>This machine decides (server, or offline).</summary>
        public bool authority => offline || (IsSpawned && IsServer);

        public HomeFragmentState state => IsSpawned ? (HomeFragmentState)m_State.Value : m_OfflineState;
        public double armedAt => IsSpawned ? m_ArmedAt.Value : m_OfflineArmedAt;
        public double zoneEnteredAt => IsSpawned ? m_ZoneEnteredAt.Value : m_OfflineZoneEnteredAt;
        public double resolvedAt => IsSpawned ? m_ResolvedAt.Value : m_OfflineResolvedAt;
        public int epoch => IsSpawned ? m_Epoch.Value : m_OfflineEpoch;

        public bool isResolved => state is HomeFragmentState.Placed or HomeFragmentState.Vanished;
        public bool isArmed => state != HomeFragmentState.Dormant;

        public string label => m_Label;
        public Transform slot => m_Slot;
        public HomeFragmentView view => m_View;
        public HomeAssembly assembly => m_Assembly;
        public Collider grabCollider => m_Collider;

        /// <summary>The paper's centre in world space (the pivot can be off it).</summary>
        public Vector3 worldCenter => transform.TransformPoint(m_LocalCenter);

        /// <summary>The paper's centre offset from the pivot, in this object's local space.</summary>
        public Vector3 localCenter => m_LocalCenter;

        /// <summary>This machine finished the glide into the slot.</summary>
        public bool landed => m_Landed;

        /// <summary>
        /// When this machine dissolves the placed fragment away (set by the assembly once every
        /// fragment is resolved). Unset until then.
        /// </summary>
        public double finalDissolveAt { get => m_FinalDissolveAt; set => m_FinalDissolveAt = value; }

        /// <summary>
        /// 0..1 dwell progress as THIS machine sees it while the fragment is held inside zone B,
        /// negative otherwise. The holder counts locally (no round trip before the hand feels it);
        /// everybody else reads the server's stamp.
        /// </summary>
        public float dwell01 { get; private set; } = -1f;

        /// <summary>When this machine's final dissolve (or vanish) is over, on the shared clock.</summary>
        public double goneAt
        {
            get
            {
                if (state == HomeFragmentState.Vanished) return resolvedAt + Mathf.Max(A.vanishDuration, 0f);
                if (state == HomeFragmentState.Placed && InteractionPhaseClock.IsSet(m_FinalDissolveAt))
                    return m_FinalDissolveAt + Mathf.Max(A.fragmentDissolve, 0f);
                return double.PositiveInfinity;
            }
        }

        // Timings, with defaults if the assembly is missing (a fragment dropped in by hand).
        HomeAssembly.Timings A => m_Assembly != null ? m_Assembly.timings : HomeAssembly.Timings.Default;

        // ---- grab rules -----------------------------------------------------------------------

        protected override bool ServerCanGrant(ulong sender) => state == HomeFragmentState.Free;

        protected override bool LocalCanSelect()
        {
            HomeFragmentState s = state;
            if (s == HomeFragmentState.Free) return true;
            if (s != HomeFragmentState.Held) return false;
            return !IsSpawned || holder == localClientId;
        }

        protected override void ServerOnGranted(ulong sender)
        {
            SetZoneEnteredAt(InteractionPhaseClock.Unset);
            SetState(HomeFragmentState.Held);
        }

        protected override void ServerOnReleased(ulong sender, Vector3 position, Quaternion rotation) =>
            AuthorityReleased(position, rotation);

        protected override void ServerOnHolderLost(ulong lostClient)
        {
            if (state != HomeFragmentState.Held) return;
            transform.GetPositionAndRotation(out Vector3 p, out Quaternion r);
            SetRest(p, r);
            SetZoneEnteredAt(InteractionPhaseClock.Unset);
            SetState(HomeFragmentState.Free);
            Debug.Log($"[Home] '{m_Label}': holder {lostClient} left; floating where it was.", this);
        }

        protected override void OnLocalGrabStarted()
        {
            m_LocalZoneEnter = -1f;
            if (!offline) return;
            if (state != HomeFragmentState.Free) return;
            SetZoneEnteredAt(InteractionPhaseClock.Unset);
            SetState(HomeFragmentState.Held);
        }

        protected override void OnLocalGrabEnded()
        {
            m_LocalZoneEnter = -1f;
            if (!offline || state != HomeFragmentState.Held) return;
            transform.GetPositionAndRotation(out Vector3 p, out Quaternion r);
            AuthorityReleased(p, r);
        }

        protected override bool NetworkTransformWanted() => !isResolved;

        void AuthorityReleased(Vector3 position, Quaternion rotation)
        {
            if (state != HomeFragmentState.Held) return;

            HomeZone zone = m_Assembly != null ? m_Assembly.zoneB : null;
            if (zone != null && zone.Contains(worldCenter))
            {
                Debug.Log($"[Home] '{m_Label}' let go inside the assembly zone: placing.", this);
                AuthorityPlace();
                return;
            }

            SetRest(position, rotation);
            SetZoneEnteredAt(InteractionPhaseClock.Unset);
            SetState(HomeFragmentState.Free);
        }

        // ---- authority actions ----------------------------------------------------------------

        /// <summary>
        /// Server (or offline): bring the fragment into the room at a pose, for epoch
        /// <paramref name="newEpoch"/>. Clears any hand, takes ownership back, and teleports.
        /// </summary>
        public void AuthorityArm(Vector3 position, Quaternion rotation, double stamp, int newEpoch)
        {
            if (!authority) return;

            if (IsSpawned)
            {
                ServerClearHolder();
                ServerReclaimOwnership(0f);
            }
            DropLocally();

            SetEpoch(newEpoch);
            SetArmedAt(stamp);
            SetResolvedAt(InteractionPhaseClock.Unset);
            SetZoneEnteredAt(InteractionPhaseClock.Unset);
            SetRest(position, rotation);
            SetState(HomeFragmentState.Free);
            m_FinalDissolveAt = InteractionPhaseClock.Unset;

            // Network transform back on BEFORE the teleport, or the teleport is never sent.
            if (m_NetworkTransform != null) m_NetworkTransform.enabled = true;
            ServerSetPose(position, rotation);
            Present(true);
        }

        /// <summary>Server (or offline): home it. No-op unless Free or Held.</summary>
        public void AuthorityPlace() => Resolve(HomeFragmentState.Placed);

        /// <summary>Server (or offline): the timeout took it. No-op unless Free or Held.</summary>
        public void AuthorityVanish() => Resolve(HomeFragmentState.Vanished);

        void Resolve(HomeFragmentState to)
        {
            if (!authority) return;
            HomeFragmentState s = state;
            if (s != HomeFragmentState.Free && s != HomeFragmentState.Held) return;

            SetZoneEnteredAt(InteractionPhaseClock.Unset);
            SetResolvedAt(InteractionPhaseClock.now);
            SetState(to);

            if (IsSpawned)
            {
                ServerClearHolder();
                ServerReclaimOwnership(m_Assembly != null ? m_Assembly.reclaimDelay : 0.1f);
            }

            // Present now rather than next frame: the network transform has to stop listening to
            // the holder before the holder's next pose arrives.
            Present(true);
        }

        /// <summary>Place it from anywhere: server/offline does it, a client asks the server.</summary>
        public void RequestPlace()
        {
            if (authority) AuthorityPlace();
            else if (IsSpawned) ForcePlaceRpc();
        }

        [Rpc(SendTo.Server)]
        void ForcePlaceRpc() => AuthorityPlace();

        // Module-level requests ride on a fragment, because the assembly itself is not networked.
        [Rpc(SendTo.Server)]
        public void RequestArmRpc() { if (m_Assembly != null) m_Assembly.AuthorityStart(); }

        [Rpc(SendTo.Server)]
        public void RequestExpireRpc() { if (m_Assembly != null) m_Assembly.AuthorityExpire(); }

        // ---- lifecycle ------------------------------------------------------------------------

        protected override void Reset()
        {
            base.Reset();
            m_View = GetComponentInChildren<HomeFragmentView>(true);
            m_Collider = GetComponent<Collider>();
        }

        protected override void Awake()
        {
            base.Awake();
            if (m_View == null) m_View = GetComponentInChildren<HomeFragmentView>(true);
            if (m_Collider == null) m_Collider = GetComponent<Collider>();
            if (m_Slot == null)
                Debug.LogWarning($"[Home] '{name}' has no slot; it can never be placed. Re-run " +
                                 "Tools > Home > Build Home Interaction.", this);
        }

        void Start() => Present(true);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Present(true); // a late joiner fast-forwards from the spawn payload
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            m_Applied = k_NothingApplied;
        }

        protected override void Update()
        {
            base.Update();
            if (authority) AuthorityDwell();
            Present(false);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            // Again after everything else has written the transform this frame (the network
            // transform, the grab): a resolved fragment's pose is this machine's own.
            if (isResolved) Glide();
        }

        // ---- server dwell ---------------------------------------------------------------------

        void AuthorityDwell()
        {
            if (state != HomeFragmentState.Held) return;
            HomeZone zone = m_Assembly != null ? m_Assembly.zoneB : null;
            if (zone == null) return;

            double entered = zoneEnteredAt;
            if (zone.Contains(worldCenter))
            {
                if (!InteractionPhaseClock.IsSet(entered)) SetZoneEnteredAt(InteractionPhaseClock.now);
                else if (InteractionPhaseClock.Since(entered) >= A.dwell)
                {
                    Debug.Log($"[Home] '{m_Label}' held in the assembly zone for {A.dwell:0.0}s: placing.", this);
                    AuthorityPlace();
                }
            }
            else if (InteractionPhaseClock.IsSet(entered))
            {
                SetZoneEnteredAt(InteractionPhaseClock.Unset);
            }
        }

        // ---- presentation (every machine) -----------------------------------------------------

        /// <summary>Re-apply this machine's presentation from the replicated facts.</summary>
        public void Present(bool force)
        {
            HomeFragmentState s = state;
            int e = epoch;
            bool off = !IsSpawned;
            bool changed = force || (byte)s != m_Applied || e != m_AppliedEpoch || off != m_AppliedOffline;

            if (changed)
            {
                HomeFragmentState? previous = m_Applied == k_NothingApplied || e != m_AppliedEpoch
                    ? null
                    : (HomeFragmentState)m_Applied;
                Enter(previous, s);
                m_Applied = (byte)s;
                m_AppliedEpoch = e;
                m_AppliedOffline = off;
            }

            Drive(s);
        }

        void Enter(HomeFragmentState? previous, HomeFragmentState s)
        {
            switch (s)
            {
                case HomeFragmentState.Dormant:
                    DropLocally();
                    SetGrabbable(false);
                    SetNetworkTransform(true);
                    if (m_View != null)
                    {
                        m_View.breathing = 0f;
                        m_View.SnapBreathing();
                        m_View.CancelWobble();
                    }
                    m_Landed = false;
                    break;

                case HomeFragmentState.Free:
                    SetGrabbable(true);
                    SetNetworkTransform(true);
                    // Coming into the room (not back from a hand): stand where the server put it.
                    // The authority already teleported; a client snaps to the replicated rest pose
                    // so it does not draw one frame at the old pose before the transform arrives.
                    if (previous != HomeFragmentState.Held && previous != HomeFragmentState.Free &&
                        IsSpawned && !IsServer)
                        transform.SetPositionAndRotation(m_RestPosition.Value, m_RestRotation.Value);
                    if (m_View != null)
                    {
                        m_View.breathing = 1f;
                        if (previous != HomeFragmentState.Held) m_View.SnapBreathing();
                        m_View.CancelWobble();
                    }
                    m_Landed = false;
                    break;

                case HomeFragmentState.Held:
                    SetGrabbable(true);
                    if (m_View != null) m_View.breathing = 0f;
                    m_Landed = false;
                    break;

                case HomeFragmentState.Placed:
                case HomeFragmentState.Vanished:
                    DropLocally();
                    SetGrabbable(false);
                    SetNetworkTransform(false);
                    transform.GetPositionAndRotation(out m_GlideFromPosition, out m_GlideFromRotation);
                    m_Landed = false;
                    if (m_View != null) m_View.breathing = 0f;
                    break;
            }
        }

        void Drive(HomeFragmentState s)
        {
            float dwell = -1f;
            float reveal;

            switch (s)
            {
                case HomeFragmentState.Free:
                    reveal = Appear();
                    m_LocalZoneEnter = -1f;
                    break;

                case HomeFragmentState.Held:
                    reveal = Appear();
                    dwell = DwellProgress();
                    break;

                case HomeFragmentState.Placed:
                    Glide();
                    reveal = InteractionPhaseClock.IsSet(m_FinalDissolveAt)
                        ? 1f - Ramp(InteractionPhaseClock.Since(m_FinalDissolveAt), A.fragmentDissolve)
                        : 1f;
                    break;

                case HomeFragmentState.Vanished:
                    Glide();
                    reveal = 1f - Ramp(InteractionPhaseClock.Since(resolvedAt), A.vanishDuration);
                    break;

                default:
                    reveal = 0f;
                    break;
            }

            dwell01 = dwell;
            if (m_View == null) return;
            m_View.dwell01 = dwell;
            m_View.reveal01 = reveal;
        }

        float Appear() => Ramp(InteractionPhaseClock.Since(armedAt), A.appearDuration);

        static float Ramp(float since, float duration)
        {
            if (since < 0f) return 0f;
            if (duration <= 0f) return 1f;
            return Mathf.Clamp01(since / duration);
        }

        float DwellProgress()
        {
            float d = Mathf.Max(A.dwell, 0.01f);
            HomeZone zone = m_Assembly != null ? m_Assembly.zoneB : null;

            if (heldLocally && zone != null)
            {
                if (!zone.Contains(worldCenter)) { m_LocalZoneEnter = -1f; return -1f; }
                if (m_LocalZoneEnter < 0f) m_LocalZoneEnter = Time.time;
                return Mathf.Clamp01((Time.time - m_LocalZoneEnter) / d);
            }

            double entered = zoneEnteredAt;
            return InteractionPhaseClock.IsSet(entered)
                ? Mathf.Clamp01(InteractionPhaseClock.Since(entered) / d)
                : -1f;
        }

        /// <summary>
        /// Placed: from wherever this machine had it to the slot, ease-in, then the settle.
        /// Vanished: stays where it is (it only dissolves). A pure function of time since the
        /// server's stamp, so a machine that hears late starts late but arrives on time.
        /// </summary>
        void Glide()
        {
            if (state != HomeFragmentState.Placed || m_Slot == null)
            {
                if (state == HomeFragmentState.Vanished)
                    transform.SetPositionAndRotation(m_GlideFromPosition, m_GlideFromRotation);
                return;
            }

            float travel = Mathf.Max(A.placeTravel, 0.01f);
            float t = InteractionPhaseClock.Since(resolvedAt) / travel;
            m_Slot.GetPositionAndRotation(out Vector3 slotPos, out Quaternion slotRot);

            if (t >= 1f)
            {
                transform.SetPositionAndRotation(slotPos, slotRot);
                if (!m_Landed)
                {
                    m_Landed = true;
                    if (m_View != null) m_View.Wobble((float)(resolvedAt + travel));
                }
                return;
            }

            float e = InteractionEasing.EaseIn(Mathf.Max(t, 0f), m_Assembly != null ? m_Assembly.placeCurve : null);
            transform.SetPositionAndRotation(
                Vector3.LerpUnclamped(m_GlideFromPosition, slotPos, e),
                Quaternion.SlerpUnclamped(m_GlideFromRotation, slotRot, e));
        }

        void SetGrabbable(bool on)
        {
            if (m_Interactable != null && m_Interactable.enabled != on) m_Interactable.enabled = on;
            if (m_Collider != null && m_Collider.enabled != on) m_Collider.enabled = on;
        }

        void SetNetworkTransform(bool on)
        {
            if (m_NetworkTransform == null) return;
            // Never switch it on under a pending grab: NetGrabbable keeps it off until the grant.
            if (on && awaitingGrant) return;
            if (m_NetworkTransform.enabled != on) m_NetworkTransform.enabled = on;
        }

        // ---- writes (server variable, or the offline mirror) ----------------------------------

        void SetState(HomeFragmentState s)
        {
            if (IsSpawned) { if (IsServer) m_State.Value = (byte)s; }
            else m_OfflineState = s;
        }

        void SetArmedAt(double v)
        {
            if (IsSpawned) { if (IsServer) m_ArmedAt.Value = v; }
            else m_OfflineArmedAt = v;
        }

        void SetZoneEnteredAt(double v)
        {
            if (IsSpawned) { if (IsServer) m_ZoneEnteredAt.Value = v; }
            else m_OfflineZoneEnteredAt = v;
        }

        void SetResolvedAt(double v)
        {
            if (IsSpawned) { if (IsServer) m_ResolvedAt.Value = v; }
            else m_OfflineResolvedAt = v;
        }

        void SetEpoch(int v)
        {
            if (IsSpawned) { if (IsServer) m_Epoch.Value = v; }
            else m_OfflineEpoch = v;
        }

        void SetRest(Vector3 p, Quaternion r)
        {
            if (!IsSpawned || !IsServer) return;
            m_RestPosition.Value = p;
            m_RestRotation.Value = r;
        }
    }
}
