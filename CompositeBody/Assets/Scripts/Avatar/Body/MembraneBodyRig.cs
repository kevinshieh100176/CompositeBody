using UnityEngine;
using XRMultiplayer;

namespace CompositeBody.Avatar.Body
{
    /// <summary>
    /// Poses the Ch36 body from the only three things a headset knows: where the eyes are and
    /// where the two hands are.
    ///
    /// The three transforms this reads are already the avatar's networked ones, so the body
    /// costs nothing on the wire and is solved identically on every machine -- the owner and
    /// everyone watching run the same solve against the same replicated poses. Sending joint
    /// angles instead would have been a second, slower copy of a pose everyone can already
    /// derive.
    ///
    /// What is solved, and what is not:
    /// - The body hangs off the head rather than standing on the floor. A headset is the one
    ///   thing whose position is certain, so anchoring to it keeps the body attached to the
    ///   player's view; anchoring to the floor would let the head drift out of the neck.
    /// - The feet are planted and the knees solved, so crouching bends the legs instead of
    ///   sinking the figure through the floor. They stay under the hips, which means the figure
    ///   glides rather than steps -- there is no foot tracking to step with.
    /// - The spine stays straight. A real lean bends at the waist; here the whole body
    ///   translates with the head, which reads fine standing and poorly bent over.
    ///
    /// The hand bones are collapsed, because the hands come from elsewhere: the avatar's own
    /// tracked hand visuals for everyone watching, and the local rig's hands for the player
    /// themselves. Both have live finger tracking and their own membrane films, which this body
    /// has no way to match -- Ch36's fingers would be a static splay. The head is collapsed for
    /// the owner alone: the membrane shader is two-sided, so from inside your own skull you
    /// would be looking at the back of your own face.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class MembraneBodyRig : MonoBehaviour
    {
        [Header("Tracked targets")]
        [SerializeField, Tooltip("The avatar's networked head. Its pose is the headset's, so the eyes -- not the head bone -- belong here.")]
        Transform m_HeadTarget;

        [SerializeField, Tooltip("The avatar's networked left hand. The left wrist is solved onto this.")]
        Transform m_LeftHandTarget;

        [SerializeField, Tooltip("The avatar's networked right hand.")]
        Transform m_RightHandTarget;

        [Header("Spine")]
        [SerializeField] Transform m_Hips;

        [SerializeField, Tooltip("Upper spine. Takes a share of the head's turn, so a look carries into the chest instead of twisting the neck alone.")]
        Transform m_Chest;

        [SerializeField] Transform m_Neck;
        [SerializeField] Transform m_HeadBone;

        [Header("Arms")]
        [SerializeField, Tooltip("The clavicles. They swing a fraction of the way toward whatever the hand is reaching for, which is both what a real shoulder does and where the last few centimetres of reach come from.")]
        Transform m_LeftShoulder, m_RightShoulder;

        [SerializeField] Transform m_LeftUpperArm, m_LeftForeArm, m_LeftHandBone;
        [SerializeField] Transform m_RightUpperArm, m_RightForeArm, m_RightHandBone;

        [Header("Legs")]
        [SerializeField] Transform m_LeftUpperLeg, m_LeftLowerLeg, m_LeftFoot;
        [SerializeField] Transform m_RightUpperLeg, m_RightLowerLeg, m_RightFoot;

        [Header("Fit")]
        [SerializeField, Tooltip("Where the eyes sit relative to the head bone, in head-bone space. The head bone is in the middle of the skull, so a body aligned to it directly wears the headset inside its forehead.")]
        Vector3 m_EyeOffset = new(0f, 0.105f, 0.085f);

        [SerializeField, Tooltip("Height of the floor in the shared space. The session calibrates both players against one origin, and the floor of that origin is zero.")]
        float m_FloorY;

        [SerializeField, Tooltip("Smallest and largest the body may be scaled to fit a player's standing height.")]
        Vector2 m_ScaleRange = new(0.75f, 1.35f);

        [SerializeField, Tooltip("How fast the standing-height estimate may fall, in metres per second, while the player is upright. The body is fitted to the tallest the player has stood, so that a crouch bends the knees rather than shrinking the body.")]
        float m_StandingDecayRate = 0.05f;

        [Header("Carriage")]
        [SerializeField, Tooltip("How far the head may turn before the torso follows, in degrees.")]
        float m_TorsoYawThreshold = 25f;

        [SerializeField, Tooltip("How fast the torso catches up once it is following.")]
        float m_TorsoYawSpeed = 3f;

        [SerializeField, Range(0f, 1f), Tooltip("Share of the head's turn taken by the neck.")]
        float m_NeckFollow = 0.45f;

        [SerializeField, Range(0f, 1f), Tooltip("Share of the head's turn taken by the chest.")]
        float m_ChestFollow = 0.18f;

        [SerializeField, Range(0f, 1f), Tooltip("How far a clavicle swings toward what the hand is reaching for. A real one barely moves, so this stays small -- it is here for the reach it adds and for the shrug of an overhead reach, not to aim the arm.")]
        float m_ShoulderFollow = 0.2f;

        [SerializeField, Tooltip("Which way the elbows break, in torso space: out, down and back. Mirrored for the left arm.")]
        Vector3 m_ElbowHint = new(0.25f, -0.85f, -0.5f);

        [SerializeField, Tooltip("Which way the knees break, in torso space.")]
        Vector3 m_KneeHint = new(0f, 0f, 1f);

        [SerializeField, Tooltip("How far an arm may stretch past its own length to reach a hand. Ch36's arms are a few centimetres shorter than a real arm on a body of the same height, and the hand is drawn by a separate mesh -- so an arm that stops short does not look short, it looks like a hand floating off the end of a forearm. One disables the stretch.")]
        float m_MaxArmStretch = 1.25f;

        [Header("What this body does not draw")]
        [SerializeField, Tooltip("Collapse the hand bones. The hands are drawn by the avatar's tracked hand visuals and by the local rig, both of which have finger tracking this body cannot match.")]
        bool m_CollapseHands = true;

        [SerializeField, Tooltip("Collapse the head bone for the player who owns this avatar. The membrane is two-sided, so without this you are looking at the inside of your own face.")]
        bool m_CollapseHeadForOwner = true;

        [SerializeField, Tooltip("Scale a collapsed bone is taken down to. Not zero: a zero-scale bone makes its skinning matrix singular.")]
        float m_CollapsedBoneScale = 0.01f;

        /// <summary>
        /// A two-bone chain -- shoulder, elbow, wrist or hip, knee, ankle -- and the bind pose it
        /// is solved back from every frame.
        /// </summary>
        struct Limb
        {
            public Transform upper, lower, end;

            Vector3 m_UpperChildDir, m_LowerChildDir;
            Quaternion m_UpperBind, m_LowerBind;
            Vector3 m_LowerBindPos, m_EndBindPos;
            float m_UpperLength, m_LowerLength;

            public bool valid => upper != null && lower != null && end != null;

            public void Capture()
            {
                if (!valid) return;

                // Which of the bone's own axes points at the next joint. Measured rather than
                // assumed: it is +Y down the bone on this rig, but a solver that hard-codes that
                // breaks silently on the next character.
                m_UpperChildDir = upper.InverseTransformDirection((lower.position - upper.position).normalized);
                m_LowerChildDir = lower.InverseTransformDirection((end.position - lower.position).normalized);

                m_UpperBind = upper.localRotation;
                m_LowerBind = lower.localRotation;

                // The offsets that are the bones' lengths. Kept so a limb can be stretched by
                // scaling them, and restored every frame so the stretch cannot creep.
                m_LowerBindPos = lower.localPosition;
                m_EndBindPos = end.localPosition;

                m_UpperLength = Vector3.Distance(upper.position, lower.position);
                m_LowerLength = Vector3.Distance(lower.position, end.position);
            }

            /// <summary>
            /// Puts <see cref="end"/> on <paramref name="target"/> with the middle joint pushed
            /// toward <paramref name="hint"/>. Solved from the bind pose rather than from last
            /// frame's result, so the bones cannot accumulate twist over a long session.
            /// </summary>
            /// <param name="maxStretch">How far past its own length the limb may be stretched to
            /// reach. One leaves the limb rigid, and short of anything out of reach.</param>
            public void Solve(Vector3 target, Vector3 hint, float scale, float maxStretch)
            {
                if (!valid) return;

                upper.localRotation = m_UpperBind;
                lower.localRotation = m_LowerBind;
                lower.localPosition = m_LowerBindPos;
                end.localPosition = m_EndBindPos;

                Vector3 root = upper.position;
                Vector3 toTarget = target - root;
                float distance = toTarget.magnitude;
                if (distance < 1e-5f) return;

                float stretch = 1f;
                if (maxStretch > 1f)
                {
                    float span = (m_UpperLength + m_LowerLength) * scale;
                    if (span > 1e-5f && distance > span)
                    {
                        stretch = Mathf.Min(distance / span, maxStretch);
                        lower.localPosition = m_LowerBindPos * stretch;
                        end.localPosition = m_EndBindPos * stretch;
                    }
                }

                float a = m_UpperLength * scale * stretch;
                float b = m_LowerLength * scale * stretch;

                // A limb cannot reach further than it is long, nor fold tighter than the
                // difference between its two bones. Outside that range the law of cosines has no
                // answer, so the reach is clamped and the limb points at what it cannot touch.
                float reach = Mathf.Clamp(distance, Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);
                Vector3 direction = toTarget / distance;

                Vector3 axis = Vector3.Cross(direction, hint);
                if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(direction, Vector3.up);
                if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(direction, Vector3.forward);
                axis.Normalize();

                float cos = Mathf.Clamp((a * a + reach * reach - b * b) / (2f * a * reach), -1f, 1f);
                float bend = Mathf.Acos(cos) * Mathf.Rad2Deg;

                // Turning the limb this way about this axis swings the joint toward the hint,
                // which is what decides whether an elbow breaks backwards or a knee the wrong way.
                Vector3 upperDirection = Quaternion.AngleAxis(bend, axis) * direction;
                Vector3 joint = root + upperDirection * a;

                Aim(upper, m_UpperChildDir, upperDirection);
                Aim(lower, m_LowerChildDir, target - joint);
            }

            static void Aim(Transform bone, Vector3 localChildDir, Vector3 worldDir)
            {
                if (worldDir.sqrMagnitude < 1e-8f) return;
                Vector3 current = bone.TransformDirection(localChildDir);
                bone.rotation = Quaternion.FromToRotation(current, worldDir) * bone.rotation;
            }
        }

        /// <summary>
        /// How far below the standing estimate still counts as standing. Below it the player is
        /// taken to be crouching, and the estimate is left alone.
        /// </summary>
        const float k_StandingBand = 0.9f;

        Limb m_LeftArm, m_RightArm, m_LeftLeg, m_RightLeg;

        // Bind-pose geometry, all held in this object's own space so it survives the body being
        // moved, turned and scaled underneath it.
        Vector3 m_BindHips, m_BindHeadBone, m_BindLeftAnkle, m_BindRightAnkle;
        Quaternion m_BindChest, m_BindNeck, m_HeadCorrection, m_BindLeftFoot, m_BindRightFoot;
        Quaternion m_BindLeftShoulder, m_BindRightShoulder;
        Vector3 m_LeftShoulderChildDir, m_RightShoulderChildDir;
        float m_BindEyeHeight;

        float m_TorsoYaw, m_TorsoYawGoal;
        float m_StandingEyeHeight = -1f;
        bool m_OwnerView;
        bool m_Captured;

        XRINetworkPlayer m_Player;

        /// <summary>True once this avatar is known to belong to the player looking at it. The one
        /// difference between the two views: your own head is not drawn.</summary>
        public bool ownerView => m_OwnerView;

        /// <summary>
        /// Every bone the solve needs is wired. Reads the serialized fields rather than the
        /// captured chains, so it answers on a prefab asset too -- where nothing has run Awake,
        /// and the chains are therefore empty even on a correctly wired rig.
        /// </summary>
        public bool bonesResolved =>
            m_Hips != null && m_Neck != null && m_HeadBone != null &&
            m_LeftUpperArm != null && m_LeftForeArm != null && m_LeftHandBone != null &&
            m_RightUpperArm != null && m_RightForeArm != null && m_RightHandBone != null &&
            m_LeftUpperLeg != null && m_LeftLowerLeg != null && m_LeftFoot != null &&
            m_RightUpperLeg != null && m_RightLowerLeg != null && m_RightFoot != null;

        /// <summary>True once the bind pose has been measured and the chains are solvable. Only
        /// meaningful at runtime.</summary>
        public bool chainsCaptured =>
            m_Captured && m_LeftArm.valid && m_RightArm.valid && m_LeftLeg.valid && m_RightLeg.valid;

        /// <summary>Where the left wrist is being solved to, for the same reason.</summary>
        public Transform leftHandTarget => m_LeftHandTarget;

        public Transform rightHandTarget => m_RightHandTarget;

        public Transform leftHandBone => m_LeftHandBone;

        public Transform rightHandBone => m_RightHandBone;

        void Awake()
        {
            Capture();

            // The avatar prefab is spawned for every player, and nothing on it says which one is
            // yours until the network says so. Asking the template rather than re-deriving it
            // keeps one answer to that question.
            m_Player = GetComponentInParent<XRINetworkPlayer>();
            if (m_Player != null) m_Player.onSpawnedLocal += OnSpawnedLocal;
        }

        void OnDestroy()
        {
            if (m_Player != null) m_Player.onSpawnedLocal -= OnSpawnedLocal;
        }

        void OnSpawnedLocal() => m_OwnerView = true;

        /// <summary>
        /// Reads the bind pose once. Everything the solve needs is measured here, while the body
        /// is still standing in the pose it was imported in.
        /// </summary>
        void Capture()
        {
            if (m_Captured) return;

            Quaternion toLocal = Quaternion.Inverse(transform.rotation);

            if (m_Hips != null) m_BindHips = transform.InverseTransformPoint(m_Hips.position);

            if (m_HeadBone != null)
            {
                m_BindHeadBone = transform.InverseTransformPoint(m_HeadBone.position);
                m_HeadCorrection = toLocal * m_HeadBone.rotation;
            }

            if (m_Chest != null) m_BindChest = toLocal * m_Chest.rotation;
            if (m_Neck != null) m_BindNeck = toLocal * m_Neck.rotation;

            if (m_LeftFoot != null)
            {
                m_BindLeftAnkle = transform.InverseTransformPoint(m_LeftFoot.position);
                m_BindLeftFoot = toLocal * m_LeftFoot.rotation;
            }

            if (m_RightFoot != null)
            {
                m_BindRightAnkle = transform.InverseTransformPoint(m_RightFoot.position);
                m_BindRightFoot = toLocal * m_RightFoot.rotation;
            }

            if (m_LeftShoulder != null && m_LeftUpperArm != null)
            {
                m_BindLeftShoulder = m_LeftShoulder.localRotation;
                m_LeftShoulderChildDir = m_LeftShoulder.InverseTransformDirection(
                    (m_LeftUpperArm.position - m_LeftShoulder.position).normalized);
            }

            if (m_RightShoulder != null && m_RightUpperArm != null)
            {
                m_BindRightShoulder = m_RightShoulder.localRotation;
                m_RightShoulderChildDir = m_RightShoulder.InverseTransformDirection(
                    (m_RightUpperArm.position - m_RightShoulder.position).normalized);
            }

            m_BindEyeHeight = m_BindHeadBone.y + m_EyeOffset.y;

            m_LeftArm = new Limb { upper = m_LeftUpperArm, lower = m_LeftForeArm, end = m_LeftHandBone };
            m_RightArm = new Limb { upper = m_RightUpperArm, lower = m_RightForeArm, end = m_RightHandBone };
            m_LeftLeg = new Limb { upper = m_LeftUpperLeg, lower = m_LeftLowerLeg, end = m_LeftFoot };
            m_RightLeg = new Limb { upper = m_RightUpperLeg, lower = m_RightLowerLeg, end = m_RightFoot };

            m_LeftArm.Capture();
            m_RightArm.Capture();
            m_LeftLeg.Capture();
            m_RightLeg.Capture();

            m_Captured = true;
        }

        /// <summary>
        /// Runs late and after the template: the owner's head and hands are written in
        /// <see cref="XRINetworkPlayer"/>'s own LateUpdate, and a remote player's arrive from
        /// their network transforms earlier in the frame. Solving before either would be solving
        /// against the previous frame's pose.
        /// </summary>
        void LateUpdate()
        {
            if (m_HeadTarget == null || m_Hips == null || m_HeadBone == null) return;

            float scale = FitToPlayer();
            PlaceBody(scale);
            PoseSpine();
            PoseArms(scale);
            PoseLegs(scale);
            ApplyCollapses();
        }

        /// <summary>
        /// Scales the body to the player's standing height, measured from the headset down to the
        /// floor and held at the tallest it has seen, so that a crouch bends the knees instead.
        /// </summary>
        float FitToPlayer()
        {
            if (m_BindEyeHeight <= 1e-3f) return 1f;

            float eyeHeight = m_HeadTarget.position.y - m_FloorY;

            if (m_StandingEyeHeight < 0f)
            {
                m_StandingEyeHeight = eyeHeight;
            }
            else if (eyeHeight > m_StandingEyeHeight)
            {
                // Taller than anything seen so far, so this is the new standing height. Taken at
                // once rather than eased into: a body that is the wrong size is worth fixing on
                // the frame the right size becomes known.
                m_StandingEyeHeight = eyeHeight;
            }
            else if (eyeHeight > m_StandingEyeHeight * k_StandingBand)
            {
                // Only trimmed while the player is still roughly upright. The estimate has to be
                // able to come down -- a headset carried above head height before being put on
                // would otherwise oversize the body for the whole session -- but a crouch must
                // not bring it down, or holding a crouch would slowly turn a crouching player
                // into a small standing one.
                m_StandingEyeHeight = Mathf.Max(eyeHeight, m_StandingEyeHeight - m_StandingDecayRate * Time.deltaTime);
            }

            return Mathf.Clamp(m_StandingEyeHeight / m_BindEyeHeight, m_ScaleRange.x, m_ScaleRange.y);
        }

        /// <summary>Turns the torso after the head and hangs the hips below the headset.</summary>
        void PlaceBody(float scale)
        {
            // Yaw from the flattened look direction rather than from euler angles, which fold
            // into nonsense the moment a player looks at their feet or straight up.
            Vector3 forward = m_HeadTarget.forward;
            Vector3 flat = new(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 0.01f)
            {
                Vector3 up = m_HeadTarget.up;
                flat = new Vector3(up.x, 0f, up.z);
            }

            float headYaw = flat.sqrMagnitude > 1e-6f
                ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg
                : m_TorsoYaw;

            // The torso holds still through small head turns and then swings round to catch up,
            // which is roughly what a standing body does and, more to the point, keeps the hips
            // from jittering with every glance.
            if (Mathf.Abs(Mathf.DeltaAngle(m_TorsoYaw, headYaw)) >= m_TorsoYawThreshold)
                m_TorsoYawGoal = headYaw;

            m_TorsoYaw = Mathf.LerpAngle(m_TorsoYaw, m_TorsoYawGoal, Time.deltaTime * m_TorsoYawSpeed);

            Quaternion torso = Quaternion.Euler(0f, m_TorsoYaw, 0f);

            transform.localScale = Vector3.one * scale;
            transform.rotation = torso;

            // Where the head bone has to be for the eyes to land in the headset, and then where
            // the hips have to be for the head bone to land there.
            //
            // The eye offset is turned by the head's yaw alone, never its pitch or roll. A skull
            // turns about the neck under it, so turning your head carries the skull a few
            // centimetres round with it -- but nodding does not move your body at all, and an
            // offset that took the full head rotation would raise the hips as you looked down and
            // lift the planted feet off the floor with them.
            Vector3 headBone = m_HeadTarget.position - Quaternion.Euler(0f, headYaw, 0f) * (m_EyeOffset * scale);
            Vector3 hips = headBone - torso * ((m_BindHeadBone - m_BindHips) * scale);

            // Moved by the gap between where the hips are and where they belong: the hips are the
            // joint everything else hangs from, so anchoring there means the rest of the body
            // does not have to be told that it moved.
            transform.position += hips - m_Hips.position;
        }

        /// <summary>
        /// Spreads the head's turn down the neck and chest, then sets the head itself exactly on
        /// the headset. A head that took the whole turn alone reads as a snapped neck.
        /// </summary>
        void PoseSpine()
        {
            Quaternion torso = transform.rotation;
            Quaternion deviation = m_HeadTarget.rotation * Quaternion.Inverse(torso);

            if (m_Chest != null)
                m_Chest.rotation = Quaternion.Slerp(Quaternion.identity, deviation, m_ChestFollow) * (torso * m_BindChest);

            if (m_Neck != null)
                m_Neck.rotation = Quaternion.Slerp(Quaternion.identity, deviation, m_NeckFollow) * (torso * m_BindNeck);

            // Set last, and in world space, so that whatever share the neck and chest took, the
            // head still ends up pointing exactly where the headset does.
            m_HeadBone.rotation = m_HeadTarget.rotation * m_HeadCorrection;
        }

        void PoseArms(float scale)
        {
            Quaternion torso = transform.rotation;

            // Shoulders first: they move the joint the arm is solved from.
            PoseShoulder(m_LeftShoulder, m_BindLeftShoulder, m_LeftShoulderChildDir, m_LeftHandTarget);
            PoseShoulder(m_RightShoulder, m_BindRightShoulder, m_RightShoulderChildDir, m_RightHandTarget);

            if (m_LeftHandTarget != null)
                m_LeftArm.Solve(m_LeftHandTarget.position,
                                torso * new Vector3(-m_ElbowHint.x, m_ElbowHint.y, m_ElbowHint.z),
                                scale, m_MaxArmStretch);

            if (m_RightHandTarget != null)
                m_RightArm.Solve(m_RightHandTarget.position, torso * m_ElbowHint, scale, m_MaxArmStretch);
        }

        /// <summary>
        /// Swings a clavicle part of the way toward what its hand is reaching for. Partly so that
        /// an overhead reach lifts the shoulder with it, and partly for the reach: the arm is
        /// solved from this joint, so moving it toward the hand is reach the arm does not have to
        /// stretch for.
        /// </summary>
        void PoseShoulder(Transform clavicle, Quaternion bindLocal, Vector3 childDir, Transform target)
        {
            if (clavicle == null || target == null) return;

            clavicle.localRotation = bindLocal;

            Vector3 toTarget = target.position - clavicle.position;
            if (toTarget.sqrMagnitude < 1e-6f) return;

            Vector3 current = clavicle.TransformDirection(childDir);
            Quaternion full = Quaternion.FromToRotation(current, toTarget.normalized);

            clavicle.rotation = Quaternion.Slerp(Quaternion.identity, full, m_ShoulderFollow) * clavicle.rotation;
        }

        /// <summary>
        /// Plants both feet on the floor under the hips and bends the knees to reach them. The
        /// feet do not step -- nothing tracks them -- but they do stay on the ground, which is
        /// the difference between a player crouching and a player sinking.
        /// </summary>
        void PoseLegs(float scale)
        {
            Quaternion torso = transform.rotation;
            Vector3 hips = m_Hips.position;
            Vector3 hint = torso * m_KneeHint;

            PlantFoot(ref m_LeftLeg, m_BindLeftAnkle, m_BindLeftFoot, hips, torso, hint, scale);
            PlantFoot(ref m_RightLeg, m_BindRightAnkle, m_BindRightFoot, hips, torso, hint, scale);
        }

        void PlantFoot(ref Limb leg, Vector3 bindAnkle, Quaternion bindFoot, Vector3 hips,
                       Quaternion torso, Vector3 hint, float scale)
        {
            if (!leg.valid) return;

            Vector3 stance = torso * (new Vector3(bindAnkle.x - m_BindHips.x, 0f, bindAnkle.z - m_BindHips.z) * scale);
            Vector3 target = new(hips.x + stance.x, m_FloorY + bindAnkle.y * scale, hips.z + stance.z);

            // No stretch on a leg. An arm that cannot reach is a hand coming off a forearm, but a
            // leg that cannot reach the floor is a player off the ground, which is what it should
            // look like.
            leg.Solve(target, hint, scale, 1f);

            // Flat on the floor and facing the way the body faces, rather than following the
            // shin: a solved knee leaves the ankle pointing wherever the chain ended up, which on
            // a planted foot looks like the figure standing on its toes.
            leg.end.rotation = torso * bindFoot;
        }

        /// <summary>
        /// Takes the parts of this body that something else is already drawing down to nothing.
        /// Scale rather than a renderer switch, because the body and the membrane over it are one
        /// mesh each: there is no way to hide a hand without hiding the arm it is on.
        /// </summary>
        void ApplyCollapses()
        {
            Vector3 collapsed = Vector3.one * m_CollapsedBoneScale;

            if (m_CollapseHands)
            {
                if (m_LeftHandBone != null) m_LeftHandBone.localScale = collapsed;
                if (m_RightHandBone != null) m_RightHandBone.localScale = collapsed;
            }

            if (m_CollapseHeadForOwner)
                m_HeadBone.localScale = m_OwnerView ? collapsed : Vector3.one;
        }
    }
}
