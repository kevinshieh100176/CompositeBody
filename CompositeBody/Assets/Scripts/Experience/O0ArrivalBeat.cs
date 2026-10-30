using CompositeBody.Multiplayer;
using UnityEngine;
using UnityEngine.Playables;
using XRMultiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// O-0 進入. The beat's space, sound and figures all live under the content root and are
    /// switched on by <see cref="BeatController"/>, so this adds the two things that cannot be
    /// expressed by activating an object: the piece opening out of darkness, and the cue.
    ///
    /// THE CUE. The players arrive inside ONE white light. A few seconds later a purple 光圈 and a
    /// yellow one come up, one on each 真人, and the white light they arrived in goes out -- so
    /// the shared light becomes two separate ones while they are standing in it. That is the
    /// whole piece in half a minute, which is why it is the first thing either player sees.
    ///
    /// The fade up is slow on purpose. The script's space is 「像意識尚未完全成形」 -- arriving by
    /// degrees is the content, not a transition covering a load.
    ///
    /// DRIVEN FROM <see cref="ExperienceClock"/>, NOT FROM A COROUTINE. Two people are standing
    /// in the same room watching the same lights change; a coroutine runs on each machine's own
    /// Time.time, and the two cross-fades would be however far apart the two headsets started.
    /// Evaluating the whole cue as a function of shared time also means it has no state to get
    /// wrong: staff can jump into this beat from anywhere, a late joiner lands at the right point
    /// in the fade rather than at the beginning of it, and running the beat twice gives the same
    /// picture both times.
    /// </summary>
    public class O0ArrivalBeat : BeatController
    {
        [SerializeField, Min(0f), Tooltip("Seconds to come up out of black at the start of the beat.")]
        float m_FadeInSeconds = 6f;

        [SerializeField, Min(0f), Tooltip("Seconds to go back to black on the way out. O-1 fades itself back up.")]
        float m_FadeOutSeconds = 2.5f;

        [Header("The cue")]
        [SerializeField, Tooltip("The Timeline that owns the cue. When this is set it drives " +
                                 "every fixture and the fields below are ignored -- they are the " +
                                 "fallback for a beat that has no timeline yet.")]
        PlayableDirector m_Director;

        [SerializeField, Min(0f), Tooltip("FALLBACK ONLY. Seconds from entering the beat until " +
                                          "the two 光圈 start to come up. Counts from the same " +
                                          "moment the screen starts fading in, so this wants to " +
                                          "be longer than the fade -- the white light has to be " +
                                          "seen alone first for its going out to mean anything.")]
        float m_HoldSeconds = 5f;

        [SerializeField, Min(0.01f), Tooltip("Seconds for the purple and yellow 光圈 to reach full.")]
        float m_ColourUpSeconds = 4f;

        [SerializeField, Min(0f), Tooltip("Seconds after the 光圈 START coming up before the white " +
                                          "light starts going. Overlap, not a hand-off: with this " +
                                          "at zero the room dips dark in the middle of the change.")]
        float m_CentreDownDelay = 2f;

        [SerializeField, Min(0.01f), Tooltip("Seconds for the white light to go out.")]
        float m_CentreDownSeconds = 5f;

        [Header("Driven -- wired by BuildO0Arrival")]
        [SerializeField, Tooltip("The white light the players arrive in.")]
        VolumetricSpot m_CentreBeam;

        [SerializeField] Light m_CentreLamp;

        [SerializeField, Tooltip("The emissive slab standing in the white light. Fades with it.")]
        Renderer m_Slab;

        [SerializeField, Min(0f), Tooltip("Full intensity of the white fixture. Authored here " +
                                          "rather than read off the Light, so re-entering the " +
                                          "beat cannot capture a value the cue already faded.")]
        float m_CentreLampIntensity = 16f;

        [SerializeField] Color m_SlabEmission = new(1.35f, 1.36f, 1.4f);

        [SerializeField, Tooltip("The two 光圈. Built dark; the cue is the only thing that lights them.")]
        VolumetricSpot[] m_ColourBeams = new VolumetricSpot[0];

        [SerializeField] Light[] m_ColourLamps = new Light[0];

        [SerializeField, Min(0f)] float m_ColourLampIntensity = 6f;

        [Header("Atmosphere")]
        [SerializeField, Tooltip("The void. Only the camera's clear colour now -- the Air zone in " +
                                 "the content root owns the fog itself and rewrites RenderSettings " +
                                 "every frame, so setting fog here would last exactly one frame.")]
        Color m_VoidColor = new(0.012f, 0.013f, 0.017f);

        [SerializeField, Tooltip("Ambient sky/equator/ground, in that order.")]
        Color m_AmbientSky = new(0.030f, 0.032f, 0.038f);
        [SerializeField] Color m_AmbientEquator = new(0.020f, 0.021f, 0.025f);
        [SerializeField] Color m_AmbientGround = new(0.010f, 0.010f, 0.013f);

        static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");

        float m_CueStart;
        MaterialPropertyBlock m_SlabBlock;

        /// <summary>Seconds since the beat was entered, on the shared clock.</summary>
        public float cueTime => ExperienceClock.now - m_CueStart;

        /// <summary>
        /// Put the cue wherever <paramref name="seconds"/> says, without playing it. For the
        /// editor preview harness and for staff scrubbing; the live beat drives this from the
        /// clock instead.
        /// </summary>
        public void ScrubCue(float seconds) => ApplyCue(seconds);

        /// <summary>
        /// Applied from code rather than authored in the scene's lighting settings, because this
        /// scene is loaded additively and Unity takes its render settings from the active scene,
        /// which stays the lobby. Each beat setting its own atmosphere on entry is also what the
        /// piece needs -- S0-1 is pitch black and S0-3 is a lit room.
        ///
        /// Public because an editor preview of this beat has to put the room in the same state
        /// the beat would, and nothing else does it -- a still rendered without this is lit by
        /// whatever ambient the empty scene came with, which is a bright default skybox.
        ///
        /// Deliberately not restored on exit: the next beat sets its own, and handing the
        /// lobby's settings back mid-show would flash the room between every beat.
        /// </summary>
        public void ApplyAtmosphere()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = m_AmbientSky;
            RenderSettings.ambientEquatorColor = m_AmbientEquator;
            RenderSettings.ambientGroundColor = m_AmbientGround;

            // URP does not fog the skybox, so a bright lobby sky would still show above the
            // horizon and give the void a ceiling it is not supposed to have.
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = m_VoidColor;
            }
        }

        protected override void OnEnterBeat()
        {
            ApplyAtmosphere();

            m_CueStart = ExperienceClock.now;
            ApplyCue(0f);

            if (ScreenFade.Instance != null)
            {
                // Snap to black first: staff can jump into this beat from anywhere, including
                // from a beat that left the view clear, and fading "up" from clear does nothing.
                ScreenFade.Instance.FadeTo(1f, 0f);
                ScreenFade.Instance.FadeIn(m_FadeInSeconds);
            }
            else
            {
                Utils.LogWarning("[O0Arrival] No ScreenFade in scene; the beat will pop in rather than fade up.");
            }

            Utils.Log($"[O0Arrival] Entered. Shared clock: {(ExperienceClock.isShared ? "session" : "local only")}. " +
                      $"光圈 up at {m_HoldSeconds:F1}s, white light out by " +
                      $"{m_HoldSeconds + m_CentreDownDelay + m_CentreDownSeconds:F1}s.");
        }

        protected override void OnExitBeat()
        {
            if (ScreenFade.Instance != null)
                ScreenFade.Instance.FadeOut(m_FadeOutSeconds);
        }

        void LateUpdate()
        {
            if (!isActiveBeat) return;
            ApplyCue(cueTime);
        }

        /// <summary>
        /// The whole cue as a function of time. No state, so it can be evaluated at any point in
        /// any order -- which is what lets a late joiner and a staff scrub land on exactly the
        /// picture the other headset is already showing.
        /// </summary>
        void ApplyCue(float t)
        {
            float centre;

            if (m_Director != null)
            {
                // The Timeline owns every fixture. Its time is DRIVEN here rather than played,
                // because a PlayableDirector left to play runs on the local machine's clock --
                // and two people standing in the same room would watch the same cross-fade at
                // different moments, however carefully it was authored. Setting the time from
                // ExperienceClock makes the Timeline the authoring surface and the shared clock
                // the time source, which is the only arrangement where both are true at once.
                //
                // UNLESS THE MASTER TIMELINE OWNS IT. When Main.playable nests this cue as a
                // Control clip it is already setting this director's time, from the same shared
                // clock -- and two writes on one frame means whichever ran second wins, which
                // shows up as the cross-fade stuttering. The fixtures are still read back below,
                // because the Timeline is still what moved them.
                if (!ShowTimelineDriver.ownsSubTimelines)
                {
                    m_Director.time = Mathf.Max(t, 0f);
                    m_Director.Evaluate();
                }

                // Read back rather than recomputed: the slab has to follow whatever the curve
                // actually does, including after someone drags the keys about.
                centre = m_CentreBeam != null ? m_CentreBeam.reveal : 0f;
            }
            else
            {
                // No Timeline on this beat: compute the same cue here. Kept because a beat is
                // built before its timeline is authored, and a beat with no cue at all is harder
                // to judge than one with a rough version of the right one.
                float colour = Smooth((t - m_HoldSeconds) / Mathf.Max(m_ColourUpSeconds, 0.01f));
                centre = 1f - Smooth((t - m_HoldSeconds - m_CentreDownDelay)
                                     / Mathf.Max(m_CentreDownSeconds, 0.01f));

                if (m_CentreBeam != null) m_CentreBeam.reveal = centre;
                if (m_CentreLamp != null) m_CentreLamp.intensity = m_CentreLampIntensity * centre;

                for (int i = 0; i < m_ColourBeams.Length; i++)
                {
                    if (m_ColourBeams[i] != null) m_ColourBeams[i].reveal = colour;
                }

                for (int i = 0; i < m_ColourLamps.Length; i++)
                {
                    // The lamp rises with the haze. A 光圈 whose air fades in over a figure
                    // already lit in that colour arrives backwards -- the light has to look like
                    // it is arriving, not like the smoke caught up with it.
                    if (m_ColourLamps[i] != null)
                        m_ColourLamps[i].intensity = m_ColourLampIntensity * colour;
                }
            }

            // The slab, either way, and deliberately NOT on the Timeline. It is driven through a
            // MaterialPropertyBlock, which an AnimationTrack cannot reach -- so rather than give
            // it a second curve that could drift out of step with the light it belongs to, it
            // follows the centre beam's reveal.
            if (m_Slab != null)
            {
                // A property block, not the material: the slab shares O0Slab.mat, and writing
                // the fade into the asset would leave it dark in the project the next time the
                // scene opened.
                m_SlabBlock ??= new MaterialPropertyBlock();
                m_Slab.GetPropertyBlock(m_SlabBlock);
                // Squared, so the slab spends less of the cross-fade as a dim grey rectangle and
                // more of it either present or gone. The pool keeps the linear curve -- light on
                // a floor can dim gracefully, a lit panel either reads as a light or as a prop.
                m_SlabBlock.SetColor(k_EmissionColor, m_SlabEmission * (centre * centre));
                m_Slab.SetPropertyBlock(m_SlabBlock);
                // At zero the slab is still a white box catching whatever is left of the lamp.
                // Switching the renderer off is what actually takes it out of the room.
                bool visible = centre > 0.004f;
                if (m_Slab.enabled != visible) m_Slab.enabled = visible;
            }
        }

        static float Smooth(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));
    }
}
