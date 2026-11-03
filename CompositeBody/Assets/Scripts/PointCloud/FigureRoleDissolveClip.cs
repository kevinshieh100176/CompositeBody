using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CompositeBody.Multiplayer;

namespace CompositeBody.PointClouds
{
    /// <summary>
    /// A 真人's coming and going, authored as one block on a beat's timeline, with the two
    /// players seeing it at different times.
    ///
    /// S1-1 asks for a stagger: each player's own 真人 is already there when the beat opens and
    /// is the last to leave, and the other player's arrives three quarters of a minute later and
    /// goes first. Both headsets run the same show at the same show time, so the asymmetry
    /// cannot live in WHEN the clip plays -- it lives in which of two authored schedules the
    /// clip reads, and that is decided locally from <see cref="PlayerRole"/>, the same way every
    /// <see cref="LocalRolePresenter"/> in the piece decides what to draw. Nothing is replicated
    /// and nothing can get out of step: each machine reads its own column of the same table.
    ///
    /// WHY ONE CLIP AND NOT TWO SETS OF TRACKS. The obvious alternative is four animation tracks
    /// -- an in and an out per player -- with two of them muted on each headset. Mute state is
    /// on the asset, so it would have to be flipped at runtime and the graph rebuilt, and worse,
    /// the authored timeline would then show a thing that is never true for anybody. Here both
    /// columns sit on one clip in the inspector, next to each other, which is also the only
    /// place the stagger is legible as a stagger.
    ///
    /// A FUNCTION OF SHOW TIME, like <see cref="CompositeBody.Experience.ScreenFadeBehaviour"/>
    /// and for the same reason: <c>ShowTimelineDriver</c> keeps this show's directors pulled onto
    /// a server-owned clock and will trim, jump and re-seek them. A dissolve started as a
    /// fire-and-forget tween -- which is what <see cref="PointCloudFigure.DissolveOver"/> is --
    /// would carry on through all of that and land somewhere different on each headset. So the
    /// amount is recomputed every frame from where the playhead sits, which also means a scrub
    /// in the Timeline window shows the truth.
    ///
    /// THE DISSOLVE RUNS BACKWARDS TO BRING THE FIGURE IN. _Dissolve 1 -> 0 is the exit played
    /// in reverse: points gather back out of the air they drifted into, growing as they arrive.
    /// That is a better "dissolve in" than _Reveal, which with a radius of 0 is a pop and with a
    /// radius is a hard shell creeping over the body -- and it costs nothing extra, because the
    /// shape, the drift and the direction are already authored per figure on the component.
    /// </summary>
    [Serializable]
    public class FigureRoleDissolveBehaviour : PlayableBehaviour
    {
        [Tooltip("Whose 真人 this is. Player1 is 玩家A (湖水綠), Player2 is 玩家B.\n\n" +
                 "This is the whole of the asymmetry: the machine whose local role matches " +
                 "reads the 'owner' column below, everyone else reads the 'other' column.")]
        public PlayerRole figure = PlayerRole.Player1;

        [Header("Seen by its own player")]
        [Tooltip("Seconds from the start of this clip at which the figure starts to gather, on " +
                 "the headset of the player it belongs to. S1-1: their own 真人 is forming as " +
                 "the beat opens.")]
        public float ownerAppearsAt;

        [Tooltip("Seconds from the start of this clip at which it starts to come apart again, " +
                 "on its own player's headset. S1-1: 02:50, the last of the two to go.")]
        public float ownerLeavesAt = 170f;

        [Header("Seen by the other player")]
        [Tooltip("Seconds from the start of this clip at which the figure starts to gather on " +
                 "the OTHER player's headset. S1-1: 00:45.")]
        public float otherAppearsAt = 45f;

        [Tooltip("Seconds from the start of this clip at which it starts to come apart on the " +
                 "other player's headset. S1-1: 02:38, before their own goes.")]
        public float otherLeavesAt = 158f;

        [Header("How long each takes")]
        [Min(0.01f)]
        [Tooltip("Seconds for the gathering. Both players get the same length; only the moment " +
                 "it starts differs.")]
        public float appearSeconds = 8f;

        [Min(0.01f)]
        [Tooltip("Seconds for the coming apart.")]
        public float leaveSeconds = 8f;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (playerData is not PointCloudFigure cloud) return;

            cloud.dissolve = DissolveAt((float)playable.GetTime());
        }

        /// <summary>
        /// 0 whole, 1 entirely gone, at <paramref name="t"/> seconds into the clip, for the role
        /// this machine is rendering as.
        ///
        /// Public and pure so a test can assert the stagger without a director, a session or a
        /// second headset -- which is the only way to check a thing whose entire content is that
        /// two machines disagree.
        /// </summary>
        public float DissolveAt(float t) => DissolveAt(t, ReadsOwnerColumn());

        /// <summary>As above, for an explicitly chosen column.</summary>
        public float DissolveAt(float t, bool asOwner)
        {
            float appearsAt = asOwner ? ownerAppearsAt : otherAppearsAt;
            float leavesAt = asOwner ? ownerLeavesAt : otherLeavesAt;

            // Before its cue: fully absent, which PointCloudFigure answers by switching the
            // renderer off, so the forty-five seconds before the other figure arrives cost
            // nothing rather than rasterising seven hundred thousand points that are all discarded.
            if (t <= appearsAt) return 1f;

            if (t < appearsAt + appearSeconds)
                return 1f - Smooth((t - appearsAt) / Mathf.Max(0.01f, appearSeconds));

            if (t <= leavesAt) return 0f;

            if (t < leavesAt + leaveSeconds)
                return Smooth((t - leavesAt) / Mathf.Max(0.01f, leaveSeconds));

            return 1f;
        }

        /// <summary>
        /// True when this machine is the one the figure belongs to -- or when there is no role
        /// to go on at all.
        ///
        /// The unknown case is the owner column deliberately: it is the earliest arrival and the
        /// latest departure, so the figure is present for the union of the two schedules. In the
        /// editor with no session, and in the seconds before the roles replicate, that shows the
        /// beat with both 真人 in it. Picking the other column instead would make a scene that is
        /// merely not connected look like a scene with a figure missing -- the same call
        /// <see cref="CompositeBody.Experience.O0FigureRoleView"/> makes when it shows both.
        /// </summary>
        bool ReadsOwnerColumn() =>
            !LocalRolePresenter.TryGetViewRole(out PlayerRole role) || role == figure;

        /// <summary>Smoothstep, so the gathering eases in rather than lurching on the cue frame.</summary>
        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }

    /// <summary>A <see cref="FigureRoleDissolveBehaviour"/> as a clip that can go on a track.</summary>
    public class FigureRoleDissolveClip : PlayableAsset, ITimelineClipAsset
    {
        public FigureRoleDissolveBehaviour template = new();

        /// <summary>
        /// No blending and no looping. Two of these overlapping would be two things writing one
        /// float, and the clip is already a whole in-hold-out rather than a shape to cross-fade.
        /// </summary>
        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) =>
            ScriptPlayable<FigureRoleDissolveBehaviour>.Create(graph, template);
    }

}
