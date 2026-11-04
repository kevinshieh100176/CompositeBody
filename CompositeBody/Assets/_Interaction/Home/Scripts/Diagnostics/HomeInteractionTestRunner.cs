using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompositeBody.Diagnostics;
using CompositeBody.Interaction.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Interaction.Home.Diagnostics
{
    /// <summary>
    /// Play-mode check of Home in S2_v1 (Tools > Home > Run Home Interaction Test). Starts a host,
    /// starts the session, then walks the module with an <see cref="InteractionTestHand"/> through
    /// the real XRI select path:
    ///   room hidden at the start; fragments scattered in A minus B with spacing; breathing touches
    ///   only the Visual child; a grab makes this machine the holder and owner; held in B it
    ///   trembles and places itself after the dwell; let go in B it places; let go outside B it
    ///   floats where it was let go and breathes again; a grab is refused while somebody else holds
    ///   it (holder forced to 77); a forced timeout resolves the rest, the ceiling comes last,
    ///   onCompleted fires exactly once, every wall ends on its ORIGINAL materials with no property
    ///   block at its authored pose; a twin at _Reveal = 1 renders like the original (no pop); the
    ///   debug row is registered; a reset hides the room and scatters again.
    /// Logs "[HomeTest] RESULT: PASS/FAIL"; exits the editor only in batch mode, otherwise leaves play mode.
    /// </summary>
    public class HomeInteractionTestRunner : MonoBehaviour
    {
        public const string Tag = "HomeTest";

        [Tooltip("Where stills are written (PNG). Empty = no files.")]
        public string captureDirectory = "";

        [Tooltip("Seconds before the runner gives up and fails.")]
        public float watchdogSeconds = 240f;

        readonly InteractionTestLog m_Log = new(Tag);
        bool m_Finished;
        float m_StartedAt;
        HomeAssembly m_Assembly;
        InteractionTestHand m_Hand;
        int m_AllResolvedEvents;
        int m_CompletedEvents;

        IEnumerator Start()
        {
            m_StartedAt = Time.realtimeSinceStartup;
            yield return null; // everything has had its Awake/Start
            yield return Guarded(Run());
            Finish();
        }

        void Update()
        {
            if (m_Finished || Time.realtimeSinceStartup - m_StartedAt < watchdogSeconds) return;
            m_Log.Check(false, $"watchdog: still running after {watchdogSeconds:0}s");
            Finish();
        }

        void Finish()
        {
            if (m_Finished) return;
            m_Finished = true;
            StopAllCoroutines();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            m_Log.Finish();
        }

        IEnumerator Guarded(IEnumerator body)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!body.MoveNext()) yield break;
                    current = body.Current;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    m_Log.Check(false, $"exception: {e.GetType().Name}: {e.Message}");
                    yield break;
                }
                yield return current;
            }
        }

        // ---- the walk -------------------------------------------------------------------------

        IEnumerator Run()
        {
            m_Assembly = FindFirstObjectByType<HomeAssembly>();
            if (!m_Log.Check(m_Assembly != null && m_Assembly.built, "HomeAssembly found and built")) yield break;
            m_Assembly.onAllResolved.AddListener(() => m_AllResolvedEvents++);
            m_Assembly.onCompleted.AddListener(() => m_CompletedEvents++);

            HomeFragment[] f = m_Assembly.pairs.Select(p => p.fragment).ToArray();
            HomeZone a = m_Assembly.zoneA, b = m_Assembly.zoneB;
            m_Log.Check(f.Length == 5 && f.All(x => x != null), "five fragments");

            // -- dormant ---------------------------------------------------------------------
            m_Log.Check(InteractionDebug.entries.Any(e => e.debugId == "home.assembly"), "debug row 'home.assembly' registered");
            m_Log.Check(RoomHidden(), "room hidden before the beat");
            m_Log.Check(f.All(x => x.state == HomeFragmentState.Dormant && !Visible(x)), "fragments dormant and invisible");
            HomeZoneBeacon beacon = m_Assembly.beacon;
            HomeSlotGhost[] ghost = f.Select(x => m_Assembly.ghosts.FirstOrDefault(gh => gh != null && gh.fragment == x)).ToArray();
            if (!m_Log.Check(beacon != null && ghost.All(x => x != null), "beacon and five slot ghosts built")) yield break;
            m_Log.Check(beacon.level <= 0f && !BeaconShown(beacon), "beacon hidden while dormant");
            m_Log.Check(ghost.All(x => !x.shown), "slot ghosts off while dormant");
            Debug.Log($"[{Tag}] row: {m_Assembly.DebugState().detail}");

            // -- host + start ----------------------------------------------------------------
            NetworkManager nm = NetworkManager.Singleton;
            if (!m_Log.Check(nm != null, "NetworkManager in the scene")) yield break;
            UseFreePort(nm);
            if (!m_Log.Check(nm.StartHost(), "StartHost()")) yield break;
            yield return m_Log.WaitFor(() => nm.IsListening && nm.IsServer && f.All(x => x.IsSpawned), 10f, "host up, fragments spawned");
            yield return m_Log.WaitFor(() => GameSessionManager.Instance != null && GameSessionManager.Instance.IsSpawned, 5f,
                                       "GameSessionManager spawned");
            if (m_Log.failures > 0) yield break;
            m_Log.Check(!m_Assembly.armed, "still dormant in the lobby");

            yield return m_Log.WaitFor(() => GameSessionManager.Instance.connectedPlayerCount >= 1, 5f, "host registered as a player");
            m_Log.Check(GameSessionManager.Instance.StartExperience(), "session started (staff Start)");
            yield return m_Log.WaitFor(() => m_Assembly.armed && f.All(x => x.state == HomeFragmentState.Free), 3f,
                                       "session InProgress arms the beat; all five Free");
            m_Log.Check(m_Assembly.epoch >= 1, $"epoch {m_Assembly.epoch}");
            CheckScatter(f, a, b, "scatter");
            m_Log.Check(RoomHidden(), "room still hidden after arming");
            yield return Wait(m_Assembly.timings.appearDuration + 0.2f);
            m_Log.Check(f.All(Visible), "fragments appeared");
            yield return m_Log.WaitFor(() => beacon.level > 0.95f && BeaconShown(beacon), 2f, "beacon up on zone B after the beat starts");
            m_Log.Check(ghost.All(x => !x.shown), "ghosts off while nobody carries anything");
            Capture("home_beacon_armed", ZonePose(2.6f, 1.7f));

            // Clear of the KWS water (it does not write the depth the column intersects against).
            Renderer beaconRenderer = beacon.GetComponent<Renderer>();
            bool hasWater = HomeWaterProbe.TryGetSurfaceTop(SceneManager.GetActiveScene(), out float surface, out string waterDetail);
            float beaconBottom = beaconRenderer.bounds.min.y;
            m_Log.Check(!hasWater || beaconBottom >= surface + 0.1f,
                        $"beacon clear of the water at runtime (bottom {beaconBottom:0.000}, " +
                        $"{(hasWater ? $"surface {surface:0.000}: {waterDetail}" : "no water in the scene")})");
            Material beaconMaterial = beaconRenderer.sharedMaterial;
            m_Log.Check(beaconMaterial != null && Mathf.Approximately(beaconMaterial.GetFloat("_GroundPool"), 0f),
                        "beacon has no ground pool");
            if (hasWater)
            {
                Vector3 c = b.center;
                var eye = new Vector3(c.x + 0.7f, surface + 0.15f, c.z - 2.2f);
                Capture("home_beacon_water", new Pose(eye, Quaternion.LookRotation(new Vector3(c.x, beaconBottom + 0.15f, c.z) - eye)));
            }

            // -- breathing on the Visual child only ------------------------------------------
            Vector3 root0 = f[0].transform.position;
            Vector3 vis0 = f[0].view.transform.localPosition;
            Vector3 scale0 = f[0].view.transform.localScale;
            yield return Wait(0.6f);
            m_Log.Check((f[0].transform.position - root0).sqrMagnitude < 1e-10f, "breathing leaves the networked root still");
            m_Log.Check((f[0].view.transform.localPosition - vis0).sqrMagnitude > 1e-8f ||
                        (f[0].view.transform.localScale - scale0).sqrMagnitude > 1e-8f, "breathing moves the Visual child");

            m_Hand = InteractionTestHand.Create("HomeTestHand", f[0].transform.position);
            yield return null;

            // -- grab, dwell in B, auto-place ------------------------------------------------
            m_Log.Check(m_Hand.TryGrab(f[0].interactable), $"grab '{f[0].label}'");
            yield return m_Log.WaitFor(() => f[0].holder == nm.LocalClientId && f[0].OwnerClientId == nm.LocalClientId &&
                                             f[0].state == HomeFragmentState.Held, 2f, "holder and owner are the grabber; state Held");
            yield return Wait(0.4f);
            float farAlpha = ghost[0].alpha, farProximity = ghost[0].proximity01;
            m_Log.Check(ghost[0].shown && farAlpha >= 0.3f && farProximity < 1f,
                        $"ghost appears in the slot while carried (alpha {farAlpha:0.00}, proximity {farProximity:0.00})");
            m_Log.Check(ghost.Skip(1).All(x => !x.shown), "the other ghosts stay off");
            CarryCentreTo(f[0], b.center);
            float enteredB = Time.time;
            yield return m_Log.WaitFor(() => f[0].view.shake01 > 0.05f, 1f, "trembles in the hand inside B");
            yield return m_Log.WaitFor(() => InteractionPhaseClock.IsSet(f[0].zoneEnteredAt), 1f, "server stamped the zone entry");
            yield return Wait(0.6f);
            m_Log.Check(f[0].view.shake01 > 0.3f && f[0].state == HomeFragmentState.Held, $"tremble ramps with the dwell ({f[0].view.shake01:0.00})");
            m_Log.Check(ghost[0].proximity01 >= 0.999f && ghost[0].alpha > farAlpha + 0.02f,
                        $"ghost brighter near the slot ({farAlpha:0.00} -> {ghost[0].alpha:0.00}, full inside B)");
            Capture("home_ghost_held", ZonePose(1.3f, 1.55f));
            yield return m_Log.WaitFor(() => f[0].state == HomeFragmentState.Placed, m_Assembly.dwell + 1f, "auto-placed after the dwell");
            float dwelled = Time.time - enteredB;
            m_Log.Check(Mathf.Abs(dwelled - m_Assembly.dwell) < 0.4f, $"placed after {dwelled:0.00}s (dwell {m_Assembly.dwell:0.0}s)");
            yield return null;
            m_Log.Check(!ghost[0].shown, "ghost off the moment it is placed");
            m_Log.Check(!m_Hand.hasSelection && !f[0].interactable.isSelected, "placing cancelled the hand's selection");
            m_Log.Check(!f[0].interactable.enabled && !f[0].grabCollider.enabled, "grab and collider off once placed");
            m_Log.Check(f[0].holder == NetGrabbable.NoHolder, "holder cleared");
            yield return Wait(m_Assembly.timings.placeTravel + 0.2f);
            m_Log.Check(f[0].landed && Vector3.Distance(f[0].transform.position, f[0].slot.position) < 0.001f &&
                        Quaternion.Angle(f[0].transform.rotation, f[0].slot.rotation) < 0.1f, "glided onto its slot");
            HomeRevealGroup g0 = m_Assembly.pairs[0].group;
            yield return m_Log.WaitFor(() => g0.started && AnyVisible(g0), 2f, $"its wall '{g0.groupName}' starts coming back");
            yield return m_Log.WaitFor(() => beacon.level <= 0f && !BeaconShown(beacon), 2.5f,
                                       "beacon fades out after the first fragment is home");

            // -- release inside B places at once ----------------------------------------------
            m_Log.Check(m_Hand.TryGrab(f[1].interactable), $"grab '{f[1].label}'");
            yield return m_Log.WaitFor(() => f[1].state == HomeFragmentState.Held, 2f, "Held");
            CarryCentreTo(f[1], b.center);
            yield return null;
            yield return null;
            m_Hand.Release();
            float releasedAt = Time.time;
            yield return m_Log.WaitFor(() => f[1].state == HomeFragmentState.Placed, 1f, "let go inside B: placed");
            m_Log.Check(Time.time - releasedAt < m_Assembly.dwell * 0.5f, "placed by the release, not the dwell");

            // -- release outside B floats and breathes ----------------------------------------
            m_Log.Check(m_Hand.TryGrab(f[2].interactable), $"grab '{f[2].label}'");
            yield return m_Log.WaitFor(() => f[2].state == HomeFragmentState.Held, 2f, "Held");
            Vector3 outside = a.center + a.rotation * new Vector3(a.size.x * 0.32f, 0.05f, -a.size.z * 0.3f);
            m_Log.Check(a.Contains(outside) && !b.Contains(outside, m_Assembly.exclusionMargin), "release point is in A, outside B");
            CarryCentreTo(f[2], outside);
            yield return Wait(0.4f);
            m_Log.Check(f[2].state == HomeFragmentState.Held && f[2].view.shake01 <= 0.001f, "no tremble outside B");
            Vector3 releasePose = f[2].transform.position;
            m_Hand.Release();
            yield return m_Log.WaitFor(() => f[2].state == HomeFragmentState.Free && f[2].holder == NetGrabbable.NoHolder, 1f,
                                       "let go outside B: Free");
            m_Log.Check(f[2].OwnerClientId == NetworkManager.ServerClientId, "server owns it again");
            yield return Wait(1.0f);
            m_Log.Check(Vector3.Distance(f[2].transform.position, releasePose) < 0.01f, "floats where it was let go");
            m_Log.Check(f[2].view.breathingNow > 0.5f, $"breathes again ({f[2].view.breathingNow:0.00})");
            m_Log.Check(!ghost[2].shown, "ghost off after a let-go outside B");

            // -- refused grab while somebody else holds it ------------------------------------
            if (SetHolder(f[3], 77))
            {
                yield return Wait(0.35f);
                m_Log.Check(ghost[3].shown, "ghost shows while the other player (client 77) carries it");
                bool grabbed = m_Hand.TryGrab(f[3].interactable);
                m_Log.Check(!grabbed && !f[3].interactable.isSelected, "grab refused while client 77 holds it");
                m_Log.Check(f[3].state == HomeFragmentState.Free, "refusal leaves it Free");
                if (grabbed) m_Hand.Release();
                SetHolder(f[3], NetGrabbable.NoHolder);
                yield return Wait(0.4f);
                m_Log.Check(!ghost[3].shown, "ghost off again once nobody holds it");
            }
            m_Log.Check(beacon.level <= 0f, "beacon stays off for the rest of the beat");

            // -- timeout: the rest dissolve, every wall comes, ceiling last ------------------
            Debug.Log($"[{Tag}] row before timeout: {m_Assembly.DebugState().detail}");
            m_Assembly.AuthorityExpire();
            yield return m_Log.WaitFor(() => f.All(x => x.isResolved), 4f, "forced timeout resolves the rest");
            m_Log.Check(m_Assembly.placedCount == 2 && m_Assembly.vanishedCount == 3,
                        $"2 placed, 3 vanished (got {m_Assembly.placedCount}/{m_Assembly.vanishedCount})");
            yield return m_Log.WaitFor(() => m_AllResolvedEvents == 1, 1f, "onAllResolved fired");

            // A still while the room is coming back.
            HomeRevealGroup front = m_Assembly.pairs.Select(p => p.group).FirstOrDefault(g => g.groupName == "Front") ?? g0;
            yield return m_Log.WaitFor(() => front.playing && AnyVisible(front), 6f, $"'{front.groupName}' mid-reveal");
            yield return Wait(0.6f);
            Capture("home_reveal_mid", WidePose());

            yield return m_Log.WaitFor(() => m_Assembly.completed, 40f, "onCompleted (room back, paper gone)");
            HomeRevealGroup top = m_Assembly.finalGroup;
            double lastOther = m_Assembly.pairs.Max(p => p.group.endAt);
            m_Log.Check(top != null && top.started && top.startAt >= lastOther - 1e-3,
                        $"Top starts after the other five ({top?.startAt:0.00} >= {lastOther:0.00})");
            yield return Wait(1.5f);
            m_Log.Check(m_CompletedEvents == 1 && m_Assembly.completedCount == 1, $"onCompleted fired exactly once ({m_CompletedEvents})");
            m_Log.Check(m_AllResolvedEvents == 1, $"onAllResolved fired exactly once ({m_AllResolvedEvents})");
            m_Log.Check(f.All(x => !Visible(x)), "every fragment dissolved away");
            CheckRoomRestored();
            Capture("home_room_final", WidePose());

            yield return CheckNoPop();

            // -- reset: new epoch hides the room and scatters again ---------------------------
            int epochBefore = m_Assembly.epoch;
            m_Assembly.DebugReset();
            yield return m_Log.WaitFor(() => m_Assembly.epoch == epochBefore + 1 && f.All(x => x.state == HomeFragmentState.Free), 2f,
                                       "reset: new epoch, all Free");
            yield return null;
            m_Log.Check(RoomHidden(), "reset hides the room again");
            m_Log.Check(!m_Assembly.completed && !m_Assembly.allResolved, "reset clears the completion");
            CheckScatter(f, a, b, "re-scatter");
            m_Log.Check(ghost.All(x => !x.shown), "reset: ghosts off");
            yield return m_Log.WaitFor(() => beacon.level > 0.95f && BeaconShown(beacon), 2.5f, "reset: beacon back on");
            Debug.Log($"[{Tag}] row after reset: {m_Assembly.DebugState().detail}");
        }

        static bool BeaconShown(HomeZoneBeacon beacon)
        {
            var r = beacon != null ? beacon.GetComponent<Renderer>() : null;
            return r != null && r.enabled;
        }

        /// <summary>Looking at zone B from <paramref name="distance"/> metres back (-Z), eye at <paramref name="eyeHeight"/>.</summary>
        Pose ZonePose(float distance, float eyeHeight)
        {
            Vector3 c = m_Assembly.zoneB.center;
            var eye = new Vector3(c.x + distance * 0.35f, eyeHeight, c.z - distance);
            return new Pose(eye, Quaternion.LookRotation(c - eye));
        }

        // ---- helpers --------------------------------------------------------------------------

        /// <summary>
        /// Host on a free local port rather than the project's 7777: a shared editor can still be
        /// holding 7777 from a session that was torn down without a Shutdown (a domain reload in
        /// play mode leaks the transport's socket until the editor restarts). Runtime only.
        /// </summary>
        static void UseFreePort(NetworkManager nm)
        {
            if (nm.NetworkConfig?.NetworkTransport is not Unity.Netcode.Transports.UTP.UnityTransport utp) return;
            for (int port = 7790; port < 7890; port++)
            {
                try
                {
                    using var probe = new System.Net.Sockets.UdpClient(port);
                }
                catch (System.Net.Sockets.SocketException)
                {
                    continue;
                }
                utp.SetConnectionData("127.0.0.1", (ushort)port, "0.0.0.0");
                Debug.Log($"[{Tag}] hosting on UDP {port} for this run.");
                return;
            }
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        void CarryCentreTo(HomeFragment f, Vector3 centre) =>
            m_Hand.MoveTo(m_Hand.transform.position + (centre - f.worldCenter));

        void CheckScatter(HomeFragment[] f, HomeZone a, HomeZone b, string what)
        {
            float margin = m_Assembly.exclusionMargin;
            m_Log.Check(f.All(x => a.Contains(x.worldCenter, 0.001f)), $"{what}: every fragment centre in zone A");
            m_Log.Check(f.All(x => !b.Contains(x.worldCenter, margin - 0.001f)), $"{what}: none in zone B (+{margin:0.00} m)");
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < f.Length; i++)
                for (int j = i + 1; j < f.Length; j++)
                    nearest = Mathf.Min(nearest, Vector3.Distance(f[i].worldCenter, f[j].worldCenter));
            m_Log.Check(nearest >= m_Assembly.minSpacing - 0.001f, $"{what}: spacing {nearest:0.00} m >= {m_Assembly.minSpacing:0.00} m");
        }

        static bool Visible(HomeFragment f) =>
            f.view != null && f.view.GetComponentsInChildren<Renderer>().Any(r => !r.forceRenderingOff);

        IEnumerable<HomeRevealGroup> AllGroups()
        {
            foreach (HomeAssembly.Pair p in m_Assembly.pairs) if (p.group != null) yield return p.group;
            if (m_Assembly.finalGroup != null) yield return m_Assembly.finalGroup;
        }

        bool RoomHidden() => AllGroups().All(g => g.elementCount > 0 && g.renderers.All(r => r.forceRenderingOff));

        static bool AnyVisible(HomeRevealGroup g) => g.renderers.Any(r => !r.forceRenderingOff);

        void CheckRoomRestored()
        {
            foreach (HomeRevealGroup g in AllGroups())
            {
                bool materials = true, blocks = true, visible = true;
                foreach (Renderer r in g.renderers)
                {
                    Material[] originals = g.OriginalMaterialsOf(r);
                    if (originals == null || !r.sharedMaterials.SequenceEqual(originals)) materials = false;
                    if (r.HasPropertyBlock()) blocks = false;
                    if (r.forceRenderingOff) visible = false;
                }
                g.MaxPoseError(out float metres, out float degrees);
                m_Log.Check(g.done && materials && blocks && visible && metres < 0.001f && degrees < 0.1f,
                            $"wall '{g.groupName}': original materials {materials}, no MPB {blocks}, visible {visible}, " +
                            $"pose error {metres * 1000f:0.000} mm / {degrees:0.000} deg");
            }
        }

        static bool SetHolder(NetGrabbable g, ulong value)
        {
            FieldInfo field = typeof(NetGrabbable).GetField("m_Holder", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(g) is not NetworkVariable<ulong> holder)
            {
                Debug.LogError($"[{Tag}] FAIL cannot reach NetGrabbable.m_Holder to force a holder");
                return false;
            }
            holder.Value = value;
            return true;
        }

        // ---- stills and the no-pop check ------------------------------------------------------

        Pose WidePose()
        {
            Vector3 c = m_Assembly.zoneB.center;
            Vector3 eye = new(c.x, 1.65f, c.z - 2.6f);
            return new Pose(eye, Quaternion.LookRotation(new Vector3(c.x, 1.3f, c.z + 4f) - eye));
        }

        Texture2D Render(Pose pose, float fov = 80f, int size = 768)
        {
            var go = new GameObject("HomeTest Capture Camera");
            var cam = go.AddComponent<Camera>();
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(go);
            return tex;
        }

        void Capture(string name, Pose pose)
        {
            Texture2D tex = Render(pose);
            Save(tex, name);
            Destroy(tex);
        }

        void Save(Texture2D tex, string name)
        {
            if (string.IsNullOrEmpty(captureDirectory)) return;
            Directory.CreateDirectory(captureDirectory);
            string path = Path.Combine(captureDirectory, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[{Tag}] still: {path}");
        }

        /// <summary>
        /// The moment of the material swap, isolated: the same frame rendered with the reveal state
        /// at its end value (twin with _Reveal = 1, MeltingMemory with _Holes = authored, by MPB) and
        /// then with the original materials and no block. Any difference is a visible pop.
        /// </summary>
        IEnumerator CheckNoPop()
        {
            yield return null;
            HomeRevealGroup front = AllGroups().FirstOrDefault(g => g.groupName == "Front") ?? AllGroups().First();
            Renderer furniture = front.renderers.FirstOrDefault(r => r.sharedMaterials.All(m => !HomeRevealGroup.IsMelting(m)) &&
                                                                     r.sharedMaterials.Any(m => front.TwinOf(m) != null));
            Renderer wall = front.renderers.FirstOrDefault(r => r.sharedMaterials.Any(HomeRevealGroup.IsMelting));
            foreach ((Renderer r, string label) in new[] { (furniture, "furniture"), (wall, "wall") })
            {
                if (!m_Log.Check(r != null, $"no-pop: a {label} renderer to test")) continue;
                Bounds bounds = r.bounds;
                Vector3 eye = bounds.center + (m_Assembly.zoneB.center - bounds.center).normalized * Mathf.Max(1.5f, bounds.extents.magnitude * 1.6f);
                eye.y = Mathf.Max(eye.y, 1.2f);
                var pose = new Pose(eye, Quaternion.LookRotation(bounds.center - eye));

                Material[] originals = r.sharedMaterials;
                var reveal = originals.Select(m => HomeRevealGroup.IsMelting(m) ? m : front.TwinOf(m) ?? m).ToArray();
                var block = new MaterialPropertyBlock();
                block.SetFloat("_Reveal", 1f);
                Material melting = originals.FirstOrDefault(HomeRevealGroup.IsMelting);
                if (melting != null) block.SetFloat("_Holes", melting.GetFloat("_Holes"));

                r.sharedMaterials = reveal;
                r.SetPropertyBlock(block);
                Texture2D before = Render(pose, 60f, 512);
                r.SetPropertyBlock(null);
                r.sharedMaterials = originals;
                Texture2D after = Render(pose, 60f, 512);

                Diff(before, after, out float mean, out float changed);
                Save(before, $"home_pop_{label}_reveal_end");
                Save(after, $"home_pop_{label}_original");
                m_Log.Check(mean < 1.5f / 255f && changed < 0.01f,
                            $"no pop on '{r.name}' ({label}): mean diff {mean * 255f:0.00}/255, {changed * 100f:0.00}% of pixels > 8/255");
                Destroy(before);
                Destroy(after);
            }
        }

        static void Diff(Texture2D x, Texture2D y, out float mean, out float changedFraction)
        {
            Color32[] p = x.GetPixels32(), q = y.GetPixels32();
            double sum = 0;
            int changed = 0;
            for (int i = 0; i < p.Length; i++)
            {
                int d = Math.Max(Math.Abs(p[i].r - q[i].r), Math.Max(Math.Abs(p[i].g - q[i].g), Math.Abs(p[i].b - q[i].b)));
                sum += d;
                if (d > 8) changed++;
            }
            mean = (float)(sum / p.Length / 255.0);
            changedFraction = (float)changed / p.Length;
        }
    }
}
