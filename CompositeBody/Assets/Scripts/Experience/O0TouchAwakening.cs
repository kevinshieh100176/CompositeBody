using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;
using CompositeBody.PointClouds;

namespace CompositeBody.Experience
{
    /// <summary>
    /// 「觀眾和真人觸碰後，觀眾與真人的地面似乎在旋轉，像是音樂盒。同時觀眾的手開始成形，雙手從粒子與
    /// 膜的流動裡出現，像從真人身上剝落出來的延伸。」
    ///
    /// The one interactive moment in O-0, and the only thing in the beat that is NOT on the
    /// Timeline. The opening is authored and runs to a clock; this waits for a person to reach
    /// out, which may be ten seconds after the 光圈 come up or may be two minutes. Putting it on
    /// the timeline would mean deciding for them when they were curious.
    ///
    /// THE TOUCH TEST IS ANALYTIC, not a collider. The 真人 are point clouds of several hundred
    /// thousand loose points with no surface to collide against, and a mesh collider built from
    /// them would be meaningless even if it were affordable. A vertical capsule through the
    /// figure's own mesh bounds is what a body is to within a few centimetres, costs a dot
    /// product, and -- unlike a trigger -- cannot be missed by a hand moving faster than one
    /// physics step.
    ///
    /// Bounds are taken from the MeshFilter, not the renderer: the renderer's localBounds are
    /// deliberately inflated by 2.5 m so a cloud that drifts and dissolves never culls early, and
    /// using those would let a player "touch" the figure from across the room.
    ///
    /// LOCAL, NOT NETWORKED. Each player's own hands form from their own touch, which is right --
    /// the hands are theirs. The plates are per station, so two people touching different 真人
    /// wind different plates. If both players should see one plate start when either of them
    /// touches, that is a networked event and is not built.
    /// </summary>
    public class O0TouchAwakening : MonoBehaviour, IDebugInteraction
    {
        [System.Serializable]
        public class Station
        {
            [Tooltip("The 真人 that can be touched.")]
            public PointCloudFigure figure;

            [Tooltip("The plate under them. Winds up on the touch.")]
            public MusicBoxFloor floor;

            [HideInInspector] public bool touched;
        }

        [SerializeField] Station[] m_Stations = new Station[0];

        [SerializeField, Tooltip("The plate under the player, which follows them. Optional.")]
        MusicBoxFloor m_PlayerFloor;

        [SerializeField, Tooltip("Both hands. They form together -- the script says 雙手.")]
        HandCloud[] m_Hands = new HandCloud[0];

        [Header("Touch")]
        [SerializeField, Range(0.02f, 0.5f), Tooltip("Metres of slack around the figure's own " +
                                                     "silhouette. A scanned body is not where it " +
                                                     "looks to within a centimetre, and reaching " +
                                                     "for something that is not solid overshoots.")]
        float m_TouchSlack = 0.12f;

        [SerializeField, Range(0.05f, 0.6f), Tooltip("Radius of the capsule through the figure, " +
                                                     "as a fraction of its width.")]
        float m_BodyRadius = 0.32f;

        [Header("Timing")]
        [SerializeField, Min(0.1f)] float m_SpinSeconds = 7f;
        [SerializeField, Min(0.1f)] float m_HandSeconds = 5.5f;

        [SerializeField, Min(0f), Tooltip("Pause between the touch landing and the hands starting " +
                                          "to arrive. A beat of nothing is what makes the hands " +
                                          "read as a consequence rather than as a cursor.")]
        float m_HandDelay = 0.6f;

        static readonly System.Collections.Generic.List<XRHandSubsystem> s_Subsystems = new();

        XRHandSubsystem m_Subsystem;
        XROrigin m_Origin;
        bool m_Awakened;
        float m_TouchTime;
        Vector3 m_TouchPoint;

        /// <summary>Whether anyone has touched a 真人 yet.</summary>
        public bool awakened => m_Awakened;

        /// <summary>Where the first touch landed, in world space.</summary>
        public Vector3 touchPoint => m_TouchPoint;

        /// <summary>
        /// Fire the whole thing without a hand, for testing from a desktop and for staff rescue
        /// if hand tracking has dropped out mid-show.
        /// </summary>
        public void ForceAwaken(Vector3 worldPoint)
        {
            if (m_Awakened) return;
            m_Awakened = true;
            m_TouchPoint = worldPoint;
            m_TouchTime = ExperienceClock.now;

            foreach (Station station in m_Stations)
            {
                if (station?.floor != null) station.floor.SpinTo(1f, m_SpinSeconds);
            }
            if (m_PlayerFloor != null) m_PlayerFloor.SpinTo(1f, m_SpinSeconds);

            foreach (HandCloud hand in m_Hands)
            {
                if (hand != null) hand.FormFrom(worldPoint);
            }
        }

