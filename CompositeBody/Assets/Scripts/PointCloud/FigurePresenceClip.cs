using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CompositeBody.PointClouds
{
    /// <summary>
    /// A point cloud arriving and leaving on a cue, authored as a block on a show timeline and
    /// seen the same way by both players.
    ///
    /// WHY NOT <see cref="FigureRoleDissolveTrack"/>, which also dissolves a 真人. That one is a
    /// two-column table -- a stagger, where the whole content is that the two headsets disagree
    /// about when a figure is there. Most of the show is not that: S1-4's three 真人 flash up
    /// together for both players, and authoring them through the role track would mean filling
    /// the owner and other columns with the same four numbers and leaving a reader to work out
    /// that nothing is actually staggered. Here the cue is a curve, and a curve is also the
    /// thing an operator wants to reshape when ten seconds turns out to be eight.
    ///
    /// A FUNCTION OF SHOW TIME, like <see cref="CompositeBody.Experience.ScreenFadeBehaviour"/>
    /// and <see cref="CompositeBody.Experience.PropPresenceBehaviour"/>, and for the same
    /// reason: <c>ShowTimelineDriver</c> keeps the director pulled onto a server-owned clock and
    /// will trim it, jump it and re-seek it. Presence is recomputed every frame from where the
    /// playhead sits, never stepped towards.
    ///
    /// THE MIXER IS WHERE THAT HAPPENS, not the clip -- see
    /// <see cref="FigurePresenceMixer"/>, copied from
    /// <see cref="CompositeBody.Experience.PropPresenceTrack"/> because the argument is
    /// identical, and it is the whole reason this is not a second role-dissolve track. A clip
    /// only runs while the playhead is inside it, which is exactly wrong for a cue whose point
    /// is the state it leaves behind: these figures stand in the scene fully formed while they
    /// are being placed, so with nothing answering before the first clip they would be in the
    /// room for the four minutes of S1 before their cue, and a backwards jump past the cue
    /// would put them back. The track mixer runs for the whole timeline, so it can answer at any
    /// time -- before the first clip <see cref="FigurePresenceTrack.presenceBeforeFirstClip"/>,
    /// inside a clip its curve, after a clip the value that clip ended on.
    ///
    /// PRESENCE RATHER THAN DISSOLVE, even though the number it ends up writing is
    /// <c>_Dissolve</c>. The curve reads the way the beat reads -- up is an arrival, down is a
    /// departure -- which is worth one subtraction in the mixer. Dissolve is the inversion
    /// because that is the property the cloud shader actually has a shape for: the points gather
    /// back out of the air they drifted into, growing as they arrive, which is a better arrival
    /// than <c>_Reveal</c>, whose radius is 0 on every one of these materials and is therefore a
    /// pop.
    /// </summary>
    [Serializable]
    public class FigurePresenceBehaviour : PlayableBehaviour
    {
        [Tooltip("Presence across the clip: 0 absent, 1 fully here, time running 0 to 1 from the " +
                 "clip's start to its end.\n\n" +
                 "THE CURVE IS THE CUE. One that rises is an arrival, one that falls is a " +
                 "departure, and one that does both is a whole apparition in a single clip -- " +
                 "which is what S1-4's 真人 are. The end value is held until the next clip on " +
                 "this track, so a figure that leaves needs nothing after it to stay gone.")]
        public AnimationCurve presence = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        /// <summary>Presence at <paramref name="t"/> seconds into a clip of <paramref name="duration"/>.</summary>
        public float PresenceAt(double t, double duration)
        {
            float u = duration > 0.0 ? (float)(t / duration) : 1f;
            return Mathf.Clamp01(presence.Evaluate(Mathf.Clamp01(u)));
        }
    }

    /// <summary>A <see cref="FigurePresenceBehaviour"/> as a clip that can go on a track.</summary>
    public class FigurePresenceClip : PlayableAsset, ITimelineClipAsset
    {
        public FigurePresenceBehaviour template = new();

        /// <summary>
        /// No blending and no looping. Two of these overlapping would be two things writing one
        /// float, and the clip is already a whole shape rather than a pose to cross-fade.
        /// </summary>
        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) =>
            ScriptPlayable<FigurePresenceBehaviour>.Create(graph, template);
    }
}
