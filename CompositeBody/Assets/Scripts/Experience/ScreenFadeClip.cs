using System;
using UnityEngine;
using UnityEngine.Playables;

namespace CompositeBody.Experience
{
    /// <summary>
    /// A fade authored as a block on the show timeline, rather than as a call that runs on its
    /// own clock.
    ///
    /// WHY NOT JUST CALL FadeOut. <see cref="ScreenFade.FadeTo"/> starts an animation that then
    /// runs by itself, and this show's timeline does not. <see cref="ShowTimelineDriver"/> keeps
    /// the director pulled onto a server-owned clock: it trims playback speed to absorb small
    /// disagreements and jumps outright for real discontinuities -- a hold clearing, a staff
    /// scrub, a late joiner. A free-running fade would carry on through all of that and end up
    /// somewhere different on each headset, and the one moment it must not is a blackout between
    /// two sections, where being half a second apart means one player is in the dark and the
    /// other is not.
    ///
    /// So the alpha here is a FUNCTION OF SHOW TIME, computed fresh every frame from where the
    /// playhead sits inside this clip. Scrub it, jump it, join late, and the fade is simply
    /// wherever the clip says it should be. That is also what makes it scrubbable while
    /// authoring, which is the point of the whole show being one timeline.
    ///
    /// THE CURVE IS THE FADE. Alpha is read straight off it, so one clip can do the whole
    /// out-hold-in of a blackout: up, flat across the middle, down again. Three abutting clips
    /// would say the same thing and could develop a gap between them, and a gap in a blackout
    /// is a frame of the next section arriving early.
    ///
    /// The end value is held after the clip finishes, so a curve that ends high leaves the
    /// screen dark until something lifts it. That is a legitimate thing to want -- the
    /// onboarding's 「Black.」 is the end of a section rather than a transition -- but then the
    /// next section has to bring the light back itself.
    /// </summary>
    [Serializable]
    public class ScreenFadeBehaviour : PlayableBehaviour
    {
        [Tooltip("The colour faded to. Black for a blackout.")]
        public Color color = Color.black;

        [Tooltip("Alpha across the clip: 0 is clear, 1 is the colour above, time runs 0 to 1 " +
                 "from the clip's start to its end.\n\n" +
                 "THE CURVE IS THE FADE, not a shape applied to one. A curve that rises, holds " +
                 "and falls is a whole out-hold-in in a single clip, which reads better on the " +
                 "timeline than three abutting ones and cannot develop gaps between them. A " +
                 "curve that only rises is a plain fade out, and its end value is held.")]
        public AnimationCurve alpha = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            ScreenFade fade = ScreenFade.Instance;
            if (fade == null) return;

            double length = playable.GetDuration();
            float t = length > 0.0 ? (float)(playable.GetTime() / length) : 1f;

            fade.SetFadeColor(color);

            // Zero seconds means snap: the timeline is the clock, so the value is set, never
            // animated towards. Animating here would be two clocks driving one number.
            fade.FadeTo(Mathf.Clamp01(alpha.Evaluate(Mathf.Clamp01(t))), 0f);
        }

        /// <summary>
        /// Settle on the end value when the clip finishes.
        ///
        /// The last ProcessFrame of a clip lands wherever the frame happened to fall, which at
        /// 72 Hz can be a percent or so short of the end. On a blackout that is the difference
        /// between dark and nearly dark, and nearly dark is visible.
        /// </summary>
        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            ScreenFade fade = ScreenFade.Instance;
            if (fade == null) return;

            // Only when the clip has actually run to its end -- a graph stopped mid-clip, which
            // is what a scrub away or a stop looks like, must not slam the screen to black.
            if (playable.GetTime() < playable.GetDuration() - 0.001) return;

            fade.FadeTo(Mathf.Clamp01(alpha.Evaluate(1f)), 0f);
        }
    }

    /// <summary>A <see cref="ScreenFadeBehaviour"/> as a clip that can be dropped on a track.</summary>
    public class ScreenFadeClip : PlayableAsset
    {
        public ScreenFadeBehaviour template = new();

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) =>
            ScriptPlayable<ScreenFadeBehaviour>.Create(graph, template);
    }
}
