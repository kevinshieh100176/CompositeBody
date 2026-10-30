using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;
using CompositeBody.PointClouds;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Runs O-0's handover and the whole of O-1 in play mode: the 真人 dissolve on O-0's own
    /// timeline and the player's body arrives after them, the objects and the boxes dissolve in,
    /// the show waits at its hold, a grab does NOT clear it, putting the object in the box and
    /// hearing the line out does, the room then dissolves out, and the orbit lights, the
    /// footprints and S0-2 arrive in order in O-2.
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
        /// Seconds before each point of interest to drop the show at, so the test is not four
        /// minutes long. These are show time, and they follow BuildMainTimeline's running order:
        /// O-0 runs 0-80s with its handover at 34-45s, O-1 runs 80-170s and holds at 161s, and
        /// O-2 runs 170-290s.
        /// </summary>
        [SerializeField] float m_ScrubToHandover = 33f;

        [SerializeField] float m_ScrubToO1 = 80f;

        [SerializeField] float m_ScrubTo = 158f;

        /// <summary>Where O-1's hold sits, and where O-2 starts. From the running order.</summary>
        const float k_HoldAt = 161f;
        const float k_O2At = 170f;
        const float k_AfterO2 = 292f;

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

            // --- find what the payoff is supposed to move ---
            if (!FindFixtures()) yield break;

            Check(!m_Orbits.activeInHierarchy, "the orbits are off before the payoff");
            Check(!m_Footprints.activeInHierarchy, "the footprints are off before the payoff");

            yield return CheckHandover();
            yield return CheckDissolveIn();

            // --- the hold ---
            m_Driver.ScrubTo(m_ScrubTo);
            yield return WaitUntil(() => m_Driver.waitingOn == "beat.O1_Control",
                                   "the show to reach the O-1 hold");

            Check(m_Driver.waitingOn == "beat.O1_Control",
                  $"the show holds for the boxes at {m_Driver.showTime:0.0}s");
            Check(Mathf.Abs(m_Driver.showTime - k_HoldAt) < 1.5f,
                  $"at the authored {k_HoldAt:0}s, nine seconds before O-1 ends " +
                  $"(measured {m_Driver.showTime:0.0}s)");
            Check(!m_Orbits.activeInHierarchy,
                  "and the orbits are still off WHILE it holds -- the hold is before the payoff");

            AudioSource heldSpeaker = FindSpeaker();
            if (heldSpeaker != null)
                Check(!heldSpeaker.isPlaying, "and S0-2 is silent while the show waits");

            // --- the grab, and then the box that clears it ---
            O1GrabStation station = FindStation(PlayerRole.Player1);
            if (!Check(station != null, "Player A's grab station is in the scene")) yield break;

            O1DropBox box = FindBox(PlayerRole.Player1);
            if (!Check(box != null, "and the box they are asked to put it in")) yield break;

            Check(!station.grabbed, "nothing has been grabbed yet");
            Check(!box.deposited, "and the box is empty");
            Check(box.target != null, "the box knows which object belongs in it");

            // THE GATE MOVED OFF THE BOX. With several objects each and a line of speech owed to
            // every one of them, "this player is finished" is a count and a silent speaker,
            // which one box cannot see -- so O1DropSequence reports it and carries the fallback.
            // A box still reporting one would clear the hold on the first drop, with a line
            // still sounding and the other object on the floor.
            O1DropSequence sequence = FindSequence();
            if (!Check(sequence != null, "O-1 has a drop sequence to run the beat")) yield break;

            Check(!sequence.reported, "which has not reported anything yet");

            // Counted from the show ARRIVING here, so it starts a frame after the hold does --
            // hence the wait rather than a bare read.
            yield return WaitUntil(() => sequence.secondsUntilTimeout > 0f,
                                   "the sequence's fallback to start counting", 5f);
            Check(sequence.secondsUntilTimeout > 0f,
                  $"its fallback is counting down ({sequence.secondsUntilTimeout:0}s left)");
            Check(box.secondsUntilTimeout < 0f,
                  "and the box is not running a second countdown of its own");

            // THE GRAB IS SILENT NOW. S0-1-A and S0-1-B played when the object was picked up;
            // 「每當玩家把一個物件放進箱子後會按順序撥放音檔」 puts the speech on the drop, and leaving
            // the old cue in place would be a line on the grab and another on the drop for the
            // same object.
            O1GrabStation other = FindStation(PlayerRole.Player2);
            Check(other != null, "Player B's grab station is in the scene too");
            Check(station.cue == null,
                  $"Player A's object is silent on the grab (carries {(station.cue != null ? station.cue.name : "nothing")})");
            Check(other == null || other.cue == null,
                  $"and so is Player B's (carries {(other != null && other.cue != null ? other.cue.name : "nothing")})");

            station.Grab();

            Check(station.grabbed, "grabbing latches");
            Check(sequence.queued == 0,
                  "and says nothing -- the line is owed to the box, not the hand");

            // THE GRAB IS NOT THE GATE. The beat asks for the object to end up in the box, and a
            // show that came off its hold the moment a hand closed would resume with a coat
            // still swinging from it. Read a frame later, so a report sent on the grab's own
            // frame would have landed by the time this looks.
            yield return null;
            Check(!StoryProgressManager.Instance.IsTaskComplete("beat.O1_Control"),
                  "picking it up does NOT clear the gate on its own");
            Check(m_Driver.waitingOn == "beat.O1_Control",
                  "and the show is still holding, waiting for the box");

            // THE OBJECT IS PUT IN, not the component told that it was. Calling Deposit() would
            // prove the report works and nothing about the thing the player actually does --
            // whether the trigger is where the box is, whether it is big enough to catch an
            // object let go of over it, whether the settle time ever elapses. So the coat is
            // moved into the mouth and dropped, and the box is left to notice by itself.
            if (Check(box.mouth != null, "the box has a volume to be filled"))
            {
                // HELD IN THE OPENING AND LET GO OF, which is exactly what the box asks for:
                // inside the volume, and not in a hand, for a quarter of a second.
                //
                // IT USED TO BE DROPPED FROM ABOVE, on the argument that this was the honest
                // test. It was also a coin toss. The coat is 0.99 m along its longest axis and
                // the carton's mouth is 0.84 m across, so it cannot go in -- it lands on the rim
                // and slides, and which way it slides is physics noise: one run it grazed the
                // trigger on the way past and filled the box, the next it bounced clear and came
                // to rest on the floor 70 cm behind it. A test that passes or fails on that is
                // not telling anyone anything about the show.
                //
                // Kinematic while it is placed, because the carton is a real mesh collider and
                // an object moved inside its walls is thrown straight back out by the
                // depenetration. Not kinematic is restored afterwards, so what the player then
                // sees -- a coat draped over a carton rather than sitting in it -- is still
                // whatever physics makes of it.
                var body = box.target.GetComponent<Rigidbody>();
                Bounds held = BoundsOf(box.target.transform);
                Vector3 over = box.mouth.bounds.center;

                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }

                // The transform is moved by the offset its CENTRE OF MASS needs, not to the
                // point itself: the pivot of these props is not in the middle of them, and the
                // box tests the centre of mass.
                Vector3 centre = body != null ? body.worldCenterOfMass : box.target.transform.position;
                box.target.transform.position += over - centre;

                // SAY WHAT THE DROP IS ACTUALLY DOING. A box that never fills is the one failure
                // here with no visible cause -- the object may be resting on the rim, sleeping
                // in mid-air, or inside a trigger that is switched off -- and all three read as
                // "timed out waiting for the box to notice". These numbers are the difference
                // between a bug report and a guess.
                Bounds volume = box.mouth.bounds;
                Log($"  drop: mouth {volume.center.ToString("F2")} +/- {volume.extents.ToString("F2")}, " +
                    $"object {held.size.ToString("F2")} let go of at {over.ToString("F2")}");

                yield return null;

                // The trigger really does contain what this calls the middle of the box. It is
                // worth asking, because every other assertion here is downstream of it: a mouth
                // whose collider had been switched off, or whose volume had drifted away from
                // the carton, would read exactly like a box nobody managed to fill.
                Check(box.objectInside, "the object is inside the box's volume");
                Check(!box.objectHeld, "and nobody is holding it");

                // SAY WHAT IT IS DOING WHILE IT SETTLES. A box that never fills is otherwise the
                // one failure here with no visible cause.
                float started = Time.realtimeSinceStartup;
                while (!box.deposited && Time.realtimeSinceStartup - started < 8f)
                {
                    yield return new WaitForSeconds(1f);
                    Vector3 com = body != null ? body.worldCenterOfMass : box.target.transform.position;
                    Log($"  drop +{Time.realtimeSinceStartup - started:0.0}s: " +
                        $"com {com.ToString("F2")}, inside {box.objectInside}, " +
                        $"held {box.objectHeld}, " +
                        $"trigger {(box.mouth.enabled && box.mouth.gameObject.activeInHierarchy)}");
                }

                Check(box.deposited,
                      "and letting go of it in there fills the box, with nothing else told");

                // Back to a thing that falls, so the beat is left in the state it would really
                // be in. The coat does not fit the carton, so this is where it drapes.
                if (body != null) body.isKinematic = false;
            }

            // 「音檔也都撥放完畢後」 -- the drop owes a line, and the beat is not over until it has
            // been heard. ONE AudioSource and one queue, so two lines cannot sound at once by
            // construction; what has to be checked is that the gate waits for the queue.
            //
            // Only when there is a device. With none, isPlaying is false on the frame Play() is
            // called, so the queue drains instantly and there is no waiting to observe -- which
            // is a fact about batch mode, not about the show.
            if (m_AudioWorks)
            {
                Check(sequence.queued > 0 || sequence.speaking,
                      "the drop owes a line, and it is queued rather than fired");
                Check(!StoryProgressManager.Instance.IsTaskComplete("beat.O1_Control"),
                      "and the gate does NOT clear while the line is still sounding");

                // HOW LONG THE LINE ACTUALLY IS, rather than a budget guessed here. The only
                // clip O-1 has at the moment is three minutes long -- longer than the beat --
                // so waiting it out would time out every run and say nothing about the show.
                AudioSource lineSpeaker = FindLineSpeaker();
                float left = lineSpeaker != null && lineSpeaker.clip != null
                    ? lineSpeaker.clip.length - lineSpeaker.time
                    : 0f;

                if (left > 30f)
                {
                    Log($"  NOTE: the queued line is {lineSpeaker.clip.length:0}s long -- longer " +
                        "than O-1 itself, so it cannot be heard out here. Stopping it by hand; " +
                        "the gate has already been shown to be waiting on it.");
                    lineSpeaker.Stop();
                    left = 0f;
                }

                yield return WaitUntil(() => !sequence.speaking && sequence.queued == 0,
                                       "the line to finish", left + 15f);
                Check(!sequence.speaking && sequence.queued == 0,
                      "and the queue drains when the speaker falls silent");
            }
            else
            {
                Log("  SKIPPED: no audio device in this run, so the queue cannot be heard out.");
            }

            yield return WaitUntil(() => sequence.reported, "the sequence to report its half", 20f);
            Check(sequence.reported, "the sequence reports once the boxes are full and it is quiet");

            yield return WaitUntil(() => StoryProgressManager.Instance.IsTaskComplete("beat.O1_Control"),
                                   "the gate to be satisfied");

            yield return WaitUntil(() => m_Driver.waitingOn == null, "the show to resume");
            Check(m_Driver.waitingOn == null, "the show comes off its hold");

            // --- and the room empties on the way out ---
            //
            // THE HOLD IS NINE SECONDS BEFORE THE END OF O-1, not at it, so this tail is the
            // dissolve-out. A hold left at the end of the beat would hand O-2 a room with two
            // cartons and a wall of boxes still standing in it.
            PropPresence[] props = FindObjectsByType<PropPresence>(FindObjectsInactive.Include,
                                                                    FindObjectsSortMode.None);
            Check(MinPresence(props) > 0.9f, "the props are still there as the hold clears");

            yield return WaitUntil(() => MaxPresence(props) < 0.01f,
                                   "the objects, boxes and wall to dissolve out", 25f);
            Check(MaxPresence(props) < 0.01f,
                  $"they are gone by {m_Driver.showTime:0.0}s, before O-2 starts at {k_O2At:0}s");
            Check(m_Driver.showTime < k_O2At + 1f,
                  $"and the room is empty when O-2 begins (measured {m_Driver.showTime:0.0}s)");

            // --- the lights ---
            yield return WaitUntil(() => m_Orbits.activeInHierarchy, "the orbits to be switched on");

            float orbitsAt = m_Driver.showTime;
            Check(m_Orbits.activeInHierarchy, $"the orbit lights come up at {orbitsAt:0.0}s");

            // They are O-2's doing now, so they must not arrive before that beat does.
            Check(orbitsAt >= k_O2At,
                  $"inside O-2, which starts at {k_O2At:0}s (measured {orbitsAt:0.0}s)");

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
            m_Driver.ScrubTo(k_AfterO2);
            yield return WaitUntil(() => m_Driver.showTime >= k_AfterO2,
                                   "the show to pass the end of O-2");
            yield return new WaitForSeconds(0.5f);

            Check(m_Orbits.activeInHierarchy, "the orbits are still up after O-2 ends");
            Check(m_Footprints.activeInHierarchy, "and so are the footprints");
            Check(Mathf.Abs(m_Driver.driftSeconds) < 0.25f,
                  $"the show is still on the shared clock (drift {m_Driver.driftSeconds * 1000f:0}ms)");
        }

        /// <summary>
        /// O-0's handover, with nobody touching anything: the 真人 dissolve on the timeline, and
        /// the player's own body is cued after them rather than before.
        ///
        /// THE ORDER IS THE POINT. 「scan dissolve後 玩家身體按照原本計畫出現」 -- a cue that fired
        /// while the figures were still coming apart would read as the body being shed off them,
        /// which is the OLD beat, and the two are indistinguishable from a built asset.
        /// </summary>
        IEnumerator CheckHandover()
        {
            var handover = FindFirstObjectByType<ScanHandover>(FindObjectsInactive.Include);
            if (!Check(handover != null, "O-0 has a ScanHandover to take the 真人 away")) yield break;

            var awakening = FindFirstObjectByType<O0TouchAwakening>(FindObjectsInactive.Include);
            if (!Check(awakening != null, "and the arrival its timeline cue fires")) yield break;

            m_Driver.ScrubTo(m_ScrubToHandover);
            yield return WaitUntil(() => m_Driver.showTime >= m_ScrubToHandover,
                                   "the show to reach O-0's handover");
            yield return null;

            Check(handover.progress < 0.05f,
                  $"the 真人 are still whole at {m_Driver.showTime:0.0}s " +
                  $"(dissolve {handover.progress:0.00})");
            Check(!awakening.awakened, "and nobody has touched anything");

            // --- half gone ---
            yield return WaitUntil(() => m_Driver.showTime >= 39f, "the 真人 to be half gone", 25f);

            float half = handover.progress;
            Check(half > 0.2f && half < 0.8f,
                  $"they dissolve rather than vanishing (progress {half:0.00} at {m_Driver.showTime:0.0}s)");
            Check(FigureDissolve() > 0.05f,
                  $"and the dissolve reaches the clouds themselves ({FigureDissolve():0.00})");

            // THE BODY IS NOT THERE YET, read while the figures are still going. This is the
            // whole ordering assertion, and it has to be sampled somewhere unambiguous rather
            // than on the frame the dissolve finishes.
            Check(AvatarFormation.local == null || !AvatarFormation.local.formed,
                  "the player has no body while the 真人 are still leaving");

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
        /// </summary>
        IEnumerator CheckDissolveIn()
        {
            PropPresence[] props = FindObjectsByType<PropPresence>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None);
            if (!Check(props.Length >= 3,
                       $"O-1's props, cartons and box wall each have a PropPresence (found {props.Length})"))
                yield break;

            Check(FindWall() != null,
                  "Showcase_CB_boxes is in O-1's group, driven with the rest of them");

            m_Driver.ScrubTo(m_ScrubToO1);
            yield return WaitUntil(() => m_Driver.showTime >= m_ScrubToO1 + 0.4f,
                                   "the show to reach the start of O-1");
            yield return null;

            Check(MaxPresence(props) < 0.2f,
                  $"the room is empty when O-1 starts (highest presence {MaxPresence(props):0.00})");

            yield return WaitUntil(() => m_Driver.showTime >= m_ScrubToO1 + 4.5f,
                                   "the dissolve-in to be under way", 25f);

            float mid = MaxPresence(props);
            Check(mid > 0.1f && mid < 0.95f,
                  $"they dissolve in rather than appearing (presence {mid:0.00} at {m_Driver.showTime:0.0}s)");

            yield return WaitUntil(() => MinPresence(props) > 0.98f,
                                   "the dissolve-in to finish", 25f);
            Check(MinPresence(props) > 0.98f,
                  $"all {props.Length} of them are fully there by {m_Driver.showTime:0.0}s");
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

        /// <summary>Every renderer under an object, in one box, in world space.</summary>
        static Bounds BoundsOf(Transform thing)
        {
            var renderers = thing.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(thing.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
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
        /// The speaker the drop sequence talks through. Found by name, like S0-2's: an
        /// AudioSource is the one thing in this beat that has to be reached from outside the
        /// component that owns it, because how long a line has left is a property of the clip
        /// and not of the queue.
        /// </summary>
        static AudioSource FindLineSpeaker()
        {
            foreach (var source in FindObjectsByType<AudioSource>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None))
            {
                if (source.name == "Cue O-1") return source;
            }
            return null;
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

        static O1DropBox FindBox(PlayerRole role)
        {
            foreach (var box in FindObjectsByType<O1DropBox>(FindObjectsInactive.Include,
                                                             FindObjectsSortMode.None))
            {
                if (box.owner == role) return box;
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
