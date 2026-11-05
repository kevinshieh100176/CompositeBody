using System;
using UnityEngine;
using UnityEngine.Playables;

namespace CompositeBody.Experience
{
    /// <summary>
    /// One end card's fade, authored as a block on the show timeline.
    ///
    /// Same argument as <see cref="ScreenFadeClip"/>, and for the same reason: the show is pulled
    /// onto a server-owned clock by <see cref="ShowTimelineDriver"/>, which trims playback speed
    /// and jumps outright for real discontinuities. A coroutine fading a card would keep its own
    /// clock through all of that and the two headsets would be showing different parts of the
    /// credits. Here the alpha is a FUNCTION OF SHOW TIME, computed fresh every frame from where
    /// the playhead sits inside this clip, so a scrub, a jump and a late joiner all land on the
    /// same value -- and so does the editor, which is what makes the end scrubbable like the
    /// rest of the piece.
    ///
    /// THE CURVE IS THE FADE, again: one clip holds the whole in-hold-out of a card rather than
    /// three abutting ones that can develop gaps between them.
    ///
    /// Unlike the screen fade, the end value is NOT held here -- nothing drives a card outside
    /// its own clip, so a card is simply at whatever it was last set to. Both curves therefore
    /// start and end at 0, which leaves the screen on clean white between the two cards and
    /// after the second.
    /// </summary>
    [Serializable]
    public class CreditsCardBehaviour : PlayableBehaviour
    {
        [Tooltip("Which of the two end cards this clip drives.")]
        public CreditsCard card = CreditsCard.Title;

        [Tooltip("Alpha across the clip: 0 is gone, 1 is full, time runs 0 to 1 from the clip's " +
                 "start to its end.\n\n" +
                 "A card's whole life is one clip -- rise, hold, fall. Start and end at 0: " +
                 "nothing drives a card outside its clip, so a curve that ends high leaves the " +
                 "card on screen for the rest of the show.")]
        public AnimationCurve alpha = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            CreditsCards cards = CreditsCards.Instance;
            if (cards == null) return;

            double length = playable.GetDuration();
            float t = length > 0.0 ? (float)(playable.GetTime() / length) : 1f;

            cards.SetAlpha(card, Mathf.Clamp01(alpha.Evaluate(Mathf.Clamp01(t))));
        }

        /// <summary>
        /// Settle on the end value when the clip finishes.
        ///
        /// The last ProcessFrame lands wherever the frame happened to fall, which at 72 Hz can
        /// be a percent or so short of the end. A card left at alpha 0.03 is a faint ghost of
        /// the credits sitting on the white for the rest of the show, and nothing comes along
        /// afterwards to clear it.
        /// </summary>
        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            // Only once the clip has actually run out. A graph stopped mid-clip -- a scrub away,
            // a stop, exiting play mode -- must not snap the card to its end value.
            if (playable.GetTime() < playable.GetDuration() - 0.001) return;

            CreditsCards cards = CreditsCards.Instance;
            if (cards != null) cards.SetAlpha(card, Mathf.Clamp01(alpha.Evaluate(1f)));
        }
    }

    /// <summary>A <see cref="CreditsCardBehaviour"/> as a clip that can be dropped on a track.</summary>
    public class CreditsCardClip : PlayableAsset
    {
        public CreditsCardBehaviour template = new();

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) =>
            ScriptPlayable<CreditsCardBehaviour>.Create(graph, template);
    }
}
