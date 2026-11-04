using System.Text;
using Unity.Netcode;
using UnityEngine;
using CompositeBody.Diagnostics;
using CompositeBody.Interaction.Diagnostics;

namespace CompositeBody.Interaction.Home
{
    /// <summary>
    /// DEVELOPMENT ONLY. Home's section of <see cref="DesktopTestHud"/> in the manual-test scene:
    /// where the beat has got to, each fragment's state and holder, and the buttons a tester wants
    /// without waiting minutes -- begin, place the next fragment, expire the timeout, reset -- plus
    /// a teleport that stands the player at the assembly zone. Everything goes through the same
    /// paths the debug panel and the server use, so it tests the module rather than bypassing it.
    /// </summary>
    public class HomeManualTestActions : MonoBehaviour, IDesktopTestActions
    {
        [SerializeField, Tooltip("Found in the scene if left empty.")]
        HomeAssembly m_Assembly;

        [SerializeField, Min(0.3f), Tooltip("How far in front of the assembly zone the teleport stands you.")]
        float m_TeleportDistance = 1.2f;

        public string desktopTestTitle => "Home";

        HomeAssembly assembly
        {
            get
            {
                if (m_Assembly == null) m_Assembly = FindAnyObjectByType<HomeAssembly>();
                return m_Assembly;
            }
        }

        static bool isAuthority
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening || nm.IsServer;
            }
        }

        public void OnDesktopTestGUI()
        {
            HomeAssembly a = assembly;
            if (a == null) { GUILayout.Label("no HomeAssembly in the scene"); return; }

            DebugInteractionState s = a.DebugState();
            GUILayout.Label(s.detail);
            GUILayout.Label(FragmentLine(a));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(a.armed ? "Place next" : "Begin") && a.DebugCanFire(DebugFireTarget.Local))
                a.DebugFire(DebugFireTarget.Local);
            GUI.enabled = a.armed;
            if (GUILayout.Button("Expire timeout")) Expire(a);
            GUI.enabled = a.DebugCanReset();
            if (GUILayout.Button("Reset")) a.DebugReset();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Teleport me to the assembly zone")) TeleportToZone(a);
        }

        static string FragmentLine(HomeAssembly a)
        {
            var sb = new StringBuilder();
            foreach (var p in a.pairs)
            {
                if (p?.fragment == null) continue;
                var f = p.fragment;
                sb.Append(f.label).Append(':').Append(f.state);
                if (f.isHeld) sb.Append("(#").Append(f.holder).Append(')');
                sb.Append("  ");
            }
            return sb.ToString();
        }

        static void Expire(HomeAssembly a)
        {
            if (isAuthority) { a.AuthorityExpire(); return; }
            // A client asks the server through any spawned fragment.
            foreach (var p in a.pairs)
            {
                if (p?.fragment == null || !p.fragment.IsSpawned) continue;
                p.fragment.RequestExpireRpc();
                return;
            }
        }

        void TeleportToZone(HomeAssembly a)
        {
            if (a.zoneB == null) return;
            var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) { Debug.LogWarning("[HomeManualTest] no XR Origin to move."); return; }

            Vector3 zone = a.zoneB.center;
            Vector3 cam = origin.Camera != null ? origin.Camera.transform.position : origin.transform.position;
            Vector3 flat = new Vector3(cam.x - zone.x, 0f, cam.z - zone.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.back;
            Vector3 stand = zone + flat.normalized * m_TeleportDistance;
            stand.y = origin.transform.position.y;

            // Move the origin so the camera ends up at the stand point, facing the zone.
            origin.MoveCameraToWorldLocation(new Vector3(stand.x, cam.y, stand.z));
            origin.MatchOriginUpCameraForward(Vector3.up, (zone - stand).normalized);
            Debug.Log($"[HomeManualTest] teleported to {stand:F2}, facing the assembly zone.");
        }
    }
}
