using System.Collections.Generic;
using UnityEngine;

namespace CompositeBody.Avatar.Body
{
    /// <summary>
    /// Makes Ch36's own finger bones follow the tracked hand skeleton, so the body wears its own
    /// hands instead of a separate pair floating at the end of its arms.
    ///
    /// WHY NOT DRIVE THE BONES DIRECTLY. The template's finger system (JointBasedHand) applies a
    /// curl as a local rotation about X:
    ///
    ///     rot.x = Mathf.Lerp(min, max, curlAmount);
    ///     jointTransform.localRotation = Quaternion.Euler(rot);
    ///
    /// That only works on a skeleton whose joints bend about their local X. Mixamo's do not, so
    /// pointing that system at Ch36 makes the fingers splay sideways rather than close. The same
    /// is true of the full-rotation path: those are the XR skeleton's rotations, in the XR
    /// skeleton's bone orientations.
    ///
    /// SO THE XR SKELETON STAYS THE DRIVER. It is already tracked, already replicated, and
    /// already correct -- only its mesh is hidden. Each Ch36 bone stores the rotation that takes
    /// its driver's orientation to its own, captured once while both are in their rest pose:
    ///
    ///     offset = inverse(source.rotation) * target.rotation      (captured once)
    ///     target.rotation = source.rotation * offset               (every frame)
    ///
    /// Whatever the two skeletons disagree about -- axis conventions, roll, bind pose -- is in
    /// that offset and cancels. Nothing in the networking changes: both machines already have the
    /// driver skeleton, so both get the fingers.
    ///
    /// Runs after MembraneBodyRig (execution order 200), which has already placed the wrist by
    /// solving the arm onto the networked hand. Only the fingers are touched here.
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
                                 "tracked pair were on screen at once.\n\n" +
                                 "'Hand Visualizer' is the raw tracked mesh. The Interaction " +
                                 "Visuals are deliberately NOT listed: they carry the hand " +
                                 "membrane films and the poke and ray affordances.")]
        string[] m_HideOnLocalRig = { "Hand Visualizer" };

        [SerializeField, Tooltip("Log the bones that were paired and the ones that were not, " +
                                 "once, on capture. A finger that silently fails to pair is the " +
                                 "one thing here that looks like nothing at all.")]
        bool m_LogPairing = true;

        /// <summary>
        /// XR Hands joint suffix to Mixamo bone suffix. The thumb is the awkward one: XR Hands
        /// gives it metacarpal, proximal, distal, tip and no intermediate, while Mixamo numbers
        /// it 1 to 4 like every other finger. The non-thumb metacarpals have no Mixamo bone at
        /// all -- they are inside the palm -- so they are simply not mapped.
        /// </summary>
        static readonly (string xr, string mixamo)[] k_Map =
        {
            ("ThumbMetacarpal", "Thumb1"), ("ThumbProximal", "Thumb2"),
            ("ThumbDistal", "Thumb3"),     ("ThumbTip", "Thumb4"),

            ("IndexProximal", "Index1"),   ("IndexIntermediate", "Index2"),
            ("IndexDistal", "Index3"),     ("IndexTip", "Index4"),

            ("MiddleProximal", "Middle1"), ("MiddleIntermediate", "Middle2"),
            ("MiddleDistal", "Middle3"),   ("MiddleTip", "Middle4"),

            ("RingProximal", "Ring1"),     ("RingIntermediate", "Ring2"),
            ("RingDistal", "Ring3"),       ("RingTip", "Ring4"),

            // XR Hands calls it Little; Mixamo calls it Pinky.
            ("LittleProximal", "Pinky1"),  ("LittleIntermediate", "Pinky2"),
            ("LittleDistal", "Pinky3"),    ("LittleTip", "Pinky4"),
        };

        readonly List<(Transform source, Transform target, Quaternion offset)> m_Pairs = new();
        bool m_Captured;

        void LateUpdate()
        {
            if (!m_Captured) Capture();

            for (int i = 0; i < m_Pairs.Count; i++)
            {
                var (source, target, offset) = m_Pairs[i];
                if (source == null || target == null) continue;
                target.rotation = source.rotation * offset;
            }
        }

        /// <summary>
        /// Pair the bones and record each offset.
        ///
        /// Captured on the first frame, which is the one moment both skeletons are still in the
        /// pose they were authored in -- tracking has not been applied yet. Capturing later would
        /// bake whatever the player's hand happened to be doing into the rest pose, so a fist at
        /// that instant would become the new "open".
        /// </summary>
        [ContextMenu("Recapture Rest Pose")]
        public void Capture()
        {
            m_Pairs.Clear();
            m_Captured = true;

            int paired = Pair(m_Left, "L_") + Pair(m_Right, "R_");
            HideLocalRigHands();

            if (m_LogPairing)
                Debug.Log($"[HandRetarget] paired {paired} finger bones " +
                          $"({m_Pairs.Count} driving). Ch36's hands now follow the tracked ones.",
                          this);
        }

        int Pair(Hand hand, string xrPrefix)
        {
            if (hand?.driverRoot == null || hand.targetRoot == null)
            {
                if (m_LogPairing)
                    Debug.LogWarning($"[HandRetarget] {xrPrefix} hand is not wired " +
                                     $"(driver={hand?.driverRoot}, target={hand?.targetRoot}); " +
                                     "those fingers will not follow.", this);
                return 0;
            }

            if (hand.hideDriverMesh) HideMesh(hand.driverRoot);

            Transform[] sources = hand.driverRoot.GetComponentsInChildren<Transform>(true);
            Transform[] targets = hand.targetRoot.GetComponentsInChildren<Transform>(true);

            int paired = 0;
            foreach (var (xr, mixamo) in k_Map)
            {
                Transform source = FindBySuffix(sources, xrPrefix + xr);
                Transform target = FindBySuffix(targets, mixamo);

                if (source == null || target == null)
                {
                    if (m_LogPairing)
                        Debug.LogWarning($"[HandRetarget] no pair for {xrPrefix}{xr} -> {mixamo} " +
                                         $"(source={(source != null)}, target={(target != null)})",
                                         this);
                    continue;
                }

                m_Pairs.Add((source, target,
                             Quaternion.Inverse(source.rotation) * target.rotation));
                paired++;
            }

            return paired;
        }

        /// <summary>
        /// Matched on the end of the name, because Mixamo prefixes every bone with "mixamorig1:"
        /// and the left and right sets are otherwise identically named.
        /// </summary>
        static Transform FindBySuffix(Transform[] candidates, string suffix)
        {
            // Active first. The networked hand carries more than one skeleton -- an AXR set and
            // a Quest set -- and only one of them is switched on at a time. Taking the first
            // name match regardless would happily pair every bone to the variant that is not
            // being tracked, which drives the fingers from a skeleton that never moves.
            Transform fallback = null;

            foreach (Transform t in candidates)
            {
                if (t == null || !t.name.EndsWith(suffix, System.StringComparison.Ordinal))
                    continue;

                if (t.gameObject.activeInHierarchy) return t;
                fallback ??= t;
            }

            return fallback;
        }

        /// <summary>
        /// forceRenderingOff rather than enabled or SetActive. `enabled` is written every frame by
        /// the membrane film links, and switching the object off would stop the skeleton being
        /// skinned at all -- which is exactly how the networked Head came to replicate nothing.
        /// </summary>
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

            var player = GetComponentInParent<XRMultiplayer.XRINetworkPlayer>(true);
            if (player == null || !player.IsOwner) return;

            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>(
                FindObjectsInactive.Include);
            if (origin == null) return;

            int hidden = 0;
            foreach (Transform t in origin.GetComponentsInChildren<Transform>(true))
            {
                foreach (string name in m_HideOnLocalRig)
                {
                    if (string.IsNullOrEmpty(name) || t.name != name) continue;

                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                    {
                        r.forceRenderingOff = true;
                        hidden++;
                    }
                }
            }

            if (m_LogPairing)
                Debug.Log($"[HandRetarget] hid {hidden} renderer(s) on the local rig " +
                          $"({string.Join(", ", m_HideOnLocalRig)}).", this);
        }

        static void HideMesh(Transform driverRoot)
        {
            foreach (Renderer r in driverRoot.GetComponentsInChildren<Renderer>(true))
                r.forceRenderingOff = true;
        }
    }
}
