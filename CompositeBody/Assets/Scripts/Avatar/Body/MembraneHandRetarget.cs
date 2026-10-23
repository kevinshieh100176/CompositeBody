using System.Collections.Generic;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace CompositeBody.Avatar.Body
{
    /// <summary>
    /// Makes Ch36's own finger bones follow the tracked hand, so the body wears its own hands
    /// rather than having a second pair stand in for them.
    ///
    /// AIMED FROM JOINT POSITIONS, NOT COPIED FROM BONE ROTATIONS. This is the whole design, and
    /// it is the second attempt. The first read the rotations of whatever skeleton the rig
    /// happened to be driving and mapped them onto Ch36's. That is a reasonable-sounding plan
    /// which failed in five different ways, all of them the same way underneath: a bone's
    /// rotation only means something against the convention its rig was authored in, and
    /// Mixamo's convention is not XR Hands'. The symptoms were a wrist rotated by a constant 90
    /// degrees, a thumb 94 degrees out, fingers whose splay came out mirrored, a search that
    /// cycled between three skeletons trying to find the one that moved, and two skeletons whose
    /// bones carried identical names.
    ///
    /// None of that is about the hand -- it is all about rigs. A JOINT POSITION has no
    /// convention. The line from one knuckle to the next points where the finger points, in any
    /// skeleton anyone ever authored, and that is the only thing this reads. Each Ch36 bone is
    /// aimed down the line between the two tracked joints it spans, with the palm's own normal
    /// deciding the roll.
    ///
    /// HandCloud in this project has always followed the hand exactly, because it draws
    /// particles straight onto XRHandSubsystem's joint positions and never touches a skeleton.
    /// That comparison is what gave this away.
    ///
    /// WHERE THE POSITIONS COME FROM depends on whose avatar this is, and only that:
    ///   - your own       -- XRHandSubsystem, the tracking itself, in the rig's tracking space.
    ///   - someone else's -- the networked hand's joints, which the replicator writes from the
    ///                       data that arrived over the wire.
    /// Both are positions, so everything downstream is the same code. Name ambiguity between the
    /// rig's two skeletons stops mattering too: two bones with the same name sit in the same
    /// place, and a position is all that is wanted from them.
    ///
    /// The arms and head are NOT this component's job and are unaffected: MembraneBodyRig solves
    /// them from the networked head and hands. This gives the wrist the one thing that solve
    /// cannot -- an orientation -- and then the fingers below it.
    /// </summary>
    [DefaultExecutionOrder(250)]
    public class MembraneHandRetarget : MonoBehaviour
    {
        [System.Serializable]
        public class Hand
        {
            [Tooltip("Root of the tracked XR hand skeleton that drives this hand -- the wrist " +
                     "of the networked hand, whose children are L_IndexProximal and the rest.")]
            public Transform driverRoot;

            [Tooltip("Ch36's wrist bone, whose children are the mixamorig1 finger bones.")]
            public Transform targetRoot;

            [Tooltip("Hide the driver's mesh. The skeleton keeps running -- renderers only, " +
                     "never the GameObject: switching that off would stop the skinning and, on " +
                     "a networked hand, its NetworkTransform with it.")]
            public bool hideDriverMesh = true;
        }

        [SerializeField] Hand m_Left = new();
        [SerializeField] Hand m_Right = new();

        [SerializeField, Tooltip("Objects on the LOCAL XR rig whose meshes are hidden, matched " +
                                 "on name. The rig draws your own hands from a hierarchy that " +
                                 "has nothing to do with the avatar, so hiding the driver above " +
                                 "does not touch it -- which is why the model's hands and a " +
                                 "tracked pair were on screen at once.")]
        string[] m_HideOnLocalRig = { "Hand Visualizer", "LeftHand", "RightHand" };

        [SerializeField, Tooltip("Renderers whose name contains any of these are never hidden, " +
                                 "even inside an object listed above. The hand membrane films " +
                                 "are generated as children of the very meshes being hidden, " +
                                 "and they are the beat, not a duplicate.")]
        string[] m_NeverHide = { "Membrane" };

        [SerializeField, Tooltip("Log which bones were bound, and the per-finger aim report.")]
        bool m_LogPairing = true;

        [Header("Wrist nudges (degrees)")]
        [SerializeField, Tooltip("Extra rotation on the left wrist, applied after the aim. Leave " +
                                 "at zero unless the hand reads wrong in the headset.")]
        Vector3 m_LeftWristTweak;

        [SerializeField, Tooltip("As the left, for the right wrist.")]
        Vector3 m_RightWristTweak;

        [Header("Finger nudges (degrees, both hands)")]
        [Tooltip("Turned on top of the computed aim, in the Ch36 bone's own axes, and shared by " +
                 "the left and right hands -- a hand is a mirror of the other, so a correction " +
                 "that suits one suits both.\n\n" +
                 "APPLIED EVERY FRAME, so a value changed during Play shows in the headset " +
                 "immediately. Play mode still discards Inspector edits on exit: copy the " +
                 "component's values before stopping, or set them on the prefab.\n\n" +
                 "Expect to leave these at zero. Aiming from joint positions should not need " +
                 "correcting; if one has to be large, something is wrong rather than merely " +
                 "imprecise, and the per-finger line in the log will say which finger.")]
        [SerializeField] Vector3 m_ThumbNudge;
        [SerializeField] Vector3 m_IndexNudge;
        [SerializeField] Vector3 m_MiddleNudge;
        [SerializeField] Vector3 m_RingNudge;
        [SerializeField] Vector3 m_LittleNudge;

        // --- what drives what -------------------------------------------------------------------

        /// <summary>
        /// One Ch36 bone, and the two tracked joints whose line it is aimed along.
        ///
        /// The Mixamo numbering does not line up with XR Hands and never did. XR Hands gives the
        /// non-thumb fingers a metacarpal inside the palm; Mixamo has no bone there, so its
        /// Index1 is the PROXIMAL. The thumb is the opposite case: both start it at the
        /// metacarpal, so Thumb1 is Thumb1. Getting this wrong is invisible -- every bone still
        /// binds, the hand merely bends at the wrong knuckle.
        /// </summary>
        readonly struct Link
        {
            public readonly XRHandJointID from, to;
            public readonly string mixamo;
            public readonly int finger;

            public Link(XRHandJointID from, XRHandJointID to, string mixamo, int finger)
            {
                this.from = from; this.to = to; this.mixamo = mixamo; this.finger = finger;
            }
        }

        /// <summary>
        /// Every bone that gets aimed, and the joint pair that aims it.
        ///
        /// The fingertips are absent on purpose. A tip has no joint beyond it to aim at, and in
        /// this model they carry no skin weights either -- Ch36's Index4 and its siblings deform
        /// nothing, so posing them would be work with no picture attached.
        /// </summary>
        static readonly Link[] k_Chain =
        {
            // The wrist first: aimed down the palm, and every finger hangs off it.
            new Link(XRHandJointID.Wrist, XRHandJointID.MiddleProximal, null, -1),

            new Link(XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, "Thumb1", 0),
            new Link(XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, "Thumb2", 0),
            new Link(XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip, "Thumb3", 0),

            new Link(XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, "Index1", 1),
            new Link(XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, "Index2", 1),
            new Link(XRHandJointID.IndexDistal, XRHandJointID.IndexTip, "Index3", 1),

            new Link(XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, "Middle1", 2),
            new Link(XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, "Middle2", 2),
            new Link(XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip, "Middle3", 2),

            new Link(XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, "Ring1", 3),
            new Link(XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, "Ring2", 3),
            new Link(XRHandJointID.RingDistal, XRHandJointID.RingTip, "Ring3", 3),

            new Link(XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, "Pinky1", 4),
            new Link(XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, "Pinky2", 4),
            new Link(XRHandJointID.LittleDistal, XRHandJointID.LittleTip, "Pinky3", 4),
        };

        /// <summary>A resolved <see cref="Link"/>: the bone found, and its constant aim offset.</summary>
        struct Bound
        {
            public Transform bone;
            public XRHandJointID from, to;
            public int finger;

            /// <summary>
            /// Takes the bone's own axes to the frame it points in, read once from Ch36's bind
            /// pose. Aiming means choosing a rotation R with R * k == the frame we want, so the
            /// bone's rotation is that frame times the inverse of this.
            /// </summary>
            public Quaternion k;
        }

        readonly List<Bound> m_LeftBound = new();
        readonly List<Bound> m_RightBound = new();

        static readonly int k_JointCount = XRHandJointID.EndMarker.ToIndex();

        /// <summary>Joint positions for one hand this frame, indexed by XRHandJointID.ToIndex().</summary>
        sealed class Joints
        {
            public readonly Vector3[] position = new Vector3[k_JointCount];
            public readonly bool[] known = new bool[k_JointCount];

            public void Clear() { for (int i = 0; i < known.Length; i++) known[i] = false; }
            public bool Has(XRHandJointID id) => known[id.ToIndex()];
            public Vector3 At(XRHandJointID id) => position[id.ToIndex()];

            public void Set(XRHandJointID id, Vector3 world)
            {
                int i = id.ToIndex();
                position[i] = world;
                known[i] = true;
            }
        }

        readonly Joints m_LeftJoints = new();
        readonly Joints m_RightJoints = new();

        bool m_Bound;
        readonly Quaternion[] m_Nudge = new Quaternion[5];

        /// <summary>
        /// Everything being kept hidden, re-asserted every frame.
        ///
        /// Setting forceRenderingOff once is not enough: AvatarFormation owns that flag on the
        /// avatar's body renderers and writes it every frame as the membrane grows, so a mesh
        /// hidden at bind time comes straight back the moment the body forms. This runs after it.
        /// </summary>
        readonly List<Renderer> m_Hidden = new();

        void LateUpdate()
        {
            if (!m_Bound) Bind();

            for (int i = 0; i < m_Hidden.Count; i++)
            {
                Renderer r = m_Hidden[i];
                if (r != null && !r.forceRenderingOff) r.forceRenderingOff = true;
            }

            SweepForLateDriverMeshes();

            m_Nudge[0] = Quaternion.Euler(m_ThumbNudge);
            m_Nudge[1] = Quaternion.Euler(m_IndexNudge);
            m_Nudge[2] = Quaternion.Euler(m_MiddleNudge);
            m_Nudge[3] = Quaternion.Euler(m_RingNudge);
            m_Nudge[4] = Quaternion.Euler(m_LittleNudge);

            PoseHand(m_Left, m_LeftBound, m_LeftJoints, true, m_LeftWristTweak);
            PoseHand(m_Right, m_RightBound, m_RightJoints, false, m_RightWristTweak);

            Report();
        }

        /// <summary>
        /// Aim one hand's bones. Does nothing when the joints are not there, which is the normal
        /// state before tracking starts and the right behaviour: a hand with no data should keep
        /// whatever pose it had rather than snap to an invented one.
        /// </summary>
        void PoseHand(Hand hand, List<Bound> bound, Joints joints, bool isLeft, Vector3 wristTweak)
        {
            if (bound.Count == 0) return;
            if (!ReadJoints(hand, joints, isLeft)) return;
            if (!PalmNormal(joints, out Vector3 up)) return;

            foreach (Bound b in bound)
            {
                if (b.bone == null || !joints.Has(b.from) || !joints.Has(b.to)) continue;

                Vector3 dir = joints.At(b.to) - joints.At(b.from);
                if (dir.sqrMagnitude < 1e-12f) continue;

                // A bone lying along the palm's normal leaves LookRotation with no plane to
                // settle the roll in, and it would quietly return something arbitrary.
                if (Vector3.Cross(dir, up).sqrMagnitude < 1e-12f) continue;

                b.bone.rotation = Quaternion.LookRotation(dir, up) * Quaternion.Inverse(b.k);

                if (b.finger >= 0) b.bone.rotation *= m_Nudge[b.finger];
                else if (wristTweak != Vector3.zero) b.bone.rotation *= Quaternion.Euler(wristTweak);
            }

            Measure(bound, joints);

            if (joints.Has(XRHandJointID.Wrist) && hand.targetRoot != null)
                m_WristGap[isLeft ? 0 : 1] =
                    Vector3.Distance(hand.targetRoot.position, joints.At(XRHandJointID.Wrist));
        }

        /// <summary>
        /// The palm's plane, from three knuckles. Used as the roll reference for every bone in
        /// the hand, so one surface decides the twist of all of them and they cannot disagree.
        /// </summary>
        static bool PalmNormal(Joints joints, out Vector3 normal)
        {
            normal = Vector3.up;
            if (!joints.Has(XRHandJointID.Wrist) ||
                !joints.Has(XRHandJointID.IndexProximal) ||
                !joints.Has(XRHandJointID.MiddleProximal) ||
                !joints.Has(XRHandJointID.LittleProximal)) return false;

            Vector3 forward = joints.At(XRHandJointID.MiddleProximal) - joints.At(XRHandJointID.Wrist);
            Vector3 across = joints.At(XRHandJointID.LittleProximal) - joints.At(XRHandJointID.IndexProximal);

            Vector3 n = Vector3.Cross(forward, across);
            if (n.sqrMagnitude < 1e-12f) return false;

            normal = n.normalized;
            return true;
        }

        // --- where the joints come from -----------------------------------------------------

        XROrigin m_Origin;
        XRHandSubsystem m_Subsystem;
        static readonly List<XRHandSubsystem> s_Subsystems = new();

        bool ReadJoints(Hand hand, Joints into, bool isLeft)
        {
            into.Clear();
            return IsOwnedHere() ? ReadFromTracking(into, isLeft) : ReadFromNetworkedHand(hand, into);
        }

        /// <summary>
        /// Straight from the tracking, for the avatar this machine owns. The same source
        /// HandCloud uses, and the reason its particles have always sat on the hand exactly.
        /// </summary>
        bool ReadFromTracking(Joints into, bool isLeft)
        {
            if (m_Subsystem == null || !m_Subsystem.running)
            {
                SubsystemManager.GetSubsystems(s_Subsystems);
                m_Subsystem = s_Subsystems.Count > 0 ? s_Subsystems[0] : null;
                if (m_Subsystem == null) return false;
            }

            if (m_Origin == null)
            {
                m_Origin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
                if (m_Origin == null) return false;
            }

            XRHand hand = isLeft ? m_Subsystem.leftHand : m_Subsystem.rightHand;
            if (!hand.isTracked) return false;

            // Joint poses are given in the rig's tracking space, not the world.
            Transform space = m_Origin.CameraFloorOffsetObject != null
                ? m_Origin.CameraFloorOffsetObject.transform
                : m_Origin.transform;

            bool any = false;
            for (int i = XRHandJointID.BeginMarker.ToIndex(); i < k_JointCount; i++)
            {
                XRHandJointID id = XRHandJointIDUtility.FromIndex(i);
                XRHandJoint joint = hand.GetJoint(id);
                if (!joint.TryGetPose(out Pose pose)) continue;

                into.Set(id, space.TransformPoint(pose.position));
                any = true;
            }
            return any;
        }

        /// <summary>
        /// For an avatar this machine is only watching. The replicator writes the finger data it
        /// received onto the networked hand's joints, so those transforms are where the other
        /// player's hand is -- and only their POSITIONS are read, which is why it no longer
        /// matters that the networked hand carries two identically named skeletons. Two bones
        /// with the same name are in the same place.
        /// </summary>
        bool ReadFromNetworkedHand(Hand hand, Joints into)
        {
            if (hand == null || hand.driverRoot == null) return false;

            // TWO PASSES, ACTIVE FIRST. The networked hand carries a Quest skeleton and an AXR
            // one, and JointBasedHand switches on whichever the platform calls for -- so one of
            // them is not being written to and sits wherever it was authored. Taking the first
            // match found would be a coin toss between the hand and a mannequin, and the search
            // deliberately includes inactive objects because before tracking comes up neither is
            // on yet. An inactive match is therefore worth having, but only if no live one
            // exists.
            bool any = false;
            Transform[] all = hand.driverRoot.GetComponentsInChildren<Transform>(true);

            foreach (Transform t in all)
            {
                if (!t.gameObject.activeInHierarchy) continue;
                if (!TryJointFromName(t.name, out XRHandJointID id) || into.Has(id)) continue;

                into.Set(id, t.position);
                any = true;
            }

            foreach (Transform t in all)
            {
                if (t.gameObject.activeInHierarchy) continue;
                if (!TryJointFromName(t.name, out XRHandJointID id) || into.Has(id)) continue;

                into.Set(id, t.position);
                any = true;
            }

            // The networked hand object sits at the wrist, so it stands in when no bone is named
            // for one -- the replicator's own joint list carries fingers only.
            if (!into.Has(XRHandJointID.Wrist))
            {
                into.Set(XRHandJointID.Wrist, hand.driverRoot.position);
                any = true;
            }
            return any;
        }

        static readonly Dictionary<string, XRHandJointID> s_ByName = BuildNameTable();

        static Dictionary<string, XRHandJointID> BuildNameTable()
        {
            var map = new Dictionary<string, XRHandJointID>();
            for (int i = XRHandJointID.BeginMarker.ToIndex(); i < XRHandJointID.EndMarker.ToIndex(); i++)
            {
                XRHandJointID id = XRHandJointIDUtility.FromIndex(i);
                map[id.ToString()] = id;
            }
            return map;
        }

        /// <summary>
        /// Matched on the end of the name, because the rig prefixes its bones with the side
        /// ("L_IndexProximal") and sometimes suffixes the variant ("L_Wrist - Quest").
        /// </summary>
        static bool TryJointFromName(string name, out XRHandJointID id)
        {
            foreach (KeyValuePair<string, XRHandJointID> entry in s_ByName)
            {
                if (name.EndsWith(entry.Key, System.StringComparison.Ordinal) ||
                    name.Contains(entry.Key + " ", System.StringComparison.Ordinal))
                {
                    id = entry.Value;
                    return true;
                }
            }
            id = XRHandJointID.Invalid;
            return false;
        }

        // --- binding ---------------------------------------------------------------------------

        /// <summary>
        /// Find Ch36's bones and read each one's aim offset from the BIND POSE.
        ///
        /// The bind pose, not the live one. A live reading is only correct if the hand happens to
        /// be in its authored pose at that instant, and the first version of this component read
        /// live -- so whatever the player's hand was doing when the show was hosted became its
        /// idea of "rest", and every finger inherited the error. A bind pose has no such moment,
        /// so nothing here has to be captured at a lucky time.
        /// </summary>
        [ContextMenu("Rebind")]
        public void Bind()
        {
            m_Bound = true;
            m_Hidden.Clear();
            m_LeftBound.Clear();
            m_RightBound.Clear();

            int left = BindHand(m_Left, m_LeftBound, "L_");
            int right = BindHand(m_Right, m_RightBound, "R_");

            HideLocalRigHands();

            if (m_LogPairing)
                Debug.Log($"[HandRetarget] {Who()}: bound {left + right} bone(s) " +
                          $"({left} left, {right} right) to tracked joint pairs, aimed from " +
                          "positions. Source: " +
                          (IsOwnedHere() ? "XRHandSubsystem." : "the networked hand."), this);
        }

        int BindHand(Hand hand, List<Bound> into, string prefix)
        {
            if (hand == null || hand.targetRoot == null)
            {
                if (m_LogPairing)
                    Debug.LogWarning($"[HandRetarget] {Who()}: {prefix} has no target root, so " +
                                     "those bones cannot follow anything.", this);
                return 0;
            }

            if (hand.hideDriverMesh && hand.driverRoot != null) HideMesh(hand.driverRoot, prefix);

            Transform[] bones = hand.targetRoot.GetComponentsInChildren<Transform>(true);
            BindPose bind = BindPose.Around(hand.targetRoot);

            if (!bind.TryPalm(hand.targetRoot,
                              FindBySuffix(bones, "Index1"),
                              FindBySuffix(bones, "Middle1"),
                              FindBySuffix(bones, "Pinky1"),
                              out Vector3 up))
            {
                if (m_LogPairing)
                    Debug.LogWarning($"[HandRetarget] {Who()}: {prefix} could not read a palm " +
                                     "from the bind pose, so its bones are left alone. The hand " +
                                     "bones are probably not skinned to the body mesh.", this);
                return 0;
            }

            int count = 0;
            foreach (Link link in k_Chain)
            {
                // The wrist's bone IS the target root; the fingers hang below it.
                Transform bone = link.mixamo == null
                    ? hand.targetRoot
                    : FindBySuffix(bones, link.mixamo);
                Transform next = NextBone(bones, link);

                if (bone == null || next == null) continue;
                if (!bind.TryAim(bone, next, up, out Quaternion k)) continue;

                into.Add(new Bound
                {
                    bone = bone, from = link.from, to = link.to, finger = link.finger, k = k,
                });
                count++;
            }
            return count;
        }

        /// <summary>The Ch36 bone at the far end of this link, which the aim points at.</summary>
        static Transform NextBone(Transform[] bones, Link link)
        {
            // The wrist aims at the middle knuckle; a finger bone aims at the next bone down,
            // which is the same name with the number raised by one.
            if (link.mixamo == null) return FindBySuffix(bones, "Middle1");

            string tail = link.mixamo.Substring(link.mixamo.Length - 1);
            if (!int.TryParse(tail, out int n)) return null;

            return FindBySuffix(bones, link.mixamo.Substring(0, link.mixamo.Length - 1) + (n + 1));
        }

        /// <summary>
        /// Matched on the end of the name, because Mixamo prefixes every bone with "mixamorig1:"
        /// and the left and right sets are otherwise identically named -- the search is already
        /// inside one hand, so the side cannot be ambiguous.
        /// </summary>
        static Transform FindBySuffix(Transform[] candidates, string suffix)
        {
            foreach (Transform t in candidates)
                if (t != null && t.name.EndsWith(suffix, System.StringComparison.Ordinal)) return t;
            return null;
        }

        // --- the bind pose ---------------------------------------------------------------------

        /// <summary>
        /// Ch36's bones as the mesh was skinned against them. Only the TARGET needs this: the
        /// source is read as positions, which need no reference pose at all.
        /// </summary>
        sealed class BindPose
        {
            readonly Dictionary<Transform, Quaternion> m_Rotation = new();
            readonly Dictionary<Transform, Vector3> m_Position = new();

            public static BindPose Around(Transform anyBone)
            {
                var pose = new BindPose();
                if (anyBone == null) return pose;

                foreach (SkinnedMeshRenderer skin in
                         anyBone.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Transform[] bones = skin.bones;
                    Matrix4x4[] binds = skin.sharedMesh != null ? skin.sharedMesh.bindposes : null;
                    if (bones == null || binds == null || binds.Length < bones.Length) continue;

                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (bones[i] == null || pose.m_Rotation.ContainsKey(bones[i])) continue;

                        // bindposes[i] takes mesh space to bone space, so its inverse is where
                        // the bone sits in mesh space -- the authored pose.
                        Matrix4x4 bind = binds[i].inverse;
                        pose.m_Rotation[bones[i]] = bind.rotation;
                        pose.m_Position[bones[i]] = bind.GetColumn(3);
                    }
                }
                return pose;
            }

            /// <summary>The palm's normal in mesh space, from the same three knuckles as the live one.</summary>
            public bool TryPalm(Transform wrist, Transform index, Transform middle, Transform little,
                                out Vector3 normal)
            {
                normal = Vector3.up;
                if (!Position(wrist, out Vector3 pw) || !Position(index, out Vector3 pi) ||
                    !Position(middle, out Vector3 pm) || !Position(little, out Vector3 pl)) return false;

                Vector3 n = Vector3.Cross(pm - pw, pl - pi);
                if (n.sqrMagnitude < 1e-12f) return false;

                normal = n.normalized;
                return true;
            }

            /// <summary>
            /// The constant rotation from a bone's own axes to the frame it points in at bind:
            /// down the line to the next bone, rolled by the palm.
            ///
            /// This is the only place a Mixamo convention is read, and it is read as a difference
            /// against that model's own geometry rather than compared with another rig's
            /// convention -- so nothing here depends on how XR Hands orients anything.
            /// </summary>
            public bool TryAim(Transform bone, Transform next, Vector3 up, out Quaternion k)
            {
                k = Quaternion.identity;
                if (!Position(bone, out Vector3 pb)) return false;
                if (!m_Rotation.TryGetValue(bone, out Quaternion rb)) return false;
                if (!PositionOrLeaf(bone, next, pb, rb, out Vector3 pn)) return false;

                Vector3 dir = pn - pb;
                if (dir.sqrMagnitude < 1e-12f) return false;
                if (Vector3.Cross(dir, up).sqrMagnitude < 1e-12f) return false;

                k = Quaternion.Inverse(rb) * Quaternion.LookRotation(dir, up);
                return true;
            }

            /// <summary>
            /// Where a bone sits at bind, falling back to deriving it for one that was never
            /// skinned.
            ///
            /// Ch36's fingertips carry no skin weights -- they deform nothing, so the exporter
            /// left them out of the bind pose -- and without them the LAST joint of every finger
            /// has nothing to aim at. That cost five bones a hand, which is the whole fingertip
            /// segment of every finger.
            ///
            /// A bone never translates against its parent, so the offset between the two is the
            /// same in any pose, including the bind one. Measuring it from the live transforms is
            /// therefore exact rather than approximate, and it is the one quantity about a live
            /// skeleton that can be trusted without knowing what pose it is in.
            /// </summary>
            bool PositionOrLeaf(Transform bone, Transform next, Vector3 bindPos, Quaternion bindRot,
                                out Vector3 position)
            {
                if (Position(next, out position)) return true;
                if (next == null || bone == null || next.parent != bone) return false;

                Vector3 offset = Quaternion.Inverse(bone.rotation) * (next.position - bone.position);
                position = bindPos + bindRot * offset;
                return true;
            }

            bool Position(Transform bone, out Vector3 position)
            {
                position = Vector3.zero;
                return bone != null && m_Position.TryGetValue(bone, out position);
            }
        }

        // --- hiding ------------------------------------------------------------------------------

        float m_NextSweep;

        /// <summary>
        /// Look again for driver meshes that were not there when this bound.
        ///
        /// JointBasedHand switches between a Quest hand and an AndroidXR one by SetActive, from
        /// its own Start and again whenever the platform changes -- which is after this has
        /// collected the renderers to hide. Whichever variant comes on late was never in the
        /// list, so it draws, and the other player sees FOUR hands: this avatar's own, and a
        /// tracked pair standing in the same place.
        ///
        /// A sweep rather than a rebind: the binding is right, only the set of renderers has
        /// grown. Twice a second, because the thing it is catching happens once at spawn and
        /// again only if someone changes platform mid-session, and the cost is a
        /// GetComponentsInChildren on two small hierarchies.
        /// </summary>
        void SweepForLateDriverMeshes()
        {
            if (Time.unscaledTime < m_NextSweep) return;
            m_NextSweep = Time.unscaledTime + 0.5f;

            SweepOne(m_Left);
            SweepOne(m_Right);
        }

        void SweepOne(Hand hand)
        {
            if (hand == null || !hand.hideDriverMesh || hand.driverRoot == null) return;

            foreach (Renderer r in hand.driverRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.forceRenderingOff || IsProtected(r.name)) continue;

                r.forceRenderingOff = true;
                if (!m_Hidden.Contains(r)) m_Hidden.Add(r);

                if (m_LogPairing)
                    Debug.Log($"[HandRetarget] {Who()}: hid '{r.name}', which appeared after " +
                              "binding -- the platform's hand variant comes on late.", this);
            }
        }

        /// <summary>
        /// Hide the tracked hand this avatar is wearing, so only the model's own hand draws.
        ///
        /// Counts out loud. A renderer that was NOT hidden here is a hand the other player can
        /// see, and there is no way to notice that from this machine -- the four-hands report
        /// came from the other side of the room, with nothing in any log to narrow it down.
        /// Hiding zero and hiding everything look identical from here without the number.
        /// </summary>
        void HideMesh(Transform root, string prefix)
        {
            int hidden = 0, protectedCount = 0;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (IsProtected(r.name)) { protectedCount++; continue; }

                r.forceRenderingOff = true;
                m_Hidden.Add(r);
                hidden++;
            }

            if (!m_LogPairing) return;

            Debug.Log($"[HandRetarget] {Who()}: {prefix} hid {hidden} driver renderer(s) under " +
                      $"'{root.name}', and left {protectedCount} alone for matching " +
                      $"{string.Join("/", m_NeverHide ?? new string[0])}." +
                      (hidden == 0
                          ? " NOTHING WAS HIDDEN -- if the other machine sees two pairs of " +
                            "hands, this is why."
                          : ""), this);
        }

        bool IsProtected(string name)
        {
            if (m_NeverHide == null) return false;
            foreach (string keep in m_NeverHide)
                if (!string.IsNullOrEmpty(keep) &&
                    name.Contains(keep, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        bool m_HideReported;

        /// <summary>
        /// Hides the rig's own hand meshes, which live outside the avatar entirely.
        ///
        /// Only on the machine that owns this avatar: these are the hands you see of YOURSELF,
        /// and the rig they belong to is this player's. A remote avatar must not reach out and
        /// switch off the local rig's renderers.
        /// </summary>
        void HideLocalRigHands()
        {
            if (m_HideOnLocalRig == null || m_HideOnLocalRig.Length == 0) return;
            if (!IsOwnedHere()) return;

            var origin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (origin == null) return;

            int hidden = 0;
            foreach (Transform t in origin.GetComponentsInChildren<Transform>(true))
            {
                foreach (string name in m_HideOnLocalRig)
                {
                    if (string.IsNullOrEmpty(name) || t.name != name) continue;

                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                    {
                        if (IsProtected(r.name)) continue;
                        r.forceRenderingOff = true;
                        hidden++;
                        m_Hidden.Add(r);
                    }
                }
            }

            if (!m_LogPairing || m_HideReported) return;
            m_HideReported = true;

            Debug.Log($"[HandRetarget] {Who()}: hid {hidden} renderer(s) on the local rig " +
                      $"({string.Join(", ", m_HideOnLocalRig)}).", this);
        }

        // --- who, and how well it is going --------------------------------------------------------

        /// <summary>
        /// Which avatar these lines are about. With two avatars in one scene an anonymous log is
        /// unreadable, and working that out from an unrelated message three lines away cost
        /// several rounds.
        /// </summary>
        string Who()
        {
            var player = GetComponentInParent<XRMultiplayer.XRINetworkPlayer>(true);
            if (player == null) return "no player";
            if (player.IsOwner) return "my own avatar";
            return player.IsSpawned ? $"their avatar (client {player.OwnerClientId})" : "their avatar";
        }

        bool IsOwnedHere()
        {
            var player = GetComponentInParent<XRMultiplayer.XRINetworkPlayer>(true);
            return player != null && player.IsOwner;
        }

        // How far each finger ends up from the direction it was aimed at. Near zero is the point:
        // the bone is aimed AT the tracked line, so a large number means a bone was bound to the
        // wrong joint pair, not that the mapping is imprecise. Those are different bugs with
        // different fixes, and this tells them apart.
        readonly float[] m_Spread = new float[5];
        float m_NextReport;

        /// <summary>
        /// How far Ch36's wrist sits from the tracked one, per hand.
        ///
        /// The retarget sets a wrist's ROTATION and never its position -- that comes from
        /// MembraneBodyRig's arm solve. So the two can disagree, and when they do the other
        /// player sees two pairs of hands: this body's, where the arm reached, and the membrane
        /// films, which sit on the tracked hand. Four hands, with nothing in any log to say why.
        /// </summary>
        readonly float[] m_WristGap = { -1f, -1f };

        /// <summary>
        /// Whether anything was measured at all this period. Without it the report prints 0.0
        /// degrees when no hand is tracked, which reads as a perfect result and is the opposite
        /// of one. Two diagnostics have already been thrown away this week for saying nothing
        /// while looking like they said something.
        /// </summary>
        bool m_Measured;

        void Measure(List<Bound> bound, Joints joints)
        {
            foreach (Bound b in bound)
            {
                if (b.finger < 0 || b.bone == null) continue;
                if (!joints.Has(b.from) || !joints.Has(b.to)) continue;

                Vector3 wanted = (joints.At(b.to) - joints.At(b.from)).normalized;
                Vector3 got = b.bone.rotation * b.k * Vector3.forward;

                m_Spread[b.finger] = Mathf.Max(m_Spread[b.finger], Vector3.Angle(wanted, got));
                m_Measured = true;
            }
        }

        void Report()
        {
            if (!m_LogPairing || Time.unscaledTime < m_NextReport) return;
            m_NextReport = Time.unscaledTime + 10f;

            if (!m_Measured)
            {
                Debug.Log($"[HandRetarget] {Who()}: no tracked hand in the last 10s, so there " +
                          "is nothing to report. Not a pass.", this);
                return;
            }
            m_Measured = false;

            var report = new StringBuilder();
            string[] names = { "Thumb", "Index", "Middle", "Ring", "Little" };
            for (int f = 0; f < 5; f++)
            {
                report.Append($"\n[HandRetarget]     {names[f]}: aim off by {m_Spread[f]:0.0} deg");
                m_Spread[f] = 0f;
            }

            for (int h = 0; h < 2; h++)
            {
                if (m_WristGap[h] < 0f) continue;

                report.Append($"\n[HandRetarget]     {(h == 0 ? "left" : "right")} wrist sits " +
                              $"{m_WristGap[h]:0.000} m from the tracked one");

                // A centimetre is the model's own wrist offset. A hand's width is two pairs.
                if (m_WristGap[h] > 0.05f)
                    report.Append(" -- FAR. The arm solve is not reaching the tracked hand, so " +
                                  "this body's hands and the membrane films are in different " +
                                  "places and read as two pairs.");
            }

            Debug.Log($"[HandRetarget] {Who()}: worst aim error over the last 10s." + report, this);
        }
    }
}
