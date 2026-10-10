using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CompositeBody.Experience
{
    /// <summary>
    /// A point on the show timeline where the show stops and waits for the players.
    ///
    /// The gated beats -- O-1, O-2, O-3 -- end when BOTH players have done the thing, not after
    /// an authored number of seconds. A fixed-length clip cannot say that, so the waiting is
    /// expressed as a marker instead: the timeline runs up to the marker and holds there until
    /// <see cref="taskId"/> is reported complete by <c>StoryProgressManager</c>, then carries on.
    ///
    /// READ FROM THE ASSET, NOT DELIVERED AS A NOTIFICATION. Timeline's own signal delivery
    /// fires while a graph is playing, and this show's directors never play -- they are evaluated
    /// at a time computed from a shared clock (see <see cref="ShowTimelineDriver"/>). A dropped or
    /// doubled notification would leave one headset holding and the other running. So the driver
    /// reads these markers off the asset and compares them against the clock, which gives the same
    /// answer on both machines, in any order, including for someone who joined late.
    ///
    /// <see cref="INotification"/> is implemented anyway so the marker still shows up as a signal
    /// to anything that does listen, and so it reads as a signal in the Timeline window.
    /// </summary>
    [Serializable]
    public class ShowHoldMarker : Marker, INotification
    {
        [SerializeField, Tooltip("The StoryProgressManager task that has to be complete before " +
                                 "the show moves past this point, e.g. beat.O1_Control. Empty " +
                                 "means never hold, which is how you mute a gate without " +
                                 "deleting the marker.")]
        string m_TaskId;

        /// <summary>The gate this hold waits on. Empty means the marker does nothing.</summary>
        public string taskId => m_TaskId;

        /// <summary>Set by the builder; the Timeline window edits the serialized field directly.</summary>
        public void SetTaskId(string value) => m_TaskId = value;

        /// <summary>True when this marker actually gates something.</summary>
        public bool gates => !string.IsNullOrWhiteSpace(m_TaskId);

        public PropertyName id => new PropertyName(m_TaskId);
    }
}
