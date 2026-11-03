using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CompositeBody.Experience
{
    /// <summary>
    /// A prop group arriving or leaving, authored as a block on a show timeline.
    ///
    /// <see cref="PropPresence"/> already turns one number into a renderer coming apart; this is
    /// how that number gets onto the timeline without an Animator. The alternative its own
    /// summary suggests -- an AnimationTrack writing <c>m_Presence</c> -- is what the O-1 props
    /// use, and it is the right answer there because that whole group already hangs off one
    /// Animator. The S1 walls do not: they are a lightmapped static model, and hanging an
    /// Animator and a clip asset on one of its children to move a single float is more machinery
    /// than the float is worth.
    ///
    /// A FUNCTION OF SHOW TIME, like <see cref="ScreenFadeBehaviour"/> and
    /// <see cref="CompositeBody.PointClouds.FigureRoleDissolveBehaviour"/>, and for the same
    /// reason: <see cref="ShowTimelineDriver"/> keeps the director pulled onto a server-owned
    /// clock and will trim it, jump it and re-seek it. Presence is therefore recomputed every
    /// frame from where the playhead sits, never stepped towards -- so a scrub, a jump, a hold
    /// clearing and a late joiner all land on the same wall.
    ///
    /// THE MIXER IS WHERE THAT HAPPENS, not the clip. A clip only runs while the playhead is
    /// inside it, which is exactly wrong for a thing whose whole point is the state it leaves
    /// behind: a backwards jump past a dissolve would land outside every clip with nothing to
    /// put the wall back, and a forwards jump over one would skip it and leave the wall standing
    /// in the next section. The track mixer runs for the whole timeline, so it can answer at any
    /// time -- before the first clip the authored presence, inside a clip its curve, after a
    /// clip the value that clip ended on.
    /// </summary>
    [Serializable]
    public class PropPresenceBehaviour : PlayableBehaviour
    {
        [Tooltip("Presence across the clip: 0 absent, 1 fully here, time running 0 to 1 from " +
                 "the clip's start to its end.\n\n" +
                 "THE CURVE IS THE MOVE. A curve that falls is a dissolve out, one that rises " +
                 "is an arrival, and one that does both is a whole blink in a single clip. The " +
                 "end value is held until the next clip on this track, so a dissolve out needs " +
                 "nothing after it to keep the prop gone.")]
        public AnimationCurve presence = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        /// <summary>Presence at <paramref name="t"/> seconds into a clip of <paramref name="duration"/>.</summary>
        public float PresenceAt(double t, double duration)
        {
            float u = duration > 0.0 ? (float)(t / duration) : 1f;
            return Mathf.Clamp01(presence.Evaluate(Mathf.Clamp01(u)));
        }
    }

    /// <summary>A <see cref="PropPresenceBehaviour"/> as a clip that can go on a track.</summary>
    public class PropPresenceClip : PlayableAsset, ITimelineClipAsset
    {
        public PropPresenceBehaviour template = new();

        /// <summary>
        /// No blending and no looping. Two of these overlapping would be two things writing one
        /// float, and the clip is already a whole shape rather than a pose to cross-fade.
        /// </summary>
        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) =>
            ScriptPlayable<PropPresenceBehaviour>.Create(graph, template);
    }
}
