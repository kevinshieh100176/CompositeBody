using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Serialization;
using UnityEngine.Timeline;
using CompositeBody.Diagnostics;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Interaction.ObjectAssembly
{
    /// <summary>Where the ObjectAssembly beat has got to. Server-written, replicated.</summary>
    public enum OAPhase : byte
    {
        /// <summary>Not started. Halves hidden and ungrabbable, lights dark.</summary>
        Dormant = 0,
        /// <summary>OA_Phase0.playable is running: the Phase 0 line, the lights coming up.</summary>
        Phase0 = 1,
        /// <summary>The halves are dissolving in at their corners.</summary>
        Appear = 2,
        /// <summary>Waiting for each player to claim a half and hear their Phase 1 line.</summary>
        Claiming = 3,
        /// <summary>Both lines heard: the halves tremble, the trail links them, closer = stronger.</summary>
        Linking = 4,
        /// <summary>The halves met: they glide together, then float to the destination under Phase 2.</summary>
        Joined = 5,
        /// <summary>Phase 2 has ended; onCompleted has fired.</summary>
        Done = 6,
    }

    /// <summary>
    /// The ObjectAssembly beat: two halves of a lamp, one claimed by each player, linked by a
    /// glowing trail that grows stronger as they bring them together, joined, and carried off.
    ///
    ///   Dormant → Phase0 (timeline: Phase 0 VO + lights up) → Appear (halves dissolve in at
    ///   Corner_P1 / Corner_P2, breathing) → Claiming (first grab claims a half; the claimant hears
    ///   their own Phase 1 line and reports "oa.phase1" when it ends) → Linking (both reported:
    ///   tremble + trail) → Joined (join points within <see cref="m_JoinDistance"/>: glide into the
    ///   assembled layout, settle, Phase 2 VO while the object floats to Destination) → Done
    ///   (<see cref="onCompleted"/>, once, on every machine). The assembled object is then
    ///   left to be seen for <see cref="m_DissolveDelay"/> and dissolves away over
    ///   <see cref="m_DissolveDuration"/>, so a finished beat leaves nothing hanging in the
    ///   room and the show does not have to switch the prop off behind a blackout.
    ///
    /// EVERYTHING IS A FUNCTION OF SERVER STAMPS. The server decides the transitions and writes a
    /// double server-time stamp for each; every machine draws the beat from "seconds since the
    /// stamp" on the shared clock. So both headsets see the same frame, a late joiner lands where
    /// the beat has got to, and nothing but the stamps and the join anchor is replicated.
    ///
    /// THE TIMELINE PLAYS AND IS PULLED ONTO THE CLOCK, like ShowTimelineDriver: a director that
    /// is only evaluated never sounds its AudioTrack. It plays at its own rate and is jumped back
    /// onto "now − Phase0 stamp" when the two disagree by more than <see cref="m_ResyncThreshold"/>.
    /// When the halves appear is an <see cref="OAAppearMarker"/> on that asset, read from the asset
    /// and compared with the clock, never delivered as a signal.
    ///
    /// LINES, AND ALL THREE PHASES ARE ASYMMETRIC. Phase 0 is TWO timeline tracks,
    /// "Phase0 VO A" and "Phase0 VO B", bound to two AudioSources that each stand behind a
    /// <see cref="CompositeBody.Multiplayer.RoleAudible"/> gate: the two players are told different
    /// things from the beat's first sentence. Timeline knows nothing about players, but every
    /// machine runs its own copy of this prefab with its own director, so two tracks on two sources
    /// are already two private decks on each headset and which one is heard is a local mute --
    /// nothing replicated, and one asset identical on both machines. An unresolved local role mutes
    /// BOTH, which is why that gate mutes rather than stops: the role often resolves after the beat
    /// has started, and the right deck has to be able to come back mid-clip.
    /// Phase 1 is per role too, but played ONLY on the claimant's machine when the claim arrives;
    /// its end is judged by the clock, not AudioSource.isPlaying, so a missing clip or a muted
    /// device still moves the gate on. Phase 2 is per role as well -- a closing line each -- but
    /// unlike Phase 1 it plays on BOTH machines, each picking its own from the local role and
    /// starting it from the shared stamp through one ungated source. Nothing about the choice is
    /// replicated: it is the same local pick as Phase 1's, so one asset serves both headsets.
    /// Its LENGTH, though, is the longer of the two lines on every machine, never the local one.
    /// The float, the Done flip, onCompleted and the dissolve are all drawn from it, so a length
    /// that differed per machine would have the two headsets disagree about where the beat has
    /// got to -- the same reason the Phase 0 appear marker belongs at the end of the longer deck.
    ///
    /// THE HALVES STILL APPEAR AT ONE TIME for both players -- the OAAppearMarker is on the shared
    /// clock and has to be, or the two headsets would disagree about the beat. The two phase-0
    /// lines are different recordings and so different lengths, so the marker belongs at the end of
    /// the LONGER one; the builder's verify says so when a deck outruns it.
    ///
    /// Lives on an always-active object with a NetworkObject: in-scene NetworkObjects are spawned
    /// once from what is active at synchronization, so a beat root switched on later would never
    /// spawn it. <see cref="m_BeatRoot"/> is how a show arms it instead.
    /// </summary>
    [DisallowMultipleComponent]
    public class OAController : NetworkBehaviour, IDebugInteraction
    {
        public const string DefaultPhase1TaskId = "oa.phase1";

        [Header("Parts")]
        [SerializeField] OAFragment m_FragmentA;
        [SerializeField] OAFragment m_FragmentB;
        [SerializeField] OATrail m_Trail;
        [SerializeField, Tooltip("Where half A appears (nearest Player 1).")] Transform m_CornerP1;
        [SerializeField, Tooltip("Where half B appears (nearest Player 2).")] Transform m_CornerP2;
        [SerializeField, Tooltip("Where the assembled object floats to. Its pose is the final pose " +
                                 "of the assembly frame (origin at the join point).")]
        Transform m_Destination;

        [Header("Start")]
        [SerializeField, Tooltip("Start when the session goes InProgress (the staff panel's Start). " +
                                 "Ignored while a beat root is set.")]
        bool m_StartOnSessionInProgress = true;

        [SerializeField, Tooltip("When set, the beat starts when this object becomes active instead " +
                                 "of on session start -- how a show arms it.")]
        GameObject m_BeatRoot;

        [Header("Phase 0")]
        [SerializeField, Tooltip("Plays OA_<Variant>_Phase0.playable: the two role-gated Phase 0 VO " +
                                 "decks (A is heard by Player 1, B by Player 2) and the lights.")]
        PlayableDirector m_Director;

        [SerializeField, Min(0f), Tooltip("When the halves appear if the timeline has no OAAppearMarker.")]
        float m_AppearFallback = 3f;

        [SerializeField, Min(0.01f), Tooltip("Jump the director back onto the shared clock past this many seconds of drift.")]
        float m_ResyncThreshold = 0.1f;

        [SerializeField, Min(0.05f), Tooltip("Seconds for the halves to dissolve in.")]
        float m_RevealDuration = 1.2f;

        [Header("Phase 1 (claimant only)")]
        [SerializeField] AudioClip m_Phase1ClipP1;
        [SerializeField] AudioClip m_Phase1ClipP2;
        [SerializeField] AudioSource m_Phase1Source;
        [SerializeField, Min(0f), Tooltip("Seconds the gate waits when this role has no Phase 1 clip.")]
        float m_Phase1FallbackSeconds = 2f;
        [SerializeField, Tooltip("StoryProgressManager task each claimant reports when their line ends. Unique per variant, ASCII.")]
        string m_Phase1TaskId = DefaultPhase1TaskId;
        [SerializeField, Tooltip("Debug row heading for that task. ASCII.")]
        string m_Phase1GateLabel = "OA phase1 -- each player heard their line";

        [Header("Linking")]
        [SerializeField, Min(0.05f)] float m_TrailFadeIn = 1.5f;
        [SerializeField, Min(0.05f)] float m_TrailFadeOut = 0.5f;
        [SerializeField, Range(0f, 1f), Tooltip("Tremble while linked, far apart.")] float m_LinkShakeFar = 0.2f;
        [SerializeField, Range(0f, 1f), Tooltip("Tremble while linked, about to join.")] float m_LinkShakeNear = 0.75f;

        [Header("Join")]
        [SerializeField, Min(0.01f), Tooltip("Join points closer than this (metres) join the halves.")]
        float m_JoinDistance = 0.12f;
        [SerializeField, Min(0.05f), Tooltip("Seconds to glide into the assembled layout.")]
        float m_GlideDuration = 0.6f;
        [SerializeField, Tooltip("Glide easing. Empty = cubic ease-in (slow → fast).")]
        AnimationCurve m_GlideCurve = new();
        [SerializeField, Min(0f), Tooltip("Seconds of settle (wobble) between arriving and Phase 2.")]
        float m_SettleDuration = 0.6f;
        [SerializeField, Min(0f), Tooltip("Server takes ownership back this long after the join, so the " +
                                          "join reaches the holders before the ownership does.")]
        float m_ReclaimDelay = 0.25f;

        [Header("Phase 2 (one line each)")]
        [SerializeField, FormerlySerializedAs("m_Phase2Clip"),
         Tooltip("Player 1's closing line. Each machine picks by its own role, like Phase 1 -- but " +
                 "the beat's length is the longer of the two lines on every machine.")]
        AudioClip m_Phase2ClipP1;
        [SerializeField, Tooltip("Player 2's closing line.")]
        AudioClip m_Phase2ClipP2;
        [SerializeField] AudioSource m_Phase2Source;
        [SerializeField, Min(0.1f), Tooltip("Seconds Phase 2 lasts when neither role has a clip.")]
        float m_Phase2FallbackSeconds = 6f;
        [SerializeField, Min(0f), Tooltip("Float duration; 0 = the Phase 2 length.")]
        float m_FloatDurationOverride;
        [SerializeField, Tooltip("Float easing. Empty = cubic ease-in-out.")]
        AnimationCurve m_FloatCurve = new();

        [Header("End")]
        [SerializeField, Min(0f), Tooltip("Seconds the assembled object is left to be seen at " +
                                          "Destination after Phase 2 before it starts dissolving.")]
        float m_DissolveDelay = 1.5f;

        [SerializeField, Min(0f), Tooltip("Seconds the assembled object takes to dissolve away. " +
                                          "0 leaves it in the room for the show to switch off.")]
        float m_DissolveDuration = 2f;

        [Header("Events")]
        [Tooltip("Fired once per run on every machine, when Phase 2 has ended.")]
        public UnityEvent onCompleted = new();

        [Header("Debug row")]
        [SerializeField, Tooltip("Unique across the show. ASCII.")] string m_DebugId = "oa.assembly";
        [SerializeField, Tooltip("ASCII -- the panel's font has no CJK glyphs.")]
        string m_DebugLabel = "OA -- claim, link, join, float";

        // ---- replicated ------------------------------------------------------------------------

        readonly NetworkVariable<byte> m_Phase = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<int> m_Epoch = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<double> m_Phase0At = NewStamp();
        readonly NetworkVariable<double> m_AppearAt = NewStamp();
        readonly NetworkVariable<double> m_ClaimingAt = NewStamp();
        readonly NetworkVariable<double> m_LinkingAt = NewStamp();
        readonly NetworkVariable<double> m_JoinedAt = NewStamp();
        readonly NetworkVariable<Vector3> m_AnchorPosition = new(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<Quaternion> m_AnchorRotation = new(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // Only used when there is no StoryProgressManager: bit 1 = P1 reported, bit 2 = P2.
        readonly NetworkVariable<byte> m_FallbackPhase1Mask = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        static NetworkVariable<double> NewStamp() =>
            new(InteractionPhaseClock.Unset, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ---- local -----------------------------------------------------------------------------

        Pose m_CornerPoseA, m_CornerPoseB;
        float m_AppearTime = -1f;
        bool m_StartConditionWas;

        int m_SeenEpoch = int.MinValue;
        bool m_LocalJoinActive;
        Pose m_JoinStartA, m_JoinStartB;
        bool m_WobbleSent;
        bool m_Phase2Started;
        int m_CompletedEpoch = int.MinValue;
        int m_Phase1ClaimEpoch = int.MinValue;
        double m_Phase1ClaimStamp = InteractionPhaseClock.Unset;
        float m_Phase1EndsAt = -1f;
        bool m_Phase1Reported;
        bool m_Phase1Playing;

        // ---- public surface --------------------------------------------------------------------

        public OAPhase phase => (OAPhase)m_Phase.Value;
        public int epoch => m_Epoch.Value;
        public OAFragment fragmentA => m_FragmentA;
        public OAFragment fragmentB => m_FragmentB;
        public OATrail trail => m_Trail;
        public Transform destination => m_Destination;
        public Transform cornerP1 => m_CornerP1;
        public Transform cornerP2 => m_CornerP2;
        public float joinDistance => m_JoinDistance;
        public float glideDuration => m_GlideDuration;
        public float settleDuration => m_SettleDuration;
        public string phase1TaskId => m_Phase1TaskId;
        public double joinedAt => m_JoinedAt.Value;

        /// <summary>Grabs are possible (the halves are in the room and not yet joined).</summary>
        public bool grabWindowOpen => IsSpawned && (phase == OAPhase.Claiming || phase == OAPhase.Linking);

        /// <summary>A claimed, released half follows its claimant.</summary>
        public bool followActive => phase == OAPhase.Claiming || phase == OAPhase.Linking;

        /// <summary>This machine started its Phase 1 line for the current run.</summary>
        public bool phase1StartedLocally => m_Phase1ClaimEpoch == m_Epoch.Value && m_Phase1ClaimEpoch != int.MinValue;

        /// <summary>This machine's Phase 1 line is running (by the clock).</summary>
        public bool phase1PlayingLocally => m_Phase1Playing;

        /// <summary>This machine reported its Phase 1 half for the current run.</summary>
        public bool phase1ReportedLocally => phase1StartedLocally && m_Phase1Reported;

        /// <summary>Phase 2 has started on this machine.</summary>
        public bool phase2Started => m_Phase2Started;

        /// <summary>How many times onCompleted has fired on this machine (all runs).</summary>
        public int completedCount { get; private set; }

        /// <summary>Server time Phase 2 starts, or Unset.</summary>
        public double phase2At => InteractionPhaseClock.IsSet(m_JoinedAt.Value)
            ? m_JoinedAt.Value + m_GlideDuration + m_SettleDuration
            : InteractionPhaseClock.Unset;

        /// <summary>
        /// Seconds Phase 2 (and by default the float) lasts: the LONGER of the two lines, never the
        /// line this machine happens to be playing. Every machine has to agree about it -- see the
        /// class summary.
        /// </summary>
        public float phase2Length
        {
            get
            {
                float longest = Mathf.Max(m_Phase2ClipP1 != null ? m_Phase2ClipP1.length : 0f,
                                          m_Phase2ClipP2 != null ? m_Phase2ClipP2.length : 0f);
                return longest > 0f ? longest : m_Phase2FallbackSeconds;
            }
        }

        public float floatDuration => m_FloatDurationOverride > 0f ? m_FloatDurationOverride : phase2Length;

        /// <summary>Server time the beat is done, or Unset.</summary>
        public double doneAt => InteractionPhaseClock.IsSet(phase2At) ? phase2At + phase2Length : InteractionPhaseClock.Unset;

        public float dissolveDelay => m_DissolveDelay;
        public float dissolveDuration => m_DissolveDuration;

        /// <summary>Server time the end-of-beat dissolve starts, or Unset when there is none.</summary>
        public double dissolveAt => InteractionPhaseClock.IsSet(doneAt) && m_DissolveDuration > 0f
            ? doneAt + m_DissolveDelay
            : InteractionPhaseClock.Unset;

        /// <summary>
        /// How far the end-of-beat dissolve has got, 0..1. Drawn from <see cref="dissolveAt"/>
        /// rather than from the phase flip, like every other phase here: both headsets dissolve
        /// on the same frame, and a machine that joins or hitches late lands part-way through it
        /// instead of finding a whole lamp hanging in the room.
        /// </summary>
        public float dissolve01
        {
            get
            {
                if (!InteractionPhaseClock.IsSet(dissolveAt)) return 0f;
                float since = InteractionPhaseClock.Since(dissolveAt);
                return since <= 0f ? 0f : Mathf.Clamp01(since / m_DissolveDuration);
            }
        }

        /// <summary>The dissolve is over: the assembled object is drawn no more.</summary>
        public bool gone => dissolve01 >= 1f;

        /// <summary>Seconds from the Phase 0 stamp to the halves appearing (the marker on the timeline).</summary>
        public float appearTime
        {
            get
            {
                if (m_AppearTime < 0f) m_AppearTime = ReadAppearMarker();
                return m_AppearTime;
            }
        }

        /// <summary>Current distance between the join points (metres).</summary>
        public float currentJoinGap =>
            m_FragmentA != null && m_FragmentB != null && m_FragmentA.joinPoint != null && m_FragmentB.joinPoint != null
                ? Vector3.Distance(m_FragmentA.joinPoint.position, m_FragmentB.joinPoint.position)
                : float.PositiveInfinity;

        /// <summary>True if <paramref name="client"/> has claimed a half other than <paramref name="except"/>.</summary>
        public bool HasClaim(ulong client, OAFragment except)
        {
            if (client == NetGrabbable.NoHolder) return false;
            if (m_FragmentA != null && m_FragmentA != except && m_FragmentA.claimedBy == client) return true;
            if (m_FragmentB != null && m_FragmentB != except && m_FragmentB.claimedBy == client) return true;
            return false;
        }

        /// <summary>Builder hook.</summary>
        public void Configure(OAFragment a, OAFragment b, OATrail trail, Transform cornerP1, Transform cornerP2,
                              Transform destination, PlayableDirector director, AudioSource phase1, AudioSource phase2)
        {
            m_FragmentA = a; m_FragmentB = b; m_Trail = trail;
            m_CornerP1 = cornerP1; m_CornerP2 = cornerP2; m_Destination = destination;
            m_Director = director; m_Phase1Source = phase1; m_Phase2Source = phase2;
        }

        // ---- lifecycle -------------------------------------------------------------------------

        void Awake()
        {
            m_CornerPoseA = CornerPose(m_CornerP1, m_FragmentA);
            m_CornerPoseB = CornerPose(m_CornerP2, m_FragmentB);

            if (m_Director != null)
            {
                m_Director.playOnAwake = false;
                m_Director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                m_Director.extrapolationMode = DirectorWrapMode.Hold;
            }

            if (m_Trail != null && m_FragmentA != null && m_FragmentB != null)
                m_Trail.SetEnds(m_FragmentA.joinPoint, m_FragmentB.joinPoint);
        }

        static Pose CornerPose(Transform corner, OAFragment fragment)
        {
            if (corner != null) return new Pose(corner.position, corner.rotation);
            return fragment != null ? new Pose(fragment.transform.position, fragment.transform.rotation) : Pose.identity;
        }

        void OnEnable()
        {
            InteractionDebug.Register(this);
            if (!string.IsNullOrWhiteSpace(m_Phase1TaskId))
                InteractionDebug.RegisterTaskGate(m_Phase1TaskId, m_Phase1GateLabel);
        }

        void OnDisable() => InteractionDebug.Unregister(this);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // The start trigger is an edge: if the session is already running when this spawns,
            // that counts as the edge; after a Reset it does not, so a reset holds until Begin.
            m_StartConditionWas = false;
        }

        void Update()
        {
            if (IsServer && IsSpawned) ServerTick();
        }

        void LateUpdate()
        {
            CheckEpoch();
            DriveDirector();
            DrivePhase1();
            DriveViews();
            DriveTrail();
            DriveJoin();
        }

        // ---- server ----------------------------------------------------------------------------

        void ServerTick()
        {
            double now = InteractionPhaseClock.now;
            switch (phase)
            {
                case OAPhase.Dormant:
                {
                    bool condition = StartCondition();
                    if (condition && !m_StartConditionWas) ServerBegin("start condition");
                    m_StartConditionWas = condition;
                    break;
                }
                case OAPhase.Phase0:
                    if (now >= m_AppearAt.Value) SetPhase(OAPhase.Appear);
                    break;
                case OAPhase.Appear:
                    if (now >= m_ClaimingAt.Value) SetPhase(OAPhase.Claiming);
                    break;
                case OAPhase.Claiming:
                    if (Phase1GateComplete()) ServerLink("both Phase 1 lines heard");
                    break;
                case OAPhase.Linking:
                    if (currentJoinGap < m_JoinDistance) ServerJoin("join points met");
                    break;
                case OAPhase.Joined:
                    if (InteractionPhaseClock.IsSet(doneAt) && now >= doneAt) SetPhase(OAPhase.Done);
                    break;
            }
        }

        bool StartCondition()
        {
            if (m_BeatRoot != null) return m_BeatRoot.activeInHierarchy;
            if (!m_StartOnSessionInProgress) return false;
            GameSessionManager session = GameSessionManager.Instance;
            return session != null && session.sessionState == SessionState.InProgress;
        }

        void SetPhase(OAPhase p)
        {
            if (phase == p) return;
            m_Phase.Value = (byte)p;
            Debug.Log($"[OAController] phase -> {p}", this);
        }

        void ServerBegin(string why)
        {
            if (!IsServer) return;
            double now = InteractionPhaseClock.now;
            m_Phase0At.Value = now;
            m_AppearAt.Value = now + appearTime;
            m_ClaimingAt.Value = now + appearTime + m_RevealDuration;
            m_LinkingAt.Value = InteractionPhaseClock.Unset;
            m_JoinedAt.Value = InteractionPhaseClock.Unset;
            if (m_FragmentA != null) m_FragmentA.ServerPlace(m_CornerPoseA);
            if (m_FragmentB != null) m_FragmentB.ServerPlace(m_CornerPoseB);
            Debug.Log($"[OAController] begin ({why}); halves appear at +{appearTime:0.0}s.", this);
            SetPhase(OAPhase.Phase0);
        }

        /// <summary>Server: jump to Claiming with stamps that read as if Phase 0 had run.</summary>
        void ServerSkipToClaiming()
        {
            if (phase >= OAPhase.Claiming) return;
            double now = InteractionPhaseClock.now;
            if (phase == OAPhase.Dormant)
            {
                if (m_FragmentA != null) m_FragmentA.ServerPlace(m_CornerPoseA);
                if (m_FragmentB != null) m_FragmentB.ServerPlace(m_CornerPoseB);
            }
            m_Phase0At.Value = now - appearTime - m_RevealDuration;
            m_AppearAt.Value = now - m_RevealDuration;
            m_ClaimingAt.Value = now;
            SetPhase(OAPhase.Claiming);
        }

        bool Phase1GateComplete()
        {
            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress != null && progress.IsSpawned) return progress.IsTaskComplete(m_Phase1TaskId);

            // No progress manager: our own flags, asking only the roles that are present.
            GameSessionManager session = GameSessionManager.Instance;
            bool needP1 = session == null || session.IsRoleOccupied(PlayerRole.Player1);
            bool needP2 = session != null && session.IsRoleOccupied(PlayerRole.Player2);
            byte mask = m_FallbackPhase1Mask.Value;
            return (!needP1 || (mask & 1) != 0) && (!needP2 || (mask & 2) != 0) && (needP1 || needP2);
        }

        void ServerLink(string why)
        {
            m_LinkingAt.Value = InteractionPhaseClock.now;
            Debug.Log($"[OAController] linking ({why}).", this);
            SetPhase(OAPhase.Linking);
        }

        void ServerJoin(string why)
        {
            if (!IsServer || m_FragmentA == null || m_FragmentB == null) return;

            Pose anchor = ComputeJoinAnchor();
            m_AnchorPosition.Value = anchor.position;
            m_AnchorRotation.Value = anchor.rotation;
            m_JoinedAt.Value = InteractionPhaseClock.now;
            m_FragmentA.ServerJoin(m_ReclaimDelay);
            m_FragmentB.ServerJoin(m_ReclaimDelay);
            Debug.Log($"[OAController] joined ({why}) at {anchor.position:F3}, gap {currentJoinGap:0.000} m.", this);
            SetPhase(OAPhase.Joined);
        }

        /// <summary>
        /// Where the assembly frame (origin at the join point) sits when the halves meet: between
        /// the two join points, upright, turned to the yaw half A is being held at -- so the glide
        /// is short and the object does not spin as it closes.
        /// </summary>
        Pose ComputeJoinAnchor()
        {
            Vector3 mid = (m_FragmentA.joinPoint.position + m_FragmentB.joinPoint.position) * 0.5f;
            Quaternion relative = m_FragmentA.transform.rotation * Quaternion.Inverse(m_FragmentA.layout.rotation);
            Vector3 fwd = Vector3.ProjectOnPlane(relative * Vector3.forward, Vector3.up);
            Quaternion yaw = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : Quaternion.identity;
            return new Pose(mid, yaw);
        }

        void ServerReset()
        {
            if (!IsServer) return;
            m_Epoch.Value++;
            m_Phase0At.Value = InteractionPhaseClock.Unset;
            m_AppearAt.Value = InteractionPhaseClock.Unset;
            m_ClaimingAt.Value = InteractionPhaseClock.Unset;
            m_LinkingAt.Value = InteractionPhaseClock.Unset;
            m_JoinedAt.Value = InteractionPhaseClock.Unset;
            m_FallbackPhase1Mask.Value = 0;
            if (m_FragmentA != null) m_FragmentA.ServerResetTo(m_CornerPoseA);
            if (m_FragmentB != null) m_FragmentB.ServerResetTo(m_CornerPoseB);
            StoryProgressManager.Instance?.ResetTask(m_Phase1TaskId);
            m_StartConditionWas = StartCondition(); // re-armed: waits for Begin or a fresh edge
            SetPhase(OAPhase.Dormant);
            Debug.Log("[OAController] reset; re-armed.", this);
        }

        /// <summary>Server: claim a free half for <paramref name="role"/> as if its player had grabbed it.</summary>
        void ServerForceClaim(PlayerRole role)
        {
            OAFragment preferred = role == PlayerRole.Player2 ? m_FragmentB : m_FragmentA;
            OAFragment other = preferred == m_FragmentA ? m_FragmentB : m_FragmentA;
            OAFragment target = preferred != null && !preferred.isClaimed ? preferred
                              : other != null && !other.isClaimed ? other : null;
            if (target == null) return;

            ulong client = OAFragment.ForcedClaim;
            GameSessionManager session = GameSessionManager.Instance;
            if (session != null)
            {
                foreach (PlayerRoleEntry entry in session.playerRoles)
                {
                    if (entry.role == role && entry.isConnected) { client = entry.clientId; break; }
                }
            }
            if (client != OAFragment.ForcedClaim && HasClaim(client, target)) client = OAFragment.ForcedClaim;
            target.ServerClaim(client, role);
        }

        void ServerForcePhase1(PlayerRole role)
        {
            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress != null && progress.IsSpawned) progress.DebugSetRoleDone(m_Phase1TaskId, role, true);
            else m_FallbackPhase1Mask.Value |= (byte)(role == PlayerRole.Player2 ? 2 : 1);
        }

        [Rpc(SendTo.Server)]
        void ReportPhase1FallbackRpc(RpcParams rpcParams = default)
        {
            GameSessionManager session = GameSessionManager.Instance;
            PlayerRole role = PlayerRole.Player1;
            if (session != null) session.TryGetRoleForClient(rpcParams.Receive.SenderClientId, out role);
            m_FallbackPhase1Mask.Value |= (byte)(role == PlayerRole.Player2 ? 2 : 1);
        }

        [Rpc(SendTo.Server)]
        void DebugAdvanceRpc() => ServerAdvance();

        /// <summary>Server: Dormant → begin; Phase 0 / Appear → straight to Claiming.</summary>
        void ServerAdvance()
        {
            if (phase == OAPhase.Dormant) ServerBegin("debug");
            else if (phase < OAPhase.Claiming) ServerSkipToClaiming();
        }

        // ---- every machine: epoch --------------------------------------------------------------

        void CheckEpoch()
        {
            int e = m_Epoch.Value;
            if (e == m_SeenEpoch) return;
            bool first = m_SeenEpoch == int.MinValue;
            m_SeenEpoch = e;
            if (first) return;
            LocalReset();
        }

        void LocalReset()
        {
            m_LocalJoinActive = false;
            m_WobbleSent = false;
            m_Phase2Started = false;
            m_Phase1ClaimEpoch = int.MinValue;
            m_Phase1ClaimStamp = InteractionPhaseClock.Unset;
            m_Phase1EndsAt = -1f;
            m_Phase1Reported = false;
            m_Phase1Playing = false;
            if (m_Phase1Source != null) m_Phase1Source.Stop();
            if (m_Phase2Source != null) m_Phase2Source.Stop();
            ResetFragment(m_FragmentA, m_CornerPoseA);
            ResetFragment(m_FragmentB, m_CornerPoseB);
        }

        static void ResetFragment(OAFragment f, Pose corner)
        {
            if (f == null) return;
            f.EndLocalAnimation();
            if (!f.IsServer) f.SetLocalPose(corner.position, corner.rotation);
            if (f.view != null)
            {
                f.view.CancelWobble();
                f.view.reveal01 = 0f;
                f.view.shake01 = 0f;
            }
        }

        // ---- every machine: Phase 0 director ---------------------------------------------------

        void DriveDirector()
        {
            if (m_Director == null) return;

            if (!IsSpawned || phase == OAPhase.Dormant || !InteractionPhaseClock.IsSet(m_Phase0At.Value))
            {
                if (m_Director.state == PlayState.Playing || m_Director.time > 0d)
                {
                    m_Director.Stop();
                    m_Director.time = 0d;
                }
                return;
            }

            double target = InteractionPhaseClock.now - m_Phase0At.Value;
            if (target < 0d) target = 0d;
            double duration = m_Director.duration;

            if (m_Director.state != PlayState.Playing)
            {
                m_Director.time = duration > 0d ? System.Math.Min(target, duration) : target;
                m_Director.Play();
                return;
            }

            if (target < duration)
            {
                double predicted = m_Director.time + Time.unscaledDeltaTime;
                if (System.Math.Abs(target - predicted) > m_ResyncThreshold)
                {
                    m_Director.time = target;
                    m_Director.Evaluate();
                }
            }
        }

        float ReadAppearMarker()
        {
            var timeline = m_Director != null ? m_Director.playableAsset as TimelineAsset : null;
            if (timeline != null && timeline.markerTrack != null)
            {
                foreach (IMarker marker in timeline.markerTrack.GetMarkers())
                    if (marker is OAAppearMarker) return (float)marker.time;
            }
            if (timeline != null)
            {
                foreach (TrackAsset track in timeline.GetOutputTracks())
                    foreach (IMarker marker in track.GetMarkers())
                        if (marker is OAAppearMarker) return (float)marker.time;
            }
            return m_AppearFallback;
        }

        // ---- every machine: Phase 1 ------------------------------------------------------------

        void DrivePhase1()
        {
            if (!IsSpawned) return;

            if (!phase1StartedLocally && phase >= OAPhase.Claiming && phase < OAPhase.Joined)
            {
                OAFragment mine = m_FragmentA != null && m_FragmentA.claimedLocally ? m_FragmentA
                                : m_FragmentB != null && m_FragmentB.claimedLocally ? m_FragmentB : null;
                if (mine != null) StartPhase1(mine);
            }

            if (!phase1StartedLocally) { m_Phase1Playing = false; return; }

            float now = ExperienceClock.now;
            m_Phase1Playing = now < m_Phase1EndsAt;
            if (m_Phase1Reported || now < m_Phase1EndsAt) return;

            m_Phase1Reported = true;
            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress != null && progress.IsSpawned) progress.ReportTaskCompleteRpc(m_Phase1TaskId);
            else ReportPhase1FallbackRpc();
            Debug.Log($"[OAController] Phase 1 line ended; reported '{m_Phase1TaskId}' as {InteractionDebug.LocalRole()}.", this);
        }

        void StartPhase1(OAFragment mine)
        {
            m_Phase1ClaimEpoch = m_Epoch.Value;
            m_Phase1ClaimStamp = mine.claimedAt;
            m_Phase1Reported = false;

            PlayerRole role = InteractionDebug.LocalRole();
            if (role == PlayerRole.Unassigned) role = mine.claimedRole;
            AudioClip clip = role == PlayerRole.Player2 ? m_Phase1ClipP2 : m_Phase1ClipP1;

            if (m_Phase1Source != null && clip != null)
            {
                m_Phase1Source.clip = clip;
                m_Phase1Source.Play();
            }

            // The clock, not isPlaying: a missing clip or a muted device still has to move the gate.
            m_Phase1EndsAt = ExperienceClock.now + (clip != null ? clip.length : m_Phase1FallbackSeconds);
            m_Phase1Playing = true;
            Debug.Log($"[OAController] claimed '{mine.name}' as {role}; playing " +
                      $"'{(clip != null ? clip.name : "no clip -- " + m_Phase1FallbackSeconds + "s fallback")}'.", this);
        }

        // ---- every machine: views and trail ----------------------------------------------------

        void DriveViews()
        {
            // From the stamp, not the phase: the server flips Phase0 → Appear a frame after the
            // stamp passes, and a late joiner should land mid-dissolve rather than restart it.
            float reveal = 0f;
            if (IsSpawned && phase >= OAPhase.Phase0 && InteractionPhaseClock.IsSet(m_AppearAt.Value))
                reveal = Mathf.Clamp01(InteractionPhaseClock.Since(m_AppearAt.Value) / m_RevealDuration);

            // ...and out again once the beat is over, off its own stamp for the same reason.
            // reveal 0 is BreathingView turning the renderers off, so the dissolve is the end of it.
            if (reveal > 0f) reveal = Mathf.Min(reveal, 1f - dissolve01);

            float shake = 0f;
            if (phase == OAPhase.Linking)
                shake = Mathf.Lerp(m_LinkShakeFar, m_LinkShakeNear, m_Trail != null ? m_Trail.intensity : 0f);

            ApplyView(m_FragmentA, reveal, shake);
            ApplyView(m_FragmentB, reveal, shake);
        }

        void ApplyView(OAFragment f, float reveal, float shake)
        {
            if (f == null || f.view == null) return;
            BreathingView v = f.view;
            v.reveal01 = reveal;
            v.shake01 = shake;
            switch (phase)
            {
                case OAPhase.Appear:
                case OAPhase.Claiming:
                    v.breathing = f.isHeld || f.heldLocally ? 0f : 1f;
                    break;
                case OAPhase.Linking:
                    v.breathing = 1f;
                    break;
                case OAPhase.Joined:
                case OAPhase.Done:
                    v.breathing = 0f;
                    break;
                default:
                    v.breathing = 1f;
                    break;
            }
        }

        void DriveTrail()
        {
            if (m_Trail == null) return;
            float vis = 0f;
            if (IsSpawned)
            {
                if (phase == OAPhase.Linking && InteractionPhaseClock.IsSet(m_LinkingAt.Value))
                    vis = Mathf.Clamp01(InteractionPhaseClock.Since(m_LinkingAt.Value) / m_TrailFadeIn);
                else if (phase >= OAPhase.Joined && InteractionPhaseClock.IsSet(m_JoinedAt.Value))
                    vis = Mathf.Clamp01(1f - InteractionPhaseClock.Since(m_JoinedAt.Value) / m_TrailFadeOut);
            }
            m_Trail.visibility = vis;
        }

        // ---- every machine: join, settle, float, done ------------------------------------------

        void DriveJoin()
        {
            if (!IsSpawned || phase < OAPhase.Joined || !InteractionPhaseClock.IsSet(m_JoinedAt.Value)) return;
            if (m_FragmentA == null || m_FragmentB == null) return;

            float since = InteractionPhaseClock.Since(m_JoinedAt.Value);

            if (!m_LocalJoinActive)
            {
                m_LocalJoinActive = true;
                m_WobbleSent = false;
                m_FragmentA.BeginLocalAnimation();
                m_FragmentB.BeginLocalAnimation();
                m_JoinStartA = new Pose(m_FragmentA.transform.position, m_FragmentA.transform.rotation);
                m_JoinStartB = new Pose(m_FragmentB.transform.position, m_FragmentB.transform.rotation);
                Debug.Log($"[OAController] join seen {since:0.00}s after the stamp; gliding.", this);
            }

            Pose anchor = AnchorAt(InteractionPhaseClock.now);
            float k = since >= m_GlideDuration ? 1f : InteractionEasing.EaseIn(since / m_GlideDuration, m_GlideCurve);
            Place(m_FragmentA, m_JoinStartA, anchor, k);
            Place(m_FragmentB, m_JoinStartB, anchor, k);

            if (!m_WobbleSent && since >= m_GlideDuration)
            {
                m_WobbleSent = true;
                float arrived = (float)(m_JoinedAt.Value + m_GlideDuration);
                if (m_FragmentA.view != null) m_FragmentA.view.Wobble(arrived);
                if (m_FragmentB.view != null) m_FragmentB.view.Wobble(arrived);
            }

            DrivePhase2();

            double done = doneAt;
            if (InteractionPhaseClock.IsSet(done) && InteractionPhaseClock.now >= done && m_CompletedEpoch != m_Epoch.Value)
            {
                m_CompletedEpoch = m_Epoch.Value;
                completedCount++;
                Debug.Log("[OAController] Phase 2 ended; onCompleted.", this);
                onCompleted?.Invoke();
            }
        }

        static void Place(OAFragment f, Pose start, Pose anchor, float k)
        {
            Pose layout = f.layout;
            Vector3 targetPos = anchor.position + anchor.rotation * layout.position;
            Quaternion targetRot = anchor.rotation * layout.rotation;
            if (k >= 1f) f.SetLocalPose(targetPos, targetRot);
            else f.SetLocalPose(Vector3.LerpUnclamped(start.position, targetPos, k),
                                Quaternion.SlerpUnclamped(start.rotation, targetRot, k));
        }

        /// <summary>The assembly frame's pose at server time <paramref name="now"/>: the join anchor, then the float.</summary>
        public Pose AnchorAt(double now)
        {
            var join = new Pose(m_AnchorPosition.Value, m_AnchorRotation.Value);
            double start = phase2At;
            if (m_Destination == null || !InteractionPhaseClock.IsSet(start) || now <= start) return join;

            float t = Mathf.Clamp01((float)((now - start) / Mathf.Max(floatDuration, 0.01f)));
            float k = InteractionEasing.EaseInOut(t, m_FloatCurve);
            return new Pose(Vector3.LerpUnclamped(join.position, m_Destination.position, k),
                            Quaternion.SlerpUnclamped(join.rotation, m_Destination.rotation, k));
        }

        void DrivePhase2()
        {
            double start = phase2At;
            if (m_Phase2Started || !InteractionPhaseClock.IsSet(start) || InteractionPhaseClock.now < start) return;

            m_Phase2Started = true;
            PlayerRole role = Phase2Role();
            AudioClip clip = role == PlayerRole.Player2 ? m_Phase2ClipP2 : m_Phase2ClipP1;
            float since = (float)(InteractionPhaseClock.now - start);
            if (m_Phase2Source != null && clip != null && since < clip.length)
            {
                m_Phase2Source.clip = clip;
                m_Phase2Source.time = Mathf.Clamp(since, 0f, clip.length - 0.01f);
                m_Phase2Source.Play();
            }
            Debug.Log($"[OAController] Phase 2 as {role} ('{(clip != null ? clip.name : "no clip -- silent for " + phase2Length + "s")}'); " +
                      $"floating to '{(m_Destination != null ? m_Destination.name : "nowhere")}' over {floatDuration:0.0}s.", this);
        }

        /// <summary>
        /// Which Phase 2 line this machine plays. The local role, and -- when that has not resolved
        /// yet -- the role of the half this machine claimed, the same fallback Phase 1 uses. Unlike
        /// Phase 1 this is read once, as the line starts: the length of the beat does not depend on
        /// it, so a role that resolves mid-line changes nothing but what is heard next time.
        /// </summary>
        PlayerRole Phase2Role()
        {
            PlayerRole role = InteractionDebug.LocalRole();
            if (role != PlayerRole.Unassigned) return role;

            OAFragment mine = m_FragmentA != null && m_FragmentA.claimedLocally ? m_FragmentA
                            : m_FragmentB != null && m_FragmentB.claimedLocally ? m_FragmentB : null;
            return mine != null ? mine.claimedRole : PlayerRole.Unassigned;
        }

        // ---- IDebugInteraction -----------------------------------------------------------------

        public string debugId => m_DebugId;
        public string debugLabel => m_DebugLabel;
        public DebugInteractionScope debugScope => DebugInteractionScope.Gate;
        public string debugFireLabel => phase == OAPhase.Dormant ? "Begin" : "Skip to claim";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = phase >= OAPhase.Phase0;
            state.player1 = ClaimedByRole(PlayerRole.Player1);
            state.player2 = ClaimedByRole(PlayerRole.Player2);

            if (!IsSpawned)
            {
                state.detail = "not spawned (no session)";
                return state;
            }

            double now = InteractionPhaseClock.now;
            switch (phase)
            {
                case OAPhase.Dormant:
                    state.detail = m_BeatRoot != null ? $"dormant -- '{m_BeatRoot.name}' is not up" : "dormant -- waiting for Start";
                    break;
                case OAPhase.Phase0:
                    state.detail = $"phase0 VO + lights; halves in {m_AppearAt.Value - now:0.0}s";
                    break;
                case OAPhase.Appear:
                    state.detail = "halves appearing";
                    break;
                case OAPhase.Claiming:
                    state.detail = $"claim -- A:{Who(m_FragmentA)} B:{Who(m_FragmentB)} | phase1 {Phase1Flags()}";
                    break;
                case OAPhase.Linking:
                    state.detail = $"linking -- {currentJoinGap:0.00} m apart (join < {m_JoinDistance:0.00}), " +
                                   $"trail {(m_Trail != null ? m_Trail.intensity : 0f):0.00}";
                    break;
                case OAPhase.Joined:
                    state.detail = now < phase2At
                        ? "joined -- settling"
                        : $"joined -- phase2, {doneAt - now:0.0}s left";
                    break;
                case OAPhase.Done:
                {
                    float d = dissolve01;
                    state.detail =
                        !InteractionPhaseClock.IsSet(dissolveAt) ? "done -- onCompleted fired, no dissolve"
                        : d <= 0f ? $"done -- dissolving in {dissolveAt - now:0.0}s"
                        : d < 1f ? $"done -- dissolving, {d * 100f:0}%"
                        : "done -- dissolved away";
                    break;
                }
            }
            return state;
        }

        bool ClaimedByRole(PlayerRole role) =>
            (m_FragmentA != null && m_FragmentA.isClaimed && m_FragmentA.claimedRole == role) ||
            (m_FragmentB != null && m_FragmentB.isClaimed && m_FragmentB.claimedRole == role);

        static string Who(OAFragment f)
        {
            if (f == null) return "?";
            if (!f.isClaimed) return "--";
            string role = f.claimedRole == PlayerRole.Player1 ? "P1" : f.claimedRole == PlayerRole.Player2 ? "P2" : "?";
            return f.claimedBy == OAFragment.ForcedClaim ? role + "(forced)" : role;
        }

        string Phase1Flags()
        {
            StoryProgressManager progress = StoryProgressManager.Instance;
            bool p1 = false, p2 = false;
            if (progress != null && progress.IsSpawned)
            {
                foreach (TaskProgress t in progress.tasks)
                {
                    if (t.taskId.ToString() != m_Phase1TaskId) continue;
                    p1 = t.player1Done; p2 = t.player2Done;
                }
            }
            else
            {
                p1 = (m_FallbackPhase1Mask.Value & 1) != 0;
                p2 = (m_FallbackPhase1Mask.Value & 2) != 0;
            }
            return $"P1:{(p1 ? "done" : "--")} P2:{(p2 ? "done" : "--")}";
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            if (!IsSpawned) return false;
            if (target == DebugFireTarget.Local) return phase < OAPhase.Claiming;
            if (!InteractionDebugRelay.canForce) return false;
            return phase < OAPhase.Joined;
        }

        /// <summary>
        /// Local: Begin when dormant, otherwise skip Phase 0 to the claim.
        /// P1 / P2: claim a half for that role if it has none, else mark its Phase 1 line heard.
        /// Both: join now.
        /// </summary>
        public void DebugFire(DebugFireTarget target)
        {
            if (!DebugCanFire(target)) return;

            if (target == DebugFireTarget.Local)
            {
                if (IsServer) ServerAdvance();
                else DebugAdvanceRpc();
                return;
            }

            if (!IsServer) return;
            if (phase < OAPhase.Claiming) ServerSkipToClaiming();

            if (target == DebugFireTarget.Both)
            {
                if (phase < OAPhase.Linking) ServerLink("debug");
                ServerJoin("debug");
                return;
            }

            PlayerRole role = target == DebugFireTarget.Player2 ? PlayerRole.Player2 : PlayerRole.Player1;
            if (!ClaimedByRole(role)) ServerForceClaim(role);
            else ServerForcePhase1(role);
            Debug.Log($"[OAController] debug fire {target} at {phase}.", this);
        }

        public bool DebugCanReset() => IsSpawned && InteractionDebugRelay.canForce && phase != OAPhase.Dormant;

        public void DebugReset()
        {
            if (DebugCanReset()) ServerReset();
        }
    }
}