        // Awake rather than OnEnable, so an interaction that is switched off still has a row --
        // "absent from the list" and "not built yet" must not look the same.
        void Awake() => InteractionDebug.Register(this);

        void OnDestroy() => InteractionDebug.Unregister(this);

        void LateUpdate()
        {
            if (!m_Awakened) PollForTouch();
            else DriveForming();

            FollowPlayer();
        }

        void PollForTouch()
        {
            if (!TryGetSubsystem() || !TryGetOrigin()) return;

            Transform space = m_Origin.CameraFloorOffsetObject != null
                ? m_Origin.CameraFloorOffsetObject.transform
                : m_Origin.transform;

            foreach (Handedness handedness in new[] { Handedness.Left, Handedness.Right })
            {
                XRHand hand = handedness == Handedness.Left ? m_Subsystem.leftHand : m_Subsystem.rightHand;
                if (!hand.isTracked) continue;

                // The index tip and the palm. A touch with the back of the wrist is not a touch,
                // and testing every joint would fire when a sleeve passed nearby.
                if (!TryJoint(hand, XRHandJointID.IndexTip, space, out Vector3 tip) &&
                    !TryJoint(hand, XRHandJointID.Palm, space, out tip)) continue;

                for (int i = 0; i < m_Stations.Length; i++)
                {
                    Station station = m_Stations[i];
                    if (station?.figure == null) continue;
                    // A station hidden because it belongs to the other player cannot be
                    // touched -- you should not be able to reach what you cannot see.
                    if (!station.figure.gameObject.activeInHierarchy) continue;
                    if (!Touching(station.figure, tip)) continue;

                    station.touched = true;
                    ForceAwaken(tip);

                    // 「像從真人身上剝落出來的延伸」. The cloud sheds toward the hand rather than
                    // simply dissolving, so what arrives on the player looks like it came off
                    // the body they just put their hand on.
                    station.figure.DriftToward(tip);
                    station.figure.GlitchBurst(0.55f, 0.5f);

                    // 「同時觀眾的手開始成形」 -- the player's own body starts arriving on the
                    // same touch. Local only: this is what they see of themselves.
                    if (AvatarFormation.local != null) AvatarFormation.local.Form();
                    return;
                }
            }
        }

        void DriveForming()
        {
            float t = ExperienceClock.now - m_TouchTime - m_HandDelay;
            float r = Mathf.Clamp01(t / m_HandSeconds);
            r = r * r * (3f - 2f * r);

            foreach (HandCloud hand in m_Hands)
            {
                if (hand != null) hand.reveal = r;
            }
        }

        /// <summary>The plate stays under the player, because they walk and it is their ground.</summary>
        void FollowPlayer()
        {
            if (m_PlayerFloor == null || !TryGetOrigin() || m_Origin.Camera == null) return;
            Vector3 head = m_Origin.Camera.transform.position;
            Transform plate = m_PlayerFloor.transform;
            plate.position = new Vector3(head.x, plate.position.y, head.z);
        }

        /// <summary>
        /// Distance from a point to the figure's vertical capsule. Cheap enough to run for every
        /// hand against every figure every frame without thinking about it.
        /// </summary>
        bool Touching(PointCloudFigure figure, Vector3 point)
        {
            var filter = figure.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;

            Transform t = figure.transform;
            Bounds local = filter.sharedMesh.bounds;
            Vector3 centre = t.TransformPoint(local.center);
            Vector3 size = Vector3.Scale(local.size, t.lossyScale);

            float half = Mathf.Max(size.y * 0.5f - size.x * m_BodyRadius, 0f);
            Vector3 up = t.up * half;
            Vector3 a = centre - up, b = centre + up;

            Vector3 ab = b - a;
            float denom = Vector3.Dot(ab, ab);
            float u = denom > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / denom) : 0f;
            float distance = Vector3.Distance(point, a + ab * u);

            float radius = Mathf.Max(size.x, size.z) * m_BodyRadius + m_TouchSlack;
            return distance <= radius;
        }

        static bool TryJoint(XRHand hand, XRHandJointID id, Transform space, out Vector3 world)
        {
            world = default;
            XRHandJoint joint = hand.GetJoint(id);
            if (!joint.TryGetPose(out Pose pose)) return false;
            world = space.TransformPoint(pose.position);
            return true;
        }

        bool TryGetSubsystem()
        {
            if (m_Subsystem != null && m_Subsystem.running) return true;
            SubsystemManager.GetSubsystems(s_Subsystems);
            foreach (var subsystem in s_Subsystems)
            {
                if (subsystem == null || !subsystem.running) continue;
                m_Subsystem = subsystem;
                return true;
            }
            m_Subsystem = null;
            return false;
        }

        bool TryGetOrigin()
        {
            if (m_Origin != null) return true;
            m_Origin = FindFirstObjectByType<XROrigin>();
            return m_Origin != null;
        }

        #region Debug panel

