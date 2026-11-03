using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Reads the track's clips against show time and writes the one number they describe.
    ///
    /// The clips are baked into a flat list when the graph is built rather than read off the
    /// asset each frame, because the asset's clip list allocates and this runs every frame of a
    /// thirty-eight minute show.
    /// </summary>
    public class PropPresenceMixer : PlayableBehaviour
    {
        public struct Block
        {
            public double start;
            public double duration;
            public PropPresenceBehaviour shape;
        }

        /// <summary>The track's clips in start order.</summary>
        public Block[] blocks = Array.Empty<Block>();

        /// <summary>What the prop is before the first clip -- normally fully here.</summary>
        public float beforeFirst = 1f;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (playerData is not PropPresence prop) return;

            double now = playable.GetTime();
            float value = beforeFirst;

            // Last clip that has started wins, and a clip that has finished still answers --
            // clamped to its end value. That is what holds a dissolved wall dissolved.
            foreach (Block block in blocks)
            {
                if (now < block.start) break;
                value = block.shape.PresenceAt(now - block.start, block.duration);
            }

            prop.presence = value;
        }
    }

    /// <summary>
    /// A track whose binding is the prop group being moved, one track per group. See
    /// <see cref="PropPresenceBehaviour"/> for what a clip on it means and why the mixer rather
    /// than the clip does the work.
    ///
    /// IN ITS OWN FILE, NAMED AFTER IT, AND IT HAS TO BE -- see
    /// <see cref="CompositeBody.PointClouds.FigurePresenceTrack"/> for what goes wrong
    /// otherwise, which is a track that works until the editor is next opened and is then
    /// silently gone.
    /// </summary>
    [TrackColor(0.85f, 0.62f, 0.40f)]
    [TrackClipType(typeof(PropPresenceClip))]
    [TrackBindingType(typeof(PropPresence))]
    public class PropPresenceTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var blocks = new System.Collections.Generic.List<PropPresenceMixer.Block>();

            foreach (TimelineClip clip in GetClips())
            {
                if (clip.asset is not PropPresenceClip asset) continue;
                blocks.Add(new PropPresenceMixer.Block
                {
                    start = clip.start,
                    duration = clip.duration,
                    shape = asset.template,
                });
            }

            blocks.Sort((a, b) => a.start.CompareTo(b.start));

            ScriptPlayable<PropPresenceMixer> mixer =
                ScriptPlayable<PropPresenceMixer>.Create(graph, inputCount);
            mixer.GetBehaviour().blocks = blocks.ToArray();
            return mixer;
        }

        /// <summary>
        /// Tell Timeline that scrubbing this track writes the prop's presence, so it snapshots
        /// the authored value before a preview and puts it back afterwards -- the same reason
        /// <see cref="CompositeBody.PointClouds.FigureRoleDissolveTrack"/> does it. Without this
        /// the scene quietly keeps whichever moment the playhead was last parked on, and the
        /// next save commits it.
        /// </summary>
        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
#if UNITY_EDITOR
            if (director != null && director.GetGenericBinding(this) is PropPresence prop)
                driver.AddFromName<PropPresence>(prop.gameObject, "m_Presence");
#endif
            base.GatherProperties(director, driver);
        }
    }
}
