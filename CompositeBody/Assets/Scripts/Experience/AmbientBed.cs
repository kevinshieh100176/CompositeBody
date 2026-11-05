using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// A looping sound bed that belongs to a STRETCH of the show rather than to a clip on it --
    /// the sea under the whole onboarding room, O-0 through O-2.
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
    /// Same shape as <see cref="DistantSoundBed"/>, which already keeps O-0's low ambience off
    /// the timeline for the same reason. This one adds the envelope.
    ///
    /// THE ENVELOPE IS A FUNCTION OF SHOW TIME, not an animation started by an event. The fades
    /// are authored in the same numbers as the running order -- the blackout that ends the
    /// onboarding is at 395s, so that is where the sea starts leaving -- and both headsets
    /// compute the level from the clock they already agree on, so neither can end up a second
    /// ahead of the other in a blackout. A scrub, a jump and a late joiner all land on the right
    /// level for where the playhead actually is, which is the argument
    /// <see cref="ScreenFadeBehaviour"/> makes for the alpha it drives.
    ///
    /// Which also means the envelope FREEZES during a hold, because show time does. That is the
    /// behaviour you want: a hold inside a fade would otherwise go on fading while the show
    /// waited. No gate sits inside either window here -- O-1's is at 301s, the fade out starts at
    /// 395s -- so in practice the hold freezes the bed at full level and the loop runs on.
    ///
    /// PLAYBACK, HOWEVER, RUNS ON REAL TIME: the AudioSource has its own clock and nothing here
    /// touches it once it has started. So a hold makes audio time and show time diverge -- 90
    /// seconds of waiting puts the file 90 seconds further in than the show is -- and this does
    /// NOT seek to close the gap. A seek on a bed is an audible click, and where a field
    /// recording of the sea happens to be is not something an audience can be wrong about.
    /// <see cref="AudioSource.loop"/> covers running past the end of the file.
    /// </summary>
    public class AmbientBed : MonoBehaviour
    {
        [SerializeField, Tooltip("The looping source. Left empty, the one on this object is used.")]
        AudioSource m_Source;

        [SerializeField, Range(0f, 1f), Tooltip("Level the bed holds at between the two fades. " +
                                                "This is the volume knob: the envelope below " +
                                                "only ever scales it.")]
        float m_Level = 0.5f;

        [Header("In")]
        [SerializeField, Tooltip("Seconds into the SHOW that the bed starts arriving. 0 is the " +
                                 "top of the show, which is also while the screen is still " +
                                 "black -- the script's own first line is 「聲音先於空間」.")]
        float m_InAt;

        [SerializeField, Min(0f), Tooltip("How long it takes to arrive. Wants to finish before " +
                                          "the first authored cue, or the bed fades up underneath " +
                                          "something and reads as a mix error.")]
        float m_InSeconds = 8f;

        [Header("Out")]
        [SerializeField, Tooltip("Seconds into the show that the bed starts leaving. Owned by " +
                                 "BuildOnboardingSea, which reads it off the blackout clip on " +
                                 "Main.playable rather than having it typed here.")]
        float m_OutAt = 395f;

        [SerializeField, Min(0f), Tooltip("How long it takes to leave. Wants to end while the " +
                                          "screen is still fully black, so the next section " +
                                          "opens on silence rather than on the last of this room.")]
        float m_OutSeconds = 6.7f;

        [Header("No show")]
        [SerializeField, Tooltip("Play flat, with no envelope, when there is no ShowTimelineDriver " +
                                 "in the scene -- a harness scene, or the room opened on its own " +
                                 "to listen to it. Off means silent without a show.")]
        bool m_PlayWithoutShow = true;

        float m_Applied;
        bool m_WarnedNoShow;

        // When the show first went missing, or -1 while there is one. See FlatLevel.
        float m_NoShowSince = -1f;

        /// <summary>How long a missing show is taken for one that has not woken up yet.</summary>
        const float k_NoShowGrace = 0.5f;

        /// <summary>The level actually being applied, 0 to 1. For the debug panel.</summary>
        public float level => m_Applied;

        /// <summary>True while the source is running, hold or no hold.</summary>
        public bool sounding => m_Source != null && m_Source.isPlaying;

        /// <summary>One line for the debug panel: where the envelope is and whether it sounds.</summary>
        public string status
        {
            get
            {
                if (m_Source == null || m_Source.clip == null) return "ambient bed: no clip";
                string where = TryShowTime(out float t) ? $"{t:F0}s" : "no show";
                return $"ambient bed {m_Source.clip.name}: {where}, level {m_Applied:0.00}" +
                       (sounding ? "" : ", silent");
            }
        }

        void Awake()
        {
            if (m_Source == null) m_Source = GetComponent<AudioSource>();
            if (m_Source == null) return;

            // Not playOnAwake: the bed starts when the envelope opens, which on a show held at 0
            // for the lobby is when staff press start. Playing from Awake would have the file
            // minutes in by then, at a level of zero, for nothing.
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

            float target = m_Level * (TryShowTime(out float show) ? Envelope(show) : FlatLevel());

            if (target <= 0f)
            {
                // Stopped rather than left playing at zero, so an eight-minute stream is not
                // decoded for the rest of the show after the onboarding has gone.
                if (m_Source.isPlaying) m_Source.Stop();
                m_Source.volume = 0f;
                m_Applied = 0f;
                return;
            }

            if (!m_Source.isPlaying)
            {
                // From the head of the file, so the sea starts where the recording starts -- its
                // first two seconds are the quietest part of it, which is what makes the arrival
                // read as an arrival. A scrub back into the show restarts it the same way.
                m_Source.volume = 0f;
                m_Source.time = 0f;
                m_Source.Play();
            }

            m_Source.volume = target;
            m_Applied = target;
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

            m_NoShowSince = -1f;
            show = driver.showTime;
            return true;
        }

        /// <summary>
        /// What to do with no show driving: nothing for a moment, then the bed flat out.
        ///
        /// THE GRACE PERIOD IS NOT PAPERING OVER ANYTHING -- it is what keeps the first frame of
        /// MainScene silent. <see cref="ShowTimelineDriver.Instance"/> is assigned in its Awake,
        /// and an Awake that has not run yet is indistinguishable here from a scene that has no
        /// show in it at all. Without the wait, a frame of "no show" at startup would open this
        /// bed at full level for that frame and then stop it again, which is a click at the top
        /// of the piece.
        /// </summary>
        float FlatLevel()
        {
            if (!m_PlayWithoutShow) return 0f;

            if (m_NoShowSince < 0f) m_NoShowSince = Time.unscaledTime;
            if (Time.unscaledTime - m_NoShowSince < k_NoShowGrace) return 0f;

            if (!m_WarnedNoShow)
            {
                m_WarnedNoShow = true;
                Debug.Log($"[AmbientBed] {name}: no ShowTimelineDriver in the scene, so the bed " +
                          "is playing flat with no fades. Fine in a harness scene; in MainScene " +
                          "it means the show is not being driven.");
            }
            return 1f;
        }

        /// <summary>
        /// The envelope: silent, up, held, down, silent.
        ///
        /// Smoothstepped on amplitude rather than ramped linearly -- a linear ramp has a corner
        /// at each end, and on a bed this long and this broad the corner is what you hear rather
        /// than the fade. The shape is one call; change it here if the sea wants to leave faster
        /// than it arrives.
        /// </summary>
        float Envelope(float show)
        {
            if (show <= m_InAt) return 0f;

            if (m_InSeconds > 0f && show < m_InAt + m_InSeconds)
                return Mathf.SmoothStep(0f, 1f, (show - m_InAt) / m_InSeconds);

            // An out that is not after the in is an out that never happens -- which is how you
            // ask for a bed that just keeps going.
            if (m_OutAt <= m_InAt) return 1f;

            if (show >= m_OutAt + m_OutSeconds) return 0f;

            if (show > m_OutAt)
            {
                if (m_OutSeconds <= 0f) return 0f;
                return Mathf.SmoothStep(1f, 0f, (show - m_OutAt) / m_OutSeconds);
            }

            return 1f;
        }
    }
}
