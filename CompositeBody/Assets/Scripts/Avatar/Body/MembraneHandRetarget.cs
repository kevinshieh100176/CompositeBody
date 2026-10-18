using System.Collections.Generic;
using System.Text;
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
        string[] m_HideOnLocalRig = { "Hand Visualizer", "LeftHand", "RightHand" };

        [SerializeField, Tooltip("Renderers whose name contains any of these are never hidden, " +
                                 "even inside an object listed above. The hand membrane films " +
                                 "are generated as children of the very meshes being hidden, " +
                                 "and they are the beat, not a duplicate.")]
        string[] m_NeverHide = { "Membrane" };

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

        /// <summary>
        /// One bone pair, plus the two wrists its rotation is expressed relative to.
        ///
        /// WRIST-RELATIVE, NOT WORLD. Copying the source bone's world rotation looks right until
        /// you notice the two wrists do not share an orientation: MembraneBodyRig solves Ch36's
        /// wrist onto the networked hand, whose axes are not the tracked skeleton's. A curl is a
        /// rotation about the finger's own axis, so expressed in world space it arrives at an
        /// angle to Ch36's palm and the fingers bend sideways and through each other. Taking the
        /// rotation relative to each skeleton's own wrist cancels that difference.
        /// </summary>
        struct Pair
        {
            public Transform source, target, sourceWrist, targetWrist;
            public Quaternion offset;
        }

        readonly List<Pair> m_Pairs = new();

        /// <summary>Wrist pairs, applied before the fingers.</summary>
        readonly List<Pair> m_WristPairs = new();
        bool m_Captured;

        float m_NextRecaptureCheck;

        /// <summary>
        /// Everything being kept hidden, re-asserted every frame.
        ///
        /// Setting forceRenderingOff once is not enough: AvatarFormation owns that flag on the
        /// avatar's body renderers and writes it every frame as the membrane grows, so a mesh
        /// hidden at capture comes straight back the moment the body forms. It runs before this
        /// does, so re-asserting here wins.
        /// </summary>
        readonly List<Renderer> m_Hidden = new();

        void LateUpdate()
        {
            if (!m_Captured) Capture();

            for (int i = 0; i < m_Hidden.Count; i++)
            {
                Renderer r = m_Hidden[i];
                if (r != null && !r.forceRenderingOff) r.forceRenderingOff = true;
            }

            // The networked hand carries two skeletons -- a Quest set and an AXR set -- and only
            // one is switched on. Before tracking comes up neither is, so the first capture pairs
            // whichever was found first and that can be the wrong one. If the bones being read
            // are not the live ones, pair again; the newly activated skeleton is still near its
            // rest pose at that moment, which is what the capture wants anyway.
            if (m_Captured && Time.unscaledTime >= m_NextRecaptureCheck)
            {
                m_NextRecaptureCheck = Time.unscaledTime + 1f;

                // Only when a DIFFERENT wrist is now the live one. The first version re-paired
                // whenever the current wrist was inactive, which with no skeleton active at all
                // meant finding the same inactive bone again and again -- a capture loop once a
                // second that also kept resetting the diagnostic below, so the one line that
                // would have explained the problem never printed.
                Transform liveLeft = LiveWrist(m_Left, "L_");
                Transform liveRight = LiveWrist(m_Right, "R_");

                if ((liveLeft != null && liveLeft != CurrentWrist(m_Left)) ||
                    (liveRight != null && liveRight != CurrentWrist(m_Right)))
                {
                    Debug.Log("[HandRetarget] a different skeleton is live now; pairing again.",
                              this);
                    Capture();
                }
            }

            // The wrists first, because every finger below is expressed relative to them.
            //
            // MembraneBodyRig solves the arm onto the hand's POSITION only -- Solve takes
            // m_LeftHandTarget.position and nothing else -- so Ch36's wrist ends up wherever the
            // two-bone IK leaves it and never turns with the hand. Retargeted the same way as a
            // finger: whatever the two skeletons disagree about is in the offset.
            for (int i = 0; i < m_WristPairs.Count; i++)
            {
                Pair w = m_WristPairs[i];
                if (w.source == null || w.target == null) continue;
                w.target.rotation = w.source.rotation * w.offset;
            }

            for (int i = 0; i < m_Pairs.Count; i++)
            {
                Pair p = m_Pairs[i];
                if (p.source == null || p.target == null ||
                    p.sourceWrist == null || p.targetWrist == null) continue;

                // Where the source bone sits relative to its own wrist, re-expressed in the
                // target wrist's frame. Parents are listed before their children, so setting a
                // world rotation on a parent is immediately corrected by the child's own.
                Quaternion relative = Quaternion.Inverse(p.sourceWrist.rotation) * p.source.rotation;
                p.target.rotation = p.targetWrist.rotation * relative * p.offset;

                // Watch one bone per hand. A retarget reading a skeleton that never moves looks
                // exactly like a retarget that is broken, and the networked hand carries more
                // than one skeleton -- so "which bones am I actually reading" is the question
                // worth answering in the log rather than by eye.
                if (i == m_WatchIndex) Observe(relative);
            }

            ReportIfSourceIsStatic();
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
            m_WristPairs.Clear();
            m_Captured = true;
            m_Hidden.Clear();

            int paired = PairHand(m_Left, "L_") + PairHand(m_Right, "R_");

            // Index proximal: the bone a curl shows on most clearly.
            m_WatchIndex = -1;
            for (int i = 0; i < m_Pairs.Count; i++)
            {
                if (!m_Pairs[i].source.name.Contains("IndexProximal")) continue;
                m_WatchIndex = i;
                m_WatchRest = Quaternion.Inverse(m_Pairs[i].sourceWrist.rotation) *
                              m_Pairs[i].source.rotation;
                break;
            }
            m_WatchMaxAngle = 0f;
            m_WatchReported = false;
            m_WatchDeadline = Time.unscaledTime + 8f;
            HideLocalRigHands();

            if (m_LogPairing)
                Debug.Log($"[HandRetarget] paired {paired} finger bones " +
                          $"({m_Pairs.Count} driving). Ch36's hands now follow the tracked ones.",
                          this);
        }

        int PairHand(Hand hand, string xrPrefix)
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

            // The tracked skeleton's own wrist, not the networked hand object above it: that
            // object is positioned by XRINetworkPlayer and carries no finger bones, so measuring
            // against it would fold the hand's own orientation back into every finger.
            // Matched on CONTAINS, not on the end of the name: the tracked skeletons label their
            // wrists "L_Wrist - Quest" and "L_Wrist - AXR" for the two rig variants. Missing it
            // falls back to the networked hand object, which sits at the wrist but does not share
            // its orientation -- and that difference is exactly what this is supposed to cancel,
            // so the fallback silently reintroduces the bent fingers.
            Transform sourceWrist = FindContaining(sources, xrPrefix + "Wrist") ?? hand.driverRoot;
            Transform targetWrist = hand.targetRoot;

            if (m_LogPairing)
                Debug.Log($"[HandRetarget] {xrPrefix} wrists: source='{sourceWrist.name}' " +
                          $"target='{targetWrist.name}'", this);

            // The joint list the replicator actually animates, keyed by XRHandJointID. Matching
            // bones by NAME picked the wrong skeleton: the networked hand carries two, their
            // bones are identically named, and the diagnostic caught a second 'L_IndexProximal'
            // sitting under a metacarpal. A name is not an identity here; this list is.
            bool isOwner = IsOwnedHere();
            Dictionary<string, Transform> animated = Candidate(hand, xrPrefix, m_Candidate, isOwner);
            if (animated != null && animated.Count > 0)
            {
                Transform wristFromDriver = animated.TryGetValue("Wrist", out Transform w) ? w : null;
                if (wristFromDriver != null) sourceWrist = wristFromDriver;

                if (m_LogPairing)
                    Debug.Log($"[HandRetarget] {xrPrefix} using the replicator's own joint list " +
                              $"({animated.Count} joints), wrist='{sourceWrist.name}'.", this);
            }

            // The wrist itself, so Ch36 hand turns with the tracked one.
            m_WristPairs.Add(new Pair
            {
                source = sourceWrist,
                target = targetWrist,
                sourceWrist = sourceWrist,
                targetWrist = targetWrist,
                offset = Quaternion.Inverse(sourceWrist.rotation) * targetWrist.rotation,
            });

            int paired = 0;
            foreach (var (xr, mixamo) in k_Map)
            {
                Transform source = animated != null && animated.TryGetValue(xr, out Transform t)
                    ? t
                    : FindBySuffix(sources, xrPrefix + xr);

                Transform target = FindBySuffix(targets, mixamo);

                if (source == null || target == null)
                {
                    if (m_LogPairing)
                        Debug.LogWarning($"[HandRetarget] no pair for {xrPrefix}{xr} -> {mixamo} " +
                                         $"(source={(source != null)}, target={(target != null)})",
                                         this);
                    continue;
                }

                Quaternion restSource = Quaternion.Inverse(sourceWrist.rotation) * source.rotation;
                Quaternion restTarget = Quaternion.Inverse(targetWrist.rotation) * target.rotation;

                m_Pairs.Add(new Pair
                {
                    source = source,
                    target = target,
                    sourceWrist = sourceWrist,
                    targetWrist = targetWrist,

                    // Takes the source bone's rest orientation to the target's, both measured
                    // from their own wrist. Everything the two skeletons disagree about -- axis
                    // conventions, roll, how open the bind pose is -- lives here and cancels.
                    offset = Quaternion.Inverse(restSource) * restTarget,
                });
                paired++;
            }

            return paired;
        }

        /// <summary>
        /// Matched on the end of the name, because Mixamo prefixes every bone with "mixamorig1:"
        /// and the left and right sets are otherwise identically named.
        /// </summary>
        /// <summary>As FindBySuffix, but matching anywhere in the name.</summary>
        static Transform FindContaining(Transform[] candidates, string fragment)
        {
            Transform fallback = null;

            foreach (Transform t in candidates)
            {
                if (t == null || !t.name.Contains(fragment, System.StringComparison.Ordinal))
                    continue;

                if (t.gameObject.activeInHierarchy) return t;
                fallback ??= t;
            }

            return fallback;
        }

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
                        if (IsProtected(r.name)) continue;
                        r.forceRenderingOff = true;
                        hidden++;
                        m_Hidden.Add(r);
                    }
                }
            }

            if (!m_LogPairing) return;

            Debug.Log($"[HandRetarget] hid {hidden} renderer(s) on the local rig " +
                      $"({string.Join(", ", m_HideOnLocalRig)}).", this);

            // Everything still drawing under the rig's hands, named. Three rounds have now been
            // spent guessing which object the duplicate pair of hands belongs to; this prints the
            // list instead so the next one can be named rather than guessed at.
            var modality = FindFirstObjectByType<
                UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager>(
                FindObjectsInactive.Include);
            if (modality == null) return;

            var still = new StringBuilder();
            foreach (GameObject side in new[] { modality.leftHand, modality.rightHand,
                                                modality.leftController, modality.rightController })
            {
                if (side == null) continue;

                foreach (Renderer r in side.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.forceRenderingOff || !r.enabled || !r.gameObject.activeInHierarchy)
                        continue;

                    still.Append($"\n[HandRetarget]     '{r.name}' ({r.GetType().Name}) under " +
                                 $"'{(r.transform.parent != null ? r.transform.parent.name : "?")}'" +
                                 $" in '{side.name}'");
                }
            }

            Debug.Log(still.Length > 0
                ? "[HandRetarget] still drawing on the rig:" + still
                : "[HandRetarget] nothing else is drawing on the rig's hands.", this);
        }

        /// <summary>
        /// The joints the finger replication actually writes to, keyed by their XRHandJointID
        /// name ("IndexProximal", "ThumbMetacarpal", ...).
        ///
        /// JointBasedHand holds these references and is what both the curl path and the
        /// full-rotation path drive, so this is the skeleton that moves -- as opposed to the one
        /// that merely has the right bone names.
        /// </summary>
        /// <summary>How many different skeletons this will try before giving up.</summary>
        const int k_CandidateCount = 3;

        int m_Candidate;

        /// <summary>
        /// The joint set to try, in order of how likely it is to be the animated one.
        ///
        /// 0 -- the networked hand's JointBasedHand. Correct on a machine watching SOMEONE ELSE,
        ///      because that is where the replicated finger data is applied.
        /// 1 -- the local XR rig's tracked hand skeleton. Correct for your OWN avatar: your own
        ///      networked hand is only written to on the other machine, so locally it is static.
        /// 2 -- any other JointBasedHand under the networked hand, for the second rig variant.
        /// </summary>
        static Dictionary<string, Transform> Candidate(Hand hand, string xrPrefix, int index,
                                                      bool isOwner)
        {
            switch (index)
            {
                case 0: return AnimatedJoints(hand.driverRoot);

                // OWNER ONLY. The local rig belongs to whoever is sitting here, not to the avatar
                // being retargeted -- so on a machine watching someone else this would drive
                // THEIR fingers from YOUR hands. It would even pass the watchdog, because those
                // bones are certainly moving. That is worse than not tracking: it looks like it
                // works.
                case 1: return isOwner ? LocalRigJoints(xrPrefix) : null;

                default: return AnimatedJoints(hand.driverRoot, skip: 1);
            }
        }

        /// <summary>
        /// The joints of the local rig's own tracked hand -- the skeleton an XRHandSkeletonDriver
        /// writes from the headset, which is the only one that moves on the machine that owns
        /// the avatar.
        /// </summary>
        static Dictionary<string, Transform> LocalRigJoints(string xrPrefix)
        {
            var modality = FindFirstObjectByType<
                UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager>(
                FindObjectsInactive.Include);
            if (modality == null) return null;

            GameObject side = xrPrefix == "L_" ? modality.leftHand : modality.rightHand;
            if (side == null) return null;

            var driver = side.GetComponentInChildren<UnityEngine.XR.Hands.XRHandSkeletonDriver>(true);
            if (driver?.jointTransformReferences == null) return null;

            var map = new Dictionary<string, Transform>();
            foreach (var reference in driver.jointTransformReferences)
            {
                if (reference.jointTransform == null) continue;
                map[reference.xrHandJointID.ToString()] = reference.jointTransform;
            }
            return map;
        }

        static Dictionary<string, Transform> AnimatedJoints(Transform driverRoot, int skip = 0)
        {
            var hands = driverRoot.GetComponentsInChildren<XRMultiplayer.JointBasedHand>(true);
            if (hands == null || hands.Length == 0) return null;

            // Prefer one that is switched on; fall back to the first, which is still better than
            // a name match because at least these transforms are the ones being written to.
            XRMultiplayer.JointBasedHand chosen = null;
            int seen = 0;
            foreach (var h in hands)
            {
                if (!h.gameObject.activeInHierarchy) continue;
                if (seen++ < skip) continue;
                chosen = h;
                break;
            }
            chosen ??= hands.Length > skip ? hands[skip] : hands[0];

            if (chosen.handFidelityOptions == null || chosen.handFidelityOptions.Length == 0)
                return null;

            var map = new Dictionary<string, Transform>();

            foreach (var finger in chosen.handFidelityOptions[0].fingerJoints)
            {
                if (finger.jointTransformReferences == null) continue;

                foreach (var reference in finger.jointTransformReferences)
                {
                    if (reference.jointTransform == null) continue;
                    map[reference.xrHandJointID.ToString()] = reference.jointTransform;
                }
            }

            return map;
        }

        /// <summary>
        /// Whether this avatar belongs to the person at this machine. Decides whether the local
        /// rig is a legitimate source for it.
        /// </summary>
        bool IsOwnedHere()
        {
            var player = GetComponentInParent<XRMultiplayer.XRINetworkPlayer>(true);
            return player != null && player.IsOwner;
        }

        /// <summary>The wrist this hand is currently paired against, or null.</summary>
        Transform CurrentWrist(Hand hand)
        {
            foreach (Pair p in m_Pairs)
                if (p.target != null && hand.targetRoot != null && p.targetWrist == hand.targetRoot)
                    return p.sourceWrist;
            return null;
        }

        /// <summary>
        /// The wrist that is active right now, or null when none is -- which is the normal state
        /// before tracking comes up and must not be mistaken for a change.
        /// </summary>
        static Transform LiveWrist(Hand hand, string xrPrefix)
        {
            if (hand?.driverRoot == null) return null;

            foreach (Transform t in hand.driverRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains(xrPrefix + "Wrist", System.StringComparison.Ordinal) &&
                    t.gameObject.activeInHierarchy)
                    return t;
            }
            return null;
        }

        // --- is the source actually animating? ---------------------------------------------------

        int m_WatchIndex = -1;
        Quaternion m_WatchRest;
        float m_WatchMaxAngle;
        float m_WatchDeadline;
        bool m_WatchReported;

        void Observe(Quaternion relative)
        {
            m_WatchMaxAngle = Mathf.Max(m_WatchMaxAngle, Quaternion.Angle(m_WatchRest, relative));
        }

        /// <summary>
        /// After a grace period, say whether the bones being read have moved at all -- and if
        /// not, name every other transform with the same bone name, which is the list the right
        /// one is hiding in.
        /// </summary>
        void ReportIfSourceIsStatic()
        {
            if (m_WatchReported || m_WatchIndex < 0 || Time.unscaledTime < m_WatchDeadline) return;
            m_WatchReported = true;

            Pair p = m_Pairs[m_WatchIndex];

            if (m_WatchMaxAngle > 2f)
            {
                Debug.Log($"[HandRetarget] source is animating: '{p.source.name}' moved " +
                          $"{m_WatchMaxAngle:0.0} degrees (candidate {m_Candidate}). Ch36 is " +
                          "following it.", this);
                return;
            }

            // NOT MOVING, SO TRY THE NEXT SKELETON. Which one carries the finger animation is not
            // something this can know in advance: the owner's networked hand is only written to
            // on OTHER machines, the local rig has its own tracked skeleton, and the bones in all
            // of them share names. Rather than guess again, it watches, and if nothing moved it
            // moves on. The candidate that moves is the right one by definition.
            if (m_Candidate + 1 < k_CandidateCount)
            {
                m_Candidate++;
                Debug.LogWarning($"[HandRetarget] '{p.source.name}' never moved; trying " +
                                 $"source candidate {m_Candidate} instead.", this);
                Capture();
                return;
            }

            var others = new System.Text.StringBuilder();
            foreach (Transform t in m_Left.driverRoot.root.GetComponentsInChildren<Transform>(true))
            {
                if (t == p.source || t.name != p.source.name) continue;
                others.Append($"\n[HandRetarget]     also '{t.name}' under " +
                              $"'{(t.parent != null ? t.parent.name : "?")}' " +
                              $"active={t.gameObject.activeInHierarchy}");
            }

            // Two very different causes look identical from here, so it says both rather than
            // picking one. In a headless run nothing is tracking and this is simply true; in a
            // headset, with the tracked hands visibly moving, it means the wrong skeleton.
            Debug.LogWarning($"[HandRetarget] source '{p.source.name}' has not moved " +
                             $"({m_WatchMaxAngle:0.0} degrees in 8s), so Ch36's fingers have " +
                             "nothing to follow. Either nothing is being tracked -- check for " +
                             "the 'neither a tracked hand nor a controller is active' warning " +
                             "-- or this is not the skeleton being animated." + (others.Length > 0
                                 ? "\n[HandRetarget]   other transforms with that name:" + others
                                 : "\n[HandRetarget]   no other transform carries that name."),
                             this);
        }

        bool IsProtected(string rendererName)
        {
            if (m_NeverHide == null) return false;

            foreach (string keep in m_NeverHide)
            {
                if (!string.IsNullOrEmpty(keep) &&
                    rendererName.Contains(keep, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        void HideMesh(Transform driverRoot)
        {
            foreach (Renderer r in driverRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (IsProtected(r.name)) continue;
                r.forceRenderingOff = true;
                m_Hidden.Add(r);
            }
        }
    }
}
