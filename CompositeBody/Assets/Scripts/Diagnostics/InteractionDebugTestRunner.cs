using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Drives the interaction debug surface in play mode: does the list populate itself, does a
    /// row report the right state, and do the three fire tiers actually do what they claim.
    ///
    /// THE TIER THAT MATTERS IS "BOTH". It is the only one that can do something no real
    /// interaction can -- write a flag for a player who never reported -- and it is the whole
    /// reason one person can walk a two-player gate alone. If it silently failed, every gate
    /// would look stuck and the panel would be worse than nothing, because it would look like
    /// the gate was the bug.
    ///
    /// Hosts on its own, so the client half of the relay (the RPC hop to the other machine) is
    /// NOT covered here -- that needs a second machine. What is covered is everything that runs
    /// on the host: discovery, state, the local report path, the server-side write, the reset,
    /// and the mirror the host writes for its own local-only rows.
    /// </summary>
    public class InteractionDebugTestRunner : MonoBehaviour
    {
        [SerializeField, Min(1f)] float m_StepTimeout = 30f;

        readonly List<string> m_Failures = new();
        int m_Checks;

        IEnumerator Start()
        {
            Log("Starting.");

            yield return RunTest();

            Log($"{m_Checks} checks, {m_Failures.Count} failed.");
            foreach (string failure in m_Failures) Debug.LogError($"[DebugPanelTest]   FAILED: {failure}");

            if (m_Failures.Count == 0) Debug.Log("[DebugPanelTest] RESULT: PASS");
            else Debug.LogError("[DebugPanelTest] RESULT: FAIL");

            yield return null;

#if UNITY_EDITOR
            // The status code is the result of the batch run. In an editor somebody is sitting in
            // front of, that same exit would close it out from under them: leave play mode instead.
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(m_Failures.Count == 0 ? 0 : 1);
            else UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(m_Failures.Count == 0 ? 0 : 1);
#endif
        }

        IEnumerator RunTest()
        {
            // --- the panel is in the scene and builds itself ---
            var panel = FindFirstObjectByType<InteractionDebugPanel>(FindObjectsInactive.Include);
            if (!Check(panel != null, "the scene has an InteractionDebugPanel")) yield break;

            Check(!panel.visible, "it starts hidden");
            Check(InteractionDebug.fireEnabled, "firing is enabled in the editor");

            panel.visible = true;
            yield return null;

            // WHAT THE LIST HOLDS BEFORE ANYTHING IS HOSTED, which is when somebody opening the
            // panel at launch actually looks at it. A row that only appears once the experience
            // scene has loaded is a row that is missing exactly when it is first wanted.
            Log("--- registered at startup, before hosting ---");
            foreach (IDebugInteraction entry in InteractionDebug.entries)
                Log($"  {entry.debugScope,-12} {entry.debugId}");

            Check(FindById("o0.awakening") != null,
                  "the O-0 touch row is there from the start, before the show is hosted");

            // --- a host, a role, a player ---
            yield return WaitUntil(
                () => GameSessionManager.Instance != null && StoryProgressManager.Instance != null,
                "the session managers");

            if (NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsListening &&
                                                    !NetworkManager.Singleton.StartHost()))
            {
                Fail("could not start a host");
                yield break;
            }

            yield return WaitUntil(() => InteractionDebugRelay.Instance != null &&
                                         InteractionDebugRelay.Instance.IsSpawned,
                                  "the debug relay to spawn");
            Check(InteractionDebugRelay.canForce, "the host may force flags");

            yield return WaitUntil(() => AvatarFormation.local != null, "the local avatar");
            yield return new WaitForSeconds(1f);

            // --- the list populated itself ---
            Check(panel.rowCount > 0, $"the panel drew rows ({panel.rowCount})");

            IDebugInteraction player = FindByScope(DebugInteractionScope.Player);
            if (Check(player != null, "a per-player row registered itself"))
            {
                DebugInteractionState state = player.DebugState();
                Check(!state.fired, "the player row reads not-yet-touched");
                Check(player.DebugCanFire(DebugFireTarget.Local),
                      "the local player's row can be fired on this machine");
            }

            // The calibrator ships switched off, and the row still has to be there saying so --
            // an interaction missing from the list reads as one that was never built.
            IDebugInteraction local = FindById("calibration");
            if (Check(local != null, "the calibration row registered itself even though disabled"))
            {
                Check(local.DebugState().detail.Contains("disabled"),
                      $"it says why it will not fire ('{local.DebugState().detail}')");
                Check(!local.DebugCanFire(DebugFireTarget.Local),
                      "its button is dead while the component is off");
            }

            // --- the gates were discovered from the beat sheet, not from a list in the panel ---
            string gateId = StoryBeats.GateTaskId(StoryBeat.O1_Control);
            var gate = FindById(gateId) as TaskGateInteraction;
            if (!Check(gate != null, $"the gate row '{gateId}' was discovered")) yield break;

            Check(!gate.DebugState().fired, "the gate starts unsatisfied");

            // --- tier 1: the local report, through the real owner-attributed RPC ---
            gate.DebugFire(DebugFireTarget.Local);
            yield return WaitUntil(() => gate.DebugState().player1, "the local report to land");

            DebugInteractionState afterLocal = gate.DebugState();
            Check(afterLocal.player1, "'report mine' set this machine's role flag");
            Check(!afterLocal.fired, "one half does not satisfy the gate");

            // --- tier 2: the server-side write, which is what lets one person walk the gate ---
            gate.DebugFire(DebugFireTarget.Both);
            yield return WaitUntil(() => gate.DebugState().fired, "the forced gate to complete");

            DebugInteractionState afterBoth = gate.DebugState();
            Check(afterBoth.fired && afterBoth.player1 && afterBoth.player2,
                  "'both' set both halves and satisfied the gate");
            Check(afterBoth.detail != null && afterBoth.detail.Contains("forced"),
                  $"the row says the flags were forced (detail: '{afterBoth.detail}')");

            // --- and it can be put back, so a beat can be rehearsed twice ---
            gate.DebugReset();
            yield return WaitUntil(() => !gate.DebugState().fired, "the gate to clear");

            DebugInteractionState afterReset = gate.DebugState();
            Check(!afterReset.player1 && !afterReset.player2, "reset cleared both halves");

            // --- the host mirrors its own local-only rows, so one screen can show both machines ---
            yield return WaitUntil(() => MirroredIds().Count > 0, "the relay to mirror a local row");
            List<string> mirrored = MirroredIds();
            Check(mirrored.Contains("calibration"),
                  $"the relay carries this machine's local rows ({string.Join(", ", mirrored)})");

            // --- the timeout countdown, which is the one number nobody can get at otherwise ---
            if (!GameSessionManager.Instance.StartExperience())
            {
                Fail("could not start the experience, so no timeout clock runs");
                yield break;
            }

            yield return WaitUntil(() => TimeoutOf(DebugInteractionScope.Player) > 0f,
                                   "the formation timeout to start counting");

            float first = TimeoutOf(DebugInteractionScope.Player);
            yield return new WaitForSeconds(2f);
            float later = TimeoutOf(DebugInteractionScope.Player);

            Check(later < first, $"the timeout counts down ({first:0.0}s -> {later:0.0}s)");

            // The gate backed by that timeout borrows it, which is the only reason a task -- a
            // thing with no clock of its own -- can show one.
            // The grab stations declare the O-1 gate and carry their own fallback, so a row that
            // is waiting for a player shows how long it will wait.
            // BY PREFIX, because the id names the object as well as the player now: Player 1
            // has a lamp and a jacket, and which one is first in the list is not this test's
            // business. What it is checking is that O-1's grab rows exist at all.
            IDebugInteraction grab = FindByPrefix("o1.grab.Player1.");
            if (Check(grab != null, "the O-1 grab row is present"))
            {
                Check(FindById("beat.O1_Control") != null,
                      "and the gate it reports to has a row of its own");
            }

            // DOES THE LIST FIT? A row that is registered, built and correct is still invisible
            // if it is below the bottom of the window, and the panel clips rather than growing.
            // This is the failure that looks exactly like "the interaction is not in the list".
            yield return null;
            Check(panel.fitsOnScreen,
                  "the panel is inside the window rather than hanging off the bottom of it");

            // A list taller than the window is fine -- it scrolls -- but every registered row
            // has to have been BUILT, or it cannot be scrolled to either.
            Check(panel.rowCount >= InteractionDebug.entries.Count,
                  $"every registered interaction has a row ({InteractionDebug.entries.Count} " +
                  $"registered, {panel.rowCount} rows incl. headings; list {panel.contentHeight:0}px " +
                  $"in a {panel.viewportHeight:0}px window)");

            // --- what the panel actually renders ---
            // READ BACK OFF THE UI, not recomposed here. Every check above went through the
            // interfaces; none of them would notice a row that drew an empty string, a label
            // that never got refreshed, or a layout that built no Text at all.
            yield return null;
            DumpRenderedRows(panel);
        }

        void DumpRenderedRows(InteractionDebugPanel panel)
        {
            var texts = panel.GetComponentsInChildren<UnityEngine.UI.Text>(true);

            int filled = 0;
            Log("--- as drawn on screen ---");
            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text.text)) continue;
                filled++;

                // Buttons are labelled once and never change; the rows are the interesting part.
                string prefix = text.transform.parent != null &&
                                text.transform.parent.name.StartsWith("Button_") ? "    btn " : "  ";
                Log($"{prefix}{text.text.Replace("\n", "\n      ")}");
            }

            Check(filled >= 6, $"the panel drew {filled} non-empty text elements");
        }

        static IDebugInteraction FindByScope(DebugInteractionScope scope)
        {
            foreach (IDebugInteraction entry in InteractionDebug.entries)
            {
                if (entry.debugScope == scope) return entry;
            }
            return null;
        }

        static IDebugInteraction FindById(string id)
        {
            foreach (IDebugInteraction entry in InteractionDebug.entries)
            {
                if (entry.debugId == id) return entry;
            }
            return null;
        }

        /// <summary>
        /// The first row whose id starts with this, for the families that name an object as
        /// well as a player -- 'o1.grab.Player1.Lamp', 'o1.box.Player1.Jacket'. A test that
        /// wants "one of this player's grab rows" should not have to know which objects the
        /// running order is showing this week.
        /// </summary>
        static IDebugInteraction FindByPrefix(string prefix)
        {
            foreach (IDebugInteraction entry in InteractionDebug.entries)
            {
                if (entry.debugId != null && entry.debugId.StartsWith(prefix)) return entry;
            }
            return null;
        }

        static float TimeoutOf(DebugInteractionScope scope)
        {
            IDebugInteraction entry = FindByScope(scope);
            return entry != null ? entry.DebugState().secondsToTimeout : -1f;
        }

        static List<string> MirroredIds()
        {
            var ids = new List<string>();
            InteractionDebugRelay relay = InteractionDebugRelay.Instance;
            if (relay == null || !relay.IsSpawned) return ids;

            foreach (var machine in relay.machines) ids.Add(machine.id.ToString());
            return ids;
        }

        IEnumerator WaitUntil(System.Func<bool> predicate, string what, float timeout = -1f)
        {
            float budget = timeout > 0f ? timeout : m_StepTimeout;
            float started = Time.realtimeSinceStartup;

            while (!predicate())
            {
                if (Time.realtimeSinceStartup - started > budget)
                {
                    Fail($"timed out after {budget:0.#}s waiting for {what}");
                    yield break;
                }
                yield return null;
            }
        }

        bool Check(bool condition, string what)
        {
            m_Checks++;
            if (condition) Log($"  OK: {what}");
            else Fail(what);
            return condition;
        }

        void Fail(string what) => m_Failures.Add(what);

        static void Log(string message) => Debug.Log($"[DebugPanelTest] {message}");
    }
}
