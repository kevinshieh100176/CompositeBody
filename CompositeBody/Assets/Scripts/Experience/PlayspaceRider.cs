using System.Collections;
using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Puts an avatar inside the playspace, so that what travels over the network is where the
    /// player is IN the room rather than where the room has got to.
    ///
    /// WHY THIS IS NEEDED AT ALL. S1 moves the playspace, and quickly. The avatar's head and
    /// hands replicate their positions; if those are world positions, every one of them that
    /// arrives is a position from one trip's worth of latency ago, while the floor it refers to
    /// has moved on since. The other player then trails behind the floor they are standing on by
    /// speed times latency -- a couple of millimetres when the room drifts, a visible slide when
    /// it moves fast, and worse the faster it goes.
    ///
    /// Parented here and replicating in local space, the numbers on the wire say "half a metre
    /// to the left of centre", which is as true a frame later as it was when it was sent. The
    /// motion of the room itself is not sent at all: both machines derive it from the same
    /// timeline on the same server-owned clock, so it is already identical on both.
    ///
    /// PARENTED, NOT FOLLOWED. A component could instead copy the playspace's pose onto this
    /// transform every frame, and it would be nearly right -- but only nearly, because it would
    /// be a frame behind whenever the two updated in the wrong order, and a frame at 72 Hz with
    /// the room moving several metres a second is centimetres. A child's transform is derived
    /// from its parent's current one with no ordering to get wrong.
    ///
    /// DONE LOCALLY ON EVERY CLIENT, not replicated. AutoObjectParentSync is off on the avatar
    /// for this reason: Netcode replicates a parent by its NetworkObject, the playspace is a
    /// plain scene object and has none, and it does not need one -- the playspace is in the
    /// scene on every machine, so every machine can do this for itself and arrive at the same
    /// answer. That includes the avatars of other players, which is the point: their local
    /// coordinates have to be read against the same playspace they were written against.
    /// </summary>
    /// <remarks>
    /// Runs BEFORE XRINetworkPlayer, which has no execution order of its own and therefore sits
    /// at zero. That ordering is the whole of the fix: XRINetworkPlayer places the head at the
    /// rig camera's WORLD pose in its LateUpdate, and a parent moved after that drags the head
    /// with it. Pinning first leaves the head to be placed last, which is the one that has to be
    /// right. Getting this backwards broke OwnerHeadFollowsTheCamera, which is in the suite
    /// precisely because the failure is invisible on the machine that causes it.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class PlayspaceRider : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("How long to keep looking for the playspace before " +
                                          "giving up and saying so. An avatar can spawn before " +
                                          "the scene it belongs in has finished loading.")]
        float m_Patience = 10f;

        IEnumerator Start()
        {
            float deadline = Time.unscaledTime + m_Patience;

            while (Playspace.Instance == null)
            {
                if (Time.unscaledTime > deadline)
                {
                    Debug.LogWarning($"[PlayspaceRider] {name} found no Playspace in {m_Patience:0}s, " +
                                     "so it stays at the scene root. It will still be in the right " +
                                     "place while the playspace is at the origin, and will be left " +
                                     "behind the moment the show moves it. Check that the " +
                                     "Playspace component is on the playspace object.", this);
                    yield break;
                }
                yield return null;
            }

            transform.SetParent(Playspace.Instance.transform, worldPositionStays: false);
            m_Riding = true;
            Pin();
        }

        bool m_Riding;

        /// <summary>
        /// Sit exactly on the playspace, so a head's local position IS its position in the room.
        ///
        /// HELD EVERY FRAME, not set once. Doing it once was wrong and the way it was wrong was
        /// invisible until it was catastrophic: the avatar's NetworkObject has
        /// SynchronizeTransform on, so Netcode writes this transform after spawn, and with
        /// AutoObjectParentSync off -- which it must be, since the playspace is a plain scene
        /// object with no NetworkObject -- what it writes is a world position landing in a local
        /// one. The root ends up somewhere arbitrary.
        ///
        /// Before the head and hands replicated in local space that did not matter: they were
        /// placed at absolute world positions and the root was decorative. Making replication
        /// relative is exactly what turned a harmless wrong number into the avatar standing 75
        /// metres away while the camera it belongs to stayed put.
        /// </summary>
        void Pin()
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        // Early in LateUpdate, before XRINetworkPlayer places the head -- see the note on the
        // class. Netcode's own transform writes land before this in the frame, which is what
        // this is correcting.
        void LateUpdate()
        {
            if (m_Riding) Pin();
        }
    }
}
