using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Diagnostics;
using CompositeBody.Experience;
using CompositeBody.Interaction.Home;
using CompositeBody.Interaction.ObjectAssembly;
using Object = UnityEngine.Object;

namespace CompositeBody.Interaction.EditorTools
{
    /// <summary>
    /// Tools > MainScene > Integrate S2 Interactions. Puts the three modules in
    /// <c>Assets/_Interaction/</c> into the show as beats of S2, and nothing else:
    ///
    ///   OA Lamp    -> S2-2. The show HOLDS at the end of S2-2 until both players finish it.
    ///   Home       -> S2-3. Time-boxed inside S2-3; the room comes back around the players.
    ///   OA Camera  -> S2-3, cued part-way in. The show HOLDS at the end of S2-3 until it is done.
    ///
    /// WHERE THEY GO IS READ, NOT TYPED IN. All three modules are authored around a root with the
    /// players standing just behind it, so each root belongs at whatever pose the playspace has
    /// reached by the time its beat starts -- which S1 records onto the show timeline (the players
    /// walk from the world origin into the S3 room over 580-715s and stay there). The step samples
    /// that animation instead of carrying a copy of the numbers, so re-recording the move and
    /// re-running this puts the modules back where the players actually are. Beat windows come off
    /// the Control clips for the same reason.
    ///
    /// RE-RUNNABLE, AND KEEPS NETWORK IDENTITY. Existing module instances are found and updated
    /// rather than replaced: an in-scene NetworkObject is matched across machines by
    /// GlobalObjectIdHash, so re-instantiating would hand every fragment a new identity.
    ///
    /// NEVER SAVES SOMEBODY ELSE'S WORK: if any open scene has unsaved changes it fails and says
    /// so. Ends with <see cref="Verify"/>: PASS/FAIL lines and a RESULT line.
    /// </summary>
    public static class IntegrateS2Interactions
    {
        public const string Tag = "S2Integrate";

        const string k_HomePrefab = "Assets/_Interaction/Home/Prefabs/HomeInteraction.prefab";
        const string k_LampPrefab = "Assets/_Interaction/ObjectAssembly/Prefabs/OA_Lamp.prefab";
        const string k_CameraPrefab = "Assets/_Interaction/ObjectAssembly/Prefabs/OA_Camera.prefab";
        const string k_RevealFolder = "Assets/_Interaction/Home/Materials/Reveal";

        const string k_LampBeatRoot = "Beat_S2-2";
        const string k_HomeBeatRoot = "Beat_S2-3";
        const string k_CameraBeatRoot = "Beat_S2-3 Camera";
        const string k_CameraTrackName = "S2-3 Camera cue";

        // The gates the show waits on. Distinct from the modules' own phase-1 task ids
        // (oa.lamp.phase1, oa.camera.phase1), which gate a step INSIDE an interaction.
        const string k_LampDoneTask = "s2.lamp.done";
        const string k_CameraDoneTask = "s2.camera.done";
        const string k_LampGateLabel = "S2-2 lamp -- assembled by both players";
        const string k_CameraGateLabel = "S2-3 camera -- assembled by both players";

        /// <summary>16:26 on the show timeline: where the camera interaction is cued.</summary>
        const double k_CameraCueAt = 986.0;

        /// <summary>16:13 -- where Home is meant to be finished. Checked, not enforced.</summary>
        const double k_HomeEndsBy = 973.0;

        /// <summary>
        /// How long after its arming root comes up the show stops for an interaction.
        ///
        /// THE SHOW WAITS WHILE THE INTERACTION RUNS, rather than running underneath it. The
        /// interaction owns this stretch of the piece: it stamps and plays its own Phase 0/1/2
        /// off the shared clock, and <see cref="CompositeBody.Interaction.InteractionPhaseClock"/>
        /// is NGO server time, which a hold does not stop -- so parking the timeline at the top of
        /// the beat costs the interaction nothing, and the lines underneath it are not spent while
        /// two people are still looking for each other's half.
        ///
        /// Not on the boundary itself, which is the trap here: the driver banks the show time
        /// exactly at the marker and evaluates the timeline there, so a hold written for the very
        /// frame the root comes up is a coin toss between OAController seeing it active and the
        /// show already being parked a frame earlier. Half a second is ~30 frames for
        /// ServerTick to see its root, still far above the driver's own 1e-4 comparisons, and far
        /// below anything anybody can see.
        /// </summary>
        const double k_HoldAfterArm = 0.5;

        const string k_S3Path = "Experience/Beats/Beat_S2/Content/S2_v1/--- Environment ---/S3";
        const string k_InnerWallPath = k_S3Path + "/Inner_Wall";

        // ---- entry ----------------------------------------------------------------------------

