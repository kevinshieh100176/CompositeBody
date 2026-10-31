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
    ///
    /// THE LIGHTING SWITCH RIDES ALONG. A blackout between two sections is also the only moment
    /// the baked lighting can be swapped without the players seeing it happen, so the clip that
    /// owns the blackout owns the swap -- see <see cref="lightingLook"/>. Putting it on a track
    /// of its own would be the same argument as three abutting fade clips: two things that have
    /// to agree on one instant, authored separately, free to drift apart. A frame of S2's fog on
    /// S1's room is the same mistake as a frame of the next section arriving early.
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

        [Tooltip("The LightingDirector look to switch to behind this fade -- 'S2' on the " +
                 "blackout that ends S1. Empty means this clip does not touch the lighting, " +
                 "which is what most fades want.")]
        public string lightingLook = "";

        [Tooltip("The look that holds BEFORE the switch point, usually the section being left.\n\n" +
                 "Filling this in is what makes the switch scrubbable: with both names the " +
                 "lighting is a function of show time like the alpha is, so dragging the " +
                 "playhead back through the blackout puts the old room's lighting back. Left " +
                 "empty, the first half of the clip simply asserts nothing and a backwards " +
                 "scrub keeps the new lighting.")]
        public string lightingLookBefore = "";

        [Range(0f, 1f)]
        [Tooltip("Where in the clip the lighting changes, 0 to 1. The default of 0.5 is the " +
                 "middle, which on an out-hold-in curve is the darkest part of the hold.\n\n" +
                 "It wants to be somewhere alpha is 1. The switch itself is a single frame and " +
                 "cannot be smoothed -- fog, ambient and a whole lightmap array change at once " +
                 "-- so the only thing hiding it is the blackout being complete.")]
        public float lightingSwitchAt = 0.5f;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            double length = playable.GetDuration();
            float t = length > 0.0 ? (float)(playable.GetTime() / length) : 1f;

            ScreenFade fade = ScreenFade.Instance;
            if (fade != null)
            {
                fade.SetFadeColor(color);

                // Zero seconds means snap: the timeline is the clock, so the value is set, never
                // animated towards. Animating here would be two clocks driving one number.
                fade.FadeTo(Mathf.Clamp01(alpha.Evaluate(Mathf.Clamp01(t))), 0f);
            }

            // Not inside the block above: there is no fade quad outside play mode, and the
            // lighting is not the quad's business anyway.
            ApplyLighting(t);
        }

        /// <summary>
        /// Put the lighting where this clip says it should be at <paramref name="t"/>.
        ///
        /// A FUNCTION OF SHOW TIME, like the alpha, not an event fired once on the way past.
        /// Which look is wanted is read fresh from the playhead every frame and handed to the
        /// director, which ignores it when it is already the current one. So a scrub, a jump, a
        /// late joiner and a hold clearing all land on the right lighting for where the playhead
        /// actually is, and none of them can leave a room wearing the next room's fog -- which
        /// an event-on-crossing would do the moment the playhead moved backwards.
        /// </summary>
        void ApplyLighting(float t)
        {
            if (string.IsNullOrEmpty(lightingLook)) return;

            // Editor only previews; carriers rewrite the global lightmap array and toggle
            // section roots, and neither belongs in a scene being authored.
            if (!Application.isPlaying) return;

            WarnIfSwitchIsVisible();

            string wanted = t >= lightingSwitchAt ? lightingLook : lightingLookBefore;
            if (string.IsNullOrEmpty(wanted)) return;

            LightingDirector.Apply(wanted);
        }

        /// <summary>
        /// Static, because this is reached from ProcessFrame and would otherwise repeat for
        /// every frame of every run. One warning per session, which does mean a second badly
        /// placed clip stays quiet until the first is fixed -- the message names the look it is
        /// switching to, so the one it does report can be found.
        /// </summary>
        static bool s_WarnedSwitchIsVisible;

        /// <summary>
        /// Say so when the switch is set to happen while the screen is not actually covered.
        ///
        /// This is the one way to get this wrong that still looks like a rendering bug rather
        /// than a timing mistake: the room changes its lighting in full view, for one frame, and
        /// the obvious suspect is the bake.
        /// </summary>
        void WarnIfSwitchIsVisible()
        {
            if (s_WarnedSwitchIsVisible) return;

            float cover = alpha.Evaluate(Mathf.Clamp01(lightingSwitchAt));
            if (cover >= 0.99f) return;

            s_WarnedSwitchIsVisible = true;
            Debug.LogWarning(
                $"[ScreenFade] the lighting switch to '{lightingLook}' is set at {lightingSwitchAt:0.##} " +
                $"of the clip, where this clip's alpha is only {cover:0.##}. The change of fog, " +
                "ambient and lightmaps happens on one frame and will be visible. Move the " +
                "switch point into the flat part of the curve. Said once; it will not repeat.");
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
            // Only when the clip has actually run to its end -- a graph stopped mid-clip, which
            // is what a scrub away or a stop looks like, must not slam the screen to black.
            if (playable.GetTime() < playable.GetDuration() - 0.001) return;

            ScreenFade fade = ScreenFade.Instance;
            if (fade != null) fade.FadeTo(Mathf.Clamp01(alpha.Evaluate(1f)), 0f);

            // The same rounding that leaves the alpha a percent short can leave the playhead
            // short of the switch point on a clip that switches late, and then the blackout
            // lifts on the old lighting. Settle it on the end state for the same reason.
            ApplyLighting(1f);
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
