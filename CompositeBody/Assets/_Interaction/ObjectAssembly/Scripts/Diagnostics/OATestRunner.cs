using System.Collections;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using CompositeBody.Diagnostics;
using CompositeBody.Interaction.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Interaction.ObjectAssembly.Diagnostics
{
    /// <summary>
    /// Play-mode check of the ObjectAssembly beat in ObjectAssembly_Demo.unity, as a host with
    /// one <see cref="InteractionTestHand"/>:
    ///
    ///   dormant + hidden before Start → Phase 0 (lights come up) → halves appear at their corners
    ///   → the hand claims half A (Phase 1 starts on this machine) → released, A follows the head
    ///   → the same hand is refused half B → the Phase 1 line ends and is reported by the clock →
    ///   P2 forced (claim + line) → Linking, trail visible → trail intensity rises step by step as
    ///   the halves close → join: grab cancelled, halves in the assembled layout, Phase 2, the
    ///   object at Destination → onCompleted exactly once → debug rows registered.
    ///
    /// Started by RunObjectAssemblyTest (Tools/ObjectAssembly/Run Test). Leaves play mode when done
    /// and exits the editor ONLY in batch mode (InteractionTestLog.Finish), so it is safe to run in
    /// an editor somebody is sitting at. Writes stills of Linking and of the joined object to
    /// &lt;project&gt;/Temp/oa-captures/.
    /// </summary>
    public class OATestRunner : MonoBehaviour
    {
        const string Tag = "OATest";

        [SerializeField] bool m_CaptureStills = true;

        IEnumerator Start()
        {
            var log = new InteractionTestLog(Tag);
            yield return null;

            OAController controller = FindAnyObjectByType<OAController>();
            NetworkManager nm = NetworkManager.Singleton;
            if (!log.Check(controller != null && nm != null, "scene has an OAController and a NetworkManager"))
            {
                log.Finish();
                yield break;
            }

            OAFragment a = controller.fragmentA, b = controller.fragmentB;
            OATrail trail = controller.trail;
            int completed = 0;
            controller.onCompleted.AddListener(() => completed++);

            // ---- host ----
            // On a free port, not the game's 7777: this runs in an editor other sessions use, where
            // a host killed mid-play by a domain reload can leave 7777 bound until the editor restarts.
            if (!nm.IsListening)
            {
                if (nm.NetworkConfig.NetworkTransport is UnityTransport utp)
                {
                    ushort port = FreeUdpPort();
                    utp.SetConnectionData("127.0.0.1", port, "0.0.0.0");
                    Debug.Log($"[{Tag}] hosting on UDP {port}.");
                }
                log.Check(nm.StartHost(), "StartHost returned true");
            }
            yield return log.WaitFor(() => controller.IsSpawned && a.IsSpawned && b.IsSpawned &&
                                           GameSessionManager.Instance != null && GameSessionManager.Instance.IsSpawned,
                                     15f, "host up; controller and halves spawned");
            yield return log.WaitFor(() => GameSessionManager.Instance != null && GameSessionManager.Instance.TryGetLocalRole(out _),
                                     10f, "host has a role");
            if (log.failures > 0)
            {
                // Nothing after this means anything without a running host.
                log.Finish();
                yield break;
            }
            ulong me = nm.LocalClientId;

            yield return null;
            log.Check(controller.phase == OAPhase.Dormant, "dormant before the session starts");
            log.Check(a.view.reveal01 == 0f && b.view.reveal01 == 0f, "halves hidden before the session starts");
            float lightsBefore = LightLevel(controller);

            // ---- Phase 0 ----
            log.Check(GameSessionManager.Instance.StartExperience(), "session started (solo)");
            yield return log.WaitFor(() => controller.phase == OAPhase.Phase0, 5f, "Start begins Phase 0");
            yield return log.WaitFor(() => controller.phase == OAPhase.Claiming,
                                     controller.appearTime + 10f, "Phase 0 → Appear → Claiming");
            log.Check(LightLevel(controller) > lightsBefore + 0.5f,
                      $"Phase 0 timeline brought the lights up ({lightsBefore:0.00} → {LightLevel(controller):0.00})");
            yield return log.WaitFor(() => a.view.reveal01 >= 1f && b.view.reveal01 >= 1f, 3f, "halves fully revealed");
            log.Check(Vector3.Distance(a.transform.position, controller.cornerP1.position) < 0.02f &&
                      Vector3.Distance(b.transform.position, controller.cornerP2.position) < 0.02f,
                      "halves appeared at Corner_P1 / Corner_P2");

            // ---- claim A ----
            InteractionTestHand hand = InteractionTestHand.Create("OATest Hand", a.transform.position);
            yield return null;
            log.Check(hand.TryGrab(a.interactable), "hand grabs half A");
            yield return log.WaitFor(() => a.holder == me && a.claimedBy == me && a.IsOwner, 3f,
                                     "server granted A: holder, claim and ownership are this client");
            yield return log.WaitFor(() => controller.phase1StartedLocally, 2f, "the claim started this player's Phase 1 line");
            log.Check(a.claimedRole != PlayerRole.Unassigned, $"A's claim carries a role ({a.claimedRole})");

            hand.MoveTo(hand.transform.position + new Vector3(0.1f, 0.05f, 0f));
            yield return null;
            hand.Release();
            yield return log.WaitFor(() => !a.isHeld && !a.heldLocally, 3f, "hand released A");
            log.Check(a.claimedBy == me && a.IsOwner, "A stays claimed and owned after release");

            // ---- the same hand may not take B ----
            yield return null;
            bool tookB = hand.TryGrab(b.interactable);
            log.Check(!tookB, "the same hand is refused half B");
            if (tookB) hand.Release();
            yield return null;
            log.Check(!b.isClaimed && !b.isHeld, "B stays unclaimed and unheld");

            // ---- A follows the head ----
            Vector3? followTarget = a.FollowTarget();
            if (log.Check(followTarget.HasValue, "a head camera to follow"))
            {
                float startGap = Vector3.Distance(a.transform.position, followTarget.Value);
                float endGap = startGap;
                float started = Time.realtimeSinceStartup;
                Camera head = Camera.main;
                while (Time.realtimeSinceStartup - started < 5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    Vector3? t = a.FollowTarget();
                    if (!t.HasValue) break;
                    endGap = Vector3.Distance(a.transform.position, t.Value);
                    Debug.Log($"[{Tag}] follow t+{Time.realtimeSinceStartup - started:0.0}s: gap {endGap:0.000} m, " +
                              $"following {a.following}, A {a.transform.position:F3}, target {t.Value:F3}, " +
                              $"head {(head != null ? head.transform.position.ToString("F3") : "none")}, dt {Time.deltaTime:0.000}");
                    if (endGap < 0.06f) break;
                }
                log.Check(a.following && endGap < 0.06f,
                          $"released A follows its claimant's head ({startGap:0.00} → {endGap:0.00} m)");
            }

            // ---- Phase 1 gate ----
            yield return log.WaitFor(() => controller.phase1ReportedLocally, 10f,
                                     "Phase 1 line ended by the clock and was reported");
            controller.DebugFire(DebugFireTarget.Player2); // claim B for P2 (no machine: forced)
            yield return null;
            log.Check(b.isClaimed && b.claimedRole == PlayerRole.Player2, "debug P2 claims B");
            controller.DebugFire(DebugFireTarget.Player2); // P2's line heard
            yield return log.WaitFor(() => controller.phase == OAPhase.Linking, 5f, "Phase 1 gate complete → Linking");
            yield return log.WaitFor(() => trail.visibility >= 0.999f, 5f, "trail faded in");
            log.Check(TrailRendering(trail), "trail strands are rendering");

            // ---- intensity follows distance ----
            log.Check(hand.TryGrab(a.interactable), "claimant grabs A again");
            yield return log.WaitFor(() => a.holder == me, 3f, "re-grab granted");

            float[] gaps = { 1.0f, 0.7f, 0.45f, 0.25f };
            var readings = new float[gaps.Length];
            for (int i = 0; i < gaps.Length; i++)
            {
                yield return PlaceGap(hand, a, b, gaps[i]);
                yield return new WaitForSeconds(1.2f);
                readings[i] = trail.intensity;
                Debug.Log($"[{Tag}] gap {controller.currentJoinGap:0.000} m → intensity {readings[i]:0.000}");
                if (i == 2 && m_CaptureStills) yield return Capture(controller, "oa_linking.png", Mid(a, b), 0.95f);
            }
            bool rising = true;
            for (int i = 1; i < readings.Length; i++) rising &= readings[i] > readings[i - 1];
            log.Check(rising && readings[^1] - readings[0] > 0.3f,
                      $"trail intensity rises as the halves close ({string.Join(" < ", System.Array.ConvertAll(readings, r => r.ToString("0.00")))})");
            log.Check(controller.phase == OAPhase.Linking, "still linking above the join distance");

            // ---- join ----
            yield return PlaceGap(hand, a, b, 0.05f);
            yield return log.WaitFor(() => controller.phase == OAPhase.Joined, 3f, "join points met → Joined");
            yield return null;
            log.Check(!a.interactable.isSelected && !hand.hasSelection, "the join cancelled the grab");
            log.Check(!a.interactable.enabled && !b.interactable.enabled, "grabs are off after the join");

            yield return new WaitForSeconds(controller.glideDuration + 0.15f);
            float seam = Vector3.Distance(a.joinPoint.position, b.joinPoint.position);
            Quaternion relative = Quaternion.Inverse(a.transform.rotation) * b.transform.rotation;
            log.Check(seam < 0.005f && Quaternion.Angle(relative, Quaternion.Inverse(a.layout.rotation) * b.layout.rotation) < 0.5f,
                      $"halves in the assembled layout (seam {seam * 1000f:0.0} mm)");
            log.Check(trail.visibility <= 0.001f, "trail faded out at the join");
            yield return log.WaitFor(() => controller.phase2Started, controller.settleDuration + 2f, "Phase 2 started after the settle");
            if (m_CaptureStills) yield return Capture(controller, "oa_joined.png", a.joinPoint.position, 0.8f);

            Vector3 floatStart = a.joinPoint.position;
            yield return new WaitForSeconds(Mathf.Min(1.5f, controller.floatDuration * 0.4f));
            log.Check(Vector3.Distance(a.joinPoint.position, controller.destination.position) <
                      Vector3.Distance(floatStart, controller.destination.position) - 0.01f,
                      "the assembled object is floating toward Destination");

            yield return log.WaitFor(() => completed >= 1, controller.floatDuration + 5f, "onCompleted fired");
            log.Check(controller.phase == OAPhase.Done || completed >= 1, "phase reached Done");
            log.Check(Vector3.Distance(a.joinPoint.position, controller.destination.position) < 0.01f &&
                      Vector3.Distance(b.joinPoint.position, controller.destination.position) < 0.01f,
                      "the assembled object arrived at Destination");
            if (m_CaptureStills) yield return Capture(controller, "oa_done.png", controller.destination.position, 0.9f);
            yield return new WaitForSeconds(1f);
            log.Check(completed == 1 && controller.completedCount == 1, $"onCompleted fired exactly once ({completed})");

            // ---- debug rows ----
            bool row = false, gate = false;
            foreach (IDebugInteraction e in InteractionDebug.entries)
            {
                if (e.debugId == "oa.assembly" && ReferenceEquals(e, controller)) row = true;
                if (e.debugId == controller.phase1TaskId) gate = true;
            }
            log.Check(row, "debug row 'oa.assembly' registered");
            log.Check(gate, $"task gate row '{controller.phase1TaskId}' registered");
            log.Check(!string.IsNullOrEmpty(controller.DebugState().detail), $"debug row reads: {controller.DebugState().detail}");

            Destroy(hand.gameObject);
            log.Finish();
        }

        static Vector3 Mid(OAFragment a, OAFragment b) => (a.transform.position + b.transform.position) * 0.5f;

        static ushort FreeUdpPort()
        {
            using var probe = new System.Net.Sockets.UdpClient(0);
            return (ushort)((System.Net.IPEndPoint)probe.Client.LocalEndPoint).Port;
        }

        static float LightLevel(OAController controller)
        {
            float sum = 0f;
            Transform root = controller.transform.parent;
            if (root == null) return 0f;
            foreach (Light l in root.GetComponentsInChildren<Light>(true)) sum += l.intensity;
            return sum;
        }

        static bool TrailRendering(OATrail trail)
        {
            foreach (LineRenderer lr in trail.GetComponentsInChildren<LineRenderer>(true))
                if (lr.enabled && lr.gameObject.activeInHierarchy && lr.positionCount > 1) return true;
            return false;
        }

        /// <summary>Carry A (in the hand) so its join point sits <paramref name="gap"/> metres from B's, on the line between them.</summary>
        static IEnumerator PlaceGap(InteractionTestHand hand, OAFragment a, OAFragment b, float gap)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector3 bj = b.joinPoint.position;
                Vector3 dir = a.joinPoint.position - bj;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.left;
                Vector3 target = bj + dir.normalized * gap;
                hand.MoveTo(hand.transform.position + (target - a.joinPoint.position));
                yield return null;
            }
        }

        /// <summary>Render a still from a temporary camera into &lt;project&gt;/Temp/oa-captures.</summary>
        static IEnumerator Capture(OAController controller, string file, Vector3 lookAt, float distance)
        {
            yield return new WaitForEndOfFrame();
            var go = new GameObject("OATest Capture Camera");
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = 50f;
                cam.nearClipPlane = 0.02f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                Vector3 eye = lookAt + new Vector3(0.15f, 0.35f, -1f).normalized * distance;
                go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(lookAt - eye, Vector3.up));

                rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;

                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp", "oa-captures");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, file);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log($"[{Tag}] still: {path}");
                cam.targetTexture = null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[{Tag}] could not capture '{file}': {e.Message}");
            }
            finally
            {
                if (rt != null) { rt.Release(); Destroy(rt); }
                if (tex != null) Destroy(tex);
                Destroy(go);
            }
        }
    }
}
