using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// A looping sound bed that belongs to a ROOM rather than to a clip on the show -- the sea
    /// under the onboarding, O-0 through O-2, and the music under S2.
    ///
    /// WHY THIS IS NOT AN AudioTrack. A gated beat does not end after an authored number of
    /// seconds; it ends when both players have done the thing, and
    /// <see cref="ShowTimelineDriver.Apply"/> expresses that wait by PAUSING the director. That
    /// is right for a cue -- a line of narration that kept talking into the wait would be telling
    /// the players the show had moved on -- and it is wrong for a room. O-1's hold sits at 301s
    /// and can last as long as the two of them need; a sea that stopped there would announce that
    /// the room had been switched off and was waiting for them, which is the one thing the
    /// onboarding must not say. So the bed is an AudioSource in the scene, outside the graph the
    /// hold pauses, and it keeps sounding through the wait.
    ///
    /// Same shape as <see cref="DistantSoundBed"/>, which already keeps the distant events off
    /// the timeline for the same reason. This one adds the envelope.
    ///
    /// TWO KINDS OF ROOM, AND THEY ARRIVE ON DIFFERENT CLOCKS. The onboarding room is older than
    /// the show: the players are standing in it through the lobby and the calibration, before
    /// staff press start, and the sea is what tells them the place has a size. A bed like that
    /// comes up on its own wall clock the moment it wakes up and does not wait for a show time
    /// that is pinned at 0 until the show is started -- <see cref="m_InAt"/> negative.
    ///
    /// A room the show walks INTO is the other case. S2 begins at 767s, inside the blackout that
    /// closes S1, and its music must not be audible over the end of S1's narration a moment
    /// earlier. There is no wall clock that knows where that is; only show time does. So
    /// <see cref="m_InAt"/> set to a show time puts the arrival on the same clock as the
    /// departure, and the bed is silent until the show reaches the section it belongs to.
    ///
    /// The departure is always show time, for both kinds.
    ///
    /// Leaving is the opposite case. The end of the onboarding is a scored moment -- a blackout
    /// at 395s with S1 opening behind it -- so the fade out is a function of
    /// <see cref="ShowTimelineDriver.showTime"/>, authored in the same numbers as the running
    /// order, and both headsets compute it from the clock they already agree on. Neither can end
    /// up a second ahead of the other in a blackout, and a scrub, a jump or a late joiner lands
    /// on the level that where-the-playhead-actually-is calls for. That is the argument
    /// <see cref="ScreenFadeBehaviour"/> makes for the alpha it drives.
    ///
    /// Which also means the fade out FREEZES during a hold, because show time does. That is the
    /// behaviour you want -- a hold inside a fade would otherwise go on fading while the show
    /// waited -- and no gate sits inside the window here: O-1's is at 301s, the fade out starts
    /// at 395s. So in practice a hold freezes the bed at full level and the loop runs on.
    ///
    /// PLAYBACK RUNS ON ITS OWN CLOCK and nothing here touches it once it has started. Audio time
    /// and show time therefore diverge -- by the whole length of the lobby, plus every second of
    /// every hold -- and this does NOT seek to close the gap. A seek on a bed is an audible
    /// click, and where a field recording of the sea happens to be is not something an audience
    /// can be wrong about. The consequence is that the loop point is reached routinely rather
    /// than exceptionally, which is why the asset is cut as a seamless loop; see
    /// <c>BuildOnboardingSea</c> for how.
    /// </summary>
    public class AmbientBed : MonoBehaviour
    {
        [SerializeField, Tooltip("The looping source. Left empty, the one on this object is used.")]
        AudioSource m_Source;

        [SerializeField, Range(0f, 1f), Tooltip("Level the bed holds at between arriving and " +
                                                "leaving. This is the volume knob: the fades " +
                                                "only ever scale it.")]
        float m_Level = 0.5f;

        [Header("In")]
        [SerializeField, Tooltip("Seconds into the SHOW that the bed starts arriving -- for a " +
                                 "room the show walks into, like S2's music, which must be " +
                                 "silent until the show gets there.\n\n" +
                                 "NEGATIVE instead means the bed arrives on this machine's own " +
                                 "wall clock, from the moment the scene wakes up: the onboarding " +
                                 "room, which is there before the show is. That is the default.")]
        float m_InAt = -1f;

        [SerializeField, Min(0f), Tooltip("How long the bed takes to arrive, on whichever clock " +
                                          "the field above selects -- from the show time it " +
                                          "names, or from the moment the scene wakes up.\n\n" +
                                          "0 means it is simply there, at full level, the " +
                                          "instant it is due.")]
        float m_InSeconds = 8f;

        [Header("Out -- always on show time")]
        [SerializeField, Tooltip("Seconds into the SHOW that the bed starts leaving. Owned by " +
                                 "the builder -- BuildOnboardingSea, BuildS2Music -- which reads " +
                                 "it off the blackout clip on Main.playable rather than having " +
                                 "it typed here.")]
        float m_OutAt = 395f;

        [SerializeField, Min(0f), Tooltip("How long it takes to leave. Wants to end while the " +
                                          "screen is still fully black, so the next section " +
                                          "opens on silence rather than on the last of this room.")]
        float m_OutSeconds = 6.7f;

        [Header("No show")]
        [SerializeField, Tooltip("Keep playing, with no fade out, when there is no " +
                                 "ShowTimelineDriver in the scene -- a harness scene, or the " +
                                 "room opened on its own to listen to it. Off means silent " +
                                 "without a show.")]
        bool m_PlayWithoutShow = true;

        float m_Applied;
        bool m_WarnedNoShow;

        // Unscaled time at which the source last started. The arrival is measured from here
        // rather than from OnEnable so that a bed which comes back -- a staff scrub back into
        // the onboarding -- arrives again instead of snapping on at full level.
        float m_SoundingSince;

        /// <summary>The level actually being applied, 0 to 1. For the debug panel.</summary>
        public float level => m_Applied;

        /// <summary>True while the source is running, hold or no hold.</summary>
        public bool sounding => m_Source != null && m_Source.isPlaying;

        /// <summary>One line for the debug panel: where the bed is and whether it sounds.</summary>
        public string status
        {
            get
            {
                if (m_Source == null || m_Source.clip == null) return "ambient bed: no clip";
                string where = TryShowTime(out float t) ? $"show {t:F0}s" : "no show";
                string arrival = m_InAt >= 0f ? $"in at {m_InAt:F0}s" : "in on the local clock";
                return $"ambient bed {m_Source.clip.name}: {where}, {arrival}, " +
                       $"level {m_Applied:0.00}" +
                       (sounding ? $", {m_Source.time:F0}s into the loop" : ", silent");
            }
        }

        void Awake()
        {
            if (m_Source == null) m_Source = GetComponent<AudioSource>();
            if (m_Source == null) return;

            // Not playOnAwake: Update starts it on the first frame anyway, at a level of zero,
            // and letting it do that keeps one place deciding whether the bed should sound.
            m_Source.playOnAwake = false;
            m_Source.loop = true;

            // Nothing in this project pauses the listener today. Said out loud anyway, because
            // the whole point of this component is that a pause somewhere else must not be able
            // to silence the room.
            m_Source.ignoreListenerPause = true;

            m_Source.volume = 0f;
        }

        void OnDisable()
        {
            if (m_Source != null && m_Source.isPlaying) m_Source.Stop();
            m_Applied = 0f;
        }

        void Update()
        {
            if (m_Source == null || m_Source.clip == null) return;

            float gate = Gate();
            if (gate <= 0f)
            {
                // Stopped rather than left playing at zero, so a seven-minute stream is not
                // decoded for the rest of the show after the onboarding has gone -- and, with a
                // show-time arrival, is not decoded for the half hour BEFORE the section either.
                if (m_Source.isPlaying) m_Source.Stop();
                m_Source.volume = 0f;
                m_Applied = 0f;
                return;
            }

            if (!m_Source.isPlaying)
            {
                // From the head of the file, and the arrival is timed from here.
                m_Source.volume = 0f;
                m_Source.time = 0f;
                m_Source.Play();
                m_SoundingSince = Time.unscaledTime;
            }

            float target = m_Level * Mathf.Min(Arrival(), gate);
            m_Source.volume = target;
            m_Applied = target;
        }

        /// <summary>
        /// How far into its arrival the bed is, 0 to 1, on this machine's own clock.
        ///
        /// Unsynchronised, and that is correct rather than merely tolerable: this runs while the
        /// two players are in the lobby, which they reach at different moments and sometimes on
        /// machines that are not talking to each other yet. There is nothing to agree with. By
        /// the time anything scored happens, both have been at full level for minutes.
        ///
        /// Flat 1 when the arrival is on show time, where <see cref="InGateAt"/> shapes it
        /// instead. Both at once would ramp the bed twice -- the source starts on the frame the
        /// show-time gate opens, so the local clock would be running the same fade over again --
        /// and the Min in Update would just pick whichever happened to be slower.
        /// </summary>
        float Arrival()
        {
            if (m_InAt >= 0f) return 1f;
            if (m_InSeconds <= 0f) return 1f;

            float since = Time.unscaledTime - m_SoundingSince;
            if (since >= m_InSeconds) return 1f;
            return Mathf.SmoothStep(0f, 1f, since / m_InSeconds);
        }

        /// <summary>
        /// How much of the bed the show wants right now, 0 to 1: up across the arrival when that
        /// is on show time, 1 while the section runs, down across the fade out, 0 outside.
        /// </summary>
        float Gate() => TryShowTime(out float show) ? GateAt(show) : NoShowGate();

        /// <summary>
        /// The gate at a given show time. Split out from <see cref="Gate"/> so the envelope can
        /// be read off at the landmarks -- the gate at 301s, at the start of the blackout, at the
        /// moment the screen is fully covered -- without a show running to put the playhead there.
        ///
        /// Multiplied rather than Min'd: the two ends never overlap on a bed that is worth
        /// building, and a product keeps the shape honest if somebody ever authors one that does.
        /// </summary>
        float GateAt(float show) => InGateAt(show) * OutGateAt(show);

        /// <summary>
        /// 0 before the bed is due, ramping to 1 across the arrival, 1 after it -- and a flat 1
        /// at all times when the arrival is on this machine's own clock instead.
        ///
        /// Same smoothstep as the way out, and for the same reason.
        /// </summary>
        float InGateAt(float show)
        {
            if (m_InAt < 0f) return 1f;

            if (show <= m_InAt) return 0f;
            if (m_InSeconds <= 0f) return 1f;
            if (show >= m_InAt + m_InSeconds) return 1f;

            return Mathf.SmoothStep(0f, 1f, (show - m_InAt) / m_InSeconds);
        }

        /// <summary>
        /// 1 while the show still wants the room, ramping to 0 across the fade out, 0 after it.
        ///
        /// Smoothstepped on amplitude rather than ramped linearly -- a linear ramp has a corner
        /// at each end, and on a bed this long and this broad the corner is what you hear rather
        /// than the fade. The shape is one call; change it here if the sea wants to leave faster
        /// than it arrives.
        /// </summary>
        float OutGateAt(float show)
        {
            if (show <= m_OutAt) return 1f;
            if (m_OutSeconds <= 0f) return 0f;
            if (show >= m_OutAt + m_OutSeconds) return 0f;

            return Mathf.SmoothStep(1f, 0f, (show - m_OutAt) / m_OutSeconds);
        }

        /// <summary>
        /// Where the show is, or false when nothing is driving one.
        ///
        /// <see cref="ShowTimelineDriver.showTime"/> rather than <see cref="ExperienceClock"/>:
        /// the clock is monotonic and never stops, which is right for the drifting and the
        /// distant sounds derived from it and wrong for a fade that has to line up with a
        /// blackout. The driver's own fallback covers there being no session, so a desk test
        /// still gets a show time here.
        /// </summary>
        bool TryShowTime(out float show)
        {
            ShowTimelineDriver driver = ShowTimelineDriver.Instance;
            if (driver == null)
            {
                show = 0f;
                return false;
            }

            show = driver.showTime;
            return true;
        }

        /// <summary>
        /// What to do with no show driving: keep the room, or nothing.
        ///
        /// There is no startup race to worry about here, unlike the fade out -- the arrival
        /// starts at zero either way, so a frame or two before
        /// <see cref="ShowTimelineDriver"/>'s Awake has run cannot produce a click.
        ///
        /// Note that this overrides a show-time arrival as well as the fade out: a section bed
        /// opened in a harness scene sounds straight away rather than waiting for a show time
        /// that will never arrive. That is the point of the switch -- somebody wants to hear the
        /// file -- and the only reason it is worth saying is that it makes the one case where
        /// this bed is audible outside its own section.
        /// </summary>
        float NoShowGate()
        {
            if (!m_PlayWithoutShow) return 0f;

            if (!m_WarnedNoShow)
            {
                m_WarnedNoShow = true;
                Debug.Log($"[AmbientBed] {name}: no ShowTimelineDriver in the scene, so the bed " +
                          "arrives and then simply stays. Fine in a harness scene; in MainScene " +
                          "it means the show is not being driven.");
            }
            return 1f;
        }
    }
}
