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

        [SerializeField, Tooltip("Extra rotation on the left wrist, in degrees, applied after " +
                                 "the palm frames are matched. Leave at zero: it exists so a " +
                                 "rig whose hand still lands wrong can be corrected without a " +
                                 "rebuild, not as the normal way to get the wrist right.")]
        Vector3 m_LeftWristTweak;

        [SerializeField, Tooltip("As the left, for the right wrist.")]
        Vector3 m_RightWristTweak;

        [Header("Finger nudges (degrees, both hands)")]
        [Tooltip("Turned on top of the computed mapping, in the Ch36 bone's own axes, and shared " +
                 "by the left and right hands -- a hand is a mirror of the other, so a correction " +
                 "that suits one suits both.\n\n" +
                 "APPLIED EVERY FRAME, not baked in when the pose is captured, so a value changed " +
                 "during Play shows in the headset immediately. That is the only way to dial one " +
                 "in: the hands are only visible while playing.\n\n" +
                 "Play mode still discards Inspector edits on exit, so copy the component's " +
                 "values before stopping, or set them on the XRI_Network_Player_Avatar prefab.\n\n" +
                 "Leave at zero where the computed mapping is already right. The per-finger line " +
                 "in the log reports how far each finger's aim is from the player's; the nudge is " +
                 "right when that number stops falling.")]
        [SerializeField] Vector3 m_ThumbNudge;
        [SerializeField] Vector3 m_IndexNudge;
        [SerializeField] Vector3 m_MiddleNudge;
        [SerializeField] Vector3 m_RingNudge;
        [SerializeField] Vector3 m_LittleNudge;

        /// <summary>The five nudges as rotations, rebuilt once a frame rather than per bone.</summary>
        readonly Quaternion[] m_Nudge = new Quaternion[5];

        void RefreshNudges()
        {
            m_Nudge[0] = Quaternion.Euler(m_ThumbNudge);
            m_Nudge[1] = Quaternion.Euler(m_IndexNudge);
            m_Nudge[2] = Quaternion.Euler(m_MiddleNudge);
            m_Nudge[3] = Quaternion.Euler(m_RingNudge);
            m_Nudge[4] = Quaternion.Euler(m_LittleNudge);
        }

        /// <summary>Which finger an XR joint name belongs to, or -1.</summary>
        static int FingerOf(string xr)
        {
            if (xr.StartsWith("Thumb", System.StringComparison.Ordinal)) return 0;
            if (xr.StartsWith("Index", System.StringComparison.Ordinal)) return 1;
            if (xr.StartsWith("Middle", System.StringComparison.Ordinal)) return 2;
            if (xr.StartsWith("Ring", System.StringComparison.Ordinal)) return 3;
            if (xr.StartsWith("Little", System.StringComparison.Ordinal)) return 4;
            return -1;
        }

        /// <summary>
        /// Whether the pairing detail is still worth printing.
        ///
        /// The search re-captures every eight seconds for as long as nothing is tracked, so the
        /// five lines a capture prints would repeat forever on a machine with no headset. They
        /// are worth having for the first pass through the candidates and worthless after that,
        /// so they stop once the whole list has been tried. The line that says a source IS
        /// animating is deliberately not gated: that one is the answer.
        /// </summary>
        bool Chatty => m_LogPairing && !m_ExhaustedReported;

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

            /// <summary>0 thumb to 4 little, or -1 for a wrist. Indexes the hand-tuned nudges.</summary>
            public int finger;
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
                    Debug.Log($"[HandRetarget] {Who()}: a different skeleton is live now; pairing again.",
                              this);
                    Capture();
                }
            }

            RefreshNudges();

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

                // Live, for the same reason the fingers are: a wrist nudge that only took effect
                // on a recapture could not be judged against the hand it is meant to match.
                Vector3 wristNudge = w.finger == -1 ? m_LeftWristTweak : m_RightWristTweak;
                if (wristNudge != Vector3.zero) w.target.rotation *= Quaternion.Euler(wristNudge);
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

                // The hand-tuned nudge, after the computed mapping and in the target bone's own
                // axes, so turning it in the Inspector turns the finger the way it looks like it
                // should. Live rather than baked into the offset at capture: these can only be
                // judged while playing, with a headset on, and a value that needed a recapture to
                // take effect could not be dialled in at all.
                if (p.finger >= 0) p.target.rotation *= m_Nudge[p.finger];

                // Watch one bone per hand. A retarget reading a skeleton that never moves looks
                // exactly like a retarget that is broken, and the networked hand carries more
                // than one skeleton -- so "which bones am I actually reading" is the question
                // worth answering in the log rather than by eye.
                if (i == m_WatchIndex) Observe(relative);
            }

            ObserveFingers();
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
            StartFingerWatch();
            HideLocalRigHands();

            if (Chatty)
                Debug.Log($"[HandRetarget] {Who()}: paired {paired} finger bones " +
                          $"({m_Pairs.Count} driving). Ch36's hands now follow the tracked ones.",
                          this);
        }

        int PairHand(Hand hand, string xrPrefix)
        {
            if (hand?.driverRoot == null || hand.targetRoot == null)
            {
                if (Chatty)
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

            if (Chatty)
                Debug.Log($"[HandRetarget] {Who()}: {xrPrefix} wrists source='{sourceWrist.name}' " +
                          $"target='{targetWrist.name}'", this);

            // The joint list the replicator actually animates, keyed by XRHandJointID. Matching
            // bones by NAME picked the wrong skeleton: the networked hand carries two, their
            // bones are identically named, and the diagnostic caught a second 'L_IndexProximal'
            // sitting under a metacarpal. A name is not an identity here; this list is.
            bool isOwner = IsOwnedHere();
            m_SourceOrigin = "no joint list; bones matched by name";
            Dictionary<string, Transform> animated = Candidate(hand, xrPrefix, m_Candidate, isOwner);
            if (animated != null && animated.Count > 0)
            {
                Transform wristFromDriver = animated.TryGetValue("Wrist", out Transform w) ? w : null;
                if (wristFromDriver != null) sourceWrist = wristFromDriver;
                else if (IsForeignWrist(animated, sourceWrist))
                {
                    // SAYS SO RATHER THAN LIMPING ON. A joint list without a wrist leaves the
                    // fingers measured against whatever the name search found, and when that is a
                    // different skeleton every finger inherits the angle between the two -- the
                    // fingers bend to impossible angles and nothing in the log admits it. One
                    // round was spent on exactly that: local-rig fingers against the networked
                    // hand's 'L_Wrist - Quest'.
                    Debug.LogWarning($"[HandRetarget] {Who()}: {xrPrefix} joint list carries no " +
                                     $"wrist, so the fingers are being measured against " +
                                     $"'{sourceWrist.name}', which is not in the same skeleton. " +
                                     "Expect the fingers to bend wrongly until the list includes " +
                                     "its own wrist.", this);
                }

                if (Chatty)
                    Debug.Log($"[HandRetarget] {Who()}: {xrPrefix} candidate {m_Candidate} is " +
                              $"the {m_SourceOrigin} -- {animated.Count} joints, " +
                              $"wrist='{sourceWrist.name}'.", this);
            }

            BindPose sourceBind = BindPose.Around(sourceWrist);
            BindPose targetBind = BindPose.Around(targetWrist);
            int liveSource = 0, liveTarget = 0;

            // Resolved up front rather than collected as the loop goes, because the thumb's
            // offset needs the palm's normal and the thumb comes first in the map.
            Transform ResolveSource(string xrName) =>
                animated != null && animated.TryGetValue(xrName, out Transform found)
                    ? found
                    : FindBySuffix(sources, xrPrefix + xrName);

            Transform srcIndex = ResolveSource("IndexProximal");
            Transform srcMiddle = ResolveSource("MiddleProximal");
            Transform srcLittle = ResolveSource("LittleProximal");
            Transform tgtIndex = FindBySuffix(targets, "Index1");
            Transform tgtMiddle = FindBySuffix(targets, "Middle1");
            Transform tgtLittle = FindBySuffix(targets, "Pinky1");

            Quaternion palmSource = Quaternion.identity, palmTarget = Quaternion.identity;
            Vector3 upSource = Vector3.up, upTarget = Vector3.up;
            bool havePalm =
                sourceBind.TryPalmFrame(sourceWrist, srcIndex, srcMiddle, srcLittle,
                                        out palmSource, out upSource) &&
                targetBind.TryPalmFrame(targetWrist, tgtIndex, tgtMiddle, tgtLittle,
                                        out palmTarget, out upTarget);

            int geometric = 0;
            int paired = 0;
            foreach (var (xr, mixamo) in k_Map)
            {
                Transform source = animated != null && animated.TryGetValue(xr, out Transform t)
                    ? t
                    : FindBySuffix(sources, xrPrefix + xr);

                Transform target = FindBySuffix(targets, mixamo);

                if (source == null || target == null)
                {
                    if (Chatty)
                        Debug.LogWarning($"[HandRetarget] no pair for {xrPrefix}{xr} -> {mixamo} " +
                                         $"(source={(source != null)}, target={(target != null)})",
                                         this);
                    continue;
                }

                // Bind pose where it is available, live rotation only where it is not. The live
                // reading is correct ONLY if the hand happens to be in its authored pose at this
                // instant, which for the local rig it never is.
                if (!sourceBind.TryRelative(sourceWrist, source, out Quaternion restSource))
                {
                    restSource = Quaternion.Inverse(sourceWrist.rotation) * source.rotation;
                    liveSource++;
                }

                if (!targetBind.TryRelative(targetWrist, target, out Quaternion restTarget))
                {
                    restTarget = Quaternion.Inverse(targetWrist.rotation) * target.rotation;
                    liveTarget++;
                }

                // Takes the source bone's rest orientation to the target's, both measured from
                // their own wrist. Everything the two skeletons disagree about -- axis
                // conventions, roll, how open the bind pose is -- lives here and cancels, AS
                // LONG AS the two bind poses mean the same thing anatomically.
                Quaternion offset = Quaternion.Inverse(restSource) * restTarget;

                // THE THUMBS DO NOT MEAN THE SAME THING, so they are matched by where the bone
                // points instead. Measured: the four fingers' turning axes land 5 to 13 degrees
                // apart, the thumb 123 -- far enough to read on the hand as curling the wrong
                // way. Only the thumb is switched, because only the thumb is broken and the
                // fingers are evidence that the simpler mapping works where it applies.
                if (havePalm && xr.StartsWith("Thumb", System.StringComparison.Ordinal) &&
                    ThumbChild(xr, out string xrChild, out string mixamoChild))
                {
                    Transform srcChild = ResolveSource(xrChild);
                    Transform tgtChild = FindBySuffix(targets, mixamoChild);

                    // The parent gives the joint its hinge. For the metacarpal that is the wrist
                    // itself, which is why these are resolved rather than assumed.
                    Transform srcParent = ThumbParent(xr, out string xrParent, out string mixamoParent)
                        ? ResolveSource(xrParent) : sourceWrist;
                    Transform tgtParent = mixamoParent != null
                        ? FindBySuffix(targets, mixamoParent) : targetWrist;

                    if (sourceBind.TryBoneFrame(source, srcChild, srcParent, upSource,
                                                out Quaternion ks) &&
                        targetBind.TryBoneFrame(target, tgtChild, tgtParent, upTarget,
                                                out Quaternion kt))
                    {
                        offset = ks * Quaternion.Inverse(kt);
                        geometric++;
                    }
                }

                m_Pairs.Add(new Pair
                {
                    source = source,
                    target = target,
                    sourceWrist = sourceWrist,
                    targetWrist = targetWrist,
                    offset = offset,
                    finger = FingerOf(xr),
                });
                paired++;

            }

            // THE WRIST, LAST, BECAUSE THE FINGERS DECIDE IT.
            //
            // The wrist offset is the one quantity here that spans the two skeletons, so unlike a
            // finger it cannot be read from either bind pose alone -- a bind rotation only means
            // something in its own mesh's space. Reading it live instead is what produced the
            // twist: it measures how Ch36's hand sat against the tracked wrist AT THE INSTANT OF
            // CAPTURE, which for the local rig is whatever the player's hand was doing when the
            // show was hosted.
            //
            // So it is derived from the shape of the two hands. The first attempt derived it from
            // the middle proximal's bind ORIENTATION, which aligned the two skeletons' bone axes
            // -- and Mixamo's and XR Hands' differ, so the hand came out rotated by a constant 90
            // degrees. Bone positions carry no such convention: wrist-to-middle runs down the
            // palm and index-to-little crosses it in any skeleton, so the frame they span
            // describes the hand itself.
            //
            //     T_hand = S_wrist * (F_s * inverse(F_t))
            //
            // puts the target's palm frame exactly where the source's is, which is what "the
            // model's hand is turned the way my hand is" actually means.
            Quaternion wristOffset = havePalm
                ? palmSource * Quaternion.Inverse(palmTarget)
                : Quaternion.Inverse(sourceWrist.rotation) * targetWrist.rotation;

            m_WristPairs.Add(new Pair
            {
                source = sourceWrist,
                target = targetWrist,
                sourceWrist = sourceWrist,
                targetWrist = targetWrist,
                offset = wristOffset,

                // -1 left, -2 right. A wrist belongs to no finger, so the field is free to say
                // which side it is, which is what the live nudge below needs.
                finger = xrPrefix == "L_" ? -1 : -2,
            });

            if (Chatty)
            {
                Debug.Log($"[HandRetarget] {Who()}: {xrPrefix} rest from bind pose -- " +
                          $"source {sourceBind.count} bone(s) ({liveSource} fell back to live), " +
                          $"target {targetBind.count} bone(s) ({liveTarget} fell back), " +
                          $"wrist {(havePalm ? "from the palm frame" : "LIVE (no palm frame)")}, " +
                          $"{geometric} thumb joint(s) matched by bone direction.",
                          this);
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
        bool m_HideReported;

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

            // Once, not once per capture. The search below re-captures every eight seconds for as
            // long as nothing is tracked, and this block is two log lines and two hierarchy
            // walks -- repeated forever it would bury the one line worth reading.
            if (!Chatty || m_HideReported) return;
            m_HideReported = true;

            Debug.Log($"[HandRetarget] {Who()}: hid {hidden} renderer(s) on the local rig " +
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
        /// <summary>
        /// How many skeletons there are to try, which depends on whose avatar this is.
        ///
        /// A remote avatar has one fewer: the local rig slot is skipped for it, and the last slot
        /// -- "some other JointBasedHand under the networked hand" -- resolves to nothing here,
        /// because this prefab carries its Quest and AXR wrist variants under a single
        /// JointBasedHand rather than one each. Counting it anyway cost eight seconds per cycle
        /// re-testing a skeleton that had already failed.
        /// </summary>
        int CandidateCount => IsOwnedHere() ? 3 : 2;

        int m_Candidate;

        /// <summary>
        /// The joint set to try, in order of how likely it is to be the animated one ON THIS
        /// MACHINE.
        ///
        /// ORDERED BY OWNERSHIP, because ownership is what decides which skeleton is written to.
        /// Your own networked hand is only ever written on the OTHER machine -- XRINetworkPlayer
        /// switches it off locally and the replicator's m_UpdateHandsLocally is false -- so for
        /// your own avatar it is a frozen pose, and the tracked rig is the only live source. For
        /// an avatar you are merely watching the reverse holds, and the rig is not just wrong but
        /// actively misleading: it belongs to whoever is sitting here, so reading it would drive
        /// THEIR fingers from YOUR hands and still pass the watchdog.
        ///
        ///   your own avatar  -- the local rig's tracked skeleton, then the networked hand
        ///   someone else's   -- the networked hand, and never the local rig
        ///
        /// The order used to be fixed, with the networked hand first for everyone. A two-machine
        /// log showed what that costs: the owner's avatar tried all three in the first 24
        /// seconds, found nothing moving because the host has no headset, and closed the search
        /// 15 seconds before the other player had even connected.
        /// </summary>
        Dictionary<string, Transform> Candidate(Hand hand, string xrPrefix, int index,
                                                bool isOwner)
        {
            int side = xrPrefix == "L_" ? 0 : 1;

            // A remote avatar skips straight past the rig slot, so it can never reach
            // LocalRigJoints however many times the search cycles.
            switch (isOwner ? index : index + 1)
            {
                case 0: return LocalRigJoints(xrPrefix, side);
                case 1: return NetworkedHandJoints(hand, side);
                default: return AnimatedJoints(hand.driverRoot, skip: 1);
            }
        }

        /// <summary>The replicator on this avatar, which owns both of the lists worth reading.</summary>
        XRMultiplayer.XRHandPoseReplicator Replicator()
        {
            if (m_Replicator == null)
                m_Replicator = GetComponentInParent<XRMultiplayer.XRHandPoseReplicator>(true);
            return m_Replicator;
        }

        XRMultiplayer.XRHandPoseReplicator m_Replicator;

        /// <summary>Which list the last pairing used, for the log. The whole problem has been not
        /// knowing this.</summary>
        string m_SourceOrigin = "?";

        /// <summary>
        /// The joints of the local rig's own tracked hand -- the skeleton an XRHandSkeletonDriver
        /// writes from the headset, which is the only one that moves on the machine that owns
        /// the avatar.
        ///
        /// ASKED FOR, NOT LOOKED UP. Deriving this independently is what cost the last round:
        /// XRHandPoseReplicator resolves its skeleton with GetComponentInChildren&lt;&gt;() --
        /// active only -- and the version here passed `true`, so it could settle on a second,
        /// inactive skeleton whose bones carry the same names. On PC B hand tracking was live and
        /// the replicator's origins resolved to 'L_Wrist', while this watched its own
        /// 'L_IndexProximal' sit at 0.0 degrees for eight seconds and moved on. The replicator
        /// hands over the list it actually reads; the old lookup stays only as a fallback for
        /// the frames before that list exists, and the log says which one was used.
        /// </summary>
        Dictionary<string, Transform> LocalRigJoints(string xrPrefix, int side)
        {
            Dictionary<string, Transform> fromReplicator = Replicator()?.LocalRigJoints(side);
            if (fromReplicator != null)
            {
                m_SourceOrigin = "local rig, as the replicator reads it";
                return fromReplicator;
            }

            var modality = FindFirstObjectByType<
                UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager>(
                FindObjectsInactive.Include);
            if (modality == null) return null;

            GameObject sideObject = xrPrefix == "L_" ? modality.leftHand : modality.rightHand;
            if (sideObject == null) return null;

            var driver = sideObject.GetComponentInChildren<UnityEngine.XR.Hands.XRHandSkeletonDriver>(true);
            if (driver?.jointTransformReferences == null) return null;

            var map = new Dictionary<string, Transform>();
            foreach (var reference in driver.jointTransformReferences)
            {
                if (reference.jointTransform == null) continue;
                map[reference.xrHandJointID.ToString()] = reference.jointTransform;
            }
            m_SourceOrigin = "local rig, found by searching the rig (replicator not ready)";
            return map.Count > 0 ? map : null;
        }

        /// <summary>
        /// The networked hand the replicator writes the other player's fingers onto. Same
        /// reasoning as <see cref="LocalRigJoints"/>: the component doing the writing is asked
        /// which skeleton it writes to, rather than the hierarchy being searched for a likely one.
        /// </summary>
        Dictionary<string, Transform> NetworkedHandJoints(Hand hand, int side)
        {
            XRMultiplayer.JointBasedHand networked = Replicator()?.NetworkedHand(side);
            if (networked != null)
            {
                Dictionary<string, Transform> map = JointsOf(networked);
                if (map != null)
                {
                    m_SourceOrigin = "networked hand, as the replicator writes it";
                    return map;
                }
            }

            Dictionary<string, Transform> found = AnimatedJoints(hand.driverRoot);
            if (found != null) m_SourceOrigin = "networked hand, found by searching the avatar";
            return found;
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
            if (chosen == null)
            {
                // A SKIP WITH NOTHING TO SKIP TO HAS NO ANSWER. Falling back to hands[0] here is
                // what made the last candidate a copy of the first: with a single JointBasedHand
                // under the networked hand, "give me the second one" returned the first, and the
                // log shows both candidates reporting the same 19 joints and the same
                // 'L_Wrist - Quest'. Eight seconds of the search went on re-testing a skeleton
                // that had already failed. Null means "no such skeleton", so it is skipped.
                if (skip >= hands.Length) return null;
                chosen = hands[skip];
            }

            return JointsOf(chosen);
        }

        /// <summary>
        /// Whether the wrist about to be used belongs to a different skeleton from the joints.
        /// Finger bones always hang below their own wrist, so one sample settles it.
        /// </summary>
        static bool IsForeignWrist(Dictionary<string, Transform> joints, Transform wrist)
        {
            foreach (Transform joint in joints.Values)
            {
                if (joint == null) continue;
                return !joint.IsChildOf(wrist);
            }
            return false;
        }

        /// <summary>
        /// Each bone's orientation in its own skeleton's BIND POSE, which is the one pose that
        /// does not depend on what the player's hand is doing right now.
        ///
        /// WHY NOT JUST READ .rotation. Capture used to, on the assumption -- written in its own
        /// comment -- that the first frame catches both skeletons still in the pose they were
        /// authored in. That held while the source was the networked hand, which nothing writes
        /// to until replicated data arrives. It stopped holding the moment the owner's first
        /// choice became the LOCAL RIG: the rig is driven by live hand tracking from scene load,
        /// long before the avatar spawns, so whatever the hand happened to be doing when the show
        /// was hosted got baked in as "rest". A wrist held at an angle became the new straight,
        /// and every finger inherited the error.
        ///
        /// A bind pose has no such moment. It is what the mesh was skinned against, readable at
        /// any time, identical on both machines.
        /// </summary>
        sealed class BindPose
        {
            readonly Dictionary<Transform, Quaternion> m_Rotation = new();
            readonly Dictionary<Transform, Vector3> m_Position = new();
            readonly Dictionary<Transform, SkinnedMeshRenderer> m_Owner = new();

            public int count => m_Rotation.Count;

            /// <summary>
            /// The hand's own geometric frame, relative to the wrist bone's bind orientation.
            ///
            /// BUILT FROM WHERE THE BONES ARE, NOT WHICH WAY THEY POINT. Aligning the two
            /// skeletons by a bone's ORIENTATION aligns their axis conventions, and Mixamo's and
            /// XR Hands' differ -- which showed up in the headset as a wrist rotated by a
            /// constant 90 degrees. Positions have no convention: the line from the wrist to the
            /// middle finger runs down the palm in any skeleton, and the line from the index to
            /// the little finger crosses it. Those two give a frame that describes the physical
            /// hand, so matching them matches the hands rather than the rigs.
            /// </summary>
            /// <summary>
            /// The constant rotation from a bone's own axes to the frame its SHAPE defines:
            /// pointing down the bone toward its child, with the palm's normal as up.
            ///
            /// Same argument as the palm frame, one level down. A finger's offset is built by
            /// matching the two skeletons' bone ORIENTATIONS, which works while their bind poses
            /// agree anatomically -- measured at 5 to 13 degrees for the four fingers. The thumbs
            /// do not agree: one rig binds it opposed across the palm, the other more extended,
            /// so matching orientations sends the curl 123 degrees off and the thumb bends
            /// backwards. Where the bone points is not a matter of convention, so a frame built
            /// from it survives the disagreement.
            /// </summary>
            public bool TryBoneFrame(Transform bone, Transform child, Transform parent,
                                     Vector3 fallbackUp, out Quaternion k)
            {
                k = Quaternion.identity;
                if (bone == null || child == null) return false;
                if (!m_Position.TryGetValue(bone, out Vector3 pb)) return false;
                if (!m_Position.TryGetValue(child, out Vector3 pc)) return false;
                if (!m_Rotation.TryGetValue(bone, out Quaternion rb)) return false;
                if (m_Owner[bone] != m_Owner[child]) return false;

                Vector3 dir = pc - pb;
                if (dir.sqrMagnitude < 1e-10f) return false;

                // THE JOINT'S OWN HINGE, where the bind pose shows one. A finger joint bends in
                // the plane its parent and child bones lie in, so the normal of that plane IS
                // the flexion axis -- anatomy, not convention, and it does not care that the two
                // rigs hold their thumbs differently. The palm normal was the first choice and
                // is a poor one for the thumb: an opposed thumb lies close to the palm's normal,
                // so it barely constrains the roll, which is how the curl ended up 67 degrees
                // off its proper axis after the first attempt.
                Vector3 up = fallbackUp;
                if (parent != null &&
                    m_Position.TryGetValue(parent, out Vector3 pp) &&
                    m_Owner.TryGetValue(parent, out SkinnedMeshRenderer owner) &&
                    owner == m_Owner[bone])
                {
                    Vector3 inbound = pb - pp;

                    // A straight chain has no bend plane; its cross product is numerical noise
                    // pointing nowhere in particular, so the palm normal remains the better
                    // reference in that case.
                    if (inbound.sqrMagnitude > 1e-10f && Vector3.Angle(inbound, dir) > 8f)
                        up = Vector3.Cross(inbound, dir).normalized;
                }

                // Nothing usable to orient the roll with.
                if (Vector3.Cross(dir, up).sqrMagnitude < 1e-10f) return false;

                k = Quaternion.Inverse(rb) * Quaternion.LookRotation(dir, up);
                return true;
            }

            public bool TryPalmFrame(Transform wrist, Transform index, Transform middle,
                                     Transform little, out Quaternion frame, out Vector3 normal)
            {
                frame = Quaternion.identity;
                normal = Vector3.up;
                if (wrist == null || index == null || middle == null || little == null) return false;
                if (!m_Position.TryGetValue(wrist, out Vector3 pw)) return false;
                if (!m_Position.TryGetValue(index, out Vector3 pi)) return false;
                if (!m_Position.TryGetValue(middle, out Vector3 pm)) return false;
                if (!m_Position.TryGetValue(little, out Vector3 pl)) return false;
                if (!m_Rotation.TryGetValue(wrist, out Quaternion rw)) return false;
                if (m_Owner[wrist] != m_Owner[middle]) return false;

                Vector3 forward = pm - pw;                  // down the palm, toward the fingers
                Vector3 across = pl - pi;                   // across the knuckles
                if (forward.sqrMagnitude < 1e-10f || across.sqrMagnitude < 1e-10f) return false;

                Vector3 up = Vector3.Cross(forward, across); // the palm's normal
                if (up.sqrMagnitude < 1e-10f) return false;  // bones collinear: no plane to find

                // In mesh space, then re-expressed against the wrist's own bind orientation so
                // the mesh's overall orientation cancels and only the hand's shape remains.
                normal = up.normalized;
                frame = Quaternion.Inverse(rw) * Quaternion.LookRotation(forward, up);
                return true;
            }

            /// <summary>Every bind rotation under the hierarchy that <paramref name="anyBone"/> belongs to.</summary>
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

                        // bindposes[i] takes mesh space to bone space, so its inverse is the
                        // bone's own placement in mesh space -- the authored pose.
                        Matrix4x4 bind = binds[i].inverse;
                        pose.m_Rotation[bones[i]] = bind.rotation;
                        pose.m_Position[bones[i]] = bind.GetColumn(3);
                        pose.m_Owner[bones[i]] = skin;
                    }
                }
                return pose;
            }

            /// <summary>
            /// <paramref name="bone"/>'s bind orientation relative to <paramref name="wrist"/>'s.
            ///
            /// Both must be skinned to the SAME mesh, because a bind rotation is only meaningful
            /// in its own mesh's space; measuring one mesh's bone against another's would
            /// reintroduce exactly the cross-skeleton error this is here to remove. Taking the
            /// two relative to each other also cancels the mesh's own orientation, so it does not
            /// matter that Ch36 and a tracked hand are authored facing different ways.
            /// </summary>
            public bool TryRelative(Transform wrist, Transform bone, out Quaternion relative)
            {
                relative = Quaternion.identity;
                if (wrist == null || bone == null) return false;
                if (!m_Rotation.TryGetValue(wrist, out Quaternion w)) return false;
                if (!m_Rotation.TryGetValue(bone, out Quaternion b)) return false;
                if (m_Owner[wrist] != m_Owner[bone]) return false;

                relative = Quaternion.Inverse(w) * b;
                return true;
            }
        }

        /// <summary>
        /// The next joint down the thumb, in both naming schemes. The tip has none, which is also
        /// where the target's bind data runs out -- Ch36's Thumb4 carries no skin weights -- so
        /// the last joint keeps the ordinary offset.
        /// </summary>
        static bool ThumbChild(string xr, out string xrChild, out string mixamoChild)
        {
            switch (xr)
            {
                case "ThumbMetacarpal": xrChild = "ThumbProximal"; mixamoChild = "Thumb2"; return true;
                case "ThumbProximal":   xrChild = "ThumbDistal";   mixamoChild = "Thumb3"; return true;
                case "ThumbDistal":     xrChild = "ThumbTip";      mixamoChild = "Thumb4"; return true;
                default:                xrChild = null;            mixamoChild = null;    return false;
            }
        }

        /// <summary>
        /// The joint above this one in the thumb. False means the wrist, which has no name in
        /// either scheme that this map would recognise.
        /// </summary>
        static bool ThumbParent(string xr, out string xrParent, out string mixamoParent)
        {
            switch (xr)
            {
                case "ThumbProximal": xrParent = "ThumbMetacarpal"; mixamoParent = "Thumb1"; return true;
                case "ThumbDistal":   xrParent = "ThumbProximal";   mixamoParent = "Thumb2"; return true;
                default:              xrParent = null;              mixamoParent = null;    return false;
            }
        }

        /// <summary>The joints one JointBasedHand drives, keyed by XRHandJointID name.</summary>
        static Dictionary<string, Transform> JointsOf(XRMultiplayer.JointBasedHand hand)
        {
            if (hand.handFidelityOptions == null || hand.handFidelityOptions.Length == 0)
                return null;

            var map = new Dictionary<string, Transform>();

            foreach (var finger in hand.handFidelityOptions[0].fingerJoints)
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

        /// <summary>
        /// A candidate has demonstrably animated, so the search is over for good. Separate from
        /// <see cref="m_WatchReported"/>, which Capture resets on every attempt.
        /// </summary>
        bool m_SourceConfirmed;

        /// <summary>Whether the full "nothing moved" block has been printed. Once is enough.</summary>
        bool m_ExhaustedReported;

        void Observe(Quaternion relative)
        {
            m_WatchMaxAngle = Mathf.Max(m_WatchMaxAngle, Quaternion.Angle(m_WatchRest, relative));
        }

        // --- per-finger: does the motion arriving actually reach Ch36? ------------------------
        //
        // "The thumb does not follow, or moves too little to see" is two very different faults
        // wearing the same face. Either the thumb's source joints are not moving -- nothing to
        // follow -- or they are moving and the retarget is sending that motion somewhere
        // invisible. The offset conjugates the source's rotation, which preserves the ANGLE and
        // changes the AXIS, so a wrong offset turns a finger curling into that finger rolling
        // about its own length: full motion, nothing to see. Measuring the source and the target
        // separately tells the two apart, and nothing else does.

        static readonly string[] k_Fingers =
            { "ThumbProximal", "IndexProximal", "MiddleProximal", "RingProximal", "LittleProximal" };

        readonly int[] m_FingerPair = new int[5];
        readonly Quaternion[] m_FingerRestSource = new Quaternion[5];
        readonly Quaternion[] m_FingerRestTarget = new Quaternion[5];
        readonly float[] m_FingerMovedSource = new float[5];
        readonly float[] m_FingerMovedTarget = new float[5];

        // WHERE EACH FINGER POINTS, relative to its own palm. Not where it was told to point.
        //
        // The previous attempt compared the source's rotation with the target's and always
        // reported zero, because it could not do anything else: the retarget writes
        // target = targetWrist * relative * offset, so measuring the target's change from rest
        // gives back the source's change with the offset cancelled -- the same quantity twice,
        // whatever the offset happens to be. A diagnostic that cannot fail is not a diagnostic.
        //
        // Direction is independent of that. Each finger's bone points somewhere in its own hand's
        // palm frame, built from where the knuckles are rather than how the bones are oriented,
        // and the two hands should agree. A mirrored splay cannot hide from it: the finger points
        // to the other side of the palm's midline.
        readonly int[] m_FingerChildPair = new int[5];
        readonly float[] m_FingerAimSpread = new float[5];

        /// <summary>Set by StartFingerWatch, cleared on the first frame that has retargeted.</summary>
        bool m_FingerRestPending;

        void StartFingerWatch()
        {
            for (int f = 0; f < 5; f++)
            {
                m_FingerPair[f] = -1;
                m_FingerChildPair[f] = -1;
                m_FingerMovedSource[f] = 0f;
                m_FingerMovedTarget[f] = 0f;
                m_FingerAimSpread[f] = 0f;
                m_FingerRestPending = true;

                for (int i = 0; i < m_Pairs.Count; i++)
                {
                    Pair p = m_Pairs[i];
                    if (p.source == null || !p.source.name.EndsWith(k_Fingers[f],
                            System.StringComparison.Ordinal)) continue;

                    m_FingerPair[f] = i;

                    // The next entry in the map is this finger's next joint down, for every
                    // finger including the thumb, so the pair after this one gives the bone's
                    // far end -- and the line between them is the direction it points.
                    m_FingerChildPair[f] =
                        i + 1 < m_Pairs.Count && m_Pairs[i + 1].targetWrist == p.targetWrist
                            ? i + 1
                            : -1;
                    break;   // the first match is the left hand, which is enough to judge by
                }
            }
        }

        void ObserveFingers()
        {
            for (int f = 0; f < 5; f++)
            {
                int i = m_FingerPair[f];
                if (i < 0 || i >= m_Pairs.Count) continue;

                Pair p = m_Pairs[i];
                if (p.source == null || p.target == null) continue;

                Quaternion s = Quaternion.Inverse(p.sourceWrist.rotation) * p.source.rotation;
                Quaternion t = Quaternion.Inverse(p.targetWrist.rotation) * p.target.rotation;

                if (m_FingerRestPending)
                {
                    m_FingerRestSource[f] = s;
                    m_FingerRestTarget[f] = t;
                    if (f == 4) m_FingerRestPending = false;
                    continue;
                }

                float movedSource = Quaternion.Angle(m_FingerRestSource[f], s);
                if (movedSource > m_FingerMovedSource[f]) m_FingerMovedSource[f] = movedSource;

                // Where the two fingers actually point, each in its own palm's frame. Worst
                // disagreement is kept rather than the latest, because a hand passes through the
                // pose where the two happen to agree on its way to the one where they do not.
                int c = m_FingerChildPair[f];
                if (c < 0 || c >= m_Pairs.Count) continue;

                Pair child = m_Pairs[c];
                if (child.source == null || child.target == null) continue;

                if (!LivePalm(m_FingerPair[1], m_FingerPair[2], m_FingerPair[4], true,
                              out Quaternion palmS) ||
                    !LivePalm(m_FingerPair[1], m_FingerPair[2], m_FingerPair[4], false,
                              out Quaternion palmT)) continue;

                Vector3 aimS = Quaternion.Inverse(palmS) *
                               (child.source.position - p.source.position).normalized;
                Vector3 aimT = Quaternion.Inverse(palmT) *
                               (child.target.position - p.target.position).normalized;

                m_FingerAimSpread[f] = Mathf.Max(m_FingerAimSpread[f], Vector3.Angle(aimS, aimT));

                m_FingerMovedTarget[f] = Mathf.Max(m_FingerMovedTarget[f],
                                                   Quaternion.Angle(m_FingerRestTarget[f], t));
            }
        }

        /// <summary>
        /// The hand's frame right now, from where the knuckles are. Built the same way for both
        /// skeletons and from positions alone, so it carries no rig's conventions with it --
        /// which is the whole point of comparing in it.
        /// </summary>
        bool LivePalm(int indexPair, int middlePair, int littlePair, bool source,
                      out Quaternion palm)
        {
            palm = Quaternion.identity;
            if (indexPair < 0 || middlePair < 0 || littlePair < 0) return false;
            if (indexPair >= m_Pairs.Count || middlePair >= m_Pairs.Count ||
                littlePair >= m_Pairs.Count) return false;

            Transform wrist = source ? m_Pairs[indexPair].sourceWrist : m_Pairs[indexPair].targetWrist;
            Transform index = source ? m_Pairs[indexPair].source : m_Pairs[indexPair].target;
            Transform middle = source ? m_Pairs[middlePair].source : m_Pairs[middlePair].target;
            Transform little = source ? m_Pairs[littlePair].source : m_Pairs[littlePair].target;
            if (wrist == null || index == null || middle == null || little == null) return false;

            Vector3 forward = middle.position - wrist.position;
            Vector3 across = little.position - index.position;
            Vector3 up = Vector3.Cross(forward, across);
            if (forward.sqrMagnitude < 1e-10f || up.sqrMagnitude < 1e-10f) return false;

            palm = Quaternion.LookRotation(forward, up);
            return true;
        }

        string FingerReport()
        {
            var report = new StringBuilder();
            for (int f = 0; f < 5; f++)
            {
                if (m_FingerPair[f] < 0) { report.Append($"\n[HandRetarget]     {k_Fingers[f]}: NOT PAIRED"); continue; }

                float src = m_FingerMovedSource[f];
                float aim = m_FingerAimSpread[f];

                string verdict = src < 5f
                    ? "source barely moved -- nothing to judge by"
                    : aim > 60f
                        ? $"POINTING {aim:0} deg AWAY from where mine points"
                        : aim > 25f
                            ? $"off by {aim:0} deg"
                            : $"matches within {aim:0} deg";

                report.Append($"\n[HandRetarget]     {k_Fingers[f]}: moved {src:0.0} deg, " +
                              $"aim {verdict}");
            }
            return report.ToString();
        }

        /// <summary>
        /// After a grace period, say whether the bones being read have moved at all -- and if
        /// not, name every other transform with the same bone name, which is the list the right
        /// one is hiding in.
        /// </summary>
        void ReportIfSourceIsStatic()
        {
            if (m_SourceConfirmed || m_WatchReported ||
                m_WatchIndex < 0 || Time.unscaledTime < m_WatchDeadline) return;
            m_WatchReported = true;

            Pair p = m_Pairs[m_WatchIndex];

            if (m_WatchMaxAngle > 2f)
            {
                m_SourceConfirmed = true;
                Debug.Log($"[HandRetarget] {Who()}: source is animating -- '{p.source.name}' " +
                          $"moved {m_WatchMaxAngle:0.0} degrees (candidate {m_Candidate}). " +
                          "Ch36 is following it." + FingerReport(), this);
                return;
            }

            // NOT MOVING, SO TRY THE NEXT SKELETON. Which one carries the finger animation is not
            // something this can know in advance: the owner's networked hand is only written to
            // on OTHER machines, the local rig has its own tracked skeleton, and the bones in all
            // of them share names. Rather than guess again, it watches, and if nothing moved it
            // moves on. The candidate that moves is the right one by definition.
            //
            // AND IT KEEPS TRYING, rather than giving up after one pass. Three strikes was a race
            // the host cannot win: PC A has no headset, so for the first half-minute nothing
            // anywhere in the scene is moving. The two-machine log shows all three candidates
            // spent and the search closed 15 seconds BEFORE the second player connected, after
            // which that avatar stayed pinned to a skeleton nothing ever writes to -- frozen
            // fingers for the rest of the show, and no further log line to say so. A source that
            // is not moving YET is not the same as a wrong source, and only motion can tell them
            // apart, so the search now runs until it finds motion.
            m_Candidate = (m_Candidate + 1) % CandidateCount;

            bool firstPass = !m_ExhaustedReported;
            if (m_Candidate != 0)
            {
                // Quiet after the first pass: one line per candidate per eight seconds, for as
                // long as nothing is tracked, would bury everything else in the log.
                if (firstPass)
                    Debug.LogWarning($"[HandRetarget] {Who()}: '{p.source.name}' never moved; " +
                                     $"trying source candidate {m_Candidate} instead.", this);
                Capture();
                return;
            }

            if (!firstPass)
            {
                // Been round them all before. Keep cycling in case tracking starts later, but say
                // nothing more until something actually moves.
                Capture();
                return;
            }

            m_ExhaustedReported = true;

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
            Debug.LogWarning($"[HandRetarget] {Who()}: source '{p.source.name}' has not moved " +
                             $"({m_WatchMaxAngle:0.0} degrees in 8s), so Ch36's fingers have " +
                             "nothing to follow. Either nothing is being tracked -- check for " +
                             "the 'neither a tracked hand nor a controller is active' warning " +
                             "-- or this is not the skeleton being animated. Still watching; " +
                             "this will say so if one of them starts moving." + (others.Length > 0
                                 ? "\n[HandRetarget]   other transforms with that name:" + others
                                 : "\n[HandRetarget]   no other transform carries that name.")
                             + FingerReport(),
                             this);

            Capture();
        }

        /// <summary>
        /// Which avatar these lines are about.
        ///
        /// Every [HandRetarget] line used to be anonymous, and with two avatars in one scene the
        /// only way to tell them apart in a log was that HideLocalRigHands happens to print for
        /// the owner alone -- so reading a two-machine run meant inferring identity from an
        /// unrelated message three lines away. Several rounds of guessing came out of that. It
        /// says whose hands it is talking about now.
        /// </summary>
        string Who()
        {
            var player = GetComponentInParent<XRMultiplayer.XRINetworkPlayer>(true);
            if (player == null) return "no player";
            if (player.IsOwner) return "my own avatar";
            return player.IsSpawned ? $"their avatar (client {player.OwnerClientId})"
                                    : "their avatar";
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
