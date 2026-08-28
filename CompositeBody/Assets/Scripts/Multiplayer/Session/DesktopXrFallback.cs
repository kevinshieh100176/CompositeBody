using System.Collections.Generic;
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
    /// </summary>
    public class DesktopXrFallback : MonoBehaviour
    {
        [SerializeField, Tooltip("Also trim exception stack traces, so any remaining per-frame error cannot flood the log.")]
        bool m_LimitStackTraces = true;

        public static bool xrRuntimeActive { get; private set; }

        void Start()
        {
            xrRuntimeActive = IsXrDisplayRunning();

            if (xrRuntimeActive)
            {
                Utils.Log("[DesktopXrFallback] XR runtime active; leaving locomotion enabled.");
                return;
            }

            if (m_LimitStackTraces)
                Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.None);

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
                    handsDisabled++;
                }
            }

            Utils.LogWarning(
                $"[DesktopXrFallback] No XR display running (headset off or Quest Link not started). " +
                $"Disabled {disabled} locomotion provider(s) and {handsDisabled} hand-tracking component(s) " +
                "to stop per-frame errors. Networking and the staff panel still work, so this machine " +
                "can be used for desktop LAN testing.");
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

        static bool IsXrDisplayRunning()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (display != null && display.running) return true;
            }
            return false;
        }
    }
}
