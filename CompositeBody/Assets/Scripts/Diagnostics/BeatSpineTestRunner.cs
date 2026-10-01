using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompositeBody.Multiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Drives <see cref="ExperienceDirector"/> through the whole running order in play mode and
    /// checks it behaves, then exits the editor with a pass/fail code.
    ///
    /// This exists because the scene builder can only prove the spine was <i>authored</i>
    /// correctly. Whether the beat index actually moves, whether a gated beat really refuses to
    /// advance, and whether beat content switches on and off with its beat are runtime
    /// behaviours, and all three fail silently -- a beat that never starts and a beat with no
    /// content in it look exactly the same from inside a headset.
    ///
    /// Runs as a host on its own, so it covers everything except the one thing that genuinely
    /// needs a second machine: a gate opening once <b>both</b> players have reported. What it
    /// can and does check is the dangerous half of that -- that one player reporting alone does
    /// not open it.
    /// </summary>
    public class BeatSpineTestRunner : MonoBehaviour
    {
        [SerializeField] string m_ExperienceSceneName = "CompositeBody_Experience";
        [SerializeField] float m_StepTimeout = 20f;

        readonly List<string> m_Failures = new();
        int m_Checks;

        IEnumerator Start()
        {
            Log("Starting.");

            yield return RunTest();

            Log($"{m_Checks} checks, {m_Failures.Count} failed.");
            foreach (string failure in m_Failures) Debug.LogError($"[SpineTest]   FAILED: {failure}");

            if (m_Failures.Count == 0) Debug.Log("[SpineTest] RESULT: PASS");
            else Debug.LogError("[SpineTest] RESULT: FAIL");

            yield return null;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(m_Failures.Count == 0 ? 0 : 1);
#else
            Application.Quit(m_Failures.Count == 0 ? 0 : 1);
#endif
        }

        IEnumerator RunTest()
        {
            // --- bring up a host ---
            if (NetworkManager.Singleton == null)
            {
                Fail("No NetworkManager in the scene.");
                yield break;
            }

            // Let the scene's own managers finish starting before hosting. In the venue the host
            // is started from a button press long after this, and a test that races them is
            // testing an order the piece never actually runs in.
            yield return WaitUntil(
                () => GameSessionManager.Instance != null && StoryProgressManager.Instance != null,
                "session managers to come up");
            yield return null;

            if (!NetworkManager.Singleton.StartHost())
            {
                Fail("StartHost() returned false.");
                yield break;
            }

            yield return WaitUntil(() => NetworkManager.Singleton.IsServer && NetworkManager.Singleton.IsListening,
                "host to start");
            if (!Check(NetworkManager.Singleton.IsServer, "host is server")) yield break;

            if (!Check(GameSessionManager.Instance != null, "GameSessionManager present")) yield break;
            if (!Check(StoryProgressManager.Instance != null, "StoryProgressManager present")) yield break;

            // The host is seeded as Player1, which is what makes the single-player gate check
            // below meaningful: there is a real role behind the report.
            yield return WaitUntil(() => GameSessionManager.Instance.TryGetLocalRole(out _), "local role to resolve");
            GameSessionManager.Instance.TryGetLocalRole(out PlayerRole localRole);
            Check(localRole == PlayerRole.Player1, $"host holds Player1 (got {localRole})");

            // --- load the experience scene through NGO, as staff Start would ---
            var status = NetworkManager.Singleton.SceneManager.LoadScene(m_ExperienceSceneName, LoadSceneMode.Additive);
            if (!Check(status == SceneEventProgressStatus.Started, $"experience scene load started ({status})")) yield break;

            yield return WaitUntil(() => ExperienceDirector.Instance != null && ExperienceDirector.Instance.IsSpawned,
                "director to spawn");
            var director = ExperienceDirector.Instance;
            if (!Check(director != null && director.IsSpawned, "director spawned")) yield break;

            Check(director.beatCount == StoryBeats.Ordered.Length,
                $"director has {StoryBeats.Ordered.Length} beats (got {director.beatCount})");

            // --- the show starts at the top ---
            yield return WaitUntil(() => director.currentBeat == StoryBeat.O0_Arrival, "O-0 to start");
            Check(director.currentBeat == StoryBeat.O0_Arrival,
                $"opens on O-0 (got {StoryBeats.ShortCode(director.currentBeat)})");

            // --- beat content follows the beat ---
            yield return BeatContentCheck(StoryBeat.O0_Arrival, true);
            yield return BeatContentCheck(StoryBeat.S0_4_TheyLeave, false);

            // --- staff advance ---
            director.Advance();
            yield return WaitUntil(() => director.currentBeat == StoryBeat.O1_Control, "advance to O-1");
            Check(director.currentBeat == StoryBeat.O1_Control,
                $"Advance() moves to O-1 (got {StoryBeats.ShortCode(director.currentBeat)})");

            // --- a gated beat must not advance on its own ---
            Check(!string.IsNullOrEmpty(director.currentGateTaskId), "O-1 has a gate task id");
            yield return new WaitForSeconds(3f);
            Check(director.currentBeat == StoryBeat.O1_Control,
                "gated O-1 does not advance on a timer");

            // --- one player reporting alone must NOT open the gate ---
            string gateId = director.currentGateTaskId;
            director.ReportLocalGateComplete();
            yield return new WaitForSeconds(1.5f);
            Check(director.currentBeat == StoryBeat.O1_Control,
                "one player's report alone does not open the gate");
            Check(!StoryProgressManager.Instance.IsTaskComplete(gateId),
                "gate task is not complete with one report");
            Check(HasFlag(gateId, PlayerRole.Player1),
                "the one report was recorded against Player1");

            // --- jump and step back ---
            director.JumpTo(StoryBeat.S0_3_EmptyRoomForms);
            yield return WaitUntil(() => director.currentBeat == StoryBeat.S0_3_EmptyRoomForms, "jump to S0-3");
            Check(director.currentBeat == StoryBeat.S0_3_EmptyRoomForms,
                $"JumpTo(S0-3) works (got {StoryBeats.ShortCode(director.currentBeat)})");

            director.GoBack();
            yield return WaitUntil(() => director.currentBeat == StoryBeat.S0_2_TracesFormBody, "step back to S0-2");
            Check(director.currentBeat == StoryBeat.S0_2_TracesFormBody,
                $"GoBack() works (got {StoryBeats.ShortCode(director.currentBeat)})");

            // --- restart clears the gates the previous run left behind ---
            director.Restart();
            yield return WaitUntil(() => director.currentBeat == StoryBeat.O0_Arrival, "restart to O-0");
            Check(director.currentBeat == StoryBeat.O0_Arrival, "Restart() returns to O-0");
            Check(!HasFlag(gateId, PlayerRole.Player1),
                "Restart() cleared the gate report from the previous run");

            // --- the timer path, on the shortest timed beat ---
            director.JumpTo(StoryBeat.O4_OnboardingEnd);
            yield return WaitUntil(() => director.currentBeat == StoryBeat.O4_OnboardingEnd, "jump to O-4");

            BeatDefinition o4 = director.DefinitionFor(StoryBeat.O4_OnboardingEnd);
            Check(o4.autoAdvanceSeconds > 0f, $"O-4 has a duration ({o4.autoAdvanceSeconds}s)");

            float budget = o4.autoAdvanceSeconds + 5f;
            yield return WaitUntil(() => director.currentBeat != StoryBeat.O4_OnboardingEnd,
                $"O-4 to run its {o4.autoAdvanceSeconds}s", budget);
            Check(director.currentBeat == StoryBeat.S0_1_SoundBeforeSpace,
                $"timed O-4 advances to S0-1 (got {StoryBeats.ShortCode(director.currentBeat)})");

            // --- the end holds instead of running off the list ---
            director.JumpTo(StoryBeat.End);
            yield return WaitUntil(() => director.currentBeat == StoryBeat.End, "jump to End");
            director.Advance();
            yield return new WaitForSeconds(1f);
            Check(director.currentBeat == StoryBeat.End, "End holds rather than advancing past the list");
        }

        /// <summary>Checks a beat's content root is active exactly when that beat is running.</summary>
        IEnumerator BeatContentCheck(StoryBeat beat, bool expectedActive)
        {
            yield return null; // BeatController binds on a coroutine, so give it a frame

            BeatController found = null;
            foreach (var controller in FindObjectsByType<BeatController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (controller.beat == beat) { found = controller; break; }
            }

            if (!Check(found != null, $"{StoryBeats.ShortCode(beat)} has a controller")) yield break;

            Check(found.isActiveBeat == expectedActive,
                $"{StoryBeats.ShortCode(beat)} content {(expectedActive ? "is live" : "is dormant")} " +
                $"(isActiveBeat={found.isActiveBeat})");
        }

        bool HasFlag(string taskId, PlayerRole role)
        {
            foreach (var task in StoryProgressManager.Instance.tasks)
            {
                if (!task.taskId.ToString().Equals(taskId)) continue;
                return role == PlayerRole.Player1 ? task.player1Done : task.player2Done;
            }
            return false;
        }

        IEnumerator WaitUntil(System.Func<bool> condition, string what, float timeout = -1f)
        {
            float limit = timeout > 0f ? timeout : m_StepTimeout;
            float started = Time.realtimeSinceStartup;

            while (!condition())
            {
                if (Time.realtimeSinceStartup - started > limit)
                {
                    Fail($"timed out after {limit:0.#}s waiting for {what}");
                    yield break;
                }
                yield return null;
            }
        }

        bool Check(bool condition, string description)
        {
            m_Checks++;
            if (condition) Log($"ok   {description}");
            else Fail(description);
            return condition;
        }

        void Fail(string description) => m_Failures.Add(description);

        static void Log(string message) => Debug.Log($"[SpineTest] {message}");
    }
}
