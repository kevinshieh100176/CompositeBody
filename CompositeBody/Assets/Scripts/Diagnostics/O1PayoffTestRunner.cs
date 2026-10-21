using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Runs the O-1 payoff in play mode: the show waits at its hold, a grab clears it, and then
    /// the orbit lights, the footprints and S0-2 arrive in order on the timeline.
    ///
    /// THE QUESTION IS WHETHER THE TIMELINE MOVES ANYTHING. A built asset proves only that the
    /// clips exist: a binding that resolved to the wrong object, an activation clip that reverts
    /// at the end of a beat, an animation curve whose property path no longer matches the
    /// hierarchy -- each one leaves a timeline that looks right in the window and changes
    /// nothing in the room. So this reads the lights, the prints and the speaker rather than the
    /// asset.
    ///
    /// AND WHETHER THE DIRECTOR IS PLAYING, which is the whole reason the driver changed: an
    /// evaluated graph never advances the audio clock, so a silent show looks exactly like a
    /// working one until someone puts headphones on.
    /// </summary>
    public class O1PayoffTestRunner : MonoBehaviour
    {
        [SerializeField, Min(1f)] float m_StepTimeout = 60f;

        /// <summary>
        /// Seconds before the hold to drop the show at, so the test is not two and a half
        /// minutes long. The O-1 hold is at 150s, the end of that beat.
        /// </summary>
        [SerializeField] float m_ScrubTo = 147f;

        readonly List<string> m_Failures = new();
        int m_Checks;

        ShowTimelineDriver m_Driver;
        GameObject m_Orbits;
        GameObject m_Footprints;
        Light[] m_Lamps;
        float[] m_FullIntensity;
        bool m_AudioWorks;

        IEnumerator Start()
        {
            Log("Starting.");

            yield return RunTest();

            Log($"{m_Checks} checks, {m_Failures.Count} failed.");
            foreach (string failure in m_Failures) Debug.LogError($"[PayoffTest]   FAILED: {failure}");

            if (m_Failures.Count == 0) Debug.Log("[PayoffTest] RESULT: PASS");
            else Debug.LogError("[PayoffTest] RESULT: FAIL");

            yield return null;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(m_Failures.Count == 0 ? 0 : 1);
#else
            Application.Quit(m_Failures.Count == 0 ? 0 : 1);
#endif
        }

        IEnumerator RunTest()
        {
            ProbeAudio();

            // --- host, and start the show ---
            yield return WaitUntil(() => GameSessionManager.Instance != null &&
                                         StoryProgressManager.Instance != null,
                                  "the session managers");

            if (NetworkManager.Singleton == null ||
                (!NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.StartHost()))
            {
                Fail("could not start a host");
                yield break;
            }

            yield return WaitUntil(() => ShowTimelineDriver.Instance != null, "the show driver");
            m_Driver = ShowTimelineDriver.Instance;

            if (!GameSessionManager.Instance.StartExperience())
            {
                Fail("could not start the experience");
                yield break;
            }

            yield return WaitUntil(() => m_Driver.isPlaying || m_Driver.showTime > 0f,
                                   "the show to start running");

            // THE ONE THAT MAKES SOUND POSSIBLE. Everything else here could pass with a director
            // that is merely evaluated, and the whole show would be silent.
            Check(m_Driver.isPlaying, "the show director is PLAYING, not just being evaluated");

            yield return new WaitForSeconds(1f);
            Check(Mathf.Abs(m_Driver.driftSeconds) < 0.25f,
                  $"it tracks the shared clock (drift {m_Driver.driftSeconds * 1000f:0}ms)");

            // --- find what the payoff is supposed to move ---
            if (!FindFixtures()) yield break;

            Check(!m_Orbits.activeInHierarchy, "the orbits are off before the payoff");
            Check(!m_Footprints.activeInHierarchy, "the footprints are off before the payoff");

            // --- the hold ---
            m_Driver.ScrubTo(m_ScrubTo);
            yield return WaitUntil(() => m_Driver.waitingOn == "beat.O1_Control",
                                   "the show to reach the O-1 hold");

            Check(m_Driver.waitingOn == "beat.O1_Control",
                  $"the show holds for the grab at {m_Driver.showTime:0.0}s");
            Check(!m_Orbits.activeInHierarchy,
                  "and the orbits are still off WHILE it holds -- the hold is before the payoff");

            AudioSource heldSpeaker = FindSpeaker();
            if (heldSpeaker != null)
                Check(!heldSpeaker.isPlaying, "and S0-2 is silent while the show waits");

            // --- the grab clears it ---
            O1GrabStation station = FindStation(PlayerRole.Player1);
            if (!Check(station != null, "Player A's grab station is in the scene")) yield break;

            Check(!station.grabbed, "nothing has been grabbed yet");

            // The fallback the production timeline asks of every gate: if one player never picks
            // their object up, their half is reported anyway rather than stranding the other.
            // It counts from the show ARRIVING here, so it starts a frame after the hold does --
            // hence the wait rather than a bare read.
            yield return WaitUntil(() => station.secondsUntilTimeout > 0f,
                                   "the grab's fallback to start counting", 5f);
            Check(station.secondsUntilTimeout > 0f,
                  $"the grab's fallback is counting down ({station.secondsUntilTimeout:0}s left)");

            // Both objects, and the right cue on each: the coat is Player A's and sounds S0-1-A,
            // the mug is Player B's and sounds S0-1-B. Swapping them is a mistake nobody would
            // catch from inside a headset, because each player only ever hears one of them.
            O1GrabStation other = FindStation(PlayerRole.Player2);
            Check(other != null, "Player B's grab station is in the scene too");
            Check(station.cue != null && station.cue.name == "S0-1-A",
                  $"Player A's object carries S0-1-A (has {(station.cue != null ? station.cue.name : "nothing")})");
            Check(other != null && other.cue != null && other.cue.name == "S0-1-B",
                  $"Player B's carries S0-1-B (has {(other != null && other.cue != null ? other.cue.name : "nothing")})");

            station.Grab();

            Check(station.grabbed, "grabbing latches");

            if (m_AudioWorks)
                Check(station.cuePlaying, "and the grab sounds its cue");
            Check(other == null || !other.cuePlaying,
                  "while the other player's cue stays silent -- each player hears only their own");
            yield return WaitUntil(() => StoryProgressManager.Instance.IsTaskComplete("beat.O1_Control"),
                                   "the grab to satisfy the gate");

            yield return WaitUntil(() => m_Driver.waitingOn == null, "the show to resume");
            Check(m_Driver.waitingOn == null, "the show comes off its hold");

            // --- the lights ---
            yield return WaitUntil(() => m_Orbits.activeInHierarchy, "the orbits to be switched on");

            float orbitsAt = m_Driver.showTime;
            Check(m_Orbits.activeInHierarchy, $"the orbit lights come up at {orbitsAt:0.0}s");

            // They are O-2's doing now, so they must not arrive before that beat does.
            Check(orbitsAt >= 150f, $"inside O-2, which starts at 150s (measured {orbitsAt:0.0}s)");

            // Mid-fade, so a clip that snapped them on is caught. Sampled a beat after the
            // activation rather than on the frame of it, where the ramp is legitimately at zero.
            yield return new WaitForSeconds(1.2f);
            Check(PartlyUp(), $"they fade in rather than snapping on ({Describe()})");

            yield return WaitUntil(() => m_Footprints.activeInHierarchy,
                                   "the footprints to appear");
            float printsAt = m_Driver.showTime;
            Check(m_Footprints.activeInHierarchy, $"the footprints appear at {printsAt:0.0}s");

            // The authored gap is two seconds; a frame of slack either side for the activation
            // being sampled on the next frame.
            float gap = printsAt - orbitsAt;
            Check(gap > 1.5f && gap < 2.6f,
                  $"about two seconds after the lights (measured {gap:0.00}s)");

            var ring = m_Footprints.GetComponentInChildren<FootprintRing>(true);
            if (Check(ring != null, "a footprint ring is under it"))
            {
                yield return new WaitForSeconds(1.5f);
                Check(ring.reveal > 0.05f,
                      $"the prints lay themselves down one at a time (reveal {ring.reveal:0.00})");
            }

            // --- S0-2 ---
            //
            // WHEN IT STARTS, NOT WHAT IS ON THE SOURCE. An AudioTrack never assigns its clip to
            // the AudioSource it is bound to -- it schedules playback through its own playable
            // and uses the source only as an output -- so speaker.clip stays null for the whole
            // cue. Checking it was the first version of this and it failed against a cue that
            // was audibly playing.
            AudioSource speaker = FindSpeaker();
            if (Check(speaker != null, "the cue speaker is under the show driver") && m_AudioWorks)
            {
                yield return WaitUntil(() => speaker.isPlaying, "S0-2 to start");

                float cueAt = m_Driver.showTime;
                Check(speaker.isPlaying, $"S0-2 starts, at {cueAt:0.0}s");

                float afterPrints = cueAt - printsAt;
                Check(afterPrints > 1.5f && afterPrints < 2.6f,
                      $"about two seconds after the footprints (measured {afterPrints:0.00}s)");
            }
            else if (speaker != null)
            {
                Log("  SKIPPED: no audio device in this run, so the cue's start cannot be timed.");
            }

            yield return new WaitForSeconds(1f);
            Check(AtFull(), $"the lights finish at their authored intensity ({Describe()})");

            // --- and they survive the end of the beat that switched them on ---
            //
            // AN ACTIVATION TRACK TURNS ITS TARGET OFF AGAIN once its director stops, unless the
            // track is told otherwise. These cues belong to O-2 but outlast it, so if that
            // setting were wrong the orbits and the prints would vanish at 270s -- a beat
            // boundary nobody is watching, three minutes after the thing that was checked.
            m_Driver.ScrubTo(272f);
            yield return WaitUntil(() => m_Driver.showTime >= 272f, "the show to pass the end of O-2");
            yield return new WaitForSeconds(0.5f);

            Check(m_Orbits.activeInHierarchy, "the orbits are still up after O-2 ends");
            Check(m_Footprints.activeInHierarchy, "and so are the footprints");
            Check(Mathf.Abs(m_Driver.driftSeconds) < 0.25f,
                  $"the show is still on the shared clock (drift {m_Driver.driftSeconds * 1000f:0}ms)");
        }

        /// <summary>
        /// Whether an AudioSource in this run reports playback at all. Batch mode usually has no
        /// audio device, and asserting on isPlaying there would fail a build for the one reason
        /// that says nothing about the show.
        /// </summary>
        void ProbeAudio()
        {
            var go = new GameObject("Audio probe");
            var source = go.AddComponent<AudioSource>();
            source.clip = AudioClip.Create("probe", 4410, 1, 44100, false);
            source.Play();
            m_AudioWorks = source.isPlaying;
            Destroy(go);

            Log(m_AudioWorks
                ? "audio device present; playback will be checked."
                : "no audio device in this run; playback cannot be checked here.");
        }

        bool FindFixtures()
        {
            var orbits = FindObjectsByType<LightOrbit>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (orbits.Length == 0)
            {
                Fail("no LightOrbit in the scene, so there are no orbit lights to bring up");
                return false;
            }

            m_Orbits = orbits[0].transform.parent != null
                ? orbits[0].transform.parent.gameObject
                : orbits[0].gameObject;

            var lamps = new List<Light>();
            var full = new List<float>();
            foreach (LightOrbit orbit in orbits)
            {
                var lamp = orbit.GetComponentInChildren<Light>(true);
                if (lamp == null) continue;
                lamps.Add(lamp);

                // What the prefab authored, read before the fade writes anything. The animation
                // track ramps to exactly this and then stops writing, leaving it here.
                full.Add(Mathf.Max(lamp.intensity, 0.0001f));
            }
            m_Lamps = lamps.ToArray();
            m_FullIntensity = full.ToArray();

            var rings = FindObjectsByType<FootprintRing>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (rings.Length == 0)
            {
                Fail("no FootprintRing in the scene; run Tools/O-1/Wire Grab Cues and Payoff");
                return false;
            }
            m_Footprints = rings[0].transform.parent != null
                ? rings[0].transform.parent.gameObject
                : rings[0].gameObject;

            Log($"fixtures: {m_Lamps.Length} lamp(s) under '{m_Orbits.name}', " +
                $"{rings.Length} ring(s) under '{m_Footprints.name}'.");
            return true;
        }

        static O1GrabStation FindStation(PlayerRole role)
        {
            foreach (var station in FindObjectsByType<O1GrabStation>(FindObjectsInactive.Include,
                                                                     FindObjectsSortMode.None))
            {
                if (station.owner == role) return station;
            }
            return null;
        }

        /// <summary>
        /// The speaker O-2's timeline plays S0-2 through. It lives in the onboarding prefab now,
        /// beside the beat that uses it, rather than under the show driver -- a timeline's
        /// bindings have to resolve inside whatever the director ships in.
        /// </summary>
        static AudioSource FindSpeaker()
        {
            foreach (var source in FindObjectsByType<AudioSource>(FindObjectsInactive.Include,
                                                                  FindObjectsSortMode.None))
            {
                if (source.name == "Cue S0-2") return source;
            }
            return null;
        }

        bool PartlyUp()
        {
            for (int i = 0; i < m_Lamps.Length; i++)
            {
                if (m_Lamps[i].intensity <= 0.02f * m_FullIntensity[i]) return false;
                if (m_Lamps[i].intensity >= 0.98f * m_FullIntensity[i]) return false;
            }
            return m_Lamps.Length > 0;
        }

        bool AtFull()
        {
            for (int i = 0; i < m_Lamps.Length; i++)
            {
                if (m_Lamps[i].intensity < 0.95f * m_FullIntensity[i]) return false;
            }
            return m_Lamps.Length > 0;
        }

        string Describe()
        {
            var parts = new List<string>();
            for (int i = 0; i < m_Lamps.Length; i++)
                parts.Add($"{m_Lamps[i].intensity:0.00}/{m_FullIntensity[i]:0.00}");
            return string.Join(", ", parts);
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

        static void Log(string message) => Debug.Log($"[PayoffTest] {message}");
    }
}
