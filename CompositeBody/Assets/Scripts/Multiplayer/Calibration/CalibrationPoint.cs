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

        XROrigin m_XROrigin;

        void Start()
        {
            m_XROrigin = FindFirstObjectByType<XROrigin>();
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
                m_XROrigin = FindFirstObjectByType<XROrigin>();
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
                m_XROrigin = FindFirstObjectByType<XROrigin>();
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

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.15f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.5f);
        }
    }
}
