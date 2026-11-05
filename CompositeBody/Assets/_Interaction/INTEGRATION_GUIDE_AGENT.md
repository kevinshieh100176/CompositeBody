# Integrating the `_Interaction` modules into MainScene — guide for AI agents

Audience: an AI coding agent working in this repository. A human-facing Chinese version is
`INTEGRATION_GUIDE_zh-TW.md` beside this file. Facts below were verified against the code and
against `Assets/_Scenes/MainScene.unity` on 2026-11-04 (branch `home-interaction`, commit
`c859c824`). Re-verify anything marked **re-check** before relying on it.

Goal: put the Home module and the ObjectAssembly variants (Lamp, Camera) into
`Assets/_Scenes/MainScene.unity` so they run as beats of the show, networked for two players,
without breaking the existing show.

---

## 0. Read first / ground rules

1. Read the repo-root `CLAUDE.md`. Unity work goes through the `unity` CLI against a running
   editor (`unity status`, `unity recompile`, `unity cmd <command>`, `unity cmd menu "<path>"`,
   `unity cmd eval '<C#>'`). Judge every result by the JSON `success` field, never the exit code.
   `unity list` no longer exists — use `unity command` to list commands, `unity cmd <name> --help`
   for arguments.
2. The editor may be in use by a person. Never save a scene you did not mean to change. Never
   hand-edit `.unity` / `.prefab` / `.asset` YAML while an editor is reachable — drive the editor.
3. Prefer **editor code** (a builder step with a `Verify()` that logs `PASS/FAIL`) over one-off
   manual edits: MainScene is rebuilt and re-baked by other people, and an integration you cannot
   re-run will rot. Follow the existing pattern in `Assets/_Interaction/*/Editor/Build*.cs` and
   `Assets/_Interaction/_Shared/Editor/InteractionBuildUtil.cs`.
4. Every interaction implements `IDebugInteraction` (already true for all three modules). Debug
   ids and story-task ids must be unique across the show.
