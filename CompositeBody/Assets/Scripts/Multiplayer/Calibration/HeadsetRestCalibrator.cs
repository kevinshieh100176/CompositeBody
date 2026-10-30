using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using XRMultiplayer;
using CompositeBody.Diagnostics;

// Aliased rather than imported: UnityEngine.InputSystem and UnityEngine.XR both define
// InputDevice and CommonUsages, and this file needs the keyboard from one and the headset's
// tracking state from the other.
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRNode = UnityEngine.XR.XRNode;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Calibrates the shared origin from the headset itself, sitting in the cradle on the
    /// physical marker, with staff pressing a key on that headset's own PC.
    ///
    /// WHY THIS AND NOT THE PINCH. <see cref="HandPinchCalibrator"/> takes its position from a
    /// fingertip and its facing from the player's head, and the head is the weak half: asking
    /// someone to "face the marker" is good to perhaps five degrees, which is a quarter of a
    /// metre of disagreement between the two headsets at three metres away, and it grows with
    /// distance. A headset resting in a cradle reports its own pose to the accuracy of its own
    /// tracking, nobody is estimating anything, and the person involved only presses a key.
    ///
    /// ARM, THEN CAPTURE WHEN IT SETTLES, rather than sampling on the keystroke. Staff place the
    /// headset and walk to the PC, or press first and then place it; either way the pose at the
    /// instant of the press may be a hand still letting go. The key arms a capture and the
    /// capture happens on the first frame the headset has been still long enough to believe,
    /// which also means both orders of operation give the same answer.
    ///
    /// WHAT IT REFUSES, because reducing human error is mostly about the errors a key press can
    /// still make: a headset that is not tracking (taken off, proximity sensor asleep, Link
    /// dropped), a headset that is still moving, and a headset whose tilt does not match the
    /// cradle, which is what being on someone's head looks like from here. A refusal moves
    /// nothing and says why.
    /// </summary>
    public class HeadsetRestCalibrator : MonoBehaviour, IDebugInteraction, IDebugNudgeable
    {
        [SerializeField, Tooltip("The marker whose rest pose the headset is lined up with. Found in the scene if left empty.")]
        CalibrationPoint m_Marker;

        [Header("Trigger")]
        [SerializeField, Tooltip("The key staff press on this machine's own PC. Each PC calibrates its own headset.")]
        Key m_CalibrateKey = Key.C;

        [SerializeField, Min(0f), Tooltip("How long an armed capture waits for the headset to be still before giving up. Long enough to press the key, walk over and place the headset.")]
        float m_ArmSeconds = 30f;

        [Header("Settling")]
        [SerializeField, Tooltip("Wait for the headset to stop moving before reading its pose. Off only for a desk test where nothing is tracking.")]
        bool m_RequireStillness = true;

        [SerializeField, Min(0.05f), Tooltip("How long the headset has to hold still before the pose is believed.")]
        float m_StillSeconds = 0.4f;

        [SerializeField, Min(0.0001f), Tooltip("Per-frame movement, in metres, under which the headset counts as still. Tracking noise on a headset lying on the floor is a few tenths of a millimetre.")]
        float m_StillPosition = 0.001f;

        [SerializeField, Min(0.001f), Tooltip("Per-frame rotation, in degrees, under which the headset counts as still.")]
        float m_StillAngle = 0.15f;

        [Header("Reporting")]
        [SerializeField, Tooltip("StoryProgressManager task to report once this headset has calibrated, so a timeline hold can wait for both of them. Empty reports nothing.")]
        string m_CompletionTaskId = "";

        XROrigin m_XROrigin;

        bool m_Armed;
        float m_ArmedUntil;

        bool m_HavePrevious;
        Vector3 m_PreviousPosition;
        Quaternion m_PreviousRotation;
        float m_StillFor;

        string m_LastFailure;

        // Hand-correction: where the origin sat before staff started nudging it, so one button
        // puts it back, and a running total for the row to show.
        bool m_HaveNudgeSnapshot;
        Vector3 m_PreNudgePosition;
        Quaternion m_PreNudgeRotation;
        Vector3 m_NudgedMetres;
        float m_NudgedDegrees;

        // Where this component last left the origin, so it can tell when something else has
        // moved it since.
        Vector3 m_LastLeftPosition;
        Quaternion m_LastLeftRotation;

        /// <summary>True once this headset has been lined up with the cradle at least once.</summary>
        public bool hasCalibrated { get; private set; }

        /// <summary>True while a key press is waiting for the headset to settle.</summary>
        public bool isArmed => m_Armed;

        /// <summary>Arms a capture. The next settled, tracked, correctly-tilted pose is taken.</summary>
        public void Arm()
        {
            m_Armed = true;
            m_ArmedUntil = Time.unscaledTime + m_ArmSeconds;
            m_LastFailure = null;
            Utils.Log("[HeadsetRest] Armed. Place the headset in the cradle; it calibrates as soon as " +
                      $"it is still. Gives up in {m_ArmSeconds:0}s.");
        }

        public void Cancel()
        {
            if (!m_Armed) return;
            m_Armed = false;
            Utils.Log("[HeadsetRest] Capture cancelled.");
        }

        // AWAKE, NOT ONENABLE, so a switched-off calibrator still gets a debug row -- the same
        // reason HandPinchCalibrator does it. A missing row reads as "this does not exist",
        // which is the one answer that sends someone looking in the wrong place.
        void Awake() => InteractionDebug.Register(this);

        void OnDestroy() => InteractionDebug.Unregister(this);

        void Start()
        {
            if (m_Marker == null) m_Marker = FindAnyObjectByType<CalibrationPoint>();
            if (m_Marker == null)
                Utils.LogWarning("[HeadsetRest] No CalibrationPoint in the scene; the key will do nothing.");
        }

        void Update()
        {
            if (Keyboard.current != null && m_CalibrateKey != Key.None &&
                Keyboard.current[m_CalibrateKey].wasPressedThisFrame)
            {
                Arm();
            }

            TrackStillness();

            if (!m_Armed) return;

            if (Time.unscaledTime > m_ArmedUntil)
            {
                m_Armed = false;
                m_LastFailure = "timed out waiting for the headset to be still";
                Utils.LogWarning("[HeadsetRest] " + m_LastFailure + "; nothing was changed.");
                return;
            }

            if (!IsReady(out string why))
            {
                m_LastFailure = why;
                return;
            }

            Capture();
        }

        /// <summary>
        /// How still the headset has been, measured frame to frame on the camera.
        ///
        /// Run every frame rather than only while armed, so the stillness is already established
        /// when the key is pressed and staff do not stand there for an extra half second after
        /// the headset has plainly stopped moving.
        /// </summary>
        void TrackStillness()
        {
            if (!TryGetCamera(out Transform cam))
            {
                m_HavePrevious = false;
                m_StillFor = 0f;
                return;
            }

            if (!m_HavePrevious)
            {
                m_HavePrevious = true;
                m_PreviousPosition = cam.position;
                m_PreviousRotation = cam.rotation;
                m_StillFor = 0f;
                return;
            }

            float moved = Vector3.Distance(cam.position, m_PreviousPosition);
            float turned = Quaternion.Angle(cam.rotation, m_PreviousRotation);

            m_StillFor = moved <= m_StillPosition && turned <= m_StillAngle
                ? m_StillFor + Time.unscaledDeltaTime
                : 0f;

            m_PreviousPosition = cam.position;
            m_PreviousRotation = cam.rotation;
        }

        bool IsReady(out string why)
        {
            why = null;

            if (m_Marker == null)
            {
                why = "no CalibrationPoint in the scene";
                return false;
            }

            if (!TryGetCamera(out _))
            {
                why = "no XROrigin with a camera";
                return false;
            }

            // A headset that is not tracking reports its last known pose, and reports it very
            // steadily, so the stillness test below would happily pass on a frozen number. Over
            // Link the usual cause is the proximity sensor putting the headset to sleep the
            // moment it comes off a head -- which is exactly what this procedure does to it.
            XRInputDevice head = XRInputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (head.isValid)
            {
                // An invalid device means no headset at all, which is a desk test rather than a
                // fault, so only a headset that IS there and says it is not tracking refuses.
                if (head.TryGetFeatureValue(XRCommonUsages.isTracked, out bool tracked) && !tracked)
                {
                    why = "the headset is not tracking -- if it went to sleep when it came off, " +
                          "disable the proximity sensor in the headset's developer settings";
                    return false;
                }
            }

            if (m_RequireStillness && m_StillFor < m_StillSeconds)
            {
                why = $"waiting for the headset to be still ({m_StillFor:0.00}/{m_StillSeconds:0.00}s)";
                return false;
            }

            return true;
        }

        void Capture()
        {
            if (!m_Marker.CalibrateFromHeadsetRest(out string failure))
            {
                // Stays armed. The refusals this hits are all states staff can fix where they
                // are standing -- take the headset off, turn it round, let go of it -- and
                // disarming would mean walking back to the PC to press the key again.
                m_LastFailure = failure;
                return;
            }

            m_Armed = false;
            m_LastFailure = null;
            hasCalibrated = true;

            // A fresh calibration is the new zero: there is nothing left to undo back to, and
            // the next nudge should snapshot this pose rather than the one before it.
            ClearNudge();

            ReportCompletion();
        }

        /// <summary>
        /// Tells the show this headset is lined up, so a hold on the running order can wait for
        /// both of them rather than for an authored number of seconds.
        /// </summary>
        void ReportCompletion()
        {
            if (string.IsNullOrWhiteSpace(m_CompletionTaskId)) return;

            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress == null)
            {
                Utils.LogWarning($"[HeadsetRest] Calibrated, but there is no StoryProgressManager to " +
                                 $"report '{m_CompletionTaskId}' to. A hold on that task will not clear.");
                return;
            }

            progress.ReportTaskCompleteRpc(new FixedString64Bytes(m_CompletionTaskId));
            Utils.Log($"[HeadsetRest] Reported '{m_CompletionTaskId}'.");
        }

        bool TryGetCamera(out Transform cam)
        {
            cam = null;
            if (m_XROrigin == null || m_XROrigin.Camera == null)
                m_XROrigin = FindAnyObjectByType<XROrigin>();
            if (m_XROrigin == null || m_XROrigin.Camera == null) return false;
            cam = m_XROrigin.Camera.transform;
            return true;
        }

        #region Hand correction

        /// <summary>
        /// Shift and turn this headset's origin by hand, on top of whatever the calibration
        /// produced.
        ///
        /// THE RESCUE PATH, and it exists because the alternative in front of an audience is
        /// asking a performer to take the headset off mid-show and put it back in the cradle.
        /// Something has gone a few centimetres out -- tracking slipped in the dark, someone
        /// knocked the cradle between the two calibrations -- and staff can see exactly how far
        /// off it is, because they are watching the two mirrors side by side. Letting them say
        /// so in centimetres is both faster and less disruptive than any procedure.
        ///
        /// IN THE MARKER'S FRAME, NOT THE HEAD'S. "Forward" means the way the floor marker
        /// points, every time, however the player is facing -- otherwise the same button moves
        /// them in a different direction each press, which is unusable for someone correcting
        /// by eye.
        ///
        /// TURNING PIVOTS ON THE HEAD, though, not on the marker: a player who is in the right
        /// place but facing slightly wrong must be fixable without being swung around the room,
        /// and at three metres out a single degree about the marker is five centimetres of
        /// position error introduced to fix a rotation one.
        /// </summary>
        public void DebugNudge(DebugFireTarget target, Vector3 roomMetres, float degrees)
        {
            if (!DebugCanNudge(target)) return;
            if (!TryGetCamera(out Transform cam)) return;

            DropStaleSnapshot();
            RememberBeforeNudge();

            if (!Mathf.Approximately(degrees, 0f))
                m_XROrigin.transform.RotateAround(cam.position, Vector3.up, degrees);

            if (roomMetres != Vector3.zero)
                m_XROrigin.transform.position += RoomToWorld(roomMetres);

            m_NudgedMetres += roomMetres;
            m_NudgedDegrees += degrees;
            RecordWhereWeLeftIt();

            Utils.Log($"[HeadsetRest] Hand-corrected by {roomMetres.x * 100f:0.#}cm right, " +
                      $"{roomMetres.z * 100f:0.#}cm forward, {roomMetres.y * 100f:0.#}cm up, " +
                      $"{degrees:0.##}deg. Total {NudgeSummary()}.");
        }

        /// <summary>
        /// This machine only. An origin belongs to the headset in front of you, and the person
        /// who can tell whether a correction helped is the one watching that headset's own
        /// mirror -- so there is no path from here to the other machine's origin, by design
        /// rather than by omission.
        /// </summary>
        public bool DebugCanNudge(DebugFireTarget target) =>
            target == DebugFireTarget.Local && isActiveAndEnabled && TryGetCamera(out _);

        public bool DebugCanUndoNudge(DebugFireTarget target) =>
            target == DebugFireTarget.Local && hasLiveCorrection;

        /// <summary>
        /// Back to where calibration left it, in one press.
        ///
        /// A STORED POSE, NOT THE SUM OF THE STEPS REVERSED. Rotating about the head and
        /// translating in the room do not commute, so undoing by subtracting the totals leaves
        /// a residue that grows with every correction -- and the one moment this button is
        /// pressed is the moment someone has made it worse and wants out cleanly.
        /// </summary>
        public void DebugUndoNudge(DebugFireTarget target)
        {
            if (!DebugCanUndoNudge(target)) return;
            if (m_XROrigin == null && !TryGetCamera(out _)) return;

            m_XROrigin.transform.localPosition = m_PreNudgePosition;
            m_XROrigin.transform.localRotation = m_PreNudgeRotation;
            RecordWhereWeLeftIt();

            Utils.Log($"[HeadsetRest] Hand-correction of {NudgeSummary()} undone; back to the " +
                      "calibrated origin.");
            ClearNudge();
        }

        /// <summary>
        /// Local pose, not world: the rig rides the playspace, which the show moves, so the
        /// world pose this was taken at means nothing by the time anyone wants it back.
        /// </summary>
        void RememberBeforeNudge()
        {
            if (m_HaveNudgeSnapshot) return;

            m_PreNudgePosition = m_XROrigin.transform.localPosition;
            m_PreNudgeRotation = m_XROrigin.transform.localRotation;
            m_HaveNudgeSnapshot = true;
            RecordWhereWeLeftIt();
        }

        void RecordWhereWeLeftIt()
        {
            if (m_XROrigin == null) return;
            m_LastLeftPosition = m_XROrigin.transform.localPosition;
            m_LastLeftRotation = m_XROrigin.transform.localRotation;
        }

        /// <summary>
        /// Whether there is a hand correction that it still makes sense to undo.
        ///
        /// THE CHECK IS FOR SOMETHING ELSE HAVING MOVED THE ORIGIN, because the thing that moves
        /// it is not always this component: a calibration from the pinch gesture goes straight
        /// to <see cref="CalibrationPoint"/> and knows nothing about this. An undo that silently
        /// restored a pose from before the headset was last in the cradle would be the worst
        /// possible behaviour from the one button staff press when something has gone wrong.
        ///
        /// A pure query, because the panel asks for it on every refresh and
        /// <see cref="DebugState"/> is not allowed to change anything.
        /// </summary>
        bool hasLiveCorrection
        {
            get
            {
                if (!m_HaveNudgeSnapshot || m_XROrigin == null) return false;

                Transform origin = m_XROrigin.transform;
                return Vector3.Distance(origin.localPosition, m_LastLeftPosition) <= 0.0005f &&
                       Quaternion.Angle(origin.localRotation, m_LastLeftRotation) <= 0.01f;
            }
        }

        /// <summary>The mutating half of the above, run only when staff actually press something.</summary>
        void DropStaleSnapshot()
        {
            if (!m_HaveNudgeSnapshot || hasLiveCorrection) return;

            Utils.Log("[HeadsetRest] The origin was moved by something other than a hand " +
                      "correction, so there is no longer a correction to undo.");
            ClearNudge();
        }

        void ClearNudge()
        {
            m_HaveNudgeSnapshot = false;
            m_NudgedMetres = Vector3.zero;
            m_NudgedDegrees = 0f;
        }

        /// <summary>
        /// A step in the marker's frame as a world vector. Flattened, so that a marker left
        /// fractionally tilted in the scene cannot turn a sideways nudge into a vertical one.
        /// </summary>
        Vector3 RoomToWorld(Vector3 roomMetres)
        {
            Transform frame = m_Marker != null ? m_Marker.transform : null;
            if (frame == null) return roomMetres;

            Vector3 right = Flatten(frame.right);
            Vector3 forward = Flatten(frame.forward);
            if (right.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f) return roomMetres;

            return right.normalized * roomMetres.x
                   + Vector3.up * roomMetres.y
                   + forward.normalized * roomMetres.z;
        }

        static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

        string NudgeSummary() =>
            $"{m_NudgedMetres.x * 100f:+0.#;-0.#;0}cm R, {m_NudgedMetres.z * 100f:+0.#;-0.#;0}cm F, " +
            $"{m_NudgedMetres.y * 100f:+0.#;-0.#;0}cm U, {m_NudgedDegrees:+0.##;-0.##;0}deg";

        #endregion

        #region Debug panel

        public string debugId => "calibration.rest";

        public string debugLabel => "Calibrate origin (headset in the cradle)";

        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        /// <summary>
        /// FIRE, NOT ARM, unlike the pinch calibrator next door. Its button only arms because a
        /// fabricated pinch would be a point nobody's hand was ever at; this one reads the
        /// headset's own pose, which is just as real whoever asked for it to be read.
        ///
        /// THIS MACHINE ONLY, THOUGH. Each PC calibrates its own headset, and the panel offers
        /// no way to reach across -- see <see cref="DebugCanNudge"/> for why. It means someone
        /// walks between the two PCs, which is the right trade: both machines are calibrated by
        /// whoever can see the headset they are calibrating.
        /// </summary>
        public string debugFireLabel => "Calibrate";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = hasCalibrated;

            if (!isActiveAndEnabled)
            {
                state.detail = "component disabled -- the key does nothing";
                return state;
            }

            if (m_Marker == null)
            {
                state.detail = "no CalibrationPoint in the scene";
                return state;
            }

            if (m_Armed)
            {
                state.secondsToTimeout = Mathf.Max(0f, m_ArmedUntil - Time.unscaledTime);
                state.detail = m_LastFailure ?? "armed, waiting for a still headset";
                return state;
            }

            state.detail = m_LastFailure != null
                ? "last attempt refused: " + m_LastFailure
                : hasCalibrated ? "calibrated" : $"idle -- press {m_CalibrateKey}";

            // Said on the row rather than left implicit, because a world that has been moved by
            // hand is no longer where the cradle says it is, and the next person to wonder why
            // the two headsets disagree needs to know someone already corrected one of them.
            if (hasLiveCorrection) state.detail += $" · hand-corrected {NudgeSummary()}";

            return state;
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            // Arming a disabled calibrator sets a flag nothing reads, since Update is what acts
            // on it -- so the button would be dead rather than merely useless.
            return target == DebugFireTarget.Local && isActiveAndEnabled && !m_Armed;
        }

        public void DebugFire(DebugFireTarget target)
        {
            if (!DebugCanFire(target)) return;
            Arm();
        }

        /// <summary>A pending capture can be taken back. A calibration that has happened cannot.</summary>
        public bool DebugCanReset() => m_Armed;

        public void DebugReset() => Cancel();

        #endregion
    }
}
