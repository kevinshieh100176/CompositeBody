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
    /// LEAVING THE SCREEN BLACK IS POSSIBLE, AND DELIBERATE. When the clip ends the last value
    /// is held, so a clip that ends at 1 leaves the screen black until something else lifts it.
    /// For the onboarding's 「Black.」 that is right -- the dark is the end of the section, not a
    /// transition effect -- but it does mean the next section has to fade itself up, with its
    /// own clip or with ScreenFade.FadeIn. A clip that returns to 0 by its own end is the other
    /// way to say it, and the curve can hold the black in the middle for as long as wanted.
    /// </summary>
    [Serializable]
    public class ScreenFadeBehaviour : PlayableBehaviour
    {
        [Tooltip("Alpha at the start of the clip. 0 is clear, 1 is the fade colour.")]
        [Range(0f, 1f)] public float from;

        [Tooltip("Alpha at the end. Held after the clip finishes, so 1 leaves the screen dark.")]
        [Range(0f, 1f)] public float to = 1f;

        [Tooltip("The colour faded to. Black for a blackout.")]
        public Color color = Color.black;

        [Tooltip("Shape across the clip. A curve that rises, flattens and falls does a whole " +
                 "out-hold-in inside one clip, which is usually easier to read on the timeline " +
                 "than three abutting ones.")]
        public AnimationCurve shape = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            ScreenFade fade = ScreenFade.Instance;
            if (fade == null) return;

            double length = playable.GetDuration();
            float t = length > 0.0 ? (float)(playable.GetTime() / length) : 1f;

            fade.SetFadeColor(color);

            // Zero seconds means snap: the timeline is the clock, so the value is set, never
            // animated towards. Animating here would be two clocks driving one number.
            fade.FadeTo(Mathf.Lerp(from, to, shape.Evaluate(Mathf.Clamp01(t))), 0f);
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

            fade.FadeTo(Mathf.Lerp(from, to, shape.Evaluate(1f)), 0f);
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
