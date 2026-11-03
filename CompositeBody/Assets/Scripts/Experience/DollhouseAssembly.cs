using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// S2-3｜隱藏的記憶 -- 「在 playspace 中央空中會出現一些碎片跟一個在正中央的
    /// Lowpoly_Wall.Floor_small，玩家要合力完成這個互動。當這個小碎片正確拚上後，LowPolyWall 裡對應的
    /// 大碎片也會從天上飛下來對到正確的位置。當全部碎片都拼完，LowPolyWall 跟 LowPolyWall (small) 都會
    /// dissolve out，然後 Inner_Wall 會 dissolve in」.
    ///
    /// <see cref="DollhousePiece"/> owns the mechanic -- a fragment finding its slot, and the
    /// server agreeing it has. THIS owns the beat around it, which is four things the fragments
    /// have no business knowing: whether they are in the room at all, which fifteen-metre wall
    /// answers which fragment, what happens when the voice-over runs out before the players do,
    /// and the change of scenery at the end.
    ///
    /// ONE COMPONENT, NOT FOUR BESIDE EACH OTHER, and for the reason
    /// <see cref="CompositeFragmentPair"/> spells out: the thing staff need in the panel is ONE
    /// ROW saying where the beat has got to -- dormant, how many pieces are home, which one is
    /// closest and by how far, how long until it finishes itself. Spread over five rows that
    /// reading has to be reassembled by eye from a screen on another machine, which is exactly
    /// when nobody has time to do it.
    ///
    /// THE FRAGMENTS ARE IN THE ROOM FROM THE FIRST FRAME, which is not a choice. They are
    /// networked objects, and Netcode spawns in-scene NetworkObjects once, during
    /// synchronization, from what is ACTIVE in the scene -- fragments under a beat root the show
    /// brings up half an hour in would never be spawned at all, and the server would have nothing
    /// to place. So they live under a root that is always active and this holds them dormant
    /// instead: renderers off, grabs off, until <see cref="m_BeatRoot"/> comes up. That root is
    /// the beat's own object, activated by the Control clip on Main.playable, so the arming is the
    /// show's own cue rather than a second timetable to keep in step with the first.
    ///
    /// THE BIG WALLS ARE AUTHORED WHERE THEY BELONG AND LIFTED AT RUNTIME. Their authored pose IS
    /// the correct one -- that is what makes the scene view show the finished house -- so this
    /// reads each one's local pose on waking, carries it up by <see cref="m_SkyHeight"/>, and
    /// brings it back down to exactly where it was. Nothing is serialized twice, and a corrected
    /// house dropped in later needs no numbers changed: 「正確版屋子之後會補」.
    ///
    /// NOTHING IN HERE CROSSES THE WIRE. Every machine runs the same choreography off the same
    /// replicated facts -- which fragments the server has placed -- so the walls fall and the
    /// scenery changes on both headsets without a single message of its own. Only the two things
    /// that are genuinely decisions are the server's: placing a fragment, and finishing the house
    /// when the voice-over runs out.
    /// </summary>
    [DisallowMultipleComponent]
    public class DollhouseAssembly : MonoBehaviour, IDebugInteraction
    {
        /// <summary>
        /// A fragment and the wall it answers for. A <see cref="fragment"/> of null means a big
        /// piece with no small counterpart -- the floor, which the players are GIVEN rather than
        /// asked for, and which therefore comes down as soon as the beat is up.
        /// </summary>
        [Serializable]
        public class Piece
        {
            [Tooltip("The small fragment the players bring to the base. Empty for a wall whose " +
                     "small counterpart is the base itself.")]
            public DollhousePiece fragment;

            [Tooltip("The big wall that comes down out of the sky once the fragment is home.")]
            public Transform big;
        }

        [Header("The house")]
        [SerializeField, Tooltip("Lowpoly_Wall.Floor_small -- the base in the middle of the " +
                                 "playspace. Not grabbable, and every slot is a child of it.")]
        Transform m_Base;

        [SerializeField, Tooltip("One entry per wall. Built by Tools > Show > Build S2-3 Dollhouse.")]
        Piece[] m_Pieces = new Piece[0];

        [SerializeField, Tooltip("The beat's own object, which the show activates for the " +
                                 "duration of the beat. The fragments are dormant -- invisible " +
                                 "and ungrabbable -- whenever it is off. Leave empty for a test " +
                                 "scene where they should simply be there.")]
        GameObject m_BeatRoot;

        [Header("The big walls coming down")]
        [SerializeField, Min(0f), Tooltip("Metres above where it belongs that each big wall hangs " +
                                          "while it waits. 「一開始就在天上」 -- they are visible up " +
                                          "there from the moment the beat starts, so this is also " +
                                          "how far away the players can see them.")]
        float m_SkyHeight = 12f;

        [SerializeField, Min(0.05f), Tooltip("How long a wall takes to come down.")]
        float m_BigTravel = 1.2f;

        [SerializeField, Min(0f), Tooltip("A beat between the fragment landing and its wall " +
                                          "starting down, so the two read as cause and effect " +
                                          "rather than as one event.")]
        float m_BigDelay = 0.25f;

        [Header("Finishing by itself")]
        [SerializeField, Min(0f), Tooltip("Seconds from the beat starting after which the house " +
                                          "finishes itself. 「這段從開始會播 VO（3分10秒）播完就補完」 " +
                                          "-- so this is the voice-over's length, and it is " +
                                          "counted from the beat coming up, not from the first grab.")]
        float m_AutoCompleteAfter = 190f;

        [SerializeField, Min(0.05f), Tooltip("How long each left-over fragment takes to fly to " +
                                             "its slot when the house finishes itself. Slower than " +
                                             "a snap, because nobody is carrying these -- they " +
                                             "should read as the house completing itself, not as " +
                                             "a glitch.")]
        float m_AutoPlaceTravel = 1.5f;

        [SerializeField, Min(0f), Tooltip("Gap between one left-over fragment leaving and the " +
                                          "next. All four at once reads as a bug.")]
        float m_AutoPlaceStagger = 0.7f;

        [Header("The change of scenery")]
        [SerializeField, Tooltip("Both LowPolyWall groups, big and small. Driven 0 to 1 once the " +
                                 "house is finished.")]
        MemoryDissolve m_LowPolyDissolve;

        [SerializeField, Tooltip("Inner_Wall. Held at 1 -- gone -- for the whole beat, then driven " +
                                 "back to 0 as the low-poly house blows away.")]
        MemoryDissolve m_InnerWallDissolve;

        [SerializeField, Min(0f), Tooltip("A beat to let the finished house be seen before it " +
                                          "starts to go. Completing and vanishing in the same " +
                                          "second reads as the work having been taken away.")]
        float m_DissolveDelay = 1.5f;

        [SerializeField, Min(0.1f), Tooltip("How long the low-poly house takes to blow away.")]
        float m_DissolveOut = 4f;

        [SerializeField, Min(0f), Tooltip("How long after the low-poly house starts to go that " +
                                          "the real wall starts to arrive. Less than the dissolve " +
                                          "out, so the two cross rather than cutting.")]
        float m_DissolveInDelay = 2f;

        [SerializeField, Min(0.1f), Tooltip("How long the real wall takes to arrive.")]
        float m_DissolveIn = 5f;

        [Header("Debug row")]
        [SerializeField, Tooltip("Unique across the show. ASCII -- the panel's font has no CJK " +
                                 "glyphs.")]
        string m_DebugId = "s2_3.dollhouse";

        [SerializeField, Tooltip("Row heading. ASCII, for the same reason.")]
        string m_DebugLabel = "S2-3 dollhouse -- four fragments, then the walls come down";

        // Everything dormancy switches off, cached once. CACHED BECAUSE DORMANCY IS RE-ASSERTED
        // EVERY FRAME and this beat is most of an hour into the show: a GetComponentsInChildren
        // per frame until then is two array allocations a frame for the whole of it, in a piece
        // that budgets its frame time for a headset.
        Renderer[] m_FragmentRenderers = Array.Empty<Renderer>();
        XRGrabInteractable[] m_Grabs = Array.Empty<XRGrabInteractable>();
        Renderer[] m_BigRenderers = Array.Empty<Renderer>();

        // One per entry, so re-asserting grabbability while armed is an array lookup rather than a
        // GetComponents per fragment per frame.
        XRGrabInteractable[] m_FragmentGrab = Array.Empty<XRGrabInteractable>();

        // Where each big wall belongs, in its own parent's space -- so a section that is still
        // being moved carries its walls with it instead of dropping them at a remembered world
        // position.
        Vector3[] m_BigGround = Array.Empty<Vector3>();
        Quaternion[] m_BigTurned = Array.Empty<Quaternion>();

        // Where each fragment was authored, so the beat can be put back and rehearsed again.
        Vector3[] m_FragmentHome = Array.Empty<Vector3>();
        Quaternion[] m_FragmentHomeTurned = Array.Empty<Quaternion>();

        // The flight of each big wall: when it left the sky, or -1 while it is still up there.
        float[] m_FlightStart = Array.Empty<float>();
        bool[] m_Landed = Array.Empty<bool>();

        bool m_Armed;
        bool m_DormancyApplied;
        float m_ArmedAt = -1f;

        bool m_Complete;
        float m_CompletedAt = -1f;

        bool m_AutoCompleting;
        float m_AutoPlaceNext = -1f;

        /// <summary>Whether the fragments are in the room and can be picked up.</summary>
        public bool armed => m_Armed;

        /// <summary>How many fragments are home.</summary>
        public int placedCount
        {
            get
            {
                int n = 0;
                foreach (Piece p in m_Pieces)
                {
                    if (p?.fragment != null && p.fragment.placed) n++;
                }
                return n;
            }
        }

        /// <summary>How many fragments the players are asked for.</summary>
        public int pieceCount
        {
            get
            {
                int n = 0;
                foreach (Piece p in m_Pieces)
                {
                    if (p?.fragment != null) n++;
                }
                return n;
            }
        }

        /// <summary>Whether every fragment is home.</summary>
        public bool complete => m_Complete;

        void Awake()
        {
            Collect();

            // Awake, so a beat that has not started still has a row rather than looking like an
            // interaction nobody built.
            InteractionDebug.Register(this);
        }

        void OnDestroy() => InteractionDebug.Unregister(this);

        void OnEnable()
        {
            // Applied outright rather than left to the first change: the house is authored
            // visible so it can be placed, and the first frame of the show is a long way before
            // this beat.
            m_DormancyApplied = false;
            ApplyDormancy(m_BeatRoot == null || m_BeatRoot.activeInHierarchy);
        }

        void Collect()
        {
            // Never null from here on: everything below walks it, and a cleared array in the
            // inspector should read as "nothing built yet" rather than throw on the first frame.
            m_Pieces ??= new Piece[0];
            int n = m_Pieces.Length;

            m_BigGround = new Vector3[n];
            m_BigTurned = new Quaternion[n];
            m_FragmentHome = new Vector3[n];
            m_FragmentHomeTurned = new Quaternion[n];
            m_FlightStart = new float[n];
            m_Landed = new bool[n];
            m_FragmentGrab = new XRGrabInteractable[n];

            var fragmentRenderers = new System.Collections.Generic.List<Renderer>();
            var grabs = new System.Collections.Generic.List<XRGrabInteractable>();
            var bigRenderers = new System.Collections.Generic.List<Renderer>();

            for (int i = 0; i < n; i++)
            {
                m_FlightStart[i] = -1f;
                Piece p = m_Pieces[i];
                if (p == null) continue;

                if (p.big != null)
                {
                    // THE AUTHORED POSE IS THE CORRECT ONE. Read before anything has had a chance
                    // to lift it, which is why this is Awake and not the first armed frame.
                    p.big.GetLocalPositionAndRotation(out m_BigGround[i], out m_BigTurned[i]);
                    bigRenderers.AddRange(p.big.GetComponentsInChildren<Renderer>(true));
                }

                if (p.fragment != null)
                {
                    p.fragment.transform.GetPositionAndRotation(out m_FragmentHome[i],
                                                                out m_FragmentHomeTurned[i]);
                    fragmentRenderers.AddRange(p.fragment.GetComponentsInChildren<Renderer>(true));

                    XRGrabInteractable[] mine =
                        p.fragment.GetComponentsInChildren<XRGrabInteractable>(true);
                    grabs.AddRange(mine);
                    if (mine.Length > 0) m_FragmentGrab[i] = mine[0];
                }
            }

            if (m_Base != null) fragmentRenderers.AddRange(m_Base.GetComponentsInChildren<Renderer>(true));

            m_FragmentRenderers = fragmentRenderers.ToArray();
            m_Grabs = grabs.ToArray();
            m_BigRenderers = bigRenderers.ToArray();

            IgnoreEachOther();

            if (m_Base == null)
                Debug.LogWarning($"[Dollhouse] '{name}' has no base, so the fragments have " +
                                 "nothing to assemble onto. Re-run Build S2-3 Dollhouse.", this);
            if (pieceCount == 0)
                Debug.LogWarning($"[Dollhouse] '{name}' has no fragments wired, so nothing here " +
                                 "can happen.", this);
        }

        /// <summary>
        /// FOUR WALL SLABS ASSEMBLING INTO A BOX MEET AT THE CORNERS, and a fragment that is home
        /// is a frozen body. Without this the third piece is held out of its own slot by the two
        /// already in place, which from inside a headset is indistinguishable from the snap being
        /// broken -- the fragment is visibly touching and nothing happens.
        ///
        /// Done here rather than with a physics layer because a layer is a project-wide setting
        /// four other sections would have to agree about, and because the pieces are built fresh by
        /// the scene builder every run -- this follows them without anybody remembering to set it.
        /// </summary>
        void IgnoreEachOther()
        {
            var colliders = new System.Collections.Generic.List<Collider>();
            foreach (Piece p in m_Pieces)
            {
                if (p?.fragment == null) continue;
                colliders.AddRange(p.fragment.GetComponentsInChildren<Collider>(true));
            }

            for (int i = 0; i < colliders.Count; i++)
            {
                for (int j = i + 1; j < colliders.Count; j++)
                {
                    if (colliders[i] == null || colliders[j] == null) continue;
                    Physics.IgnoreCollision(colliders[i], colliders[j], true);
                }
            }
        }

        void Update()
        {
            ApplyDormancy(m_BeatRoot == null || m_BeatRoot.activeInHierarchy);
            if (!m_Armed) return;

            WatchForPlacedPieces();
            FlyTheWalls();
            FinishByItself();
            Dissolve();
        }

        #region Dormancy

        /// <summary>
        /// In the room, or not yet. Re-asserted every frame while dormant rather than written
        /// once, because the networked interactables are switched back on whenever ownership
        /// changes -- which happens long after the show has started and would quietly hand the
        /// beat's fragments to the players half an hour early.
        /// </summary>
        void ApplyDormancy(bool armed)
        {
            bool changed = armed != m_Armed || !m_DormancyApplied;
            m_Armed = armed;
            m_DormancyApplied = true;

            if (changed)
            {
                foreach (Renderer r in m_FragmentRenderers)
                {
                    if (r != null && r.forceRenderingOff == armed) r.forceRenderingOff = !armed;
                }
                foreach (Renderer r in m_BigRenderers)
                {
                    if (r != null && r.forceRenderingOff == armed) r.forceRenderingOff = !armed;
                }

                if (armed)
                {
                    Arm();
                }
                else
                {
                    // THE BEAT CAN END IN THE MIDDLE OF THE DISSOLVE, and when it does the room
                    // still has to be right. The beat's window is fixed on Main.playable and the
                    // players are not; if the clip runs out while the low-poly house is still
                    // blowing away, dormancy hides the fragments and the real wall is left stuck
                    // at whatever fraction it had reached -- a half-dissolved room for the rest of
                    // the show. Whatever happened in here, once the beat is over the real room is
                    // there and the low-poly one is not.
                    if (m_ArmedAt >= 0f) FinishScenery();
                    m_ArmedAt = -1f;
                }
            }

            if (armed)
            {
                // Grabbability is the beat's to give and a fragment that is home refuses it. Off a
                // cached array, which is the reason this is in Update at all.
                for (int i = 0; i < m_FragmentGrab.Length; i++)
                {
                    XRGrabInteractable grab = m_FragmentGrab[i];
                    DollhousePiece fragment = m_Pieces[i]?.fragment;
                    if (grab == null || fragment == null) continue;

                    bool wanted = !fragment.placed;
                    if (grab.enabled != wanted) grab.enabled = wanted;
                }
                return;
            }

            // A comparison per grab per frame off a cached array, which is the reason this is in
            // Update at all.
            foreach (XRGrabInteractable grab in m_Grabs)
            {
                if (grab != null && grab.enabled) grab.enabled = false;
            }
        }

        /// <summary>
        /// The beat is up: the walls go to the sky, the real wall goes away, and the clock the
        /// voice-over runs against starts.
        /// </summary>
        void Arm()
        {
            m_ArmedAt = ExperienceClock.now;
            m_AutoPlaceNext = -1f;

            for (int i = 0; i < m_Pieces.Length; i++)
            {
                Transform big = m_Pieces[i]?.big;
                if (big == null) continue;

                m_FlightStart[i] = -1f;
                m_Landed[i] = false;
                SetBigAt(i, 1f);
            }

            // 「然後 Inner_Wall 會 dissolve in」 -- so for the whole of this beat it is NOT there,
            // and the house the players build is the only one in the room. Written on arming
            // rather than authored as gone, so the scene view keeps showing the real wall.
            if (m_InnerWallDissolve != null) m_InnerWallDissolve.progress = 1f;
            if (m_LowPolyDissolve != null) m_LowPolyDissolve.progress = 0f;

            Debug.Log($"[Dollhouse] '{name}': the beat is up. {pieceCount} fragments in the room, " +
                      $"the walls are waiting {m_SkyHeight:0.#} m up, and the house finishes " +
                      $"itself in {m_AutoCompleteAfter:0}s.", this);
        }

        #endregion

        #region The walls coming down

        void WatchForPlacedPieces()
        {
            for (int i = 0; i < m_Pieces.Length; i++)
            {
                Piece p = m_Pieces[i];
                if (p?.big == null || m_FlightStart[i] >= 0f || m_Landed[i]) continue;

                // A wall with no fragment is the floor, which the players are given: it comes
                // down as soon as the beat is up, because its counterpart is already in place.
                bool due = p.fragment == null || p.fragment.placed;
                if (!due) continue;

                m_FlightStart[i] = ExperienceClock.now + m_BigDelay;
            }

            if (m_Complete || pieceCount == 0) return;
            if (placedCount < pieceCount) return;

            m_Complete = true;
            m_CompletedAt = ExperienceClock.now;
            Debug.Log($"[Dollhouse] '{name}': all {pieceCount} fragments are home.", this);
        }

        void FlyTheWalls()
        {
            for (int i = 0; i < m_Pieces.Length; i++)
            {
                if (m_Landed[i] || m_FlightStart[i] < 0f) continue;
                if (ExperienceClock.now < m_FlightStart[i]) continue;

                float t = Mathf.Clamp01((ExperienceClock.now - m_FlightStart[i]) / m_BigTravel);

                // Eased in and out: a wall that starts at speed has already been falling, and one
                // that arrives at speed has gone through the floor.
                SetBigAt(i, 1f - t * t * (3f - 2f * t));

                if (t < 1f) continue;

                m_Landed[i] = true;
                Debug.Log($"[Dollhouse] '{name}': '{m_Pieces[i].big.name}' is down.", this);
            }
        }

        /// <summary>
        /// Puts one big wall somewhere between where it belongs (0) and the sky (1). In its
        /// parent's space, so the offset is the parent's own up rather than the world's -- a
        /// section authored at an angle drops its walls along itself.
        /// </summary>
        void SetBigAt(int i, float height)
        {
            Transform big = m_Pieces[i]?.big;
            if (big == null) return;

            // Converted through the parent so m_SkyHeight stays honest metres whatever the
            // section is scaled to.
            Vector3 up = big.parent != null
                ? big.parent.InverseTransformVector(Vector3.up * m_SkyHeight)
                : Vector3.up * m_SkyHeight;

            big.SetLocalPositionAndRotation(m_BigGround[i] + up * height, m_BigTurned[i]);
        }

        #endregion

        #region Finishing by itself

        /// <summary>
        /// 「播完就補完」. The server, and only the server: placing a fragment is a decision, and two
        /// machines deciding it independently would place every piece twice.
        ///
        /// STAGGERED, not all at once, and each one FLIES rather than appearing: the players are
        /// still standing there watching, and four walls blinking into place is indistinguishable
        /// from the beat having crashed.
        /// </summary>
        void FinishByItself()
        {
            if (m_Complete || m_ArmedAt < 0f) return;
            if (ExperienceClock.now < m_ArmedAt + m_AutoCompleteAfter) return;

            DollhousePiece next = NextUnplaced();
            if (next == null) return;

            // The server decides, except on a desk where there is no server and this machine is
            // the only one there is -- the same reading CompositeFragmentPair.IDrive uses.
            if (next.IsSpawned && !next.IsServer) return;

            if (!m_AutoCompleting)
            {
                m_AutoCompleting = true;
                Debug.Log($"[Dollhouse] '{name}': the voice-over is over and " +
                          $"{pieceCount - placedCount} fragments are still out. Finishing the " +
                          "house.", this);
            }

            if (ExperienceClock.now < m_AutoPlaceNext) return;
            m_AutoPlaceNext = ExperienceClock.now + m_AutoPlaceStagger;

            next.RequestPlace(m_AutoPlaceTravel);
        }

        /// <summary>The next fragment still out in the room, or null once they are all home.</summary>
        DollhousePiece NextUnplaced()
        {
            foreach (Piece p in m_Pieces)
            {
                if (p?.fragment == null || p.fragment.placed) continue;
                return p.fragment;
            }
            return null;
        }

        #endregion

        #region The change of scenery

        /// <summary>
        /// 「LowPolyWall 跟 LowPolyWall (small) 都會 dissolve out，然後 Inner_Wall 會 dissolve in」.
        ///
        /// Run on every machine off the replicated flags rather than driven by the server,
        /// because it is presentation: both headsets are on the same clock and reach the same
        /// numbers from the same facts, so there is nothing here worth a message.
        /// </summary>
        void Dissolve()
        {
            if (!m_Complete) return;

            float since = ExperienceClock.now - m_CompletedAt - m_DissolveDelay;
            if (since < 0f) return;

            if (m_LowPolyDissolve != null)
                m_LowPolyDissolve.progress = Mathf.Clamp01(since / m_DissolveOut);

            if (m_InnerWallDissolve == null) return;

            float arriving = since - m_DissolveInDelay;
            if (arriving < 0f) return;

            m_InnerWallDissolve.progress = 1f - Mathf.Clamp01(arriving / m_DissolveIn);
        }

        /// <summary>
        /// The end state, asserted outright: the real room is there, the low-poly one is gone.
        /// Called when the beat's clip runs out, however far the dissolve had got.
        /// </summary>
        void FinishScenery()
        {
            if (m_LowPolyDissolve != null) m_LowPolyDissolve.progress = 1f;
            if (m_InnerWallDissolve != null) m_InnerWallDissolve.progress = 0f;

            if (!m_Complete)
                Debug.LogWarning($"[Dollhouse] '{name}': the beat ended with " +
                                 $"{pieceCount - placedCount} fragment(s) still out. The room has " +
                                 "been handed over anyway, but the auto-complete never had time " +
                                 "to run -- Auto Complete After is longer than the beat's clip on " +
                                 "Main.playable.", this);
        }

        #endregion

        #region Debug panel

        public string debugId => string.IsNullOrWhiteSpace(m_DebugId) ? "dollhouse" : m_DebugId;

        public string debugLabel =>
            string.IsNullOrWhiteSpace(m_DebugLabel) ? "Dollhouse" : m_DebugLabel;

        // The fragments' own state is replicated, so both machines read the same thing here --
        // but there is no StoryProgressManager task behind it, which is what Gate means. This is
        // the SEQUENCE around the fragments, same as CompositeFragmentPair's row.
        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        public string debugFireLabel => "Place next";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;

            if (m_Base == null || pieceCount == 0)
            {
                state.detail = "not built -- run Tools > Show > Build S2-3 Dollhouse";
                return state;
            }

            state.fired = placedCount > 0;

            if (!m_Armed)
            {
                state.detail = m_BeatRoot != null
                    ? $"dormant -- '{m_BeatRoot.name}' is not up yet"
                    : "dormant";
                return state;
            }

            if (m_Complete)
            {
                float since = ExperienceClock.now - m_CompletedAt - m_DissolveDelay;

                if (since < 0f)
                    state.detail = $"all {pieceCount} placed; the house goes in " +
                                   $"{-since:0.0}s";
                else if (m_InnerWallDissolve != null && m_InnerWallDissolve.whole)
                    state.detail = "done -- the real wall is in";
                else
                    state.detail = "dissolving -- low-poly " +
                                   $"{(m_LowPolyDissolve != null ? m_LowPolyDissolve.progress : 0f) * 100f:0}% gone, " +
                                   $"wall {(1f - (m_InnerWallDissolve != null ? m_InnerWallDissolve.progress : 1f)) * 100f:0}% in";

                return state;
            }

            // The countdown staff actually need: how long they have before the house finishes
            // itself, which is the difference between 「再等一下」 and 「可以過去幫他們」.
            if (m_ArmedAt >= 0f)
                state.secondsToTimeout =
                    Mathf.Max(m_ArmedAt + m_AutoCompleteAfter - ExperienceClock.now, 0f);

            // WHICH piece and HOW FAR, not just how many. A row reading 2/4 is the one thing here
            // that does not say what to do next; 「Left 還差 18 公分」 is something to say out loud.
            DollhousePiece nearest = null;
            float best = float.PositiveInfinity;
            foreach (Piece p in m_Pieces)
            {
                if (p?.fragment == null || p.fragment.placed) continue;
                float d = p.fragment.distanceToSlot;
                if (d >= best) continue;
                best = d;
                nearest = p.fragment;
            }

            state.detail = $"{placedCount}/{pieceCount} placed";
            if (nearest != null)
                state.detail += $" -- nearest '{nearest.name}' {best:0.00} m out, " +
                                $"snaps at {nearest.snapRadius:0.00}" +
                                (nearest.wasEverHeld ? "" : " (never touched)");

            return state;
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            if (m_Base == null || pieceCount == 0 || m_Complete) return false;

            // There are no roles in this beat -- anybody may carry any fragment -- so the per-role
            // buttons would be a lie. Local is the desk, Both is the real path.
            if (target == DebugFireTarget.Player1 || target == DebugFireTarget.Player2) return false;
            if (target == DebugFireTarget.Local) return isActiveAndEnabled;

            return InteractionDebugRelay.canForce;
        }

        /// <summary>
        /// THE TWO TIERS ARE NOT THE SAME RESCUE. Local places the next fragment on this machine
        /// and nowhere else: the other headset still sees it floating, and the server still has
        /// not counted it, so the walls do not come down and the beat does not end. Useful on a
        /// desk, misleading in the venue.
        ///
        /// Both asks the server to place it, which is the path a real snap ends in -- every
        /// machine sees it land, the wall comes down on both, and the count moves. Pressed four
        /// times it finishes the house without waiting out the voice-over.
        /// </summary>
        public void DebugFire(DebugFireTarget target)
        {
            if (!DebugCanFire(target)) return;

            if (!m_Armed) ApplyDormancy(true);

            DollhousePiece next = NextUnplaced();
            if (next == null) return;

            if (target == DebugFireTarget.Local)
            {
                next.PlaceLocally();
                Debug.Log($"[Dollhouse] '{name}': '{next.name}' placed on this machine only.", this);
                return;
            }

            next.RequestPlace(m_AutoPlaceTravel);
            Debug.Log($"[Dollhouse] '{name}': asked the server to place '{next.name}'.", this);
        }

        /// <summary>
        /// Puts the whole beat back so it can be rehearsed again -- which, unlike
        /// <see cref="CompositeFragmentPair.DebugReset"/>, it genuinely can: nothing here is
        /// reparented and nothing is welded, so every fragment goes back to where it was authored
        /// and the walls go back up.
        /// </summary>
        public bool DebugCanReset() => placedCount > 0 || m_Complete;

        public void DebugReset()
        {
            for (int i = 0; i < m_Pieces.Length; i++)
            {
                DollhousePiece fragment = m_Pieces[i]?.fragment;
                if (fragment == null) continue;

                if (fragment.IsSpawned && !fragment.IsServer) continue;
                fragment.ServerUnplace(m_FragmentHome[i], m_FragmentHomeTurned[i]);

                m_FlightStart[i] = -1f;
                m_Landed[i] = false;
                SetBigAt(i, 1f);
            }

            // The floor has no fragment, so its wall has to be sent back up by hand.
            for (int i = 0; i < m_Pieces.Length; i++)
            {
                if (m_Pieces[i]?.fragment != null) continue;
                m_FlightStart[i] = -1f;
                m_Landed[i] = false;
                SetBigAt(i, 1f);
            }

            m_Complete = false;
            m_CompletedAt = -1f;
            m_AutoCompleting = false;
            m_AutoPlaceNext = -1f;
            m_ArmedAt = m_Armed ? ExperienceClock.now : -1f;

            if (m_LowPolyDissolve != null) m_LowPolyDissolve.progress = 0f;
            if (m_InnerWallDissolve != null) m_InnerWallDissolve.progress = m_Armed ? 1f : 0f;

            Debug.Log($"[Dollhouse] '{name}': put back. The fragments are out again and the walls " +
                      "are back in the sky.", this);
        }

        #endregion
    }
}