        [MenuItem("Tools/MainScene/Integrate S2 Interactions")]
        public static void Integrate()
        {
            if (!MainSceneIntegrationUtil.RequireMainScene(Tag, out Scene scene)) return;
            if (!InteractionBuildUtil.RevealShaderOk(out string problem))
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - {InteractionBuildUtil.RevealShaderName} {problem}.");
                return;
            }

            try
            {
                IntegrateInto(scene);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[{Tag}] RESULT: FAIL - exception: {e.Message}");
            }
        }

        [MenuItem("Tools/MainScene/Verify S2 Interactions")]
        public static void VerifyMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != MainSceneIntegrationUtil.MainScenePath)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - open '{MainSceneIntegrationUtil.MainScenePath}' first.");
                return;
            }
            Verify(scene);
        }

        // ---- build ----------------------------------------------------------------------------

        static void IntegrateInto(Scene scene)
        {
            Transform beats = MainSceneIntegrationUtil.Beats(scene, Tag);
            if (beats == null) return;

            TimelineAsset tl = MainSceneIntegrationUtil.ShowTimeline(scene, Tag, out PlayableDirector dir);
            if (tl == null) return;

            if (!MainSceneIntegrationUtil.TryGetSceneWindow(tl, "S2-2", out double s22Start, out double s22End) ||
                !MainSceneIntegrationUtil.TryGetSceneWindow(tl, "S2-3", out double s23Start, out double s23End))
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - could not find the S2-2 and S2-3 Control clips on " +
                               $"'{AssetDatabase.GetAssetPath(tl)}'.");
                return;
            }
            Debug.Log($"[{Tag}] show windows read off the timeline: S2-2 {s22Start:0}-{s22End:0}s, " +
                      $"S2-3 {s23Start:0}-{s23End:0}s; camera cued at {k_CameraCueAt:0}s.");

            // ---- the arming roots -----------------------------------------------------------
            // Lamp and Home ride the beat roots the show already switches on. The camera is a cue
            // inside S2-3, so it gets one of its own.
            GameObject lampBeat = beats.Find(k_LampBeatRoot)?.gameObject;
            GameObject homeBeat = beats.Find(k_HomeBeatRoot)?.gameObject;
            if (lampBeat == null || homeBeat == null)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - '{k_LampBeatRoot}' or '{k_HomeBeatRoot}' is missing " +
                               $"under '/{MainSceneIntegrationUtil.BeatsPath}'.");
                return;
            }
            GameObject cameraBeat = MainSceneIntegrationUtil.EnsureBeatRoot(beats, k_CameraBeatRoot, Tag);

            // ---- the modules ----------------------------------------------------------------
            GameObject lamp = MainSceneIntegrationUtil.FindOrInstantiate(scene, k_LampPrefab, beats, Tag);
            GameObject home = MainSceneIntegrationUtil.FindOrInstantiate(scene, k_HomePrefab, beats, Tag);
            GameObject camera = MainSceneIntegrationUtil.FindOrInstantiate(scene, k_CameraPrefab, beats, Tag);
            if (lamp == null || home == null || camera == null) return;

            if (!Place(scene, lamp, (float)s22Start) ||
                !Place(scene, home, (float)s23Start) ||
                !Place(scene, camera, (float)k_CameraCueAt)) return;

            // ---- arming ---------------------------------------------------------------------
            var lampCtl = lamp.GetComponentInChildren<OAController>(true);
            var homeAsm = home.GetComponent<HomeAssembly>() ?? home.GetComponentInChildren<HomeAssembly>(true);
            var cameraCtl = camera.GetComponentInChildren<OAController>(true);
            if (lampCtl == null || homeAsm == null || cameraCtl == null)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - a module is missing its root component " +
                               $"(lamp={lampCtl != null}, home={homeAsm != null}, camera={cameraCtl != null}).");
                return;
            }

            MainSceneIntegrationUtil.SetRef(lampCtl, "m_BeatRoot", lampBeat);
            MainSceneIntegrationUtil.SetRef(homeAsm, "m_BeatRoot", homeBeat);
            MainSceneIntegrationUtil.SetRef(cameraCtl, "m_BeatRoot", cameraBeat);
            Debug.Log($"[{Tag}] armed: lamp <- '{lampBeat.name}', home <- '{homeBeat.name}', camera <- '{cameraBeat.name}'.");

            // ---- the camera cue -------------------------------------------------------------
            // Runs to the end of S2-3 rather than stopping at the hold: the show parks ON the
            // marker while it waits, and a clip that ended exactly there would switch the cue off
            // under the players while they are still assembling.
            MainSceneIntegrationUtil.EnsureActivation(dir, tl, k_CameraTrackName, cameraBeat,
                                                      k_CameraCueAt, s23End + 5.0, Tag);

            // ---- the gates ------------------------------------------------------------------
            InteractionTaskReporter lampReporter = EnsureReporter(lamp, k_LampDoneTask, k_LampGateLabel);
            InteractionTaskReporter cameraReporter = EnsureReporter(camera, k_CameraDoneTask, k_CameraGateLabel);
            MainSceneIntegrationUtil.WireEvent(lampCtl, "onCompleted", lampReporter, "Report", Tag);
            MainSceneIntegrationUtil.WireEvent(cameraCtl, "onCompleted", cameraReporter, "Report", Tag);
            // Each hold sits just after the root that arms its module, not at the end of the
            // beat: the lamp's root is S2-2 itself, the camera's is the cue inside S2-3.
            MainSceneIntegrationUtil.EnsureHoldMarker(tl, HoldTimeAfterArm(s22Start), k_LampDoneTask, Tag);
            MainSceneIntegrationUtil.EnsureHoldMarker(tl, HoldTimeAfterArm(k_CameraCueAt), k_CameraDoneTask, Tag);

            // ---- Home: the room it brings back ----------------------------------------------
            if (!PrepareRoom(scene, homeAsm)) return;

            // ---- save, hashes, save ---------------------------------------------------------
            AssetDatabase.SaveAssetIfDirty(tl);
            if (!MainSceneIntegrationUtil.SaveWithHashes(scene, Tag, new[] { lamp, home, camera })) return;

            Debug.Log($"[{Tag}] integrated 3 modules under '/{MainSceneIntegrationUtil.BeatsPath}' and saved " +
                      $"MainScene + {AssetDatabase.GetAssetPath(tl)}.");
            Verify(scene);
        }

        /// <summary>Puts a module root where the players are standing when its beat starts.</summary>
        static bool Place(Scene scene, GameObject module, float showTime)
        {
            if (!MainSceneIntegrationUtil.TryPlayspacePose(scene, showTime, Tag, out Pose pose)) return false;
            module.transform.SetPositionAndRotation(pose.position, pose.rotation);
            module.transform.localScale = Vector3.one;
            if (PrefabUtility.IsPartOfPrefabInstance(module))
                PrefabUtility.RecordPrefabInstancePropertyModifications(module.transform);
            Debug.Log($"[{Tag}] '{module.name}' placed at {MainSceneIntegrationUtil.Fmt(pose.position)} " +
                      $"(the playspace at {showTime:0}s).");
            return true;
        }

        /// <summary>The reporter that turns a module completion into the task the show waits on.</summary>
        static InteractionTaskReporter EnsureReporter(GameObject module, string taskId, string label)
        {
            Transform existing = module.transform.Find("Task Reporter");
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject("Task Reporter");
                go.transform.SetParent(module.transform, false);
                Debug.Log($"[{Tag}] added a Task Reporter under '{module.name}'.");
            }

            var reporter = InteractionBuildUtil.GetOrAdd<InteractionTaskReporter>(go);
            reporter.Configure(taskId, label);
            EditorUtility.SetDirty(reporter);
            if (PrefabUtility.IsPartOfPrefabInstance(reporter))
                PrefabUtility.RecordPrefabInstancePropertyModifications(reporter);
            return reporter;
        }

        /// <summary>
        /// Points Home's six reveal groups at MainScene's own walls and makes those walls
        /// movable.
        ///
        /// The groups are authored against the demo scene's copy of the same FBX, so the targets
        /// are scene references that a prefab cannot carry and have to be written as overrides
        /// here. Everything else a group needs -- the element list, the authored poses, the
        /// original materials -- it reads at Awake from whatever the target actually is, so the
        /// only serialized state to rebuild is the target, the outward axis and the twin table.
        /// </summary>
        static bool PrepareRoom(Scene scene, HomeAssembly assembly)
        {
            Transform inner = MainSceneIntegrationUtil.FindPath(scene, k_InnerWallPath);
            Transform s3 = MainSceneIntegrationUtil.FindPath(scene, k_S3Path);
            if (inner == null || s3 == null)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - '/{k_InnerWallPath}' is missing.");
                return false;
            }

            // Active, because Netcode aside, a group reads its target's authored pose and
            // materials in Awake -- and Home then holds every renderer with forceRenderingOff
            // until its fragment lands, so an active room is still an invisible one.
            if (!inner.gameObject.activeSelf)
            {
                inner.gameObject.SetActive(true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(inner.gameObject);
                Debug.Log($"[{Tag}] switched '{inner.name}' on (Home hides it with forceRenderingOff until its moment).");
            }

            // A statically batched renderer cannot visibly move, and every element of the reveal
            // flies in from an offset.
            int cleared = InteractionBuildUtil.ClearBatchingStatic(inner.gameObject);
            if (cleared > 0)
            {
                foreach (Transform t in inner.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                Debug.Log($"[{Tag}] cleared BatchingStatic on {cleared} object(s) under Inner_Wall (ContributeGI kept).");
            }

            // If anybody has run BuildS2Dollhouse since, the walls carry its *_Dissolve materials
            // as overrides -- and Home would record those lossy materials as the originals it puts
            // back at the end.
            int reverted = 0;
            foreach (Renderer r in inner.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.sharedMaterials.Any(m => m != null && m.name.EndsWith("_Dissolve"))) continue;
                PrefabUtility.RevertPropertyOverride(new SerializedObject(r).FindProperty("m_Materials"),
                                                     InteractionMode.AutomatedAction);
                reverted++;
            }
            if (reverted > 0)
                Debug.Log($"[{Tag}] reverted dissolve-material overrides on {reverted} renderer(s) back to the S2_v1 originals.");

            // The room's own bounds, so each wall's outward axis is derived rather than trusted.
            var walls = new List<Transform>();
            Bounds room = default;
            bool first = true;
            foreach (Transform child in inner)
            {
                var mf = child.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                walls.Add(child);
                Bounds b = WorldBounds(child, mf.sharedMesh);
                if (first) { room = b; first = false; } else room.Encapsulate(b);
            }

            var groups = new List<HomeRevealGroup>();
            foreach (HomeAssembly.Pair p in assembly.pairs) if (p?.group != null) groups.Add(p.group);
            if (assembly.finalGroup != null) groups.Add(assembly.finalGroup);

            if (groups.Count != 6 || walls.Count != 6)
            {
                Debug.LogError($"[{Tag}] RESULT: FAIL - expected 6 reveal groups and 6 walls, found " +
                               $"{groups.Count} and {walls.Count}.");
                return false;
            }

            int made = 0;
            foreach (HomeRevealGroup g in groups)
            {
                // The authored mapping lives in the group's own target path; only the scene it
                // points into changes.
                string leaf = g.targetPath.TrimEnd('/');
                leaf = leaf.Substring(leaf.LastIndexOf('/') + 1);
                Transform wall = walls.FirstOrDefault(w => w.name == leaf);
                if (wall == null)
                {
                    Debug.LogError($"[{Tag}] RESULT: FAIL - group '{g.groupName}' wants '{leaf}', which is not " +
                                   $"a wall of MainScene's Inner_Wall ({string.Join(", ", walls.Select(w => w.name))}).");
                    return false;
                }

                var mf = wall.GetComponent<MeshFilter>();
                Vector3 offset = WorldBounds(wall, mf.sharedMesh).center - room.center;
                Vector3 direction = DominantAxis(offset, room.extents);

                var sources = new List<Material>();
                var twins = new List<Material>();
                foreach (Renderer r in wall.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null || sources.Contains(m) || HomeRevealGroup.IsMelting(m)) continue;
                        Material twin = InteractionBuildUtil.MakeRevealTwin(m, k_RevealFolder);
                        if (twin == null)
                        {
                            Debug.LogError($"[{Tag}] RESULT: FAIL - no reveal twin could be made for '{m.name}'.");
                            return false;
                        }
                        if (!AssetDatabase.Contains(twin)) made++;
                        sources.Add(m);
                        twins.Add(twin);
                    }

                g.Configure(g.groupName, wall, MainSceneIntegrationUtil.PathOf(wall), direction,
                            sources.ToArray(), twins.ToArray());
                EditorUtility.SetDirty(g);
                if (PrefabUtility.IsPartOfPrefabInstance(g)) PrefabUtility.RecordPrefabInstancePropertyModifications(g);

                Debug.Log($"[{Tag}] group {g.groupName,-6} -> '{wall.name}' float {MainSceneIntegrationUtil.Fmt(direction)}, " +
                          $"{sources.Count} twin(s), {wall.GetComponentsInChildren<Renderer>(true).Length} renderer(s).");
            }
            if (made > 0) Debug.Log($"[{Tag}] created {made} new reveal twin(s) in {k_RevealFolder}.");

            return true;
        }

        /// <summary>
        /// Where the hold belongs for a module whose arming root comes up at
        /// <paramref name="armedAt"/>: <see cref="k_HoldAfterArm"/> later, at the top of the beat.
        ///
        /// Used by both the integrate step and Verify, so the marker it writes and the marker it
        /// checks for cannot drift apart.
        ///
        /// The held frame still has to be a frame of the beat with nothing fading over it, and
        /// <see cref="CheckGate"/> asserts both. At the top of a beat that comes free: this show's
        /// fades straddle beat ENDS (S2 -> S3 runs 996-1006s), never their first second.
        /// </summary>
        static double HoldTimeAfterArm(double armedAt) => armedAt + k_HoldAfterArm;

        /// <summary>
        /// 986 as '16:26', for log lines that quote a clock time beside the seconds.
        ///
        /// Computed rather than written out: the two that were written out both went stale the
        /// first time S2 was re-cut, and a label naming the wrong minute is worse than no label.
        /// </summary>
        static string Clock(double seconds) =>
            string.Format("{0:0}:{1:00}", System.Math.Floor(seconds / 60.0), System.Math.Floor(seconds % 60.0));

        static Bounds WorldBounds(Transform t, Mesh mesh)
        {
            Bounds b = mesh.bounds;
            Matrix4x4 m = t.localToWorldMatrix;
            var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                         (i & 2) == 0 ? b.min.y : b.max.y,
                                         (i & 4) == 0 ? b.min.z : b.max.z);
                result.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return result;
        }

        static Vector3 DominantAxis(Vector3 offset, Vector3 half)
        {
            var n = new Vector3(offset.x / Mathf.Max(half.x, 1e-3f),
                                offset.y / Mathf.Max(half.y, 1e-3f),
                                offset.z / Mathf.Max(half.z, 1e-3f));
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(n.x), 0, 0);
            if (ay >= az) return new Vector3(0, Mathf.Sign(n.y), 0);
            return new Vector3(0, 0, Mathf.Sign(n.z));
        }

        // ---- verify ---------------------------------------------------------------------------

        public static bool Verify(Scene scene)
        {
            var failures = new List<string>();
            bool Check(bool ok, string what) => InteractionBuildUtil.Check(Tag, ok, what, failures);

            Transform beats = MainSceneIntegrationUtil.FindPath(scene, MainSceneIntegrationUtil.BeatsPath);
            if (!Check(beats != null && beats.gameObject.activeInHierarchy,
                       $"/{MainSceneIntegrationUtil.BeatsPath} exists and is active"))
            {
                InteractionBuildUtil.Verdict(Tag, failures);
                return false;
            }

            TimelineAsset tl = MainSceneIntegrationUtil.ShowTimeline(scene, Tag, out PlayableDirector dir);
            if (!Check(tl != null, "the show timeline is reachable"))
            {
                InteractionBuildUtil.Verdict(Tag, failures);
                return false;
            }

            MainSceneIntegrationUtil.TryGetSceneWindow(tl, "S2-2", out double s22Start, out double s22End);
            MainSceneIntegrationUtil.TryGetSceneWindow(tl, "S2-3", out double s23Start, out double s23End);

            // One instance of each module, under Beats, with its beat root set.
            GameObject lamp = InstanceOf(beats, k_LampPrefab);
            GameObject home = InstanceOf(beats, k_HomePrefab);
            GameObject camera = InstanceOf(beats, k_CameraPrefab);
            Check(lamp != null, "exactly one OA_Lamp under /Experience/Beats");
            Check(home != null, "exactly one HomeInteraction under /Experience/Beats");
            Check(camera != null, "exactly one OA_Camera under /Experience/Beats");
            if (lamp == null || home == null || camera == null)
            {
                InteractionBuildUtil.Verdict(Tag, failures);
                return false;
            }

            var lampCtl = lamp.GetComponentInChildren<OAController>(true);
            var cameraCtl = camera.GetComponentInChildren<OAController>(true);
            var homeAsm = home.GetComponent<HomeAssembly>() ?? home.GetComponentInChildren<HomeAssembly>(true);
            Check(lampCtl != null && homeAsm != null && cameraCtl != null, "each module has its root component");
            if (lampCtl == null || homeAsm == null || cameraCtl == null)
            {
                InteractionBuildUtil.Verdict(Tag, failures);
                return false;
            }

            Check(homeAsm.built, "HomeAssembly is built");
            Check(BeatRootOf(lampCtl, "m_BeatRoot") != null && BeatRootOf(lampCtl, "m_BeatRoot").name == k_LampBeatRoot,
                  $"lamp arms off '{k_LampBeatRoot}'");
            Check(homeAsm.beatRoot != null && homeAsm.beatRoot.name == k_HomeBeatRoot,
                  $"home arms off '{k_HomeBeatRoot}'");
            GameObject cameraBeat = BeatRootOf(cameraCtl, "m_BeatRoot");
            Check(cameraBeat != null && cameraBeat.name == k_CameraBeatRoot, $"camera arms off '{k_CameraBeatRoot}'");

            // Arming roots must be switched off in the file: the show owns when they come on, and
            // a module armed at scene load would start during S1.
            foreach (string n in new[] { k_LampBeatRoot, k_HomeBeatRoot, k_CameraBeatRoot })
            {
                Transform t = beats.Find(n);
                Check(t != null && !t.gameObject.activeSelf, $"'{n}' is inactive in the file");
            }

            // Module roots are NOT under a beat root (they would never spawn).
            foreach (GameObject m in new[] { lamp, home, camera })
                Check(m.transform.parent == beats, $"'{m.name}' sits directly under /Experience/Beats");

            // Placement: each root on the playspace pose for its own cue.
            CheckPlaced(scene, lamp, (float)s22Start, Check);
            CheckPlaced(scene, home, (float)s23Start, Check);
            CheckPlaced(scene, camera, (float)k_CameraCueAt, Check);

            // The camera cue.
            ActivationTrack cue = tl.GetOutputTracks().OfType<ActivationTrack>()
                                    .FirstOrDefault(t => t.name == k_CameraTrackName);
            Check(cue != null, $"the ActivationTrack '{k_CameraTrackName}' is on the show timeline");
            if (cue != null)
            {
                Check(dir.GetGenericBinding(cue) as GameObject == cameraBeat, $"'{k_CameraTrackName}' is bound to '{k_CameraBeatRoot}'");
                TimelineClip clip = cue.GetClips().FirstOrDefault();
                Check(clip != null && System.Math.Abs(clip.start - k_CameraCueAt) < 0.01,
                      $"the camera cue starts at {k_CameraCueAt:0}s ({Clock(k_CameraCueAt)})" +
                      (clip != null ? $" -- it starts at {clip.start:0.00}s" : ""));
                Check(clip != null && clip.end >= s23End, $"the camera cue runs to at least the end of S2-3 ({s23End:0}s)");
            }

            // The two gates.
            CheckGate(tl, lamp, lampCtl, k_LampDoneTask, HoldTimeAfterArm(s22Start), s22Start, s22End, "lamp", Check);
            CheckGate(tl, camera, cameraCtl, k_CameraDoneTask, HoldTimeAfterArm(k_CameraCueAt), s23Start, s23End, "camera", Check);

            // Home must not be armed while the lamp gate is still holding: the show parks ON the
            // marker, so a lamp hold at or past 853s is already the first frame of S2-3.
            ShowHoldMarker lampHold = tl.markerTrack == null ? null :
                tl.markerTrack.GetMarkers().OfType<ShowHoldMarker>().FirstOrDefault(m => m.taskId == k_LampDoneTask);
            Check(lampHold != null && lampHold.time < s23Start,
                  $"the lamp hold is before S2-3 starts ({s23Start:0}s), so Home does not arm during it" +
                  (lampHold != null ? $" -- it is at {lampHold.time:0.00}s" : ""));

            // Home is time-boxed, so its worst case has to fit inside its window.
            double homeWorstCase = HomeWorstCaseEnd(homeAsm, s23Start);
            Check(homeWorstCase <= k_HomeEndsBy,
                  $"home finishes by {k_HomeEndsBy:0}s ({Clock(k_HomeEndsBy)}) -- worst case {homeWorstCase:0.0}s " +
                  $"(armed {s23Start:0}s + timeout {homeAsm.timings.timeout:0}s + reveal tail)");
            Check(homeWorstCase <= s23End, $"home finishes inside S2-3 (ends {s23End:0}s)");

            // Home's room.
            Transform inner = MainSceneIntegrationUtil.FindPath(scene, k_InnerWallPath);
            Check(inner != null, "MainScene has the S3 Inner_Wall");
            if (inner != null)
            {
                Check(inner.gameObject.activeSelf, "Inner_Wall is active (Home hides it by forceRenderingOff)");
                Check(!InteractionBuildUtil.AnyBatchingStatic(inner.gameObject), "no BatchingStatic left under Inner_Wall");
                int dissolve = inner.GetComponentsInChildren<Renderer>(true)
                                    .SelectMany(r => r.sharedMaterials)
                                    .Count(m => m != null && m.name.EndsWith("_Dissolve"));
                Check(dissolve == 0, $"no *_Dissolve material on Inner_Wall (found {dissolve})");

                // Placement follows the playspace, the room does not. If somebody re-records the
                // S1 move, the fragments could be scattered through a wall and this is what says so.
                Bounds room = default;
                bool any = false;
                foreach (Renderer r in inner.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { room = r.bounds; any = true; } else room.Encapsulate(r.bounds);
                }
                HomeZone zoneA = homeAsm.zoneA;
                Check(zoneA != null, "home has its scatter zone A");
                if (any && zoneA != null)
                {
                    Vector3 h = zoneA.size * 0.5f;
                    bool inside = true;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y,
                                                 (i & 4) == 0 ? -h.z : h.z);
                        if (!room.Contains(zoneA.center + zoneA.rotation * corner)) inside = false;
                    }
                    Check(inside, $"zone A {MainSceneIntegrationUtil.Fmt(zoneA.size)} at " +
                                  $"{MainSceneIntegrationUtil.Fmt(zoneA.center)} is inside the room " +
                                  $"{MainSceneIntegrationUtil.Fmt(room.size)} at {MainSceneIntegrationUtil.Fmt(room.center)}");
                }
            }

            var groups = new List<HomeRevealGroup>();
            foreach (HomeAssembly.Pair p in homeAsm.pairs) if (p?.group != null) groups.Add(p.group);
            if (homeAsm.finalGroup != null) groups.Add(homeAsm.finalGroup);
            Check(groups.Count == 6, $"home has 6 reveal groups (found {groups.Count})");
            Check(groups.Select(g => g.target).Distinct().Count() == groups.Count, "each group has its own wall");

            foreach (HomeRevealGroup g in groups)
            {
                bool inMain = g.target != null && g.target.gameObject.scene == scene &&
                              inner != null && g.target.IsChildOf(inner);
                Check(inMain, $"group {g.groupName}: target is a MainScene Inner_Wall child" +
                              (g.target != null ? $" (it is '{g.target.name}')" : " (it is NULL)"));
                Check(g.target == null || g.targetPath == MainSceneIntegrationUtil.PathOf(g.target),
                      $"group {g.groupName}: path fallback matches the target");
                if (g.target == null) continue;

                int slots = 0, bad = 0;
                foreach (Renderer r in g.target.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                    {
                        slots++;
                        if (m == null) { bad++; continue; }
                        if (HomeRevealGroup.IsMelting(m)) continue;
                        Material twin = g.TwinOf(m);
                        if (twin == null || !InteractionBuildUtil.IsRevealMaterialOk(twin) || !AssetDatabase.Contains(twin)) bad++;
                    }
                Check(bad == 0, $"group {g.groupName}: all {slots} material slot(s) are melting or have a twin asset");
            }

            // Grab stack on every fragment.
            foreach (GameObject m in new[] { lamp, home, camera })
                foreach (NetGrabbable grab in m.GetComponentsInChildren<NetGrabbable>(true))
                    InteractionBuildUtil.VerifyGrabbable(Tag, grab.gameObject, failures);

            // Network.
            InteractionBuildUtil.VerifyHashes(Tag, scene, failures);
            int managers = Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                 .Count(n => n.gameObject.scene == scene);
            Check(managers == 1, $"exactly one NetworkManager in MainScene (found {managers})");
            Check(beats.GetComponentsInChildren<Transform>(true)
                       .All(t => t.name != InteractionBuildUtil.BootstrapRootName),
                  "no demo bootstrap was brought into MainScene");

            // Ids unique across the whole scene.
            var ids = new List<string>();
            foreach (GameObject go in scene.GetRootGameObjects())
                foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb is IDebugInteraction di && !string.IsNullOrWhiteSpace(di.debugId)) ids.Add(di.debugId);
            foreach (string id in new[] { "oa.lamp", "home.assembly", "oa.camera" })
                Check(ids.Count(i => i == id) == 1, $"debug id '{id}' appears once in MainScene");

            var taskIds = scene.GetRootGameObjects()
                               .SelectMany(g => g.GetComponentsInChildren<InteractionTaskReporter>(true))
                               .Select(r => r.taskId).ToList();
            foreach (string id in new[] { k_LampDoneTask, k_CameraDoneTask })
                Check(taskIds.Count(i => i == id) == 1, $"task id '{id}' is reported by exactly one reporter");

            // The legacy beats this replaces stay switched off.
            foreach (string n in new[] { "S2-2 Fragments", "S2-3 Dollhouse" })
            {
                Transform t = beats.Find(n);
                Check(t == null || !t.gameObject.activeSelf, $"legacy '{n}' is still inactive");
            }

            InteractionBuildUtil.Verdict(Tag, failures);
            return failures.Count == 0;
        }

        static void CheckPlaced(Scene scene, GameObject module, float showTime, System.Func<bool, string, bool> check)
        {
            if (!MainSceneIntegrationUtil.TryPlayspacePose(scene, showTime, Tag, out Pose pose)) return;
            float err = Vector3.Distance(module.transform.position, pose.position);
            check(err < 0.001f, $"'{module.name}' is on the playspace pose at {showTime:0}s " +
                                $"({MainSceneIntegrationUtil.Fmt(pose.position)}, out by {err * 1000f:0.0} mm)");
        }

        static void CheckGate(TimelineAsset tl, GameObject module, OAController controller, string taskId,
                              double at, double windowStart, double windowEnd, string what,
                              System.Func<bool, string, bool> check)
        {
            var reporter = module.GetComponentsInChildren<InteractionTaskReporter>(true)
                                 .FirstOrDefault(r => r.taskId == taskId);
            check(reporter != null, $"{what}: an InteractionTaskReporter for '{taskId}'");
            if (reporter != null)
                check(MainSceneIntegrationUtil.HasEventCall(controller, "onCompleted", reporter, "Report"),
                      $"{what}: onCompleted calls the reporter");

            ShowHoldMarker hold = tl.markerTrack == null ? null :
                tl.markerTrack.GetMarkers().OfType<ShowHoldMarker>().FirstOrDefault(m => m.taskId == taskId);
            check(hold != null, $"{what}: a ShowHoldMarker waits on '{taskId}'");
            if (hold == null) return;

            check(System.Math.Abs(hold.time - at) < 0.01,
                  $"{what}: the hold is at {at:0.00}s (it is at {hold.time:0.00}s)");

            // The held frame has to be a frame of this beat, with nothing fading over it.
            check(hold.time > windowStart && hold.time < windowEnd,
                  $"{what}: the hold is strictly inside its beat ({windowStart:0}-{windowEnd:0}s)");
            foreach (TrackAsset track in tl.GetOutputTracks())
            {
                if (track.name == null || track.name.IndexOf("Fade", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                foreach (TimelineClip clip in track.GetClips())
                    check(hold.time <= clip.start || hold.time >= clip.end,
                          $"{what}: the hold is not inside the fade '{clip.displayName}' ({clip.start:0}-{clip.end:0}s)");
            }
        }

        static GameObject InstanceOf(Transform parent, string prefabPath)
        {
            var found = new List<GameObject>();
            foreach (Transform child in parent)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) &&
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject) == prefabPath)
                    found.Add(child.gameObject);
            return found.Count == 1 ? found[0] : null;
        }

        static GameObject BeatRootOf(Component c, string property) =>
            new SerializedObject(c).FindProperty(property)?.objectReferenceValue as GameObject;

        /// <summary>
        /// When Home is finished if nobody places anything: the timeout takes the five fragments
        /// one at a time, each one's wall flies in behind it, and the ceiling follows the last of
        /// them. Worst case assumes the slowest wall is the one the last fragment brings back.
        /// </summary>
        static double HomeWorstCaseEnd(HomeAssembly assembly, double armedAt)
        {
            var so = new SerializedObject(assembly);
            float Get(string p, float fallback)
            {
                SerializedProperty sp = so.FindProperty(p);
                return sp != null ? sp.floatValue : fallback;
            }

            HomeAssembly.Timings t = assembly.timings;
            float stagger = Get("m_TimeoutStagger", 0.35f);
            float groupDelay = Get("m_GroupDelay", 0.25f);
            float finalDelay = Get("m_FinalGroupDelay", 0.5f);

            int fragments = assembly.pairs?.Count(p => p?.fragment != null) ?? 0;
            double lastVanished = armedAt + t.timeout + System.Math.Max(0, fragments - 1) * stagger + t.vanishDuration;

            double slowestGroup = 0;
            foreach (HomeAssembly.Pair p in assembly.pairs)
                if (p?.group != null) slowestGroup = System.Math.Max(slowestGroup, GroupDuration(p.group));

            double wallsDone = lastVanished + groupDelay + slowestGroup;
            double ceilingDone = wallsDone + finalDelay +
                                 (assembly.finalGroup != null ? GroupDuration(assembly.finalGroup) : 0);
            return ceilingDone;
        }

        /// <summary>
        /// How long a group takes, computed the way it will at runtime.
        ///
        /// NOT <c>HomeRevealGroup.duration</c>: that counts the elements it prepared, and a group
        /// only prepares them in Awake. Read in the editor it reports the one-element case and the
        /// whole estimate comes out several seconds short -- which is the direction that turns a
        /// beat that overruns its window into a PASS.
        /// </summary>
        static double GroupDuration(HomeRevealGroup g)
        {
            var so = new SerializedObject(g);
            float Get(string p, float fallback)
            {
                SerializedProperty sp = so.FindProperty(p);
                return sp != null ? sp.floatValue : fallback;
            }

            // Prepare() adds the wall itself, then each direct child with its whole subtree.
            int elements = g.target != null ? 1 + g.target.childCount : 1;
            return System.Math.Max(0, elements - 1) * Get("m_Stagger", 0.3f)
                   + Get("m_ElementTravel", 1.8f) + Get("m_WobbleDuration", 0.7f);
        }
    }
}
