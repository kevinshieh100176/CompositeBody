using System;
using Unity.Netcode;
using UnityEngine;
using XRMultiplayer;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Server-authoritative conductor for the whole piece. The experience is a strictly linear
    /// chain of beats (see <see cref="StoryBeat"/>), so exactly one of them is running at any
    /// moment and every client derives what it should be showing from the same replicated index.
    ///
    /// A beat ends in one of three ways, checked in that order of authority:
    /// <list type="bullet">
    /// <item>its gate task is reported complete by <b>both</b> players, via
    /// <see cref="StoryProgressManager"/> -- the interactive onboarding beats;</item>
    /// <item>its authored duration elapses -- the non-interactive S0 beats, which are played to
    /// the audience rather than solved by them;</item>
    /// <item>staff press a button on <see cref="StaffControlPanel"/>.</item>
    /// </list>
    ///
    /// Staff override is not a debug affordance that gets removed later. A two-player gate is a
    /// hang risk in a venue -- one confused or motion-sick player stalls the show for both -- and
    /// rehearsing S0-4 would otherwise mean replaying ten minutes from the top every time.
    ///
    /// Entering a beat <i>clears</i> its gate task rather than assuming it starts clear, so
    /// jumping backwards and restarting between audiences both re-arm the gate instead of
    /// finding it already satisfied and advancing straight back out again.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class ExperienceDirector : NetworkBehaviour
    {
        public static ExperienceDirector Instance { get; private set; }

        [SerializeField, Tooltip("Running order and per-beat exit conditions. Populated with the canonical beat sheet if left empty.")]
        BeatDefinition[] m_Beats = Array.Empty<BeatDefinition>();

        readonly NetworkVariable<int> m_BeatIndex =
            new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Raised on every client, host included, one frame after the beat changes.</summary>
        public event Action<StoryBeat, StoryBeat> onBeatChanged;

        // Server-only: when the current beat was entered, for the authored-duration exit.
        float m_BeatEnteredTime;

        // Last beat this client told its listeners about. Drives change detection; see Update.
        StoryBeat m_RaisedBeat = StoryBeat.None;

        public int beatIndex => m_BeatIndex.Value;
        public int beatCount => m_Beats.Length;
        public StoryBeat currentBeat => BeatAt(m_BeatIndex.Value);

        /// <summary>Seconds the current beat has been running. Server-side only; 0 elsewhere.</summary>
        public float timeInBeat => IsServer && m_BeatIndex.Value >= 0 ? Time.time - m_BeatEnteredTime : 0f;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Utils.LogWarning("[ExperienceDirector] Duplicate instance found, destroying.");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (m_Beats == null || m_Beats.Length == 0)
                m_Beats = DefaultBeats();
        }

        void Reset() => m_Beats = DefaultBeats();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) EnterBeatIndex(0);
        }

        /// <summary>
        /// Beat changes are detected by polling rather than by subscribing to the
        /// NetworkVariable's OnValueChanged. Polling behaves identically for the host writing
        /// the value, a client receiving it, a headset that reconnects mid-show and arrives with
        /// the index already set, and staff jumping backwards -- none of which raise the same
        /// callbacks in the same order. One frame of latency is irrelevant at this timescale,
        /// and a beat that silently never starts on one machine is the expensive failure here.
        /// </summary>
        void Update()
        {
            if (IsServer) TickServer();

            StoryBeat beat = currentBeat;
            if (beat == m_RaisedBeat) return;

            StoryBeat previous = m_RaisedBeat;
            m_RaisedBeat = beat;
            Utils.Log($"[ExperienceDirector] Beat -> {StoryBeats.DisplayName(beat)} (was {StoryBeats.DisplayName(previous)}).");
            onBeatChanged?.Invoke(previous, beat);
        }

        void TickServer()
        {
            int index = m_BeatIndex.Value;
            if (index < 0 || index >= m_Beats.Length) return;

            BeatDefinition def = m_Beats[index];

            if (!string.IsNullOrEmpty(def.gateTaskId) &&
                StoryProgressManager.Instance != null &&
                StoryProgressManager.Instance.IsTaskComplete(def.gateTaskId))
            {
                Utils.Log($"[ExperienceDirector] Gate '{def.gateTaskId}' satisfied by both players.");
                Advance();
                return;
            }

            if (def.autoAdvanceSeconds > 0f && Time.time - m_BeatEnteredTime >= def.autoAdvanceSeconds)
            {
                Utils.Log($"[ExperienceDirector] Beat {StoryBeats.ShortCode(def.beat)} ran its authored {def.autoAdvanceSeconds}s.");
                Advance();
            }
        }

        public StoryBeat BeatAt(int index) =>
            index >= 0 && index < m_Beats.Length ? m_Beats[index].beat : StoryBeat.None;

        /// <summary>Exit conditions authored for a beat, or a blank definition if it isn't in the running order.</summary>
        public BeatDefinition DefinitionFor(StoryBeat beat)
        {
            foreach (BeatDefinition def in m_Beats)
            {
                if (def.beat == beat) return def;
            }
            return default;
        }

        /// <summary>The gate task id for the beat currently running, or empty if it has no gate.</summary>
        public string currentGateTaskId
        {
            get
            {
                int index = m_BeatIndex.Value;
                return index >= 0 && index < m_Beats.Length ? m_Beats[index].gateTaskId : string.Empty;
            }
        }

        /// <summary>
        /// Called by content scripts on whichever client finished its half of the current beat.
        /// Safe to call repeatedly: the second report from the same player is a no-op, and the
        /// beat only advances once both roles have reported.
        /// </summary>
        public void ReportLocalGateComplete()
        {
            string taskId = currentGateTaskId;
            if (string.IsNullOrEmpty(taskId))
            {
                Utils.LogWarning($"[ExperienceDirector] {StoryBeats.DisplayName(currentBeat)} has no gate to report.");
                return;
            }

            if (StoryProgressManager.Instance == null)
            {
                Utils.LogError("[ExperienceDirector] No StoryProgressManager in scene; gate cannot be reported.");
                return;
            }

            StoryProgressManager.Instance.ReportTaskCompleteRpc(taskId);
        }

        #region Staff control (host-local; every entry point is server-only)

        public bool Advance() => EnterBeatIndex(m_BeatIndex.Value + 1);

        public bool GoBack() => EnterBeatIndex(m_BeatIndex.Value - 1);

        public bool JumpTo(StoryBeat beat)
        {
            for (int i = 0; i < m_Beats.Length; i++)
            {
                if (m_Beats[i].beat == beat) return EnterBeatIndex(i);
            }
            return false;
        }

        /// <summary>
        /// Back to the top for the next audience: clears every gate, not just the first beat's,
        /// so an onboarding task solved by the previous pair doesn't come pre-completed.
        /// </summary>
        public bool Restart()
        {
            if (!IsServer) return false;

            if (StoryProgressManager.Instance != null)
                StoryProgressManager.Instance.ResetAllTasks();

            // Restarting a show that never got going leaves index 0 already current, so
            // EnterBeatIndex below returns early without touching either of these. Re-raising
            // the beat locally replays its entry, and the timer has to start again from here.
            m_RaisedBeat = StoryBeat.None;
            m_BeatEnteredTime = Time.time;

            return EnterBeatIndex(0) || m_BeatIndex.Value == 0;
        }

        bool EnterBeatIndex(int index)
        {
            if (!IsServer) return false;
            if (m_Beats.Length == 0) return false;

            index = Mathf.Clamp(index, 0, m_Beats.Length - 1);
            if (index == m_BeatIndex.Value) return false;

            m_BeatIndex.Value = index;
            m_BeatEnteredTime = Time.time;

            // Arm the gate rather than trusting it to be clear: on a jump backwards it is
            // already satisfied, and TickServer would advance straight back out of the beat.
            string gateTaskId = m_Beats[index].gateTaskId;
            if (!string.IsNullOrEmpty(gateTaskId) && StoryProgressManager.Instance != null)
                StoryProgressManager.Instance.ResetTask(gateTaskId);

            return true;
        }

        #endregion

        /// <summary>
        /// The canonical beat sheet. Durations on the non-interactive beats are placeholders
        /// standing in for cues that do not exist yet -- they are how long the beat holds, not a
        /// judgement about how long it should run once there is something in it.
        /// </summary>
        public static BeatDefinition[] DefaultBeats() => new[]
        {
            // Nothing is asked of the players yet; they are getting used to having no body.
            Timed(StoryBeat.O0_Arrival, 20f),

            // The three ghost verbs. Each ends when both players have done it, not one.
            Gated(StoryBeat.O1_Control),
            Gated(StoryBeat.O2_Grasp),
            Gated(StoryBeat.O3_Communion),

            // Props dim, the key turns, black.
            Timed(StoryBeat.O4_OnboardingEnd, 12f),

            // S0 is played to the audience: sound, then a body, then the room, then the couple.
            Timed(StoryBeat.S0_1_SoundBeforeSpace, 35f),
            Timed(StoryBeat.S0_2_TracesFormBody, 45f),
            Timed(StoryBeat.S0_3_EmptyRoomForms, 30f),
            Timed(StoryBeat.S0_4_TheyLeave, 40f),

            // Holds until staff restart. Where S1 would be appended.
            new BeatDefinition { beat = StoryBeat.End }
        };

        static BeatDefinition Gated(StoryBeat beat) => new()
        {
            beat = beat,
            gateTaskId = StoryBeats.GateTaskId(beat)
        };

        static BeatDefinition Timed(StoryBeat beat, float seconds) => new()
        {
            beat = beat,
            autoAdvanceSeconds = seconds
        };
    }

    /// <summary>How one beat ends. A beat may have a gate, a duration, both, or neither.</summary>
    [Serializable]
    public struct BeatDefinition
    {
        public StoryBeat beat;

        [Tooltip("StoryProgressManager task both players must report before the beat ends. Empty for a beat the audience does not act in.")]
        public string gateTaskId;

        [Tooltip("Seconds before the beat ends on its own. 0 waits indefinitely for the gate or for staff.")]
        public float autoAdvanceSeconds;
    }
}
