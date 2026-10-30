using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using XRMultiplayer;
using CompositeBody.Diagnostics;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Calibrates the shared origin when the player pinches index finger and thumb together on
    /// the marker. The pinch point gives the position and the head gives the facing, so the
    /// venue needs one physical marker rather than two.
    ///
    /// Why a pinch and not a button: both players are hand-tracked ghosts with no controllers,
    /// and the pinch is the one gesture Quest tracks reliably enough to use as a confirm.
    ///
    /// Two safeguards, because a stray recalibration mid-show would move one player's whole
    /// world relative to the other's and there is no way to undo it from inside a headset. The
    /// pinch has to be <i>held</i>, so brushing the fingers together does nothing; and it only
    /// counts while armed, which by default means the lobby, before the piece starts.
    /// </summary>
    public class HandPinchCalibrator : MonoBehaviour, IDebugInteraction
    {
        [SerializeField, Tooltip("The physical marker to line up with. Found in the scene if left empty.")]
        CalibrationPoint m_Marker;

        [Header("Gesture")]
        [SerializeField, Tooltip("Which hand confirms. Right by default.")]
        Handedness m_Hand = Handedness.Right;

        [SerializeField, Min(0.001f), Tooltip("Fingertip gap, in metres, below which the pinch counts as closed.")]
        float m_PinchEnter = 0.022f;

        [SerializeField, Min(0.001f), Tooltip("Gap it has to reopen past before another pinch can count. Wider than the close threshold so a hand held near the boundary does not chatter.")]
        float m_PinchExit = 0.038f;

        [SerializeField, Min(0f), Tooltip("How long the pinch must be held. Short enough not to be a chore, long enough that an accidental touch is not a recalibration.")]
        float m_HoldSeconds = 0.6f;

        [Header("Safety")]
        [SerializeField, Tooltip("Only accept a pinch while the session is still in the lobby. Staff can re-arm from the control panel if it has to be redone.")]
        bool m_ArmedInLobbyOnly = true;

        static readonly List<XRHandSubsystem> s_Subsystems = new();

        XRHandSubsystem m_Subsystem;
        XROrigin m_XROrigin;

        bool m_PinchClosed;
        float m_ClosedFor;
        bool m_ConsumedThisPinch;
        bool m_StaffArmed;

        /// <summary>True once this headset has lined up with the marker at least once.</summary>
        public bool hasCalibrated { get; private set; }

        /// <summary>How far through the hold the current pinch is, 0..1. For staff feedback.</summary>
        public float holdProgress =>
            m_PinchClosed && !m_ConsumedThisPinch && m_HoldSeconds > 0f
                ? Mathf.Clamp01(m_ClosedFor / m_HoldSeconds)
                : 0f;

        public bool isArmed
        {
            get
            {
                if (m_StaffArmed) return true;
                if (!m_ArmedInLobbyOnly) return true;

                var session = GameSessionManager.Instance;
                return session == null || session.sessionState == SessionState.Lobby;
            }
        }

        /// <summary>Lets the player calibrate again after the piece has started. Staff only.</summary>
        public void Arm()
        {
            m_StaffArmed = true;
            Utils.Log("[HandPinchCalibrator] Armed by staff; next held pinch will recalibrate.");
        }

        public void Disarm() => m_StaffArmed = false;

        // AWAKE, NOT ONENABLE, so a SWITCHED-OFF interaction still gets a row. This component
        // ships disabled -- the headset's own room setup stands in for a shared origin until the
        // piece needs one -- and a debug list that simply omitted it would be saying "this
        // interaction does not exist", which is the one answer that sends someone looking in the
        // wrong place. Awake runs on a disabled component of an active object; OnEnable does not.
        void Awake() => InteractionDebug.Register(this);

        void OnDestroy() => InteractionDebug.Unregister(this);

        void Start()
        {
            if (m_Marker == null) m_Marker = FindAnyObjectByType<CalibrationPoint>();
            if (m_Marker == null)
                Utils.LogWarning("[HandPinchCalibrator] No CalibrationPoint in scene; pinching will do nothing.");
        }

        void Update()
        {
            if (!TryGetSubsystem()) return;

            XRHand hand = m_Hand == Handedness.Left ? m_Subsystem.leftHand : m_Subsystem.rightHand;
            if (!hand.isTracked)
            {
                ResetPinch();
                return;
            }

            if (!TryGetPinch(hand, out Vector3 trackingSpacePoint, out float gap))
            {
                ResetPinch();
                return;
            }

            // Hysteresis: close on the tight threshold, reopen only past the looser one.
            if (!m_PinchClosed && gap <= m_PinchEnter) m_PinchClosed = true;
            else if (m_PinchClosed && gap >= m_PinchExit) ResetPinch();

            if (!m_PinchClosed) return;

            m_ClosedFor += Time.deltaTime;
            if (m_ConsumedThisPinch || m_ClosedFor < m_HoldSeconds) return;

            // One calibration per pinch, however long it is held after this.
            m_ConsumedThisPinch = true;

            if (!isArmed)
            {
                Utils.Log("[HandPinchCalibrator] Pinch held but not armed; ignoring. " +
                          "Staff can re-arm from the control panel.");
                return;
            }

            Calibrate(trackingSpacePoint);
        }

        void Calibrate(Vector3 trackingSpacePoint)
        {
            if (m_Marker == null) return;
            if (!TryGetOrigin()) return;

            Transform trackingSpace = TrackingSpace();
            Vector3 worldPinch = trackingSpace.TransformPoint(trackingSpacePoint);

            // A pinch the player can actually see is within arm's reach of their own head. If
            // the computed point is nowhere near it, the joint poses are being read in the wrong
            // space, and calibrating on it would throw the origin somewhere arbitrary -- so say
            // so rather than silently moving the world.
            Transform cam = m_XROrigin.Camera.transform;
            float reach = Vector3.Distance(worldPinch, cam.position);
            if (reach > 1.5f)
            {
                Utils.LogError($"[HandPinchCalibrator] Pinch resolved {reach:0.00}m from the head, which is " +
                               "further than an arm. Hand poses are probably being converted in the wrong " +
                               "space; refusing to calibrate.");
                return;
            }

            m_Marker.CalibrateAtPoint(worldPinch);
            hasCalibrated = true;
            m_StaffArmed = false;

            Utils.Log($"[HandPinchCalibrator] Calibrated on a {m_Hand} pinch {reach:0.00}m from the head.");
        }

        /// <summary>
        /// The space XR Hands reports joint poses in, which is the same space the camera's
        /// tracked pose is applied in -- the floor offset object when the rig has one, otherwise
        /// the origin itself. Getting this wrong is what the reach check above catches.
        /// </summary>
        Transform TrackingSpace()
        {
            if (m_XROrigin.CameraFloorOffsetObject != null)
                return m_XROrigin.CameraFloorOffsetObject.transform;
            return m_XROrigin.transform;
        }

        bool TryGetPinch(XRHand hand, out Vector3 midpoint, out float gap)
        {
            midpoint = Vector3.zero;
            gap = float.MaxValue;

            XRHandJoint index = hand.GetJoint(XRHandJointID.IndexTip);
            XRHandJoint thumb = hand.GetJoint(XRHandJointID.ThumbTip);

            if (!index.TryGetPose(out Pose indexPose)) return false;
            if (!thumb.TryGetPose(out Pose thumbPose)) return false;

            // The gap is a distance between two poses in the same space, so it needs no
            // conversion; only the midpoint does, and only once the pinch is confirmed.
            gap = Vector3.Distance(indexPose.position, thumbPose.position);
            midpoint = (indexPose.position + thumbPose.position) * 0.5f;
            return true;
        }

        void ResetPinch()
        {
            m_PinchClosed = false;
            m_ClosedFor = 0f;
            m_ConsumedThisPinch = false;
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

        #region Debug panel

        public string debugId => "calibration";

        public string debugLabel => "Calibrate origin (hold a pinch on the marker)";

        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        /// <summary>
        /// ARM, NOT FIRE, and the button says so. A fabricated pinch would hand Calibrate a
        /// point nobody's hand was at, which moves this player's whole world relative to the
        /// other's with no way to undo it from inside a headset -- and the reach check would
        /// refuse it anyway. So the button does the part staff are allowed to do: let the next
        /// real pinch count.
        /// </summary>
        public string debugFireLabel => "Arm";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = hasCalibrated;

            if (!isActiveAndEnabled)
            {
                // The gesture is wired and switched off, which is a state worth naming: nothing
                // is broken, nothing will happen either, and Update is not running to notice.
                state.detail = "component disabled -- the pinch gesture is not being watched";
                return state;
            }

            string armed = isArmed ? "armed" : "not armed";
            float progress = holdProgress;

            state.detail = m_Marker == null
                ? "no CalibrationPoint in the scene"
                : progress > 0f
                    ? $"{armed}, holding {progress * 100f:0}%"
                    : armed;

            return state;
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            // Arming a disabled calibrator sets a flag that nothing reads, since Update is what
            // acts on it -- so the button is dead rather than quietly useless.
            if (target == DebugFireTarget.Local) return isActiveAndEnabled && !isArmed;
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
                Arm();
                return;
            }

            if (target != DebugFireTarget.Player2) ArmRole(PlayerRole.Player1);
            if (target != DebugFireTarget.Player1) ArmRole(PlayerRole.Player2);
        }

        static void ArmRole(PlayerRole role)
        {
            if (InteractionDebugRelay.TryGetClientForRole(role, out ulong clientId))
                InteractionDebugRelay.Instance?.FireOnClient(clientId, "calibration");
        }

        /// <summary>Disarming is the one half of this that can be taken back.</summary>
        public bool DebugCanReset() => m_StaffArmed;

        public void DebugReset() => Disarm();

        #endregion

        bool TryGetOrigin()
        {
            if (m_XROrigin != null && m_XROrigin.Camera != null) return true;

            m_XROrigin = FindAnyObjectByType<XROrigin>();
            if (m_XROrigin == null || m_XROrigin.Camera == null)
            {
                Utils.LogError("[HandPinchCalibrator] No XROrigin with a camera; cannot calibrate.");
                return false;
            }
            return true;
        }
    }
}
