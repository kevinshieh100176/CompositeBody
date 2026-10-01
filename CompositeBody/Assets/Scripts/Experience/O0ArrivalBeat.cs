using CompositeBody.Multiplayer;
using UnityEngine;
using XRMultiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// O-0 進入. The beat's space, sound and blob all live under the content root and are
    /// switched on by <see cref="BeatController"/>, so all this adds is the thing that cannot be
    /// expressed by activating an object: the piece opening out of darkness, and closing back
    /// into it on the way to O-1.
    ///
    /// The fade up is slow on purpose. This is the first thing either player sees, and the
    /// script's space is "像意識尚未完全成形" -- arriving by degrees is the content, not a
    /// transition covering a load.
    /// </summary>
    public class O0ArrivalBeat : BeatController
    {
        [SerializeField, Min(0f), Tooltip("Seconds to come up out of black at the start of the beat.")]
        float m_FadeInSeconds = 6f;

        [SerializeField, Min(0f), Tooltip("Seconds to go back to black on the way out. O-1 fades itself back up.")]
        float m_FadeOutSeconds = 2.5f;

        [Header("Atmosphere")]
        [SerializeField, Tooltip("Haze colour. The void has no far wall, so fog is what gives it a distance.")]
        Color m_FogColor = new(0.020f, 0.034f, 0.048f);

        [SerializeField, Min(0f), Tooltip("Exponential-squared fog density. High enough that the sea fades out before its edge does.")]
        float m_FogDensity = 0.035f;

        [SerializeField, Tooltip("Ambient sky/equator/ground, in that order.")]
        Color m_AmbientSky = new(0.075f, 0.098f, 0.122f);
        [SerializeField] Color m_AmbientEquator = new(0.042f, 0.052f, 0.064f);
        [SerializeField] Color m_AmbientGround = new(0.012f, 0.016f, 0.020f);

        /// <summary>
        /// Applied from code rather than authored in the scene's lighting settings, because this
        /// scene is loaded additively and Unity takes its render settings from the active scene,
        /// which stays the lobby. Each beat setting its own atmosphere on entry is also what the
        /// piece needs -- S0-1 is pitch black and S0-3 is a lit room.
        ///
        /// Deliberately not restored on exit: the next beat sets its own, and handing the
        /// lobby's settings back mid-show would flash the room between every beat.
        /// </summary>
        void ApplyAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = m_FogColor;
            RenderSettings.fogDensity = m_FogDensity;

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
                cam.backgroundColor = m_FogColor;
            }
        }

        protected override void OnEnterBeat()
        {
            ApplyAtmosphere();

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

            Utils.Log($"[O0Arrival] Entered. Shared clock: {(ExperienceClock.isShared ? "session" : "local only")}.");
        }

        protected override void OnExitBeat()
        {
            if (ScreenFade.Instance != null)
                ScreenFade.Instance.FadeOut(m_FadeOutSeconds);
        }
    }
}