        // LOCAL SCOPE, BECAUSE THE INTERACTION IS LOCAL. Each player's hands form from their own
        // touch and nothing about that crosses the network -- so the row describes this machine,
        // and the relay mirrors it so one screen can still show both.
        public string debugId => "o0.awakening";

        // ASCII, because the panel's font has no CJK glyphs; see AvatarFormationSync.debugLabel.
        public string debugLabel => "Touch the scan figure -- hands, plate, body";

        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        public string debugFireLabel => "Touch";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = m_Awakened;

            if (m_Awakened)
            {
                float since = ExperienceClock.now - m_TouchTime;
                float reveal = Mathf.Clamp01((since - m_HandDelay) / Mathf.Max(m_HandSeconds, 0.01f));
                state.detail = $"{TouchedStations()} touched, hands {reveal * 100f:0}%";
            }
            else if (!isActiveAndEnabled)
            {
                state.detail = "inactive -- its beat is not running, so no touch is being watched for";
            }
            else
            {
                // Says WHY nothing can happen, which is the question someone asks of a touch
                // that will not fire: without hand tracking the poll never gets as far as the
                // capsule test, and that looks identical to a capsule that is too small.
                state.detail = m_Stations.Length == 0
                    ? "no stations wired"
                    : (TryGetSubsystem() ? "waiting for a hand on a scan figure" : "no hand tracking on this machine");
            }

            return state;
        }

        string TouchedStations()
        {
            int touched = 0;
            foreach (Station station in m_Stations)
            {
                if (station != null && station.touched) touched++;
            }
            return $"{touched}/{m_Stations.Length} station(s)";
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            // Firing while the beat's content is switched off would set the flag and then never
            // drive the forming, because LateUpdate is not running.
            if (target == DebugFireTarget.Local) return isActiveAndEnabled && !m_Awakened;
            if (!InteractionDebugRelay.canForce) return false;

            bool p1 = InteractionDebugRelay.TryGetClientForRole(PlayerRole.Player1, out _);
            bool p2 = InteractionDebugRelay.TryGetClientForRole(PlayerRole.Player2, out _);

            return target switch
            {
                DebugFireTarget.Player1 => p1,
                DebugFireTarget.Player2 => p2,
                DebugFireTarget.Both => p1 || p2,
                _ => false,
            };
        }

        public void DebugFire(DebugFireTarget target)
        {
            if (!DebugCanFire(target)) return;

            if (target == DebugFireTarget.Local)
            {
                // Through ForceAwaken, which is the staff rescue path the beat already has, so
                // the panel fires exactly what a person reaching out would.
                ForceAwaken(DebugTouchPoint());
                return;
            }

            if (target != DebugFireTarget.Player2) FireForRole(PlayerRole.Player1);
            if (target != DebugFireTarget.Player1) FireForRole(PlayerRole.Player2);
        }

        static void FireForRole(PlayerRole role)
        {
            if (InteractionDebugRelay.TryGetClientForRole(role, out ulong clientId))
                InteractionDebugRelay.Instance?.FireOnClient(clientId, "o0.awakening");
        }

        /// <summary>
        /// Where a debug touch lands: the first station's own centre if there is one. The point
        /// matters -- the hands shed from it and the cloud drifts toward it -- so a fabricated
        /// touch at the origin would play the beat with its one piece of staging wrong.
        /// </summary>
        Vector3 DebugTouchPoint()
        {
            foreach (Station station in m_Stations)
            {
                if (station?.figure == null) continue;
                var filter = station.figure.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                return station.figure.transform.TransformPoint(filter.sharedMesh.bounds.center);
            }
            return transform.position;
        }

        // NO RESET. The touch spins plates up, sheds a cloud toward a hand and starts a body
        // arriving; there is no reverse for any of it, and a button that un-awakened only the
        // flag would leave the room looking touched and reporting untouched.
        public bool DebugCanReset() => false;

        public void DebugReset() { }

        #endregion

        void OnDrawGizmosSelected()
        {
            // The capsule a hand actually has to reach, so a touch that will not fire is visible
            // before anyone is standing in a headset wondering why.
            Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.7f);
            foreach (Station station in m_Stations)
            {
                if (station?.figure == null) continue;
                var filter = station.figure.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                Transform t = station.figure.transform;
                Bounds local = filter.sharedMesh.bounds;
                Vector3 centre = t.TransformPoint(local.center);
                Vector3 size = Vector3.Scale(local.size, t.lossyScale);
                float radius = Mathf.Max(size.x, size.z) * m_BodyRadius + m_TouchSlack;
                Gizmos.DrawWireSphere(centre, radius);
                Gizmos.DrawWireSphere(centre + t.up * (size.y * 0.5f - radius), radius);
                Gizmos.DrawWireSphere(centre - t.up * (size.y * 0.5f - radius), radius);
            }
        }
    }
}
