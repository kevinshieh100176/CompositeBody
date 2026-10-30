using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using XRMultiplayer;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// A physical reference point in the room (e.g. a taped floor marker). The player stands on
    /// it facing this transform's forward direction and calibrates; the XR Origin is shifted so
    /// the headset's tracked position/yaw lines up with this marker. Since every headset
    /// calibrates to the same physical point, their networked positions end up agreeing with the
    /// real room even though each headset's own tracking origin is independent.
    /// </summary>
    public class CalibrationPoint : MonoBehaviour
    {
        [SerializeField, Tooltip("Local keyboard shortcut for quick testing on a PC. In the venue the trigger is a held pinch; see HandPinchCalibrator.")]
        Key m_TestCalibrateKey = Key.C;

        [SerializeField, Tooltip("Also match the marker's height. Off by default: the headset's own floor estimate is normally better than a hand held at an unknown height, and a wrong height sinks the player through the floor or floats them above it.")]
        bool m_AlignHeight = false;

        [Header("Headset at rest")]
        [SerializeField, Tooltip("The pose the headset's camera reports once it is sitting in the cradle on the marker. Measure it against the real cradle. Empty falls back to this marker's own pose, which is only right if the cradle holds the headset upright at the marker's height.")]
        Transform m_HeadsetRest;

        [SerializeField, Tooltip("Take the floor height from the cradle as well. ON here, unlike the pinch path: the cradle's height is measured and the same for both headsets, whereas each headset's own floor estimate is its own guess, and two guesses 3cm apart put one player's world 3cm below the other's.")]
        bool m_AlignRestHeight = true;

        [SerializeField, Range(1f, 60f), Tooltip("How far the headset's measured tilt may differ from the cradle's before the calibration is refused. The commonest human error is pressing the key while the headset is still on someone's head, and that reads as tens of degrees.")]
        float m_MaxRestTilt = 20f;

        // A horizontal projection shorter than this carries no usable facing; 0.34 is about 20
        // degrees off vertical.
        const float k_MinHorizontal = 0.34f;

        XROrigin m_XROrigin;

        void Start()
        {
            m_XROrigin = FindAnyObjectByType<XROrigin>();
            if (m_XROrigin == null)
                Utils.LogWarning("[CalibrationPoint] No XROrigin found in scene yet.");
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current[m_TestCalibrateKey].wasPressedThisFrame)
                CalibrateHere();
        }

        /// <summary>
        /// Shifts the XR Origin so the camera's current position/yaw matches this marker.
        /// Call this while the player is physically standing on the marker facing its forward.
        /// </summary>
        public void CalibrateHere()
        {
            if (m_XROrigin == null)
            {
                m_XROrigin = FindAnyObjectByType<XROrigin>();
                if (m_XROrigin == null)
                {
                    Utils.LogError("[CalibrationPoint] Cannot calibrate: no XROrigin in scene.");
                    return;
                }
            }

            Transform cam = m_XROrigin.Camera.transform;

            // Rotate around the camera's current position so only yaw changes -- head tilt
            // (pitch/roll) must never be baked into the origin.
            float yawDelta = transform.eulerAngles.y - cam.eulerAngles.y;
            m_XROrigin.transform.RotateAround(cam.position, Vector3.up, yawDelta);

            // Translate so camera XZ lands on the marker; leave height alone since floor
            // height should already match real-world tracking.
            Vector3 positionDelta = transform.position - cam.position;
            positionDelta.y = 0f;
            m_XROrigin.transform.position += positionDelta;

            Utils.Log("[CalibrationPoint] Calibrated XR Origin to marker.");
        }

        /// <summary>
        /// Lines the origin up so that <paramref name="worldReferencePoint"/> -- in practice the
        /// player's pinched fingertips, resting on the physical marker -- lands on this marker,
        /// while the facing still comes from the head.
        ///
        /// Position from the hand and yaw from the head, because they fail differently: a
        /// fingertip placed on a taped cross is accurate to a centimetre or two, whereas asking
        /// someone to stand on a spot is not; but a single point carries no facing at all, so
        /// the head's yaw is the only rotation on offer. The residual error is therefore all in
        /// yaw, which two markers would fix if it ever proves too coarse.
        /// </summary>
        public void CalibrateAtPoint(Vector3 worldReferencePoint)
        {
            if (m_XROrigin == null)
            {
                m_XROrigin = FindAnyObjectByType<XROrigin>();
                if (m_XROrigin == null)
                {
                    Utils.LogError("[CalibrationPoint] Cannot calibrate: no XROrigin in scene.");
                    return;
                }
            }

            Transform cam = m_XROrigin.Camera.transform;

            // Rotated about the reference point rather than the camera, so the point the player
            // is physically touching does not move out from under their finger; the translation
            // below is then purely the offset to the marker. Yaw only -- head pitch and roll
            // must never end up baked into the origin.
            float yawDelta = transform.eulerAngles.y - cam.eulerAngles.y;
            m_XROrigin.transform.RotateAround(worldReferencePoint, Vector3.up, yawDelta);

            Vector3 positionDelta = transform.position - worldReferencePoint;
            if (!m_AlignHeight) positionDelta.y = 0f;
            m_XROrigin.transform.position += positionDelta;

            Utils.Log($"[CalibrationPoint] Calibrated on reference point; yaw {yawDelta:0.0}deg, " +
                      $"shift {positionDelta.magnitude:0.00}m" + (m_AlignHeight ? " (height included)." : "."));
        }

        /// <summary>
        /// Where the headset itself comes to rest on the physical marker: the pose its camera
        /// reports while it sits in the cradle. Falls back to the marker, which only reads as a
        /// rest pose if the cradle holds the headset upright at the marker's own height.
        /// </summary>
        public Transform headsetRest => m_HeadsetRest != null ? m_HeadsetRest : transform;

        /// <summary>
        /// Lines the origin up from the headset's own pose while it rests on the marker. This is
        /// the one calibration with no human inside the measurement: the cradle holds the
        /// headset, the headset reports where it is, and the only thing a person does is press a
        /// key on their own PC.
        ///
        /// YAW ONLY, THOUGH THE CRADLE KNOWS ALL THREE ANGLES. A rest pose is a measurement of
        /// something someone built and taped down, so it is a degree or two out; taking its
        /// pitch and roll would tilt that player's whole world by that much for the rest of the
        /// show. A crooked horizon is the most nauseating error available and the hardest to
        /// notice from inside a headset, whereas a yaw error is both the one thing the headset
        /// cannot work out for itself and the one a mis-measured cradle cannot poison, because
        /// a tilt in the cradle does not project onto yaw.
        ///
        /// Returns false having moved nothing when the measured pose does not look like a
        /// headset in a cradle, which in practice means someone pressed the key while wearing it.
        /// </summary>
        public bool CalibrateFromHeadsetRest(out string failure)
        {
            failure = null;

            if (!TryGetCamera(out Transform cam))
            {
                failure = "no XROrigin with a camera in the scene";
                Utils.LogError("[CalibrationPoint] Cannot calibrate: " + failure);
                return false;
            }

            Transform rest = headsetRest;
            Quaternion want = rest.rotation;
            Quaternion have = cam.rotation;

            // The axis the facing is read off: whichever of the headset's own three lies
            // flattest in the cradle. A near-vertical axis carries almost no yaw -- a headset
            // lying face-down has no usable "forward" -- and reading euler angles off one is
            // how a yaw of 180 degrees comes back as a yaw of 0.
            Vector3 axis = FlattestAxis(want);
            Vector3 wantDir = Flatten(want * axis);
            Vector3 haveDir = Flatten(have * axis);

            if (haveDir.sqrMagnitude < k_MinHorizontal * k_MinHorizontal)
            {
                failure = "the headset is tilted too close to vertical for its facing to be read; " +
                          "check it is sitting in the cradle the way the rest pose says";
                Utils.LogWarning("[CalibrationPoint] Refused: " + failure);
                return false;
            }

            float yawDelta = Vector3.SignedAngle(haveDir, wantDir, Vector3.up);

            // What is left once that yaw is taken out is tilt, and tilt is the tell: a headset
            // in a cradle matches the cradle, a headset on a head does not.
            float tilt = Quaternion.Angle(Quaternion.AngleAxis(yawDelta, Vector3.up) * have, want);
            if (tilt > m_MaxRestTilt)
            {
                failure = $"the headset is {tilt:0.#}deg off the cradle's pose (limit {m_MaxRestTilt:0.#}deg) -- " +
                          "it is probably still being worn, or it is in the cradle the wrong way round";
                Utils.LogWarning("[CalibrationPoint] Refused: " + failure);
                return false;
            }

            m_XROrigin.transform.RotateAround(cam.position, Vector3.up, yawDelta);

            Vector3 positionDelta = rest.position - cam.position;
            if (!m_AlignRestHeight) positionDelta.y = 0f;
            m_XROrigin.transform.position += positionDelta;

            Utils.Log($"[CalibrationPoint] Calibrated from the headset at rest; yaw {yawDelta:0.0}deg, " +
                      $"shift {positionDelta.magnitude:0.000}m, tilt mismatch {tilt:0.#}deg" +
                      (m_AlignRestHeight ? " (height included)." : "."));
            return true;
        }

        bool TryGetCamera(out Transform cam)
        {
            cam = null;
            if (m_XROrigin == null) m_XROrigin = FindAnyObjectByType<XROrigin>();
            if (m_XROrigin == null || m_XROrigin.Camera == null) return false;
            cam = m_XROrigin.Camera.transform;
            return true;
        }

        static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Whichever of the three local axes lies flattest under this rotation.</summary>
        static Vector3 FlattestAxis(Quaternion rotation)
        {
            Vector3 best = Vector3.forward;
            float bestTilt = Mathf.Abs((rotation * Vector3.forward).y);

            float upTilt = Mathf.Abs((rotation * Vector3.up).y);
            if (upTilt < bestTilt) { best = Vector3.up; bestTilt = upTilt; }

            if (Mathf.Abs((rotation * Vector3.right).y) < bestTilt) best = Vector3.right;

            return best;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.15f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.5f);

            // The rest pose drawn as a headset-sized box, because the thing that goes wrong is
            // someone eyeballing where the cradle holds it rather than measuring.
            if (m_HeadsetRest == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.matrix = Matrix4x4.TRS(m_HeadsetRest.position, m_HeadsetRest.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.19f, 0.09f, 0.08f));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.25f);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
