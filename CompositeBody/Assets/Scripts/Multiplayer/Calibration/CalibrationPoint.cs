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
        [SerializeField, Tooltip("Local keyboard shortcut for quick testing on a PC. Wire a proper XRI button for real use.")]
        Key m_TestCalibrateKey = Key.C;

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

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.15f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.5f);
        }
    }
}
