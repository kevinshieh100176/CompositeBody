using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CompositeBody.PointClouds
{
    /// <summary>
    /// A track whose binding is the 真人 being dissolved, one track per figure. See
    /// <see cref="FigureRoleDissolveBehaviour"/> for what a clip on it means.
    ///
    /// Bound rather than found by name or reached through a singleton: S1-1 has two of these and
    /// the show has more figures elsewhere, and the binding is what makes the Timeline window
    /// say which cloud a row belongs to while it is being authored.
    ///
    /// IN ITS OWN FILE, NAMED AFTER IT, AND IT HAS TO BE -- see
    /// <see cref="FigurePresenceTrack"/> for what goes wrong otherwise. This is the track it
    /// went wrong on: S1-1's two staggered figures were authored, verified, saved, and then
    /// quietly absent the next time the project opened.
    /// </summary>
    [TrackColor(0.47f, 0.76f, 0.85f)]
    [TrackClipType(typeof(FigureRoleDissolveClip))]
    [TrackBindingType(typeof(PointCloudFigure))]
    public class FigureRoleDissolveTrack : TrackAsset
    {
        /// <summary>
        /// Tell Timeline that scrubbing this track writes the figure's dissolve, so it snapshots
        /// the authored value before a preview and puts it back afterwards.
        ///
        /// Without this, authoring leaks: the clip writes a plain component field, which does not
        /// dirty the scene but does leave the last previewed frame sitting in memory, and the next
        /// save would commit whatever moment the playhead happened to be parked on. Doing the
        /// save-and-restore by hand inside the clip looks equivalent and is not -- a rebuilt graph
        /// re-captures from a value a previous preview already wrote, so repeated scrubbing
        /// gradually bakes the preview in. Timeline's own preview system takes its snapshot from
        /// the serialized object before any of that, which is the only place the authored value is
        /// still the authored value.
        /// </summary>
        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
#if UNITY_EDITOR
            if (director != null && director.GetGenericBinding(this) is PointCloudFigure figure)
                driver.AddFromName<PointCloudFigure>(figure.gameObject, "m_Dissolve");
#endif
            base.GatherProperties(director, driver);
        }
    }
}