5. Do not change files outside the scope you were given. In particular do not commit the user's
   pre-existing uncommitted changes (ProjectSettings/*, `Assets/Oculus/OculusProjectConfig.asset`,
   URP global settings, `OpenXR Package Settings.asset`, `Assets/_VFX/PowderBurst.vfx`,
   `IX_Scene/S2_v1/LightingData.asset`, `CompositeBody/.vsconfig`). Commit locally with an
   explicit pathspec only when the user allows it; push only when asked.
6. If several agent sessions share one editor: use the lock file
   `/c/Users/swsg9/AppData/Local/Temp/compositebody-editor.lock` (`set -C; echo "<name> $(date +%s)" > lock`,
   `rm` to release, stale after 20 min) around scene opens, play mode, builders, `menu`, and
   **recompiles** (a recompile during someone's play mode kills their Netcode session).
7. Before starting, ask the user the open decisions in §4. Do not guess them.

---

## 1. What exists

All under `Assets/_Interaction/`. Scripts compile into Assembly-CSharp (no asmdefs).

| Module | Prefab (drop into MainScene) | Demo scene | Root component | Debug id | Story task ids | Completion hook |
|---|---|---|---|---|---|---|
| Home | `Home/Prefabs/HomeInteraction.prefab` | `Home/S2_v1.unity` | `CompositeBody.Interaction.Home.HomeAssembly` (MonoBehaviour) | `home.assembly` | none yet | `onAllResolved`, `onCompleted` (UnityEvents; fire once per machine) |
| ObjectAssembly — Lamp | `ObjectAssembly/Prefabs/OA_Lamp.prefab` | `ObjectAssembly/OA_Lamp_Demo.unity` | `CompositeBody.Interaction.ObjectAssembly.OAController` (NetworkBehaviour) | `oa.lamp` | `oa.lamp.phase1` | `onCompleted` (public UnityEvent field) |
| ObjectAssembly — Camera | `ObjectAssembly/Prefabs/OA_Camera.prefab` | `ObjectAssembly/OA_Camera_Demo.unity` | `OAController` | `oa.camera` | `oa.camera.phase1` | `onCompleted` |

Shared core: `_Shared/` (`NetGrabbable` server-granted grab ownership, `BreathingView`,
`InteractionEasing`, `InteractionPhaseClock`, shader `CompositeBody/InteractionReveal`, test hands,
`InteractionBuildUtil`). API summary: `_Shared/README.md`.

Builders / verifiers / runners (menus):
- `Tools/Home/Build Home Interaction`, `Tools/Home/Verify Home Interaction`, `Tools/Home/Run Home Interaction Test`
- `Tools/ObjectAssembly/Lamp/{Build Demo, Verify Demo, Run Test}`
- `Tools/ObjectAssembly/Camera/{Build Demo, Verify Demo, Run Test}`

These build the **demo scenes** only. There is no MainScene mode yet — adding one is the
recommended integration (§5).

Prefab facts (verified): no prefab contains the demo bootstrap (no NetworkManager /
GameSessionManager / staff panel / rig). Prefab roots have no NetworkObject. Home's prefab holds
5 NetworkObjects (the fragments); each OA prefab holds 3 (controller + two halves). Every prefab
has `m_BeatRoot = None`. OA prefabs also contain 2 Lights and a PlayableDirector (Phase 0
timeline that brings those lights up).

Key serialized fields:
- `HomeAssembly`: `m_BeatRoot` (GameObject), `m_Pairs` (fragment ↔ `HomeRevealGroup`),
  `m_FinalGroup` (Top_Wall), `m_ZoneA`, `m_ZoneB`, `m_Timings` (`dwell` 1.5, `placeTravel` 0.6,
  `appearDuration` 1.2, `vanishDuration` 0.6, `fragmentDissolve` 1.5, `timeout` 180 s),
  `m_OnAllResolved`, `m_OnCompleted`, `m_DebugId`.
- `HomeRevealGroup`: `m_Target` (scene reference to a wall), `m_TargetPath` (fallback, resolved
  with `GameObject.Find`, which only finds ACTIVE objects), `m_FloatOffset`, `m_FloatDirection`.
- `OAController`: `m_BeatRoot`, `m_FragmentA/B`, `m_Trail`, `m_CornerP1/P2`, `m_Destination`,
  `m_Director`, `m_Phase1ClipP1`, `m_Phase1ClipP2`, `m_Phase1Source`, `m_Phase1FallbackSeconds`
  (2), `m_Phase1TaskId`, `m_Phase2Clip`, `m_Phase2Source`, `m_Phase2FallbackSeconds` (6),
  `m_JoinDistance` (0.12 m), `onCompleted`, `m_DebugId`.
- Phase 0 is PER ROLE, on two decks. `OA_<Variant>_Phase0.playable` carries two AudioTracks,
  `Phase0 VO A` and `Phase0 VO B`, bound to two AudioSources under `Controller` that each hold a
  `RoleAudible` gated to Player1 / Player2 — so 玩家A 跟玩家B 聽到不一樣的 VO from the beat's first
  sentence. Do NOT put one line on both decks expecting "both hear it": that is Phase 2's job.
  Author the decks through the `phase0ClipA` / `phase0ClipB` rows of `BuildObjectAssembly`'s variant
  table, not by hand, and run `Tools/ObjectAssembly/<Variant>/Upgrade Phase 0 Voices` — never
  `Build Demo` — on a prefab MainScene already instances, because the full build re-writes the
  prefab's file ids and would drop every per-beat override in the scene.
- Lamp phase 0 currently holds `S2_1_B.wav` on BOTH decks: a named placeholder, not the real pair.
  Camera phase 0 is empty on both. Phase 1 and Phase 2 have their real clips (lamp: `S2_2_A` /
  `S2_2_B` / `S2-2-AB`; camera: `S2-5-A` / `S2-5-B` / `S2-6-AB`), assigned as instance overrides
  in MainScene.
- The OAAppearMarker is on the shared clock, so the halves appear at ONE time for both players.
  With two lines of different lengths it belongs at the end of the longer one; the builder's verify
  warns when a deck outruns it.

Test status: Home runner 72/72, Lamp and Camera runners 45/45 each (single machine); two-player
Multiplayer Play Mode check passed for Home and Lamp (contested grab, client placement, ownership
hand-over, disconnect while holding, late rejoin, claim rules, link, join, Phase 2, destination).
Not yet tested on headsets / two PCs, and not in MainScene.

---

## 2. MainScene as it is (verified 2026-11-04)

Roots: `//System` (inactive), `Network Manager VR Multiplayer`, `XRI Network Game Manager`,
`Player_Notification_UI`, `GameSessionManager`, `StaffControlPanel`, `Debug View`,
`InteractionDebugPanel`, `//Scenes` (inactive), `Playspace`, `Experience`,
`ProbeVolumePerSceneData`, `CalibrationPanel`. The network/session stack is already there — do
NOT add another.

Relevant paths:
- `/Experience/ExperienceDirector/ShowTimeline` — `ShowTimelineDriver` (+ `Main.playable`). The
  show is a timeline of Control clips that switch `Beat_*` roots on and off.
- `/Experience/Beats/Beat_S2` (active in the file) → `/Experience/Beats/Beat_S2/Content/S2_v1`
  = prefab instance of `Assets/_Scenes/IX_Scene/S2_v1.prefab`, world pos (-61.60, -21.40, 181.20);
  `BakedLightmapCarrier` lives on it.
- `/Experience/Beats/Beat_S2/Content/S2_v1/--- Environment ---/S3` world pos (-0.06, -21.40, 100.00).
- `.../S3/Inner_Wall` is **inactive**. Its walls (`Back_Wall`, `Floor`, `Front_Wall`,
  `Left_Wall`, `Right_Wall`, `Top_Wall`) carry **scene overrides with the S2-3 dissolve
  materials**: `Melting_Wall_Dissolve`, `Atlas_Dissolve`, `Wooden Floor_wdipfjw_Dissolve`
  (written permanently by `BuildS2Dollhouse.BuildInnerWallDissolve`). Every wall renderer still has
  **BatchingStatic** on. Children per wall: Back 3, Front 8, Left 6, Right 5, Top 1, Floor 0.
- `.../S2_v1/--- Environment ---/LowPolyWall` — the art copy of the 5 paper pieces (Home brings
  its own networked copies).
- `/Experience/Beats/Beat_S2-1`, `Beat_S2-2`, `Beat_S2-3` — inactive beat roots switched by the timeline.
- `/Experience/Beats/S2-2 Fragments` (inactive, `CompositeFragmentPair`, the old lamp pair) and
  `/Experience/Beats/S2-3 Dollhouse` (inactive, `DollhouseAssembly`, children `LowPoly Dissolve`
  and `Inner Wall Dissolve` with `MemoryDissolve`). Commit 74abf222 deactivated both on purpose.
- `Assets/_Scenes/MainScene.unity` is build-settings scene 0; the demo scenes are also listed.

Timeline timing (**re-check**, from exploration): S2-1 starts 767 s (18 s long), S2-2 785 s (68 s),
S2-3 853 s (328 s). The only `ShowHoldMarker` gate today is `beat.O1_Control` at 301 s; the S2
beats are time-boxed. Read the real table with
`ShowTimelineDriver.Instance.TryGetScene("S2-3", out start, out end)` rather than hard-coding.

---

## 3. Invariants (break any of these and it fails in a way that is hard to see)

1. **Module roots under an always-active parent**, e.g. `/Experience/Beats/` next to the legacy
   `S2-2 Fragments` / `S2-3 Dollhouse`. NOT under `Beat_S2*` (switched off by the timeline) and NOT
   under `Playspace`. Netcode spawns in-scene NetworkObjects once, at sync, from what is active;
   anything under an inactive parent is never spawned.
2. **Arm via `m_BeatRoot`** = the beat's own root (`Beat_S2-3` for Home etc.). Left empty, the
   module arms as soon as the session goes InProgress (staff Start) — the wrong time in MainScene.
3. **No second network stack.** Never instantiate `InteractionBuildUtil.BuildDemoBootstrap` in MainScene.
4. **Regenerate NetworkObject hashes after the first save** —
   `InteractionBuildUtil.RegenerateNetworkObjectHashes(scene)`, mark dirty, save again, then
   `InteractionBuildUtil.VerifyHashes(tag, scene, failures)` (non-zero, unique). Never apply hash
   overrides back to a prefab asset.
5. **Unique ids** (`home.assembly`, `oa.lamp`, `oa.camera`, `oa.*.phase1`, any new gate task id).
6. **Keep fragment flags** set by `InteractionBuildUtil.ConfigureGrabbable` (check with
   `VerifyGrabbable`): DontDestroyWithOwner, no AutoObjectParentSync, `unparentTransformOnGrab=false`,
   `predictedVisualsTransform=null`, kinematic, no gravity.
7. **Do not touch shared material assets.** Home swaps per-renderer to builder-made twin assets
   (`Home/Materials/Reveal/*_Reveal.mat`) and swaps back; MeltingMemory slots are driven by MPB.

---

## 4. Decisions to get from the user before starting

1. Beat mapping. Recommended: **Home → S2-3** (replaces the dollhouse; same LowPolyWall + Inner_Wall
   content) and **OA Lamp → S2-2** (replaces the old lamp pair). **OA Camera has no slot**: a new
   beat (e.g. `Beat_S2-4` + a Control clip on `Main.playable`) or a different section — ask.
2. Legacy beats `S2-2 Fragments` / `S2-3 Dollhouse`: keep them inactive (recommended) or delete.
3. Completion: gate the timeline on the module (recommended, §5.4) or keep time-boxed clips.
4. ObjectAssembly lights: keep the prefab's own 2 lights + Phase 0 animation, or rebind the Phase 0
   Animation track to S2's scene lights.
5. Where in the S2 room the OA corners and `Destination` should be.
6. Real VO clips. Phase 1 and 2 are assigned. Phase 0 is per role and still needs its pairs: the
   lamp has `S2_1_B` standing in on both decks, the camera has nothing on either.

---

## 5. Procedure (implement as a re-runnable "MainScene" step in the builders)

Suggested shape: `Tools/Home/Integrate Into MainScene` and
`Tools/ObjectAssembly/<Variant>/Integrate Into MainScene`, each: precondition (MainScene is the
active scene and clean — `InteractionBuildUtil.RequireCleanActiveScene("Assets/_Scenes/MainScene.unity", tag, out scene)`),
idempotent rebuild of its own root only, save → hashes → save, then a MainScene `Verify()`.

### 5.1 Common
1. Find-or-instantiate the prefab under `/Experience/Beats/` (`PrefabUtility.InstantiatePrefab`,
   then `SetParent(beats, true)`). On re-run, replace the previous instance (match by prefab source).
2. Set `m_BeatRoot` on the root component (SerializedObject on the scene instance).
3. Save scene → `RegenerateNetworkObjectHashes(scene)` → save again → `VerifyHashes`.

### 5.2 Home (→ `Beat_S2-3`)
1. **Pose**: set the `HomeInteraction` root's world pose to the world pose of
   `/Experience/Beats/Beat_S2/Content/S2_v1/--- Environment ---/S3` (the demo was authored with
   S3 at identity). Validate: every Home slot (`HomeFragment.slot`) matches the world pose of the
   corresponding child of MainScene's `.../S2_v1/--- Environment ---/LowPolyWall` within 0.5 mm.
   If not, fix the pose; never move the art.
2. **Inner_Wall active**: override `Inner_Wall.activeSelf = true` on the prefab instance. Home
   hides each element (`forceRenderingOff`) until its fragment lands, so the room is still
   invisible before/after its moment.
3. **Restore the original wall materials**: revert the material overrides on every MeshRenderer
   under `Inner_Wall` to the `S2_v1.prefab` values (e.g. `PrefabUtility.RevertObjectOverride(renderer, InteractionMode.AutomatedAction)`
   or revert the `m_Materials` property). Verify no `*_Dissolve` material remains. Do not edit the
   `*_Dissolve` assets. (Without this Home records the lossy dissolve materials as "originals".)
4. **Retarget the six groups**: for each `HomeRevealGroup` set `m_Target` to the MainScene wall
   (`.../S3/Inner_Wall/<Back_Wall|Floor|Front_Wall|Left_Wall|Right_Wall|Top_Wall>`) and
   `m_TargetPath` to that full path. Then re-run whatever preparation `BuildHomeInteraction` does
   for a group (element list wall-first then children, original/twin material arrays, float-out
   offsets) against the MainScene walls — reuse its code, don't duplicate it. Create any missing
   twin with `InteractionBuildUtil.MakeRevealTwin(src, "Assets/_Interaction/Home/Materials/Reveal")`
   (expected: none, the sources are the same originals as the demo).
5. **Static flags**: `InteractionBuildUtil.ClearBatchingStatic(innerWall)` (keeps ContributeGI);
   verify `AnyBatchingStatic(innerWall) == false`.
6. **Art copy off**: override `.../S2_v1/--- Environment ---/LowPolyWall` to inactive (Home's
   networked fragments replace it; it stays as the reference for 5.2.1).
7. Lighting: lightmaps travel with each renderer via `BakedLightmapCarrier`; no re-bake needed. If
   someone re-bakes S2, re-run the carrier's capture.
8. Timing: Home auto-completes at `m_Timings.timeout` (180 s) after arming; S2-3 is ~328 s
   (**re-check**). Either keep it inside the clip or gate (§5.4).

### 5.3 ObjectAssembly Lamp (→ `Beat_S2-2`), Camera (→ the beat the user picks)
1. Pose the OA root in the S2 room where the two players stand during that beat (use the legacy
   `S2-2 Fragments` halves' world positions and the S2 player spawns as reference). Corners
   (`m_CornerP1/P2`) and `m_Destination` are children of the prefab — move them as the user decided.
2. Lights: either keep the prefab's lights or rebind the Phase 0 timeline's Animation track to S2
   lights on the instance's PlayableDirector (decision §4.4).
3. Assign VO: `m_Phase1ClipP1`, `m_Phase1ClipP2`, `m_Phase2Clip` on the instance, and phase 0's two
   role-gated decks through the variant table's `phase0ClipA` / `phase0ClipB` (they live in the
   .playable, which is per variant, so they are NOT instance overrides).
4. Camera needs its own beat root first (create `Beat_S2-4` and a Control clip on `Main.playable`
   only after the user agrees — that edits the show).

### 5.4 Completion → show flow (recommended)
There is no generic "report a story task from a UnityEvent" component yet. Add one in `_Shared`
(e.g. `InteractionTaskReporter : MonoBehaviour` with `[SerializeField] string m_TaskId` and
`public void Report() => StoryProgressManager.Instance?.ReportTaskCompleteRpc(m_TaskId)`), wire each
module's `onCompleted` to it (tasks e.g. `home.done`, `oa.lamp.done`, `oa.camera.done`), register the
gate row with `InteractionDebug.RegisterTaskGate(taskId, label)`, and put a `ShowHoldMarker`
(`m_TaskId`) at the end of the beat's clip on `Main.playable`. `onCompleted` fires on every machine,
so each player reports its own role and `StoryProgressManager` completes the task when both have
(solo runs are handled by `IsTaskComplete`).

---

## 6. Verification

1. `unity recompile` clean.
2. The integrate steps' `Verify()` all `PASS`, covering at least: one instance of each prefab under
   `/Experience/Beats/`; `m_BeatRoot` set; NetworkObject hashes non-zero and unique; `VerifyGrabbable`
   on every fragment; Home slots ↔ LowPolyWall art within 0.5 mm; six groups resolved to MainScene
   walls; no `*_Dissolve` on Inner_Wall; no BatchingStatic under Inner_Wall; Inner_Wall active;
   unique debug/task ids; legacy beats still inactive; exactly one NetworkManager in the scene.
3. Regression: the demo builders' Verify and the three runners still PASS in their demo scenes.
4. Play MainScene (host solo is allowed from the staff panel). Jump to a beat on the server with
   `ShowTimelineDriver.Instance.TryGetScene("S2-3", out var s, out var e); ShowTimelineDriver.Instance.ScrubTo(s);`
   and check: module arms only when its beat root is active, plays, completes, and (if gated)
   the show resumes after the task completes.
5. Two players in Multiplayer Play Mode — see §7 for the traps. Then two PCs + headsets on the LAN.

---

## 7. Known pitfalls (learned the hard way)

- **MPPM identity clash**: MPPM players share PlayerPrefs, so both send the same identity guid
  (`PlayerIdentity`, key `compositebody_player_guid`) and the joiner takes over Player1. For tests,
  set a distinct payload on Player 2 before joining:
  `NetworkManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes("mppm-player2-<id>")`.
  A reconnect must reuse the same payload or it is refused ("Session already has two players").
- **Solo-start race**: pressing Start before the second player's role entry exists starts the
  session solo. Wait until `GameSessionManager.playerRoles` shows both roles connected.
- **Pipeline ports move** after a domain reload (7800 → 7801 …). Target editors with `--pid`, not `--port`.
- **Recompile during play mode** kills Netcode and can leave UDP 7777 bound by the editor until it
  restarts. The editor preference `ScriptCompilationDuringPlay` is set to 1 (recompile after play)
  on this machine; still take the lock before recompiling.
- **NGO "Add Scene to Scenes in Build" modal** blocks play mode for any scene with a
  scene-management NetworkManager that is not in Build Settings. MainScene is listed.
- **Home re-fires `onCompleted` on a machine that disconnects and rejoins** (its local count
  becomes 2). Make anything wired to it idempotent, or add a guard in `HomeAssembly`.
- **Playing `Home/S2_v1.unity` marks it dirty** (rig UI, water bounds). Reload from disk, don't save.
- **Paper meshes are flat**: Home fragments use builder-made box colliders (≥ 4 cm thick) in
  `Home/Meshes/*_Collider.asset`.
- The `HTTP/1.1 403 Forbidden` from `VivoxApiClient` and the KriptoFX `KWS_UpdateManager`
  NullReference are unrelated to these modules.
- MainScene-wide edits (timeline, build settings, project settings) are shared with the team — get
  explicit approval and keep them in their own commits.
