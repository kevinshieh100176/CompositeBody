using System;
using UnityEngine;

namespace CompositeBody.Avatar.Membrane
{
    /// <summary>
    /// A single breakable piece of a <see cref="MembraneCocoonGenerator"/> web. It's physically
    /// attached to the cocoon's anchor via a FixedJoint; grabbing it with a VR hand
    /// (movementType = VelocityTracking, so the hand pulls it with real force) and yanking hard
    /// enough exceeds the joint's breakForce, and Unity's physics engine tears it free on its
    /// own -- this is genuine physics, not a scripted/shader illusion.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MembranePanel : MonoBehaviour
    {
        // A panel can carry multiple joints -- its anchor FixedJoint plus one or two neighbor
        // SpringJoints for the strand network. Unity's OnJointBreak message doesn't say which
        // joint broke, so we track the anchor specifically: the panel only counts as "torn free"
        // once THAT one is gone, not when a neighboring strand snaps.
        Joint m_AnchorJoint;

        public bool isTorn { get; private set; }

        /// <summary>Fired once, the moment this panel tears free of its anchor to the body.</summary>
        public event Action<MembranePanel> onTorn;

        public void SetAnchorJoint(Joint anchorJoint)
        {
            m_AnchorJoint = anchorJoint;
        }

        void OnJointBreak(float breakForce)
        {
            if (isTorn) return;
            if (m_AnchorJoint != null) return; // a neighbor strand broke, not the anchor

            isTorn = true;
            onTorn?.Invoke(this);
        }
    }
}
