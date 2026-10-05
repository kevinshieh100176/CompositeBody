using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using XRMultiplayer;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Keeps the app usable when it starts without an active XR runtime (no headset, or Quest
    /// Link not running).
    ///
    /// In that state XRI's locomotion providers throw a NullReferenceException every single
    /// frame. That is not merely noisy: it wrote a 69 MB, one-million-line player log in a
    /// single session, and the resulting per-frame exception handling and disk I/O starve the
    /// main loop badly enough to stall Netcode's connection and scene-synchronization
    /// handshakes. Disabling locomotion when there is no XR device keeps the frame loop clean,
    /// which is what makes desktop LAN testing possible without headsets on both machines.
    ///
    /// Two separate questions are being asked here, and they have different answers:
    /// - <b>Is there a runtime at all?</b> Decides locomotion and hand tracking. Answered once,
    ///   after giving the runtime time to start, and revisited if one turns up later.
    /// - <b>Is a head being tracked right now?</b> Decides the camera, and only that. Link can be
    ///   running with the headset asleep on a desk, which is a runtime by every measure and still
    ///   no head to put a camera on. Watched continuously, because a headset picked up mid-session
    ///   has to take the camera straight back.
    ///
    /// Conflating the two is what made this worth splitting: the camera was being parked on the
    /// strength of a runtime check made before OpenXR had finished starting, which cost a working
    /// session its head tracking.
    /// </summary>
    public class DesktopXrFallback : MonoBehaviour
    {
        [SerializeField, Tooltip("Also trim exception stack traces, so any remaining per-frame error cannot flood the log.")]
        bool m_LimitStackTraces = true;

        [SerializeField, Tooltip("Eye height to stand the camera at when no head is being tracked. Zero leaves the camera alone entirely.")]
        float m_DesktopEyeHeight = 1.6f;

        [SerializeField, Tooltip("How long to wait before concluding that there is no runtime, and no head, in seconds. Quest Link has been measured taking over five seconds to come up on this project, and standing down early only to hand everything back a moment later is churn worth avoiding. It costs a genuinely headset-less machine this many seconds of startup log noise.")]
        float m_XrStartupGrace = 10f;

        /// <summary>How often the headset is looked for once the scene is running, in seconds.</summary>
        const float k_PollInterval = 0.5f;

        /// <summary>
        /// Whether there is an XR runtime to leave alone.
        ///
        /// Asked fresh each time rather than remembered from startup, because at startup the
        /// answer is no and a second later it is yes. A display subsystem alone is not enough to
        /// ask -- it is the last part of a runtime to come up -- so a tracked head counts too.
        /// </summary>
        public static bool xrRuntimeActive => IsXrDisplayRunning() || IsHeadTracked();

        // What was switched off to get to desktop, so it can be switched back on.
        readonly List<Behaviour> m_SwitchedOff = new();
        readonly List<Behaviour> m_DisabledDrivers = new();
        bool m_StoodDown;
        float m_NextPoll;

        // What the camera was before it was stood up, and when the head was first lost.
        XROrigin m_Origin;
        bool m_CameraStoodUp;
        float m_HeadLostAt = -1f;
        XROrigin.TrackingOriginMode m_PreviousOriginMode;
        float m_PreviousCameraYOffset;
        Vector3 m_PreviousCameraPosition, m_PreviousFloorOffset;
        Quaternion m_PreviousCameraRotation;

        /// <summary>
        /// Waits for XR to come up before concluding that it is not going to, and only then stands
        /// locomotion and hand tracking down.
        ///
        /// The wait is the whole point. Asked on the first frame, OpenXR and Link have not finished
        /// starting and every check says there is no headset, so a working session was having its
        /// locomotion and hand tracking switched off underneath it.
        /// </summary>
        IEnumerator Start()
        {
            // Trimmed up front and put back if a runtime does appear: the seconds spent waiting
            // are seconds a headset-less run spends throwing one exception per frame per
            // locomotion provider, and those are what wrote the 69 MB log.
            if (m_LimitStackTraces)
                Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.None);

            float started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < m_XrStartupGrace)
            {
                if (xrRuntimeActive)
                {
                    if (m_LimitStackTraces)
                        Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);

                    Utils.Log($"[DesktopXrFallback] XR runtime up after {Time.realtimeSinceStartup - started:0.0}s; " +
                              "leaving locomotion and hand tracking alone.");
                    yield break;
                }

                yield return null;
            }

            StandDownToDesktop();
        }

        void Update()
        {
            // Polled rather than checked every frame: both questions walk the subsystem and device
            // lists, and neither changes between one frame and the next.
            if (Time.unscaledTime < m_NextPoll) return;
            m_NextPoll = Time.unscaledTime + k_PollInterval;

            FollowTheHeadset();

            if (m_StoodDown && xrRuntimeActive) HandTheRigBackToXr();
        }

        /// <summary>
        /// Gives the camera to whichever is actually there: a tracked head, or a standing height
        /// made up for it.
        ///
        /// Continuous rather than decided at startup. Pressing play and then picking the headset up
        /// is an ordinary thing to do, and a camera parked by a decision made five seconds earlier
        /// would simply stop following the player's head.
        /// </summary>
        void FollowTheHeadset()
        {
            if (m_DesktopEyeHeight <= 0f) return;

            if (IsHeadTracked())
            {
                m_HeadLostAt = -1f;
                if (m_CameraStoodUp) RestoreCamera();
                return;
            }

            if (m_CameraStoodUp) return;

            // The same grace the runtime gets, for the same reason: a head that is not tracked yet
            // is not the same as a head that is not coming.
            if (m_HeadLostAt < 0f)
            {
                m_HeadLostAt = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - m_HeadLostAt < m_XrStartupGrace) return;

            StandCameraUp();
        }

        void StandDownToDesktop()
        {
            // "Array index (0) is out of bounds" still fires thousands of times per second from
            // an unidentified source, and carries no stack trace to trace it by. Capture the
            // stack for the FIRST one only, then silence them -- enough to find the culprit
            // without re-flooding the log.
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
            Application.logMessageReceived += CaptureFirstArrayIndexError;

            int disabled = 0;
            foreach (var provider in FindObjectsByType<LocomotionProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!provider.enabled) continue;
                provider.enabled = false;
                m_SwitchedOff.Add(provider);
                disabled++;
            }

            // XR Hands drivers poll a subsystem that does not exist without a runtime and log
            // "Array index (0) is out of bounds" every frame. Matched by namespace rather than
            // concrete type so this assembly needs no reference to the XR Hands package.
            int handsDisabled = 0;
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null || !behaviour.enabled) continue;

                string ns = behaviour.GetType().Namespace;
                if (ns != null && ns.StartsWith("UnityEngine.XR.Hands"))
                {
                    behaviour.enabled = false;
                    m_SwitchedOff.Add(behaviour);
                    handsDisabled++;
                }
            }

            m_StoodDown = true;

            Utils.LogWarning(
                $"[DesktopXrFallback] No XR runtime after {m_XrStartupGrace:0.#}s (headset off or Quest Link not " +
                $"started). Disabled {disabled} locomotion provider(s) and {handsDisabled} hand-tracking " +
                "component(s) to stop per-frame errors. Networking and the staff panel still work, so this " +
                "machine can be used for desktop LAN testing. Will hand them back if a headset turns up.");
        }

        void HandTheRigBackToXr()
        {
            m_StoodDown = false;

            int restored = 0;
            foreach (var behaviour in m_SwitchedOff)
            {
                // Destroyed in the meantime, or switched off again by something that meant it.
                if (behaviour == null) continue;
                behaviour.enabled = true;
                restored++;
            }
            m_SwitchedOff.Clear();

            Application.logMessageReceived -= CaptureFirstArrayIndexError;
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
            if (m_LimitStackTraces)
                Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);

            Utils.Log($"[DesktopXrFallback] An XR runtime turned up; handed back {restored} component(s).");
        }

        /// <summary>
        /// Puts the camera at a standing eye height when there is no head to measure one from.
        ///
        /// With nothing tracking it the camera sits wherever the rig left it, which is on the
        /// floor: the player is a camera lying on the ground. That was survivable while the avatar
        /// was a floating head. Now the body is solved from the headset's height, so a camera on
        /// the floor hangs the whole figure below it, through the floor, and the one thing a
        /// desktop run is good for -- looking at the avatar -- stops working.
        /// </summary>
        void StandCameraUp()
        {
            m_Origin = FindFirstObjectByType<XROrigin>();
            if (m_Origin == null || m_Origin.Camera == null) return;

            // The pose driver goes first. A headset that is not being worn can still be feeding a
            // pose -- lying on a desk it reports a point below the floor -- and that is applied on
            // top of whatever height is set here, dragging the camera straight back down. Matched
            // by type name rather than by type so this assembly needs no reference to the input
            // package.
            foreach (var behaviour in m_Origin.Camera.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null || !behaviour.enabled) continue;
                if (behaviour.GetType().Name != "TrackedPoseDriver") continue;

                behaviour.enabled = false;
                m_DisabledDrivers.Add(behaviour);
            }

            // Everything the rig is about to be moved from, so that a headset turning up later
            // gets it back the way it was rather than the way this script would have guessed it.
            m_PreviousOriginMode = m_Origin.RequestedTrackingOriginMode;
            m_PreviousCameraYOffset = m_Origin.CameraYOffset;
            m_PreviousCameraPosition = m_Origin.Camera.transform.localPosition;
            m_PreviousCameraRotation = m_Origin.Camera.transform.localRotation;
            if (m_Origin.CameraFloorOffsetObject != null)
                m_PreviousFloorOffset = m_Origin.CameraFloorOffsetObject.transform.localPosition;

            m_Origin.Camera.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Device mode is what makes the origin apply a camera height at all; in floor mode it
            // expects the runtime to supply one, and there is no head here to supply it.
            m_Origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            m_Origin.CameraYOffset = m_DesktopEyeHeight;

            if (m_Origin.CameraFloorOffsetObject != null)
            {
                Vector3 offset = m_Origin.CameraFloorOffsetObject.transform.localPosition;
                m_Origin.CameraFloorOffsetObject.transform.localPosition =
                    new Vector3(offset.x, m_DesktopEyeHeight, offset.z);
            }

            m_CameraStoodUp = true;

            Utils.LogWarning($"[DesktopXrFallback] No head tracked after {m_XrStartupGrace:0.#}s; stood the camera " +
                             $"at {m_DesktopEyeHeight:0.00}m so the avatar has a height to be solved against. " +
                             "Tracking takes it back as soon as a headset is on.");
        }

        /// <summary>Puts the rig back as it was found, so tracking owns the camera again.</summary>
        void RestoreCamera()
        {
            m_CameraStoodUp = false;

            foreach (var driver in m_DisabledDrivers)
            {
                if (driver == null) continue;
                driver.enabled = true;
            }
            m_DisabledDrivers.Clear();

            if (m_Origin == null) return;

            m_Origin.RequestedTrackingOriginMode = m_PreviousOriginMode;
            m_Origin.CameraYOffset = m_PreviousCameraYOffset;

            if (m_Origin.CameraFloorOffsetObject != null)
                m_Origin.CameraFloorOffsetObject.transform.localPosition = m_PreviousFloorOffset;

            if (m_Origin.Camera != null)
                m_Origin.Camera.transform.SetLocalPositionAndRotation(m_PreviousCameraPosition, m_PreviousCameraRotation);

            Utils.Log("[DesktopXrFallback] A head is being tracked; gave the camera back to it.");
        }

        bool m_ArrayIndexReported;

        void CaptureFirstArrayIndexError(string condition, string stackTrace, LogType type)
        {
            if (m_ArrayIndexReported) return;
            if (condition == null || !condition.Contains("Array index")) return;

            m_ArrayIndexReported = true;

            // Silence the flood now that we have one sample, and stop listening.
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
            Application.logMessageReceived -= CaptureFirstArrayIndexError;

            Debug.Log($"[DesktopXrFallback] FIRST '{condition}' originated from:\n" +
                      (string.IsNullOrWhiteSpace(stackTrace) ? "(no managed stack - native source)" : stackTrace));
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= CaptureFirstArrayIndexError;
        }

        // Reused rather than allocated per call: this is asked every frame while waiting for a
        // runtime, and twice a second after that.
        static readonly List<XRDisplaySubsystem> s_Displays = new();

        static bool IsXrDisplayRunning()
        {
            SubsystemManager.GetSubsystems(s_Displays);
            foreach (var display in s_Displays)
            {
                if (display != null && display.running) return true;
            }
            return false;
        }

        /// <summary>
        /// A headset reporting a tracked head pose. Asked separately from the display subsystem,
        /// which is both the last part of a runtime to start -- there is a window where the head is
        /// tracked and driving the camera while the display still reports nothing -- and perfectly
        /// happy to keep running while the headset sits asleep on a desk.
        /// </summary>
        static bool IsHeadTracked()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.isValid &&
                   head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) &&
                   tracked;
        }
    }
}
