using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Interaction.Home
{
    /// <summary>
    /// Home: five paper fragments float around the room; either player brings each one to the
    /// mini-house in the middle; every fragment that goes home brings back its wall of the big
    /// room, and when all five are home the ceiling follows and the paper dissolves.
    ///
    /// THIS OWNS THE BEAT, the fragments own the mechanic (<see cref="HomeFragment"/>). Four
    /// things here are the server's decisions, and only those: when the beat starts (and where
    /// the fragments are scattered), when the voice-over runs out (<see cref="Timings.timeout"/>:
    /// every fragment still out dissolves away and its wall comes anyway), a debug place, and a
    /// debug reset. Everything else -- which walls are back, the ceiling, the paper dissolving, the
    /// two events -- is drawn on every machine from the fragments' replicated state and stamps, so
    /// it costs no messages and a late joiner fast-forwards to where the room has got to.
    ///
    /// ARMING, in order of precedence:
    ///   1. <see cref="m_BeatRoot"/> set: the beat starts when that object is active (MainScene).
    ///   2. otherwise, when the session goes InProgress (the staff panel's Start) -- the demo.
    ///   3. the debug row's Begin, always.
    /// The fragments are in the scene from the first frame (in-scene NetworkObjects must be, to be
    /// spawned at all) and held Dormant -- invisible, ungrabbable -- until then, and the big room
    /// is hidden while dormant (<see cref="m_HideRoomUntilArmed"/>).
    ///
    /// No NetworkObject here, on purpose: the prefab root must not be one (it would have to be
    /// spawned before its children could be), and nothing here needs to be replicated -- module
    /// requests from a client ride on a fragment's RPCs.
    /// </summary>
    [DisallowMultipleComponent]
    public class HomeAssembly : MonoBehaviour, IDebugInteraction
    {
        [Serializable]
        public class Pair
        {
            public HomeFragment fragment;
            [Tooltip("The wall of the big room this fragment brings back.")]
            public HomeRevealGroup group;
        }

        [Serializable]
        public struct Timings
        {
            [Min(0.1f), Tooltip("Seconds held inside zone B before it places itself.")]
            public float dwell;
            [Min(0.01f), Tooltip("Seconds of the glide from wherever it is to its slot (ease-in).")]
            public float placeTravel;
            [Min(0f), Tooltip("Seconds the fragments take to appear when the beat starts.")]
            public float appearDuration;
            [Min(0f), Tooltip("Seconds a fragment takes to dissolve when the timeout takes it.")]
            public float vanishDuration;
            [Min(0f), Tooltip("Seconds the placed fragments take to dissolve at the end.")]
            public float fragmentDissolve;
            [Min(1f), Tooltip("Seconds from the beat starting until everything still out is taken " +
                              "(the voice-over's length).")]
            public float timeout;

            public static Timings Default => new()
            {
                dwell = 1.5f, placeTravel = 0.6f, appearDuration = 1.2f, vanishDuration = 0.6f,
                fragmentDissolve = 1.5f, timeout = 180f,
            };
        }

        [Header("Pieces (built by Tools > Home > Build Home Interaction)")]
        [SerializeField] Pair[] m_Pairs = Array.Empty<Pair>();

        [SerializeField, Tooltip("Top_Wall: no fragment; plays after the other five walls are back.")]
        HomeRevealGroup m_FinalGroup;

        [SerializeField, Tooltip("Where the fragments are scattered (minus zone B).")]
        HomeZone m_ZoneA;

        [SerializeField, Tooltip("The assembly zone around the mini-house.")]
        HomeZone m_ZoneB;

        [Header("Arming")]
        [SerializeField, Tooltip("MainScene: the beat's own object. Set, it alone starts the beat. " +
                                 "Empty (the demo): the session going InProgress starts it.")]
        GameObject m_BeatRoot;

        [SerializeField, Tooltip("With no beat root, start when the staff panel starts the session.")]
        bool m_ArmOnSessionStart = true;

        [SerializeField, Tooltip("Hide the big room until the beat starts (it is authored visible).")]
        bool m_HideRoomUntilArmed = true;

        [Header("Scatter")]
        [SerializeField, Tooltip("Seed; each epoch scatters differently but reproducibly.")]
        int m_Seed = 20251104;

        [SerializeField, Min(0f), Tooltip("Zone B grown by this much is kept clear of fragments.")]
        float m_ExclusionMargin = 0.15f;

        [SerializeField, Min(0f), Tooltip("Minimum metres between fragment centres.")]
        float m_MinSpacing = 0.45f;

        [SerializeField, Min(0f), Tooltip("Metres kept clear of zone A's faces.")]
        float m_EdgeInset = 0.12f;

        [SerializeField, Min(10)] int m_ScatterTries = 600;

        [Header("Timing")]
        [SerializeField] Timings m_Timings = Timings.Default;

        [SerializeField, Tooltip("Optional curve for the glide into the slot. Empty = cubic ease-in.")]
        AnimationCurve m_PlaceCurve = new();

        [SerializeField, Min(0f), Tooltip("Seconds after a fragment lands before its wall starts.")]
        float m_GroupDelay = 0.25f;

        [SerializeField, Min(0f), Tooltip("Seconds after the fifth wall is back before the ceiling starts.")]
        float m_FinalGroupDelay = 0.5f;

        [SerializeField, Min(0f), Tooltip("Seconds the finished mini-house is left to be seen before " +
                                          "the paper dissolves.")]
        float m_CompleteDelay = 1.5f;

        [SerializeField, Min(0f), Tooltip("Seconds between one left-over fragment vanishing and the " +
                                          "next when the timeout takes them -- all at once reads as a bug.")]
        float m_TimeoutStagger = 0.35f;

        [SerializeField, Min(0f), Tooltip("Seconds the server waits after placing before taking " +
                                          "ownership back, so the state reaches the holder first.")]
        float m_ReclaimDelay = 0.1f;

        [Header("Events (every machine, once per run)")]
        [SerializeField] UnityEvent m_OnAllResolved = new();
        [SerializeField, Tooltip("Everything visual is finished: ceiling down, paper gone. The hook " +
                                 "for the next step.")]
        UnityEvent m_OnCompleted = new();

        [Header("Debug row")]
        [SerializeField] string m_DebugId = "home.assembly";
        [SerializeField] string m_DebugLabel = "Home -- five paper fragments bring the room back";

        // Server/offline.
        double m_ExpireAt = InteractionPhaseClock.Unset;
        double m_NextVanishAt = InteractionPhaseClock.Unset;
        int m_OfflineEpoch;

        // This machine's presentation.
        bool m_Prepared;
        bool m_KeyValid;
        bool m_KeyOffline;
        int m_KeyEpoch;
        double m_KeyArmedAt;
        bool[] m_GroupStarted = Array.Empty<bool>();
        bool m_FinalStarted;
        bool m_AllResolvedFired;
        bool m_CompletedFired;
        double m_AllResolvedAt = InteractionPhaseClock.Unset;
        double m_CompletedAt = InteractionPhaseClock.Unset;
        int m_AllResolvedCount;
        int m_CompletedCount;

        // ---- public surface -------------------------------------------------------------------

        public UnityEvent onAllResolved => m_OnAllResolved;
        public UnityEvent onCompleted => m_OnCompleted;
        public HomeZone zoneA => m_ZoneA;
        public HomeZone zoneB => m_ZoneB;
        public Timings timings => m_Timings;
        public AnimationCurve placeCurve => m_PlaceCurve;
        public float reclaimDelay => m_ReclaimDelay;
        public float dwell => m_Timings.dwell;
        public float minSpacing => m_MinSpacing;
        public float exclusionMargin => m_ExclusionMargin;
        public IReadOnlyList<Pair> pairs => m_Pairs;
        public HomeRevealGroup finalGroup => m_FinalGroup;
        public GameObject beatRoot { get => m_BeatRoot; set => m_BeatRoot = value; }

        /// <summary>Times the events have fired on this machine (tests).</summary>
        public int allResolvedCount => m_AllResolvedCount;
        public int completedCount => m_CompletedCount;
        public bool completed => m_CompletedFired;
        public bool allResolved => m_AllResolvedFired;

        public bool built => m_Pairs != null && m_Pairs.Length > 0 && m_ZoneA != null && m_ZoneB != null;

        /// <summary>The beat has started (as replicated).</summary>
        public bool armed
        {
            get
            {
                foreach (Pair p in m_Pairs)
                    if (p?.fragment != null && p.fragment.isArmed) return true;
                return false;
            }
        }

        public double armedAt
        {
            get
            {
                foreach (Pair p in m_Pairs)
                    if (p?.fragment != null && p.fragment.isArmed) return p.fragment.armedAt;
                return InteractionPhaseClock.Unset;
            }
        }

        public int epoch
        {
            get
            {
                int e = 0;
                foreach (Pair p in m_Pairs)
                    if (p?.fragment != null) e = Mathf.Max(e, p.fragment.epoch);
                return e;
            }
        }

        public int placedCount => Count(HomeFragmentState.Placed);
        public int vanishedCount => Count(HomeFragmentState.Vanished);
        public int fragmentCount
        {
            get
            {
                int n = 0;
                foreach (Pair p in m_Pairs) if (p?.fragment != null) n++;
                return n;
            }
        }

        int Count(HomeFragmentState s)
        {
            int n = 0;
            foreach (Pair p in m_Pairs) if (p?.fragment != null && p.fragment.state == s) n++;
            return n;
        }

        /// <summary>This machine decides (server, or nothing is listening).</summary>
        public bool authority
        {
            get
            {
                if (HomeFragment.offline) return true;
                HomeFragment f = AnyFragment();
                return f != null && f.IsSpawned && f.IsServer;
            }
        }

        /// <summary>Shared-clock deadline of the timeout (server's view), or Unset.</summary>
        public double deadline
        {
            get
            {
                if (InteractionPhaseClock.IsSet(m_ExpireAt)) return m_ExpireAt;
                double a = armedAt;
                return InteractionPhaseClock.IsSet(a) ? a + m_Timings.timeout : InteractionPhaseClock.Unset;
            }
        }

        HomeFragment AnyFragment()
        {
            foreach (Pair p in m_Pairs) if (p?.fragment != null) return p.fragment;
            return null;
        }

        // ---- lifecycle ------------------------------------------------------------------------

        void Awake()
        {
            m_Pairs ??= Array.Empty<Pair>();
            m_GroupStarted = new bool[m_Pairs.Length];
            PrepareGroups();
        }

        void OnEnable() => InteractionDebug.Register(this);
        void OnDisable() => InteractionDebug.Unregister(this);

        void PrepareGroups()
        {
            if (m_Prepared) return;
            m_Prepared = true;
            foreach (Pair p in m_Pairs) if (p?.group != null) p.group.Prepare();
            if (m_FinalGroup != null) m_FinalGroup.Prepare();
            if (m_HideRoomUntilArmed) HideRoom();
            if (!built)
                Debug.LogWarning($"[Home] '{name}' is not built; run Tools > Home > Build Home Interaction.", this);
        }

        void HideRoom()
        {
            foreach (Pair p in m_Pairs) if (p?.group != null) p.group.Hide();
            if (m_FinalGroup != null) m_FinalGroup.Hide();
        }

        void Update()
        {
            if (!built) return;

            if (authority)
            {
                if (!armed && ShouldArm()) AuthorityStart();
                if (armed) AuthorityTimeout();
            }

            Present();
        }

        bool ShouldArm()
        {
            if (m_BeatRoot != null) return m_BeatRoot.activeInHierarchy;
            if (!m_ArmOnSessionStart) return false;
            GameSessionManager session = GameSessionManager.Instance;
            return session != null && session.IsSpawned && session.sessionState == SessionState.InProgress;
        }

        // ---- server ---------------------------------------------------------------------------

        /// <summary>
        /// Start the beat (or start it again, for a reset): new epoch, fragments scattered in A
        /// minus B. Server/offline; a client asks the server.
        /// </summary>
        public void AuthorityStart()
        {
            if (!built) return;
            if (!authority)
            {
                HomeFragment f = AnyFragment();
                if (f != null && f.IsSpawned) f.RequestArmRpc();
                return;
            }

            int newEpoch = Mathf.Max(epoch, m_OfflineEpoch) + 1;
            m_OfflineEpoch = newEpoch;
            m_ExpireAt = InteractionPhaseClock.Unset;
            m_NextVanishAt = InteractionPhaseClock.Unset;
            double stamp = InteractionPhaseClock.now;

            var rng = new System.Random(unchecked(m_Seed * 7919 + newEpoch * 104729));
            var centres = new List<Vector3>();
            foreach (Pair p in m_Pairs)
            {
                HomeFragment f = p?.fragment;
                if (f == null) continue;

                Vector3 centre = PickScatterPoint(rng, centres);
                centres.Add(centre);

                // Random yaw about world up; the fragment keeps its own posture (walls stand, the
                // floor lies flat), so each one is recognisable from where it floats.
                float yaw = (float)rng.NextDouble() * 360f;
                Quaternion rot = Quaternion.AngleAxis(yaw, Vector3.up) *
                                 (f.slot != null ? f.slot.rotation : f.transform.rotation);
                Vector3 pivot = centre - rot * Vector3.Scale(f.transform.lossyScale, f.localCenter);
                f.AuthorityArm(pivot, rot, stamp, newEpoch);
            }

            Debug.Log($"[Home] '{name}': the beat is up (epoch {newEpoch}). {fragmentCount} fragments in " +
                      $"the room; everything still out goes in {m_Timings.timeout:0}s.", this);
        }

        Vector3 PickScatterPoint(System.Random rng, List<Vector3> taken)
        {
            Vector3 best = m_ZoneA.center;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < m_ScatterTries; i++)
            {
                Vector3 c = m_ZoneA.RandomPoint(rng, m_EdgeInset);
                if (m_ZoneB.Contains(c, m_ExclusionMargin)) continue;

                float nearest = float.PositiveInfinity;
                foreach (Vector3 t in taken) nearest = Mathf.Min(nearest, Vector3.Distance(c, t));
                if (nearest >= m_MinSpacing) return c;
                if (nearest > bestScore) { bestScore = nearest; best = c; }
            }
            Debug.LogWarning($"[Home] '{name}': could not keep {m_MinSpacing:0.00} m between fragments in " +
                             $"zone A; best was {bestScore:0.00} m. Make zone A larger or the spacing smaller.", this);
            return best;
        }

        /// <summary>Server/offline: run the timeout now (debug, tests). A client asks the server.</summary>
        public void AuthorityExpire()
        {
            if (!authority)
            {
                HomeFragment f = AnyFragment();
                if (f != null && f.IsSpawned) f.RequestExpireRpc();
                return;
            }
            if (!armed) return;
            m_ExpireAt = InteractionPhaseClock.now;
            Debug.Log($"[Home] '{name}': timeout forced.", this);
        }

        void AuthorityTimeout()
        {
            double d = deadline;
            if (!InteractionPhaseClock.IsSet(d) || InteractionPhaseClock.now < d) return;

            HomeFragment next = null;
            foreach (Pair p in m_Pairs)
            {
                if (p?.fragment == null || p.fragment.isResolved || !p.fragment.isArmed) continue;
                next = p.fragment;
                break;
            }
            if (next == null) return;

            double now = InteractionPhaseClock.now;
            if (InteractionPhaseClock.IsSet(m_NextVanishAt) && now < m_NextVanishAt) return;
            m_NextVanishAt = now + m_TimeoutStagger;

            Debug.Log($"[Home] '{name}': time is up; '{next.label}' dissolves and its wall comes anyway.", this);
            next.AuthorityVanish();
        }

        // ---- every machine --------------------------------------------------------------------

        void Present()
        {
            bool off = HomeFragment.offline;
            bool isArmed = armed;
            int e = epoch;
            double a = armedAt;

            if (!m_KeyValid || off != m_KeyOffline || e != m_KeyEpoch || !SameStamp(a, m_KeyArmedAt))
            {
                ResetPresentation(isArmed);
                m_KeyValid = true;
                m_KeyOffline = off;
                m_KeyEpoch = e;
                m_KeyArmedAt = a;
            }

            if (!isArmed) return;

            double now = InteractionPhaseClock.now;
            HomeTimings(out float travel, out float vanish);

            bool allResolved = true;
            double lastLanding = double.NegativeInfinity;
            double lastResolved = double.NegativeInfinity;

            for (int i = 0; i < m_Pairs.Length; i++)
            {
                HomeFragment f = m_Pairs[i]?.fragment;
                if (f == null) continue;
                if (!f.isResolved) { allResolved = false; continue; }

                bool placed = f.state == HomeFragmentState.Placed;
                double landing = f.resolvedAt + (placed ? travel : vanish * 0.5f);
                lastLanding = Math.Max(lastLanding, landing);
                lastResolved = Math.Max(lastResolved, f.resolvedAt);

                HomeRevealGroup g = m_Pairs[i].group;
                if (g != null && !m_GroupStarted[i])
                {
                    m_GroupStarted[i] = true;
                    g.Play(landing + m_GroupDelay);
                    Debug.Log($"[Home] '{f.label}' {(placed ? "placed" : "vanished")}: wall '{g.groupName}' comes back.", this);
                }
            }

            foreach (Pair p in m_Pairs) if (p?.group != null) p.group.Tick(now);

            if (!allResolved || fragmentCount == 0) return;

            if (!m_AllResolvedFired)
            {
                m_AllResolvedFired = true;
                m_AllResolvedAt = lastResolved;
                m_AllResolvedCount++;

                // The finished mini-house is left to be seen, then the paper goes.
                double dissolveAt = lastLanding + m_CompleteDelay;
                foreach (Pair p in m_Pairs)
                    if (p?.fragment != null && p.fragment.state == HomeFragmentState.Placed)
                        p.fragment.finalDissolveAt = dissolveAt;

                Debug.Log($"[Home] '{name}': all {fragmentCount} resolved ({placedCount} placed, " +
                          $"{vanishedCount} vanished).", this);
                m_OnAllResolved.Invoke();
            }

            // The ceiling, once the other five walls are back.
            if (m_FinalGroup != null && !m_FinalStarted)
            {
                double last = double.NegativeInfinity;
                foreach (Pair p in m_Pairs) if (p?.group != null) last = Math.Max(last, p.group.endAt);
                if (!double.IsInfinity(last))
                {
                    m_FinalStarted = true;
                    m_FinalGroup.Play(last + m_FinalGroupDelay);
                }
            }
            if (m_FinalGroup != null) m_FinalGroup.Tick(now);

            if (m_CompletedFired) return;
            if (!RoomDone()) return;
            foreach (Pair p in m_Pairs)
                if (p?.fragment != null && now < p.fragment.goneAt) return;

            m_CompletedFired = true;
            m_CompletedAt = now;
            m_CompletedCount++;
            Debug.Log($"[Home] '{name}': complete -- the room is back and the paper is gone.", this);
            m_OnCompleted.Invoke();
        }

        bool RoomDone()
        {
            foreach (Pair p in m_Pairs) if (p?.group != null && !p.group.done) return false;
            return m_FinalGroup == null || m_FinalGroup.done;
        }

        void HomeTimings(out float travel, out float vanish)
        {
            travel = Mathf.Max(m_Timings.placeTravel, 0.01f);
            vanish = Mathf.Max(m_Timings.vanishDuration, 0f);
        }

        static bool SameStamp(double a, double b) => Math.Abs(a - b) < 1e-6;

        void ResetPresentation(bool isArmed)
        {
            PrepareGroups();
            m_GroupStarted = new bool[m_Pairs.Length];
            m_FinalStarted = false;
            m_AllResolvedFired = false;
            m_CompletedFired = false;
            m_AllResolvedAt = InteractionPhaseClock.Unset;
            m_CompletedAt = InteractionPhaseClock.Unset;
            foreach (Pair p in m_Pairs)
                if (p?.fragment != null) p.fragment.finalDissolveAt = InteractionPhaseClock.Unset;

            if (isArmed || m_HideRoomUntilArmed) HideRoom();
        }

        // ---- debug row ------------------------------------------------------------------------

        public string debugId => string.IsNullOrWhiteSpace(m_DebugId) ? "home.assembly" : m_DebugId;
        public string debugLabel => string.IsNullOrWhiteSpace(m_DebugLabel) ? "Home" : m_DebugLabel;
        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;
        public string debugFireLabel => armed ? "Place next" : "Begin";

        public DebugInteractionState DebugState()
        {
            var s = DebugInteractionState.Idle;
            if (!built)
            {
                s.detail = "not built -- run Tools > Home > Build Home Interaction";
                return s;
            }

            if (!armed)
            {
                s.detail = m_BeatRoot != null
                    ? $"dormant -- waiting for '{m_BeatRoot.name}'"
                    : m_ArmOnSessionStart ? "dormant -- waiting for the staff panel's Start" : "dormant -- press Begin";
                return s;
            }

            s.fired = true;
            if (m_CompletedFired)
            {
                s.detail = $"done -- {placedCount} placed, {vanishedCount} vanished; room back";
                return s;
            }
            if (m_AllResolvedFired)
            {
                s.detail = $"all resolved ({placedCount} placed) -- the room is finishing";
                return s;
            }

            var parts = new List<string> { $"{placedCount}/{fragmentCount} placed" };
            foreach (Pair p in m_Pairs)
            {
                HomeFragment f = p?.fragment;
                if (f == null) continue;
                if (f.state == HomeFragmentState.Held)
                {
                    parts.Add($"{f.label} held {RoleOf(f.holder)}");
                    if (InteractionPhaseClock.IsSet(f.zoneEnteredAt))
                        parts.Add($"{f.label} in B {InteractionPhaseClock.Since(f.zoneEnteredAt):0.0}s");
                }
            }
            if (vanishedCount > 0) parts.Add($"{vanishedCount} vanished");

            double d = deadline;
            if (InteractionPhaseClock.IsSet(d))
            {
                float left = Mathf.Max(0f, (float)(d - InteractionPhaseClock.now));
                s.secondsToTimeout = left;
                parts.Add($"timeout {left:0}s");
            }
            s.detail = string.Join(" -- ", parts);
            return s;
        }

        static string RoleOf(ulong clientId)
        {
            if (clientId == NetGrabbable.NoHolder) return "(nobody)";
            GameSessionManager session = GameSessionManager.Instance;
            if (session != null && session.TryGetRoleForClient(clientId, out PlayerRole role))
            {
                if (role == PlayerRole.Player1) return "P1";
                if (role == PlayerRole.Player2) return "P2";
            }
            return HomeFragment.offline ? "here" : $"client {clientId}";
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            if (!built || m_AllResolvedFired) return false;
            // No roles here -- anybody may carry any fragment -- so per-role buttons would lie.
            if (target == DebugFireTarget.Player1 || target == DebugFireTarget.Player2) return false;
            return isActiveAndEnabled;
        }

        /// <summary>
        /// Begin if dormant, else place the next fragment still out. Either way through the
        /// server (a client asks it), so both machines see the same thing.
        /// </summary>
        public void DebugFire(DebugFireTarget target)
        {
            if (!DebugCanFire(target)) return;
            if (!armed)
            {
                AuthorityStart();
                return;
            }

            foreach (Pair p in m_Pairs)
            {
                HomeFragment f = p?.fragment;
                if (f == null || f.isResolved) continue;
                f.RequestPlace();
                Debug.Log($"[Home] '{name}': debug place '{f.label}'.", this);
                return;
            }
        }

        public bool DebugCanReset() => built && armed;

        /// <summary>New epoch: the room hides again and the fragments are scattered afresh.</summary>
        public void DebugReset()
        {
            if (!DebugCanReset()) return;
            AuthorityStart();
        }
    }
}
