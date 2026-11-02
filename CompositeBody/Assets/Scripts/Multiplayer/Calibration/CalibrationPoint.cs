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
        [SerializeField, Tooltip("Local keyboard shortcut for quick testing on a PC: calibrates immediately from the worn headset, with no waiting for it to settle. Normally None -- in the venue the trigger is HeadsetRestCalibrator's key or the calibration panel, both of which wait for a still pose first.")]
        Key m_TestCalibrateKey = Key.None;

        [SerializeField, Tooltip("Also match the marker's height. Off by default: the headset's own floor estimate is normally better than a hand held at an unknown height, and a wrong height sinks the player through the floor or floats them above it.")]
        bool m_AlignHeight = false;

        [Header("Headset at rest")]
        [SerializeField, Tooltip("The pose the headset's camera reports once it is sitting in the cradle on the marker. Measure it against the real cradle. Empty falls back to this marker's own pose, which is only right if the cradle holds the headset upright at the marker's height.")]
        Transform m_HeadsetRest;

        [SerializeField, Tooltip("Take the floor height from the cradle as well. ON here, unlike the pinch path: the cradle's height is measured and the same for both headsets, whereas each headset's own floor estimate is its own guess, and two guesses 3cm apart put one player's world 3cm below the other's.")]
        bool m_AlignRestHeight = true;

        [SerializeField, Range(1f, 60f), Tooltip("How far the headset's measured tilt may differ from the cradle's before the calibration is refused. The commonest human error is pressing the key while the headset is still on someone's head, and that reads as tens of degrees.")]
        float m_MaxRestTilt = 20f;

        [SerializeField, Min(0.05f), Tooltip("How far, in metres, the headset may be from the cradle's height before the calibration is refused. The tilt check alone cannot catch a worn headset while the rest pose happens to be upright -- but a head is more than a metre above a cradle, whichever way it is facing.")]
        float m_MaxRestHeightError = 0.5f;

        [Header("Headset on a head")]
        [SerializeField, Min(0.2f), Tooltip("How far above the marker the headset must be before it is believed to be on a head. This catches the opposite mistake to the rest-pose height check: calibrating from a worn pose while the headset is in fact sitting in the cradle.")]
        float m_MinWornHeight = 0.8f;

        [SerializeField, Range(5f, 80f), Tooltip("How far the worn headset may be rolled from level. A head rolls a few degrees; past this it is a headset lying on something rather than on a person.")]
        float m_MaxWornRoll = 30f;

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
            // None is the normal setting and is not a key: indexing the keyboard with it throws.
            if (m_TestCalibrateKey != Key.None && Keyboard.current != null &&
                Keyboard.current[m_TestCalibrateKey].wasPressedThisFrame)
                CalibrateHere();
        }

        /// <summary>
        /// Shifts the XR Origin so the camera's current position/yaw matches this marker.
        /// Call this while the player is physically standing on the marker facing its forward.
        ///
        /// The one-line form, for a key press and for the inspector. The work, and every reason
        /// it might refuse, is in <see cref="CalibrateFromWornHeadset"/>.
        /// </summary>
        public void CalibrateHere()
        {
            if (!CalibrateFromWornHeadset(out string failure))
                Utils.LogWarning("[CalibrationPoint] Not calibrated: " + failure);
        }

        /// <summary>
        /// Lines the origin up from the headset while somebody is wearing it: they stand on the
        /// marker, face the way it points, and the pose they are in becomes the marker's pose.
        ///
        /// THE PERSON IS INSIDE THIS MEASUREMENT, which is the whole of the difference from
        /// <see cref="CalibrateFromHeadsetRest"/>. Where someone stands is good to a handspan and
        /// which way they face is good to perhaps five degrees, and five degrees is a quarter of
        /// a metre of disagreement between the two headsets at three metres out, growing with
        /// distance. What it buys for that is a procedure needing nothing measured and nothing
        /// built -- no cradle, no rest pose, no tape measure -- so it works the first time it is
        /// asked, which the cradle path does not until somebody has been under the marker with a
        /// ruler.
        ///
        /// NEVER THE HEIGHT, unlike the cradle path. The distance from floor to headset is this
        /// player's own eye height, not a measured constant: taking it would plant everyone's
        /// eyes at the marker's height, and for a marker taped to the floor that means their
        /// eyes on the floor. The floor is already handled, by the headset's own floor-level
        /// tracking origin, and is not this procedure's business.
        ///
        /// Returns false having moved nothing when the pose does not look like a headset on a
        /// head, which in practice means it is in the cradle and the other method was wanted.
        /// </summary>
        public bool CalibrateFromWornHeadset(out string failure)
        {
            failure = null;

            if (!TryGetCamera(out Transform cam))
            {
                failure = "no XROrigin with a camera in the scene";
                Utils.LogError("[CalibrationPoint] Cannot calibrate: " + failure);
                return false;
            }

            // THE MIRROR OF THE CRADLE PATH'S HEIGHT CHECK, catching the same confusion from the
            // other side: run this while the headset is sitting in the cradle and it reads the
            // cradle's spot as a standing person's, silently, because this path discards height
            // and so has nothing left to look wrong. A head is the better part of a metre above
            // a floor marker whether its owner is standing or sitting down.
            float height = cam.position.y - transform.position.y;
            if (height < m_MinWornHeight)
            {
                failure = $"the headset is only {height:0.00}m above the marker " +
                          $"(expected at least {m_MinWornHeight:0.00}m) -- it is not on a head; " +
                          "if it is in the cradle, calibrate from the rest pose instead";
                Utils.LogWarning("[CalibrationPoint] Refused: " + failure);
                return false;
            }

            // A head rolls a few degrees and no further. Past the limit it is a headset lying on
            // something at about head height, and its facing is then whichever way it was set
            // down rather than anything a person chose.
            float roll = Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(cam.right, Vector3.up), -1f, 1f)) *
                                   Mathf.Rad2Deg);
            if (roll > m_MaxWornRoll)
            {
                failure = $"the headset is rolled {roll:0.#}deg off level (limit {m_MaxWornRoll:0.#}deg) -- " +
                          "it is lying on its side rather than sitting on a head";
                Utils.LogWarning("[CalibrationPoint] Refused: " + failure);
                return false;
            }

            // Yaw off the flattened forward vectors rather than the difference of two
            // eulerAngles.y. For any pose with a usable horizontal forward the two agree, so
            // this is not a bug being fixed; what it buys is the degenerate case. A head looking
            // almost straight down has no azimuth to read, and an euler decomposition answers
            // anyway, folding roll into yaw and handing back a plausible number. The flattened
            // form hands back a vector too short to use, and that is something that can be
            // refused instead of believed.
            Vector3 haveDir = Flatten(cam.forward);
            Vector3 wantDir = Flatten(transform.forward);

            if (haveDir.sqrMagnitude < k_MinHorizontal * k_MinHorizontal)
            {
                failure = "the headset is pointed too near straight up or down for its facing to " +
                          "be read -- look level, along the way the marker points";
                Utils.LogWarning("[CalibrationPoint] Refused: " + failure);
                return false;
            }

            if (wantDir.sqrMagnitude < k_MinHorizontal * k_MinHorizontal)
            {
                failure = "the marker itself points vertically, so it carries no facing to " +
                          "calibrate against; check its rotation in the scene";
                Utils.LogWarning("[CalibrationPoint] Refused: " + failure);
                return false;
            }

            float yawDelta = Vector3.SignedAngle(haveDir, wantDir, Vector3.up);

            // About the camera, so the turn costs no position. The translation below is then the
            // whole of the position correction, and the two halves can be read apart.
            m_XROrigin.transform.RotateAround(cam.position, Vector3.up, yawDelta);

            Vector3 positionDelta = transform.position - cam.position;
            positionDelta.y = 0f;
            m_XROrigin.transform.position += positionDelta;

            Utils.Log($"[CalibrationPoint] Calibrated from the worn headset; yaw {yawDelta:0.0}deg, " +
                      $"shift {positionDelta.magnitude:0.000}m, head {height:0.00}m up " +
                      "(height left alone).");
            return true;
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

            // HEIGHT FIRST, because it is the check that does not depend on the cradle having a
            // distinctive orientation. The tilt test below is the better tell when the cradle
            // holds the headset at an angle -- but while the rest pose is upright, which it is
            // whenever nobody has measured the real cradle yet, a headset worn by someone
            // standing level and facing roughly the right way passes it. A head is more than a
            // metre above a cradle on the floor no matter which way it is pointing.
            float heightError = Mathf.Abs(cam.position.y - rest.position.y);
            if (heightError > m_MaxRestHeightError)
            {
                failure = $"the headset is {heightError:0.00}m from the cradle's height " +
                          $"(limit {m_MaxRestHeightError:0.00}m) -- it is probably still being worn, " +
                          "or the rest pose has not been measured against the real cradle";
                Utils.LogWarning("[CalibrationPoint] Refused: " + failure);
                return false;
            }

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
