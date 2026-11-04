using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CompositeBody.Interaction.ObjectAssembly
{
    /// <summary>
    /// The point on OA_Phase0.playable where the two halves start to appear.
    ///
    /// READ FROM THE ASSET, NOT DELIVERED AS A NOTIFICATION, for the reason
    /// <see cref="CompositeBody.Experience.ShowHoldMarker"/> gives: the director is pulled onto the
    /// shared clock and may jump, so a signal could be dropped or doubled on one headset.
    /// <see cref="OAController"/> reads this marker's time once and compares it with the clock,
    /// which gives the same answer on both machines and for a late joiner.
    ///
    /// Move it in the Timeline window to retime the appearance against the Phase 0 voice-over.
    /// </summary>
    [Serializable]
    public class OAAppearMarker : Marker, INotification
    {
        public PropertyName id => new("oa.appear");
    }
}
