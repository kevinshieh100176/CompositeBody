using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;
using CompositeBody.PointClouds;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Runs O-0's handover and the whole of O-1 in play mode: the 真人 dissolve on O-0's own
    /// timeline and the player's body arrives after them, the first wave's objects and the
    /// cartons dissolve in, the show waits at its hold, and then the chain -- pick an object up,
    /// both players holding it unlocks its line, put it in the box, the next object arrives when
    /// that line is over -- until the last line has been heard and the gate clears. The room then
    /// dissolves out, and the orbit lights, the footprints and S0-2 arrive in order in O-2.
    ///
    /// THE GRABS ARE REAL GRABS. A hand with no input behind it (<see cref="TestHand"/>) closes
    /// on the object through the interaction manager, carries it to the carton and opens there,
    /// which is the only way to see what the players actually complain about: that an object
    /// floating with gravity off keeps any push it is given for ever, and that it has to become
    /// an ordinary falling object the moment a hand opens on it. Calling Grab() proves the cue
    /// fires and nothing else.
    ///
    /// NOBODY TOUCHES ANYTHING IN O-0 ANY MORE, which is the half of this that had no coverage
    /// at all: the beat used to hang on a player reaching out, and the three things it starts --
    /// their hands, their body, the 真人 leaving -- are now a cue on the timeline. A cue that
    /// fires at forty-five seconds whether or not this machine has an avatar yet is exactly the
    /// kind of thing that works on the desk and fails in the venue, so it is checked here.
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
        /// THE RUNNING ORDER IS READ OFF THE TIMELINE, NOT WRITTEN DOWN HERE. This used to be
        /// four serialized show times -- 10, 108, 140, 218 -- copied from BuildMainTimeline's
        /// table, with the hold at 221s and O-2 at 230s as constants beside them. Every one of
        /// them went stale the day O-1 grew from 90 seconds to 170: the test scrubbed to 218s
        /// looking for a hold that is now at 301s, waited its minute out, and reported eleven
        /// failures that were all the same wrong number. Worse, they were reported in the
        /// language of the show -- "the show does not hold for the boxes" -- which is exactly
        /// what a real fault would say.
        ///
        /// So the driver is asked where each beat is (<see cref="ShowTimelineDriver.TryGetScene"/>)
        /// and where the hold marker sits (<see cref="ShowTimelineDriver.TryGetHold"/>), both of
        /// which come off the asset the show actually plays. What is left below is OFFSETS -- how
        /// far into a beat to look for something, which is a property of the beat's own shape and
        /// does not move when the running order does.
        /// </summary>
        [SerializeField, Min(0f), Tooltip("How far into O-0 to check the room is still empty. " +
                                          "The 真人 arrive with the first 自白, not at the top.")]
        float m_IntoO0 = 10f;

        [SerializeField, Min(0f), Tooltip("Seconds before the END of O-0 that the 真人 are still " +
                                          "whole. The handover is authored to land on the beat's " +
                                          "last word -- the dissolve runs 109-119s of a 140s " +
                                          "O-0 -- so it moves with the end, not with the start.")]
        float m_WholeBeforeO0Ends = 31f;

        [SerializeField, Min(0f), Tooltip("Seconds before the end of O-0 to sample the dissolve " +
                                          "half-done.")]
        float m_HalfGoneBeforeO0Ends = 26f;

        [SerializeField, Min(1f), Tooltip("How far before the hold marker to drop the show in. " +
                                          "Far enough to see it arrive, near enough not to wait.")]
        float m_BeforeHold = 6f;

        [SerializeField, Min(0f), Tooltip("How far into O-1 the dissolve-in is under way -- " +
                                          "sampled mid-fade, where a curve that snaps from 0 to " +
                                          "1 is caught.")]
        float m_IntoDissolveIn = 4.5f;

        /// <summary>The gate O-1 holds on. The one id the show and this test have to agree on.</summary>
        const string k_Gate = "beat.O1_Control";

        /// <summary>
        /// Where AvatarFormation's authored m_BodyCurve leaves zero. Below this the player has a
        /// membrane forming and no body under it, which is the half of the arrival that is
        /// allowed to overlap the dissolve.
        /// </summary>
        const float k_MembraneShare = 0.5f;

        readonly List<string> m_Failures = new();
        int m_Checks;

        ShowTimelineDriver m_Driver;
        GameObject m_Orbits;
        GameObject m_Footprints;
        Light[] m_Lamps;
        float[] m_FullIntensity;
        bool m_AudioWorks;

        // The running order, as the show itself has it. Filled by ReadRunningOrder.
        float m_O0Start, m_O0End, m_O1Start, m_O1End, m_O2Start, m_O2End, m_HoldAt;

        /// <summary>The role this machine holds, which is the side of O-1 it can actually play.</summary>
        PlayerRole m_Role = PlayerRole.Player1;

        PlayerRole m_Absent => m_Role == PlayerRole.Player1 ? PlayerRole.Player2 : PlayerRole.Player1;

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

            // --- where the beats are, according to the show ---
            if (!ReadRunningOrder()) yield break;

            m_Role = InteractionDebug.LocalRole();
            Check(m_Role != PlayerRole.Unassigned,
                  $"this machine holds a role ({m_Role}), so its reports can be attributed");

            // --- find what the payoff is supposed to move ---
            if (!FindFixtures()) yield break;

            Check(!m_Orbits.activeInHierarchy, "the orbits are off before the payoff");
            Check(!m_Footprints.activeInHierarchy, "the footprints are off before the payoff");

            yield return CheckHandover();
            yield return CheckDissolveIn();

            // --- the hold ---
            m_Driver.ScrubTo(m_HoldAt - m_BeforeHold);
            yield return WaitUntil(() => m_Driver.waitingOn == k_Gate,
                                   "the show to reach the O-1 hold", m_BeforeHold + 20f);

            Check(m_Driver.waitingOn == k_Gate,
                  $"the show holds for the boxes at {m_Driver.showTime:0.0}s");
            Check(Mathf.Abs(m_Driver.showTime - m_HoldAt) < 1.5f,
                  $"at its marker's {m_HoldAt:0}s, {m_O1End - m_HoldAt:0}s before O-1 ends " +
                  $"(measured {m_Driver.showTime:0.0}s)");
            Check(!m_Orbits.activeInHierarchy,
                  "and the orbits are still off WHILE it holds -- the hold is before the payoff");

            AudioSource heldSpeaker = FindSpeaker();
            if (heldSpeaker != null)
                Check(!heldSpeaker.isPlaying, "and S0-2 is silent while the show waits");

            // --- the chain: both players' objects, one wave at a time ---
            //
            // THIS IS NOT THE BEAT THE TEST WAS WRITTEN FOR. O-1 was one object each, picked up
            // and put in a box, with the line owed to the drop. It is a chain now:
            // 「玩家自己把自己的物品放進箱子後就會跑出下一樣自己的物品」 -- several objects each, a line
            // answering BOTH players holding that wave's object, and the next wave arriving only
            // once that line has been heard. So this follows the chain O1GrabWaves actually has
            // rather than a copy of it written out here.
            O1GrabWaves waves = FindWaves();
            if (!Check(waves != null, "O-1 has a chain of waves to run")) yield break;
            if (!Check(waves.waves.Count > 0, "with waves wired in it")) yield break;

            O1DropSequence sequence = FindSequence();
            if (!Check(sequence != null, "and a drop sequence to carry the gate")) yield break;
            Check(!sequence.reported, "which has not reported anything yet");

            // ONE MACHINE CANNOT BE TWO PLAYERS, and the beat's lines wait on both of them.
            // 「vo還是要兩個玩家都抓過自己的那個後才會出現」 is a flag per role on the server, and
            // StoryProgressManager raises its unlock only when BOTH flags are in -- the solo
            // accommodation is on IsTaskComplete, not on the unlock. So this plays its own side
            // for real, through XRI, and forces the absent player's half of each wave the way
            // the debug panel's 'as P2' does. What is under test is the local half: the grab,
            // the physics, the box, the queue and the gate.
            Log($"this machine plays {m_Role}; {m_Absent}'s halves are forced server-side.");

            // Counted from the show ARRIVING at the gate, so it starts a frame after the hold.
            yield return WaitUntil(() => sequence.secondsUntilTimeout > 0f,
                                   "the sequence's fallback to start counting", 5f);
            Check(sequence.secondsUntilTimeout > 0f,
                  $"its fallback is counting down ({sequence.secondsUntilTimeout:0}s left)");

            O1DropBox[] mine = BoxesFor(m_Role);
            if (!Check(mine.Length > 0, $"{m_Role} has box watchers to fill ({mine.Length})"))
                yield break;

            // THE SAME ANSWER FOR EVERY OBJECT, whichever answer it is. Whether a grab makes its
            // own short sound has changed more than once -- the cue was on the grab, then the
            // speech moved to the drop and the cue came off, then the speech moved to the wave
            // and the cue came back, and it was taken off again while this test was being
            // written. Asserting either state pins a decision that is not this file's to make;
            // what IS a fault, and a silent one, is three stations carrying a cue and the fourth
            // not -- the player who gets that object hears nothing and reports their headset.
            CheckCuesAgree(waves);

            // A hand with no input behind it, so the grabs below take the real XRI path: the
            // interactable's own select events, its dynamic attach, its rigidbody handling.
            var hand = new GameObject("Test hand").AddComponent<TestHand>();
            yield return null;                 // let it register with the interaction manager

            for (int i = 0; i < waves.waves.Count; i++)
            {
                O1GrabWaves.Wave wave = waves.waves[i];
                if (wave == null) continue;

                string label = string.IsNullOrEmpty(wave.name) ? $"wave {i + 1}" : wave.name;

                // A WAVE WITH NO OBJECTS IS THE PAUSE -- 「兩人要將紙箱封箱」, a line and nothing
                // else, 「這裡先不要加互動」. Nothing reports it and there is nothing to pick up: it
                // speaks as soon as the lines before it have been heard.
                if (wave.isPause)
                {
                    Log($"  {label}: a pause -- a line with no object behind it.");
                    yield return WaitUntil(() => wave.spoken, $"{label} to speak by itself", 30f);
                    Check(wave.spoken,
                          $"{label} speaks for itself once the lines before it are done");
                    yield return WaitOutLine(waves, label);
                    continue;
                }

                O1GrabStation station = StationIn(wave, m_Role);
                if (!Check(station != null, $"{label} has an object for {m_Role}")) continue;

                // IT HAS TO BE HERE BEFORE IT CAN BE TAKEN. Every wave after the first fades in
                // once the previous line has been heard, and PropPresence keeps the grab
                // switched off until the object is substantially there.
                if (i > 0)
                {
                    yield return WaitUntil(() => station.grab != null && station.grab.enabled,
                                           $"{label}'s object to arrive for {m_Role}", 30f);
                    Check(station.grab != null && station.grab.enabled,
                          $"{label} arrives for {m_Role} and can be picked up");
                }

                if (!Check(station.grab != null, $"{station.name} has something to take hold of"))
                    continue;

                Check(!station.grabbed, $"{station.name} has not been picked up yet");


                yield return CheckItFloats(station);

                // --- a real grab, through XRI, not station.Grab() ---
                if (!GrabWith(hand, station))
                {
                    Fail($"could not put a hand on {station.name}");
                    continue;
                }

                yield return null;

                Check(station.grab.isSelected, $"{station.name} is in a hand");
                Check(station.grabbed, "and the grab latches");

                // Only when there is one to hear. A station with no clip is silent on purpose or
                // not, and CheckCuesAgree above is where that is judged.
                if (m_AudioWorks && station.cue != null)
                    Check(station.cuePlaying, "and its cue sounds");

                // --- the other player, who is not in this run ---
                Check(!wave.unlocked, $"{label} does not unlock on one player's grab");

                StoryProgressManager.Instance.DebugSetRoleDone(wave.taskId, m_Absent, true);
                yield return null;

                // THE FLAGS AND THE CHAIN ARE TWO DIFFERENT QUESTIONS, and a test that only
                // waited for the wave to unlock could not say which of them had gone wrong. The
                // server holding both halves while the beat knows nothing about it is a listener
                // that never attached -- O1GrabWaves subscribes to onTaskBeatUnlocked in
                // OnEnable, which in MainScene races StoryProgressManager's own Awake.
                Check(StoryProgressManager.Instance.IsTaskComplete(wave.taskId),
                      $"the server holds both halves of '{wave.taskId}'");

                yield return WaitUntil(() => wave.unlocked,
                                       $"{label} to unlock once both halves are in", 10f);
                Check(wave.unlocked, $"{label} unlocks when both players are holding theirs");
                Check(waves.speaking, "and its line starts");

                // --- and into the box, by letting go of it in there ---
                O1DropBox box = BoxFor(mine, station);
                if (!Check(box != null, $"a box watcher is waiting for {station.name}")) continue;
                if (!Check(box.mouth != null, "which has a volume to be filled")) continue;
                Check(!box.deposited, "and is empty");

                yield return PutInBox(hand, station, box, label);
                yield return WaitOutLine(waves, label);
            }

            Destroy(hand.gameObject);

            // NOT ON THE FRAME THE LAST CLIP RAN OUT. 'speaking' goes false the moment the
            // clip's length is up, but the chain holds the gap after it before it lets go of the
            // wave -- so asking here instead of waiting reported a beat that was half a second
            // from being over as a beat that never finished.
            yield return WaitUntil(() => waves.finished, "the chain to let go of its last line", 10f);
            Check(waves.finished, "every one of O-1's lines has been heard");

            // --- which is what the gate was waiting for ---
            yield return WaitUntil(() => sequence.reported, "the sequence to report its half", 20f);
            Check(sequence.reported,
                  "the sequence reports once every object has been held and the last line has stopped");

            yield return WaitUntil(() => StoryProgressManager.Instance.IsTaskComplete(k_Gate),
                                   "the gate to be satisfied", 20f);

            yield return WaitUntil(() => m_Driver.waitingOn == null, "the show to resume", 20f);
            Check(m_Driver.waitingOn == null, "the show comes off its hold");

            // --- and the room empties on the way out ---
            //
            // THE HOLD IS BEFORE THE END OF O-1, not at it, so this tail is the dissolve-out. A
            // hold left at the end of the beat would hand O-2 a room with two cartons and a wall
            // of boxes still standing in it.
            PropPresence[] props = O1Props();
            Check(MinPresence(props) > 0.9f, "the props are still there as the hold clears");

            yield return WaitUntil(() => MaxPresence(props) < 0.01f,
                                   "the objects, boxes and wall to dissolve out", 25f);
            Check(MaxPresence(props) < 0.01f,
                  $"they are gone by {m_Driver.showTime:0.0}s, before O-2 starts at {m_O2Start:0}s");
            Check(m_Driver.showTime < m_O2Start + 1f,
                  $"and the room is empty when O-2 begins (measured {m_Driver.showTime:0.0}s)");

            // --- the lights ---
            yield return WaitUntil(() => m_Orbits.activeInHierarchy, "the orbits to be switched on");

            float orbitsAt = m_Driver.showTime;
            Check(m_Orbits.activeInHierarchy, $"the orbit lights come up at {orbitsAt:0.0}s");

            // They are O-2's doing now, so they must not arrive before that beat does.
            Check(orbitsAt >= m_O2Start,
                  $"inside O-2, which starts at {m_O2Start:0}s (measured {orbitsAt:0.0}s)");

            // Mid-fade, so a clip that snapped them on is caught. Sampled a beat after the
            // activation rather than on the frame of it, where the ramp is legitimately at zero.
            //
            // AND THE CLOCK IS NAMED WHEN IT IS NOT SETTLED. These cues fire on the DIRECTOR's
            // time and are measured against the SHARED clock, and the two disagree for a while
            // after a hold: the driver nudges the director's speed in small steps rather than
            // jumping it, deliberately, because a jump is audible. So a run where the gate
            // cleared promptly can read the two-second gap as 1.26s and the two-second fade as
            // already finished -- which is a measurement, not a fault, and must say so rather
            // than being quietly widened into a tolerance that would hide a real snap.
            yield return new WaitForSeconds(1.2f);
            Check(PartlyUp(), $"they fade in rather than snapping on ({Describe()}){Settling()}");

            yield return WaitUntil(() => m_Footprints.activeInHierarchy,
                                   "the footprints to appear");
            float printsAt = m_Driver.showTime;
            Check(m_Footprints.activeInHierarchy, $"the footprints appear at {printsAt:0.0}s");

            // The authored gap is two seconds; a frame of slack either side for the activation
            // being sampled on the next frame.
            float gap = printsAt - orbitsAt;
            Check(gap > 1.5f && gap < 2.6f,
                  $"about two seconds after the lights (measured {gap:0.00}s){Settling()}");

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
            // setting were wrong the orbits and the prints would vanish at the end of the beat
            // -- a boundary nobody is watching, minutes after the thing that was checked.
            float afterO2 = m_O2End + 2f;
            m_Driver.ScrubTo(afterO2);
            yield return WaitUntil(() => m_Driver.showTime >= afterO2,
                                   $"the show to pass the end of O-2 at {m_O2End:0}s");
            yield return new WaitForSeconds(0.5f);

            Check(m_Orbits.activeInHierarchy, "the orbits are still up after O-2 ends");
            Check(m_Footprints.activeInHierarchy, "and so are the footprints");
            Check(Mathf.Abs(m_Driver.driftSeconds) < 0.25f,
                  $"the show is still on the shared clock (drift {m_Driver.driftSeconds * 1000f:0}ms)");
        }

        /// <summary>
        /// O-0's handover, with nobody touching anything: the room is empty, the 真人 arrive with
        /// the first 自白, they dissolve on the timeline, and the player's own body comes in
        /// behind them.
        ///
        /// THE ORDER IS THE POINT, AND IT IS NOT THE ORDER OF THE CUES ANY MORE. The body is cued
        /// at 109.75s, while the figures are still coming apart, so that the formation lands on
        /// the last word of the second 自白 at 140s. What must still be true is the picture rather
        /// than the schedule: AvatarFormation grows the membrane over the first half of its ramp
        /// and only then fades the Ch36 body in, so while a 真人 is still in the room the player
        /// has a film forming and no body under it. 「scan dissolve後 玩家身體按照原本計畫出現」 is
        /// checked as that, and asserting the cue is still down would be asserting the old beat.
        /// </summary>
        IEnumerator CheckHandover()
        {
            var handover = FindFirstObjectByType<ScanHandover>(FindObjectsInactive.Include);
            if (!Check(handover != null, "O-0 has a ScanHandover to take the 真人 away")) yield break;

            var awakening = FindFirstObjectByType<O0TouchAwakening>(FindObjectsInactive.Include);
            if (!Check(awakening != null, "and the arrival its timeline cue fires")) yield break;

            // --- the empty room, before they arrive ---
            //
            // 「現在O_0的SCAN 一開始就在 改成跟第一段VO(A1/B1)一起出現」. Read off the clouds and not off
            // ScanHandover's own field, because the field being 0 is what "a whole 真人" looked
            // like when they were there from the first frame: the arrival composes with the
            // dissolve inside the component, so only the figures can say whether anyone is in
            // the room.
            float emptyAt = m_O0Start + m_IntoO0;
            m_Driver.ScrubTo(emptyAt);
            yield return WaitUntil(() => m_Driver.showTime >= emptyAt,
                                   "the show to reach the top of O-0");
            yield return null;

            Check(FigureDissolve() > 0.95f,
                  $"the room is empty at {m_Driver.showTime:0.0}s, before the 真人 arrive " +
                  $"(dissolve {FigureDissolve():0.00})");

            float wholeAt = m_O0End - m_WholeBeforeO0Ends;
            m_Driver.ScrubTo(wholeAt);
            yield return WaitUntil(() => m_Driver.showTime >= wholeAt,
                                   $"the show to reach O-0's handover at {wholeAt:0}s");
            yield return null;

            Check(handover.progress < 0.05f,
                  $"the 真人 are still whole at {m_Driver.showTime:0.0}s " +
                  $"(dissolve {handover.progress:0.00})");

            // AND THEY ARE ACTUALLY IN THE ROOM, eighty seconds after the seven-second clip that
            // brought them in. That clip carries a flat tail to the end of the dissolve for
            // exactly this reason; if it is ever shortened to its ramp, the 真人 spend the whole
            // first 自白 invisible and every other check here still passes.
            Check(FigureDissolve() < 0.05f,
                  $"and they are in the room to be spoken over (dissolve {FigureDissolve():0.00})");
            Check(!awakening.awakened, "and nobody has touched anything");

            // --- half gone ---
            float halfGoneAt = m_O0End - m_HalfGoneBeforeO0Ends;
            yield return WaitUntil(() => m_Driver.showTime >= halfGoneAt,
                                   $"the 真人 to be half gone at {halfGoneAt:0}s", 25f);

            float half = handover.progress;
            Check(half > 0.2f && half < 0.8f,
                  $"they dissolve rather than vanishing (progress {half:0.00} at {m_Driver.showTime:0.0}s)");
            Check(FigureDissolve() > 0.05f,
                  $"and the dissolve reaches the clouds themselves ({FigureDissolve():0.00})");

            // THE BODY ITSELF IS NOT THERE YET, read while the figures are still going. This is
            // the whole ordering assertion, and it has to be sampled somewhere unambiguous rather
            // than on the frame the dissolve finishes. Not !formed, which is true the instant the
            // cue fires and is now meant to be: what is asserted is that the arrival has not got
            // past its membrane half, so there is no Ch36 body in a room that still has a 真人 in
            // it.
            float formation = AvatarFormation.local == null ? 0f : AvatarFormation.local.progress;
            Check(formation < k_MembraneShare,
                  "the player's body has not begun to show while the 真人 are still leaving " +
                  $"(formation {formation:0.00})");

            yield return WaitUntil(() => handover.complete, "the 真人 to go", 25f);
            Check(handover.complete, $"the 真人 are gone by {m_Driver.showTime:0.0}s");

            // --- and then the body ---
            yield return WaitUntil(() => awakening.awakened, "O-0's timeline to cue the body", 25f);

            float firedAt = m_Driver.showTime;
            Check(firedAt > 44f,
                  $"the body is cued AFTER the 真人 have gone, at {firedAt:0.0}s");

            if (Check(AvatarFormation.local != null, "there is a local avatar to give a body to"))
            {
                yield return WaitUntil(() => AvatarFormation.local.formed,
                                       "the body to start arriving", 20f);
                Check(AvatarFormation.local.formed,
                      "the player's own body starts arriving, with no touch anywhere");
            }
        }

        /// <summary>
        /// O-1's dissolve-in: the objects, the cartons and the wall of boxes arriving together on
        /// the beat's own timeline.
        ///
        /// MID-WAY AS WELL AS AT THE ENDS, because the failure this is really looking for is a
        /// presence that goes straight from 0 to 1 -- which is what a broken curve path, a
        /// missing Animator binding or an opaque material with no clipping all look like.
        ///
        /// AND ONLY THE FIRST WAVE ARRIVES HERE. This used to read every PropPresence in the
        /// scene and wait for all of them to reach 1, which two changes made impossible: the
        /// later waves' objects stand in the room at presence 0 until their own wave brings them
        /// in, and other beats have props of their own with a presence of their own -- one of
        /// them sitting at 1 made "the room is empty when O-1 starts" fail against a room that
        /// was empty. So the group is O-1's own, and the objects that have not been cued yet are
        /// taken out of it and checked for the opposite.
        /// </summary>
        IEnumerator CheckDissolveIn()
        {
            PropPresence[] props = O1Props();
            if (!Check(props.Length >= 3,
                       $"O-1's props, cartons and box wall each have a PropPresence (found {props.Length})"))
                yield break;

            Check(FindWall() != null,
                  "Showcase_CB_boxes is in O-1's group, driven with the rest of them");

            PropPresence[] later = LaterWaveProps();
            PropPresence[] first = Except(props, later);
            Log($"O-1's presences: {props.Length}, of which {later.Length} belong to a later " +
                $"wave and arrive when its line is over.");

            m_Driver.ScrubTo(m_O1Start);
            yield return WaitUntil(() => m_Driver.showTime >= m_O1Start + 0.4f,
                                   "the show to reach the start of O-1");
            yield return null;

            Check(MaxPresence(props) < 0.2f,
                  $"the room is empty when O-1 starts (highest presence {MaxPresence(props):0.00})");

            yield return WaitUntil(() => m_Driver.showTime >= m_O1Start + m_IntoDissolveIn,
                                   "the dissolve-in to be under way", 25f);

            float mid = MaxPresence(first);
            Check(mid > 0.1f && mid < 0.95f,
                  $"they dissolve in rather than appearing (presence {mid:0.00} at {m_Driver.showTime:0.0}s)");

            yield return WaitUntil(() => MinPresence(first) > 0.98f,
                                   "the dissolve-in to finish", 25f);
            Check(MinPresence(first) > 0.98f,
                  $"all {first.Length} of the first wave's are fully there by " +
                  $"{m_Driver.showTime:0.0}s");

            // 「兩個人都抓完燈後 vo跑完 外套才會出現」 -- the jacket is not in the room yet, and a
            // later wave standing there from the start of the beat is the one failure the check
            // above cannot see.
            if (later.Length > 0)
                Check(MaxPresence(later) < 0.02f,
                      $"and the {later.Length} later-wave object(s) are not here yet " +
                      $"(highest {MaxPresence(later):0.00})");
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

        /// <summary>
        /// Where each beat runs and where O-1 stops, asked of the show rather than remembered
        /// here. Every one of these is a hard failure: a test that cannot find the beat it is
        /// about would otherwise report the beat as broken.
        /// </summary>
        bool ReadRunningOrder()
        {
            bool ok = Check(m_Driver.TryGetScene("O-0", out m_O0Start, out m_O0End),
                            "O-0 is on the running order");
            ok &= Check(m_Driver.TryGetScene("O-1", out m_O1Start, out m_O1End), "and so is O-1");
            ok &= Check(m_Driver.TryGetScene("O-2", out m_O2Start, out m_O2End), "and O-2");
            ok &= Check(m_Driver.TryGetHold(k_Gate, out m_HoldAt),
                        $"and a hold marker gates it on '{k_Gate}'");

            if (ok)
                Log($"running order: O-0 {m_O0Start:0}-{m_O0End:0}s, " +
                    $"O-1 {m_O1Start:0}-{m_O1End:0}s holding at {m_HoldAt:0}s, " +
                    $"O-2 {m_O2Start:0}-{m_O2End:0}s. Read off the timeline, not from this file.");

            return ok;
        }

        static O1GrabWaves FindWaves() =>
            FindFirstObjectByType<O1GrabWaves>(FindObjectsInactive.Include);

        /// <summary>
        /// Every station in a wave carries a cue, or none of them does. Which of the two is the
        /// beat's business; the mixture is the fault, and it is one a single player hits and
        /// nobody else can reproduce.
        /// </summary>
        void CheckCuesAgree(O1GrabWaves waves)
        {
            int withCue = 0, total = 0;
            var names = new List<string>();

            foreach (O1GrabWaves.Wave wave in waves.waves)
            {
                if (wave == null || wave.isPause) continue;

                foreach (O1GrabWaves.Side side in wave.sides)
                {
                    if (side?.station == null) continue;
                    total++;
                    if (side.station.cue == null) continue;
                    withCue++;
                    names.Add(side.station.cue.name);
                }
            }

            Check(withCue == 0 || withCue == total,
                  withCue == 0
                      ? $"every grab in the beat is silent ({total} objects, no cues)"
                      : $"all {total} grabs carry a cue ({string.Join(", ", names)})");
        }

        /// <summary>This role's object in one wave, or null when the wave has none for them.</summary>
        static O1GrabStation StationIn(O1GrabWaves.Wave wave, PlayerRole role)
        {
            foreach (O1GrabWaves.Side side in wave.sides)
            {
                if (side != null && side.role == role && side.station != null) return side.station;
            }
            return null;
        }

        /// <summary>Every box watcher belonging to one player. There is one per object now.</summary>
        static O1DropBox[] BoxesFor(PlayerRole role)
        {
            var found = new List<O1DropBox>();
            foreach (var box in FindObjectsByType<O1DropBox>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None))
            {
                if (box.owner == role) found.Add(box);
            }
            return found.ToArray();
        }

        /// <summary>
        /// The watcher waiting for one particular object, matched on the object itself rather
        /// than on its owner: one carton holds everything a player has to put away, so owner
        /// alone names two of these and the first match is the wrong one half the time.
        /// </summary>
        static O1DropBox BoxFor(O1DropBox[] boxes, O1GrabStation station)
        {
            foreach (O1DropBox box in boxes)
            {
                if (box != null && box.target != null && box.target == station.grab) return box;
            }
            return null;
        }

        /// <summary>
        /// O-1's own presences -- the group the beat's dissolve drives -- taken from under
        /// whatever object carries the drop sequence. Scene-wide would also catch other beats'
        /// props, which have a presence of their own and nothing to do with this one.
        /// </summary>
        static PropPresence[] O1Props()
        {
            O1DropSequence sequence = FindSequence();
            if (sequence == null) return new PropPresence[0];

            Transform root = sequence.transform;
            return root.GetComponentsInChildren<PropPresence>(true);
        }

        /// <summary>
        /// The presences that belong to a wave AFTER the first, which are the objects that have
        /// not been cued in yet. Read off the chain's own wiring: a wave's side names the
        /// PropPresence that fades its object in.
        /// </summary>
        static PropPresence[] LaterWaveProps()
        {
            var found = new List<PropPresence>();

            O1GrabWaves waves = FindWaves();
            if (waves == null) return found.ToArray();

            for (int i = 1; i < waves.waves.Count; i++)
            {
                O1GrabWaves.Wave wave = waves.waves[i];
                if (wave == null) continue;

                foreach (O1GrabWaves.Side side in wave.sides)
                {
                    if (side?.appears != null && !found.Contains(side.appears))
                        found.Add(side.appears);
                }
            }
            return found.ToArray();
        }

        static PropPresence[] Except(PropPresence[] all, PropPresence[] remove)
        {
            var kept = new List<PropPresence>();
            foreach (PropPresence p in all)
            {
                bool drop = false;
                foreach (PropPresence r in remove)
                {
                    if (r == p) drop = true;
                }
                if (!drop) kept.Add(p);
            }
            return kept.ToArray();
        }

        /// <summary>
        /// What the box tests: the centre of mass, not the pivot. These props have their origin
        /// well outside the mesh -- the lamp's is most of a metre under it -- so moving the
        /// transform to a point puts the object somewhere else entirely.
        /// </summary>
        static Vector3 CentreOf(O1GrabStation station) =>
            station.body != null ? station.body.worldCenterOfMass : station.grab.transform.position;

        /// <summary>
        /// Close a hand on the object, through the interaction manager, which is the real path:
        /// the interactable's select events fire, its dynamic attach is computed, and its
        /// rigidbody is handed over and handed back by XRI rather than by this file.
        ///
        /// THE HAND GOES WHERE THE OBJECT IS FIRST. The grab uses a dynamic attach -- held where
        /// the hand closed rather than snapped to the pivot -- so the offset between the two is
        /// taken at this moment, and a hand that closed a metre away would carry the lamp a metre
        /// away for the rest of the beat.
        /// </summary>
        bool GrabWith(TestHand hand, O1GrabStation station)
        {
            XRInteractionManager manager = hand.interactionManager;
            if (manager == null || station.grab == null) return false;

            hand.transform.position = CentreOf(station);
            manager.SelectEnterUnconditionally(hand, station.grab);
            return station.grab.isSelected;
        }

        /// <summary>
        /// The object floats where it was put and CANNOT BE PUSHED -- which is the whole of
        /// 「很容易碰到後 道具無重力的飛出去 不好抓」. A body with gravity off and no damping keeps
        /// whatever push it is given for ever: there is nothing to pull it down and nothing to
        /// slow it, so a hand or a knee catching the lamp on the way past sent it across the room
        /// at walking pace and it never came back. O1GrabStation holds it kinematic until a hand
        /// has opened on it; this is the assertion that it does.
        /// </summary>
        IEnumerator CheckItFloats(O1GrabStation station)
        {
            Rigidbody body = station.body;
            if (!Check(body != null, $"{station.name} has a body to hold still")) yield break;

            Check(body.isKinematic && !body.useGravity,
                  $"{station.name} floats where it was put (kinematic {body.isKinematic}, " +
                  $"gravity {body.useGravity})");

            Vector3 before = station.grab.transform.position;
            body.AddForce(new Vector3(0f, 0f, 25f), ForceMode.Impulse);
            body.AddTorque(new Vector3(0f, 12f, 0f), ForceMode.Impulse);

            yield return new WaitForSeconds(0.4f);

            float moved = Vector3.Distance(station.grab.transform.position, before);
            Check(moved < 0.005f, $"and a shove does not move it ({moved * 1000f:0} mm)");
        }

        /// <summary>
        /// Carry the object to the box and let go of it in there, which is exactly what the box
        /// asks for: inside the mouth, out of a hand, for a quarter of a second.
        ///
        /// CARRIED AND RELEASED, NOT DROPPED FROM ABOVE. The mouth is 0.84 m across and some of
        /// these objects are longer than that, so an object dropped over the carton lands on the
        /// rim and slides -- and which way it slides is physics noise. One run it grazed the
        /// trigger on the way past and filled the box, the next it came to rest on the floor 70
        /// cm behind it, and the test passed or failed on that.
        /// </summary>
        IEnumerator PutInBox(TestHand hand, O1GrabStation station, O1DropBox box, string label)
        {
            Bounds mouth = box.mouth.bounds;
            hand.transform.position += mouth.center - CentreOf(station);

            // Two frames: one for XRI to drive the object to the hand, one for the box's own
            // reading of where it is.
            yield return null;
            yield return null;

            Log($"  {label}: mouth {mouth.center.ToString("F2")} +/- {mouth.extents.ToString("F2")}, " +
                $"object's centre of mass now {CentreOf(station).ToString("F2")}");

            Check(box.objectInside, $"{station.name} is inside {box.owner}'s box");
            Check(box.objectHeld, "and still in a hand, which is not a drop");
            Check(!box.deposited, "so the box has not counted it yet");

            // Cast, because the overload that takes the concrete types is the deprecated one and
            // it is the better match for an XRBaseInteractor and an XRGrabInteractable.
            hand.interactionManager.SelectExit((IXRSelectInteractor)hand,
                                               (IXRSelectInteractable)station.grab);
            yield return null;

            // THE OTHER HALF OF THE HOLD: once a hand has opened on it, it is an ordinary object
            // again. XRI restores the kinematic flag it found -- which is the hold's own -- so
            // without that handover the object would hang in mid-air wherever the hand opened.
            Check(station.released, $"{station.name} has been let go of");
            Check(station.body == null || !station.body.isKinematic, "its body is dynamic again");
            Check(station.body == null || station.body.useGravity,
                  "with gravity on, so it falls rather than hanging where the hand opened");

            yield return WaitUntil(() => box.deposited, $"{label} to settle in the box", 10f);
            Check(box.deposited, "and letting go of it in there fills the box, with nothing else told");
        }

        /// <summary>
        /// Wait for a wave's line to finish.
        ///
        /// THERE IS NO SHORTENING IT. The queue is timed off the length of the longest clip on
        /// the show's own clock rather than off AudioSource.isPlaying -- deliberately, so a muted
        /// run and a debug unlock still advance -- which means stopping the speaker here would
        /// change nothing. O-1 is 170 seconds long for this reason: its three lines are 74, 34
        /// and 37 seconds, and the beat has to hold them.
        /// </summary>
        IEnumerator WaitOutLine(O1GrabWaves waves, string label)
        {
            if (!waves.speaking) yield break;

            float left = waves.secondsOfLineLeft;
            Log($"  {label}: waiting its line out, {left:0}s left on the clock.");

            yield return WaitUntil(() => !waves.speaking, $"{label}'s line to finish", left + 25f);
            Check(!waves.speaking, $"{label}'s line finishes");
        }

        /// <summary>
        /// How far the most-dissolved 真人 has gone. Taken as a maximum across every point cloud
        /// in the scene rather than by naming one: S1 has figures of its own sitting at zero, so
        /// the maximum is whichever cloud the handover is actually driving -- and zero if it is
        /// driving none of them, which is the failure this is for.
        /// </summary>
        static float FigureDissolve()
        {
            float most = 0f;
            foreach (var figure in FindObjectsByType<PointCloudFigure>(FindObjectsInactive.Include,
                                                                       FindObjectsSortMode.None))
                most = Mathf.Max(most, figure.dissolve);
            return most;
        }

        static PropPresence FindWall()
        {
            foreach (var presence in FindObjectsByType<PropPresence>(FindObjectsInactive.Include,
                                                                      FindObjectsSortMode.None))
            {
                if (presence.name == "Showcase_CB_boxes") return presence;
            }
            return null;
        }

        static O1DropSequence FindSequence() =>
            FindFirstObjectByType<O1DropSequence>(FindObjectsInactive.Include);

        /// <summary>
        /// A note for any timing read while the director is still catching up with the shared
        /// clock -- empty once they agree. Appended to the measurements that are taken in that
        /// window, so a number that looks wrong says why before anyone goes looking.
        /// </summary>
        string Settling()
        {
            float drift = m_Driver != null ? m_Driver.driftSeconds : 0f;
            return Mathf.Abs(drift) < 0.05f
                ? ""
                : $" -- NOTE: the clock was still catching up after the hold, drift {drift * 1000f:0}ms";
        }

        static float MaxPresence(PropPresence[] props)
        {
            float most = 0f;
            foreach (PropPresence p in props) most = Mathf.Max(most, p.presence);
            return most;
        }

        static float MinPresence(PropPresence[] props)
        {
            float least = 1f;
            foreach (PropPresence p in props) least = Mathf.Min(least, p.presence);
            return props.Length == 0 ? 0f : least;
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

    /// <summary>
    /// A hand with no input behind it, for a test that has to pick things up where there is no
    /// headset and no controller.
    ///
    /// WHY NOT AN XRDirectInteractor: the input-driven interactors decide for themselves whether
    /// they are selecting, from an input reader that reads nothing in batch mode -- so the
    /// interaction manager would take the object straight back out of the hand on the frame
    /// after it was put there. <see cref="XRBaseInteractor"/>'s own isSelectActive is simply
    /// true, and keepSelectedTargetValid keeps a forced selection alive even though this hand
    /// never finds a valid target of its own, which together is exactly a hand that holds what
    /// it is told to hold until it is told to let go.
    ///
    /// AND IT IS STILL THE REAL PATH. The grab goes through the interaction manager, so the
    /// interactable runs its own select-enter and select-exit: the dynamic attach, the rigidbody
    /// being made kinematic and handed back, forceGravityOnDetach, O1GrabStation's cue and its
    /// hold. Calling O1GrabStation.Grab() instead -- which is what this test used to do -- proves
    /// that the cue fires and nothing at all about the physics the players complain about.
    /// </summary>
    class TestHand : XRBaseInteractor
    {
    }
}
