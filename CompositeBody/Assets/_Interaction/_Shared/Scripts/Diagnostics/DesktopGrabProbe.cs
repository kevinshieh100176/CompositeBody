using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CompositeBody.Interaction.Diagnostics
{
    /// <summary>
    /// DEVELOPMENT ONLY. Press G to grab the <see cref="NetGrabbable"/> nearest the camera's line
    /// of sight; G again lets go; the held object rides in front of the camera.
    ///
    /// Exists for Multiplayer Play Mode, whose virtual players have no XR runtime: without some
    /// way to grab from a desktop window, "two players reach for the same fragment" -- the one
    /// ownership case that matters -- cannot be tried before two headsets are in a room. The grab
    /// goes through the validated XRI path, so the select filter and the server's grant or refusal
    /// are exactly what a hand would get.
    /// </summary>
    public class DesktopGrabProbe : MonoBehaviour
    {
        [SerializeField] Key m_Key = Key.G;
        [SerializeField, Min(0.1f)] float m_MaxDistance = 6f;
        [SerializeField, Range(1f, 45f), Tooltip("Half-angle of the cone around the view direction.")]
        float m_ConeDegrees = 15f;
        [SerializeField, Min(0.1f)] float m_HoldDistance = 0.6f;

        InteractionTestHand m_Hand;

        void Update()
        {
            Camera cam = Camera.main;
            if (cam == null || Keyboard.current == null) return;

            if (m_Hand == null)
            {
                m_Hand = InteractionTestHand.Create("DesktopGrabProbe Hand", cam.transform.position);
                m_Hand.transform.SetParent(transform, true);
            }

            if (m_Hand.hasSelection)
                m_Hand.MoveTo(cam.transform.position + cam.transform.forward * m_HoldDistance);

            if (!Keyboard.current[m_Key].wasPressedThisFrame) return;

            if (m_Hand.hasSelection)
            {
                m_Hand.Release();
                return;
            }

            XRGrabInteractable target = FindTarget(cam.transform);
            if (target == null)
            {
                Debug.Log("[DesktopGrabProbe] nothing grabbable in view.");
                return;
            }

            bool ok = m_Hand.TryGrab(target);
            Debug.Log(ok
                ? $"[DesktopGrabProbe] grabbed '{target.name}'."
                : $"[DesktopGrabProbe] select refused for '{target.name}'.");
        }

        XRGrabInteractable FindTarget(Transform view)
        {
            float cos = Mathf.Cos(m_ConeDegrees * Mathf.Deg2Rad);
            XRGrabInteractable best = null;
            float bestScore = float.NegativeInfinity;

            foreach (NetGrabbable g in FindObjectsByType<NetGrabbable>(FindObjectsInactive.Exclude))
            {
                if (g.interactable == null || !g.interactable.isActiveAndEnabled) continue;
                Vector3 to = g.transform.position - view.position;
                float d = to.magnitude;
                if (d > m_MaxDistance || d < 1e-3f) continue;
                float dot = Vector3.Dot(to / d, view.forward);
                if (dot < cos) continue;

                float score = dot - d * 0.05f;
                if (score > bestScore) { bestScore = score; best = g.interactable; }
            }
            return best;
        }
    }
}
