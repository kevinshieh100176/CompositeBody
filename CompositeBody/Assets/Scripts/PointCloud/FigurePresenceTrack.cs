using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CompositeBody.PointClouds
{
    /// <summary>
    /// Reads the track's clips against show time and writes the one number they describe.
    ///
    /// The clips are baked into a flat list when the graph is built rather than read off the
    /// asset each frame, because the asset's clip list allocates and this runs every frame of a
    /// thirty-eight minute show.
    /// </summary>
    public class FigurePresenceMixer : PlayableBehaviour
    {
        public struct Block
        {
            public double start;
            public double duration;
            public FigurePresenceBehaviour shape;
        }

        /// <summary>The track's clips in start order.</summary>
        public Block[] blocks = Array.Empty<Block>();

        /// <summary>What the cloud is before the first clip.</summary>
        public float beforeFirst;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (playerData is not PointCloudFigure cloud) return;

            double now = playable.GetTime();
            float value = beforeFirst;

            // Last clip that has started wins, and a clip that has finished still answers --
            // clamped to its end value. That is what keeps a figure that has gone, gone.
            foreach (Block block in blocks)
            {
                if (now < block.start) break;
                value = block.shape.PresenceAt(now - block.start, block.duration);
            }

            cloud.dissolve = 1f - value;
        }
    }

    /// <summary>
    /// A track whose binding is the cloud being cued, one track per cloud. See
    /// <see cref="FigurePresenceBehaviour"/> for what a clip on it means and why the mixer
    /// rather than the clip does the work.
    ///
    /// IN ITS OWN FILE, NAMED AFTER IT, AND IT HAS TO BE. A TrackAsset is a ScriptableObject,
    /// and Unity only makes a MonoScript for the one type in a file whose name matches the
    /// file's. A track class sitting in a file named after its clip gets no MonoScript, so
    /// every track of it serializes with <c>m_Script: {fileID: 0}</c> -- which works perfectly
    /// for the rest of the session that created it, and then loads as a null sub-asset the next
    /// time the editor opens. The timeline comes back with the track silently gone: no error,
    /// no missing-script warning, just a beat that no longer does anything. This project lost
    /// S1-1's whole staggered dissolve that way once. Any new TrackAsset goes in its own file.
    /// </summary>
    [TrackColor(0.47f, 0.76f, 0.85f)]
    [TrackClipType(typeof(FigurePresenceClip))]
    [TrackBindingType(typeof(PointCloudFigure))]
    public class FigurePresenceTrack : TrackAsset
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Presence before the first clip on this track. 0 -- absent until cued -- is " +
                 "almost always what is wanted, and is why this defaults the opposite way round " +
                 "to the prop track: a cloud only gets one of these tracks because its arrival " +
                 "is being authored, whereas a wall is standing there to begin with.\n\n" +
                 "The authored value on the component is left alone either way, so the figure " +
                 "stays visible in the scene view while it is being placed. This is what the " +
                 "show does with it, not what the scene looks like.")]
        float m_PresenceBeforeFirstClip;

        /// <summary>Presence before the first clip on this track.</summary>
        public float presenceBeforeFirstClip
        {
            get => m_PresenceBeforeFirstClip;
            set => m_PresenceBeforeFirstClip = Mathf.Clamp01(value);
        }

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var blocks = new System.Collections.Generic.List<FigurePresenceMixer.Block>();

            foreach (TimelineClip clip in GetClips())
            {
                if (clip.asset is not FigurePresenceClip asset) continue;
                blocks.Add(new FigurePresenceMixer.Block
                {
                    start = clip.start,
                    duration = clip.duration,
                    shape = asset.template,
                });
            }

            blocks.Sort((a, b) => a.start.CompareTo(b.start));

            ScriptPlayable<FigurePresenceMixer> mixer =
                ScriptPlayable<FigurePresenceMixer>.Create(graph, inputCount);
            FigurePresenceMixer behaviour = mixer.GetBehaviour();
            behaviour.blocks = blocks.ToArray();
            behaviour.beforeFirst = m_PresenceBeforeFirstClip;
            return mixer;
        }

        /// <summary>
        /// Tell Timeline that scrubbing this track writes the cloud's dissolve, so it snapshots
        /// the authored value before a preview and puts it back afterwards -- the same reason
        /// <see cref="FigureRoleDissolveTrack"/> and
        /// <see cref="CompositeBody.Experience.PropPresenceTrack"/> do it. Without this the
        /// scene quietly keeps whichever moment the playhead was last parked on, and the next
        /// save commits it.
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
